# EL CONTRATO DEL GRAFO: compila el nucleo tal como esta y comprueba que sigue cumpliendo todas
# sus promesas (ver tests\ContratoDelGrafo\Contrato.cs, que ES el contrato).
#
# El contrato vive en tests\ de la RAIZ y no dentro de windows-client\: el csproj de WPF compila
# por glob todo lo que cuelga de su carpeta, y un proyecto anidado le mete sus AssemblyInfo
# generados — CS0579, atributo duplicado, medido el 2026-08-08.
#
# Se corre ANTES de dar por bueno cualquier cambio que toque SurfaceMap.cs — y en la duda, siempre:
# tarda medio minuto y ha costado dias encontrar a mano lo que esto encuentra solo.
#
#   .\scripts\contrato-del-grafo.ps1
#
# Compila a directorios de usar y tirar: ni toca la app estable, ni la de desarrollo, ni deja
# nada en el repo. El codigo del contrato juzga los BINARIOS recien compilados del nucleo, para
# que lo aprobado sea exactamente lo que se va a ejecutar.

$ErrorActionPreference = 'Stop'
$repo     = Split-Path -Parent $PSScriptRoot
$scratch  = Join-Path $env:TEMP "u-contrato"
$binApp   = Join-Path $scratch "bin-app"
$binTest  = Join-Path $scratch "bin-contrato"

Write-Host "1/3 compilando el nucleo..." -ForegroundColor Cyan
dotnet build (Join-Path $repo "windows-client\WindowsClient.csproj") -c Debug -o $binApp --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "el nucleo no compila (codigo $LASTEXITCODE)" }

Write-Host "2/3 compilando el contrato contra esos binarios..." -ForegroundColor Cyan
dotnet build (Join-Path $repo "tests\ContratoDelGrafo\ContratoDelGrafo.csproj") `
  -c Debug -o $binTest -p:UBin=$binApp --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "el contrato no compila (codigo $LASTEXITCODE)" }

Write-Host "3/3 juzgando..." -ForegroundColor Cyan
& (Join-Path $binTest "contrato-del-grafo.exe")
exit $LASTEXITCODE
