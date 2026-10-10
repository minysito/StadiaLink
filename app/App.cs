using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using System.Reflection;

[assembly:AssemblyTitle("StadiaLink")]
[assembly:AssemblyDescription("Native Stadia controller settings")]
[assembly:AssemblyCompany("StadiaLink")]
[assembly:AssemblyProduct("StadiaLink")]
[assembly:AssemblyVersion("1.0.1.0")]
[assembly:AssemblyFileVersion("1.0.1.0")]

namespace StadiaStudio {
    public static class Entry {
        [STAThread] public static int Main(string[] args) {
            try {
                if(args.Length==1 && args[0]=="--register-app") {bool startup=InstallerActions.StartWithWindows,notices=InstallerActions.Notifications;InstallerActions.RegisterApp();InstallerActions.SavePreferences(startup,notices);return 0;}
                if(args.Length==2 && args[0]=="--diagnose") {Diagnose(args[1],false);return 0;}
                if(args.Length==2 && args[0]=="--verify-device") {Diagnose(args[1],true);return 0;}
                if(args.Length==2 && args[0]=="--verify-rumble") {VerifyRumble(args[1]);return 0;}
                if(args.Length==2 && args[0]=="--verify-preview") {VerifyPreview(args[1]);return 0;}
                if(args.Length==2 && args[0]=="--self-test") {SelfTest(args[1]);return 0;}
                using(Mutex mutex=new Mutex(false,@"Local\StadiaStudio.App")) {
                    bool acquired=false;try{acquired=mutex.WaitOne(0);}catch(AbandonedMutexException){acquired=true;}
                    if(!acquired){if(!args.Contains("--tray"))MessageBox.Show(I18n.T("StadiaLink ya está abierto."),"StadiaLink");return 0;}
                    Application app=new Application();app.DispatcherUnhandledException+=(s,e)=>{string log=Log(e.Exception);MessageBox.Show(I18n.T("Se produjo un error. Detalles en ")+log,"StadiaLink");e.Handled=true;};
                    MainWindow window=new MainWindow();
                    if(args.Length==2&&args[0]=="--responsive-test")window.Loaded+=async(s,e)=>{try{await window.VerifyResponsive(args[1]);}catch(Exception ex){Directory.CreateDirectory(args[1]);File.WriteAllText(Path.Combine(args[1],"error.txt"),ex.ToString());Environment.ExitCode=1;}finally{window.Close();}};
                    if(args.Length==2&&args[0]=="--continuous-test")window.Loaded+=(s,e)=>{DispatcherTimer test=new DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};test.Tick+=async(a,b)=>{test.Stop();try{await window.VerifyContinuous(args[1]);}catch(Exception ex){File.WriteAllText(args[1]+".error.txt",ex.ToString());Environment.ExitCode=1;}finally{window.Close();}};test.Start();};
                    if(args.Length==2&&args[0]=="--startup-setting-test")window.Loaded+=(s,e)=>{try{window.VerifyStartupSetting(args[1]);}catch(Exception ex){File.WriteAllText(args[1]+".error.txt",ex.ToString());Environment.ExitCode=1;}finally{window.Close();}};
                    if(args.Length==2&&args[0]=="--shortcut-smoke")window.Loaded+=(s,e)=>{try{window.VerifyShortcutDialog(args[1]);}catch(Exception ex){File.WriteAllText(args[1]+".error.txt",ex.ToString());Environment.ExitCode=1;}window.Close();};
                    window.SuppressNotifications=args.Length>0&&!args.Contains("--tray");
                    if(args.Length==2&&args[0]=="--languages-test")window.Loaded+=(s,e)=>{DispatcherTimer test=new DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};test.Tick+=async(a,b)=>{test.Stop();try{await window.VerifyLanguages(args[1]);}catch(Exception ex){Directory.CreateDirectory(args[1]);File.WriteAllText(Path.Combine(args[1],"error.txt"),ex.ToString());Environment.ExitCode=1;}finally{window.Close();}};test.Start();};
                    if(args.Length==2&&args[0]=="--features-test")window.Loaded+=(s,e)=>{DispatcherTimer test=new DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};test.Tick+=(a,b)=>{test.Stop();try{TestNewFeatures(args[1]);window.VerifyExtras(args[1]);}catch(Exception ex){File.WriteAllText(args[1]+".error.txt",ex.ToString());Environment.ExitCode=1;}finally{window.Close();}};test.Start();};
                    if(args.Contains("--tray"))window.Loaded+=(s,e)=>{window.WindowState=WindowState.Minimized;};
                    if(args.Length==3&&args[1]=="--startup-test")window.Loaded+=(s,e)=>{window.SuppressNotifications=true;DispatcherTimer test=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(300)};test.Tick+=(a,b)=>{test.Stop();try{window.VerifyStartupState(args[2]);}catch(Exception ex){File.WriteAllText(args[2]+".error.txt",ex.ToString());Environment.ExitCode=1;}finally{window.Close();}};test.Start();};
                    if(args.Length==2 && args[0]=="--visual-test") {window.Loaded+=(s,e)=>{try{new ControllerVisual().VerifyMotion();window.VerifyAnalogPreview();File.WriteAllText(args[1],"PASS: independent Stadia stick travel, pressed-button feedback, neutral reset on disconnect.");}catch(Exception ex){File.WriteAllText(args[1]+".error.txt",ex.ToString());Environment.ExitCode=1;}window.Close();};}
                    if(args.Length==2 && args[0]=="--performance-test") {window.Loaded+=(s,e)=>{DispatcherTimer measure=new DispatcherTimer{Interval=TimeSpan.FromSeconds(6)};measure.Tick+=(a,b)=>{measure.Stop();window.PerformanceReport(args[1]);window.Close();};measure.Start();};}
                    if(args.Length==2 && args[0]=="--smoke") {
                        window.Loaded+=(s,e)=>{DispatcherTimer shot=new DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};shot.Tick+=(a,b)=>{shot.Stop();string dir=Path.GetFullPath(args[1]);Directory.CreateDirectory(dir);foreach(string route in new[]{"Mando","Sticks y gatillos","Vibración","Perfiles","Ajustes","Mando","Ajustes","Mando"}){window.SetPageForTest(route);window.ExportSnapshot(Path.Combine(dir,route.Replace(" ","-")+".png"));}window.Close();};shot.Start();};
                    }
                    if(args.Length==2 && args[0]=="--ui-test") {
                        window.Loaded+=(s,e)=>{DispatcherTimer test=new DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};test.Tick+=async(a,b)=>{test.Stop();try{await window.VerifyInterface(args[1]);}catch(Exception ex){File.WriteAllText(args[1]+".error.txt",ex.ToString());Environment.ExitCode=1;}finally{window.Close();}};test.Start();};
                    }
                    if(args.Length==2 && args[0]=="--draft-test") {
                        window.Loaded+=(s,e)=>{DispatcherTimer test=new DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};test.Tick+=(a,b)=>{test.Stop();try{window.VerifyDraftBehavior(args[1]);}catch(Exception ex){File.WriteAllText(args[1]+".error.txt",ex.ToString());Environment.ExitCode=1;}finally{window.Close();}};test.Start();};
                    }
                    app.Run(window);mutex.ReleaseMutex();return Environment.ExitCode;
                }
            }catch(Exception e){string log=Log(e);if(args.Length>0){if(args.Length==2)File.WriteAllText(args[1]+".error.txt",e.ToString());return 1;}MessageBox.Show(I18n.Error(e)+Environment.NewLine+I18n.T("Registro: ")+log,"StadiaLink");return 1;}
        }
        public static string Log(Exception e){string dir=Path.Combine(ProfileLibrary.DirectoryPath,"logs");Directory.CreateDirectory(dir);string file=Path.Combine(dir,"app.log");File.AppendAllText(file,DateTime.Now.ToString("s")+" "+e+Environment.NewLine);return file;}
        static void Diagnose(string file,bool verify) {
            System.Text.StringBuilder report=new System.Text.StringBuilder();var pads=Controller.Find();report.AppendLine("Stadia devices: "+pads.Count);
            foreach(PadInfo info in pads)using(Controller pad=new Controller(info.Path)) {
                PadStatus status=pad.Read();report.AppendLine(info.Label+" config="+status.ConfigVersion+" GATT=0x"+status.OpenError.ToString("X8")+" writes="+status.Writes+" stops="+status.Stops);
                Profile original=pad.Configuration();report.AppendLine("Configuration: "+original.ToJson());
                if(verify) {Profile changed=original.Clone();changed.LeftDeadzone=original.LeftDeadzone==17?18:17;try{pad.Apply(changed);report.AppendLine("Write/read configuration: PASS");}finally{pad.Apply(original);}report.AppendLine("Restore original configuration: PASS");}
            }
            File.WriteAllText(file,report.ToString());if(pads.Count==0)throw new InvalidOperationException("No Stadia controller found.");
        }
        static void Assert(bool value,string name){if(!value)throw new Exception("FAILED: "+name);}
        static void VerifyRumble(string file) {
            var pads=Controller.Find();if(pads.Count!=1)throw new InvalidOperationException("Expected exactly one Stadia controller.");
            using(Controller pad=new Controller(pads[0].Path)) {
                PadStatus before=pad.Read();try {pad.Test(60,60);Thread.Sleep(700);PadStatus after=pad.Read();
                    Assert(after.WriteError==0 && !after.RumbleFailed,"GATT write success");Assert(after.Writes>=before.Writes+2,"pulse and autonomous stop");Assert(after.Stops>=before.Stops+1,"driver watchdog stop");
                    File.WriteAllText(file,"PASS: exact Stadia HID identified; pulse and autonomous driver stop confirmed. Writes="+after.Writes+" stops="+after.Stops+".");
                }finally{pad.Test(0,0);}
            }
        }
        static void VerifyPreview(string file) {
            var pads=Controller.Find();Assert(pads.Count==1,"exactly one Stadia");
            using(Controller pad=new Controller(pads[0].Path)) {
                byte[] configuration=pad.Configuration().ToReport();PadStatus before=pad.Read();
                Assert(before.RawMotorPreview,"updated driver preview capability");
                try {pad.Preview(45,55);Thread.Sleep(700);PadStatus after=pad.Read();
                    Assert(after.WriteError==0&&!after.RumbleFailed,"preview GATT write");
                    Assert(after.Writes>=before.Writes+2&&after.Stops>=before.Stops+1,"preview autonomous stop");
                    Assert(configuration.SequenceEqual(pad.Configuration().ToReport()),"preview does not save configuration");
                    File.WriteAllText(file,"PASS: unsaved motor preview, successful Bluetooth writes, autonomous stop, configuration unchanged.");
                }finally{pad.Preview(0,0);}
            }
        }
        static void TestNewFeatures(string file){
            string originalLanguage=I18n.Code;object savedLanguage=null;using(var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\StadiaStudio")){if(key!=null)savedLanguage=key.GetValue("Language");}try{I18n.Set("fr");Assert(I18n.T("Guardar y aplicar")=="Enregistrer et appliquer"&&I18n.T("Batería: 58 %")=="Batterie : 58 %","French static and dynamic text");I18n.Save();Assert(I18n.Saved=="fr","language persisted");I18n.Set("en");Assert(I18n.T("Batería baja")=="Low battery","English notification text");I18n.Set("de");Assert(I18n.T("Salir")=="Beenden","German tray text");}finally{using(var key=Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\StadiaStudio")){if(savedLanguage==null)key.DeleteValue("Language",false);else key.SetValue("Language",savedLanguage);}I18n.Set(originalLanguage);}
            const string runPath=@"Software\Microsoft\Windows\CurrentVersion\Run",prefsPath=@"Software\StadiaStudio";object originalRun=null,originalNotice=null;
            using(var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(runPath)){if(key!=null)originalRun=key.GetValue("StadiaStudio");}using(var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(prefsPath)){if(key!=null)originalNotice=key.GetValue("Notifications");}
            try{InstallerActions.SavePreferences(true,false);Assert(InstallerActions.StartWithWindows&&!InstallerActions.Notifications,"startup and notification preferences written");using(var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(runPath)){Assert((string)key.GetValue("StadiaStudio")=="\""+Path.Combine(InstallerActions.InstallDirectory,"StadiaLink.exe")+"\" --tray","quoted startup command");}InstallerActions.SavePreferences(false,true);Assert(!InstallerActions.StartWithWindows&&InstallerActions.Notifications,"startup can be disabled");}
            finally{using(var key=Microsoft.Win32.Registry.CurrentUser.CreateSubKey(runPath)){if(originalRun==null)key.DeleteValue("StadiaStudio",false);else key.SetValue("StadiaStudio",originalRun);}using(var key=Microsoft.Win32.Registry.CurrentUser.CreateSubKey(prefsPath)){if(originalNotice==null)key.DeleteValue("Notifications",false);else key.SetValue("Notifications",originalNotice);}}
            DeviceNotifications alerts=new DeviceNotifications();DateTime time=DateTime.UtcNow;
            Assert(alerts.Observe(false,null,time).Count==0,"no disconnected alert at startup");
            Assert(alerts.Observe(true,50,time.AddSeconds(1)).Count==0,"connection debounce");
            Assert(alerts.Observe(true,50,time.AddSeconds(2)).Count==1,"connected notification");
            Assert(alerts.Observe(true,15,time.AddSeconds(3)).Count==1,"low battery notification");
            Assert(alerts.Observe(true,14,time.AddSeconds(4)).Count==0,"low battery not repeated");
            Assert(alerts.Observe(true,null,time.AddSeconds(5)).Count==0,"unknown battery not full");
            alerts.Observe(true,99,time.AddSeconds(6));Assert(alerts.Observe(true,100,time.AddSeconds(7)).Count==1,"full battery notification");
            Assert(alerts.Observe(true,100,time.AddSeconds(8)).Count==0,"full battery not repeated");
            alerts.Observe(true,99,time.AddSeconds(8));Assert(alerts.Observe(true,100,time.AddSeconds(8)).Count==0,"full battery hysteresis");
            alerts.Observe(false,null,time.AddSeconds(9));Assert(alerts.Observe(false,null,time.AddSeconds(10)).Count==1,"disconnected notification");
            Profile p=new Profile();p.EnsureActions();p.Map[0]=0;p.Actions[0]=14;p.Keys[0]=121;p.Modifiers[0]=3;Assert(Profile.FromJson(p.ToJson()).Actions[0]==14,"tap shortcut JSON");
            var emitted=new System.Collections.Generic.List<string>();using(DesktopActions engine=new DesktopActions()){engine.Send=(k,up)=>emitted.Add(k+":"+up);PadStatus s=new PadStatus{HasInput=true};s.Raw[1]=8;engine.Update(s,p);s.Raw[3]=64;engine.Update(s,p);Assert(emitted.Count==6&&emitted.Last()=="17:True","tap keys released immediately");engine.Update(s,p);Assert(emitted.Count==6,"tap not repeated while held");s.Raw[3]=0;engine.Update(s,p);s.Raw[3]=64;engine.Update(s,p);Assert(emitted.Count==12,"second press repeats shortcut");}
            string preview=file+".png";using(var sheet=new System.Drawing.Bitmap(480,144))using(var g=System.Drawing.Graphics.FromImage(sheet)){g.Clear(System.Drawing.Color.FromArgb(26,34,37));g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;g.PixelOffsetMode=System.Drawing.Drawing2D.PixelOffsetMode.Half;int[] values={0,9,15,58,99,100};for(int i=0;i<values.Length;i++)using(var icon=BatteryTray.DrawBadge(values[i])){Assert(icon.Width==16&&icon.Height==16,"native tray icon size");g.DrawImage(icon,new System.Drawing.Rectangle(i*80,0,64,64));g.DrawImageUnscaled(icon,i*80+24,90);}sheet.Save(preview,System.Drawing.Imaging.ImageFormat.Png);}
            File.WriteAllText(file,"PASS: connection debounce; one low-battery alert; unknown battery handling; full-battery crossing; disconnect; tap shortcut serialization and edge behavior; native-size badge preview. No global keys or notifications sent.");
        }
        static void SelfTest(string file) {
            Assert(Profile.AxisValue(128,0,200,false)==0,"neutral at high sensitivity");
            Assert(Profile.AxisValue(136,10,200,false)==0,"deadzone before sensitivity");
            Assert(Profile.AxisValue(136,0,100,false)>0,"zero deadzone preserves small travel");
            Assert(Profile.AxisValue(150,0,200,false)>Profile.AxisValue(150,0,100,false),"sensitivity affects partial travel");
            Assert(Profile.AxisValue(150,0,50,false)<Profile.AxisValue(150,0,100,false),"lower sensitivity reduces travel");
            Assert(Profile.AxisValue(255,0,200,false)==1&&Profile.AxisValue(0,0,100,false)==-1,"axis endpoints and saturation");
            Assert(Profile.AxisValue(150,0,100,true)==-Profile.AxisValue(150,0,100,false),"preview inversion");
            Profile p=new Profile();Assert(p.ToReport().Length==64,"report size");Assert(Profile.FromReport(p.ToReport()).ToJson()==p.ToJson(),"report roundtrip");
            p.Name="Perfil ñ";p.Flags=63;p.Map[0]=2;p.LeftDeadzone=23;p.StrongScale=38;Assert(Profile.FromJson(p.ToJson()).ToJson()==p.ToJson(),"JSON roundtrip");
            byte[] corrupt=p.ToReport();corrupt[4]^=1;bool rejected=false;try{Profile.FromReport(corrupt);}catch(InvalidDataException){rejected=true;}Assert(rejected,"checksum rejection");
            p.Map[0]=200;rejected=false;try{p.ToReport();}catch(InvalidDataException){rejected=true;}Assert(rejected,"invalid mapping rejection");
            rejected=false;try{Profile.FromJson("{\"Version\":1,\"Name\":\"Bad\",\"LeftSensitivity\":100,\"RightSensitivity\":100,\"StrongScale\":100,\"WeakScale\":100,\"TriggerThreshold\":50,\"Map\":[1]}");}catch(InvalidDataException){rejected=true;}Assert(rejected,"truncated mapping rejection");
            Profile action=new Profile();action.EnsureActions();action.Map[0]=0;action.Map[1]=0;action.Actions[0]=action.Actions[1]=13;action.Keys[0]=65;action.Keys[1]=66;action.Modifiers[0]=action.Modifiers[1]=1;
            Assert(Profile.FromJson(action.ToJson()).Actions[0]==13,"shortcut JSON roundtrip");
            var emitted=new System.Collections.Generic.List<string>();using(DesktopActions engine=new DesktopActions()) {
                engine.Send=(k,up)=>emitted.Add(k+":"+up);PadStatus state=new PadStatus{HasInput=true};state.Raw[1]=8;engine.Update(state,action);
                state.Raw[3]=64;engine.Update(state,action);engine.Update(state,action);Assert(emitted.Count==2,"one press without repeats");
                state.Raw[3]=96;engine.Update(state,action);Assert(emitted.Count==3,"shared modifier not pressed twice");
                state.Raw[3]=32;engine.Update(state,action);Assert(emitted.Count==4&&emitted[3]=="65:True","first key released while modifier held");
                engine.Update(null,action);Assert(emitted.Contains("17:True")&&emitted.Contains("66:True"),"all held keys released on disconnect");
            }
            File.WriteAllText(file,"PASS: configuration and JSON roundtrips, checksum and profile validation, shortcut serialization, rising-edge actions, shared modifiers, key release on disconnect. No system keys injected during tests.");
        }
    }
}






