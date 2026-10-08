// Read-only access to the standard BLE Battery Service, never the HID service.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <setupapi.h>
#include <bthledef.h>
#include <bluetoothleapis.h>
#include <vector>
#include <string>
#include <algorithm>
#include <stdio.h>
#include <winusb.h>

static HRESULT ReadUsbBattery(int* level) {
    *level=-1;const GUID guid={0x8d3e4437,0x0f1f,0x483b,{0xb5,0x2e,0xc7,0x30,0x6f,0x0c,0x89,0xc0}};
    HDEVINFO list=SetupDiGetClassDevsW(&guid,nullptr,nullptr,DIGCF_PRESENT|DIGCF_DEVICEINTERFACE);
    if(list==INVALID_HANDLE_VALUE)return HRESULT_FROM_WIN32(GetLastError());
    HRESULT result=HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
    for(DWORD i=0;;i++) {
        SP_DEVICE_INTERFACE_DATA data={};data.cbSize=sizeof(data);
        if(!SetupDiEnumDeviceInterfaces(list,nullptr,&guid,i,&data))break;
        DWORD size=0;SetupDiGetDeviceInterfaceDetailW(list,&data,nullptr,0,&size,nullptr);
        if(!size||size>65536)continue;std::vector<BYTE> buffer(size);
        auto detail=reinterpret_cast<SP_DEVICE_INTERFACE_DETAIL_DATA_W*>(buffer.data());detail->cbSize=sizeof(*detail);
        if(!SetupDiGetDeviceInterfaceDetailW(list,&data,detail,size,nullptr,nullptr))continue;
        std::wstring path(detail->DevicePath);std::transform(path.begin(),path.end(),path.begin(),[](wchar_t c){return (wchar_t)towlower(c);});
        if(path.find(L"vid_18d1&pid_9400&mi_00")==std::wstring::npos)continue;
        HANDLE device=CreateFileW(detail->DevicePath,GENERIC_READ|GENERIC_WRITE,FILE_SHARE_READ|FILE_SHARE_WRITE,nullptr,OPEN_EXISTING,FILE_FLAG_OVERLAPPED,nullptr);
        if(device==INVALID_HANDLE_VALUE){result=HRESULT_FROM_WIN32(GetLastError());continue;}
        WINUSB_INTERFACE_HANDLE usb=nullptr;
        if(WinUsb_Initialize(device,&usb)) {
            ULONG timeout=1000;WinUsb_SetPipePolicy(usb,0,PIPE_TRANSFER_TIMEOUT,sizeof(timeout),&timeout);
            WINUSB_SETUP_PACKET setup={};setup.RequestType=0x21;setup.Request=0x83;ULONG actual=0;
            if(WinUsb_ControlTransfer(usb,setup,nullptr,0,&actual,nullptr)) {
                Sleep(100);BYTE bytes[64]={};setup.RequestType=0xa1;setup.Request=0x84;setup.Length=sizeof(bytes);
                if(WinUsb_ControlTransfer(usb,setup,bytes,sizeof(bytes),&actual,nullptr)&&actual==2&&bytes[1]==0&&bytes[0]<=100){*level=bytes[0];result=S_OK;}
                else result=HRESULT_FROM_WIN32(GetLastError()?GetLastError():ERROR_INVALID_DATA);
            }else result=HRESULT_FROM_WIN32(GetLastError());
            WinUsb_Free(usb);
        }else result=HRESULT_FROM_WIN32(GetLastError());
        CloseHandle(device);if(SUCCEEDED(result))break;
    }
    SetupDiDestroyDeviceInfoList(list);return result;
}

static HRESULT ReadBattery(UINT64 address,int* level) {
    *level=-1;if(!address)return ReadUsbBattery(level);
    const GUID serviceGuid={0x6e3bb679,0x4372,0x40c8,{0x9e,0xaa,0x45,0x09,0xdf,0x26,0x0c,0xd8}};
    HDEVINFO list=SetupDiGetClassDevsW(&serviceGuid,nullptr,nullptr,DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
    if(list==INVALID_HANDLE_VALUE)return HRESULT_FROM_WIN32(GetLastError());
    wchar_t mac[20];swprintf_s(mac,L"%012llx",address);HRESULT answer=HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
    for(DWORD i=0;;i++) {
        SP_DEVICE_INTERFACE_DATA data={};data.cbSize=sizeof(data);
        if(!SetupDiEnumDeviceInterfaces(list,nullptr,&serviceGuid,i,&data))break;
        DWORD size=0;SetupDiGetDeviceInterfaceDetailW(list,&data,nullptr,0,&size,nullptr);
        if(!size || size>65536)continue;std::vector<BYTE> bytes(size);
        auto detail=reinterpret_cast<SP_DEVICE_INTERFACE_DETAIL_DATA_W*>(bytes.data());detail->cbSize=sizeof(*detail);
        if(!SetupDiGetDeviceInterfaceDetailW(list,&data,detail,size,nullptr,nullptr))continue;
        std::wstring path(detail->DevicePath);std::transform(path.begin(),path.end(),path.begin(),[](wchar_t c){return (wchar_t)towlower(c);});
        if(path.find(L"0000180f")==std::wstring::npos || path.find(mac)==std::wstring::npos || path.find(L"9400")==std::wstring::npos)continue;
        HANDLE device=CreateFileW(detail->DevicePath,GENERIC_READ,FILE_SHARE_READ | FILE_SHARE_WRITE,nullptr,OPEN_EXISTING,0,nullptr);
        if(device==INVALID_HANDLE_VALUE){answer=HRESULT_FROM_WIN32(GetLastError());continue;}
        USHORT count=0;HRESULT hr=BluetoothGATTGetCharacteristics(device,nullptr,0,nullptr,&count,BLUETOOTH_GATT_FLAG_NONE);
        if(hr==HRESULT_FROM_WIN32(ERROR_MORE_DATA) && count>0 && count<128) {
            std::vector<BTH_LE_GATT_CHARACTERISTIC> chars(count);USHORT actual=0;
            hr=BluetoothGATTGetCharacteristics(device,nullptr,count,chars.data(),&actual,BLUETOOTH_GATT_FLAG_NONE);
            if(SUCCEEDED(hr) && actual<=count)for(USHORT j=0;j<actual;j++) {
                auto& ch=chars[j];bool battery=ch.CharacteristicUuid.IsShortUuid && ch.CharacteristicUuid.Value.ShortUuid==0x2a19;
                if(!battery)continue;USHORT needed=0;
                hr=BluetoothGATTGetCharacteristicValue(device,&ch,0,nullptr,&needed,BLUETOOTH_GATT_FLAG_FORCE_READ_FROM_DEVICE);
                if(hr==HRESULT_FROM_WIN32(ERROR_MORE_DATA) && needed>=sizeof(ULONG)+1 && needed<4096) {
                    std::vector<BYTE> value(needed);USHORT used=0;
                    auto v=reinterpret_cast<PBTH_LE_GATT_CHARACTERISTIC_VALUE>(value.data());
                    hr=BluetoothGATTGetCharacteristicValue(device,&ch,needed,v,&used,BLUETOOTH_GATT_FLAG_FORCE_READ_FROM_DEVICE);
                    if(SUCCEEDED(hr) && used<=needed && v->DataSize==1 && v->Data[0]<=100){*level=v->Data[0];answer=S_OK;break;}
                    if(SUCCEEDED(hr))hr=HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
                }
            }
        }
        CloseHandle(device);if(*level>=0)break;answer=hr;
    }
    SetupDiDestroyDeviceInfoList(list);return answer;
}
extern "C" __declspec(dllexport) HRESULT WINAPI StadiaReadBattery(UINT64 address,int* level) {
    if(!level)return E_POINTER;try{return ReadBattery(address,level);}catch(...){*level=-1;return E_FAIL;}
}
#ifdef BATTERY_PROBE
int wmain(int argc,wchar_t** argv){if(argc!=2)return 2;UINT64 address=_wcstoui64(argv[1],nullptr,16);int level=-1;HRESULT hr=ReadBattery(address,&level);printf("Battery HRESULT=0x%08lX level=%d\n",(unsigned long)hr,level);return SUCCEEDED(hr)?0:1;}
#endif
