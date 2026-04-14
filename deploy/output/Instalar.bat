@echo off
:: Verifica si ya tiene permisos de administrador
net session >nul 2>&1
if %errorlevel% == 0 goto :instalar

:: Si no tiene permisos, se relanza a si mismo como administrador (fuerza 64 bits)
echo Solicitando permisos de administrador...
powershell -Command "Start-Process '%SystemRoot%\System32\cmd.exe' -ArgumentList '/c \"%~f0\"' -Verb RunAs"
exit /b

:instalar
set "MSI=%~dp0AlfaNet.WireGuardWatchdog.Setup.msi"
set "SERVICE=AlfaNet.WireGuardWatchdog"
set "SVCREGKEY=HKLM\SYSTEM\CurrentControlSet\Services\%SERVICE%"

if not exist "%MSI%" (
    echo No se encontro el archivo instalador.
    pause
    exit /b 1
)

:: Detiene el servicio si esta corriendo
echo Deteniendo servicio si esta activo...
sc stop "%SERVICE%" >nul 2>&1
timeout /t 3 /nobreak >nul

:: Elimina el servicio via sc
echo Eliminando servicio anterior...
sc delete "%SERVICE%" >nul 2>&1
timeout /t 2 /nobreak >nul

:: Fuerza la limpieza de la clave de registro del servicio (puede quedar colgada)
echo Limpiando registro...
reg delete "%SVCREGKEY%" /f >nul 2>&1

:: Desinstala el MSI anterior si existe en el registro de programas
echo Buscando instalacion anterior en programas...
%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ^
  "$paths = @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall'); $prod = Get-ChildItem $paths -ErrorAction SilentlyContinue | Get-ItemProperty | Where-Object { $_.DisplayName -like '*WireGuard Watchdog*' } | Select-Object -First 1; if ($prod) { Write-Host 'Desinstalando version anterior...'; Start-Process '%SystemRoot%\System32\msiexec.exe' -ArgumentList '/x',$prod.PSChildName,'/quiet','/norestart' -Wait; Write-Host 'Hecho.' } else { Write-Host 'No habia instalacion previa via MSI.' }"

timeout /t 3 /nobreak >nul

echo Instalando AlfaNet WireGuard Watchdog...
%SystemRoot%\System32\msiexec.exe /i "%MSI%"
