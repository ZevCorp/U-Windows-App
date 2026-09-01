<#
  Instala UMedidor.exe en un PC de urgencias — sin admin, sin instalador pesado.

  El medidor es un .exe separado de U.exe (que ya está instalado en esos PCs). Para el MVP del
  baseline se entrega como una carpeta que se copia a %LOCALAPPDATA%\Programs\MedidorU y se
  registra en la Run key del usuario (HKCU, no HKLM: no requiere admin). El auto-update por
  Velopack llega en la fase 4; hasta entonces, actualizar es volver a correr esto.

  Uso, en el PC del médico (PowerShell normal, sin «como administrador»):
    .\instalar-medidor.ps1 -Origen .\out\medidor

  -Origen  = carpeta con UMedidor.exe y sus DLLs (la salida de `dotnet publish`).
  El primer arranque pide el código de enrolamiento (lo da el superadmin del portal).
#>
param(
  [Parameter(Mandatory = $true)][string]$Origen,
  [switch]$NoArrancar
)

$ErrorActionPreference = "Stop"

$exe = Join-Path $Origen "UMedidor.exe"
if (-not (Test-Path $exe)) {
  Write-Host "No encuentro UMedidor.exe en «$Origen». ¿Corriste el publish?" -ForegroundColor Red
  Write-Host "  dotnet publish medidor\App\App.csproj -c Release -r win-x64 --self-contained true -o out\medidor" -ForegroundColor DarkGray
  exit 1
}

$destino = Join-Path $env:LOCALAPPDATA "Programs\MedidorU"
Write-Host "Instalando el medidor en $destino (sin admin)…" -ForegroundColor Cyan

# Si ya está corriendo, cerrarlo para poder sobrescribir el binario.
Get-Process -Name UMedidor -ErrorAction SilentlyContinue | ForEach-Object {
  Write-Host "  cerrando la instancia en marcha…" -ForegroundColor DarkGray
  $_ | Stop-Process -Force
  Start-Sleep -Milliseconds 500
}

New-Item -ItemType Directory -Force -Path $destino | Out-Null
Copy-Item -Path (Join-Path $Origen "*") -Destination $destino -Recurse -Force
$exeInstalado = Join-Path $destino "UMedidor.exe"

# Arranque automático al iniciar sesión (Run key del usuario: HKCU, sin admin).
$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
Set-ItemProperty -Path $runKey -Name "MedidorU" -Value "`"$exeInstalado`""
Write-Host "  arranque automático registrado (HKCU\...\Run\MedidorU)." -ForegroundColor DarkGray

Write-Host "Listo. El medidor arrancará al iniciar sesión." -ForegroundColor Green
Write-Host "La primera vez pedirá el código de instalación (lo da el panel de administración)." -ForegroundColor Green

if (-not $NoArrancar) {
  Write-Host "Arrancándolo ahora…" -ForegroundColor Cyan
  Start-Process -FilePath $exeInstalado
}
