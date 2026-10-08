using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using Forms=System.Windows.Forms;
namespace StadiaStudio {
 public static class Battery {
  [DllImport("StadiaDevice.dll",CallingConvention=CallingConvention.Winapi)] static extern int StadiaReadBattery(ulong address,out int level);
  public static int? Read(ulong address){int level;int hr=StadiaReadBattery(address,out level);return hr>=0&&level>=0&&level<=100?(int?)level:null;}
 }
 public sealed class BatteryTray:IDisposable {
  readonly Forms.NotifyIcon tray=new Forms.NotifyIcon();readonly Icon logo;
  readonly Forms.ToolStripMenuItem level=new Forms.ToolStripMenuItem("Batería: —"){Enabled=false};Icon badge;int? lastLevel;bool lastConnected;bool lastCharging;bool first=true;
  readonly Queue<DeviceNotice> pending=new Queue<DeviceNotice>();readonly Forms.Timer noticeTimer=new Forms.Timer{Interval=6500};
  string lastLanguage;
  [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
  public BatteryTray(Action open,Action exit){
   logo=Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);tray.Icon=logo;tray.Text=I18n.T("StadiaLink");
   var menu=new Forms.ContextMenuStrip();menu.BackColor=Color.FromArgb(26,34,37);menu.ForeColor=Color.FromArgb(233,238,235);menu.Items.Add(level);menu.Items.Add(new Forms.ToolStripSeparator());menu.Items.Add(I18n.T("Abrir StadiaLink"),null,(s,e)=>open()).Tag="Abrir StadiaLink";menu.Items.Add(I18n.T("Salir"),null,(s,e)=>exit()).Tag="Salir";tray.ContextMenuStrip=menu;tray.DoubleClick+=(s,e)=>open();tray.Visible=true;
   noticeTimer.Tick+=(s,e)=>{if(pending.Count==0)noticeTimer.Stop();else ShowNotice(pending.Dequeue());};
  }
  public bool Visible {get{return tray.Visible;}}
  public void Update(bool connected,int? percent,bool charging=false){
   if(!first && lastLevel==percent && lastConnected==connected&&lastCharging==charging&&lastLanguage==I18n.Code)return;first=false;lastLevel=percent;lastConnected=connected;lastCharging=charging;lastLanguage=I18n.Code;foreach(Forms.ToolStripItem item in tray.ContextMenuStrip.Items)if(item.Tag is string)item.Text=I18n.T((string)item.Tag);
   level.Text=I18n.T(!connected?"Mando desconectado":percent.HasValue?"Batería: "+percent.Value+" %":"Batería: no disponible");
   tray.Text=I18n.T(!connected?"StadiaLink · sin mando":percent.HasValue?"Stadia · "+percent.Value+" %":"StadiaLink · batería no disponible");
   if(connected&&charging){string suffix=I18n.T(percent==100?" · Carga completa":" · Cargando");level.Text+=suffix;tray.Text=(tray.Text+suffix).Substring(0,Math.Min(63,tray.Text.Length+suffix.Length));}
   Icon previous=badge;badge=null;
   if(connected&&percent.HasValue){using(Bitmap b=DrawBadge(percent.Value)) {
     IntPtr native=b.GetHicon();try{using(Icon view=Icon.FromHandle(native))badge=(Icon)view.Clone();}finally{DestroyIcon(native);}
   }}
   tray.Icon=badge??logo;if(previous!=null)previous.Dispose();
  }
  void ShowNotice(DeviceNotice notice){tray.ShowBalloonTip(5000,I18n.T(notice.Title),I18n.T(notice.Text),notice.Warning?Forms.ToolTipIcon.Warning:Forms.ToolTipIcon.Info);}
  public void Notify(DeviceNotice notice){if(noticeTimer.Enabled){if(pending.Count<8)pending.Enqueue(notice);}else{ShowNotice(notice);noticeTimer.Start();}}
  public void ClearNotifications(){pending.Clear();noticeTimer.Stop();}
  public static Bitmap DrawBadge(int percent){Bitmap bitmap=new Bitmap(16,16);string[] glyphs={"111101101101111","010110010010111","111001111100111","111001111001111","101101111001001","111100111001111","111100111101111","111001001001001","111101111101111","111101111001111"};string digits=percent.ToString();int sx=digits.Length==3?1:2,sy=2,width=digits.Length*(3*sx+1)-1,x0=(16-width)/2;using(Graphics g=Graphics.FromImage(bitmap))using(SolidBrush ink=new SolidBrush(Color.White))using(SolidBrush charge=new SolidBrush(percent<=15?Color.FromArgb(255,138,112):Color.FromArgb(202,245,122))){g.Clear(Color.FromArgb(16,21,23));for(int d=0;d<digits.Length;d++)for(int y=0;y<5;y++)for(int x=0;x<3;x++)if(glyphs[digits[d]-'0'][y*3+x]=='1')g.FillRectangle(ink,x0+d*(3*sx+1)+x*sx,1+y*sy,sx,sy);g.FillRectangle(charge,1,13,Math.Max(1,(int)Math.Round(14*percent/100.0)),2);}return bitmap;}
  public void Dispose(){noticeTimer.Dispose();tray.Visible=false;tray.Dispose();if(badge!=null)badge.Dispose();if(logo!=null)logo.Dispose();}
 }
}


