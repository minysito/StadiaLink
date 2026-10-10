param([switch]$Driver)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
& "$PSScriptRoot\build.ps1" -Driver:$Driver
$scratch=[IO.Path]::GetFullPath((Join-Path $root '.build/app'))
$stage=Join-Path $scratch 'stage'
if([IO.Path]::GetFullPath($stage) -ine (Join-Path $scratch 'stage')){throw 'Invalid staging path'}
if(Test-Path -LiteralPath $stage){Remove-Item -LiteralPath $stage -Recurse -Force}
New-Item -ItemType Directory -Force $stage,(Join-Path $stage 'driver/pkg') | Out-Null
$release=Join-Path $root 'release'
Copy-Item "$release\StadiaLink.exe","$release\StadiaLink.exe.config" $stage
Copy-Item "$release\StadiaDevice.dll" $stage
Copy-Item "$root\driver\out\pkg\winstadia.dll","$root\driver\out\pkg\winstadia.inf" (Join-Path $stage 'driver/pkg')
Copy-Item "$root\driver\install.ps1" (Join-Path $stage 'driver')
Copy-Item "$root\LICENSE","$root\UPSTREAM-README.md","$PSScriptRoot\USO.md" $stage
$sourceStage=Join-Path $scratch 'source'
if(Test-Path -LiteralPath $sourceStage){if([IO.Path]::GetFullPath($sourceStage) -ine (Join-Path $scratch 'source')){throw 'Invalid source path'};Remove-Item -LiteralPath $sourceStage -Recurse -Force}
New-Item -ItemType Directory -Force $sourceStage | Out-Null
foreach($name in @('app','driver','tests')){
    $target=Join-Path $sourceStage $name
    New-Item -ItemType Directory -Force $target | Out-Null
    Get-ChildItem (Join-Path $root $name) -File | Where-Object {$_.Extension -in '.cs','.c','.cpp','.h','.ps1','.inf','.manifest','.config','.md'} | Copy-Item -Destination $target
}
Copy-Item -LiteralPath "$PSScriptRoot\assets" -Destination (Join-Path $sourceStage 'app/assets') -Recurse
Copy-Item "$root\LICENSE","$root\UPSTREAM-README.md","$root\README*.md","$root\CHANGELOG.md" $sourceStage
Compress-Archive -Path "$sourceStage\*" -DestinationPath "$stage\Source.zip" -Force
$payload=Join-Path $scratch 'payload.zip'
Compress-Archive -Path "$stage\*" -DestinationPath $payload -Force
$hash=(Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash
$hashSource=Join-Path $scratch 'PayloadInfo.cs'
Set-Content -LiteralPath $hashSource -Value ('namespace StadiaStudio { public static class PayloadInfo { public const string Sha256="'+$hash+'"; } }') -Encoding utf8
$framework="$env:windir\Microsoft.NET\Framework64\v4.0.30319"
$refs=@('System.dll','System.Core.dll','System.Runtime.Serialization.dll','System.Xaml.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll') | ForEach-Object { '/reference:'+(Join-Path $framework $_) }
$sources='Setup.cs','Ui.cs','Model.cs','InstallerActions.cs','I18n.cs' | ForEach-Object {Join-Path $PSScriptRoot $_}
& "$framework\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 /win32manifest:"$PSScriptRoot\app.manifest" /win32icon:"$PSScriptRoot\StadiaLink.ico" /out:"$release\StadiaLink.Setup.exe" /resource:"$payload,payload.zip" /resource:"$root\LICENSE,LICENSE" /resource:"$PSScriptRoot\assets\locales.tsv,locales.tsv" $refs $sources $hashSource
if($LASTEXITCODE){throw 'Installer compilation failed'}
Copy-Item "$PSScriptRoot\StadiaLink.exe.config" "$release\StadiaLink.Setup.exe.config"
Get-FileHash "$release\StadiaLink.Setup.exe" -Algorithm SHA256 | ForEach-Object { $_.Hash+'  StadiaLink.Setup.exe' } | Set-Content "$release\SHA256.txt"
Write-Output "Installer ready: $release\StadiaLink.Setup.exe"

