// Bluetooth transport: the inbox HID over GATT driver stays in the stack and
// this driver sits on top of it, in the same driver host. It asks the driver
// below for Stadia input reports the way the HID class driver would, and hands
// it Stadia output reports.

#include "winstadia.h"
#include <hidport.h>

#define WRITE_TIMEOUT_MS 2000
#define RETRY_DELAY_MS 500
// Room for any input report of the controller.
#define READ_BUFFER_LEN 64

EVT_WDF_REQUEST_COMPLETION_ROUTINE EvtBluetoothReadComplete;
EVT_WDF_TIMER EvtBluetoothRetryTimer;

static VOID
ReadFailed(PBLUETOOTH_READER Reader, NTSTATUS Status)
{
    PDEVICE_CONTEXT context = Reader->Context;
    BOOLEAN retry;

    AcquireSRWLockExclusive(&context->Lock);
    Reader->Sent = FALSE;
    // Stopping cancels the reads, that is no error.
    if (Status != STATUS_CANCELLED) {
        context->LastError = Status;
    }
    retry = context->Reading;
    ReleaseSRWLockExclusive(&context->Lock);

    if (retry) {
        WdfTimerStart(context->RetryTimer, WDF_REL_TIMEOUT_IN_MS(RETRY_DELAY_MS));
    }
}

// Sends the reader's request down, unless it is there already or the
// transport is stopped.
static VOID
SendRead(PBLUETOOTH_READER Reader)
{
    PDEVICE_CONTEXT context = Reader->Context;
    WDF_REQUEST_REUSE_PARAMS reuse;
    BOOLEAN send;
    NTSTATUS status;

    AcquireSRWLockExclusive(&context->Lock);
    send = context->Reading && !Reader->Sent;
    if (send) {
        Reader->Sent = TRUE;
    }
    ReleaseSRWLockExclusive(&context->Lock);
    if (!send) {
        return;
    }

    WDF_REQUEST_REUSE_PARAMS_INIT(&reuse, WDF_REQUEST_REUSE_NO_FLAGS, STATUS_SUCCESS);
    WdfRequestReuse(Reader->Request, &reuse);
    status = WdfIoTargetFormatRequestForIoctl(context->LowerDriver, Reader->Request, IOCTL_HID_READ_REPORT, NULL,
                                              NULL, Reader->Buffer, NULL);
    if (NT_SUCCESS(status)) {
        WdfRequestSetCompletionRoutine(Reader->Request, EvtBluetoothReadComplete, Reader);
        if (WdfRequestSend(Reader->Request, context->LowerDriver, WDF_NO_SEND_OPTIONS)) {
            return;
        }
        status = WdfRequestGetStatus(Reader->Request);
    }
    ReadFailed(Reader, status);
}

VOID
EvtBluetoothReadComplete(WDFREQUEST Request, WDFIOTARGET Target, PWDF_REQUEST_COMPLETION_PARAMS Params,
                         WDFCONTEXT Context)
{
    PBLUETOOTH_READER reader = Context;
    PDEVICE_CONTEXT context = reader->Context;
    size_t capacity;
    const UCHAR *report = WdfMemoryGetBuffer(reader->Buffer, &capacity);

    UNREFERENCED_PARAMETER(Request);
    UNREFERENCED_PARAMETER(Target);

    if (!NT_SUCCESS(Params->IoStatus.Status)) {
        ReadFailed(reader, Params->IoStatus.Status);
        return;
    }
    if (Params->IoStatus.Information == 0) {
        // Reading on right away could spin if the driver below keeps doing this.
        ReadFailed(reader, STATUS_NO_DATA_DETECTED);
        return;
    }
    StadiaInputReport(context, report, min(Params->IoStatus.Information, capacity));

    AcquireSRWLockExclusive(&context->Lock);
    reader->Sent = FALSE;
    ReleaseSRWLockExclusive(&context->Lock);
    SendRead(reader);
}

VOID
EvtBluetoothRetryTimer(WDFTIMER Timer)
{
    PDEVICE_CONTEXT context = GetDeviceContext(WdfTimerGetParentObject(Timer));

    for (int i = 0; i < BLUETOOTH_READER_COUNT; i++) {
        SendRead(&context->Readers[i]);
    }
}

// Rumble does not work this way yet. The driver below fails the write with
// 0xC0070057 (invalid parameter), as it does for HidD_SetOutputReport on the
// plain inbox stack. The likely reason is in the controller's GATT table: the
// output report's characteristic (0x2A4D, value handle 0x0044) is declared
// with Read and Write only, where the HID over GATT profile demands Write
// Without Response as well. Protocol Mode and the control point lack it too.
//
// Writing the characteristic with the Bluetooth GATT API
// (BluetoothGATTSetCharacteristicValue) was tried and is closed off:
// - The API needs a handle of the service. All it does with the handle is
//   fetch 16 bytes that stand for that open (IOCTL 0x00411490); reads and
//   writes then go to bthserv over RPC. The handle of the whole device lists
//   the table but fails every read and write with ERROR_INVALID_FUNCTION.
// - The service's device interface cannot be opened: the create ends at the
//   HID class driver on top of this stack (error 31).
// - From inside the driver host, WdfIoTargetOpen by file either fails with
//   access denied, or, with UmdfDispatcher=FileHandle, succeeds without
//   giving a handle, and the IOCTL above then fails with STATUS_FILE_CLOSED.
//   A filter as lowest driver of the stack gets the same. BthLEEnum seems to
//   refuse user mode opens of protected services, as it does for 0x1800 and
//   0x1801 (error 5). The reflector's control devices (\\.\UMDFCtrlDev-*)
//   refuse a CreateFile from the host as well.
// The public WinRT API (Windows.Devices.Bluetooth.GenericAttributeProfile,
// what bleak and Web Bluetooth use) is closed off as well: GetGattServices
// lists 0x1812, but RequestAccess on it returns DeniedBySystem and
// GetCharacteristics returns AccessDenied, offline and connected alike,
// while the other services are allowed. Reports of a plain GATT write
// making the motors run come from macOS and Linux, whose stacks let user
// mode at the HID service.
// What is left is what the driver below uses itself: the WinRT class
// Microsoft.Bluetooth.Profiles.Gatt.Interface.GattClientDevice, served by
// bthserv, with GattClientCharacteristic and GattClientWriteResult next to
// it. It is private, without metadata, so its interfaces would have to be
// recovered from the inbox driver.
static NTSTATUS
BluetoothSendOutputReport(PDEVICE_CONTEXT Context, PUCHAR Report, size_t Length)
{
    // Serialized by the caller's RumbleLock; input remains on the inbox path.
    STADIA_GATT_DIAGNOSTICS diagnostics;
    HRESULT hr;
    if (!Context->PrivateGattSession) {
        return STATUS_DEVICE_NOT_READY;
    }
    hr = StadiaPrivateGattWrite(Context->PrivateGattSession, Report, Length, &diagnostics);
    AcquireSRWLockExclusive(&Context->Lock);
    Context->PrivateGattDiagnostics = diagnostics;
    ReleaseSRWLockExclusive(&Context->Lock);
    // Diagnostic write status is an HRESULT; its sign also signals failure here.
    return (NTSTATUS)hr;
}

static NTSTATUS
GetBluetoothAddress(WDFDEVICE Device, UINT64 *Address)
{
    // Query this devnode's identity, rather than a fixed controller or GATT handle.
    static const DEVPROPKEY instanceIdKey = {
        {0x78c34fc8, 0x104a, 0x4aca, {0x9e, 0xa4, 0x52, 0x4d, 0x52, 0x99, 0x6e, 0x57}}, 256
    };
    WDF_DEVICE_PROPERTY_DATA property;
    WDF_OBJECT_ATTRIBUTES attributes;
    WDFMEMORY memory;
    DEVPROPTYPE type;
    NTSTATUS status;
    size_t length;
    WCHAR *id;
    WCHAR *address;
    WCHAR *end;
    WDF_DEVICE_PROPERTY_DATA_INIT(&property, &instanceIdKey);
    WDF_OBJECT_ATTRIBUTES_INIT(&attributes);
    attributes.ParentObject = Device;
    status = WdfDeviceAllocAndQueryPropertyEx(Device, &property, NonPagedPoolNx, &attributes, &memory, &type);
    if (!NT_SUCCESS(status)) return status;
    id = WdfMemoryGetBuffer(memory, &length);
    if (type != DEVPROP_TYPE_STRING || length < sizeof(WCHAR) || id[length / sizeof(WCHAR) - 1] != L'\0') {
        WdfObjectDelete(memory);
        return STATUS_INVALID_PARAMETER;
    }
    address = wcsrchr(id, L'_');
    if (!address || wcslen(address + 1) < 13 || address[13] != L'\\') {
        WdfObjectDelete(memory);
        return STATUS_INVALID_PARAMETER;
    }
    for (int i = 1; i <= 12; i++) {
        WCHAR c = address[i];
        if (!((c >= L'0' && c <= L'9') || (c >= L'A' && c <= L'F') || (c >= L'a' && c <= L'f'))) {
            WdfObjectDelete(memory);
            return STATUS_INVALID_PARAMETER;
        }
    }
    *Address = _wcstoui64(address + 1, &end, 16);
    status = *end == L'\\' && *Address != 0 ? STATUS_SUCCESS : STATUS_INVALID_PARAMETER;
    WdfObjectDelete(memory);
    return status;
}

static NTSTATUS
BluetoothPrepareHardware(WDFDEVICE Device)
{
    PDEVICE_CONTEXT context = GetDeviceContext(Device);
    WDF_OBJECT_ATTRIBUTES attributes;
    WDF_TIMER_CONFIG timerConfig;
    NTSTATUS status;

    // Survives a stop and restart of the device, so set it up only once.
    if (context->LowerDriver != NULL) {
        return STATUS_SUCCESS;
    }

    WDF_OBJECT_ATTRIBUTES_INIT(&attributes);
    attributes.ParentObject = Device;
    for (int i = 0; i < BLUETOOTH_READER_COUNT; i++) {
        PBLUETOOTH_READER reader = &context->Readers[i];

        reader->Context = context;
        status = WdfRequestCreate(&attributes, WdfDeviceGetIoTarget(Device), &reader->Request);
        if (!NT_SUCCESS(status)) {
            return status;
        }
        status = WdfMemoryCreate(&attributes, NonPagedPoolNx, 0, READ_BUFFER_LEN, &reader->Buffer, NULL);
        if (!NT_SUCCESS(status)) {
            return status;
        }
    }

    WDF_TIMER_CONFIG_INIT(&timerConfig, EvtBluetoothRetryTimer);
    timerConfig.AutomaticSerialization = FALSE;
    status = WdfTimerCreate(&timerConfig, &attributes, &context->RetryTimer);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    context->LowerDriver = WdfDeviceGetIoTarget(Device);
    status = GetBluetoothAddress(Device, &context->BluetoothAddress);
    if (!NT_SUCCESS(status)) {
        context->PrivateGattProbeStatus = HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
    }
    return STATUS_SUCCESS;
}

static NTSTATUS
BluetoothStart(PDEVICE_CONTEXT Context)
{
    NTSTATUS status = WdfIoTargetStart(Context->LowerDriver);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    AcquireSRWLockExclusive(&Context->RumbleLock);
    StadiaPrivateGattClose(Context->PrivateGattSession);
    Context->PrivateGattSession = NULL;
    STADIA_GATT_DIAGNOSTICS diagnostics = {0};
    HRESULT hr = StadiaPrivateGattOpen(Context->BluetoothAddress, &Context->PrivateGattSession, &diagnostics);
    AcquireSRWLockExclusive(&Context->Lock);
    Context->PrivateGattDiagnostics = diagnostics;
    Context->PrivateGattProbeStatus = hr;
    Context->PrivateGattProbeRan = TRUE;
    ReleaseSRWLockExclusive(&Context->Lock);
    ReleaseSRWLockExclusive(&Context->RumbleLock);

    AcquireSRWLockExclusive(&Context->Lock);
    Context->Reading = TRUE;
    ReleaseSRWLockExclusive(&Context->Lock);
    for (int i = 0; i < BLUETOOTH_READER_COUNT; i++) {
        SendRead(&Context->Readers[i]);
    }
    return STATUS_SUCCESS;
}

static VOID
BluetoothStop(PDEVICE_CONTEXT Context)
{
    AcquireSRWLockExclusive(&Context->RumbleLock);
    StadiaPrivateGattClose(Context->PrivateGattSession);
    Context->PrivateGattSession = NULL;
    ReleaseSRWLockExclusive(&Context->RumbleLock);
    AcquireSRWLockExclusive(&Context->Lock);
    Context->Reading = FALSE;
    ReleaseSRWLockExclusive(&Context->Lock);

    // A retry that slips in after this finds Reading cleared and does nothing.
    WdfTimerStop(Context->RetryTimer, TRUE);
    WdfIoTargetStop(Context->LowerDriver, WdfIoTargetCancelSentIo);
}

const TRANSPORT BluetoothTransport = {
    BluetoothPrepareHardware,
    BluetoothStart,
    BluetoothStop,
    BluetoothSendOutputReport,
};
