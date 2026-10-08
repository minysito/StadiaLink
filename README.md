# StadiaLink

Aplicación para configurar el mando Stadia en Windows 11 x64. Versión 1.0.

[Descargar](https://github.com/minysito/StadiaLink/releases/latest) · [Comunicar un problema](https://github.com/minysito/StadiaLink/issues) · [GPL-3.0](LICENSE)

## Funciones

- Conexión USB y Bluetooth con salida compatible con XInput.
- Asignación de botones, atajos y pulsar para hablar.
- Ajuste de sticks, zonas muertas, sensibilidad y gatillos.
- Vibración por motor y prueba continua con parada manual.
- Perfiles importables y exportables.
- Batería, avisos, bandeja e inicio con Windows.
- Español, inglés, francés y alemán.

## Instalación

Ejecuta `StadiaLink.Setup.exe`. La instalación del controlador requiere permiso de administrador. El instalador incluye sus componentes y funciona sin Internet. Empareja el mando desde los ajustes Bluetooth de Windows o conéctalo por USB.

Descarga el instalador desde [Releases](https://github.com/minysito/StadiaLink/releases/latest). Para Bluetooth, el mando debe tener el firmware Bluetooth instalado; StadiaLink no modifica su firmware. No requiere emuladores de mando ni runtimes adicionales de terceros: utiliza .NET Framework 4.8 y componentes de Windows.

Comprueba la descarga comparando el resultado con `SHA256SUMS.txt` de la misma versión:

```powershell
Get-FileHash .\StadiaLink.Setup.exe -Algorithm SHA256
```

## Compilación

### Herramientas necesarias

- Windows 11 x64 y Windows PowerShell 5.1, incluido en Windows.
- [Git para Windows](https://git-scm.com/downloads/win), o el ZIP del código de GitHub.
- [Visual Studio 2022 o Build Tools 2022](https://visualstudio.microsoft.com/downloads/), con **Desarrollo para el escritorio con C++**, MSVC x64 y Windows SDK **10.0.26100.0**.
- .NET Framework **4.8**. Los scripts utilizan el compilador de `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe` y las bibliotecas WPF de Windows. No requieren Node.js ni `dotnet build`.
- Internet durante la primera compilación del controlador. El script descarga de NuGet `Microsoft.Windows.WDK.x64` **10.0.26100.6584**, utiliza UMDF **2.31** y conserva el WDK en `.build/driver/.wdk`.

### Construcción completa desde cero

Abre Windows PowerShell en una carpeta de trabajo. Compilar no requiere permisos de administrador; instalar el controlador sí.

```powershell
git clone https://github.com/minysito/StadiaLink.git
cd StadiaLink

# Si los scripts están bloqueados, solo para esta sesión.
Set-ExecutionPolicy -Scope Process -ExecutionPolicy RemoteSigned

# Controlador, aplicación, biblioteca nativa e instalador.
.\app\package.ps1 -Driver

# Pruebas de traducción de entradas y del modelo de la aplicación.
.\tests\run.ps1
```

El instalador se genera en `release/`. El código correspondiente se incluye como `Source.zip` dentro del instalador. `.build/` contiene únicamente archivos temporales de compilación.

Si utilizas el ZIP de GitHub, extráelo y abre PowerShell en la carpeta que contiene `app`, `driver` y `tests`. Revisa los scripts antes de desbloquearlos si Windows los marca como descargados de Internet. La política del ejemplo no cambia la configuración permanente del equipo.

| Ruta | Resultado |
| --- | --- |
| `release/StadiaLink.exe` | Aplicación WPF x64. |
| `release/StadiaLink.exe.config` | Configuración de .NET Framework. |
| `release/StadiaDevice.dll` | Consulta nativa de batería. |
| `release/StadiaLink.Setup.exe` | Instalador con componentes y código fuente incorporados. |
| `release/StadiaLink.Setup.exe.config` | Configuración del instalador. |
| `release/SHA256.txt` | Huella del instalador generado localmente. |
| `driver/out/pkg/` | Paquete UMDF compilado, pendiente de la firma local al instalar. |
| `.build/` | Dependencias, objetos y resultados de pruebas. |

Ejecuta `release/StadiaLink.Setup.exe` para instalar tu compilación. Abrir únicamente la aplicación no instala el controlador.

Para construir por partes:

```powershell
.\driver\build.ps1       # Solo el controlador.
.\app\build.ps1          # Aplicación y biblioteca nativa.
.\app\package.ps1        # Instalador con el controlador ya construido.
.\tests\run.ps1          # Requiere la aplicación compilada.
```

Si falta MSVC, revisa la carga de trabajo C++ de Visual Studio. Si faltan cabeceras o bibliotecas, instala el SDK indicado. Si falla la descarga del WDK, comprueba el acceso a NuGet. El controlador incorpora la fecha y versión de cada compilación; los binarios no tienen por qué ser idénticos entre compilaciones.

## Uso

Ajusta el perfil y pulsa **Guardar y aplicar**. Cambiar de pestaña descarta los cambios del perfil sin guardar. La casilla de inicio con Windows se guarda inmediatamente. La vibración continua se detiene con **Detener**, al cambiar de pestaña o al cerrar la app.

Los atajos de Windows requieren la app abierta. Los tiempos de respuesta mostrados en Ajustes miden consultas HID y actualización de la interfaz, no la latencia física completa hasta un juego. El audio del jack funciona por USB; no se ofrece audio Bluetooth ni activación de Wi-Fi.

Los atajos también funcionan con la app minimizada en la bandeja. Para pulsar para hablar, configura la misma tecla o combinación en tu juego o aplicación de voz. El inicio con Windows abre StadiaLink en la bandeja al iniciar sesión con el usuario que lo activó.

Los perfiles se guardan en `%LOCALAPPDATA%\StadiaStudio\profiles.json`. La app se instala en `%LOCALAPPDATA%\Programs\StadiaStudio`. Estos nombres internos se mantienen por compatibilidad con las versiones anteriores.

## Batería: detección y activación

No hay que activar una función del firmware ni instalar otra utilidad. Al detectar el mando, la app consulta el porcentaje en segundo plano mediante `StadiaDevice.dll` y repite la lectura cada **30 segundos**. El resultado aparece en la interfaz y en el icono de la bandeja.

- **Bluetooth:** Windows debe enumerar el servicio BLE Battery Service (`0x180F`) del mando emparejado. La biblioteca busca Battery Level (`0x2A19`) en el mismo dispositivo y fuerza una lectura desde el mando. Valida una respuesta de un byte entre 0 y 100. No escribe en el servicio de batería ni modifica el servicio HID.
- **USB:** busca la interfaz WinUSB del Stadia `VID_18D1 / PID_9400 / MI_00`. Solicita el dato con la petición de control `0x83` y recupera la respuesta con `0x84`, validando su formato y porcentaje. No escribe firmware.
- **Carga:** muestra **Cargando** al detectar USB y **Carga completa** si además obtiene el 100 %. Es una indicación basada en la conexión: no mide corriente ni confirma que la batería esté cargándose físicamente.
- **Avisos:** con los avisos habilitados, notifica conexión, desconexión, batería baja al **15 % o menos** y nivel del **100 %**. El aviso de nivel máximo requiere una lectura válida.

Si aparece **Batería: no disponible**, no se ha podido consultar el nivel y no se inventa un porcentaje. En Bluetooth, apaga y enciende el mando y espera la siguiente lectura. Si persiste, comprueba el emparejamiento; quitarlo y volverlo a emparejar permite que Windows enumere de nuevo sus servicios. Por USB, comprueba la conexión de datos y la disponibilidad de la interfaz WinUSB. Que los botones funcionen no garantiza que Windows exponga la interfaz de batería.

## Firma del instalador y del controlador

La versión 1.0 publicada **no tiene firma Authenticode de un editor validado públicamente**. Se distinguen dos firmas:

### Instalador y aplicación

Para distribuir el EXE con identidad de editor verificada hace falta un certificado de firma de código emitido por una autoridad reconocida, o un servicio de firma con validación de identidad. Un certificado autofirmado no proporciona confianza pública en otros equipos.

Con un certificado ya disponible en el almacén personal, utiliza SignTool del Windows SDK. Sustituye la huella y la URL por las de tu certificado y proveedor de sellado de tiempo:

```powershell
signtool sign /sha1 HUELLA_DEL_CERTIFICADO /fd SHA256 /tr URL_RFC3161_DEL_PROVEEDOR /td SHA256 .\release\StadiaLink.Setup.exe
signtool verify /pa /v .\release\StadiaLink.Setup.exe
Get-FileHash .\release\StadiaLink.Setup.exe -Algorithm SHA256
```

El ejemplo presupone `signtool` en el PATH y acceso a la clave privada; un token o servicio remoto puede requerir parámetros adicionales. Firma después de generar el instalador y regenera la huella de descarga después de firmarlo. No publiques claves, certificados privados ni contraseñas. Para firmar los ejecutables incorporados hay que añadir una etapa de firma antes de empaquetar: el script actual recompila la aplicación al crear el instalador.

Una firma identifica al editor y comprueba integridad, pero no garantiza la desaparición inmediata de los avisos SmartScreen para un binario nuevo. Referencias: [SignTool](https://learn.microsoft.com/en-us/windows/win32/seccrypto/signtool), [reputación SmartScreen](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation) y [Microsoft Artifact Signing](https://learn.microsoft.com/en-us/azure/artifact-signing/quickstart). La elegibilidad de este último depende del país y del registro como organización o desarrollador individual.

### Controlador

Actualmente `driver/install.ps1` crea un certificado local, añade su parte pública a `Root` y `TrustedPublisher` del equipo y firma el catálogo del paquete. Después elimina la clave privada generada. Esto establece confianza local; **no es una certificación WHQL ni una firma pública de Microsoft**. La desinstalación retira la confianza de esos certificados. El procedimiento del proyecto no requiere desactivar Secure Boot ni activar el modo de pruebas.

Firmar el EXE no sustituye la firma del paquete del controlador. Una distribución con firma de producción requiere preparar y validar por separado el paquete UMDF y su catálogo según la [documentación de Microsoft](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/windows-driver-signing-tutorial), y adaptar el instalador para conservar ese paquete firmado en lugar de volver a firmarlo localmente.

## Problemas, contribuciones y desinstalación

Abre un [issue](https://github.com/minysito/StadiaLink/issues) indicando versión de Windows, versión de la app, USB o Bluetooth y pasos para reproducir el fallo. El registro de instalación está en `%LOCALAPPDATA%\StadiaStudio\logs\driver-install.log`; revisa y elimina información personal antes de adjuntarlo.

Las contribuciones mediante pull request son bienvenidas. Incluye los pasos de comprobación y ejecuta `tests/run.ps1` cuando cambies la configuración o la traducción de entradas. No subas dependencias descargadas, binarios generados, perfiles personales ni material de investigación de firmware.

Desinstala StadiaLink desde **Aplicaciones instaladas** de Windows. Para retirar únicamente el controlador de una copia compilada:

```powershell
.\driver\install.ps1 -Uninstall
```

Solicita permisos de administrador, elimina los paquetes de este controlador y devuelve el mando al controlador HID incluido en Windows. Puede ser necesario volver a conectarlo.

## Compatibilidad y licencia

El transporte de vibración Bluetooth utiliza interfaces privadas de Windows, comprobadas en Windows 11 build 26200; pueden cambiar con las actualizaciones del sistema. El controlador se firma localmente durante la instalación.

GPL-3.0. El controlador parte de [aisk/WinStadia](https://github.com/aisk/WinStadia). Consulta [LICENSE](LICENSE) y [UPSTREAM-README.md](UPSTREAM-README.md). Proyecto independiente de Google y Microsoft.

Parte del proyecto pudo llevarse a cabo gracias al trabajo de **aisk en WinStadia**, del que deriva el controlador nativo. Repositorio original completo: **https://github.com/aisk/WinStadia**.

Referencia de origen: [commit e9e84a0a24c3cc13bf7cc7ac2b8baa2b653ce80a](https://github.com/aisk/WinStadia/commit/e9e84a0a24c3cc13bf7cc7ac2b8baa2b653ce80a). Se conservan los avisos de copyright. StadiaLink y sus modificaciones se distribuyen bajo GPL-3.0.

## Desarrollo con ChatGPT / Codex

El proyecto está gestionado y desarrollado con ayuda de **ChatGPT / Codex**, mediante **vibe coding**: el responsable define las funciones y revisa los resultados; Codex ayuda a implementar, investigar, documentar y ejecutar comprobaciones. Las pruebas con el mando físico y la revisión de cambios forman parte del trabajo. Esta asistencia no implica certificación ni respaldo de OpenAI, Google o Microsoft.
