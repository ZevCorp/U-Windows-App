# EL ICONO DEL ESCRITORIO PARA LA CONSULTA (spec 004, fase 8).
#
# Crea "Miracle Consulta.lnk" en el escritorio apuntando a `U.exe --consulta`: la ventana de
# grabar la consulta medico-paciente, delante y lista.
#
# Por defecto apunta al build local del repo (para probar sin instalar nada); con -Instalada apunta
# a la copia de Velopack en %LocalAppData%\U, que es la que se actualiza sola.
#
#   .\scripts\atajo-consulta.ps1              # build local (Release)
#   .\scripts\atajo-consulta.ps1 -Instalada   # la instalada
#
# POR QUE EXISTE ESTE SCRIPT: `vpk pack` corre hoy sin --shortcuts, asi que NO esta comprobado que
# el instalador deje un icono en el escritorio (mirado el 2026-09-01 en windows-release.yml:102).
# Hasta verificar eso en una maquina limpia, este script es el camino honesto.
#
# SIN ACENTOS NI GUIONES LARGOS A PROPOSITO: PowerShell 5.1 lee los .ps1 sin BOM como ANSI, y un
# caracter multibyte parte el parser con un error que no menciona la codificacion (2026-09-01).

param([switch]$Instalada)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

if ($Instalada) {
    $exe = Join-Path $env:LOCALAPPDATA "U\current\U.exe"
    if (-not (Test-Path $exe)) { throw "no hay una copia instalada en $exe : instala con U-Setup.exe o usa el build local (sin -Instalada)" }
} else {
    $exe = Join-Path $repo "windows-client\bin\x64\Release\net8.0-windows10.0.19041.0\U.exe"
    if (-not (Test-Path $exe)) { throw "no hay build local en $exe : compila primero con dotnet build windows-client -c Release" }
}

$escritorio = [Environment]::GetFolderPath('Desktop')
$lnk = Join-Path $escritorio "Miracle Consulta.lnk"

$shell = New-Object -ComObject WScript.Shell
$atajo = $shell.CreateShortcut($lnk)
$atajo.TargetPath = $exe
$atajo.Arguments = "--consulta"
$atajo.WorkingDirectory = Split-Path -Parent $exe
$atajo.IconLocation = "$exe,0"
$atajo.Description = "Grabar una consulta medico-paciente con Miracle"
$atajo.Save()

Write-Host "listo: $lnk -> $exe --consulta" -ForegroundColor Green
