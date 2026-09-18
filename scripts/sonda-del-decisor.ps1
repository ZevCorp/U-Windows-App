# LA SONDA DEL DECISOR: ensena el interruptor Luna/Jev funcionando, sin SAP.
#
#   .\scripts\sonda-del-decisor.ps1              # los seis casos en seco, sin tocar la red
#   .\scripts\sonda-del-decisor.ps1 -DeVerdad    # ademas llama a TypeSafe, SOLO si hay clave
#
# ESTE SCRIPT SOLO COMPILA Y LANZA. La sonda de verdad es sondas\DelDecisor, un proyecto de consola,
# y no un .ps1: esta maquina solo tiene Windows PowerShell 5.1, que corre sobre .NET Framework y NO
# PUEDE cargar un ensamblado net8.0-windows (FileNotFoundException en LoadFrom, medido el
# 2026-09-17). La consola comparte runtime con la app y lo carga sin trucos, igual que el contrato.
#
# SIN -DeVerdad NO SALE UN SOLO PAQUETE de esta maquina.

param([switch]$DeVerdad)

$ErrorActionPreference = 'Stop'
$repo    = Split-Path -Parent $PSScriptRoot
$scratch = Join-Path $env:TEMP "u-sonda-decisor"
$binApp  = Join-Path $scratch "bin-app"
$binSonda= Join-Path $scratch "bin-sonda"

# Release y no Debug, por el mismo motivo que el contrato: con Debug el binario no tiene reputacion
# y Windows con Smart App Control lo bloquea (0x800711C7).
Write-Host "1/3 compilando el nucleo..." -ForegroundColor Cyan
dotnet build (Join-Path $repo "windows-client\WindowsClient.csproj") -c Release -o $binApp --nologo -v quiet -nodeReuse:false
if ($LASTEXITCODE -ne 0) { throw "el nucleo no compila (codigo $LASTEXITCODE)" }

Write-Host "2/3 compilando la sonda contra esos binarios..." -ForegroundColor Cyan
dotnet build (Join-Path $repo "sondas\DelDecisor\DelDecisor.csproj") `
  -c Release -o $binSonda -p:UBin=$binApp --nologo -v quiet -nodeReuse:false
if ($LASTEXITCODE -ne 0) { throw "la sonda no compila (codigo $LASTEXITCODE)" }

Write-Host "3/3 sondeando..." -ForegroundColor Cyan
$exe = Join-Path $binSonda "sonda-del-decisor.exe"
if ($DeVerdad) { & $exe --deverdad } else { & $exe }
exit $LASTEXITCODE
