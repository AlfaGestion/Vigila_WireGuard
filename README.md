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
- `appsettings.json`: configuración operativa.
- `scripts/ServiceControl.ps1`: instalación y administración del servicio.

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
- `.NET 8 SDK` para compilar.
- Permisos de administrador para instalar el servicio.

## Compilación

```powershell
dotnet restore
dotnet build -c Release
```

## Publicación self-contained win-x64

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Salida esperada:

```text
bin\Release\net8.0\win-x64\publish\
```

## Instalación del servicio

Ejemplo manual con `sc.exe`:

```powershell
sc.exe create AlfaNet.WireGuardWatchdog binPath= "C:\Ruta\publish\AlfaNet.WireGuardWatchdog.exe" start= auto
sc.exe start AlfaNet.WireGuardWatchdog
```

O usando el script provisto:

```powershell
.\scripts\ServiceControl.ps1 -Action Install
.\scripts\ServiceControl.ps1 -Action Start
```

Si querés instalar otra compilación o una carpeta distinta, podés indicar la ruta manualmente:

```powershell
.\scripts\ServiceControl.ps1 -Action Install -ExecutablePath "C:\Ruta\publish\AlfaNet.WireGuardWatchdog.exe"
```

## Comandos del script

```powershell
.\scripts\ServiceControl.ps1 -Action Install
.\scripts\ServiceControl.ps1 -Action Uninstall
.\scripts\ServiceControl.ps1 -Action Start
.\scripts\ServiceControl.ps1 -Action Stop
.\scripts\ServiceControl.ps1 -Action Restart
.\scripts\ServiceControl.ps1 -Action Status
```

Comportamiento del instalador:

- usa por defecto `publish\win-x64\AlfaNet.WireGuardWatchdog.exe`
- crea la carpeta de logs configurada en `appsettings.json` si todavía no existe
- instala el servicio con inicio automático

## Distribución web y actualizaciones

Sitio previsto:

- `https://www.alfagestion.com.ar/wireguard-watchdog/`

Estructura recomendada en la web:

- `https://www.alfagestion.com.ar/wireguard-watchdog/latest.json`
- `https://www.alfagestion.com.ar/wireguard-watchdog/releases/1.0.0/AlfaNet.WireGuardWatchdog-win-x64-1.0.0.zip`

Preparar una release web desde la salida publicada:

```powershell
.\scripts\Prepare-WebRelease.ps1 -Version 1.0.0
```

Salida local generada:

- `deploy\output\latest.json`
- `deploy\output\releases\1.0.0\release.json`
- `deploy\output\releases\1.0.0\AlfaNet.WireGuardWatchdog-win-x64-1.0.0.zip`

Flujo de publicación sugerido:

1. Ejecutar `dotnet publish`.
2. Ejecutar `.\scripts\Prepare-WebRelease.ps1 -Version x.y.z`.
3. Subir el `.zip` a `releases/x.y.z/`.
4. Subir `latest.json` a la raíz del sitio.
5. Mantener también `release.json` dentro de la carpeta versionada para auditoría.

Qué falta para autoactualización completa:

- firma digital del instalador y del ejecutable
- opcionalmente agregar canales `stable` y `beta`
- endurecer rollback y telemetría de actualizaciones

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

La tarea usa por defecto:

- updater instalado en `C:\Program Files\Alfa Gestion\WireGuard Watchdog\Updater`
- manifiesto `https://www.alfagestion.com.ar/wireguard-watchdog/latest.json`
- frecuencia de chequeo cada 6 horas

El flujo del MSI ahora también publica e incluye el updater.

## Instalador MSI

Se agregó un scaffold WiX en:

- `installer\AlfaNet.WireGuardWatchdog.Setup.wixproj`
- `installer\Package.wxs`

Objetivo del instalador:

- instala en `C:\Program Files\Alfa Gestion\WireGuard Watchdog`
- registra el servicio `AlfaNet.WireGuardWatchdog`
- deja el servicio en inicio automático
- soporta `MajorUpgrade` para reemplazar versiones anteriores

Flujo sugerido de build:

```powershell
.\scripts\Build-Msi.ps1 -Version 1.0.0
```

Ese script:

1. publica el servicio en `publish\win-x64`
2. publica el updater en `publish\updater\win-x64`
3. compila el proyecto WiX
4. genera el MSI del instalador

Notas:

- el scaffold apunta hoy a la salida `publish\win-x64`
- para evitar símbolos innecesarios, el instalador excluye `*.pdb`
- el MSI instala también `Updater\AlfaNet.WireGuardWatchdog.Updater.exe`
- el siguiente paso natural es firmar el `.msi` y subirlo junto al `latest.json`

## Logs

- Archivo: carpeta configurada en `Watchdog:LogDirectory`.
- Event Viewer: opcional, controlado por `Watchdog:EnableEventLog`.

## Punto pendiente

En `Utils/WireGuardPaths.cs` quedó el `TODO` para encapsular el método final de reinicio del túnel si decidimos usar otra estrategia distinta a `sc.exe`, por ejemplo `wireguard.exe`, `wireguard /installtunnelservice` o una integración más específica con el entorno.

## Evolución futura

La solución ya está preparada para agregar:

- lectura más avanzada del estado del túnel,
- métricas,
- envío de eventos a un servidor central,
- pruebas unitarias sobre interfaces y políticas.
