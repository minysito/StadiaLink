$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$out=Join-Path $root '.build/app'
New-Item -ItemType Directory -Force $out | Out-Null
$vswhere="${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vs=& $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
cmd /c "`"$vs\VC\Auxiliary\Build\vcvars64.bat`" >nul 2>&1 && set" | ForEach-Object {if($_ -match '^([^=]+)=(.*)$'){Set-Item "env:$($Matches[1])" $Matches[2]}}
& cl /nologo /W4 /O2 /MT /EHsc /std:c++17 /Fo"$out\configuration-tests.obj" /Fe"$out\configuration-tests.exe" "$PSScriptRoot\configuration-tests.cpp"
if($LASTEXITCODE){throw 'Configuration tests compilation failed'}
& "$out\configuration-tests.exe" | Tee-Object "$out\translation-tests.txt"
if($LASTEXITCODE){throw 'Translation tests failed'}
$process=Start-Process "$root\release\StadiaLink.exe" -ArgumentList '--self-test',('"'+[IO.Path]::GetFullPath("$out\model-tests.txt")+'"') -PassThru -Wait -WindowStyle Hidden
if($process.ExitCode){throw 'Application model tests failed'}
Get-Content "$out\model-tests.txt"
