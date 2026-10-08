<div align="center">
  <img src="app/assets/readme-logo.svg" width="112" height="112" alt="StadiaLink logo">
  <h1>StadiaLink</h1>
  <p><strong>Your Stadia. Your way.</strong></p>
  <p>Stadia controller configuration for Windows 11 x64 · Version 1.0</p>
  <p><a href="https://github.com/minysito/StadiaLink/releases/latest"><strong>⬇ Download</strong></a> · <a href="https://github.com/minysito/StadiaLink/issues">Report an issue</a> · <a href="LICENSE">GPL-3.0</a></p>
  <p>🪟 Windows 11 x64 &nbsp; · &nbsp; 🎮 XInput &nbsp; · &nbsp; 🔗 USB + Bluetooth</p>
  <p><a href="README.md"><img src="app/assets/flag-es.svg" width="24" height="16" alt="ES"> Español</a> &nbsp; | &nbsp; <strong><img src="app/assets/flag-en.svg" width="24" height="16" alt="EN"> English</strong> &nbsp; | &nbsp; <a href="README.fr.md"><img src="app/assets/flag-fr.svg" width="24" height="16" alt="FR"> Français</a> &nbsp; | &nbsp; <a href="README.de.md"><img src="app/assets/flag-de.svg" width="24" height="16" alt="DE"> Deutsch</a></p>
</div>

---

## 🎮 Features

- USB and Bluetooth connections with XInput output.
- Button mapping, keyboard shortcuts and push to talk.
- Stick sensitivity, dead zones and trigger adjustments.
- Per-motor vibration and a continuous test with manual stop.
- Importable and exportable profiles.
- Battery level, notifications, system tray and Windows startup.
- Responsive interface in Spanish, English, French and German.

## 🚀 Installation

Download `StadiaLink.Setup.exe` from [Releases](https://github.com/minysito/StadiaLink/releases/latest), run it and approve the administrator prompt for driver installation. Connect your controller by USB or pair it in Windows Bluetooth settings and turn it on. StadiaLink detects the available controller.

Bluetooth requires the controller's Bluetooth firmware. StadiaLink does not modify firmware. The installer includes its components and corresponding source code and works offline. It uses .NET Framework 4.8 and Windows components; no third-party controller emulator or additional runtime is required.

Compare the download hash with `SHA256SUMS.txt` from the same release:

```powershell
Get-FileHash .\StadiaLink.Setup.exe -Algorithm SHA256
```

## 🛠 Build from scratch

### Requirements

- Windows 11 x64 and the included Windows PowerShell 5.1.
- [Git for Windows](https://git-scm.com/downloads/win), or GitHub's source ZIP.
- [Visual Studio 2022 or Build Tools 2022](https://visualstudio.microsoft.com/downloads/) with **Desktop development with C++**, MSVC x64 and Windows SDK **10.0.26100.0**.
- .NET Framework **4.8**. Scripts use `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe` and Windows WPF libraries. Node.js and `dotnet build` are not required.
- Internet for the first driver build. The script downloads `Microsoft.Windows.WDK.x64` **10.0.26100.6584** from NuGet, uses UMDF **2.31** and caches the WDK in `.build/driver/.wdk`.

Open Windows PowerShell in a working folder. Building does not require administrator privileges; installing the driver does.

```powershell
git clone https://github.com/minysito/StadiaLink.git
cd StadiaLink

# If scripts are blocked; applies only to this session.
Set-ExecutionPolicy -Scope Process -ExecutionPolicy RemoteSigned

# Driver, application, native library and installer.
.\app\package.ps1 -Driver

# Input translation and application model tests.
.\tests\run.ps1
```

If using the ZIP, extract it and open PowerShell in the folder containing `app`, `driver` and `tests`. Review scripts before unblocking them if Windows marks them as Internet downloads. The example does not change the machine's permanent execution policy.

| Path | Output |
| --- | --- |
| `release/StadiaLink.exe` | x64 WPF application. |
| `release/StadiaLink.exe.config` | .NET Framework configuration. |
| `release/StadiaDevice.dll` | Native battery reader. |
| `release/StadiaLink.Setup.exe` | Installer with components and embedded `Source.zip`. |
| `release/StadiaLink.Setup.exe.config` | Installer configuration. |
| `release/SHA256.txt` | Hash of your locally built installer. |
| `driver/out/pkg/` | Compiled UMDF driver package. |
| `.build/` | Dependencies, object files and test results. |

Run `release/StadiaLink.Setup.exe` to install your build. Running the application alone does not install the driver. For separate build steps:

```powershell
.\driver\build.ps1       # Driver only.
.\app\build.ps1          # Application and native library.
.\app\package.ps1        # Installer using the existing driver build.
.\tests\run.ps1          # Requires the built application.
```

If MSVC is missing, check Visual Studio's C++ workload. Missing headers or libraries usually require the specified SDK. Check NuGet access if the WDK download fails. Driver builds include a build date and version; binaries are not necessarily identical between builds.

## ⚙️ Usage

Adjust a profile and click **Save and apply**. Switching tabs discards unsaved profile changes. The Windows startup checkbox saves immediately and starts StadiaLink in the tray when that user signs in. Continuous vibration stops with **Stop**, on tab changes, disconnection or app exit.

Keyboard shortcuts require the app to remain open, including in the tray. Configure the same push-to-talk key or combination in your game or voice app. The driver retains the applied XInput configuration when the interface closes.

Profiles are stored at `%LOCALAPPDATA%\StadiaStudio\profiles.json`; the installation folder is `%LOCALAPPDATA%\Programs\StadiaStudio`. These internal names are retained for compatibility with earlier versions.

## 🔋 Battery detection

No firmware activation or separate utility is needed. After detecting a controller, `StadiaDevice.dll` reads its percentage in the background every **30 seconds**, displaying it in the interface and tray icon.

- **Bluetooth:** Windows must enumerate the paired controller's BLE Battery Service (`0x180F`). The library reads Battery Level (`0x2A19`) directly from that device and validates a one-byte value from 0 to 100. It does not write to the battery service or modify HID.
- **USB:** the library locates the Stadia WinUSB interface `VID_18D1 / PID_9400 / MI_00`, requests the value with control request `0x83` and retrieves it with `0x84`, validating its format and percentage. It does not write firmware.
- **Charging:** USB connection displays **Charging**, or **Fully charged** if the returned level is 100 %. This is inferred from the connection; it does not measure charging current or confirm physical charging.
- **Notifications:** when enabled, connection, disconnection, battery at **15 % or below**, and **100 %** generate notifications. Full-battery notification requires a valid reading.

**Battery unavailable** means the level could not be read; no percentage is invented. On Bluetooth, power-cycle the controller and wait for another reading. If needed, remove and pair it again so Windows re-enumerates its services. On USB, check the data connection and WinUSB interface availability. Working buttons do not guarantee access to battery data.

## 💬 Issues, contributions and removal

Open an [issue](https://github.com/minysito/StadiaLink/issues) with Windows version, app version, connection type and reproduction steps. Installation logs are at `%LOCALAPPDATA%\StadiaStudio\logs\driver-install.log`; remove personal information before sharing them.

Pull requests are welcome. Describe your checks and run `tests/run.ps1` when changing configuration or input translation. Do not commit downloaded dependencies, generated binaries, personal profiles or firmware research material.

Uninstall through Windows **Installed apps**. To remove only the driver from a built checkout:

```powershell
.\driver\install.ps1 -Uninstall
```

This requests administrator privileges, removes this driver's packages and returns the controller to Windows' built-in HID driver. Reconnection may be needed.

## 🤝 Compatibility, credits and license

Bluetooth vibration uses private Windows interfaces tested on Windows 11 build **26200**; system updates may change them. The audio jack works over USB. Bluetooth audio, integrated microphone activation and Wi-Fi activation are not provided. Timing figures measure HID queries and interface updates, not total physical latency through a game and display.

Part of this project was made possible by **aisk's WinStadia**, from which StadiaLink's native driver derives. Full upstream repository: **https://github.com/aisk/WinStadia**.

Upstream reference: [commit e9e84a0a24c3cc13bf7cc7ac2b8baa2b653ce80a](https://github.com/aisk/WinStadia/commit/e9e84a0a24c3cc13bf7cc7ac2b8baa2b653ce80a). Copyright notices are retained. StadiaLink and its modifications are distributed under **GPL-3.0**; see [LICENSE](LICENSE) and [UPSTREAM-README.md](UPSTREAM-README.md). This project is independent of Google and Microsoft.

## ✨ Development with ChatGPT / Codex

The project is managed and developed with **ChatGPT / Codex**, using **vibe coding**: the project owner defines features and reviews results; Codex helps implement, investigate, document and run checks. Physical-controller testing and change review are part of the work. This assistance does not imply certification or endorsement by OpenAI, Google or Microsoft.
