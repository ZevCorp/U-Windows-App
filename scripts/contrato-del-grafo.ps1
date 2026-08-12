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

# RELEASE, NO DEBUG, y no es una preferencia: con Debug el contrato NO PUEDE CORRER en una maquina
# con Smart App Control activo. El binario de Debug es distinto del que la app ejecuta a diario, asi
# que no tiene reputacion, y Windows bloquea su carga:
#
#   FileLoadException: Could not load file or assembly 'U.Graph.dll'.
#   Una directiva de Control de aplicaciones bloqueo este archivo. (0x800711C7)
#
# Las diez promesas fallaban a la vez y el veredicto decia «CONTRATO ROTO» — un fallo del arnes
# disfrazado de nucleo roto, que es lo peor que puede decir un juez. Con Release el binario coincide
# con el que ya se ejecuta y pasa (2026-08-08). Ademas es lo correcto por si solo: se juzga el mismo
# binario que se distribuye, no una variante.
Write-Host "1/3 compilando el nucleo..." -ForegroundColor Cyan
dotnet build (Join-Path $repo "windows-client\WindowsClient.csproj") -c Release -o $binApp --nologo -v quiet -nodeReuse:false
if ($LASTEXITCODE -ne 0) { throw "el nucleo no compila (codigo $LASTEXITCODE)" }

Write-Host "2/3 compilando el contrato contra esos binarios..." -ForegroundColor Cyan
dotnet build (Join-Path $repo "tests\ContratoDelGrafo\ContratoDelGrafo.csproj") `
  -c Release -o $binTest -p:UBin=$binApp --nologo -v quiet -nodeReuse:false
if ($LASTEXITCODE -ne 0) { throw "el contrato no compila (codigo $LASTEXITCODE)" }

# EL JUEZ, ANTES DE JUZGAR A NADIE. Cuatro promesas de mentira con resultado conocido: si el arnes
# no sabe contar, su veredicto sobre el nucleo no vale nada — y ya dijo una vez «CONTRATO ROTO: 10
# promesas» sin haber probado ninguna (2026-08-08). Cuesta un segundo.
Write-Host "3/4 el arnes, juzgandose a si mismo..." -ForegroundColor Cyan
& (Join-Path $binTest "contrato-del-grafo.exe") --autoprueba
if ($LASTEXITCODE -ne 0) { throw "EL ARNES NO SABE CONTAR (codigo $LASTEXITCODE): cualquier veredicto suyo sobre el nucleo es sospechoso. Arreglar esto antes de mirar el contrato." }

Write-Host "4/4 juzgando..." -ForegroundColor Cyan
& (Join-Path $binTest "contrato-del-grafo.exe")
exit $LASTEXITCODE
