param([switch]$Driver)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if($Driver){& "$root\driver\build.ps1";if($LASTEXITCODE){throw 'Driver build failed'}}
$out=Join-Path $root 'release'
New-Item -ItemType Directory -Force $out | Out-Null
$framework="$env:windir\Microsoft.NET\Framework64\v4.0.30319"
$refs=@('System.dll','System.Core.dll','System.Runtime.Serialization.dll','System.Xaml.dll','System.Windows.Forms.dll','System.Drawing.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll') | ForEach-Object { '/reference:'+(Join-Path $framework $_) }
$sources='App.cs','Ui.cs','Model.cs','Controller.cs','ControllerVisual.cs','MainWindow.cs','InstallerActions.cs','DesktopActions.cs','BatteryTray.cs','DeviceNotifications.cs','I18n.cs' | ForEach-Object {Join-Path $PSScriptRoot $_}
& "$PSScriptRoot\create-icon.ps1" -OutputPath "$PSScriptRoot\StadiaLink.ico"
& "$framework\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 /win32manifest:"$PSScriptRoot\app.manifest" /win32icon:"$PSScriptRoot\StadiaLink.ico" /resource:"$PSScriptRoot\assets\locales.tsv,locales.tsv" /out:"$out\StadiaLink.exe" $refs $sources
if($LASTEXITCODE){throw 'App compilation failed'}
Copy-Item "$PSScriptRoot\StadiaLink.exe.config" $out
& "$PSScriptRoot\build-native.ps1"
Write-Output "Application ready: $out\StadiaLink.exe"

