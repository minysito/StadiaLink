# Código de terceros incorporado

## aisk/WinStadia

Repositorio original: https://github.com/aisk/WinStadia

Referencia de origen: https://github.com/aisk/WinStadia/commit/e9e84a0a24c3cc13bf7cc7ac2b8baa2b653ce80a

El controlador nativo de StadiaLink deriva de WinStadia y se incluye con modificaciones. La base incorporada comprende la estructura UMDF, la traducción de entradas Stadia a XInput, los transportes USB/Bluetooth y los archivos de instalación y compilación en `driver/`.

Archivos derivados: `winstadia.c`, `winstadia.h`, `stadia.c`, `usb.c`, `bluetooth.c`, `winstadia.inf`, `build.ps1`, `install.ps1` y `status.ps1`. Las modificaciones y los archivos adicionales de StadiaLink forman parte del código distribuido.

Licencia: GPL-3.0. Se conservan los avisos originales aplicables. StadiaLink y sus modificaciones se distribuyen bajo GPL-3.0; véase [LICENSE](LICENSE).

Este documento acredita código incorporado a la aplicación y al controlador distribuidos. No es una bibliografía de repositorios consultados durante la investigación.
