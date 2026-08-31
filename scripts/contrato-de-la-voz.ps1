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

# LA MISMA RED QUE EL CONTRATO DEL GRAFO (ver alli el porque completo): el & lanza si SAC bloquea el
# exe y ErrorActionPreference='Stop' mataria el script sin decir nada; el bloqueo es intermitente,
# asi que se reintenta; y si ni el apphost ni el host de .NET arrancan, se dice "no se" con exit 99
# en vez de devolver un codigo que se lea como recuento de promesas rotas.
$salida = @(); $codigo = -1; $arranco = $false
foreach ($intento in 1..3) {
  try {
    $salida = & (Join-Path $binTest "contrato-voz.exe") 2>&1
    $codigo = $LASTEXITCODE
    $arranco = $true
    break
  } catch {
    Write-Host ("   x intento {0}/3 - no se pudo ni ARRANCAR el juez: {1}" -f $intento, $_.Exception.Message) -ForegroundColor Red
    if ($intento -lt 3) { Start-Sleep -Seconds 3 }
  }
}
if (-not $arranco) {
  $dll = Join-Path $binTest "contrato-voz.dll"
  if (Test-Path $dll) {
    Write-Host "   -> el .exe no arranca; se prueba con el host de .NET (dotnet <dll>)" -ForegroundColor DarkYellow
    $salida = & dotnet $dll 2>&1
    $codigo = $LASTEXITCODE
  }
}
$salida | ForEach-Object { Write-Host $_ }

# Se busca "NTEGRA" sin la tilde a proposito: el veredicto es "VOZ INTEGRA" con I acentuada, y
# anclar la comparacion a la tilde la ata a la codificacion con que se lea la salida - una
# comparacion entre cadenas de distinta forma da falso SIEMPRE y en silencio (aprendizaje n.16).
if (-not ($salida -match 'VOZ (\S*NTEGRA|ROTA)')) {
  Write-Host ""
  Write-Host "NO SE PUDO JUZGAR: el contrato de la voz no llego a emitir veredicto." -ForegroundColor Red
  Write-Host "Esto NO es un verde ni un rojo: es un no-se. Mira la salida de arriba." -ForegroundColor Red
  exit 99
}
exit $codigo
