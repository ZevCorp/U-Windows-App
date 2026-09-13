# Autoprueba del CONDUCTOR del nivel 4 (conducir.ps1 y piezas.ps1): sin U, sin modelo y sin credito.
#
#   powershell -ExecutionPolicy Bypass -File scripts\nivel4-voz\autoprueba-conductor.ps1               lo puro: el log
#   powershell -ExecutionPolicy Bypass -File scripts\nivel4-voz\autoprueba-conductor.ps1 -Conductor    + conducir.ps1 entero
#   powershell -ExecutionPolicy Bypass -File scripts\nivel4-voz\autoprueba-conductor.ps1 -Escritorio   + el escritorio real
#
# -Conductor corre conducir.ps1 -Ensayo (no pulsa ninguna tecla) contra un U FALSO que escribe un log con
# guion, y juzga lo que el conductor decidio. -Escritorio abre un explorador en Descargas y Configuracion en
# Bluetooth, y comprueba que el conductor sabe devolverlos a un estado de partida neutro. Unos 2 minutos los dos.
#
# Nace de tres fallos del intento del 2026-09-12 (spec 018, V5, V7 y V8) que ningun juez veia:
#  - el Ctrl+Alt+M final REABRIO una voz que ya estaba cerrada (23:39:40 y 23:43:06);
#  - "sesion abierta con" sale en el mismo segundo que el error de cuota, y el conductor la tomo por voz abierta;
#  - T4 ya cumplia su estado final antes de empezar (3 exploradores abiertos), y sin credito se gasto la bateria.
# Las lineas de log de los casos son LITERALES de aquellas corridas (U-completo.log), no inventadas.
#
# ASCII PURO (ver conducir.ps1): los acentos de las lineas se construyen con [char].
# Sale con 0 si todo se cumple; 1 si algo falla o esta PENDIENTE (lo que aun no existe cuenta como fallo);
# 2 si no se pudo ejecutar (un juez que no puede correr no dice "culpable", aprendizaje n.17).
param([switch]$Conductor, [switch]$Escritorio)
$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding $false
$aqui = $PSScriptRoot

function L([string]$s) {
  return $s.Replace('~o', [string][char]0xF3).Replace('~i', [string][char]0xED).Replace('<<', [string][char]0xAB).Replace('>>', [string][char]0xBB).Replace('~U', [string][char]0xDC).Replace('~x', [string][char]0x2717).Replace('~.', [string][char]0xB7)
}
function Juntar([string[]]$lineas, [string]$fin = "`r`n") { return ((@($lineas) | ForEach-Object { L $_ }) -join $fin) + $fin }

$resultados = New-Object System.Collections.ArrayList
function Comprueba([string]$nombre, [scriptblock]$cond) {
  try { $ok = [bool](& $cond) }
  catch {
    $ok = $false
    for ($x = $_.Exception; $x; $x = $x.InnerException) { Write-Host "      x $($x.GetType().Name): $($x.Message)" }
  }
  [void]$resultados.Add(@($nombre, $ok))
  Write-Host ((@("FALLO ", "OK    ")[[int]$ok]) + $nombre)
}

$piezas = Join-Path $aqui "piezas.ps1"
if (-not (Test-Path $piezas)) {
  Write-Host "PENDIENTE: $piezas no existe todavia. Las comprobaciones estan escritas y en rojo, que es donde tienen que estar."
  exit 1
}
try { . $piezas }
catch {
  Write-Host "NO PUDE EJECUTAR: cargar piezas.ps1 lanzo una excepcion; esto no dice nada del conductor."
  for ($x = $_.Exception; $x; $x = $x.InnerException) { Write-Host "   x $($x.GetType().Name): $($x.Message)" }
  exit 2
}
$faltan = @('EstadoDeLaVoz', 'SinCredito', 'Estado', 'LlevarAPartidaNeutra') | Where-Object { -not (Get-Command $_ -ErrorAction SilentlyContinue) }
if ($faltan) { Write-Host "PENDIENTE: piezas.ps1 no define todavia: $($faltan -join ', ')."; exit 1 }

# -- Las lineas, literales del 2026-09-12 -------------------------------------
$gptLiveSinCredito = @(
  '[23:36:44] atajo: doble Ctrl (voz), doble Ctrl+Shift (panel), triple Ctrl (collar): activos',
  '[23:36:53] voz-viva: sesi~on abierta con <<gpt-live-1>> (OpenAI GPT-Live)',
  '[23:36:53] voz-viva: micr~ofono abierto a 24000 Hz',
  '[23:36:53] voz-viva: el servidor dice: You have no credits remaining. Add credits to continue using the API at https://platform.openai.com/settings/organization/billing/.',
  '[23:36:55] voz-viva: se cort~o la escucha: The remote party closed the WebSocket connection without completing the close handshake.',
  '[23:36:55] voz-viva: la sesi~on no lleg~o a abrir: el servidor contest~o <<You have no credits remaining.>> en vez de confirmarla, y cerr~o. No se reintenta: la misma apertura fallar~ia igual',
  '[23:36:55] voz-viva: micr~ofono cerrado',
  '[23:36:55] voz-viva: sesi~on cerrada'
)
$realtimeSinCredito = @(
  '[23:40:19] voz-viva: sesi~on abierta con <<gpt-realtime-2.1-mini>> (OpenAI)',
  '[23:40:19] voz-viva: micr~ofono abierto a 24000 Hz',
  '[23:40:19] voz-viva: el servidor cerr~o la conexi~on: 1013 <<insufficient_quota.credit_balance_exhausted>>',
  '[23:40:20] voz-viva: reconectada SIN continuidad: la conversaci~on empieza de cero (intento 1)',
  '[23:40:24] voz-viva: la conexi~on se cay~o 5 veces seguidas: se deja'
)
$reabiertaAlFinal = @(
  '[23:39:40] voz-viva: sesi~on abierta con <<gpt-live-1>> (OpenAI GPT-Live)',
  '[23:39:40] voz-viva: micr~ofono abierto a 24000 Hz'
)
$cerebro429 = @(
  '[23:37:15] agent: ~x el cerebro no respondi~o: cerebro: OpenAI HTTP 429: {',
  '  "error": {',
  '    "type": "insufficient_quota",',
  '[23:37:21] omi: no se pudo abrir el collar ~. COMException (0x800710DF): sin mensaje'
)
$normal = @(
  '[10:00:01] voz-viva: sesi~on abierta con <<gpt-live-1>> (OpenAI GPT-Live)',
  '[10:00:02] voz-viva: el servidor confirm~o la sesi~on',
  '[10:00:09] voz-viva: llamada recibida: map_open_app',
  '[10:00:11] voz-viva: ~U dijo: Listo, abr~i el explorador.',
  '[10:00:12] voz-viva: usuario dijo: sesi~on cerrada'
)

Write-Host "== Lo puro: el estado de la voz y el credito, leidos del log"
Comprueba "sin nada en el log, no se sabe: estado vacio" { (EstadoDeLaVoz "") -eq "" }
Comprueba "la cuenta de Miracle no es la voz: 'cuenta: ... sesion abierta' no abre nada (la regex de antes, 'sesi.n abierta', la tomaba por voz abierta)" {
  (EstadoDeLaVoz (Juntar @('[09:00:00] cuenta: cuenta creada y sesi~on abierta ~. u-1'))) -eq "" }
Comprueba "GPT-Live sin credito (23:36): la voz queda CERRADA, aunque dijo 'sesion abierta con'" { (EstadoDeLaVoz (Juntar $gptLiveSinCredito)) -eq "cerrada" }
Comprueba "Realtime sin credito (23:40): 'la conexion se cayo 5 veces' ya es cerrada, sin esperar a 'sesion cerrada'" { (EstadoDeLaVoz (Juntar $realtimeSinCredito)) -eq "cerrada" }
Comprueba "la forma vieja: 'sesion abierta con' sin confirmacion (Realtime no confirma) es abierta" { (EstadoDeLaVoz (Juntar $reabiertaAlFinal)) -eq "abierta" }
Comprueba "manda la ULTIMA linea: cerrada y despues reabierta (el Ctrl+Alt+M de 23:39:40) es abierta" { (EstadoDeLaVoz (Juntar ($gptLiveSinCredito + $reabiertaAlFinal))) -eq "abierta" }
Comprueba "lo que alguien DICE no cambia el estado: 'usuario dijo: sesion cerrada' deja la voz abierta" { (EstadoDeLaVoz (Juntar $normal)) -eq "abierta" }
Comprueba "abierta y despues 'sesion cerrada' a secas (el cierre de TerminarAsync) es cerrada" { (EstadoDeLaVoz (Juntar ($normal + @('[10:00:20] voz-viva: micr~ofono cerrado', '[10:00:20] voz-viva: sesi~on cerrada')))) -eq "cerrada" }
Comprueba "un session.closed del servidor ('el servidor dice: sesion cerrada: ...') no es aun el cierre de la app" {
  (EstadoDeLaVoz (Juntar ($reabiertaAlFinal + @('[23:39:50] voz-viva: el servidor dice: sesi~on cerrada: idle_timeout')))) -eq "abierta" }
Comprueba "la forma nueva: 'socket conectado' sin confirmar es abriendo, no abierta" {
  (EstadoDeLaVoz (Juntar @('[10:00:01] voz-viva: socket conectado con <<gpt-live-1>> (OpenAI GPT-Live): falta que el servidor confirme la sesi~on'))) -eq "abriendo" }
Comprueba "la forma nueva: 'sesion abierta con ... socket conectado' tampoco es abierta" {
  (EstadoDeLaVoz (Juntar @('[10:00:01] voz-viva: sesi~on abierta con <<gpt-live-1>> (OpenAI GPT-Live): socket conectado, sin confirmar'))) -eq "abriendo" }
Comprueba "la forma nueva confirmada: tras 'socket conectado', 'sesion abierta con' es abierta" {
  (EstadoDeLaVoz (Juntar @('[10:00:01] voz-viva: socket conectado con <<gpt-live-1>> (OpenAI GPT-Live)', '[10:00:02] voz-viva: sesi~on abierta con <<gpt-live-1>> (OpenAI GPT-Live)'))) -eq "abierta" }
Comprueba "la confirmacion de hoy ('el servidor confirmo la sesion') es abierta" {
  (EstadoDeLaVoz (Juntar @('[10:00:01] voz-viva: socket conectado con <<gpt-live-1>>', '[10:00:02] voz-viva: el servidor confirm~o la sesi~on'))) -eq "abierta" }
Comprueba "con saltos de linea LF en vez de CRLF da lo mismo (el sabotaje del 2026-08-21 fallo por CRLF)" {
  ((EstadoDeLaVoz (Juntar $gptLiveSinCredito "`n")) -eq "cerrada") -and ((EstadoDeLaVoz (Juntar $reabiertaAlFinal "`n")) -eq "abierta") }
Comprueba "no poder abrir la sesion, o no tener clave, es cerrada" {
  ((EstadoDeLaVoz (Juntar ($reabiertaAlFinal + @('[10:00:05] voz-viva: no se pudo abrir la sesi~on: Unable to connect')))) -eq "cerrada") -and
  ((EstadoDeLaVoz (Juntar @('[10:00:05] voz-viva: sin OPENAI_API_KEY: no se arranca'))) -eq "cerrada") }
Comprueba "sin credito, GPT-Live: la linea 'You have no credits remaining' se detecta" { (SinCredito (Juntar $gptLiveSinCredito)) -match 'no credits remaining' }
Comprueba "sin credito, Realtime: el cierre 1013 'credit_balance_exhausted' se detecta aunque no haya otra linea" {
  (SinCredito (Juntar @('[23:40:19] voz-viva: el servidor cerr~o la conexi~on: 1013 <<insufficient_quota.credit_balance_exhausted>>'))) -match 'credit_balance_exhausted' }
Comprueba "sin credito, el cerebro: 'insufficient_quota' en una linea de continuacion del 429 (sin hora delante) se detecta" { (SinCredito (Juntar $cerebro429)) -match 'insufficient_quota' }
Comprueba "con credito no hay aviso: un log normal no dice 'sin credito'" { (SinCredito (Juntar ($normal + $reabiertaAlFinal))) -eq "" }

# -- El conductor entero, en ensayo, contra un U falso ------------------------
$abiertas = @()
if ($Conductor -or $Escritorio) {
  $base = Join-Path $env:TEMP ("nivel4-autoprueba-conductor-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
  New-Item -ItemType Directory -Force -Path $base | Out-Null
  Write-Host ""
  Write-Host "== El conductor entero (conducir.ps1 -Ensayo) contra un U falso - $base"
  $falsoPs1 = Join-Path $base "falso-u.ps1"
  $falsoCmd = Join-Path $base "falso-u.cmd"
  [IO.File]::WriteAllText($falsoCmd, "@powershell -NoProfile -ExecutionPolicy Bypass -File `"%~dp0falso-u.ps1`"`r`n", $utf8)
  [IO.File]::WriteAllText($falsoPs1, @'
# U falso: escribe en el log de U_DATA_DIR las lineas del guion a su hora, y se va cuando matan a su padre.
$utf8 = New-Object System.Text.UTF8Encoding $false
$guion = [IO.File]::ReadAllText($env:FALSO_U_GUION, $utf8) | ConvertFrom-Json
$dir = Join-Path $env:U_DATA_DIR "local\U\logs"
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$log = Join-Path $dir ("u-" + (Get-Date -Format yyyyMMdd) + ".log")
$padre = (Get-CimInstance Win32_Process -Filter "ProcessId=$PID").ParentProcessId
$t0 = Get-Date
[IO.File]::AppendAllText($log, ("[{0}] falso-u: arranca pid={1} padre={2}`r`n" -f (Get-Date -Format HH:mm:ss), $PID, $padre), $utf8)
foreach ($x in @($guion)) {
  while (((Get-Date) - $t0).TotalSeconds -lt [double]$x.s) {
    Start-Sleep -Milliseconds 100
    if (-not (Get-Process -Id $padre -ErrorAction SilentlyContinue)) { exit 0 }
  }
  [IO.File]::AppendAllText($log, ("[{0}] {1}`r`n" -f (Get-Date -Format HH:mm:ss), $x.l), $utf8)
}
while (((Get-Date) - $t0).TotalSeconds -lt 150 -and (Get-Process -Id $padre -ErrorAction SilentlyContinue)) { Start-Sleep -Milliseconds 300 }
'@, $utf8)

  function Correr([string]$nombre, [object[]]$guion, [string]$cuales, [int]$reps) {
    $dir = Join-Path $base $nombre
    New-Item -ItemType Directory -Force -Path (Join-Path $dir "datos\local\U") | Out-Null
    $g = Join-Path $dir "guion.json"
    $items = @($guion | ForEach-Object { [ordered]@{ s = $_[0]; l = (L $_[1]) } })
    [IO.File]::WriteAllText($g, (ConvertTo-Json -InputObject $items -Depth 3), $utf8)
    $env:FALSO_U_GUION = $g
    $sal = Join-Path $dir "salida"
    $ini = Get-Date
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $aqui "conducir.ps1") -Exe $falsoCmd -Datos (Join-Path $dir "datos") -Salida $sal -Tareas (Join-Path $aqui "tareas.json") -Cuales $cuales -Repeticiones $reps -Ensayo | Out-File (Join-Path $dir "consola.txt") -Encoding utf8
    $codigo = $LASTEXITCODE
    $d = Join-Path $sal "conductor.log"
    $texto = if (Test-Path $d) { [IO.File]::ReadAllText($d, $utf8) } else { "" }
    Write-Host ("   {0}: codigo {1} en {2:0} s" -f $nombre, $codigo, ((Get-Date) - $ini).TotalSeconds)
    if ($codigo -eq 4) { Write-Host "NO PUDE EJECUTAR: el escritorio de entrada no es 'Default' (salvapantallas o bloqueo). Esto no dice nada del conductor."; exit 2 }
    return @{ codigo = $codigo; diario = $texto; dir = $dir }
  }
  function Veces([string]$texto, [string]$patron) { return ([regex]::Matches($texto, $patron)).Count }
  $abre = @(@(1, 'voz-viva: sesi~on abierta con <<gpt-live-1>> (OpenAI GPT-Live)'), @(1.2, 'voz-viva: el servidor confirm~o la sesi~on'))
}

if ($Conductor) {
  # A: la voz se cierra sola durante la bateria (como a las 07:43:08 del 2026-09-11). Al terminar NO se pulsa
  # Ctrl+Alt+M: la reabriria justo antes de matar U. Se cierra a los 21 s del falso, cuando el conductor ya
  # comprobo (a los ~18 s) que abrio, y la bateria de 6 repeticiones en ensayo acaba hacia los ~28 s.
  $ra = Correr "A-se-cierra-sola" ($abre + @(@(21, 'voz-viva: micr~ofono cerrado'), @(21, 'voz-viva: sesi~on cerrada'))) "T1" 6
  Comprueba "A: termina bien (codigo 0)" { $ra.codigo -eq 0 }
  Comprueba "A: con la voz ya cerrada, al final NO se pulsa Ctrl+Alt+M (V7)" { $ra.diario -match 'NO se pulsa Ctrl\+Alt\+M, que la reabriria' }
  Comprueba "A: no se pulsa Ctrl+Alt+M ni una sola vez (tampoco al empezar: la voz ya estaba abierta)" { (Veces $ra.diario '\(ensayo\) Ctrl\+Alt\+M sin pulsar') -eq 0 }

  # B: la voz sigue abierta al terminar: se cierra con el atajo, una vez, para que U deje la linea de lo que duro (218).
  $rb = Correr "B-sigue-abierta" $abre "T1" 1
  Comprueba "B: termina bien (codigo 0)" { $rb.codigo -eq 0 }
  Comprueba "B: con la voz abierta, al final SI se cierra con Ctrl+Alt+M" { $rb.diario -match 'cierro la voz con Ctrl\+Alt\+M' }
  Comprueba "B: el atajo se pulsa exactamente una vez" { (Veces $rb.diario '\(ensayo\) Ctrl\+Alt\+M sin pulsar') -eq 1 }

  # C: sin credito, las lineas literales de las 23:36. Se para al arrancar con su propio codigo, sin bateria.
  $rc = Correr "C-sin-credito" @(
    @(1, 'voz-viva: sesi~on abierta con <<gpt-live-1>> (OpenAI GPT-Live)'),
    @(1, 'voz-viva: micr~ofono abierto a 24000 Hz'),
    @(1, 'voz-viva: el servidor dice: You have no credits remaining. Add credits to continue using the API at https://platform.openai.com/settings/organization/billing/.'),
    @(3, 'voz-viva: se cort~o la escucha: The remote party closed the WebSocket connection without completing the close handshake.'),
    @(3, 'voz-viva: la sesi~on no lleg~o a abrir: el servidor contest~o <<You have no credits remaining.>> en vez de confirmarla, y cerr~o. No se reintenta: la misma apertura fallar~ia igual'),
    @(3, 'voz-viva: sesi~on cerrada')) "T1,T4" 3
  Comprueba "C: sin credito sale con el codigo 5, no con el 3 de 'la voz no abrio'" { $rc.codigo -eq 5 }
  Comprueba "C: el diario dice que la cuenta no tiene credito" { $rc.diario -match 'la cuenta no tiene credito' }
  Comprueba "C: no gasta la bateria: ninguna tarea empieza" { ($rc.diario -notmatch 'fin de la bateria') -and ($rc.diario -notmatch 'T1 r1:') }

  # E: V5. La voz dice "sesion abierta con" y el servidor la rechaza por otra causa que el credito ("Instructions
  # must not exceed 16384 tokens", medido el 2026-09-12: llega en lugar de session.started y cierra). El cierre
  # llega a los 13 s del falso, despues de que el conductor la viera abierta (~12 s) y antes de volver a mirar
  # (~18 s): tiene que parar con el 3 y sin bateria. Escrito DESPUES del codigo (2026-09-13); su rojo es S7.
  $re = Correr "E-parecia-abierta" @(
    @(1, 'voz-viva: sesi~on abierta con <<gpt-live-1>> (OpenAI GPT-Live)'),
    @(1, 'voz-viva: el servidor dice: Instructions must not exceed 16384 tokens'),
    @(13, 'voz-viva: la sesi~on no lleg~o a abrir: el servidor contest~o <<Instructions must not exceed 16384 tokens>> en vez de confirmarla, y cerr~o. No se reintenta: la misma apertura fallar~ia igual'),
    @(13, 'voz-viva: sesi~on cerrada')) "T1" 3
  Comprueba "E: la voz que parecia abierta y a los 6 s esta cerrada para con el codigo 3" { $re.codigo -eq 3 }
  Comprueba "E: lo dice, y no corre la bateria" { ($re.diario -match 'parecia abierta') -and ($re.diario -notmatch 'fin de la bateria') }

  # F: la forma de la apertura confirmada. Al arrancar el log dice "socket conectado" sin confirmar: pulsar
  # Ctrl+Alt+M la cerraria. Se espera, y el atajo solo se pulsa una vez, al final. Escrito DESPUES del codigo; su rojo es S8.
  $rf = Correr "F-abriendo-al-arrancar" @(
    @(1, 'voz-viva: socket conectado con <<gpt-live-1>> (OpenAI GPT-Live): falta que el servidor confirme la sesi~on'),
    @(14, 'voz-viva: sesi~on abierta con <<gpt-live-1>> (OpenAI GPT-Live)')) "T1" 1
  Comprueba "F: termina bien (codigo 0)" { $rf.codigo -eq 0 }
  Comprueba "F: con la voz abriendose, al arrancar espera la confirmacion sin pulsar" { $rf.diario -match 'se espera a que el servidor confirme' }
  Comprueba "F: el atajo se pulsa una sola vez, la del final" { (Veces $rf.diario '\(ensayo\) Ctrl\+Alt\+M sin pulsar') -eq 1 }
}

if ($Escritorio) {
  Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
  $sh = New-Object -ComObject Shell.Application
  function Exps { @($sh.Windows() | Where-Object { $_ -and $_.FullName -match 'explorer\.exe$' }) }
  function AbrirEn([string]$donde) {
    $antes = @(Exps | ForEach-Object { [int64]$_.HWND })
    Start-Process explorer.exe -ArgumentList $donde
    for ($i = 0; $i -lt 20; $i++) { Start-Sleep -Milliseconds 400; $n = @(Exps | Where-Object { $antes -notcontains [int64]$_.HWND }); if ($n) { return [int64]$n[0].HWND } }
    throw "no aparecio la ventana del explorador en $donde"
  }
  function Ventana([int64]$h) { return (Exps | Where-Object { [int64]$_.HWND -eq $h } | Select-Object -First 1) }
  try {
    # D1: las piezas solas, con un explorador en Descargas
    $h = AbrirEn 'shell:Downloads'; $abiertas += $h; Start-Sleep -Seconds 1
    Comprueba "D1: con un explorador en Descargas, el estado final de T4 ya se cumple" { Estado "T4" }
    $m = LlevarAPartidaNeutra "T4"
    Comprueba "D1: llevarlo a partida neutra funciona y no devuelve motivo ('$m')" { $m -eq "" }
    Comprueba "D1: despues, el estado final de T4 ya no se cumple y la ventana esta en otro sitio" { (-not (Estado "T4")) -and ((Ventana $h).LocationName -notmatch '^(Descargas|Downloads)$') }

    # D2: Configuracion en Bluetooth. Medido hoy: "ms-settings:" la deja en la pagina donde estaba, y
    # "ms-settings:home" la lleva a Inicio.
    if (Get-Process SystemSettings -ErrorAction SilentlyContinue) {
      Write-Host "   D2 no se corre: Configuracion ya estaba abierta y no es de esta prueba cerrarla."
    } else {
      Start-Process 'ms-settings:bluetooth'
      $ya = $false; for ($i = 0; $i -lt 20; $i++) { Start-Sleep -Milliseconds 500; if (Estado "T3") { $ya = $true; break } }
      Comprueba "D2: con Configuracion en Bluetooth, el estado final de T3 ya se cumple" { $ya }
      $m2 = LlevarAPartidaNeutra "T3"
      Comprueba "D2: llevarla a partida neutra funciona ('$m2') y T3 deja de cumplirse" { ($m2 -eq "") -and (-not (Estado "T3")) }
      Get-Process SystemSettings -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill() }
    }

    if ($Conductor) {
      # D3: el conductor entero con un explorador en Descargas abierto ANTES (V8): T4 empieza cumplido, y el
      # conductor lo dice y lo deshace antes de pulsar Enter.
      $h3 = AbrirEn 'shell:Downloads'; $abiertas += $h3; Start-Sleep -Seconds 1
      $d3 = Correr "D3-t4-ya-cumplido" $abre "T4" 1
      Comprueba "D3: termina bien (codigo 0)" { $d3.codigo -eq 0 }
      Comprueba "D3: el conductor deja constancia de que T4 ya se cumplia antes de empezar" { $d3.diario -match 'T4 r1: el estado final YA se cumplia antes de empezar' }
      Comprueba "D3: y lo lleva a un estado de partida neutro" { $d3.diario -match 'T4 r1: .*llevado a un estado de partida neutro' }
      Comprueba "D3: el explorador abierto antes ya no esta en Descargas" { (Ventana $h3).LocationName -notmatch '^(Descargas|Downloads)$' }
    }
  } finally {
    foreach ($x in $abiertas) { $v = Ventana $x; if ($v) { try { $v.Quit() } catch { Write-Host "   no pude cerrar la ventana de prueba ${x}: $($_.Exception.Message)" } } }
  }
}

$malos = @($resultados | Where-Object { -not $_[1] })
Write-Host ""
if ($malos.Count -gt 0) { Write-Host "CONDUCTOR ROTO: $($malos.Count) de $($resultados.Count) comprobacion(es) incumplida(s)."; exit 1 }
Write-Host "CONDUCTOR INTACTO: $($resultados.Count) de $($resultados.Count) comprobaciones."
exit 0
