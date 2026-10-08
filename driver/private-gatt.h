#pragma once
#include <windows.h>
#ifdef __cplusplus
extern "C" {
#endif
typedef struct _STADIA_GATT_DIAGNOSTICS {
    HRESULT OpenStatus;
    HRESULT WriteStatus;
    ULONG WriteCount;
    ULONG StopCount;
    USHORT ValueHandle;
    UCHAR ReportId;
    UCHAR ReportType;
} STADIA_GATT_DIAGNOSTICS;
HRESULT StadiaPrivateGattOpen(UINT64 Address, void **Session, STADIA_GATT_DIAGNOSTICS *Diagnostics);
HRESULT StadiaPrivateGattWrite(void *Session, const UCHAR *Report, size_t Length, STADIA_GATT_DIAGNOSTICS *Diagnostics);
void StadiaPrivateGattClose(void *Session);
#ifdef __cplusplus
}
#endif
