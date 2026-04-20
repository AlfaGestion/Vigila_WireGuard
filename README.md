# AlfaNet.WireGuardWatchdog

Servicio Windows basado en `.NET 8 Worker Service` para supervisar un túnel de WireGuard y recuperarlo automáticamente cuando la conectividad se degrada.

## Qué hace

El servicio ejecuta un loop de monitoreo con estas reglas:

1. Verifica si hay salida a Internet.
2. Verifica si la VPN responde al host configurado en `Watchdog:VpnHealthHost`.
3. Si antes no había Internet y ahora volvió, espera `InternetRestoreDelaySeconds` y reinicia el túnel.
4. Si hay Internet pero la VPN falla `PingFailuresBeforeRestart` veces consecutivas, reinicia el túnel.
5. Si hubo un reinicio reciente, respeta `RestartCooldownSeconds` para evitar bucles.

## Estructura

- `Program.cs`: configura el host, DI, opciones, logging y ejecución como Windows Service.
- `Worker.cs`: contiene el loop principal y la coordinación de recuperación.
- `Models/`: modelos de configuración y estado.
- `Interfaces/`: contratos para facilitar mantenimiento, pruebas y futuras extensiones.
- `Services/`: implementaciones de conectividad, política de recuperación, control de WireGuard y logging.
- `Utils/`: utilidades de encapsulación técnica.
- `updater/`: proyecto separado para autoactualización.
- `installer/`: proyecto WiX del instalador MSI.
- `scripts/`: automatización de instalación, build y actualización.
- `appsettings.json`: configuración operativa.

## Configuración

Editar `appsettings.json`:

```json
{
  "Watchdog": {
    "TunnelName": "AlfaNetTunnel",
    "VpnHealthHost": "10.8.0.1",
    "CheckIntervalSeconds": 15,
    "PingFailuresBeforeRestart": 3,
    "InternetRestoreDelaySeconds": 10,
    "RestartCooldownSeconds": 60,
    "PingTimeoutMs": 1500,
    "LogDirectory": "C:\\ProgramData\\AlfaNet\\WireGuardWatchdog\\Logs",
    "EnableEventLog": true
  }
}
```

## Requisitos

- Windows con WireGuard ya instalado.
- `.NET 8 SDK` para compilar el proyecto.
- Permisos de administrador para instalar el servicio y registrar la tarea de actualización.

Nota: el artefacto distribuido al cliente es `self-contained`, por lo que el cliente no necesita tener `.NET` instalado.

## Compilación

```powershell
dotnet restore
dotnet build Vigila_WireGuard.sln -c Release
```

## Publicación self-contained win-x64

Publicación manual del servicio:

```powershell
dotnet publish AlfaNet.WireGuardWatchdog.csproj -c Release -r win-x64 --self-contained true -o .\publish\win-x64
```

Publicación manual del updater:

```powershell
dotnet publish .\updater\AlfaNet.WireGuardWatchdog.Updater.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\publish\updater\win-x64
```

Salidas esperadas:

```text
publish\win-x64\
publish\updater\win-x64\
```

## Instalación del servicio

Ejemplo manual con `sc.exe`:

```powershell
sc.exe create AlfaNet.WireGuardWatchdog binPath= "C:\Ruta\AlfaNet.WireGuardWatchdog.exe" start= auto
sc.exe start AlfaNet.WireGuardWatchdog
```

O usando el script provisto:

```powershell
.\scripts\ServiceControl.ps1 -Action Install
.\scripts\ServiceControl.ps1 -Action Start
```

Si querés instalar otra compilación o una carpeta distinta, podés indicar la ruta manualmente:

```powershell
.\scripts\ServiceControl.ps1 -Action Install -ExecutablePath "C:\Ruta\AlfaNet.WireGuardWatchdog.exe"
```

Comportamiento del script:

- usa por defecto `publish\win-x64\AlfaNet.WireGuardWatchdog.exe`
- crea la carpeta de logs configurada en `appsettings.json` si todavía no existe
- instala el servicio con inicio automático

## Autoactualización

Se agregó un updater separado en:

- `updater\AlfaNet.WireGuardWatchdog.Updater.csproj`
- `updater\Program.cs`

Responsabilidades del updater:

- consulta `https://www.alfagestion.com.ar/wireguard-watchdog/latest.json`
- compara la versión remota contra la instalada
- descarga el `.zip` de release
- valida SHA-256
- detiene el servicio
- hace backup de la instalación actual
- copia los binarios nuevos
- conserva el `appsettings.json` del cliente
- vuelve a iniciar el servicio

La tarea programada se administra con:

- `scripts\UpdateTaskControl.ps1`

Ejemplos:

```powershell
.\scripts\UpdateTaskControl.ps1 -Action Register
.\scripts\UpdateTaskControl.ps1 -Action Status
.\scripts\UpdateTaskControl.ps1 -Action RunNow
.\scripts\UpdateTaskControl.ps1 -Action Unregister
```

Valores por defecto de la tarea:

- updater instalado en `C:\Program Files\Alfa Gestion\WireGuard Watchdog\Updater`
- manifiesto `https://www.alfagestion.com.ar/wireguard-watchdog/latest.json`
- frecuencia de chequeo cada 6 horas

## Instalador MSI

El instalador se genera con WiX desde:

- `installer\AlfaNet.WireGuardWatchdog.Setup.wixproj`
- `installer\Package.wxs`
- `installer\license-es.rtf`
- `installer\es-es.wxl`

Objetivo del instalador:

- instala en `C:\Program Files\Alfa Gestion\WireGuard Watchdog`
- registra el servicio `AlfaNet.WireGuardWatchdog`
- instala también el updater en `Updater\`
- deja el servicio en inicio automático
- exige permisos de administrador y muestra un aviso claro si no los tiene
- soporta `MajorUpgrade` para reemplazar versiones anteriores
- muestra interfaz en español

Instalación simple para el usuario final:

1. Tener `WireGuard` instalado en Windows.
2. Abrir `AlfaNet.WireGuardWatchdog.Setup.msi`.
3. Si Windows pide permisos de administrador, aceptarlos.
4. Finalizar el asistente.

Al terminar, el servicio `AlfaNet.WireGuardWatchdog` queda instalado como servicio de Windows con inicio automático.

Build sugerido:

```powershell
.\scripts\Build-Msi.ps1 -Version 1.0.0
```

Ese script:

1. publica el servicio en `publish\win-x64`
2. publica el updater en `publish\updater\win-x64`
3. compila el proyecto WiX
4. genera el MSI del instalador

Modo de publicación actual:

- servicio `self-contained`
- updater `self-contained`
- no requiere que el cliente tenga `.NET` instalado

Salida actual del MSI:

```text
installer\bin\x64\Release\es-ES\AlfaNet.WireGuardWatchdog.Setup.msi
```

## Distribución web

Sitio previsto:

- `https://www.alfagestion.com.ar/wireguard-watchdog/`

Estructura recomendada:

- `https://www.alfagestion.com.ar/wireguard-watchdog/index.html`
- `https://www.alfagestion.com.ar/wireguard-watchdog/AlfaNet.WireGuardWatchdog.Setup.msi`
- `https://www.alfagestion.com.ar/wireguard-watchdog/latest.json`
- `https://www.alfagestion.com.ar/wireguard-watchdog/releases/1.0.0/AlfaNet.WireGuardWatchdog-win-x64-1.0.0.zip`
- `https://www.alfagestion.com.ar/wireguard-watchdog/releases/1.0.0/release.json`

Preparar una release web desde la salida publicada:

```powershell
.\scripts\Prepare-WebRelease.ps1 -Version 1.0.0
```

Salida local generada:

- `deploy\output\index.html`
- `deploy\output\latest.json`
- `deploy\output\releases\1.0.0\release.json`
- `deploy\output\releases\1.0.0\AlfaNet.WireGuardWatchdog-win-x64-1.0.0.zip`

Flujo sugerido de publicación:

1. Ejecutar `.\scripts\Build-Msi.ps1 -Version x.y.z`.
2. Ejecutar `.\scripts\Prepare-WebRelease.ps1 -Version x.y.z`.
3. Subir el `.zip` a `releases/x.y.z/`.
4. Subir `release.json` a `releases/x.y.z/`.
5. Subir `latest.json` a la raíz del sitio.
6. Subir `index.html` a la raíz del sitio.
7. Subir `AlfaNet.WireGuardWatchdog.Setup.msi` a la raíz del sitio.

## Qué falta

- firma digital del instalador y de los ejecutables
- opcionalmente agregar canales `stable` y `beta`
- endurecer rollback y telemetría de actualizaciones

## Logs

- Archivo: carpeta configurada en `Watchdog:LogDirectory`.
- Event Viewer: opcional, controlado por `Watchdog:EnableEventLog`.

## Punto pendiente

En `Utils/WireGuardPaths.cs` quedó el `TODO` para encapsular el método final de reinicio del túnel si decidimos usar otra estrategia distinta a `sc.exe`, por ejemplo `wireguard.exe`, `wireguard /installtunnelservice` o una integración más específica con el entorno.

## Evolución futura

La solución ya está preparada para agregar:

- lectura más avanzada del estado del túnel
- métricas
- envío de eventos a un servidor central
- pruebas unitarias sobre interfaces y políticas
