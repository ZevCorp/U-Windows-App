# UNA RONDA DE LA NOCHE (spec 052): si el escritorio está libre, corre la batería y deja una línea de resumen en
# %TEMP%\u-medicion\noche.log. Si no lo está —bloqueado, salvapantallas—, lo dice y NO fuerza nada: en esta
# máquina el salvapantallas del fabricante (OLED Care) se traga la entrada inyectada, y encender la pantalla de
# un OLED a la fuerza toda la noche es justo lo que no se hace.

$ErrorActionPreference = 'Continue'
$repo = Split-Path -Parent $PSScriptRoot
$noche = Join-Path $env:TEMP "u-medicion\noche.log"
New-Item -ItemType Directory -Force (Split-Path $noche) | Out-Null
function Anota($t) { $l = "[$(Get-Date -Format 'yyyy-MM-dd HH:mm')] $t"; Add-Content -Path $noche -Value $l -Encoding UTF8; Write-Host $l }

$bloqueado = [bool](Get-Process LogonUI -ErrorAction SilentlyContinue)
# Se le PREGUNTA a Windows, no se adivina por el nombre del proceso: «AsusOLEDShifter» corre siempre (desplaza
# píxeles, no es un salvapantallas) y la primera ronda de la noche se saltó por él (2026-09-25, 00:14).
Add-Type -Name P -Namespace NocheU -MemberDefinition '[DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint a, uint b, ref bool c, uint d);'
$corriendo = $false; [NocheU.P]::SystemParametersInfo(0x0072, 0, [ref]$corriendo, 0) | Out-Null
$salvapantallas = @(if ($corriendo) { [pscustomobject]@{ ProcessName = 'salvapantallas' } })
if ($bloqueado -or $salvapantallas.Count -gt 0) {
    Anota ("ronda SALTADA: " + ($(if ($bloqueado) { "sesión bloqueada" } else { "" }) + " " + (($salvapantallas | ForEach-Object ProcessName) -join ',')).Trim())
    exit 0
}

$salida = & (Join-Path $repo "scripts\bateria-u.ps1") -SinCompilar 2>&1 | Out-String -Width 400
$resumen = ($salida -split "`n" | Where-Object { $_ -match '^vueltas:' } | Select-Object -Last 1)
$filas = ($salida -split "`n" | Where-Object { $_ -match '\d+ de \d+ hechos' })
Anota ("ronda: " + $resumen.Trim() + " · pedidos con plan: " + $filas.Count)
