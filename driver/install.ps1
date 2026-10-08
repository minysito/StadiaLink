# Installs (or with -Uninstall removes) the driver package, built by build.ps1
# or taken from a release: signs it for this machine and puts the driver on
# Stadia controllers, connected now or later.
# Elevates itself; output goes to install.log next to the package.
param([switch]$Uninstall,[switch]$Reconnect)

$ErrorActionPreference = 'Stop'
Import-Module Microsoft.PowerShell.Security

# A release has the package next to this script, a checkout has it in the
# build output.
$pkg = Join-Path $PSScriptRoot 'pkg'
if (-not (Test-Path $pkg)) { $pkg = Join-Path $PSScriptRoot 'out\pkg' }
$inf = Join-Path $pkg 'winstadia.inf'
$cat = Join-Path $pkg 'winstadia.cat'
$log = Join-Path (Split-Path $pkg -Parent) 'install.log'
$certSubject = 'CN=WinStadia driver signing'
$stores = 'Root', 'TrustedPublisher'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$admin = [Security.Principal.WindowsBuiltInRole]::Administrator
if (-not ([Security.Principal.WindowsPrincipal]$identity).IsInRole($admin)) {
    $arguments = '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`""
    if ($Uninstall) { $arguments += '-Uninstall' }
    if ($Reconnect) { $arguments += '-Reconnect' }
    $process = Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -Wait -PassThru -ArgumentList $arguments
    Get-Content $log
    if ($process.ExitCode -ne 0) { throw "Installer failed: $($process.ExitCode)" }
    return
}

# Driver packages of this driver in the driver store, whatever their version.
function Get-InstalledPackage {
    Get-ChildItem "$env:windir\INF\oem*.inf" |
        Where-Object { Select-String -Path $_ -Pattern 'winstadia\.dll' -Quiet }
}

# Takes the trust away from this driver's certificates, from all of them or
# from those the filter picks.
function Remove-Trust([scriptblock]$filter = { $true }) {
    foreach ($store in $stores) {
        Get-ChildItem "Cert:\LocalMachine\$store" |
            Where-Object { $_.Subject -eq $certSubject } |
            Where-Object $filter |
            Remove-Item
    }
}

# Windows wants driver packages signed by someone the machine trusts. The
# certificate for that is made here and trusted only here, and its private key
# is gone again once the package is signed, so nothing else can ever be signed
# with it.
function Add-Signature {
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $certSubject `
        -CertStoreLocation Cert:\LocalMachine\My -KeyExportPolicy NonExportable -NotAfter (Get-Date).AddYears(30)
    try {
        # Signing checks the result, so the trust has to be there first.
        $public = New-Object Security.Cryptography.X509Certificates.X509Certificate2 (, $cert.RawData)
        foreach ($store in $stores) {
            $target = New-Object Security.Cryptography.X509Certificates.X509Store $store, 'LocalMachine'
            $target.Open('ReadWrite')
            $target.Add($public)
            $target.Close()
        }

        Remove-Item $cat -ErrorAction SilentlyContinue
        New-FileCatalog -Path $pkg -CatalogFilePath $cat -CatalogVersion 2 | Out-Null
        $signature = Set-AuthenticodeSignature -FilePath $cat -Certificate $cert -HashAlgorithm SHA256
        if ($signature.Status -ne 'Valid') { throw "signing failed: $($signature.StatusMessage)" }
    } finally {
        Remove-Item $cert.PSPath -DeleteKey
    }
    $cert.Thumbprint
}

$failed = $false
Start-Transcript $log -Force | Out-Null
try {
    if ($Uninstall) {
        # Returns the controller to the inbox HID driver.
        Get-InstalledPackage | ForEach-Object { pnputil /delete-driver $_.Name /uninstall }
        Remove-Trust
    } else {
        if (-not (Test-Path $inf)) { throw "no driver package in $pkg, run build.ps1 first" }
        $thumbprint = Add-Signature
        $previous = @(Get-InstalledPackage)
        pnputil /add-driver $inf /install
        $exitCode = $LASTEXITCODE
        "pnputil exit code: $exitCode"
        if (@(Get-InstalledPackage).Count -gt $previous.Count) {
            # Earlier versions would pile up in the driver store. A controller
            # that is not connected right now is still on one, and moves to the
            # new package with this.
            $previous | ForEach-Object { pnputil /delete-driver $_.Name /uninstall }
            Remove-Trust { $_.Thumbprint -ne $thumbprint }
        } else {
            # The same build was in the store already, under the certificate
            # it got back then.
            Remove-Trust { $_.Thumbprint -eq $thumbprint }
        }
        # 259 = the package was staged but no controller was connected to
        # install onto yet. Checked after the cleanup so a failed install
        # still gets its certificate trust removed.
        if ($exitCode -notin 0, 259, 3010) {
            throw "pnputil /add-driver failed with exit code $exitCode"
        }
        if ($exitCode -eq 3010) {
            # Something held the controller open, so it could not restart.
            'Reconnect the controller to finish. Over Bluetooth, switch Bluetooth off and on.'
        }
        if ($Reconnect) {
            # Limit reconnection to parents of the exact Stadia HID service.
            # No other Bluetooth device or the adapter is disabled.
            $parents = @(Get-PnpDevice -PresentOnly | Where-Object {
                $_.InstanceId -like 'BTHLEDEVICE\{00001812-0000-1000-8000-00805F9B34FB}_DEV_VID&0218D1_PID&9400*'
            } | ForEach-Object {
                (Get-PnpDeviceProperty -InstanceId $_.InstanceId -KeyName 'DEVPKEY_Device_Parent').Data
            } | Where-Object { $_ -match '^BTHLE\\DEV_[0-9A-F]{12}\\' } | Select-Object -Unique)
            foreach ($parent in $parents) {
                # The Parent property can use different casing from the CIM
                # device key. Pass the actual enumerated instance to PnpDevice.
                $actualParent = @(Get-PnpDevice -PresentOnly | Where-Object { $_.InstanceId -ieq $parent })
                if ($actualParent.Count -ne 1) {
                    Write-Warning 'Stadia parent not currently present. Reconnect the controller if needed.'
                    continue
                }
                $parent = $actualParent[0].InstanceId
                try {
                    Disable-PnpDevice -InstanceId $parent -Confirm:$false -ErrorAction Stop
                    'Stadia Bluetooth device disabled for driver activation.'
                } catch {
                    # PnP may already have restarted the stack after replacing
                    # its package. An optional restart failure is not an install
                    # failure; keep the device enabled and allow normal pairing.
                    Write-Warning "Optional Stadia reconnection: $($_.Exception.Message). Reconnect manually if needed."
                } finally {
                    try {
                        Enable-PnpDevice -InstanceId $parent -Confirm:$false -ErrorAction Stop
                        'Stadia Bluetooth device enabled.'
                    } catch {
                        Write-Warning "Enable the Stadia again or reconnect it: $($_.Exception.Message)"
                    }
                }
            }
        }
    }
} catch {
    $failed = $true
    "FAILED: $_"
} finally {
    Stop-Transcript | Out-Null
}
if ($failed) { exit 1 }
