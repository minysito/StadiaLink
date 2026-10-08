// Stadia protocol: translates the controller's input reports to Xbox controls
// and rumble to its output report. The transports (usb.c, bluetooth.c) carry
// the reports.

#include "winstadia.h"

// Stadia input report layout:
//   [0] report ID (0x03)
//   [1] d-pad hat: 0 = up, clockwise to 7 = up-left, 8 = released
//   [2] RS click, Options, Menu, Stadia, R2, L2, Assistant, Capture (bit 7..0)
//   [3] -, A, B, X, Y, L1, R1, LS click (bit 7..0)
//   [4..8] left X, left Y, right X, right Y: centered at 128, Y grows downwards
//   [8..10] L2, R2 analog
#define STADIA_INPUT_REPORT_ID 0x03
#define STADIA_INPUT_REPORT_LEN 10
#define STADIA_RUMBLE_REPORT_ID 0x05
#define STADIA_HAT_RELEASED 8

#define STADIA_B2_RS 0x80
#define STADIA_B2_OPTIONS 0x40
#define STADIA_B2_MENU 0x20
#define STADIA_B2_STADIA 0x10

#define STADIA_B3_A 0x40
#define STADIA_B3_B 0x20
#define STADIA_B3_X 0x10
#define STADIA_B3_Y 0x08
#define STADIA_B3_L1 0x04
#define STADIA_B3_R1 0x02
#define STADIA_B3_LS 0x01

// Xbox controls, as laid out in PAD_CONTROLS_LEN:
//   [0..8] left X, left Y, right X, right Y: 16 bit little endian, centered
//          at 0x8000, Y grows downwards
//   [8..12] left, right trigger: 10 bit little endian
//   [12] d-pad hat: 1 = up, clockwise to 8 = up-left, 0 = released
//   [13] Menu, View, RB, LB, Y, X, B, A (bit 7..0)
//   [14] RS click, LS click (bit 1..0)
//   [15] Xbox button
#define XBOX_B13_MENU 0x80
#define XBOX_B13_VIEW 0x40
#define XBOX_B13_RB 0x20
#define XBOX_B13_LB 0x10
#define XBOX_B13_Y 0x08
#define XBOX_B13_X 0x04
#define XBOX_B13_B 0x02
#define XBOX_B13_A 0x01

#define XBOX_B14_RS 0x02
#define XBOX_B14_LS 0x01

#define BIT(source, mask, target) (((source) & (mask)) ? (target) : 0)

VOID
StadiaRecordError(PDEVICE_CONTEXT Context, NTSTATUS Status)
{
    AcquireSRWLockExclusive(&Context->Lock);
    Context->LastError = Status;
    ReleaseSRWLockExclusive(&Context->Lock);
}

VOID
StadiaInputReport(PDEVICE_CONTEXT Context, const UCHAR *Report, size_t Length)
{
    UCHAR controls[PAD_CONTROLS_LEN];

    AcquireSRWLockExclusive(&Context->Lock);
    Context->RawReportLength = (UCHAR)min(Length, RAW_REPORT_MAX);
    RtlCopyMemory(Context->RawReport, Report, Context->RawReportLength);
    ReleaseSRWLockExclusive(&Context->Lock);

    if (Length >= STADIA_INPUT_REPORT_LEN && Report[0] == STADIA_INPUT_REPORT_ID) {
        STADIA_CONFIG configuration;
        AcquireSRWLockShared(&Context->Lock);
        configuration = Context->Configuration;
        ReleaseSRWLockShared(&Context->Lock);
        StadiaTranslate(Report, &configuration, controls);
        PadPublishControls(Context, controls);
    }
}

static VOID
SetRumble(PDEVICE_CONTEXT Context, UCHAR Strong, UCHAR Weak,BOOLEAN Scale)
{
    // Motor speeds are 16 bit little endian; x * 257 scales 8 to 16 bits.
    UCHAR report[] = {STADIA_RUMBLE_REPORT_ID, Strong, Strong, Weak, Weak};
    NTSTATUS status;
    if(Scale) {
    AcquireSRWLockShared(&Context->Lock);
    Strong = (UCHAR)(Strong * Context->Configuration.StrongScale / 100);
    Weak = (UCHAR)(Weak * Context->Configuration.WeakScale / 100);
    ReleaseSRWLockShared(&Context->Lock);
    }
    report[1] = report[2] = Strong; report[3] = report[4] = Weak;

    // Games repeat the same output report a lot; only changes reach the wire.
    AcquireSRWLockExclusive(&Context->RumbleLock);
    if (Strong == Context->RumbleStrong && Weak == Context->RumbleWeak) {
        ReleaseSRWLockExclusive(&Context->RumbleLock);
        return;
    }
    status = Context->Transport->SendOutputReport(Context, report, sizeof(report));
    if (NT_SUCCESS(status)) {
        Context->RumbleStrong = Strong;
        Context->RumbleWeak = Weak;
    }
    ReleaseSRWLockExclusive(&Context->RumbleLock);

    AcquireSRWLockExclusive(&Context->Lock);
    Context->RumbleFailed = !NT_SUCCESS(status);
    if (!NT_SUCCESS(status)) {
        Context->LastError = status;
    }
    ReleaseSRWLockExclusive(&Context->Lock);
}

VOID StadiaSetRumble(PDEVICE_CONTEXT Context,UCHAR Strong,UCHAR Weak){SetRumble(Context,Strong,Weak,TRUE);}
VOID StadiaTestRumble(PDEVICE_CONTEXT Context,UCHAR Strong,UCHAR Weak){SetRumble(Context,Strong,Weak,FALSE);}

NTSTATUS
StadiaPrepareHardware(WDFDEVICE Device, WDFCMRESLIST ResourcesRaw, WDFCMRESLIST ResourcesTranslated)
{
    UNREFERENCED_PARAMETER(ResourcesRaw);
    UNREFERENCED_PARAMETER(ResourcesTranslated);

    StadiaLoadConfiguration(Device);
    return GetDeviceContext(Device)->Transport->PrepareHardware(Device);
}

NTSTATUS
StadiaD0Entry(WDFDEVICE Device, WDF_POWER_DEVICE_STATE PreviousState)
{
    PDEVICE_CONTEXT context = GetDeviceContext(Device);

    UNREFERENCED_PARAMETER(PreviousState);

    // The motors are off after power up, whatever was requested before.
    AcquireSRWLockExclusive(&context->RumbleLock);
    context->RumbleStrong = 0;
    context->RumbleWeak = 0;
    ReleaseSRWLockExclusive(&context->RumbleLock);

    return context->Transport->Start(context);
}

NTSTATUS
StadiaD0Exit(WDFDEVICE Device, WDF_POWER_DEVICE_STATE TargetState)
{
    PDEVICE_CONTEXT context = GetDeviceContext(Device);

    UNREFERENCED_PARAMETER(TargetState);

    // Fails harmlessly when the controller is already gone.
    WdfTimerStop(context->TestRumbleTimer, TRUE);
    StadiaSetRumble(context, 0, 0);
    context->Transport->Stop(context);
    return STATUS_SUCCESS;
}
