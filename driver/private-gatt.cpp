// Native Stadia rumble transport. Private ABI recovered from the installed
// Microsoft.Bluetooth.Service and HidOverGatt public symbols (Windows 26200).
#define WIN32_LEAN_AND_MEAN
#include "private-gatt.h"
#include <roapi.h>
#include <winstring.h>
#include <inspectable.h>
#include <asyncinfo.h>
#include <windows.storage.streams.h>
#include <robuffer.h>
#include <wrl/client.h>
#include <wrl/wrappers/corewrappers.h>
#include <new>
#include <memory>

using Microsoft::WRL::ComPtr;
using Microsoft::WRL::Wrappers::HStringReference;
using ABI::Windows::Storage::Streams::IBuffer;
using ABI::Windows::Storage::Streams::IBufferFactory;
using Windows::Storage::Streams::IBufferByteAccess;
template<typename T> static T Slot(IInspectable* object,size_t index) {
    return reinterpret_cast<T>((*reinterpret_cast<void***>(object))[index]);
}
struct RemoteAddress { UINT64 Address;UINT32 Type;UINT32 Padding; };
struct GattStatics:IInspectable {
    virtual HRESULT STDMETHODCALLTYPE CreateAsync(RemoteAddress,UINT32,HSTRING,HSTRING,UINT32,IInspectable**)=0;
};
// The exact ABI sizes and offsets were checked against Microsoft's call sites.
struct ServiceInfo { USHORT First,Last;GUID Uuid;UINT32 Flags;USHORT Handle,Padding; };
struct CharacteristicInfo { USHORT First,Last;GUID Uuid;UINT32 Properties;USHORT Handle,ValueHandle;UINT32 Padding; };
static_assert(sizeof(ServiceInfo)==28,"service ABI");
static_assert(sizeof(CharacteristicInfo)==32,"characteristic ABI");
static GUID BluetoothUuid(USHORT value) { return {value,0,0x1000,{0x80,0,0,0x80,0x5f,0x9b,0x34,0xfb}}; }
struct Apartment {
    HRESULT Status=RoInitialize(RO_INIT_MULTITHREADED);
    ~Apartment() {if(SUCCEEDED(Status)) RoUninitialize();}
    HRESULT Check() const {return Status==RPC_E_CHANGED_MODE?S_OK:Status;}
};
struct Session {
    ComPtr<IAgileReference> Device,Service;
    ComPtr<IInspectable> Characteristic;
    GUID CharacteristicIID={};
    STADIA_GATT_DIAGNOSTICS Diagnostics={};
};
static HRESULT Await(IInspectable* operation,IInspectable** result) {
    *result=nullptr;if(!operation) return E_UNEXPECTED;
    ComPtr<ABI::Windows::Foundation::IAsyncInfo> info;
    HRESULT hr=operation->QueryInterface(IID_PPV_ARGS(&info));
    if(FAILED(hr)) return hr;
    AsyncStatus status=Started;DWORD start=GetTickCount();
    while(SUCCEEDED(hr=info->get_Status(&status)) && status==Started && GetTickCount()-start<2000) Sleep(2);
    if(SUCCEEDED(hr) && status==Completed)
        hr=Slot<HRESULT (STDMETHODCALLTYPE *)(IInspectable*,IInspectable**)>(operation,8)(operation,result);
    else if(status==Started) {info->Cancel();hr=HRESULT_FROM_WIN32(ERROR_TIMEOUT);}
    else if(SUCCEEDED(hr)) {info->get_ErrorCode(&hr);if(SUCCEEDED(hr)) hr=E_FAIL;}
    info->Close();return hr;
}
static HRESULT CheckCommunication(IInspectable* result) {
    if(!result) return E_UNEXPECTED;
    UINT32 status=0;
    HRESULT hr=Slot<HRESULT (STDMETHODCALLTYPE *)(IInspectable*,UINT32*)>(result,6)(result,&status);
    if(FAILED(hr)) return hr;
    // Successful async completion alone does not prove a successful ATT operation.
    if(status==0) return S_OK;
    if(status==3) return E_ACCESSDENIED;
    return HRESULT_FROM_WIN32(ERROR_GEN_FAILURE);
}
static HRESULT ResultVector(IInspectable* result,IInspectable** vector,UINT32* count) {
    *vector=nullptr;*count=0;
    HRESULT hr=CheckCommunication(result);if(FAILED(hr)) return hr;
    hr=Slot<HRESULT (STDMETHODCALLTYPE *)(IInspectable*,IInspectable**)>(result,9)(result,vector);
    if(FAILED(hr) || !*vector) return FAILED(hr)?hr:E_UNEXPECTED;
    hr=Slot<HRESULT (STDMETHODCALLTYPE *)(IInspectable*,UINT32*)>(*vector,7)(*vector,count);
    if(SUCCEEDED(hr) && *count>128) return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
    return hr;
}
static HRESULT GetDevice(UINT64 address,IInspectable** device) {
    *device=nullptr;
    const GUID staticsIID={0xcbaa1a5f,0xdcef,0x508b,{0xac,0xc6,0x15,0x8f,0x1e,0x16,0x0c,0x0f}};
    ComPtr<GattStatics> factory;
    HRESULT hr=RoGetActivationFactory(HStringReference(L"Microsoft.Bluetooth.Profiles.Gatt.Interface.GattClientDevice").Get(),staticsIID,(void**)factory.GetAddressOf());
    if(FAILED(hr)) return hr;
    RemoteAddress remote={address,1,0};ComPtr<IInspectable> operation;
    hr=factory->CreateAsync(remote,0,nullptr,nullptr,0,&operation);if(FAILED(hr)) return hr;
    ComPtr<IInspectable> created;hr=Await(operation.Get(),&created);if(FAILED(hr)) return hr;
    if(!created) return E_UNEXPECTED;
    const GUID iid={0x6bbede1f,0x797f,0x5c17,{0x8a,0xc7,0x3f,0x34,0x20,0x37,0xcb,0x59}};
    return created->QueryInterface(iid,(void**)device);
}
static HRESULT ReadReportReference(IInspectable* discovery,IInspectable* characteristic,const CharacteristicInfo& info,UCHAR* id,UCHAR* type) {
    GUID reference=BluetoothUuid(0x2908);
    ComPtr<IInspectable> operation;
    HRESULT hr=Slot<HRESULT (STDMETHODCALLTYPE *)(IInspectable*,const CharacteristicInfo*,const GUID*,UINT32,IInspectable**)>(discovery,11)
        (discovery,&info,&reference,0,&operation);
    if(FAILED(hr)) return hr;
    ComPtr<IInspectable> result;hr=Await(operation.Get(),&result);if(FAILED(hr)) return hr;
    ComPtr<IInspectable> vector;UINT32 count=0;hr=ResultVector(result.Get(),&vector,&count);if(FAILED(hr)) return hr;
    if(count!=1) return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
    // Descriptor info is kept opaque; the exact value returned by discovery is
    // passed to the read method without inventing or modifying an attribute handle.
    alignas(16) BYTE descriptor[128]={};
    hr=Slot<HRESULT (STDMETHODCALLTYPE *)(IInspectable*,UINT32,void*)>(vector.Get(),6)(vector.Get(),0,descriptor);
    if(FAILED(hr)) return hr;
    operation.Reset();
    hr=Slot<HRESULT (STDMETHODCALLTYPE *)(IInspectable*,const void*,UINT32,UINT32,IInspectable**)>(characteristic,9)
        (characteristic,descriptor,0,0,&operation);
    if(FAILED(hr)) return hr;
    result.Reset();hr=Await(operation.Get(),&result);if(FAILED(hr)) return hr;
    hr=CheckCommunication(result.Get());if(FAILED(hr)) return hr;
    ComPtr<IBuffer> buffer;
    hr=Slot<HRESULT (STDMETHODCALLTYPE *)(IInspectable*,IBuffer**)>(result.Get(),8)(result.Get(),&buffer);
    if(FAILED(hr) || !buffer) return FAILED(hr)?hr:E_UNEXPECTED;
    UINT32 length=0;hr=buffer->get_Length(&length);if(FAILED(hr)) return hr;
    if(length!=2) return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
    ComPtr<IBufferByteAccess> access;hr=buffer.As(&access);if(FAILED(hr)) return hr;
    BYTE* bytes=nullptr;hr=access->Buffer(&bytes);if(FAILED(hr) || !bytes) return FAILED(hr)?hr:E_UNEXPECTED;
    *id=bytes[0];*type=bytes[1];return S_OK;
}
static HRESULT Open(UINT64 address,Session& session) {
    ComPtr<IInspectable> device;HRESULT hr=GetDevice(address,&device);if(FAILED(hr)) return hr;
    GUID hid=BluetoothUuid(0x1812);ComPtr<IInspectable> operation;
    hr=Slot<HRESULT (STDMETHODCALLTYPE *)(IInspectable*,const GUID*,UINT32,IInspectable**)>(device.Get(),11)(device.Get(),&hid,0,&operation);
    if(FAILED(hr)) return hr;
    ComPtr<IInspectable> result;hr=Await(operation.Get(),&result);if(FAILED(hr)) return hr;
    ComPtr<IInspectable> vector;UINT32 count=0;hr=ResultVector(result.Get(),&vector,&count);if(FAILED(hr)) return hr;
    if(count!=1) return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
    ServiceInfo serviceInfo={};
    hr=Slot<HRESULT (STDMETHODCALLTYPE *)(IInspectable*,UINT32,ServiceInfo*)>(vector.Get(),6)(vector.Get(),0,&serviceInfo);
    if(FAILED(hr)) return hr;
    if(!IsEqualGUID(serviceInfo.Uuid,hid)) return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
    ComPtr<IInspectable> service;
    hr=Slot<HRESULT (STDMETHODCALLTYPE *)(IInspectable*,const ServiceInfo*,UINT64,UINT32,IInspectable**)>(device.Get(),14)
        (device.Get(),&serviceInfo,0x0000000100000001ULL,0,&service);
    if(FAILED(hr) || !service) return FAILED(hr)?hr:E_UNEXPECTED;
    ComPtr<IInspectable> discovery;
    hr=Slot<HRESULT (STDMETHODCALLTYPE *)(IInspectable*,IInspectable**)>(service.Get(),11)(service.Get(),&discovery);
    if(FAILED(hr) || !discovery) return FAILED(hr)?hr:E_UNEXPECTED;
    operation.Reset();hr=Slot<HRESULT (STDMETHODCALLTYPE *)(IInspectable*,UINT32,IInspectable**)>(discovery.Get(),8)(discovery.Get(),0,&operation);
    if(FAILED(hr)) return hr;
    result.Reset();hr=Await(operation.Get(),&result);if(FAILED(hr)) return hr;
    vector.Reset();hr=ResultVector(result.Get(),&vector,&count);if(FAILED(hr)) return hr;
    GUID reportUuid=BluetoothUuid(0x2a4d);
    for(UINT32 i=0;i<count;i++) {
        CharacteristicInfo info={};
        hr=Slot<HRESULT (STDMETHODCALLTYPE *)(IInspectable*,UINT32,CharacteristicInfo*)>(vector.Get(),6)(vector.Get(),i,&info);
        if(FAILED(hr)) return hr;
        if(!IsEqualGUID(info.Uuid,reportUuid) || !(info.Properties&0x08)) continue;
        ComPtr<IInspectable> characteristic;
        hr=Slot<HRESULT (STDMETHODCALLTYPE *)(IInspectable*,const CharacteristicInfo*,IInspectable**)>(service.Get(),12)(service.Get(),&info,&characteristic);
        if(FAILED(hr) || !characteristic) return FAILED(hr)?hr:E_UNEXPECTED;
        UCHAR id=0,type=0;hr=ReadReportReference(discovery.Get(),characteristic.Get(),info,&id,&type);
        if(FAILED(hr)) return hr;
        if(id!=5 || type!=2) continue;
        hr=RoGetAgileReference(AGILEREFERENCE_DEFAULT,__uuidof(IInspectable),device.Get(),&session.Device);if(FAILED(hr)) return hr;
        hr=RoGetAgileReference(AGILEREFERENCE_DEFAULT,__uuidof(IInspectable),service.Get(),&session.Service);if(FAILED(hr)) return hr;
        // The native characteristic proxy implements FtmBase. Preserve the
        // original typed interface facet rather than resolving its COM identity.
        session.Characteristic=characteristic;
        session.Diagnostics.ValueHandle=info.ValueHandle;session.Diagnostics.ReportId=id;session.Diagnostics.ReportType=type;
        return S_OK;
    }
    return HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
}
extern "C" HRESULT StadiaPrivateGattOpen(UINT64 address,void** output,STADIA_GATT_DIAGNOSTICS* diagnostics) {
    if(!output || !diagnostics || !address) return E_INVALIDARG;
    *output=nullptr;*diagnostics={};Apartment apartment;
    HRESULT hr=apartment.Check();if(FAILED(hr)) {diagnostics->OpenStatus=hr;return hr;}
    std::unique_ptr<Session> session(new(std::nothrow) Session);if(!session) return E_OUTOFMEMORY;
    hr=Open(address,*session);session->Diagnostics.OpenStatus=hr;*diagnostics=session->Diagnostics;
    if(SUCCEEDED(hr)) *output=session.release();return hr;
}
static HRESULT Write(Session& session,const UCHAR* report,size_t length) {
    if(!report || length!=5 || report[0]!=session.Diagnostics.ReportId) return E_INVALIDARG;
    ComPtr<IInspectable> characteristic=session.Characteristic; HRESULT hr=S_OK;
    ComPtr<IBufferFactory> factory;
    hr=RoGetActivationFactory(HStringReference(L"Windows.Storage.Streams.Buffer").Get(),IID_PPV_ARGS(&factory));if(FAILED(hr)) return hr;
    ComPtr<IBuffer> buffer;hr=factory->Create(4,&buffer);if(FAILED(hr)) return hr;
    hr=buffer->put_Length(4);if(FAILED(hr)) return hr;
    ComPtr<IBufferByteAccess> access;hr=buffer.As(&access);if(FAILED(hr)) return hr;
    BYTE* bytes=nullptr;hr=access->Buffer(&bytes);if(FAILED(hr) || !bytes) return FAILED(hr)?hr:E_UNEXPECTED;
    // Report ID belongs to the Report Reference descriptor, not the GATT value.
    CopyMemory(bytes,report+1,4);
    ComPtr<IInspectable> operation;
    // GattWriteOption::WriteWithResponse (1); no write-without-response path.
    hr=Slot<HRESULT (STDMETHODCALLTYPE *)(IInspectable*,UINT32,UINT32,IBuffer*,IInspectable**)>(characteristic.Get(),8)
        (characteristic.Get(),1,0,buffer.Get(),&operation);
    if(FAILED(hr)) return hr;
    ComPtr<IInspectable> result;hr=Await(operation.Get(),&result);if(FAILED(hr)) return hr;
    return CheckCommunication(result.Get());
}
extern "C" HRESULT StadiaPrivateGattWrite(void* opaque,const UCHAR* report,size_t length,STADIA_GATT_DIAGNOSTICS* diagnostics) {
    if(!opaque || !diagnostics) return E_INVALIDARG;
    Session& session=*static_cast<Session*>(opaque);Apartment apartment;
    HRESULT hr=apartment.Check();if(SUCCEEDED(hr)) hr=Write(session,report,length);
    session.Diagnostics.WriteStatus=hr;
    if(SUCCEEDED(hr)) {session.Diagnostics.WriteCount++;if(report[1]==0 && report[2]==0 && report[3]==0 && report[4]==0) session.Diagnostics.StopCount++;}
    *diagnostics=session.Diagnostics;return hr;
}
extern "C" void StadiaPrivateGattClose(void* session) {
    Apartment apartment;delete static_cast<Session*>(session);
}
