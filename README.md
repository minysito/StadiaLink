# StadiaLink

Aplicación para configurar el mando Stadia en Windows 11 x64. Versión 1.0.

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

## Compilación

Requiere Windows x64, Visual Studio 2022 con herramientas C++, Windows SDK 10.0.26100 y .NET Framework 4.8. El script del controlador obtiene su WDK desde NuGet la primera vez.

```powershell
./app/package.ps1 -Driver
./tests/run.ps1
```

El instalador se genera en `release/`. El código correspondiente se incluye como `Source.zip` dentro del instalador. `.build/` contiene únicamente archivos temporales de compilación.

## Uso

Ajusta el perfil y pulsa **Guardar y aplicar**. Cambiar de pestaña descarta los cambios del perfil sin guardar. La casilla de inicio con Windows se guarda inmediatamente. La vibración continua se detiene con **Detener**, al cambiar de pestaña o al cerrar la app.

Los atajos de Windows requieren la app abierta. Los tiempos de respuesta mostrados en Ajustes miden consultas HID y actualización de la interfaz, no la latencia física completa hasta un juego. El audio del jack funciona por USB; no se ofrece audio Bluetooth ni activación de Wi-Fi.

## Compatibilidad y licencia

El transporte de vibración Bluetooth utiliza interfaces privadas de Windows, comprobadas en Windows 11 build 26200; pueden cambiar con las actualizaciones del sistema. El controlador se firma localmente durante la instalación.

GPL-3.0. El controlador parte de [aisk/WinStadia](https://github.com/aisk/WinStadia). Consulta [LICENSE](LICENSE) y [UPSTREAM-README.md](UPSTREAM-README.md). Proyecto independiente de Google y Microsoft.
