using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace StadiaStudio {
    public static class InstallerActions {
        public static string InstallDirectory {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","StadiaStudio");}}
        public static string Shortcut {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"StadiaLink.lnk");}}
        public static async Task RunDriver(string script,bool uninstall) {
            ProcessStartInfo info=new ProcessStartInfo("powershell.exe","-NoProfile -ExecutionPolicy Bypass -File \""+script+"\""+(uninstall?" -Uninstall":" -Reconnect"));
            info.UseShellExecute=false;info.CreateNoWindow=true;info.RedirectStandardOutput=true;info.RedirectStandardError=true;
            // A developer shell can export PowerShell 7's module path. The
            // Windows PowerShell host must load its own inbox modules instead.
            info.EnvironmentVariables["PSModulePath"]=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),@"System32\WindowsPowerShell\v1.0\Modules");
            using(Process p=Process.Start(info)) {
                Task<string> stdout=p.StandardOutput.ReadToEndAsync(),stderr=p.StandardError.ReadToEndAsync();await Task.Run(()=>p.WaitForExit());string output=await stdout;string error=await stderr;
                string logs=Path.Combine(ProfileLibrary.DirectoryPath,"logs");Directory.CreateDirectory(logs);File.WriteAllText(Path.Combine(logs,"driver-install.log"),output+Environment.NewLine+error);
                if(p.ExitCode!=0)throw new InvalidOperationException("Windows no pudo completar la instalación del controlador. Consulta el registro en "+logs+".");
            }
        }
        public static void RegisterApp() {
            string previous=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"Stadia Studio.lnk");if(File.Exists(previous))File.Delete(previous);
            object shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));object shortcut=null;
            try {
                shortcut=shell.GetType().InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{Shortcut});
                shortcut.GetType().InvokeMember("TargetPath",BindingFlags.SetProperty,null,shortcut,new object[]{Path.Combine(InstallDirectory,"StadiaLink.exe")});
                shortcut.GetType().InvokeMember("WorkingDirectory",BindingFlags.SetProperty,null,shortcut,new object[]{InstallDirectory});
                shortcut.GetType().InvokeMember("Description",BindingFlags.SetProperty,null,shortcut,new object[]{I18n.T("Configura tu Stadia en Windows")});
                shortcut.GetType().InvokeMember("Save",BindingFlags.InvokeMethod,null,shortcut,null);
            }finally{if(shortcut!=null)Marshal.FinalReleaseComObject(shortcut);Marshal.FinalReleaseComObject(shell);}
            using(RegistryKey key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\StadiaStudio")) {
                key.SetValue("DisplayName","StadiaLink");key.SetValue("DisplayVersion","1.0.1");key.SetValue("Publisher","StadiaLink · WinStadia GPLv3");
                key.SetValue("InstallLocation",InstallDirectory);key.SetValue("DisplayIcon",Path.Combine(InstallDirectory,"StadiaLink.exe"));
                key.SetValue("UninstallString","\""+Path.Combine(InstallDirectory,"StadiaLink.Setup.exe")+"\" /uninstall");
                key.SetValue("NoModify",1,RegistryValueKind.DWord);key.SetValue("NoRepair",1,RegistryValueKind.DWord);
                long bytes=Directory.GetFiles(InstallDirectory,"*",SearchOption.AllDirectories).Sum(f=>new FileInfo(f).Length);key.SetValue("EstimatedSize",(int)(bytes/1024),RegistryValueKind.DWord);
            }
        }
        public static bool StartWithWindows {get{using(RegistryKey key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))return key!=null&&key.GetValue("StadiaStudio")!=null;}}
        public static void SaveStartup(bool enabled) {
            string executable=Path.Combine(InstallDirectory,"StadiaLink.exe");
            if(enabled&&!File.Exists(executable))throw new FileNotFoundException("Instala StadiaLink para activar el inicio con Windows.");
            using(RegistryKey key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) {
                if(enabled)key.SetValue("StadiaStudio","\""+executable+"\" --tray",RegistryValueKind.String);else key.DeleteValue("StadiaStudio",false);
            }
            if(StartWithWindows!=enabled)throw new IOException("No se pudo guardar el inicio con Windows.");
        }
        public static bool Notifications {get{using(RegistryKey key=Registry.CurrentUser.OpenSubKey(@"Software\StadiaStudio"))return key==null||Convert.ToInt32(key.GetValue("Notifications",1))!=0;}}
        public static void SavePreferences(bool startup,bool notifications){using(RegistryKey key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")){if(startup)key.SetValue("StadiaStudio","\""+Path.Combine(InstallDirectory,"StadiaLink.exe")+"\" --tray");else key.DeleteValue("StadiaStudio",false);}using(RegistryKey key=Registry.CurrentUser.CreateSubKey(@"Software\StadiaStudio"))key.SetValue("Notifications",notifications?1:0,RegistryValueKind.DWord);}
        public static void UnregisterApp() {using(RegistryKey key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run",true)){if(key!=null)key.DeleteValue("StadiaStudio",false);}if(File.Exists(Shortcut))File.Delete(Shortcut);Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\StadiaStudio",false);}
    }
}



