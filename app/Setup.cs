using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;

[assembly:AssemblyTitle("Instalador de StadiaLink")]
[assembly:AssemblyProduct("StadiaLink")]
[assembly:AssemblyVersion("1.0.0.0")]
[assembly:AssemblyFileVersion("1.0.0.0")]

namespace StadiaStudio {
    public static class SetupEntry {
        [STAThread] public static int Main(string[] args) {
            try {
                if(args.Length==2 && args[0]=="/extract") {Extract(args[1]);return 0;}
                Application application=new Application();SetupWindow window=new SetupWindow(args.Length==1&&args[0]=="/uninstall");
                if(args.Length==1&&args[0]=="/install-test") window.Loaded+=async(s,e)=>{try{await window.Install();}catch(Exception ex){Log(ex.ToString());Environment.ExitCode=1;}finally{window.Close();}};
                if(args.Length==2&&args[0]=="/smoke")window.Loaded+=(s,e)=>{DispatcherTimer t=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(500)};t.Tick+=(a,b)=>{t.Stop();window.Snapshot(args[1]);window.Close();};t.Start();};
                application.Run(window);return Environment.ExitCode;
            }catch(Exception ex){Log(ex.ToString());if(args.Length==0)MessageBox.Show(ex.Message,"StadiaLink");return 1;}
        }
        public static void Log(string text){string path=Path.Combine(ProfileLibrary.DirectoryPath,"logs");Directory.CreateDirectory(path);File.AppendAllText(Path.Combine(path,"setup.log"),DateTime.Now.ToString("s")+" "+text+Environment.NewLine);}
        public static void Extract(string directory) {
            byte[] bytes;using(Stream stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip")) {if(stream==null)throw new InvalidDataException("No se encuentra el paquete de instalación.");using(MemoryStream m=new MemoryStream()){stream.CopyTo(m);bytes=m.ToArray();}}
            using(SHA256 hash=SHA256.Create()){string actual=BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","");if(actual!=PayloadInfo.Sha256)throw new InvalidDataException("El paquete de instalación está dañado.");}
            string root=Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;Directory.CreateDirectory(root);
            using(MemoryStream m=new MemoryStream(bytes))using(ZipArchive archive=new ZipArchive(m,ZipArchiveMode.Read))foreach(ZipArchiveEntry entry in archive.Entries) {
                string path=Path.GetFullPath(Path.Combine(root,entry.FullName));if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Ruta no válida en el instalador.");
                if(String.IsNullOrEmpty(entry.Name)){Directory.CreateDirectory(path);continue;}Directory.CreateDirectory(Path.GetDirectoryName(path));using(Stream input=entry.Open())using(FileStream output=new FileStream(path,FileMode.Create,FileAccess.Write))input.CopyTo(output);
            }
        }
    }
    public sealed class SetupWindow:Window {
        readonly TextBlock state=Ui.T("Todo preparado para instalar.",13,Ui.Muted);readonly ProgressBar progress=new ProgressBar{Height=4,Maximum=100,Foreground=Ui.Lime,Background=Ui.Line,BorderThickness=new Thickness(0)};
        readonly Button install;readonly CheckBox removeDriver=new CheckBox{Content="Retirar también el controlador del mando",IsChecked=false};readonly bool uninstall;bool working,complete;
        public SetupWindow(bool remove) {
            uninstall=remove;Title=remove?"Desinstalar StadiaLink":"Instalar StadiaLink";Width=700;Height=670;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterScreen;Background=Ui.Background;Foreground=Ui.Text;FontFamily=new FontFamily("Segoe UI");WindowStyle=WindowStyle.None;
            WindowChrome.SetWindowChrome(this,new WindowChrome{CaptionHeight=0,CornerRadius=new CornerRadius(16),GlassFrameThickness=new Thickness(0),ResizeBorderThickness=new Thickness(0)});Ui.Styles(Resources);
            Grid layout=new Grid{Margin=new Thickness(38,22,38,32)};layout.RowDefinitions.Add(new RowDefinition{Height=new GridLength(50)});layout.RowDefinitions.Add(new RowDefinition());layout.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            Grid top=new Grid();top.MouseLeftButtonDown+=(s,e)=>{if(e.LeftButton==System.Windows.Input.MouseButtonState.Pressed)DragMove();};TextBlock brand=Ui.T("STADIA STUDIO",15,Ui.Text,true);brand.VerticalAlignment=VerticalAlignment.Center;top.Children.Add(brand);Button close=Ui.Button("×",()=>{if(!working)Close();});close.HorizontalAlignment=HorizontalAlignment.Right;close.Width=36;close.Height=30;close.MinHeight=30;close.Padding=new Thickness(0);close.Background=Ui.Background;close.BorderThickness=new Thickness(0);top.Children.Add(close);layout.Children.Add(top);
            StackPanel body=new StackPanel();body.Children.Add(Ui.Logo(62));body.Children.Add(Ui.Gap(12));body.Children.Add(Ui.T(remove?"Desinstalar StadiaLink":"Instalar StadiaLink",30,null,true));body.Children.Add(Ui.Gap(12));body.Children.Add(Ui.T(remove?"Retira la aplicación de esta cuenta de Windows. Tus perfiles se conservarán.":"Aplicación y controlador para Windows 11 x64.",14,Ui.Muted));body.Children.Add(Ui.Gap(18));
            StackPanel benefits=new StackPanel();benefits.Children.Add(Ui.T(remove?"DESINSTALACIÓN":"INCLUYE",10,Ui.Lime,true));benefits.Children.Add(Ui.Gap(15));
            if(remove){benefits.Children.Add(Ui.T("Se eliminarán Studio y su acceso del menú Inicio.",13));benefits.Children.Add(removeDriver);benefits.Children.Add(Ui.Gap(10));benefits.Children.Add(Ui.T("Retirar el controlador afecta a todos los usuarios del equipo y requiere permiso de administrador.",12,Ui.Muted));}
            else{benefits.Children.Add(Ui.T("✓  Detección automática por Bluetooth y USB",14));benefits.Children.Add(Ui.Gap(12));benefits.Children.Add(Ui.T("✓  Botones, sticks, gatillos y vibración",14));benefits.Children.Add(Ui.Gap(12));benefits.Children.Add(Ui.T("✓  Perfiles y acciones de Windows",14));benefits.Children.Add(Ui.Gap(12));benefits.Children.Add(Ui.T("✓  Instalación sin descargas",14));}
            body.Children.Add(Ui.Card(benefits,22));body.Children.Add(Ui.Gap(20));body.Children.Add(Ui.T(remove?"Los ajustes Bluetooth de Windows no se modificarán.":"Windows 11 · x64. Windows pedirá permiso de administrador para instalar el controlador y su certificado local.",12,Ui.Muted));Grid.SetRow(body,1);layout.Children.Add(body);
            StackPanel bottom=new StackPanel();bottom.Children.Add(state);bottom.Children.Add(Ui.Gap(12));bottom.Children.Add(progress);bottom.Children.Add(Ui.Gap(18));Grid row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});Button license=Ui.Button("Licencia GPLv3",ShowLicense);license.BorderThickness=new Thickness(0);license.Background=Ui.Background;license.HorizontalAlignment=HorizontalAlignment.Left;row.Children.Add(license);
            install=Ui.Button(remove?"Desinstalar":"Instalar StadiaLink",async()=>{if(complete){if(!uninstall)Process.Start(Path.Combine(InstallerActions.InstallDirectory,"StadiaLink.exe"));Close();return;}try{await Install();}catch(Exception e){state.Text=I18n.T(e.Message);state.Foreground=Ui.Coral;SetupEntry.Log(e.ToString());install.Content=I18n.T("Reintentar");}},true);Grid.SetColumn(install,1);row.Children.Add(install);bottom.Children.Add(row);Grid.SetRow(bottom,2);layout.Children.Add(bottom);Content=layout;I18n.TranslateTree(layout);Closing+=(s,e)=>{if(working)e.Cancel=true;};
        }
        void CheckRequirements(){if(!Environment.Is64BitOperatingSystem)throw new InvalidOperationException("Se necesita Windows x64.");using(RegistryKey key=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion")){int build;if(key==null||!Int32.TryParse(Convert.ToString(key.GetValue("CurrentBuildNumber")),out build)||build<22000)throw new InvalidOperationException("Se necesita Windows 11.");}}
        public async Task Install() {
            working=true;install.IsEnabled=false;try {
                CheckRequirements();string root=InstallerActions.InstallDirectory;
                if(Process.GetProcessesByName("StadiaStudio").Length>0||Process.GetProcessesByName("StadiaLink").Length>0)throw new InvalidOperationException("Cierra StadiaLink antes de continuar.");
                if(uninstall){
                    state.Text=I18n.T("Retirando StadiaLink…");
                    if(removeDriver.IsChecked==true){state.Text=I18n.T("Retirando el controlador…");await InstallerActions.RunDriver(Path.Combine(root,"driver","install.ps1"),true);}
                    InstallerActions.UnregisterApp();progress.Value=80;
                    // The running uninstaller stays until it exits. The next
                    // installation replaces it; other application files go now.
                    string expected=Path.GetFullPath(root).TrimEnd('\\')+"\\";
                    if(!Directory.Exists(root))throw new DirectoryNotFoundException("La aplicación ya no está instalada.");
                    string running=Path.GetFullPath(Assembly.GetExecutingAssembly().Location);
                    await Task.Run(()=>{foreach(string file in Directory.GetFiles(root,"*",SearchOption.AllDirectories)){string full=Path.GetFullPath(file);if(!full.StartsWith(expected,StringComparison.OrdinalIgnoreCase))throw new IOException("Ruta de desinstalación no válida.");if(full.Equals(running,StringComparison.OrdinalIgnoreCase))continue;File.Delete(full);}});
                    state.Text=I18n.T("Aplicación desinstalada. Tus perfiles se han conservado.");install.Content=I18n.T("Cerrar");
                }else {
                    state.Text=I18n.T("Preparando la aplicación…");progress.Value=15;await Task.Run(()=>SetupEntry.Extract(root));
                    string running=Assembly.GetExecutingAssembly().Location,target=Path.Combine(root,"StadiaLink.Setup.exe");if(!Path.GetFullPath(running).Equals(Path.GetFullPath(target),StringComparison.OrdinalIgnoreCase))File.Copy(running,target,true);
                    progress.Value=40;state.Text=I18n.T("Instalando el controlador. Acepta el permiso de Windows…");await InstallerActions.RunDriver(Path.Combine(root,"driver","install.ps1"),false);
                    progress.Value=80;state.Text=I18n.T("Creando el acceso del menú Inicio…");InstallerActions.RegisterApp();
                    string log=Path.Combine(ProfileLibrary.DirectoryPath,"logs","driver-install.log");bool restart=File.Exists(log)&&File.ReadAllText(log).Contains("pnputil exit code: 3010");
                    state.Text=I18n.T(restart?"Instalado. Si el mando no aparece, reconéctalo o reinicia Windows.":"Instalado. Enciende tu Stadia y abre StadiaLink.");install.Content=I18n.T("Abrir StadiaLink");
                }
                progress.Value=100;complete=true;SetupEntry.Log("Installation operation completed: "+(uninstall?"uninstall":"install"));
            }finally{working=false;install.IsEnabled=true;}
        }
        void ShowLicense(){using(Stream stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("LICENSE")){string text;using(StreamReader reader=new StreamReader(stream))text=reader.ReadToEnd();Window dialog=new Window{Title=I18n.T("Licencia GPLv3"),Width=750,Height=550,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Ui.Background};dialog.Content=new TextBox{Text=text,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(20)};dialog.ShowDialog();}}
        public void Snapshot(string path){UpdateLayout();var image=new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(this);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));using(FileStream f=File.Create(path))encoder.Save(f);}
    }
}




