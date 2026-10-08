using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;

namespace StadiaStudio {
 public sealed class KeyOption {
  public int Code;public string Label;public override string ToString(){return I18n.T(Label);}
  public static KeyOption[] All {
   get {var list=new List<KeyOption>();for(int i=65;i<=90;i++)list.Add(new KeyOption{Code=i,Label=((char)i).ToString()});for(int i=48;i<=57;i++)list.Add(new KeyOption{Code=i,Label=((char)i).ToString()});for(int i=1;i<=12;i++)list.Add(new KeyOption{Code=111+i,Label="F"+i});
    int[] codes={13,32,27,9,8,37,38,39,40};string[] names={"Enter","Espacio","Escape","Tab","Retroceso","←","↑","→","↓"};for(int i=0;i<codes.Length;i++)list.Add(new KeyOption{Code=codes[i],Label=names[i]});return list.ToArray();}
  }
 }
 public sealed class DesktopActions:IDisposable {
  [StructLayout(LayoutKind.Sequential)] struct Keyboard {public ushort Key,Scan;public uint Flags,Time;public UIntPtr Extra;}
  [StructLayout(LayoutKind.Explicit,Size=32)] struct Data {[FieldOffset(0)]public Keyboard Keyboard;}
  [StructLayout(LayoutKind.Sequential)] struct Input {public uint Type;public Data Data;}
  [DllImport("user32.dll",SetLastError=true)] static extern uint SendInput(uint count,Input[] inputs,int size);
  readonly Dictionary<int,ushort[]> held=new Dictionary<int,ushort[]>();readonly Dictionary<ushort,int> counts=new Dictionary<ushort,int>();
  bool[] previous=new bool[19];bool primed;
  public Action<ushort,bool> Send;
  public DesktopActions(){Send=NativeSend;}
  public static bool KeySupported(int code){return Profile.KeySupported(code);}
  static void NativeSend(ushort key,bool up) {
   Input[] input={new Input{Type=1,Data=new Data{Keyboard=new Keyboard{Key=key,Flags=up?2u:0u}}}};
   if(SendInput(1,input,Marshal.SizeOf(typeof(Input)))!=1)throw new Win32Exception(Marshal.GetLastWin32Error(),"Windows no ha aceptado el atajo.");
  }
  public static ushort[] Combination(int key,int flags){var keys=new List<ushort>();if((flags&1)!=0)keys.Add(0x11);if((flags&2)!=0)keys.Add(0x12);if((flags&4)!=0)keys.Add(0x10);if((flags&8)!=0)keys.Add(0x5b);keys.Add((ushort)key);return keys.ToArray();}
  static ushort[] Command(int action) {
   switch(action){case 1:return new ushort[]{0xaf};case 2:return new ushort[]{0xae};case 3:return new ushort[]{0xad};case 4:return new ushort[]{0xb3};case 5:return new ushort[]{0xb0};case 6:return new ushort[]{0xb1};case 7:return Combination(0x53,12);case 8:return Combination(0x47,8);case 9:return Combination(0x44,8);case 10:return Combination(9,2);case 11:return Combination(0x25,2);case 12:return Combination(0x27,2);default:return new ushort[0];}
  }
  void Down(ushort[] keys){foreach(ushort key in keys){int count;counts.TryGetValue(key,out count);if(count==0)Send(key,false);counts[key]=count+1;}}
  void Up(ushort[] keys){foreach(ushort key in keys.Reverse()){int count;if(!counts.TryGetValue(key,out count))continue;if(count<=1){counts.Remove(key);Send(key,true);}else counts[key]=count-1;}}
  public void Update(PadStatus status,Profile active) {
   if(status==null || active==null){Release();return;}if(!status.HasInput)return;active.EnsureActions();bool[] current=status.Buttons;
   int dz=255*active.TriggerDeadzone/100;for(int t=0;t<2;t++)current[17+t]=status.Raw[8+t]>dz&&(status.Raw[8+t]-dz)*100>=(255-dz)*active.TriggerThreshold;
   if(!primed){previous=current;primed=true;return;}
   for(int i=0;i<19;i++) {
    if(current[i]&&!previous[i]&&active.Actions[i]!=0){ushort[] keys=active.Actions[i]>=13?Combination(active.Keys[i],active.Modifiers[i]):Command(active.Actions[i]);Down(keys);if(active.Actions[i]==13)held[i]=keys;else Up(keys);}
    if(!current[i]&&previous[i]){ushort[] keys;if(held.TryGetValue(i,out keys)){held.Remove(i);Up(keys);}}
   }
   previous=current;
  }
  public void Release(){foreach(var pair in counts.ToArray())try{Send(pair.Key,true);}catch{}counts.Clear();held.Clear();previous=new bool[19];primed=false;}
  public void Dispose(){Release();}
 }
}

