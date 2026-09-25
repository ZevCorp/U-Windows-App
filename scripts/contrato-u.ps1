# EL CONTRATO DE Ü DESDE CERO (spec 052, promesas 430-441). Sin pantalla y sin red.
#
#   .\scripts\contrato-u.ps1
#
# Release y no Debug, por el mismo motivo que el contrato del grafo: con Debug el binario no tiene
# reputación y Smart App Control lo bloquea (0x800711C7), y el juez diría «roto» sin haber juzgado.

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$bin  = Join-Path $env:TEMP "u-contrato-u"

dotnet build (Join-Path $repo "u\Contrato\Contrato.csproj") -c Release -o $bin --nologo -v quiet -nodeReuse:false | Out-Null
if ($LASTEXITCODE -ne 0) {
    dotnet build (Join-Path $repo "u\Contrato\Contrato.csproj") -c Release -o $bin --nologo -v quiet -nodeReuse:false | Select-String "error" | Select-Object -First 8
    Write-Host "NO PUDE JUZGAR: el contrato de Ü no compila." -ForegroundColor Red
    exit 3
}
& (Join-Path $bin "contrato-u.exe")
exit $LASTEXITCODE
