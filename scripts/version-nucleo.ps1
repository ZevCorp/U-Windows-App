# VERSIONES DEL NUCLEO DEL GRAFO. La v0 es la original congelada; se edita sobre la version que el
# DUENO elija, y volver a la que funcionaba es un clic en la tira izquierda del explorador del grafo.
#
#   .\scripts\version-nucleo.ps1 -Bootstrap                # crea v0 (congelada) y v1 (en edicion), y las compila
#   .\scripts\version-nucleo.ps1 -Crear -Nota "prueba X"   # nueva version desde la que esta en edicion
#   .\scripts\version-nucleo.ps1 -Editar 2                 # elegir cual se edita (SOLO el dueno; v0 jamas)
#   .\scripts\version-nucleo.ps1 -Construir                # compilar la version en edicion a su directorio
#
# Como encajan las piezas:
#   - La INSTANTANEA de cada version es versiones\nucleo\vN.cs (en el repo: es codigo).
#   - El archivo de TRABAJO es src\Navigation\SurfaceMap.cs: SIEMPRE contiene la version en
#     edicion. El agente de codigo solo puede editar ese archivo (con contrasena) — las
#     instantaneas las bloquea el guardian sin popup siquiera.
#   - El REGISTRO (que versiones hay, cual se edita) es C:\U-versiones\versiones.json: UNA copia,
#     la leen el runtime, el guardian y este script. Dos copias se desincronizan en silencio.
#   - Cada version COMPILADA vive en C:\U-versiones\vN\bin con su version.txt. Saltar de version
#     en la app no compila nada: arranca ese binario. Por eso -Construir existe: la tira solo
#     ofrece lo que ya este compilado.
#
# -Construir tambien refresca la instantanea vN.cs desde el archivo de trabajo: la instantanea es
# LO CONSTRUIDO, no un borrador. Y corre el contrato antes de dar la build por buena.

[CmdletBinding()]
param(
  [switch]$Bootstrap,
  [switch]$Crear,
  [int]$Editar = -1,
  [switch]$Construir,
  [int]$Version = -1,          # con -Construir: cual (por defecto, la que esta en edicion)
  [string]$Nota = "",
  [switch]$SinContrato         # solo para depurar el propio script; el contrato es la red
)

$ErrorActionPreference = 'Stop'
$repo      = Split-Path -Parent $PSScriptRoot
$trabajo   = Join-Path $repo "windows-client\src\Navigation\SurfaceMap.cs"
$snaps     = Join-Path $repo "versiones\nucleo"
$raiz      = if ($env:U_VERSIONES_DIR) { $env:U_VERSIONES_DIR } else { "C:\U-versiones" }
$registro  = Join-Path $raiz "versiones.json"

function Leer-Registro {
  if (Test-Path $registro) { Get-Content $registro -Raw | ConvertFrom-Json }
  else { [pscustomobject]@{ EnEdicion = -1; Versiones = @() } }
}
function Guardar-Registro($r) {
  New-Item -ItemType Directory -Force -Path $raiz | Out-Null
  # DONDE VIVE EL REPO, apuntado en cada guardado. La app corre desde C:\U-dev2 o desde
  # C:\U-versiones\vN y no tiene forma de saber donde esta el codigo; sin esto, el boton «+» de la
  # tira no podria invocar este mismo script. Se reescribe siempre para que mover el repo se
  # arregle solo la proxima vez que se toque una version.
  $r | Add-Member -NotePropertyName Repo -NotePropertyValue $repo -Force
  $r | ConvertTo-Json -Depth 5 | Set-Content $registro -Encoding utf8
}
function Snap($n) { Join-Path $snaps ("v{0}.cs" -f $n) }

function Compilar($n) {
  $bin = Join-Path $raiz ("v{0}\bin" -f $n)

  # NO SE RECOMPILA LA VERSION QUE SE ESTA EJECUTANDO. Su U.exe esta bloqueado y el build muere con
  # un MSB3027 que no explica nada (2026-08-08, tropezado en vivo: el usuario habia saltado a v1
  # desde la tira y reconstruir v1 fallaba).
  #
  # Se RECHAZA en vez de cerrarla por las buenas, y la razon es de fondo: esa instancia la lanzo la
  # app con SU entorno —U_DATA_DIR, claves, sonda—, y relanzarla desde aqui le daria otro terreno
  # sin avisar. Un sistema que cambia en silencio los datos que miras es peor que uno que te pide
  # un clic. El clic ademas ya existe: saltar a otra version en la tira relanza con el mismo
  # entorno, que es justo lo que hace falta.
  $viva = Get-Process U -ErrorAction SilentlyContinue |
          Where-Object { $_.Path -and $_.Path.StartsWith([IO.Path]::GetFullPath($bin), [StringComparison]::OrdinalIgnoreCase) }
  if ($viva) {
    # La version de ejemplo NO puede ser la que se reconstruye: este mensaje llego a decir «salta
    # a v0» reconstruyendo la v0 (2026-08-08). Se sugiere otra que exista de verdad.
    $otra = (Leer-Registro).Versiones | Where-Object { $_.N -ne $n } | Select-Object -First 1
    $aDonde = if ($otra) { "v$($otra.N)" } else { "otra version (crea una con -Crear si no hay)" }
    throw ("v{0} se esta EJECUTANDO ahora mismo (PID {1}): su U.exe esta bloqueado y no se puede recompilar." -f $n, $viva.Id) +
          " Salta a $aDonde en la tira izquierda del explorador y repite este comando;" +
          " despues vuelve a v$n. Si solo querias probar un cambio sin tocar esta version, usa el boton + para crear una nueva."
  }

  Write-Host ("compilando v{0} -> {1} ..." -f $n, $bin) -ForegroundColor Cyan

  # La instantanea manda: se construye EXACTAMENTE lo que dice vN.cs. Si no es la version en
  # edicion, el archivo de trabajo se aparta y se restaura pase lo que pase.
  $eraTrabajo = (Get-FileHash $trabajo).Hash -eq (Get-FileHash (Snap $n)).Hash
  $respaldo = "$trabajo.antes-de-compilar"
  if (-not $eraTrabajo) { Copy-Item $trabajo $respaldo -Force; Copy-Item (Snap $n) $trabajo -Force }
  try {
    # -nodeReuse:false: MSBuild deja nodos vivos entre compilaciones para ir mas rapido, y esos
    # nodos se quedan agarrados a obj\ y a los binarios de salida. Compilando varias versiones
    # seguidas, uno de esos nodos colgo el build diez minutos sin decir nada (2026-08-08, medido).
    # Un build que a veces no termina es peor que un build medio segundo mas lento.
    dotnet build (Join-Path $repo "windows-client\WindowsClient.csproj") -c Debug -o $bin --nologo -v quiet -nodeReuse:false
    if ($LASTEXITCODE -ne 0) { throw "v$n no compila (codigo $LASTEXITCODE)" }
    Set-Content (Join-Path $bin "version.txt") $n -Encoding ascii
  } finally {
    if (-not $eraTrabajo) { Move-Item $respaldo $trabajo -Force }
  }

  if (-not $SinContrato) {
    Write-Host ("contrato sobre v{0}..." -f $n) -ForegroundColor Cyan
    $binTest = Join-Path $env:TEMP ("u-contrato\bin-v{0}" -f $n)
    dotnet build (Join-Path $repo "tests\ContratoDelGrafo\ContratoDelGrafo.csproj") -c Debug -o $binTest -p:UBin=$bin --nologo -v quiet -nodeReuse:false
    if ($LASTEXITCODE -ne 0) { throw "el contrato no compila contra v$n" }
    & (Join-Path $binTest "contrato-del-grafo.exe")
    if ($LASTEXITCODE -ne 0) { throw "v$n ROMPE el contrato: la build queda, pero no la uses sin arreglarlo" }
  }
  Write-Host ("v{0} lista." -f $n) -ForegroundColor Green
}

# --- bootstrap: v0 congelada + v1 editable ------------------------------------
if ($Bootstrap) {
  if (Test-Path $registro) { throw "ya hay registro en ${registro}: el bootstrap es solo la primera vez" }
  New-Item -ItemType Directory -Force -Path $snaps | Out-Null
  Copy-Item $trabajo (Snap 0) -Force
  Copy-Item $trabajo (Snap 1) -Force
  $hoy = Get-Date -Format "yyyy-MM-dd"
  Guardar-Registro ([pscustomobject]@{
    EnEdicion = 1
    Versiones = @(
      [pscustomobject]@{ N = 0; Creada = $hoy; Nota = "la original congelada" },
      [pscustomobject]@{ N = 1; Creada = $hoy; Nota = "en edicion" }
    )
  })
  Compilar 0
  Compilar 1
  Write-Host "v0 congelada y v1 en edicion. La tira izquierda del explorador ya las ofrece." -ForegroundColor Green
  return
}

# --- crear: nueva version desde la que esta en edicion ------------------------
if ($Crear) {
  $r = Leer-Registro
  $n = ($r.Versiones | Measure-Object -Property N -Maximum).Maximum + 1
  Copy-Item $trabajo (Snap $n) -Force
  $r.Versiones += [pscustomobject]@{ N = $n; Creada = (Get-Date -Format "yyyy-MM-dd"); Nota = $Nota }
  $r.EnEdicion = $n
  Guardar-Registro $r
  Compilar $n
  Write-Host ("v{0} creada desde lo que habia en edicion, y queda EN EDICION." -f $n) -ForegroundColor Green
  return
}

# --- editar: elegir sobre cual se trabaja -------------------------------------
if ($Editar -ge 0) {
  if ($Editar -eq 0) { throw "la v0 es la original congelada: no se edita. Crea una version nueva con -Crear." }
  $r = Leer-Registro
  if (-not ($r.Versiones | Where-Object { $_.N -eq $Editar })) { throw "no existe la v$Editar" }
  # Lo editado sin construir se perderia al traer la otra instantanea: se avisa y se corta.
  $enEd = $r.EnEdicion
  if ($enEd -ge 0 -and (Test-Path (Snap $enEd)) -and
      ((Get-FileHash $trabajo).Hash -ne (Get-FileHash (Snap $enEd)).Hash)) {
    throw "el archivo de trabajo tiene cambios de v$enEd sin construir. Corre -Construir primero (o descartalos a mano)."
  }
  Copy-Item (Snap $Editar) $trabajo -Force
  $r.EnEdicion = $Editar
  Guardar-Registro $r
  Write-Host ("ahora se edita la v{0}: el archivo de trabajo ES esa version." -f $Editar) -ForegroundColor Green
  return
}

# --- construir ----------------------------------------------------------------
if ($Construir) {
  $r = Leer-Registro
  $n = if ($Version -ge 0) { $Version } else { $r.EnEdicion }
  if ($n -lt 0) { throw "no hay version en edicion ni -Version dada" }
  if ($n -eq $r.EnEdicion) {
    # La instantanea es LO CONSTRUIDO: se refresca desde el trabajo justo antes de compilar.
    Copy-Item $trabajo (Snap $n) -Force
  }
  Compilar $n
  return
}

Write-Host "nada que hacer: usa -Bootstrap, -Crear, -Editar N o -Construir (ver cabecera)" -ForegroundColor Yellow
