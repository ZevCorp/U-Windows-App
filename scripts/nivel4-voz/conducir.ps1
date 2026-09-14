# Nivel 4 de la spec 017: conduce la VOZ de U escribiendo en "Escribele..." y guarda, por tarea, el
# trozo de log, el estado final y una captura. Lo usa correr.ps1; se puede llamar solo.
#
# ASCII PURO a proposito: PowerShell 5.1 lee un .ps1 sin BOM como ANSI, y un acento aqui acabaria
# tecleado como basura. Las frases viven en tareas.json (UTF-8) y se leen con codificacion explicita.
#
# DOS GUARDAS QUE NO SE NEGOCIAN:
#  1. El escritorio de entrada tiene que ser "Default". La noche del 2026-09-10 el salvapantallas del
#     fabricante (OLED Care) se trago toda la entrada inyectada: keybd_event falla EN SILENCIO y la voz
#     "no abria". Se comprueba al empezar y antes de cada Enter.
#  2. Enter solo se pulsa si la ventana del frente es de U Y su cuadro de texto tiene el teclado. Si no,
#     lo tecleado caeria en otra aplicacion.
#
# CODIGOS DE SALIDA: 0 bateria entera; 2 no aparece el log de U; 3 la voz no abrio (o no siguio abierta);
# 4 el escritorio de entrada no es "Default"; 5 la cuenta de OpenAI no tiene credito.
#
# -Ensayo no pulsa NINGUNA tecla ni teclea nada: lo que pulsaria lo escribe en el diario. Es lo que usa
# autoprueba-conductor.ps1 para juzgar las decisiones del conductor contra un U falso, sin U y sin credito.
param(
  [Parameter(Mandatory=$true)][string]$Exe,
  [Parameter(Mandatory=$true)][string]$Datos,
  [Parameter(Mandatory=$true)][string]$Salida,
  [Parameter(Mandatory=$true)][string]$Tareas,
  [string]$Cuales = "T1,T3,T4,T5",
  [int]$Repeticiones = 3,
  [int]$TopePorTarea = 75,
  [switch]$Ensayo
)
$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding $false
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public static class W {
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  [DllImport("user32.dll", SetLastError=true)] public static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
  [DllImport("user32.dll", SetLastError=true)] public static extern bool GetUserObjectInformation(IntPtr h, int index, StringBuilder pv, int len, out int needed);
  [DllImport("user32.dll")] public static extern bool CloseDesktop(IntPtr h);
  public static uint PidDelFrente() { uint p; GetWindowThreadProcessId(GetForegroundWindow(), out p); return p; }
  public static string Escritorio() {
    IntPtr d = OpenInputDesktop(0, false, 0x0001);
    if (d == IntPtr.Zero) return "(no se puede abrir: error " + Marshal.GetLastWin32Error() + ")";
    var sb = new StringBuilder(256); int n; GetUserObjectInformation(d, 2, sb, 512, out n); CloseDesktop(d); return sb.ToString(); }
  public static void Tecla(byte vk) { keybd_event(vk,0,0,UIntPtr.Zero); keybd_event(vk,0,2,UIntPtr.Zero); }
  public static void CtrlAlt(byte vk) {
    keybd_event(0x11,0x1D,0,UIntPtr.Zero); keybd_event(0x12,0x38,0,UIntPtr.Zero);
    keybd_event(vk,0,0,UIntPtr.Zero); keybd_event(vk,0,2,UIntPtr.Zero);
    keybd_event(0x12,0x38,2,UIntPtr.Zero); keybd_event(0x11,0x1D,2,UIntPtr.Zero);
  }
}
"@
# EstadoDeLaVoz, SinCredito, Exploradores, Estado, LlevarAPartidaNeutra: viven aparte para poder juzgarlas sin U.
. (Join-Path $PSScriptRoot "piezas.ps1")
$A = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]

New-Item -ItemType Directory -Force -Path $Salida | Out-Null
$diario = Join-Path $Salida "conductor.log"
function D([string]$m) {
  $l = "[{0}] {1}" -f (Get-Date -Format "HH:mm:ss.fff"), $m
  [IO.File]::AppendAllText($diario, $l + "`r`n", $utf8); Write-Host $l
}
function EscritorioNormal { return ([W]::Escritorio() -eq "Default") }
function Atajo([string]$nombre, [byte]$vk) { if ($Ensayo) { D "(ensayo) $nombre sin pulsar"; return }; [W]::CtrlAlt($vk) }

$esc = [W]::Escritorio()
if ($esc -ne "Default") {
  D "ABORTADO ANTES DE EMPEZAR: el escritorio de entrada es '$esc', no 'Default'. Con el salvapantallas o la pantalla de bloqueo delante, la entrada inyectada no llega (y keybd_event no avisa). Mueve el raton y vuelve a correr."
  exit 4
}
if ($Ensayo) { D "ENSAYO: no se pulsa ninguna tecla ni se teclea nada" }

$tj = [IO.File]::ReadAllText($Tareas, $utf8) | ConvertFrom-Json
$logs = Join-Path $Datos "local\U\logs"
function LogActual { Get-ChildItem $logs -Filter "u-*.log" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 1 }
function Tam([string]$r) { if (Test-Path $r) { (Get-Item $r).Length } else { 0 } }
function LeerDesde([string]$r, [long]$desde) {
  if (-not (Test-Path $r)) { return "" }
  $fs = [IO.File]::Open($r, 'Open', 'Read', 'ReadWrite')
  try { [void]$fs.Seek($desde, 'Begin'); $sr = New-Object IO.StreamReader($fs, $utf8); return $sr.ReadToEnd() } finally { $fs.Dispose() }
}
function Captura([string]$nombre) {
  try {
    $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
    $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($b.Left, $b.Top, 0, 0, $b.Size); $g.Dispose()
    $bmp.Save((Join-Path $Salida $nombre), [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  } catch { D "captura fallida: $($_.Exception.Message)" }
}

# EL LOG DE ESTE U, NO EL DEL DIA. El log es uno por dia (u-AAAAMMDD.log): con unos datos que ya tuvieran uno
# de hoy, leer desde el byte 0 mezclaba la voz de una corrida anterior con la de esta. correr.ps1 copia los
# datos sin logs y no lo sufria; conducir.ps1 llamado solo, si.
$tamAntes = @{}
Get-ChildItem $logs -Filter "u-*.log" -ErrorAction SilentlyContinue | ForEach-Object { $tamAntes[$_.FullName] = $_.Length }
$ruta = ""; $inicio = [long]0; $hayQueLimpiar = $false
function DesdeArranque { LeerDesde $ruta $inicio }

function Terminar([int]$codigo, [string]$porque) {
  if ($porque) { D $porque }
  try { if (-not $p.HasExited) { $p.Kill() } } catch { D "no pude matar U (pid $($p.Id)): $($_.Exception.Message)" }
  if ($ruta) { [IO.File]::WriteAllText((Join-Path $Salida "U-completo.log"), (DesdeArranque), $utf8) }
  if ($hayQueLimpiar) { Limpiar }
  if ($codigo -eq 0) { D "hecho" }
  exit $codigo
}

# SIN CREDITO SE PARA TODO, con su propio codigo. El 2026-09-12 la voz, Realtime y el cerebro del agente
# contestaron "You have no credits remaining", y el conductor gasto la bateria entera igual: 2 tareas de 75 s
# cada una sin una sola llamada, y dos tablas de "0 de 2" que no eran un veredicto sobre la rama.
function PararSiNoHayCredito([string]$texto, [string]$cuando) {
  $l = SinCredito $texto
  if ($l) { Terminar 5 "ABORTADO ($cuando): la cuenta no tiene credito; no se gasta la bateria. Lo dice el log de U: $l" }
}

# -- Arranque ----------------------------------------------------------------
Get-Process U -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $Exe } | ForEach-Object { try { $_.Kill() } catch { D "no pude matar un U anterior (pid $($_.Id)): $($_.Exception.Message)" } }
$env:U_DATA_DIR = $Datos
$p = Start-Process -FilePath $Exe -PassThru
D "lanzado $Exe pid=$($p.Id) datos=$Datos"
Start-Sleep -Seconds 12
$log = LogActual
if (-not $log -or ($tamAntes.ContainsKey($log.FullName) -and $log.Length -le $tamAntes[$log.FullName])) {
  Terminar 2 "ABORTADO: este U no ha escrito ningun log en $logs en 12 s"
}
$ruta = $log.FullName
if ($tamAntes.ContainsKey($ruta)) { $inicio = [long]$tamAntes[$ruta] }
D "log: $ruta (desde el byte $inicio)"

# LA VOZ ESTA ABIERTA SI LO DICE LA ULTIMA LINEA DE ESTADO, no si alguna vez dijo "sesion abierta" (V5).
$texto = DesdeArranque
PararSiNoHayCredito $texto "al arrancar"
$ev = EstadoDeLaVoz $texto
if ($ev -eq "abierta") { D "la voz ya estaba abierta: NO se pulsa Ctrl+Alt+M (alterna, la cerraria)" }
else {
  if ($ev -eq "abriendo") { D "la voz se esta abriendo (socket conectado, sin confirmar): NO se pulsa Ctrl+Alt+M, se espera a que el servidor confirme" }
  else { D "abro la voz con Ctrl+Alt+M (estado de la voz en el log: '$ev')"; Atajo "Ctrl+Alt+M" 0x4D }
  $ok = $false
  for ($i = 0; $i -lt 50; $i++) {
    Start-Sleep -Milliseconds 500
    $texto = DesdeArranque
    PararSiNoHayCredito $texto "abriendo la voz"
    $ev = EstadoDeLaVoz $texto
    if ($ev -eq "abierta") { $ok = $true; break }
  }
  if (-not $ok) { Terminar 3 "ABORTADO: la voz no abrio en 25 s (ultimo estado en el log: '$ev'; escritorio de entrada: '$([W]::Escritorio())')" }
}
# "sesion abierta con" sale en el mismo segundo que un error de apertura, y el cierre llega ~2 s despues
# (medido el 2026-09-12: 23:36:53 y 23:36:55). Tras esperar, se vuelve a mirar.
Start-Sleep -Seconds 6
$texto = DesdeArranque
PararSiNoHayCredito $texto "tras abrir la voz"
$ev = EstadoDeLaVoz $texto
if ($ev -ne "abierta") { Terminar 3 "ABORTADO: la voz parecia abierta y 6 s despues el log dice '$ev': no se corre una bateria que mediria otro camino" }
D "voz abierta (y lo sigue 6 s despues)"

$iniciales = @(Exploradores | ForEach-Object { [int64]$_.HWND })
$settingsAntes = [bool](Get-Process SystemSettings -ErrorAction SilentlyContinue)
D "estado inicial del escritorio: $($iniciales.Count) ventana(s) del explorador, Configuracion abierta=$settingsAntes"
function Limpiar {
  foreach ($w in @(Exploradores)) {
    try { if ($iniciales -notcontains [int64]$w.HWND) { $w.Quit() } } catch { D "no pude cerrar un explorador: $($_.Exception.Message)" }
  }
  if (-not $settingsAntes) { Get-Process SystemSettings -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { D "no pude cerrar Configuracion: $($_.Exception.Message)" } } }
  Start-Sleep -Milliseconds 1500
}
$hayQueLimpiar = $true
function CuadroDeTexto {
  $cp = New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $p.Id)
  $ci = New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, "Input")
  foreach ($w in $A::RootElement.FindAll($TS::Children, $cp)) { $x = $w.FindFirst($TS::Descendants, $ci); if ($x) { return $x } }
  return $null
}

# -- La bateria --------------------------------------------------------------
$resumen = Join-Path $Salida "resumen.jsonl"
foreach ($r in 1..$Repeticiones) {
  foreach ($id in $Cuales.Split(",")) {
    $t = $tj.$id
    if (-not (EscritorioNormal)) { D "$id r${r}: el escritorio de entrada ya no es 'Default' ('$([W]::Escritorio())'): se para la bateria"; break }
    Limpiar
    if ($t.antes -eq "explorador") { Start-Process explorer.exe; Start-Sleep -Seconds 3 }
    # "ms-settings:home" y no "ms-settings:": medido el 2026-09-13, "ms-settings:" deja Configuracion en la
    # pagina donde se quedo, y T5 podia empezar ya en Bluetooth, o en Sistema.
    if ($t.antes -eq "configuracion") { Start-Process "ms-settings:home"; Start-Sleep -Seconds 4 }

    # EL ESTADO INICIAL QUEDA ESCRITO, y si el final ya se cumple, se deshace o se marca (V8). El 2026-09-12 T4
    # empezo con tres exploradores ya en Descargas y "Limpiar" solo cierra los nuevos: el estado final bueno no
    # lo trajo la voz. Se corre igual (el log de lo que hizo la voz sigue valiendo), pero el juez no la aprueba.
    $estadoInicial = Estado $id
    $yaCumplido = $false
    if ($estadoInicial) {
      $motivo = LlevarAPartidaNeutra $id
      if ($motivo) { $yaCumplido = $true; D "$id r${r}: el estado final YA se cumplia antes de empezar y no se pudo deshacer ($motivo): se corre y queda marcada ya_cumplido (no medible)" }
      else { D "$id r${r}: el estado final YA se cumplia antes de empezar: llevado a un estado de partida neutro" }
    } else { D "$id r${r}: estado inicial: el estado final no se cumple" }
    if ($Ensayo) { D "$id r${r}: (ensayo) no se teclea ni se pulsa Enter"; continue }

    [W]::CtrlAlt(0x55); Start-Sleep -Milliseconds 1300
    $in = CuadroDeTexto
    if (-not $in) { D "$id r${r}: NO encuentro el cuadro de texto de la carita; no se teclea"; continue }
    try { $in.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($t.texto) }
    catch { D "$id r${r}: no pude escribir en el cuadro: $($_.Exception.Message)"; continue }
    if (-not $in.Current.HasKeyboardFocus) { try { $in.SetFocus() } catch { D "$id r${r}: SetFocus fallo: $($_.Exception.Message)" }; Start-Sleep -Milliseconds 400 }
    $frente = [W]::PidDelFrente()
    if ($frente -ne $p.Id -or -not $in.Current.HasKeyboardFocus -or -not (EscritorioNormal)) {
      D "$id r${r}: GUARDA: la carita no tiene el teclado (frente pid=$frente, foco=$($in.Current.HasKeyboardFocus), escritorio='$([W]::Escritorio())'); NO se pulsa Enter"
      try { $in.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue("") } catch { D "$id r${r}: no pude vaciar el cuadro: $($_.Exception.Message)" }
      continue
    }
    $desde = Tam $ruta
    $t0 = Get-Date
    [W]::Tecla(0x0D)
    D "$id r${r}: enviado (t0=$($t0.ToString('HH:mm:ss.fff')), voz en el log: '$(EstadoDeLaVoz (DesdeArranque))')"

    # Termina cuando U ya hablo y nada nuevo pasa en 5 s; tope duro $TopePorTarea.
    $limite = $t0.AddSeconds($TopePorTarea); $firma = ""; $quietoDesde = Get-Date; $hablo = $false
    while ((Get-Date) -lt $limite) {
      Start-Sleep -Milliseconds 700
      $nuevo = LeerDesde $ruta $desde
      if (SinCredito $nuevo) {
        [IO.File]::WriteAllText((Join-Path $Salida "$id-r$r-sin-credito.log"), $nuevo, $utf8)
        PararSiNoHayCredito $nuevo "$id r$r"
      }
      $f = "{0}|{1}|{2}" -f ([regex]::Matches($nuevo, "llamada recibida")).Count, ([regex]::Matches($nuevo, "mapa-mcp: ")).Count, ([regex]::Matches($nuevo, "voz-viva: \S{1,2} dijo:")).Count
      if ($f -ne $firma) { $firma = $f; $quietoDesde = Get-Date }
      if ($nuevo -match "voz-viva: \S{1,2} dijo:") { $hablo = $true }
      if ($hablo -and ((Get-Date) - $quietoDesde).TotalSeconds -ge 5) { break }
    }
    $tf = Get-Date
    $trozo = LeerDesde $ruta $desde
    [IO.File]::WriteAllText((Join-Path $Salida "$id-r$r.log"), $trozo, $utf8)
    $ok = Estado $id
    Captura "$id-r$r.png"
    $seg = [math]::Round(($tf - $t0).TotalSeconds, 1)
    D "$id r${r}: estado_final=$ok - ventana de observacion $seg s - hablo=$hablo - ya_cumplido=$yaCumplido"
    $o = [ordered]@{ tarea = $id; rep = $r; t0 = $t0.ToString('HH:mm:ss.fff'); tf = $tf.ToString('HH:mm:ss.fff'); estado_final = $ok; hablo = $hablo; tope = (-not $hablo); estado_inicial = $estadoInicial; ya_cumplido = $yaCumplido }
    [IO.File]::AppendAllText($resumen, ($o | ConvertTo-Json -Compress) + "`r`n", $utf8)
  }
}

D "fin de la bateria"
# CTRL+ALT+M ALTERNA: con la voz ya cerrada, la REABRE justo antes de matar U. Paso el 2026-09-12 a las 23:39:40
# y a las 23:43:06 (V7): una sesion nueva, y con ella otro error de cuota, en el log de la corrida. Solo se pulsa
# si la ultima linea de estado dice que la voz sigue viva. "abriendo" tambien: ConversacionEnVivo pone Viva
# antes de escribir la linea del socket (ArrancarAsync), asi que el atajo la cierra, no la abre. Cerrarla antes
# de matar U es lo que deja la linea "la voz duro al menos N s" (promesa 218).
$ev = EstadoDeLaVoz (DesdeArranque)
if ($ev -eq "abierta" -or $ev -eq "abriendo") {
  D "cierro la voz con Ctrl+Alt+M (estado de la voz en el log: '$ev')"
  Atajo "Ctrl+Alt+M" 0x4D
  $cerro = $false
  for ($i = 0; $i -lt 16; $i++) { Start-Sleep -Milliseconds 500; if ((EstadoDeLaVoz (DesdeArranque)) -eq "cerrada") { $cerro = $true; break } }
  if ($cerro) { D "la voz dijo 'sesion cerrada' antes de matar U" }
  else { D "la voz NO dijo 'sesion cerrada' en 8 s tras Ctrl+Alt+M: se mata U igual, y la linea de lo que duro la voz puede faltar" }
} else {
  D "la voz ya no esta abierta (estado de la voz en el log: '$ev'): NO se pulsa Ctrl+Alt+M, que la reabriria"
}
Terminar 0 ""
