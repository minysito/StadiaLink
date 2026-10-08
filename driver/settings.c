#include "winstadia.h"

static const UNICODE_STRING ConfigurationName={sizeof(L"StudioConfigurationV1")-sizeof(WCHAR),sizeof(L"StudioConfigurationV1"),L"StudioConfigurationV1"};

VOID StadiaLoadConfiguration(WDFDEVICE Device) {
    PDEVICE_CONTEXT context=GetDeviceContext(Device);
    WDFKEY key;STADIA_CONFIG config;ULONG length=0,type=0;
    NTSTATUS status=WdfDeviceOpenRegistryKey(Device,
        PLUGPLAY_REGKEY_DEVICE | WDF_REGKEY_DEVICE_SUBKEY,KEY_READ,
        WDF_NO_OBJECT_ATTRIBUTES,&key);
    if(!NT_SUCCESS(status)) return;
    status=WdfRegistryQueryValue(key,&ConfigurationName,sizeof(config),&config,&length,&type);
    WdfRegistryClose(key);
    if(NT_SUCCESS(status) && length==sizeof(config) && type==REG_BINARY && StadiaConfigValid(&config)) {
        AcquireSRWLockExclusive(&context->Lock);context->Configuration=config;
        ReleaseSRWLockExclusive(&context->Lock);
    }
}

NTSTATUS StadiaApplyConfiguration(WDFDEVICE Device,const STADIA_CONFIG *Configuration) {
    PDEVICE_CONTEXT context=GetDeviceContext(Device);WDFKEY key;
    NTSTATUS status;UCHAR raw[RAW_REPORT_MAX];size_t length;
    if(!StadiaConfigValid(Configuration)) return STATUS_INVALID_PARAMETER;
    // Persist before publishing, so a successful response means both operations
    // succeeded. Profiles continue working after the desktop app is closed.
    AcquireSRWLockExclusive(&context->ConfigurationLock);
    status=WdfDeviceOpenRegistryKey(Device,
        PLUGPLAY_REGKEY_DEVICE | WDF_REGKEY_DEVICE_SUBKEY,KEY_READ | KEY_SET_VALUE,
        WDF_NO_OBJECT_ATTRIBUTES,&key);
    if(NT_SUCCESS(status)) {
        status=WdfRegistryAssignValue(key,&ConfigurationName,REG_BINARY,sizeof(*Configuration),(PVOID)Configuration);
        WdfRegistryClose(key);
    }
    AcquireSRWLockExclusive(&context->Lock);
    context->ConfigurationPersistenceStatus=status;
    if(NT_SUCCESS(status)) context->Configuration=*Configuration;
    length=context->RawReportLength;RtlCopyMemory(raw,context->RawReport,length);
    ReleaseSRWLockExclusive(&context->Lock);
    // Publish held controls immediately with their new mapping, including releases.
    if(NT_SUCCESS(status) && length>=10) StadiaInputReport(context,raw,length);
    ReleaseSRWLockExclusive(&context->ConfigurationLock);
    return status;
}
