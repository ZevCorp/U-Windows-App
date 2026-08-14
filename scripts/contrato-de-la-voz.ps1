# EL CONTRATO DE LA VOZ: comprueba lo que promete la parte que convierte tramas del collar Omi en
# muestras (ver voz\Contrato\Contrato.cs, que ES el contrato).
#
#   .\scripts\contrato-de-la-voz.ps1
#
# No necesita Bluetooth, ni collar, ni abrir una ventana: voz\Omi es net8.0 puro. Por eso este juez
# SI puede correr en la nube en cada PR, a diferencia de los escenarios.
#
# Y por eso NO depende de U.dll: el contrato del grafo referencia ese binario y por tanto le afecta
# el salto de TFM a WinRT; este no lo toca, asi que no puede caerse por un cambio ajeno.
#
# Compila a un directorio de usar y tirar: ni toca la app, ni deja nada en el repo.

$ErrorActionPreference = 'Stop'
$repo    = Split-Path -Parent $PSScriptRoot
$binTest = Join-Path $env:TEMP "u-contrato-voz"

# RELEASE, y por la misma razon que el contrato del grafo: se juzga el mismo binario que se
# distribuye, no una variante. Con Debug el contrato no puede ni cargar en una maquina con Smart App
# Control activo (0x800711C7, medido el 2026-08-08).
Write-Host "1/2 compilando el contrato de la voz..." -ForegroundColor Cyan
dotnet build (Join-Path $repo "voz\Contrato\Contrato.csproj") `
  -c Release -o $binTest --nologo -v quiet -nodeReuse:false
if ($LASTEXITCODE -ne 0) { throw "el contrato de la voz no compila (codigo $LASTEXITCODE)" }

Write-Host "2/2 juzgando..." -ForegroundColor Cyan
& (Join-Path $binTest "contrato-voz.exe")
exit $LASTEXITCODE
