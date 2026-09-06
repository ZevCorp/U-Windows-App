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

# DONDE ESTAN LAS FUENTES, para la promesa 164 (spec 011): es la unica que juzga el CODIGO y no el
# binario, porque lo que puede volver a llenarse de carteles es el codigo. El contrato compila a un
# directorio de usar y tirar, asi que desde alli no hay forma de encontrar el repo — y adivinarlo
# subiendo carpetas se anclaria al nombre de la del repo, que es justo el error del patron n.11: el
# candado del nucleo protegio NADA durante cinco horas por eso. Se pasa, no se deduce.
$env:U_REPO = $repo
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

Write-Host "3/3 juzgando..." -ForegroundColor Cyan

# EL & LANZA SI EL EXE NO ARRANCA. Con $ErrorActionPreference='Stop' (arriba), que Smart App Control
# bloquee el binario no da un codigo de salida: mata el script aqui mismo, antes de que pueda decir
# "no pude juzgar". Se captura para poder decirlo, y se IMPRIME el motivo entero - que es lo
# contrario de un catch mudo (patron n.3).
#
# TRES INTENTOS porque el bloqueo es INTERMITENTE: el mismo binario, sin tocar nada, arranca a la
# segunda mas veces de las que uno querria (medido el 2026-08-26). Si las tres fallan, no se insiste.
# EAP='Continue' SOLO alrededor del juez, y es un arreglo probado, no estetico: con 'Stop' y 2>&1,
# PowerShell 5.1 LANZA en cuanto el exe escribe UNA linea a stderr AUNQUE haya arrancado y corrido
# (probado el 2026-08-31: cmd /c "echo ok & echo boom 1>&2 & exit 7" -> catch, codigo=-1, salida
# vacia). Un juez que crashea A MITAD escribe su excepcion a stderr: con 'Stop' eso caia al catch
# de "no se pudo ni ARRANCAR" -- un mensaje que no distingue sus causas (aprendizaje n.2) -- y el
# respaldo de abajo volvia a lanzar y mataba el script ANTES del exit 99. El agujero estaba
# exactamente en el escenario que motivo el escudo.
$salida = @(); $codigo = -1; $arranco = $false
$eapAntes = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
try {
  foreach ($intento in 1..3) {
    try {
      $salida = & (Join-Path $binTest "contrato-del-grafo.exe") 2>&1
      $codigo = $LASTEXITCODE
      $arranco = $true
      break
    } catch {
      Write-Host ("   x intento {0}/3 - el juez no llego a correr o murio sin veredicto: {1}" -f $intento, $_.Exception.Message) -ForegroundColor Red
      if ($intento -lt 3) { Start-Sleep -Seconds 3 }
    }
  }
} finally { $ErrorActionPreference = $eapAntes }

# RESPALDO: EL MISMO CODIGO, POR EL HOST DE .NET EN VEZ DEL .EXE.
#
# Un .exe de .NET framework-dependent es un "apphost": unos kilobytes cuyo unico trabajo es cargar
# el .dll de al lado. `dotnet app.dll` es la forma estandar y documentada de ejecutar lo mismo, y es
# lo que hacen muchas CI. Lo que SAC bloquea es el APPHOST, que nace sin firma en cada compilacion.
# No se le da la vuelta a nada: mismo binario, misma maquina, la puerta que .NET documenta.
if (-not $arranco) {
  $dll = Join-Path $binTest "contrato-del-grafo.dll"
  if (Test-Path $dll) {
    Write-Host "   -> el .exe no arranca; se prueba con el host de .NET (dotnet <dll>)" -ForegroundColor DarkYellow
    # Mismo escudo que arriba: sin el, una linea de stderr del respaldo mataba el script entero
    # antes de poder decir "no se" (probado el 2026-08-31).
    $ErrorActionPreference = 'Continue'
    try {
      try {
        $salida = & dotnet $dll 2>&1
        $codigo = $LASTEXITCODE
      } catch {
        Write-Host ("   x el respaldo tampoco: {0}" -f $_.Exception.Message) -ForegroundColor Red
      }
    } finally { $ErrorActionPreference = $eapAntes }
  }
}
$salida | ForEach-Object { Write-Host $_ }

# UN JUEZ QUE NO PUEDE CORRER NO DICE "NO SE": DICE UN NUMERO, Y EL NUMERO SE LEE COMO VEREDICTO.
#
# Medido el 2026-08-25, saboteando a proposito para comprobar unas promesas nuevas: el exe reventaba
# al cargar el ensamblado y salia con un codigo cualquiera, SIN HABER JUZGADO NADA. Quien lo llamaba
# lo tomaba por "tantas promesas incumplidas": verificar.ps1 llegaba a imprimir "N/81 verdes" con un
# numero inventado, y un guion de sabotaje contaba "cero rojas", que se lee igual que "la promesa no
# sirve". Estuve a un paso de retirar una promesa buena por esto.
#
# Es el aprendizaje n.17 con el signo cambiado, que es peor: no dijo "culpable", dijo "inocente".
#
# 99 y no otro: el codigo normal es el numero de promesas incumplidas, y 81 no llegan ahi.
if (-not ($salida -match 'CONTRATO (INTACTO|ROTO)')) {
  Write-Host ""
  Write-Host "NO SE PUDO JUZGAR: el contrato no llego a emitir veredicto." -ForegroundColor Red
  Write-Host "Esto NO es un verde ni un rojo: es un no-se. Mira la salida de arriba." -ForegroundColor Red
  exit 99
}
exit $codigo
