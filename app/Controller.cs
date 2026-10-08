using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace StadiaStudio {
    public sealed class PadStatus {
        public long SampleTicks;public byte[] Raw=new byte[10];public bool HasInput,RumbleFailed,Bluetooth;
        public uint OpenError,WriteError,Writes,Stops;public byte ConfigVersion;
        public ulong Address;public uint PersistenceError;public bool RawMotorPreview;
        public bool[] Buttons {
            get {
                byte a=Raw[2],b=Raw[3],h=Raw[1];
                return new bool[]{(b&64)!=0,(b&32)!=0,(b&16)!=0,(b&8)!=0,(b&4)!=0,(b&2)!=0,(a&64)!=0,(a&32)!=0,(b&1)!=0,(a&128)!=0,
                    (a&16)!=0,(a&1)!=0,(a&2)!=0,h==0||h==1||h==7,h==3||h==4||h==5,h==5||h==6||h==7,h==1||h==2||h==3,Raw[8]>127,Raw[9]>127};
            }
        }
    }
    public sealed class PadInfo {
        public string Path,Label; public override string ToString(){return Label;}
    }
    public sealed class Controller:IDisposable {
        readonly object gate=new object();SafeFileHandle handle;public string Path;
        [StructLayout(LayoutKind.Sequential)] struct Attributes {public int Size;public ushort Vendor,Product,Version;}
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateFileW(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
        [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("hid.dll")][return:MarshalAs(UnmanagedType.U1)] static extern bool HidD_GetAttributes(SafeFileHandle h,ref Attributes a);
        [DllImport("hid.dll",CharSet=CharSet.Unicode)][return:MarshalAs(UnmanagedType.U1)] static extern bool HidD_GetSerialNumberString(SafeFileHandle h,StringBuilder text,int length);
        [DllImport("hid.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.U1)] static extern bool HidD_GetFeature(SafeFileHandle h,byte[] data,int size);
        [DllImport("hid.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.U1)] static extern bool HidD_SetFeature(SafeFileHandle h,byte[] data,int size);
        [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)] static extern int CM_Get_Device_Interface_List_SizeW(out uint size,ref Guid guid,string id,uint flags);
        [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)] static extern int CM_Get_Device_Interface_ListW(ref Guid guid,string id,char[] data,uint size,uint flags);
        static SafeFileHandle Open(string path){return CreateFileW(path,0,3,IntPtr.Zero,3,0,IntPtr.Zero);}
        public static List<PadInfo> Find() {
            Guid guid;HidD_GetHidGuid(out guid);uint size;
            if(CM_Get_Device_Interface_List_SizeW(out size,ref guid,null,0)!=0 || size<2 || size>1048576) return new List<PadInfo>();
            char[] chars=new char[size];if(CM_Get_Device_Interface_ListW(ref guid,null,chars,size,0)!=0) return new List<PadInfo>();
            List<PadInfo> pads=new List<PadInfo>();
            foreach(string path in new string(chars).Split(new char[]{'\0'},StringSplitOptions.RemoveEmptyEntries)) {
                using(SafeFileHandle h=Open(path)) {
                    if(h.IsInvalid) continue;Attributes a=new Attributes();a.Size=Marshal.SizeOf(typeof(Attributes));StringBuilder serial=new StringBuilder(128);
                    if(HidD_GetAttributes(h,ref a) && a.Vendor==0x045e && a.Product==0x02fd && HidD_GetSerialNumberString(h,serial,serial.Capacity*2) && serial.ToString()=="WinStadia") {
                        byte[] b=new byte[64];b[0]=0xE1;
                        if(!HidD_GetFeature(h,b,64)) continue;
                        ulong address=BitConverter.ToUInt64(b,45);string label=address==0?"Stadia · USB":"Stadia · Bluetooth · "+address.ToString("X12").Substring(8);
                        pads.Add(new PadInfo{Path=path,Label=label});
                    }
                }
            }
            return pads;
        }
        public Controller(string path){Path=path;handle=Open(path);if(handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());}
        byte[] Get(byte id) {
            lock(gate){byte[] b=new byte[64];b[0]=id;if(handle==null || handle.IsInvalid || !HidD_GetFeature(handle,b,b.Length)) throw new Win32Exception(Marshal.GetLastWin32Error());return b;}
        }
        void Set(byte[] b) {
            lock(gate){if(handle==null || handle.IsInvalid || !HidD_SetFeature(handle,b,b.Length)) throw new Win32Exception(Marshal.GetLastWin32Error());}
        }
        public PadStatus Read() {
            byte[] b=Get(0xE1);PadStatus s=new PadStatus();s.RumbleFailed=b[1]!=0;s.HasInput=b[6]>=10;
            if(s.HasInput) Buffer.BlockCopy(b,7,s.Raw,0,10);else {s.Raw[1]=8;for(int i=4;i<8;i++)s.Raw[i]=128;}
            s.OpenError=BitConverter.ToUInt32(b,25);s.WriteError=BitConverter.ToUInt32(b,29);s.Writes=BitConverter.ToUInt32(b,37);s.Stops=BitConverter.ToUInt32(b,41);
            s.Address=BitConverter.ToUInt64(b,45);s.Bluetooth=s.Address!=0;s.ConfigVersion=b[53];s.PersistenceError=BitConverter.ToUInt32(b,54);s.RawMotorPreview=b[58]!=0;return s;
        }
        public Profile Configuration(){return Profile.FromReport(Get(0xE2));}
        public void Apply(Profile p) {
            byte[] expected=p.ToReport();Set(expected);byte[] actual=Get(0xE2);
            for(int i=0;i<64;i++) if(actual[i]!=expected[i]) throw new InvalidOperationException("El controlador no ha confirmado la configuración.");
        }
        public void Test(byte strong,byte weak){byte[] b=new byte[64];b[0]=0xE3;b[1]=1;b[2]=strong;b[3]=weak;Set(b);}
        public void Preview(byte strong,byte weak){byte[] b=new byte[64];b[0]=0xE3;b[1]=1;b[2]=strong;b[3]=weak;b[4]=1;Set(b);}
        public void Dispose(){lock(gate){if(handle!=null){handle.Dispose();handle=null;}}}
    }
}
