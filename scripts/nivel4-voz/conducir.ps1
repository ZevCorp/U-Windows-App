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
param(
  [Parameter(Mandatory=$true)][string]$Exe,
  [Parameter(Mandatory=$true)][string]$Datos,
  [Parameter(Mandatory=$true)][string]$Salida,
  [Parameter(Mandatory=$true)][string]$Tareas,
  [string]$Cuales = "T1,T3,T4,T5",
  [int]$Repeticiones = 3,
  [int]$TopePorTarea = 75
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
$A = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]

New-Item -ItemType Directory -Force -Path $Salida | Out-Null
$diario = Join-Path $Salida "conductor.log"
function D([string]$m) {
  $l = "[{0}] {1}" -f (Get-Date -Format "HH:mm:ss.fff"), $m
  [IO.File]::AppendAllText($diario, $l + "`r`n", $utf8); Write-Host $l
}
function EscritorioNormal { return ([W]::Escritorio() -eq "Default") }

$esc = [W]::Escritorio()
if ($esc -ne "Default") {
  D "ABORTADO ANTES DE EMPEZAR: el escritorio de entrada es '$esc', no 'Default'. Con el salvapantallas o la pantalla de bloqueo delante, la entrada inyectada no llega (y keybd_event no avisa). Mueve el raton y vuelve a correr."
  exit 4
}

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

# -- Arranque ----------------------------------------------------------------
Get-Process U -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $Exe } | ForEach-Object { try { $_.Kill() } catch {} }
$env:U_DATA_DIR = $Datos
$p = Start-Process -FilePath $Exe -PassThru
D "lanzado $Exe pid=$($p.Id) datos=$Datos"
Start-Sleep -Seconds 12
$log = LogActual
if (-not $log) { D "ABORTADO: no aparece el log en $logs"; try { $p.Kill() } catch {}; exit 2 }
$ruta = $log.FullName
D "log: $ruta"
# LA VOZ SE DA POR ABIERTA CUANDO EL SERVIDOR LO CONFIRMA, NO CUANDO U CONECTA EL SOCKET (2026-09-13). Hasta
# entonces esto esperaba "sesion abierta" a secas, y esa linea salia al conectar: en el nivel 4 del 2026-09-12,
# con la cuenta sin credito, salio en el mismo segundo que "el servidor dice: You have no credits remaining", y
# el conductor siguio como si hubiera voz (patron n.2). Desde la promesa 220 del grafo U escribe "socket
# conectado, esperando confirmacion de ..." al conectar y "sesion abierta con ...: el servidor la confirmo"
# solo al confirmar. La linea VIEJA (main, y todo binario anterior a la 220) se sigue aceptando para poder
# medir main, pero solo tras 3 s sin un error ni un corte detras. Y mientras la voz se esta abriendo no se
# pulsa Ctrl+Alt+M: alterna, y la cerraria. El estado lo lee apertura.ps1, juzgado por autoprueba-apertura.ps1.
. (Join-Path $PSScriptRoot "apertura.ps1")
$estado = EstadoDeLaVoz (LeerDesde $ruta 0)
$desde = 0
if ($estado -eq "nada" -or $estado -eq "cerrada") {
  D "abro la voz con Ctrl+Alt+M (estado de la voz en el log: $estado)"
  $desde = Tam $ruta
  [W]::CtrlAlt(0x4D)
} else { D "la voz ya se esta abriendo o esta abierta ($estado): NO se pulsa Ctrl+Alt+M (alterna, la cerraria)" }
$ok = $false; $sinConfirmarDesde = $null
for ($i = 0; $i -lt 50; $i++) {
  $estado = EstadoDeLaVoz (LeerDesde $ruta $desde)
  if ($estado -eq "confirmada") { $ok = $true; D "voz abierta: el servidor confirmo la sesion"; break }
  if ($estado -eq "fallando" -or $estado -eq "cerrada") { break }
  if ($estado -eq "sin-confirmar") {
    if ($null -eq $sinConfirmarDesde) { $sinConfirmarDesde = Get-Date }
    elseif (((Get-Date) - $sinConfirmarDesde).TotalSeconds -ge 3) { $ok = $true; D "voz abierta SIN confirmacion del servidor: linea de apertura vieja (binario anterior a la 220) y 3 s sin error ni corte detras"; break }
  } else { $sinConfirmarDesde = $null }
  Start-Sleep -Milliseconds 500
}
if (-not $ok) {
  if ($estado -eq "fallando" -or $estado -eq "cerrada") { $porque = "el log dice '$estado': un error, un corte o un cierre antes de que el servidor confirmara la sesion" }
  else { $porque = "a los 25 s el log sigue en '$estado', sin confirmacion del servidor" }
  D "ABORTADO: la voz no abrio: $porque (escritorio de entrada: '$([W]::Escritorio())')"; try { $p.Kill() } catch {}; exit 3
}
Start-Sleep -Seconds 6

$shell = New-Object -ComObject Shell.Application
function Exploradores { @($shell.Windows() | Where-Object { $_ -and $_.FullName -match 'explorer\.exe$' }) }
$iniciales = @(Exploradores | ForEach-Object { [int64]$_.HWND })
$settingsAntes = [bool](Get-Process SystemSettings -ErrorAction SilentlyContinue)
D "estado inicial: $($iniciales.Count) ventana(s) del explorador, Configuracion abierta=$settingsAntes"
function Limpiar {
  foreach ($w in @(Exploradores)) { try { if ($iniciales -notcontains [int64]$w.HWND) { $w.Quit() } } catch {} }
  if (-not $settingsAntes) { Get-Process SystemSettings -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch {} } }
  Start-Sleep -Milliseconds 1500
}
function EnConfiguracionBluetooth {
  $cw = New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)
  foreach ($w in $A::RootElement.FindAll($TS::Children, $cw)) {
    if ($w.Current.Name -notmatch '^(Configuraci.n|Settings)$') { continue }
    $ci = New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
    foreach ($li in $w.FindAll($TS::Descendants, $ci)) {
      if ($li.Current.Name -notmatch '^Bluetooth') { continue }
      try { if ($li.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected) { return $true } } catch {}
    }
  }
  return $false
}
function Estado([string]$id) {
  switch ($id) {
    "T1" { return [bool](@(Exploradores) | Where-Object { $_.LocationName -match '^(Documentos|Documents)$' -or $_.LocationURL -match '/Documents$' }) }
    "T4" { return [bool](@(Exploradores) | Where-Object { $_.LocationName -match '^(Descargas|Downloads)$' -or $_.LocationURL -match '/Downloads$' }) }
    default { return (EnConfiguracionBluetooth) }
  }
}
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
    if ($t.antes -eq "configuracion") { Start-Process "ms-settings:"; Start-Sleep -Seconds 4 }
    [W]::CtrlAlt(0x55); Start-Sleep -Milliseconds 1300
    $in = CuadroDeTexto
    if (-not $in) { D "$id r${r}: NO encuentro el cuadro de texto de la carita; no se teclea"; continue }
    try { $in.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($t.texto) }
    catch { D "$id r${r}: no pude escribir en el cuadro: $($_.Exception.Message)"; continue }
    if (-not $in.Current.HasKeyboardFocus) { try { $in.SetFocus() } catch {}; Start-Sleep -Milliseconds 400 }
    $frente = [W]::PidDelFrente()
    if ($frente -ne $p.Id -or -not $in.Current.HasKeyboardFocus -or -not (EscritorioNormal)) {
      D "$id r${r}: GUARDA: la carita no tiene el teclado (frente pid=$frente, foco=$($in.Current.HasKeyboardFocus), escritorio='$([W]::Escritorio())'); NO se pulsa Enter"
      try { $in.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue("") } catch {}
      continue
    }
    $desde = Tam $ruta
    $t0 = Get-Date
    [W]::Tecla(0x0D)
    D "$id r${r}: enviado (t0=$($t0.ToString('HH:mm:ss.fff')))"

    # Termina cuando U ya hablo y nada nuevo pasa en 5 s; tope duro $TopePorTarea.
    $limite = $t0.AddSeconds($TopePorTarea); $firma = ""; $quietoDesde = Get-Date; $hablo = $false
    while ((Get-Date) -lt $limite) {
      Start-Sleep -Milliseconds 700
      $nuevo = LeerDesde $ruta $desde
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
    D "$id r${r}: estado_final=$ok - ventana de observacion $seg s - hablo=$hablo"
    $o = [ordered]@{ tarea = $id; rep = $r; t0 = $t0.ToString('HH:mm:ss.fff'); tf = $tf.ToString('HH:mm:ss.fff'); estado_final = $ok; hablo = $hablo; tope = (-not $hablo) }
    [IO.File]::AppendAllText($resumen, ($o | ConvertTo-Json -Compress) + "`r`n", $utf8)
  }
}

D "fin de la bateria"
try { [W]::CtrlAlt(0x4D) } catch {}
Start-Sleep -Seconds 2
try { $p.Kill() } catch {}
Copy-Item $ruta (Join-Path $Salida "U-completo.log") -Force
Limpiar
D "hecho"
