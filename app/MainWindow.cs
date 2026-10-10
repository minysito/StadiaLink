using System;
using System.IO;
using Path=System.IO.Path;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows.Shell;
using Microsoft.Win32;

namespace StadiaStudio {
    public sealed class MainWindow:Window {
        ProfileLibrary library;Profile draft;string originalName;bool dirty,loading,pollBusy,closed,pendingApply;
        volatile Controller pad;PadStatus status;List<PadInfo> available=new List<PadInfo>();
        volatile bool stopSampling;volatile Controller sampledDevice,failedDevice;volatile PadStatus latestSample;
        readonly ConcurrentQueue<Tuple<Controller,PadStatus>> inputSamples=new ConcurrentQueue<Tuple<Controller,PadStatus>>();
        CancellationTokenSource vibrationCancel;Task vibrationLoop=Task.CompletedTask;
        readonly object timingGate=new object();readonly List<double> readTimes=new List<double>(),sampleAges=new List<double>(),readIntervals=new List<double>();long previousReadTicks;
        long sampleCount,frameCount;readonly Stopwatch performance=Stopwatch.StartNew();
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr CreateWaitableTimerExW(IntPtr attributes,string name,uint flags,uint access);
        [DllImport("kernel32.dll")] static extern bool SetWaitableTimer(IntPtr timer,ref long due,int period,IntPtr callback,IntPtr arg,bool resume);
        [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        readonly DispatcherTimer timer=new DispatcherTimer();DateTime nextDiscovery=DateTime.MinValue;
        readonly Grid page=new Grid();readonly TextBlock footer=Ui.T("Buscando tu Stadia…",12,Ui.Muted);
        readonly TextBlock connection=Ui.T("BUSCANDO MANDO",10,Ui.Muted,true),deviceText=Ui.T("Sin conexión",12,Ui.Muted);
        readonly ComboBox profilePicker=new ComboBox{Width=170},devicePicker=new ComboBox{MinWidth=210};
        readonly Dictionary<string,Button> navigation=new Dictionary<string,Button>();
        readonly List<Action<PadStatus>> live=new List<Action<PadStatus>>();
        Button apply;ControllerVisual visual;ComboBox sourcePicker,destinationPicker;TextBlock selectedText,selectedHint,saveState;
        int selectedButton=0;string route="Mando";Profile loadedHardware;
        readonly DesktopActions desktopActions=new DesktopActions();Profile activeActions;
        BatteryTray tray;int? batteryLevel;DateTime nextBattery=DateTime.MinValue;bool batteryBusy;
        readonly TextBlock batteryText=Ui.T("Batería: —",12,Ui.Muted);
        ComboBox actionPicker,keyPicker;StackPanel shortcutOptions;TextBlock actionHint;
        readonly Dictionary<int,CheckBox> modifierChecks=new Dictionary<int,CheckBox>();
        bool selectedOnDrawing;
        readonly DeviceNotifications notices=new DeviceNotifications();
        bool draftStartup=InstallerActions.StartWithWindows,draftNotifications=InstallerActions.Notifications,notificationsEnabled=InstallerActions.Notifications,preferencesDirty;
        public bool SuppressNotifications;
        string draftLanguage=I18n.Saved;
        ComboBox languagePicker;
        public MainWindow() {
            Title="StadiaLink";Width=1340;Height=850;MinWidth=820;MinHeight=580;WindowStartupLocation=WindowStartupLocation.CenterScreen;
            Background=Ui.Background;Foreground=Ui.Text;FontFamily=new FontFamily("Segoe UI");WindowStyle=WindowStyle.None;
            WindowChrome.SetWindowChrome(this,new WindowChrome{CaptionHeight=0,CornerRadius=new CornerRadius(16),GlassFrameThickness=new Thickness(0),ResizeBorderThickness=new Thickness(6)});
            Ui.Styles(Resources);
            try{library=ProfileLibrary.Load();}catch(Exception e){library=ProfileLibrary.Defaults();I18n.SetText(footer,"No se pudo cargar la biblioteca: "+I18n.Error(e));}
            Profile chosen=library.Profiles.FirstOrDefault(p=>p.Name==library.Selected)??library.Profiles[0];draft=chosen.Clone();originalName=chosen.Name;
            Grid shell=new Grid();shell.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(208)});shell.ColumnDefinitions.Add(new ColumnDefinition());
            Border sidebar=new Border{Background=Ui.Inset,BorderBrush=Ui.Line,BorderThickness=new Thickness(0,0,1,0),Padding=new Thickness(20,26,20,20)};
            Grid side=new Grid();side.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});side.RowDefinitions.Add(new RowDefinition());side.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            StackPanel branding=new StackPanel();branding.Children.Add(Ui.Logo(42));branding.Children.Add(Ui.Gap(10));branding.Children.Add(Ui.T("STADIA",21,null,true));branding.Children.Add(Ui.T("LINK",10,Ui.Muted,true));branding.Children.Add(Ui.Gap(35));side.Children.Add(branding);
            StackPanel nav=new StackPanel();Grid.SetRow(nav,1);side.Children.Add(nav);
            foreach(string name in new[]{"Mando","Sticks y gatillos","Vibración","Perfiles","Ajustes"}) {
                string target=name;Button b=Ui.Button(name,()=>Navigate(target,true));b.HorizontalContentAlignment=HorizontalAlignment.Left;b.Margin=new Thickness(0,0,0,8);b.BorderThickness=new Thickness(0);
                navigation[name]=b;nav.Children.Add(b);
            }
            StackPanel sideFoot=new StackPanel();sideFoot.Children.Add(connection);sideFoot.Children.Add(Ui.Gap(8));sideFoot.Children.Add(deviceText);sideFoot.Children.Add(Ui.Gap(8));sideFoot.Children.Add(batteryText);sideFoot.Children.Add(Ui.Gap(20));sideFoot.Children.Add(Ui.T("StadiaLink 1.0.1",10,Ui.Muted));Grid.SetRow(sideFoot,2);side.Children.Add(sideFoot);sidebar.Child=side;shell.Children.Add(sidebar);
            Grid main=new Grid{Margin=new Thickness(30,0,30,0)};Grid.SetColumn(main,1);shell.Children.Add(main);
            main.RowDefinitions.Add(new RowDefinition{Height=new GridLength(34)});main.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});main.RowDefinitions.Add(new RowDefinition());main.RowDefinitions.Add(new RowDefinition{Height=new GridLength(30)});
            Grid chrome=new Grid();chrome.MouseLeftButtonDown+=(s,e)=>{if(e.ClickCount==2)WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;else if(e.LeftButton==MouseButtonState.Pressed)DragMove();};
            chrome.Children.Add(Ui.T("CONFIGURACIÓN DEL MANDO",10,Ui.Muted,true));((TextBlock)chrome.Children[0]).VerticalAlignment=VerticalAlignment.Center;
            StackPanel windowButtons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
            windowButtons.Children.Add(WindowButton("—",()=>WindowState=WindowState.Minimized));windowButtons.Children.Add(WindowButton("□",()=>WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized));windowButtons.Children.Add(WindowButton("×",Close));chrome.Children.Add(windowButtons);main.Children.Add(chrome);
            Grid header=new Grid{Margin=new Thickness(0,0,0,12)};header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            StackPanel titles=new StackPanel();TextBlock title=Ui.T("",30,null,true);title.Name="PageTitle";titles.Children.Add(title);header.Children.Add(titles);
            StackPanel actions=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(actions,1);header.Children.Add(actions);
            profilePicker.Margin=new Thickness(0,0,10,0);I18n.Tooltip(profilePicker,"Perfil activo");FillProfiles();profilePicker.SelectionChanged+=(s,e)=>{if(loading)return;SwitchProfile(profilePicker.SelectedIndex<0?null:library.Profiles[profilePicker.SelectedIndex].Name);};actions.Children.Add(profilePicker);
            apply=Ui.Button("Guardar y aplicar",()=>Run(ApplyDraft),true);actions.Children.Add(apply);Grid.SetRow(header,1);main.Children.Add(header);
            Grid.SetRow(page,2);main.Children.Add(page);footer.VerticalAlignment=VerticalAlignment.Center;footer.TextTrimming=TextTrimming.CharacterEllipsis;Grid.SetRow(footer,3);main.Children.Add(footer);
            SizeChanged+=(s,e)=>{bool compact=ActualWidth<1100;shell.ColumnDefinitions[0].Width=new GridLength(compact?160:208);sidebar.Padding=new Thickness(compact?12:20,18,compact?12:20,14);main.Margin=new Thickness(compact?16:24,0,compact?16:24,0);};
            Content=shell;Navigate("Mando");timer.Interval=TimeSpan.FromMilliseconds(8);timer.Tick+=(s,e)=>{ConsumeInput();Poll();};Loaded+=(s,e)=>{tray=new BatteryTray(RestoreFromTray,Close);Task.Factory.StartNew(SampleInput,CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);CompositionTarget.Rendering+=RenderFrame;timer.Start();Poll();};
            StateChanged+=(s,e)=>{if(WindowState==WindowState.Minimized){Hide();ShowInTaskbar=false;}};
            Closed+=(s,e)=>{closed=true;stopSampling=true;if(vibrationCancel!=null)vibrationCancel.Cancel();CompositionTarget.Rendering-=RenderFrame;timer.Stop();desktopActions.Dispose();if(tray!=null)tray.Dispose();if(pad!=null)pad.Dispose();};
        }
        void RestoreFromTray(){ShowInTaskbar=true;Show();WindowState=WindowState.Normal;Activate();}
        Button WindowButton(string text,Action click){Button b=Ui.Button(text,click);b.MinHeight=30;b.Width=40;b.Padding=new Thickness(4);b.Margin=new Thickness(4,2,0,2);b.BorderThickness=new Thickness(0);b.Background=Ui.Background;return b;}
        void FillProfiles(){loading=true;profilePicker.ItemsSource=library.Profiles.Select(p=>I18n.ProfileName(p.Name)).ToArray();profilePicker.SelectedIndex=library.Profiles.FindIndex(p=>p.Name==originalName);loading=false;}
        void SwitchProfile(string name) {
            Profile chosen=library.Profiles.FirstOrDefault(p=>p.Name==name);if(chosen==null)return;
            draftStartup=InstallerActions.StartWithWindows;draftNotifications=notificationsEnabled;preferencesDirty=false;draft=chosen.Clone();originalName=chosen.Name;dirty=false;pendingApply=false;Navigate(route);I18n.SetText(footer,"Perfil seleccionado");
        }
        void MarkDirty(){if(loading)return;dirty=true;if(saveState!=null)I18n.SetText(saveState,"Cambios sin aplicar");I18n.SetText(footer,"Sin guardar");}
        void Navigate(string name,bool discard=false) {
            if(discard&&name==route)return;
            if(discard&&name!=route&&dirty){Profile saved=library.Profiles.FirstOrDefault(p=>p.Name==originalName);if(saved!=null)draft=saved.Clone();draftStartup=InstallerActions.StartWithWindows;draftNotifications=notificationsEnabled;preferencesDirty=false;dirty=false;I18n.SetText(footer,"Cambios descartados.");}
            if(!preferencesDirty&&I18n.Code!=I18n.Saved){draftLanguage=I18n.Saved;I18n.Set(draftLanguage);FillProfiles();I18n.TranslateTree(Content as DependencyObject);}
            if(name!=route&&vibrationCancel!=null)vibrationCancel.Cancel();
            route=name;live.Clear();page.Children.Clear();visual=null;saveState=null;
            foreach(KeyValuePair<string,Button> p in navigation){p.Value.Background=p.Key==name?Ui.Brush("#2C3A2D"):Ui.Inset;p.Value.Foreground=p.Key==name?Ui.Lime:Ui.Muted;}
            if(name=="Mando")BuildController();else if(name=="Sticks y gatillos")BuildSticks();else if(name=="Vibración")BuildVibration();else if(name=="Perfiles")BuildProfiles();else BuildApplication();
            I18n.TranslateTree(page);Ui.Enter(page);UpdateLive();
        }
        Grid Columns(double rightWidth) {
            Grid g=new Grid();g.ColumnDefinitions.Add(new ColumnDefinition());g.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(18)});g.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(rightWidth)});g.SizeChanged+=(s,e)=>{g.ColumnDefinitions[2].Width=new GridLength(Math.Min(rightWidth,Math.Max(240,g.ActualWidth*.34)));g.ColumnDefinitions[1].Width=new GridLength(g.ActualWidth<650?12:18);};return g;
        }
        StackPanel Section(string eyebrow,string title,string detail) {
            StackPanel s=new StackPanel();if(!String.IsNullOrEmpty(eyebrow)&&eyebrow!="LINK"&&!eyebrow.Equals(title,StringComparison.OrdinalIgnoreCase)){s.Children.Add(Ui.T(eyebrow,10,Ui.Lime,true));s.Children.Add(Ui.Gap(10));}s.Children.Add(Ui.T(title,23,null,true));s.Children.Add(Ui.Gap(7));if(!String.IsNullOrEmpty(detail))s.Children.Add(Ui.T(detail,12,Ui.Muted));s.Children.Add(Ui.Gap(20));return s;
        }
        void BuildController() {
            Grid layout=Columns(282);ScrollViewer controllerScroll=new ScrollViewer{Content=layout,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};controllerScroll.SizeChanged+=(s,e)=>{layout.Height=Math.Max(470,controllerScroll.ActualHeight);};page.Children.Add(controllerScroll);
            Grid left=new Grid{MinHeight=360};left.RowDefinitions.Add(new RowDefinition());left.RowDefinitions.Add(new RowDefinition{Height=new GridLength(122)});layout.Children.Add(left);
            Grid hero=new Grid();hero.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});hero.RowDefinitions.Add(new RowDefinition());hero.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            Grid heroTop=new Grid();heroTop.ColumnDefinitions.Add(new ColumnDefinition());heroTop.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            StackPanel heading=new StackPanel();heading.Children.Add(Ui.T("MANDO",10,Ui.Muted,true));heading.Children.Add(Ui.Gap(8));heading.Children.Add(Ui.T("Stadia Controller",23,null,true));heroTop.Children.Add(heading);
            TextBlock chip=Ui.T("●  BUSCANDO",10,Ui.Muted,true);chip.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(chip,1);heroTop.Children.Add(chip);hero.Children.Add(heroTop);
            live.Add(s=>{I18n.SetText(chip,s==null?"○  SIN CONEXIÓN":s.Bluetooth?"●  BLUETOOTH":"●  USB");chip.Foreground=s==null?Ui.Muted:Ui.Lime;});
            visual=new ControllerVisual{Margin=new Thickness(0,24,0,12)};visual.Selected+=SelectButton;if(selectedOnDrawing)visual.Select(selectedButton);Grid.SetRow(visual,1);hero.Children.Add(visual);
            TextBlock hint=Ui.T("Pulsa un botón del dibujo para cambiar su función.",12,Ui.Muted);hint.HorizontalAlignment=HorizontalAlignment.Center;Grid.SetRow(hint,2);hero.Children.Add(hint);left.Children.Add(Ui.Card(hero));
            Grid meters=new Grid{Margin=new Thickness(0,16,0,0)};meters.ColumnDefinitions.Add(new ColumnDefinition());meters.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(12)});meters.ColumnDefinitions.Add(new ColumnDefinition());Grid.SetRow(meters,1);left.Children.Add(meters);
            StackPanel leftMeter=new StackPanel(),rightMeter=new StackPanel();leftMeter.Children.Add(Ui.T("GATILLO IZQUIERDO",10,Ui.Muted,true));rightMeter.Children.Add(Ui.T("GATILLO DERECHO",10,Ui.Muted,true));
            AddTriggerMeter(leftMeter,8);AddTriggerMeter(rightMeter,9);meters.Children.Add(Ui.Card(leftMeter,17));Border rm=Ui.Card(rightMeter,17);Grid.SetColumn(rm,2);meters.Children.Add(rm);
            StackPanel details=Section("ASIGNACIÓN","Asignación","Selecciona un botón y su función.");
            details.Children.Add(Ui.T("BOTÓN FÍSICO",10,Ui.Muted,true));details.Children.Add(Ui.Gap(8));sourcePicker=new ComboBox{ItemsSource=Destination.Physical.Select(I18n.T).ToArray(),SelectedIndex=selectedButton};sourcePicker.SelectionChanged+=(s,e)=>{if(sourcePicker.SelectedIndex>=0)SelectButton(sourcePicker.SelectedIndex);};details.Children.Add(sourcePicker);
            details.Children.Add(Ui.Gap(22));selectedText=Ui.T("",18,Ui.Lime,true);details.Children.Add(selectedText);details.Children.Add(Ui.Gap(10));details.Children.Add(Ui.T("FUNCIÓN",10,Ui.Muted,true));details.Children.Add(Ui.Gap(8));
            draft.EnsureActions();actionPicker=new ComboBox{ItemsSource=ExtraAction.All};actionPicker.SelectionChanged+=(s,e)=>{if(loading)return;ExtraAction a=actionPicker.SelectedItem as ExtraAction;if(a==null)return;draft.Actions[selectedButton]=a.Id;if(a.Id!=0){draft.Map[selectedButton]=0;if(a.Id>=13&&!Profile.KeySupported(draft.Keys[selectedButton]))draft.Keys[selectedButton]=65;}RefreshSelected();MarkDirty();};details.Children.Add(actionPicker);details.Children.Add(Ui.Gap(10));
            destinationPicker=new ComboBox{ItemsSource=Destination.All};destinationPicker.SelectionChanged+=(s,e)=>{if(loading)return;Destination d=destinationPicker.SelectedItem as Destination;if(d!=null){draft.Map[selectedButton]=d.Id;RefreshSelected();MarkDirty();}};details.Children.Add(destinationPicker);
            shortcutOptions=new StackPanel{Margin=new Thickness(0,10,0,0)};keyPicker=new ComboBox{ItemsSource=KeyOption.All};keyPicker.SelectionChanged+=(s,e)=>{if(loading)return;KeyOption k=keyPicker.SelectedItem as KeyOption;if(k!=null){draft.Keys[selectedButton]=k.Code;MarkDirty();}};shortcutOptions.Children.Add(keyPicker);
            WrapPanel modifiers=new WrapPanel();modifierChecks.Clear();foreach(var pair in new[]{new[]{1,0},new[]{2,1},new[]{4,2},new[]{8,3}}){int flag=pair[0];string label=new[]{"Ctrl","Alt","Shift","Win"}[pair[1]];CheckBox c=new CheckBox{Content=label,Margin=new Thickness(0,10,10,0)};c.Checked+=(s,e)=>{if(!loading){draft.Modifiers[selectedButton]|=flag;MarkDirty();}};c.Unchecked+=(s,e)=>{if(!loading){draft.Modifiers[selectedButton]&=~flag;MarkDirty();}};modifiers.Children.Add(c);modifierChecks[flag]=c;}shortcutOptions.Children.Add(modifiers);details.Children.Add(shortcutOptions);
            actionHint=Ui.T("StadiaLink debe estar abierto para las acciones de Windows.",11,Ui.Muted);actionHint.Margin=new Thickness(0,10,0,0);details.Children.Add(actionHint);
            details.Children.Add(Ui.Gap(16));selectedHint=Ui.T("",12,Ui.Muted);details.Children.Add(selectedHint);details.Children.Add(Ui.Gap(24));details.Children.Add(new Border{Height=1,Background=Ui.Line});details.Children.Add(Ui.Gap(20));
            
            
            Border card=Ui.Card(new ScrollViewer{Content=details},22);Grid.SetColumn(card,2);layout.Children.Add(card);RefreshSelected();
        }
        void AddTriggerMeter(StackPanel panel,int index) {
            Grid row=new Grid{Margin=new Thickness(0,10,0,6)};TextBlock label=Ui.T(index==8?"L2":"R2",18,null,true),value=Ui.T("0 %",18,Ui.Lime,true);value.HorizontalAlignment=HorizontalAlignment.Right;row.Children.Add(label);row.Children.Add(value);panel.Children.Add(row);
            ProgressBar bar=new ProgressBar{Minimum=0,Maximum=255,Height=4,Foreground=Ui.Lime,Background=Ui.Line,BorderThickness=new Thickness(0)};panel.Children.Add(bar);
            live.Add(s=>{int v=s==null?0:s.Raw[index];I18n.SetText(value,(v*100/255)+" %");bar.Value=v;});
        }
        void AddTravelPreview(StackPanel panel,int side) {
            Grid heading=new Grid();heading.Children.Add(Ui.T(side==0?"L2":"R2",18,null,true));TextBlock percent=Ui.T("0 %",18,Ui.Lime,true);percent.HorizontalAlignment=HorizontalAlignment.Right;heading.Children.Add(percent);panel.Children.Add(heading);panel.Children.Add(Ui.Gap(12));
            Grid track=new Grid{Height=26,ClipToBounds=true,Background=Ui.Inset};Border output=new Border{Background=Ui.Lime,HorizontalAlignment=HorizontalAlignment.Left,Height=8,VerticalAlignment=VerticalAlignment.Center,CornerRadius=new CornerRadius(4)};Border dead=new Border{Background=Ui.Coral,Opacity=.25,HorizontalAlignment=HorizontalAlignment.Left};Border threshold=new Border{Background=Ui.Coral,Width=2,HorizontalAlignment=HorizontalAlignment.Left};Border input=new Border{Background=Ui.Text,Width=2,HorizontalAlignment=HorizontalAlignment.Left};track.Children.Add(output);track.Children.Add(dead);track.Children.Add(threshold);track.Children.Add(input);track.SizeChanged+=(s,e)=>UpdateLive();panel.Children.Add(track);
            Grid scale=new Grid{Margin=new Thickness(0,6,0,10)};scale.Children.Add(Ui.T("0",10,Ui.Muted));TextBlock mid=Ui.T("50",10,Ui.Muted);mid.HorizontalAlignment=HorizontalAlignment.Center;scale.Children.Add(mid);TextBlock end=Ui.T("100 %",10,Ui.Muted);end.HorizontalAlignment=HorizontalAlignment.Right;scale.Children.Add(end);panel.Children.Add(scale);
            TextBlock values=Ui.T("",11,Ui.Muted);panel.Children.Add(values);panel.Children.Add(Ui.Gap(6));panel.Children.Add(Ui.T("Umbral",10,Ui.Coral));
            live.Add(state=>{int source=(draft.Flags&32)!=0?1-side:side;int raw=state==null?0:state.Raw[8+source],dz=255*draft.TriggerDeadzone/100;double result=raw<=dz?0:(raw-dz)/(255.0-dz);double width=track.ActualWidth;output.Width=result*width;dead.Width=draft.TriggerDeadzone*width/100;threshold.Margin=new Thickness(Math.Max(0,draft.TriggerThreshold*width/100-1),0,0,0);input.Margin=new Thickness(Math.Max(0,raw*width/255-1),0,0,0);I18n.SetText(percent,Math.Round(result*100)+" %");I18n.SetText(values,"Entrada "+Math.Round(raw*100/255.0)+" %  ·  Salida "+Math.Round(result*100)+" %");});
        }
        void SelectButton(int id){if(id<0 || id>=19)return;selectedButton=id;selectedOnDrawing=true;if(visual!=null)visual.Select(id);if(sourcePicker!=null && sourcePicker.SelectedIndex!=id)sourcePicker.SelectedIndex=id;RefreshSelected();}
        void RefreshSelected() {
            if(route!="Mando" || selectedText==null || destinationPicker==null)return;loading=true;
            draft.EnsureActions();int action=draft.Actions[selectedButton];I18n.SetText(selectedText,Destination.Physical[selectedButton]+"  →  "+(action==0?Destination.Label(draft.Map[selectedButton]):ExtraAction.All.First(a=>a.Id==action).Name));destinationPicker.SelectedItem=Destination.All.First(d=>d.Id==draft.Map[selectedButton]);destinationPicker.Visibility=action==0?Visibility.Visible:Visibility.Collapsed;
            actionPicker.SelectedItem=ExtraAction.All.First(a=>a.Id==action);shortcutOptions.Visibility=action>=13?Visibility.Visible:Visibility.Collapsed;actionHint.Visibility=action==0?Visibility.Collapsed:Visibility.Visible;
            keyPicker.SelectedItem=((KeyOption[])keyPicker.ItemsSource).FirstOrDefault(k=>k.Code==draft.Keys[selectedButton]);foreach(var pair in modifierChecks)pair.Value.IsChecked=(draft.Modifiers[selectedButton]&pair.Key)!=0;
            I18n.SetText(selectedHint,selectedButton>=17?"Al asignarlo a un botón, se activa al superar el umbral de gatillo configurado.":"Puedes repetir una función en varios botones o dejar una entrada sin asignar.");loading=false;
        }
        void SaveStartupSetting(CheckBox toggle,bool enabled) {
            if(loading)return;
            try{InstallerActions.SaveStartup(enabled);draftStartup=enabled;I18n.SetText(footer,enabled?"Inicio con Windows activado":"Inicio con Windows desactivado");}
            catch(Exception ex){loading=true;toggle.IsChecked=InstallerActions.StartWithWindows;loading=false;I18n.SetText(footer,I18n.Error(ex));}
        }
        void BuildSticks() {
            StackPanel content=new StackPanel();Grid two=new Grid();two.ColumnDefinitions.Add(new ColumnDefinition());two.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(18)});two.ColumnDefinitions.Add(new ColumnDefinition());
            two.Children.Add(StickCard(false));Border right=StickCard(true);Grid.SetColumn(right,2);two.Children.Add(right);content.Children.Add(two);content.Children.Add(Ui.Gap(18));
            StackPanel triggers=Section("RECORRIDO ANALÓGICO","Gatillos","");
            SliderField(triggers,"Zona muerta de gatillos",0,40,draft.TriggerDeadzone,v=>draft.TriggerDeadzone=v," %");SliderField(triggers,"Umbral para botones reasignados",1,100,draft.TriggerThreshold,v=>draft.TriggerThreshold=v," %");
            Grid travel=new Grid{Margin=new Thickness(0,0,0,20)};travel.ColumnDefinitions.Add(new ColumnDefinition());travel.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(24)});travel.ColumnDefinitions.Add(new ColumnDefinition());StackPanel lt=new StackPanel(),rt=new StackPanel();AddTravelPreview(lt,0);AddTravelPreview(rt,1);travel.Children.Add(lt);Grid.SetColumn(rt,2);travel.Children.Add(rt);triggers.Children.Insert(5,travel);Check(triggers,"Intercambiar gatillos izquierdo y derecho",32);Check(triggers,"Intercambiar sticks izquierdo y derecho",16);content.Children.Add(Ui.Card(triggers));page.Children.Add(new ScrollViewer{Content=content});
        }
        Border StickCard(bool right) {
            StackPanel s=Section(right?"STICK DERECHO":"STICK IZQUIERDO",right?"Stick derecho":"Stick izquierdo","");
            Canvas scope=new Canvas{Width=130,Height=130,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,0,0,12)};
            scope.Children.Add(new Ellipse{Width=130,Height=130,Stroke=Ui.Line,StrokeThickness=1,Fill=Ui.Inset});
            scope.Children.Add(new Line{X1=65,Y1=0,X2=65,Y2=130,Stroke=Ui.Line});scope.Children.Add(new Line{X1=0,Y1=65,X2=130,Y2=65,Stroke=Ui.Line});Ellipse dot=new Ellipse{Width=10,Height=10,Fill=Ui.Lime};Canvas.SetLeft(dot,60);Canvas.SetTop(dot,60);scope.Children.Add(dot);s.Children.Add(new Viewbox{Width=110,Height=110,Child=scope,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Center});
            Ellipse rawDot=new Ellipse{Width=8,Height=8,Stroke=Ui.Muted,StrokeThickness=1.5};Canvas.SetLeft(rawDot,61);Canvas.SetTop(rawDot,61);scope.Children.Add(rawDot);
            Ellipse deadRing=new Ellipse{Stroke=Ui.Coral,StrokeThickness=1,Opacity=.6,IsHitTestVisible=false};scope.Children.Insert(1,deadRing);
            TranslateTransform motion=new TranslateTransform(),rawMotion=new TranslateTransform();dot.RenderTransform=motion;rawDot.RenderTransform=rawMotion;int axis=right?2:0;
            live.Add(state=>{int source=(draft.Flags&16)!=0?(axis+2)%4:axis;int offset=4+source;double radius=65*(right?draft.RightDeadzone:draft.LeftDeadzone)/100.0;deadRing.Width=deadRing.Height=radius*2;Canvas.SetLeft(deadRing,65-radius);Canvas.SetTop(deadRing,65-radius);
                motion.X=state==null?0:draft.Axis(state.Raw[offset],axis)*59;motion.Y=state==null?0:draft.Axis(state.Raw[offset+1],axis+1)*59;
                rawMotion.X=state==null?0:Profile.AxisValue(state.Raw[offset],0,100,false)*59;rawMotion.Y=state==null?0:Profile.AxisValue(state.Raw[offset+1],0,100,false)*59;});
            s.Children.Add(Ui.T("○ Entrada física   ● Salida configurada",11,Ui.Muted));s.Children.Add(Ui.Gap(14));
            Slider deadzoneSlider=SliderField(s,"Zona muerta",0,40,right?draft.RightDeadzone:draft.LeftDeadzone,v=>{if(right)draft.RightDeadzone=v;else draft.LeftDeadzone=v;}," %");
            Button noDeadzone=Ui.Button("Sin zona muerta",()=>{deadzoneSlider.Value=0;});noDeadzone.MinHeight=30;noDeadzone.Padding=new Thickness(10,4,10,4);s.Children.Add(noDeadzone);s.Children.Add(Ui.Gap(8));
            SliderField(s,"Sensibilidad",50,200,right?draft.RightSensitivity:draft.LeftSensitivity,v=>{if(right)draft.RightSensitivity=v;else draft.LeftSensitivity=v;}," %");Check(s,"Invertir eje horizontal",right?4:1);Check(s,"Invertir eje vertical",right?8:2);return Ui.Card(s);
        }
        Slider SliderField(StackPanel panel,string name,int min,int max,int current,Action<int> set,string suffix) {
            Grid label=new Grid{Margin=new Thickness(0,0,0,8)};label.Children.Add(Ui.T(name,13));TextBlock value=Ui.T(current+suffix,13,Ui.Lime,true);value.HorizontalAlignment=HorizontalAlignment.Right;label.Children.Add(value);panel.Children.Add(label);
            Slider slider=new Slider{Minimum=min,Maximum=max,Value=current,TickFrequency=1,IsSnapToTickEnabled=true,SmallChange=1,LargeChange=5,Margin=new Thickness(0,0,0,18),Foreground=Ui.Lime};
            slider.ValueChanged+=(s,e)=>{int v=(int)Math.Round(slider.Value);I18n.SetText(value,v+suffix);set(v);MarkDirty();UpdateLive();};panel.Children.Add(slider);return slider;
        }
        void Check(StackPanel panel,string name,int flag) {
            CheckBox check=new CheckBox{Content=I18n.T(name),IsChecked=(draft.Flags&flag)!=0};check.Checked+=(s,e)=>{draft.Flags|=flag;MarkDirty();};check.Unchecked+=(s,e)=>{draft.Flags&=~flag;MarkDirty();};I18n.Remember(check,name);panel.Children.Add(check);
        }
        void BuildVibration() {
            StackPanel content=Section("RESPUESTA HÁPTICA","Vibración","");
            Grid two=Columns(350);StackPanel strong=Section("MOTOR IZQUIERDO","Motor fuerte","");SliderField(strong,"Intensidad",0,100,draft.StrongScale,v=>draft.StrongScale=v," %");strong.Children.Add(Ui.Button("Probar motor izquierdo",()=>Run(()=>TestMotor(110,0)),true));two.Children.Add(Ui.Card(strong));
            StackPanel weak=Section("MOTOR DERECHO","Motor suave","");SliderField(weak,"Intensidad",0,100,draft.WeakScale,v=>draft.WeakScale=v," %");weak.Children.Add(Ui.Button("Probar motor derecho",()=>Run(()=>TestMotor(0,110)),true));Border wc=Ui.Card(weak);Grid.SetColumn(wc,2);two.Children.Add(wc);content.Children.Add(two);content.Children.Add(Ui.Gap(20));
            StackPanel actions=new StackPanel{Orientation=Orientation.Horizontal};actions.Children.Add(Ui.Button("Probar ambos motores",()=>Run(()=>TestMotor(85,85))));Button continuous=Ui.Button("Vibración continua",()=>Run(StartContinuous),true);continuous.Margin=new Thickness(10,0,0,0);actions.Children.Add(continuous);Button stop=Ui.Button("Detener vibración",()=>Run(()=>StopMotor()));stop.Margin=new Thickness(10,0,0,0);actions.Children.Add(stop);content.Children.Add(actions);content.Children.Add(Ui.Gap(25));
            TextBlock state=Ui.T("Conecta el mando para probar la vibración.",13,Ui.Muted);content.Children.Add(Ui.Card(state));live.Add(s=>I18n.SetText(state,s==null?"Conecta el mando para probar la vibración.":s.Bluetooth&&s.OpenError!=0?"El canal Bluetooth no está disponible. Reconecta el mando.":s.RumbleFailed?"El último envío no se completó. Reconecta el mando y vuelve a probar.":vibrationCancel!=null&&!vibrationLoop.IsCompleted?"Vibración continua":"Listo"));page.Children.Add(new ScrollViewer{Content=content});
        }
        void BuildProfiles() {
            StackPanel content=Section("PERFILES","Perfiles","");
            Grid layout=Columns(360);StackPanel list=new StackPanel();foreach(Profile p in library.Profiles) {
                string name=p.Name;Button b=Ui.Button(name,()=>{SwitchProfile(name);FillProfiles();});b.Content=I18n.ProfileName(name);I18n.Raw(b);b.Margin=new Thickness(0,0,0,10);b.Background=name==originalName?Ui.Brush("#2C3A2D"):Ui.Inset;b.Foreground=name==originalName?Ui.Lime:Ui.Text;list.Children.Add(b);
            }
            list.Children.Add(Ui.Gap(10));list.Children.Add(Ui.Button("+ Nuevo perfil",NewProfile));list.Children.Add(Ui.Gap(10));list.Children.Add(Ui.Button("Importar perfil",ImportProfile));layout.Children.Add(Ui.Card(list));
            StackPanel editor=Section("PERFIL SELECCIONADO","Nombre","");TextBox nameBox=new TextBox{Text=I18n.ProfileName(draft.Name),MaxLength=60};nameBox.TextChanged+=(s,e)=>{draft.Name=nameBox.Text;MarkDirty();};editor.Children.Add(nameBox);editor.Children.Add(Ui.Gap(20));
            editor.Children.Add(Ui.Button("Duplicar",DuplicateProfile));editor.Children.Add(Ui.Gap(10));editor.Children.Add(Ui.Button("Exportar JSON",ExportProfile));editor.Children.Add(Ui.Gap(10));Button delete=Ui.Button("Eliminar perfil",DeleteProfile);delete.IsEnabled=library.Profiles.Count>1;editor.Children.Add(delete);
            editor.Children.Add(Ui.Gap(20));editor.Children.Add(Ui.T("",12,Ui.Muted));Border ec=Ui.Card(editor);Grid.SetColumn(ec,2);layout.Children.Add(ec);content.Children.Add(layout);page.Children.Add(new ScrollViewer{Content=content});
        }
        string UniqueName(string name){if(name.Length>55)name=name.Substring(0,55);string candidate=name;int n=2;while(library.Profiles.Any(p=>p.Name.Equals(candidate,StringComparison.OrdinalIgnoreCase)))candidate=name+" "+n++;return candidate;}
        void NewProfile(){AddProfile(new Profile{Name=UniqueName(I18n.T("Nuevo perfil"))});}
        void DuplicateProfile(){Profile p=draft.Clone();p.Name=UniqueName(draft.Name.Length>45?I18n.T("Copia de perfil"):I18n.ProfileName(draft.Name)+I18n.T(" · copia"));AddProfile(p);}
        void AddProfile(Profile p){RunSync(()=>{if(library.Profiles.Count>=100)throw new InvalidOperationException("La biblioteca admite hasta 100 perfiles.");library.Profiles.Add(p);library.Selected=p.Name;library.Save();draft=p.Clone();originalName=p.Name;dirty=false;FillProfiles();Navigate("Perfiles");I18n.SetText(footer,"Perfil creado. Guárdalo y aplícalo cuando esté listo.");});}
        void DeleteProfile(){if(library.Profiles.Count<=1)return;if(MessageBox.Show(this,I18n.T("¿Eliminar el perfil «")+I18n.ProfileName(originalName)+"»?",I18n.T("Eliminar perfil"),MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;RunSync(()=>{if(library.Applied==originalName){library.Applied=null;activeActions=null;desktopActions.Release();}library.Profiles.RemoveAll(p=>p.Name==originalName);draft=library.Profiles[0].Clone();originalName=draft.Name;library.Selected=originalName;library.Save();dirty=false;FillProfiles();Navigate("Perfiles");});}
        void ImportProfile(){OpenFileDialog d=new OpenFileDialog{Filter=I18n.T("Perfil StadiaLink (*.json)")+"|*.json"};if(d.ShowDialog(this)!=true)return;RunSync(()=>{FileInfo f=new FileInfo(d.FileName);if(f.Length>65536)throw new InvalidDataException("Archivo demasiado grande.");Profile p=Profile.FromJson(File.ReadAllText(d.FileName));p.Name=UniqueName(p.Name);AddProfile(p);});}
        void ExportProfile(){SaveFileDialog d=new SaveFileDialog{Filter=I18n.T("Perfil StadiaLink (*.json)")+"|*.json",FileName="perfil-stadia.json"};if(d.ShowDialog(this)!=true)return;RunSync(()=>{File.WriteAllText(d.FileName,draft.ToJson());I18n.SetText(footer,"Perfil exportado.");});}
        void ResetDraft(){string name=draft.Name;draft=new Profile{Name=name};MarkDirty();Navigate(route);}
        void SetVoiceShortcut(int source,int key,int modifiers,bool held){draft.EnsureActions();draft.Map[source]=0;draft.Actions[source]=held?13:14;draft.Keys[source]=key;draft.Modifiers[source]=modifiers;MarkDirty();}
        void EditVoiceShortcut(int source,string title){draft.EnsureActions();StackPanel body=Section(Destination.Physical[source],title,"Configura esta misma combinación en el juego o en Discord.");body.Margin=new Thickness(24);ComboBox keys=new ComboBox{ItemsSource=KeyOption.All};keys.SelectedItem=((KeyOption[])keys.ItemsSource).First(k=>k.Code==(Profile.KeySupported(draft.Keys[source])?draft.Keys[source]:source==12?120:source==11?121:122));body.Children.Add(keys);WrapPanel flags=new WrapPanel();var checks=new Dictionary<int,CheckBox>();foreach(int bit in new[]{1,2,4,8}){CheckBox check=new CheckBox{Content=bit==1?"Ctrl":bit==2?"Alt":bit==4?"Shift":"Win",IsChecked=(draft.Modifiers[source]&bit)!=0,Margin=new Thickness(0,14,18,14)};flags.Children.Add(check);checks[bit]=check;}body.Children.Add(flags);CheckBox hold=new CheckBox{Content="Mantener la tecla mientras pulso el botón",IsChecked=draft.Actions[source]==13||(draft.Actions[source]==0&&source==12)};body.Children.Add(hold);body.Children.Add(Ui.Gap(18));body.Children.Add(Ui.T("Marcado: pulsar para hablar. Desmarcado: atajo para alternar el silencio del micrófono o cualquier otra acción.",12,Ui.Muted));body.Children.Add(Ui.Gap(22));Window dialog=new Window{Title=I18n.T("Configurar atajo"),Owner=this,Width=520,Height=430,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Ui.Background,Foreground=Ui.Text,Content=body};Ui.Styles(dialog.Resources);body.Children.Add(Ui.Button("Actualizar borrador",()=>{int modifiers=0;foreach(var pair in checks)if(pair.Value.IsChecked==true)modifiers|=pair.Key;SetVoiceShortcut(source,((KeyOption)keys.SelectedItem).Code,modifiers,hold.IsChecked==true);dialog.DialogResult=true;},true));I18n.TranslateTree(body);dialog.ShowDialog();}
        void BuildApplication() {
            StackPanel content=Section("","Ajustes","");
            StackPanel prefs=Section("PREFERENCIAS","Inicio y avisos","");CheckBox startup=new CheckBox{Content="Iniciar con Windows",IsChecked=draftStartup};startup.Checked+=(s,e)=>SaveStartupSetting(startup,true);startup.Unchecked+=(s,e)=>SaveStartupSetting(startup,false);prefs.Children.Add(startup);CheckBox notifications=new CheckBox{Content="Avisos de conexión y batería",IsChecked=draftNotifications,Margin=new Thickness(0,12,0,0)};notifications.Checked+=(s,e)=>{draftNotifications=true;preferencesDirty=true;MarkDirty();};notifications.Unchecked+=(s,e)=>{draftNotifications=false;preferencesDirty=true;MarkDirty();};prefs.Children.Add(notifications);prefs.Children.Add(Ui.Gap(12));content.Children.Add(Ui.Card(prefs));content.Children.Add(Ui.Gap(20));
            StackPanel language=Section("LINK","Idioma","");ComboBox languages=new ComboBox{ItemsSource=I18n.Options,ItemTemplate=I18n.LanguageTemplate(),SelectedItem=I18n.Options.First(o=>o.Code==draftLanguage),MinWidth=260,HorizontalAlignment=HorizontalAlignment.Left};languages.SelectionChanged+=(s,e)=>{LanguageOption choice=languages.SelectedItem as LanguageOption;if(choice!=null&&choice.Code!=I18n.Code){draftLanguage=choice.Code;I18n.Set(choice.Code);FillProfiles();preferencesDirty=true;MarkDirty();I18n.TranslateTree(Content as DependencyObject);Navigate(route);}};languagePicker=languages;language.Children.Add(languages);content.Children.Add(Ui.Card(language));content.Children.Add(Ui.Gap(20));
            StackPanel quick=Section("ATAJOS","Voz y accesos rápidos","");
            foreach(int source in new[]{12,11,10}){int id=source;string label=id==12?"Pulsar para hablar · Asistente":id==11?"Silenciar / activar micrófono · Captura":"Atajo adicional · Stadia";quick.Children.Add(Ui.Button(label,()=>EditVoiceShortcut(id,label)));quick.Children.Add(Ui.Gap(8));}content.Children.Add(Ui.Card(quick));content.Children.Add(Ui.Gap(20));
            Panel previousParent=devicePicker.Parent as Panel;if(previousParent!=null)previousParent.Children.Remove(devicePicker);
            StackPanel card=Section("CONEXIÓN","Tu dispositivo","");devicePicker.ItemsSource=available;devicePicker.SelectedItem=available.FirstOrDefault(p=>pad!=null&&p.Path==pad.Path);devicePicker.SelectionChanged-=DeviceChanged;devicePicker.SelectionChanged+=DeviceChanged;card.Children.Add(devicePicker);card.Children.Add(Ui.Gap(15));TextBlock detail=Ui.T("",12,Ui.Muted);card.Children.Add(detail);
            live.Add(s=>I18n.SetText(detail,s==null?"Enciende el Stadia y emparéjalo desde los ajustes Bluetooth de Windows.":s.ConfigVersion!=1?"Mando detectado. Instala el controlador incluido para activar la configuración.":"Conectado por "+(s.Bluetooth?"Bluetooth":"USB")));
            card.Children.Add(Ui.Gap(20));StackPanel buttons=new StackPanel{Orientation=Orientation.Horizontal};buttons.Children.Add(Ui.Button("Abrir ajustes Bluetooth",()=>RunSync(()=>Process.Start("ms-settings:bluetooth"))));Button rescan=Ui.Button("Volver a detectar",()=>{nextDiscovery=DateTime.MinValue;Poll();});rescan.Margin=new Thickness(10,0,0,0);buttons.Children.Add(rescan);card.Children.Add(buttons);card.Children.Add(Ui.Gap(10));card.Children.Add(Ui.Button("Reparar controlador",()=>Run(RepairDriver)));content.Children.Add(Ui.Card(card));content.Children.Add(Ui.Gap(20));
            StackPanel response=Section("RENDIMIENTO","Tiempo de respuesta","");TextBlock measurement=Ui.T("Tiempos de la aplicación",12,Ui.Muted);response.Children.Add(measurement);response.Children.Add(Ui.Gap(12));response.Children.Add(Ui.Button("Medir durante 6 s",()=>Run(()=>MeasureResponse(measurement))));response.Children.Add(Ui.Gap(8));response.Children.Add(Ui.Button("Abrir informe",()=>RunSync(()=>{string report=Path.Combine(ProfileLibrary.DirectoryPath,"logs","response.txt");if(File.Exists(report))Process.Start(report);})));content.Children.Add(Ui.Card(response));content.Children.Add(Ui.Gap(20));
            StackPanel about=Section("LINK","Archivos","");
            about.Children.Add(Ui.Button("Abrir carpeta de perfiles",()=>RunSync(()=>{Directory.CreateDirectory(ProfileLibrary.DirectoryPath);Process.Start(ProfileLibrary.DirectoryPath);})));about.Children.Add(Ui.Gap(16));about.Children.Add(Ui.T("1.0.1 · GPLv3",12,Ui.Muted));about.Children.Add(Ui.Gap(8));about.Children.Add(Ui.T("",12,Ui.Muted));content.Children.Add(Ui.Card(about));page.Children.Add(new ScrollViewer{Content=content});
        }
        void DeviceChanged(object sender,SelectionChangedEventArgs e){PadInfo info=devicePicker.SelectedItem as PadInfo;if(info==null || (pad!=null && pad.Path==info.Path))return;RunSync(()=>{if(pad!=null)pad.Dispose();pad=new Controller(info.Path);status=null;loadedHardware=null;activeActions=null;desktopActions.Release();batteryLevel=null;nextBattery=DateTime.MinValue;pendingApply=false;nextDiscovery=DateTime.UtcNow.AddSeconds(2);});}
        async Task RepairDriver(){if(pad!=null){pad.Dispose();pad=null;}string file=System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"driver","install.ps1");if(!File.Exists(file))throw new FileNotFoundException("Abre el instalador de StadiaLink para instalar el controlador.");await InstallerActions.RunDriver(file,false);nextDiscovery=DateTime.MinValue;I18n.SetText(footer,"Controlador instalado. Si Windows lo pide, reconecta el mando o reinicia el equipo.");}
        async Task ApplyDraft() {
            draft.Validate();if(library.Profiles.Any(p=>p.Name.Equals(draft.Name,StringComparison.OrdinalIgnoreCase)&&p.Name!=originalName))throw new InvalidOperationException("Ya existe un perfil con ese nombre.");
            Profile copy=draft.Clone();Controller device=pad;if(device!=null){if(status==null || status.ConfigVersion!=1)throw new InvalidOperationException("Instala el controlador incluido para configurar el mando.");await Task.Run(()=>device.Apply(copy));}
            if(preferencesDirty){InstallerActions.SavePreferences(draftStartup,draftNotifications);I18n.Save();notificationsEnabled=draftNotifications;if(!notificationsEnabled&&tray!=null)tray.ClearNotifications();preferencesDirty=false;}
            int index=library.Profiles.FindIndex(p=>p.Name==originalName);if(index<0)index=0;library.Profiles[index]=copy;library.Selected=copy.Name;library.Applied=copy.Name;library.Save();originalName=copy.Name;dirty=false;pendingApply=device==null;desktopActions.Release();activeActions=device==null?null:copy.Clone();FillProfiles();if(saveState!=null)I18n.SetText(saveState,device==null?"Guardado · pendiente de conexión":"Aplicado al mando");
            I18n.SetText(footer,device==null?"Perfil guardado. Se aplicará cuando conectes el mando con StadiaLink abierto.":"Perfil guardado y aplicado.");
            if(route=="Perfiles")Navigate("Perfiles");
        }
        async Task TestMotor(byte strong,byte weak) {
            await StopMotor();Controller device=pad;if(device==null)throw new InvalidOperationException("Conecta el mando para probar la vibración.");if(status==null || status.ConfigVersion!=1)throw new InvalidOperationException("Instala el controlador incluido para usar las pruebas.");
            if(!status.RawMotorPreview)throw new InvalidOperationException("Actualiza el controlador incluido para probar ajustes sin guardarlos.");
            byte s=(byte)(strong*draft.StrongScale/100),w=(byte)(weak*draft.WeakScale/100);await Task.Run(()=>device.Preview(s,w));I18n.SetText(footer,"Prueba enviada. Ajustes sin guardar.");
        }
        async Task StartContinuous() {
            await StopMotor();Controller device=pad;
            if(device==null||status==null||!status.RawMotorPreview)throw new InvalidOperationException("Conecta el mando para probar la vibración.");
            vibrationCancel=new CancellationTokenSource();vibrationLoop=ContinuousLoop(device,vibrationCancel.Token);
            await Task.Delay(20);if(vibrationLoop.IsFaulted)await vibrationLoop;
        }
        public async Task VerifyContinuous(string file) {
            Exception failure=null;
            try {
                if(pad==null)throw new Exception("Connect Stadia for continuous vibration test.");
                byte[] before=pad.Configuration().ToReport();PadStatus initial=pad.Read();Navigate("Vibración");await StartContinuous();await Task.Delay(80);PadStatus baseline=pad.Read();
                await Task.Delay(1100);PadStatus running=pad.Read();if(baseline.Writes<=initial.Writes||running.Stops!=baseline.Stops||running.RumbleFailed||vibrationLoop.IsCompleted)throw new Exception("Continuous renewal failed: writes "+baseline.Writes+" -> "+running.Writes+", stops "+baseline.Stops+" -> "+running.Stops+", failed="+running.RumbleFailed);
                await StopMotor();await Task.Delay(450);PadStatus stopped=pad.Read();if(stopped.Stops<=running.Stops)throw new Exception("Manual stop not confirmed.");
                await StartContinuous();Navigate("Ajustes");await vibrationLoop;if(!vibrationLoop.IsCompleted)throw new Exception("Tab navigation did not cancel continuous test.");
                if(!before.SequenceEqual(pad.Configuration().ToReport()))throw new Exception("Vibration test changed the profile.");
                File.WriteAllText(file,"PASS: renewed vibration beyond driver watchdog; no watchdog stop while running; manual stop; tab change stops; configuration unchanged.");
            }catch(Exception ex){failure=ex;}
            await StopMotor();if(failure!=null)throw failure;
        }
        async Task MeasureResponse(TextBlock result) {
            if(pad==null)throw new InvalidOperationException("Conecta el mando para medir.");
            lock(timingGate){readTimes.Clear();sampleAges.Clear();readIntervals.Clear();previousReadTicks=0;}
            Interlocked.Exchange(ref sampleCount,0);frameCount=0;performance.Restart();I18n.SetText(result,"Midiendo…");await Task.Delay(6000);
            string directory=Path.Combine(ProfileLibrary.DirectoryPath,"logs");Directory.CreateDirectory(directory);PerformanceReport(Path.Combine(directory,"response.txt"));
            lock(timingGate){double[] query=readTimes.OrderBy(x=>x).ToArray(),frame=sampleAges.OrderBy(x=>x).ToArray();if(query.Length==0||frame.Length==0)throw new InvalidOperationException("No hay muestras suficientes.");I18n.SetText(result,"HID p95: "+query[(int)((query.Length-1)*.95)].ToString("F2")+" ms · UI p95: "+frame[(int)((frame.Length-1)*.95)].ToString("F2")+" ms");}
        }
        async Task ContinuousLoop(Controller device,CancellationToken token) {
            try {while(!token.IsCancellationRequested&&pad==device&&!closed){byte strong=(byte)(110*draft.StrongScale/100),weak=(byte)(110*draft.WeakScale/100);await Task.Run(()=>device.Preview(strong,weak));await Task.Delay(150,token);}}
            catch(OperationCanceledException){}
            catch(Exception ex){if(!closed)I18n.SetText(footer,I18n.Error(ex));}
            try{await Task.Run(()=>device.Test(0,0));}catch{}
        }
        async Task StopMotor(){if(vibrationCancel!=null){vibrationCancel.Cancel();await vibrationLoop;vibrationCancel.Dispose();vibrationCancel=null;}Controller device=pad;if(device!=null)await Task.Run(()=>device.Test(0,0));I18n.SetText(footer,"Vibración detenida");}
        async void Run(Func<Task> action){apply.IsEnabled=false;page.IsEnabled=false;profilePicker.IsEnabled=false;try{await action();}catch(Exception e){I18n.SetText(footer,I18n.Error(e));MessageBox.Show(this,I18n.Error(e),"StadiaLink",MessageBoxButton.OK,MessageBoxImage.Information);}finally{apply.IsEnabled=true;page.IsEnabled=true;profilePicker.IsEnabled=true;}}
        void RunSync(Action action){try{action();}catch(Exception e){I18n.SetText(footer,I18n.Error(e));MessageBox.Show(this,I18n.Error(e),"StadiaLink",MessageBoxButton.OK,MessageBoxImage.Information);}}
        async void Poll() {
            if(pollBusy || closed)return;pollBusy=true;
            try {
                if(DateTime.UtcNow>=nextDiscovery){nextDiscovery=DateTime.UtcNow.AddSeconds(2);List<PadInfo> found=await Task.Run(()=>Controller.Find());bool listChanged=!available.Select(p=>p.Path).SequenceEqual(found.Select(p=>p.Path));available=found;if(closed)return;
                    if(pad==null && available.Count>0){pad=new Controller(available[0].Path);loadedHardware=null;I18n.SetText(footer,"Stadia detectado. Listo para configurar.");}
                    if(route=="Ajustes" && listChanged){devicePicker.SelectionChanged-=DeviceChanged;devicePicker.ItemsSource=available;devicePicker.SelectedItem=available.FirstOrDefault(p=>pad!=null&&p.Path==pad.Path);devicePicker.SelectionChanged+=DeviceChanged;}
                }
                if(pad!=null){Controller device=pad;if(failedDevice==device)throw new IOException("Controller disconnected.");if(sampledDevice!=device||latestSample==null)return;status=latestSample;if(closed)return;
                    if(status.ConfigVersion==1 && loadedHardware==null){loadedHardware=await Task.Run(()=>device.Configuration());
                        if(!File.Exists(ProfileLibrary.FilePath)&&!dirty){string name=draft.Name;draft=loadedHardware.Clone();draft.Name=name;library.Profiles[0]=draft.Clone();Navigate(route);}
                        Profile selected=library.Profiles.FirstOrDefault(p=>p.Name==library.Applied);activeActions=selected!=null&&selected.ToReport().SequenceEqual(loadedHardware.ToReport())?selected.Clone():null;desktopActions.Release();nextBattery=DateTime.MinValue;
                        if(pendingApply){Profile saved=library.Profiles.First(p=>p.Name==originalName).Clone();await Task.Run(()=>device.Apply(saved));activeActions=saved;pendingApply=false;I18n.SetText(footer,"Perfil aplicado.");}
                    }
                }
            }catch(Exception){if(pad!=null)pad.Dispose();pad=null;status=null;loadedHardware=null;activeActions=null;desktopActions.Release();batteryLevel=null;}
            finally{pollBusy=false;}
        }
        void SampleInput(){IntPtr wait=CreateWaitableTimerExW(IntPtr.Zero,null,2,0x1F0003);try{while(!stopSampling){Controller device=pad;if(device!=null&&failedDevice!=device){try{long began=Stopwatch.GetTimestamp();PadStatus read=device.Read();long completed=Stopwatch.GetTimestamp();read.SampleTicks=completed;lock(timingGate){if(readTimes.Count<12000)readTimes.Add((completed-began)*1000.0/Stopwatch.Frequency);if(previousReadTicks!=0&&readIntervals.Count<12000)readIntervals.Add((completed-previousReadTicks)*1000.0/Stopwatch.Frequency);previousReadTicks=completed;}latestSample=read;sampledDevice=device;Interlocked.Increment(ref sampleCount);inputSamples.Enqueue(Tuple.Create(device,read));while(inputSamples.Count>256){Tuple<Controller,PadStatus> discarded;inputSamples.TryDequeue(out discarded);}}catch{failedDevice=device;}}if(wait!=IntPtr.Zero){long due=device==null?-1000000:-40000;if(SetWaitableTimer(wait,ref due,0,IntPtr.Zero,IntPtr.Zero,false))WaitForSingleObject(wait,120);else Thread.Sleep(4);}else Thread.Sleep(device==null?100:4);}}finally{if(wait!=IntPtr.Zero)CloseHandle(wait);}}
        void ConsumeInput(){if(sampledDevice==pad&&pad!=null&&failedDevice!=pad)status=latestSample;Tuple<Controller,PadStatus> sample;while(inputSamples.TryDequeue(out sample)){if(sample.Item1!=pad||failedDevice==pad)continue;try{desktopActions.Update(sample.Item2,activeActions);}catch(Exception e){desktopActions.Release();I18n.SetText(footer,I18n.Error(e));}}if(pad==null||failedDevice==pad){status=null;desktopActions.Release();if(vibrationCancel!=null)vibrationCancel.Cancel();}foreach(DeviceNotice notice in notices.Observe(status!=null,status==null?null:batteryLevel,DateTime.UtcNow))if(tray!=null&&notificationsEnabled&&!SuppressNotifications)tray.Notify(notice);if(!IsVisible){if(tray!=null)tray.Update(status!=null,batteryLevel,status!=null&&!status.Bluetooth);RefreshBattery();}}
        TimeSpan lastRender=TimeSpan.MinValue;
        void RenderFrame(object sender,EventArgs e){RenderingEventArgs frame=e as RenderingEventArgs;if(frame!=null&&frame.RenderingTime==lastRender)return;if(frame!=null)lastRender=frame.RenderingTime;if(closed||!IsVisible)return;ConsumeInput();frameCount++;if(status!=null&&status.SampleTicks>0)lock(timingGate){if(sampleAges.Count<3000)sampleAges.Add((Stopwatch.GetTimestamp()-status.SampleTicks)*1000.0/Stopwatch.Frequency);}UpdateLive();}
        static string Distribution(string name,List<double> samples){if(samples.Count==0)return I18n.T(name)+I18n.T(": no samples");double[] values=samples.OrderBy(x=>x).ToArray();Func<double,double> percentile=p=>values[Math.Min(values.Length-1,(int)Math.Ceiling(p*values.Length)-1)];return I18n.T(name)+I18n.T(" (ms): median=")+percentile(.5).ToString("F3")+" p95="+percentile(.95).ToString("F3")+" p99="+percentile(.99).ToString("F3")+" max="+values.Last().ToString("F3")+" n="+values.Length;}
        public void PerformanceReport(string file){double seconds=performance.Elapsed.TotalSeconds;string detail;lock(timingGate){detail=Distribution("HID query duration",readTimes)+Environment.NewLine+Distribution("Query intervals",readIntervals)+Environment.NewLine+Distribution("Latest query to UI frame",sampleAges);}File.WriteAllText(file,I18n.T("Render callbacks/s=")+(frameCount/seconds).ToString("F1")+"; "+I18n.T("HID queries/s=")+(Interlocked.Read(ref sampleCount)/seconds).ToString("F1")+"; "+I18n.T("WPF tier=")+(RenderCapability.Tier>>16)+Environment.NewLine+detail+Environment.NewLine+I18n.T("Queries can return cached input. These measurements exclude physical actuation, radio transport, game polling and display latency; they cannot establish end-to-end latency."));}
        void UpdateLive() {
            I18n.SetText(connection,status==null?"○  SIN CONEXIÓN":"●  MANDO CONECTADO");connection.Foreground=status==null?Ui.Muted:Ui.Lime;
            I18n.SetText(deviceText,status==null?"Enciende tu Stadia":status.Bluetooth?"Bluetooth · "+status.Address.ToString("X12").Substring(8):"Conectado por USB");
            if(visual!=null)visual.Update(status);foreach(Action<PadStatus> action in live)action(status);
            I18n.SetText(batteryText,status==null?"Batería: —":(batteryLevel.HasValue?"Batería: "+batteryLevel.Value+" %":"Batería: no disponible")+(!status.Bluetooth?(batteryLevel==100?" · Carga completa":" · Cargando"):""));batteryText.Foreground=batteryLevel.HasValue?Ui.Lime:Ui.Muted;
            if(tray!=null)tray.Update(status!=null,batteryLevel,status!=null&&!status.Bluetooth);RefreshBattery();
        }
        async void RefreshBattery(){if(closed||batteryBusy||status==null||DateTime.UtcNow<nextBattery)return;batteryBusy=true;bool bluetooth=status.Bluetooth;Controller batteryDevice=pad;ulong address=bluetooth?status.Address:0;nextBattery=DateTime.UtcNow.AddSeconds(30);try{int? read=await Task.Run(()=>Battery.Read(address));if(!closed&&status!=null&&pad==batteryDevice&&status.Bluetooth==bluetooth&&(bluetooth?status.Address==address:true))batteryLevel=read;}catch{batteryLevel=null;}finally{batteryBusy=false;}}
        public void ExportSnapshot(string path) {
            UpdateLayout();RenderTargetBitmap bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);
            PngBitmapEncoder encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(FileStream f=File.Create(path))encoder.Save(f);
        }
        public void SetPageForTest(string name){Navigate(name,true);}
        public async Task VerifyLanguages(string directory){Directory.CreateDirectory(directory);string savedCode=I18n.Saved;Profile original=draft.Clone();string oldRoute=route;bool oldDirty=dirty,oldPreferences=preferencesDirty;try{Navigate("Ajustes");foreach(LanguageOption option in I18n.Options){languagePicker.SelectedItem=option;if(I18n.Code!=option.Code)throw new Exception("Language selector failed.");foreach(string tab in new[]{"Mando","Sticks y gatillos","Vibración","Perfiles","Ajustes"}){Navigate(tab);await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(40);ExportSnapshot(Path.Combine(directory,option.Code+"-"+tab.Replace(" ","-")+".png"));File.WriteAllLines(Path.Combine(directory,option.Code+"-"+tab.Replace(" ","-")+".txt"),VisualChildren<TextBlock>(this).Select(t=>t.Text).Concat(VisualChildren<ComboBox>(this).Where(c=>c!=languagePicker).SelectMany(c=>c.Items.Cast<object>().Select(i=>i.ToString()))));}VerifyShortcutDialog(Path.Combine(directory,option.Code+"-shortcut.png"));I18n.VerifyCatalog();if(option.Code!="es"&&I18n.T("Guardar y aplicar")=="Guardar y aplicar")throw new Exception("Translation missing.");}languagePicker.SelectedItem=I18n.Options.First(o=>o.Code=="de");Navigate("Mando",true);if(I18n.Code!=savedCode||preferencesDirty)throw new Exception("Pending language was not discarded.");File.WriteAllText(Path.Combine(directory,"result.txt"),"PASS: four language selector values, twenty rendered pages, pending language discarded on tab change, profile data unchanged.");}finally{I18n.Set(savedCode);draftLanguage=savedCode;draft=original;dirty=oldDirty;preferencesDirty=oldPreferences;I18n.TranslateTree(Content as DependencyObject);Navigate(oldRoute);}}
        public void VerifyStartupState(string file){if(IsVisible||ShowInTaskbar||tray==null||!tray.Visible)throw new Exception("Startup did not open in the notification area.");File.WriteAllText(file,"PASS: --tray startup leaves only the notification-area icon; no main window or taskbar button.");}
        public void VerifyShortcutDialog(string file){DispatcherTimer capture=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(400)};Exception failure=null;capture.Tick+=(s,e)=>{capture.Stop();Window dialog=OwnedWindows.Cast<Window>().Single();try{ComboBox keys=VisualChildren<ComboBox>(dialog).First();if(keys.SelectedItem==null)throw new Exception("Default voice shortcut key was not selected.");dialog.UpdateLayout();var bitmap=new RenderTargetBitmap((int)dialog.ActualWidth,(int)dialog.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(dialog);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(FileStream stream=File.Create(file))encoder.Save(stream);}catch(Exception ex){failure=ex;}finally{dialog.Close();}};capture.Start();EditVoiceShortcut(12,"Pulsar para hablar · Asistente");if(failure!=null)throw failure;}
        public void VerifyExtras(string file){Profile before=draft.Clone();bool startupBefore=draftStartup;Navigate("Ajustes");SetVoiceShortcut(12,120,0,true);SetVoiceShortcut(11,121,3,false);SetVoiceShortcut(10,122,0,false);draft.Validate();if(draft.Actions[12]!=13||draft.Actions[11]!=14||draft.Actions[10]!=14)throw new Exception("Voice shortcut modes failed.");draftStartup=!startupBefore;preferencesDirty=true;MarkDirty();Navigate("Mando",true);if(draft.ToJson()!=before.ToJson()||draftStartup!=InstallerActions.StartWithWindows||preferencesDirty)throw new Exception("Shortcut/preferences drafts were not discarded.");File.AppendAllText(file,Environment.NewLine+"PASS: three voice shortcut drafts and pending startup setting discarded on tab change.");}
        public void VerifyDraftBehavior(string file) {
            Profile original=draft.Clone();bool wasDirty=dirty;string previousRoute=route;
            try {
                Navigate("Mando");SelectButton(0);int changed=original.Map[0]==2?1:2;
                destinationPicker.SelectedItem=Destination.All.First(d=>d.Id==changed);
                if(!dirty||draft.Map[0]!=changed)throw new Exception("Mapping edit failed.");
                Navigate("Sticks y gatillos",true);if(dirty||draft.Map[0]!=original.Map[0])throw new Exception("Tab switch did not discard mapping.");
                Navigate("Mando",true);SelectButton(0);actionPicker.SelectedItem=ExtraAction.All.First(a=>a.Id==13);keyPicker.SelectedItem=((KeyOption[])keyPicker.ItemsSource).First(k=>k.Code==116);modifierChecks[1].IsChecked=true;
                if(draft.Actions[0]!=13||draft.Keys[0]!=116||draft.Modifiers[0]!=1||draft.Map[0]!=0)throw new Exception("Shortcut controls did not update the draft.");
                Navigate("Vibración",true);if(dirty||draft.Actions[0]!=original.Actions[0])throw new Exception("Tab switch did not discard shortcut.");
                WindowState=WindowState.Minimized;if(IsVisible||ShowInTaskbar||tray==null||!tray.Visible)throw new Exception("Minimize to tray failed.");
                RestoreFromTray();if(!IsVisible||!ShowInTaskbar||WindowState!=WindowState.Normal)throw new Exception("Restore from tray failed.");
                File.WriteAllText(file,"PASS: tabs discard edits without saving; shortcut selector and modifiers; minimize to notification area; restore window.");
            }finally{draft=original;dirty=wasDirty;Navigate(previousRoute);}
        }
        public void VerifyAnalogPreview() {
            Profile previous=draft.Clone();bool wasDirty=dirty;string previousRoute=route;
            try {
                draft.Flags=0;draft.LeftDeadzone=0;draft.LeftSensitivity=100;Navigate("Sticks y gatillos");UpdateLayout();
                PadStatus sample=new PadStatus{HasInput=true};sample.Raw[4]=150;sample.Raw[5]=128;sample.Raw[6]=128;sample.Raw[7]=128;
                Action repaint=()=>{foreach(Action<PadStatus> update in live)update(sample);};
                Ellipse dot=VisualChildren<Ellipse>(page).First(e=>e.Fill==Ui.Lime);
                TranslateTransform motion=(TranslateTransform)dot.RenderTransform;
                Slider[] controls=VisualChildren<Slider>(page).ToArray();repaint();double baseline=motion.X;
                controls[1].Value=200;repaint();if(motion.X<=baseline*1.9)throw new Exception("Sensitivity slider does not change the output preview.");
                controls[0].Value=20;repaint();if(motion.X!=0)throw new Exception("Deadzone does not suppress small travel.");
                Button reset=VisualChildren<Button>(page).First(b=>b.Content as string==I18n.T("Sin zona muerta"));reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));repaint();
                if(draft.LeftDeadzone!=0||motion.X<=0)throw new Exception("No-deadzone button did not restore small travel.");
                Border[] travel=VisualChildren<Border>(page).Where(b=>b.Background==Ui.Lime).ToArray();
                sample.Raw[8]=128;draft.TriggerDeadzone=0;repaint();double before=travel[0].Width;
                controls[4].Value=40;repaint();if(before<=0||travel[0].Width>=before)throw new Exception("Trigger deadzone preview did not change travel.");
                sample.Raw[8]=64;repaint();if(travel[0].Width!=0)throw new Exception("Trigger deadzone did not suppress early travel.");
                sample.Raw[9]=255;draft.Flags|=32;repaint();if(travel[0].Width<=before)throw new Exception("Trigger swap is not reflected in preview.");
                Navigate("Mando",true);if(dirty)throw new Exception("Preview edits were not discarded.");
            }finally{draft=previous;dirty=wasDirty;Navigate(previousRoute);}
        }
        public void VerifyStartupSetting(string file) {
            bool previous=InstallerActions.StartWithWindows;
            try {
                Navigate("Ajustes");UpdateLayout();CheckBox checkbox=VisualChildren<CheckBox>(page).First(c=>c.Content as string==I18n.T("Iniciar con Windows"));
                checkbox.IsChecked=false;checkbox.IsChecked=true;
                if(!InstallerActions.StartWithWindows)throw new Exception("Startup checkbox did not persist immediately.");
                Navigate("Mando",true);if(!InstallerActions.StartWithWindows)throw new Exception("Tab change removed the startup setting.");
                Navigate("Ajustes");UpdateLayout();checkbox=VisualChildren<CheckBox>(page).First(c=>c.Content as string==I18n.T("Iniciar con Windows"));
                if(checkbox.IsChecked!=true)throw new Exception("Startup state was not reloaded.");
                checkbox.IsChecked=false;if(InstallerActions.StartWithWindows)throw new Exception("Startup cannot be disabled.");
                File.WriteAllText(file,"PASS: startup saved from checkbox without profile save; persists across tabs; state reload; disabling; original preference restored.");
            }finally{InstallerActions.SaveStartup(previous);draftStartup=previous;}
        }
        public async Task VerifyResponsive(string directory) {
            Directory.CreateDirectory(directory);
            foreach(int[] size in new[]{new[]{820,580},new[]{1000,680},new[]{1340,850},new[]{1700,1000}}) {
                Width=size[0];Height=size[1];UpdateLayout();
                foreach(string tab in new[]{"Mando","Sticks y gatillos","Vibración","Perfiles","Ajustes"}) {
                    Navigate(tab);UpdateLayout();await Task.Delay(220);UpdateLayout();
                    Point corner=apply.TranslatePoint(new Point(apply.ActualWidth,apply.ActualHeight),this);
                    if(corner.X>ActualWidth||corner.Y>110||page.ActualWidth<400)throw new Exception("Responsive toolbar or content does not fit.");
                    ExportSnapshot(Path.Combine(directory,size[0]+"x"+size[1]+"-"+tab.Replace(" ","-")+".png"));
                }
            }
            File.WriteAllText(Path.Combine(directory,"result.txt"),"PASS: five pages at four window sizes, toolbar bounds, responsive content width and twenty snapshots.");
        }
        static IEnumerable<T> VisualChildren<T>(DependencyObject parent) where T:DependencyObject {
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){DependencyObject child=VisualTreeHelper.GetChild(parent,i);T found=child as T;if(found!=null)yield return found;foreach(T next in VisualChildren<T>(child))yield return next;}
        }
        public async Task VerifyInterface(string file) {
            if(pad==null || status==null || status.ConfigVersion!=1)throw new InvalidOperationException("Connect a Stadia with the current driver for the interface test.");
            Profile hardware=pad.Configuration(),original=draft.Clone();string name=originalName;bool existed=File.Exists(ProfileLibrary.FilePath);
            byte[] previous=existed?File.ReadAllBytes(ProfileLibrary.FilePath):null;bool backupExisted=File.Exists(ProfileLibrary.FilePath+".bak");byte[] backup=backupExisted?File.ReadAllBytes(ProfileLibrary.FilePath+".bak"):null;
            ProfileLibrary saved=new ProfileLibrary{Selected=library.Selected,Applied=library.Applied,Profiles=library.Profiles.Select(p=>p.Clone()).ToList()};Profile previousActions=activeActions;Action<ushort,bool> sender=desktopActions.Send;desktopActions.Send=(k,u)=>{};
            try {
                Navigate("Mando");SelectButton(0);destinationPicker.SelectedItem=Destination.All.First(d=>d.Id==2);
                if(draft.Map[0]!=2 || !dirty)throw new Exception("Mapping selection did not update the profile.");
                await ApplyDraft();if(pad.Configuration().Map[0]!=2)throw new Exception("Mapping was not applied by the save action.");
                Navigate("Sticks y gatillos");UpdateLayout();Slider deadzone=VisualChildren<Slider>(page).First();deadzone.Value=17;
                if(draft.LeftDeadzone!=17)throw new Exception("Deadzone slider did not update the profile.");await ApplyDraft();if(pad.Configuration().LeftDeadzone!=17)throw new Exception("Deadzone was not applied.");
                Navigate("Vibración");UpdateLayout();Slider motor=VisualChildren<Slider>(page).First();motor.Value=42;
                if(draft.StrongScale!=42)throw new Exception("Motor slider did not update the profile.");await ApplyDraft();if(pad.Configuration().StrongScale!=42)throw new Exception("Motor intensity was not applied.");
                int count=library.Profiles.Count;DuplicateProfile();if(library.Profiles.Count!=count+1 || draft.Map[0]!=2 || draft.StrongScale!=42)throw new Exception("Profile duplication failed.");
                File.WriteAllText(file,"PASS: interactive mapping selection and Save action, deadzone slider, motor intensity slider, profile duplication, real HID confirmation, original configuration restored.");
            }finally {
                pad.Apply(hardware);library=saved;draft=original;originalName=name;dirty=false;pendingApply=false;
                if(existed)File.WriteAllBytes(ProfileLibrary.FilePath,previous);else if(File.Exists(ProfileLibrary.FilePath))File.Delete(ProfileLibrary.FilePath);
                if(backupExisted)File.WriteAllBytes(ProfileLibrary.FilePath+".bak",backup);else if(File.Exists(ProfileLibrary.FilePath+".bak"))File.Delete(ProfileLibrary.FilePath+".bak");
                desktopActions.Release();desktopActions.Send=sender;activeActions=previousActions;FillProfiles();Navigate("Mando");
            }
        }
    }
}















