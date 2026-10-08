<div align="center">
  <img src="app/assets/readme-logo.svg" width="112" height="112" alt="StadiaLink-Logo">
  <h1>StadiaLink</h1>
  <p><strong>Dein Stadia. Deine Einstellungen.</strong></p>
  <p>Stadia-Controller für Windows 11 x64 konfigurieren · Version 1.0</p>
  <p><a href="https://github.com/minysito/StadiaLink/releases/latest"><strong>⬇ Herunterladen</strong></a> · <a href="https://github.com/minysito/StadiaLink/issues">Problem melden</a> · <a href="LICENSE">GPL-3.0</a></p>
  <p>🪟 Windows 11 x64 &nbsp; · &nbsp; 🎮 XInput &nbsp; · &nbsp; 🔗 USB + Bluetooth</p>
  <p><a href="README.md"><img src="app/assets/flag-es.svg" width="24" height="16" alt="ES"> Español</a> &nbsp; | &nbsp; <a href="README.en.md"><img src="app/assets/flag-en.svg" width="24" height="16" alt="EN"> English</a> &nbsp; | &nbsp; <a href="README.fr.md"><img src="app/assets/flag-fr.svg" width="24" height="16" alt="FR"> Français</a> &nbsp; | &nbsp; <strong><img src="app/assets/flag-de.svg" width="24" height="16" alt="DE"> Deutsch</strong></p>
</div>

---

## 🎮 Funktionen

- USB und Bluetooth mit XInput-Ausgabe.
- Tastenbelegung, Tastenkombinationen und Push-to-Talk.
- Stick-Empfindlichkeit, Totzonen und Trigger-Einstellungen.
- Vibration je Motor und Dauertest mit manuellem Stopp.
- Profile importieren und exportieren.
- Akkustand, Benachrichtigungen, Infobereich und Windows-Autostart.
- Anpassbare Oberfläche auf Spanisch, Englisch, Französisch und Deutsch.

## 🚀 Installation

Lade `StadiaLink.Setup.exe` unter [Releases](https://github.com/minysito/StadiaLink/releases/latest) herunter, starte das Installationsprogramm und bestätige die Administratorabfrage für den Treiber. Verbinde den Controller über USB oder kopple ihn in den Windows-Bluetooth-Einstellungen und schalte ihn ein. StadiaLink erkennt den verfügbaren Controller.

Bluetooth setzt die Bluetooth-Firmware des Controllers voraus. StadiaLink verändert keine Firmware. Das Installationsprogramm enthält die Komponenten und den zugehörigen Quellcode und funktioniert offline. Es nutzt .NET Framework 4.8 und Windows-Komponenten; zusätzliche Controller-Emulatoren oder Laufzeitumgebungen von Drittanbietern sind nicht erforderlich.

Vergleiche die Prüfsumme mit `SHA256SUMS.txt` derselben Version:

```powershell
Get-FileHash .\StadiaLink.Setup.exe -Algorithm SHA256
```

## 🛠 Von Grund auf kompilieren

### Voraussetzungen

- Windows 11 x64 und das enthaltene Windows PowerShell 5.1.
- [Git für Windows](https://git-scm.com/downloads/win) oder das Quellcode-ZIP von GitHub.
- [Visual Studio 2022 oder Build Tools 2022](https://visualstudio.microsoft.com/downloads/) mit **Desktopentwicklung mit C++**, MSVC x64 und Windows SDK **10.0.26100.0**.
- .NET Framework **4.8**. Die Skripte verwenden `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe` und die WPF-Bibliotheken von Windows. Node.js und `dotnet build` werden nicht benötigt.
- Internet beim ersten Treiber-Build: Das Skript lädt `Microsoft.Windows.WDK.x64` **10.0.26100.6584** von NuGet, nutzt UMDF **2.31** und speichert das WDK unter `.build/driver/.wdk`.

Öffne Windows PowerShell in einem Arbeitsordner. Das Kompilieren braucht keine Administratorrechte; die Treiberinstallation dagegen schon.

```powershell
git clone https://github.com/minysito/StadiaLink.git
cd StadiaLink

# Falls Skripte blockiert sind, nur für diese Sitzung.
Set-ExecutionPolicy -Scope Process -ExecutionPolicy RemoteSigned

# Treiber, Anwendung, native Bibliothek und Installationsprogramm.
.\app\package.ps1 -Driver

# Tests für Eingabeübersetzung und Anwendungsmodell.
.\tests\run.ps1
```

Entpacke bei Verwendung des ZIPs das Archiv und öffne PowerShell im Ordner mit `app`, `driver` und `tests`. Prüfe die Skripte, bevor du sie entsperrst, wenn Windows sie als Internetdownload markiert. Das Beispiel ändert keine dauerhafte Ausführungsrichtlinie des PCs.

| Pfad | Ergebnis |
| --- | --- |
| `release/StadiaLink.exe` | WPF-Anwendung für x64. |
| `release/StadiaLink.exe.config` | .NET-Framework-Konfiguration. |
| `release/StadiaDevice.dll` | Native Abfrage des Akkustands. |
| `release/StadiaLink.Setup.exe` | Installationsprogramm mit Komponenten und eingebettetem `Source.zip`. |
| `release/StadiaLink.Setup.exe.config` | Konfiguration des Installationsprogramms. |
| `release/SHA256.txt` | Prüfsumme des lokal erstellten Installationsprogramms. |
| `driver/out/pkg/` | Kompiliertes UMDF-Treiberpaket. |
| `.build/` | Abhängigkeiten, Objektdateien und Testergebnisse. |

Starte `release/StadiaLink.Setup.exe`, um deinen Build zu installieren. Das Starten der Anwendung allein installiert keinen Treiber. Einzelne Schritte:

```powershell
.\driver\build.ps1       # Nur der Treiber.
.\app\build.ps1          # Anwendung und native Bibliothek.
.\app\package.ps1        # Installer mit bereits kompiliertem Treiber.
.\tests\run.ps1          # Benötigt die kompilierte Anwendung.
```

Prüfe bei fehlendem MSVC die C++-Workload von Visual Studio. Installiere bei fehlenden Headern oder Bibliotheken das angegebene SDK. Prüfe den NuGet-Zugriff, falls der WDK-Download fehlschlägt. Treiber-Builds enthalten ein Datum und eine Version; ihre Binärdateien müssen zwischen Builds nicht identisch sein.

## ⚙️ Verwendung

Passe ein Profil an und klicke auf **Speichern und anwenden**. Ein Tabwechsel verwirft ungespeicherte Profiländerungen. Die Autostart-Checkbox wird sofort gespeichert und startet StadiaLink bei der Anmeldung dieses Benutzers im Infobereich. Dauervibration endet mit **Stoppen**, beim Tabwechsel, beim Verbindungsabbruch oder beim Beenden.

Tastenkombinationen benötigen die geöffnete Anwendung, auch minimiert im Infobereich. Stelle dieselbe Push-to-Talk-Taste oder Kombination im Spiel oder Sprachprogramm ein. Der Treiber behält die angewendete XInput-Konfiguration nach dem Schließen der Oberfläche bei.

Profile liegen unter `%LOCALAPPDATA%\StadiaStudio\profiles.json`, die Installation unter `%LOCALAPPDATA%\Programs\StadiaStudio`. Diese internen Namen bleiben aus Kompatibilitätsgründen erhalten.

## 🔋 Akkustand erkennen

Es ist keine Firmware-Aktivierung und kein zusätzliches Programm erforderlich. `StadiaDevice.dll` fragt den Akkustand alle **30 Sekunden** im Hintergrund ab und zeigt ihn in der Oberfläche und im Symbol des Infobereichs an.

- **Bluetooth:** Windows muss den BLE Battery Service (`0x180F`) des gekoppelten Controllers bereitstellen. Die Bibliothek liest Battery Level (`0x2A19`) direkt vom passenden Gerät und prüft einen Byte-Wert von 0 bis 100. Sie schreibt nicht in den Akkudienst und verändert HID nicht.
- **USB:** Die Bibliothek sucht die Stadia-WinUSB-Schnittstelle `VID_18D1 / PID_9400 / MI_00`, fordert den Wert mit Steueranfrage `0x83` an und liest ihn mit `0x84`. Format und Prozentwert werden geprüft; es wird keine Firmware geschrieben.
- **Laden:** Bei USB erscheint **Wird geladen**, bei einem gelesenen Wert von 100 % **Vollständig geladen**. Dies wird aus der Verbindung abgeleitet; weder Ladestrom noch tatsächliches Laden werden gemessen.
- **Benachrichtigungen:** Wenn aktiviert, erscheinen Meldungen zu Verbindung, Trennung, Akkustand von **15 % oder weniger** und **100 %**. Die Meldung bei vollem Akku benötigt einen gültigen Messwert.

**Akkustand nicht verfügbar** bedeutet, dass die Abfrage gescheitert ist; ein Wert wird nicht erfunden. Schalte den Controller bei Bluetooth aus und wieder ein und warte auf die nächste Abfrage. Entferne bei Bedarf die Kopplung und kopple erneut, damit Windows die Dienste neu erkennt. Prüfe bei USB die Datenverbindung und die WinUSB-Schnittstelle. Funktionierende Tasten garantieren keinen Zugriff auf den Akkustand.

## 💬 Probleme, Beiträge und Deinstallation

Öffne ein [Issue](https://github.com/minysito/StadiaLink/issues) mit Windows-Version, App-Version, Verbindungstyp und Schritten zur Reproduktion. Das Installationsprotokoll liegt unter `%LOCALAPPDATA%\StadiaStudio\logs\driver-install.log`; entferne persönliche Informationen vor dem Teilen.

Pull Requests sind willkommen. Beschreibe die Prüfungen und führe bei Änderungen an Konfiguration oder Eingabeübersetzung `tests/run.ps1` aus. Lade keine heruntergeladenen Abhängigkeiten, erzeugten Binärdateien, persönlichen Profile oder Firmware-Forschungsmaterialien hoch.

Deinstalliere über **Installierte Apps** in Windows. Um nur den Treiber einer kompilierten Arbeitskopie zu entfernen:

```powershell
.\driver\install.ps1 -Uninstall
```

Das Skript fordert Administratorrechte an, entfernt die Pakete dieses Treibers und stellt den Windows-HID-Treiber wieder her. Erneutes Verbinden kann nötig sein.

## 🤝 Kompatibilität, Danksagung und Lizenz

Bluetooth-Vibration nutzt private Windows-Schnittstellen, getestet auf Windows 11 Build **26200**. Systemupdates können sie verändern. Die Audiobuchse funktioniert über USB. Bluetooth-Audio sowie die Aktivierung des integrierten Mikrofons oder von WLAN werden nicht angeboten. Zeitmessungen betreffen HID-Abfragen und die Oberfläche, nicht die gesamte physische Latenz von Spiel und Bildschirm.

Teile dieses Projekts wurden durch **aisks WinStadia** ermöglicht, von dem der native Treiber abgeleitet ist. Vollständiges Original-Repository: **https://github.com/aisk/WinStadia**.

Ausgangsstand: [Commit e9e84a0a24c3cc13bf7cc7ac2b8baa2b653ce80a](https://github.com/aisk/WinStadia/commit/e9e84a0a24c3cc13bf7cc7ac2b8baa2b653ce80a). Copyright-Hinweise bleiben erhalten. StadiaLink und seine Änderungen stehen unter **GPL-3.0**; siehe [LICENSE](LICENSE) und [UPSTREAM-README.md](UPSTREAM-README.md). Unabhängiges Projekt ohne Verbindung zu Google oder Microsoft.

## ✨ Entwicklung mit ChatGPT / Codex

Das Projekt wird mit **ChatGPT / Codex** verwaltet und entwickelt, im Rahmen von **Vibe Coding**: Der Projektverantwortliche legt Funktionen fest und prüft die Ergebnisse; Codex unterstützt Implementierung, Recherche, Dokumentation und Prüfungen. Tests mit dem physischen Controller und die Prüfung der Änderungen gehören dazu. Diese Unterstützung bedeutet keine Zertifizierung oder Empfehlung durch OpenAI, Google oder Microsoft.
