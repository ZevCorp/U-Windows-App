# Las piezas del conductor del nivel 4 que se pueden juzgar sin lanzar U: leer el log de la voz y mirar el
# escritorio. Las carga conducir.ps1 (con ". piezas.ps1") y las juzga autoprueba-conductor.ps1.
#
# ASCII PURO (ver conducir.ps1): los acentos del log se casan con "." en las expresiones ("sesi.n").

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$ShellDeWindows = New-Object -ComObject Shell.Application

# -- El log --------------------------------------------------------------------

# El estado de la voz segun la ULTIMA linea de estado de "voz-viva" del texto: "abierta", "abriendo",
# "cerrada", o "" si no hay ninguna.
#
# POR QUE NO BASTA CON BUSCAR "sesi.n abierta" (intento del 2026-09-12, V5 y V7 de la spec 018):
#  - "sesion abierta con" se escribe al conectar el socket, en el mismo segundo que el error de cuota; a los
#    2 s el log ya decia "sesion cerrada", y el conductor seguia creyendola abierta. Manda la ultima linea.
#  - la cuenta de Miracle escribe "cuenta: cuenta creada y sesion abierta": sin mirar la etiqueta, eso tambien
#    "abria la voz". Solo cuentan las lineas "[hh:mm:ss] voz-viva: ...", y por su principio: "usuario dijo:
#    sesion cerrada" es algo que alguien dijo, no un cierre.
#  - la forma que va a escribir el arreglo de la apertura: una linea con "socket conectado" es "abriendo"
#    (sin confirmar), y "sesion abierta con" sin "socket conectado", o "el servidor confirmo la sesion", es
#    "abierta". "sesion abierta con" a secas, la forma de hoy, sigue siendo "abierta": Realtime no confirma.
#  - "el servidor dice: sesion cerrada: <motivo>" (session.closed de GPT-Live) no cambia nada por si sola:
#    detras vienen la reconexion o el cierre de la app, que son las que lo dicen.
function EstadoDeLaVoz([string]$texto) {
  $estado = ""
  foreach ($linea in ($texto -split '\r?\n')) {
    $m = [regex]::Match($linea, '^\[\d\d:\d\d:\d\d\] voz-viva: (.*?)\s*$')
    if (-not $m.Success) { continue }
    $msg = $m.Groups[1].Value
    if ($msg -match 'socket conectado') { $estado = "abriendo" }
    elseif ($msg -match '^sesi.n abierta con' -or $msg -match '^el servidor confirm. la sesi.n') { $estado = "abierta" }
    elseif ($msg -match '^sesi.n cerrada$' -or $msg -match '^la sesi.n no lleg. a abrir' -or $msg -match '^no se pudo abrir la sesi.n' -or
            $msg -match '^sin OPENAI_API_KEY' -or $msg -match '^la conexi.n se cay. \d+ veces seguidas') { $estado = "cerrada" }
  }
  return $estado
}

# La primera linea que dice que la cuenta de OpenAI no tiene credito, o "" si ninguna lo dice.
#
# Tres formas, las tres LITERALES del log del 2026-09-12: la voz de GPT-Live solo trae el mensaje ("You have
# no credits remaining", sin codigo); Realtime cierra con "1013 insufficient_quota.credit_balance_exhausted";
# y el cerebro del agente vuelca el 429 en varias lineas, con "insufficient_quota" en una de continuacion SIN
# hora delante. Por eso se busca en el texto entero y no solo en las lineas "[hh:mm:ss] etiqueta:".
function SinCredito([string]$texto) {
  $m = [regex]::Match($texto, '(?im)^.*(credit_balance_exhausted|insufficient_quota|no credits remaining).*$')
  if (-not $m.Success) { return "" }
  $l = $m.Value.Trim()
  if ($l.Length -gt 240) { $l = $l.Substring(0, 240) + "..." }
  return $l
}

# -- El escritorio -------------------------------------------------------------

# LOS TIPOS SE NOMBRAN ENTEROS, sin atajos como $A: este archivo se carga con ". piezas.ps1" en el ambito de
# quien lo usa, y en PowerShell $a y $A son la MISMA variable. El 2026-09-13 la autoprueba guardo un resultado
# en $a y EnConfiguracionBluetooth revento con "El valor no puede ser nulo: property".
function Exploradores { @($ShellDeWindows.Windows() | Where-Object { $_ -and $_.FullName -match 'explorer\.exe$' }) }

function ExploradorEnDestino($w, [string]$id) {
  if ($id -eq "T1") { return ($w.LocationName -match '^(Documentos|Documents)$' -or $w.LocationURL -match '/Documents$') }
  if ($id -eq "T4") { return ($w.LocationName -match '^(Descargas|Downloads)$' -or $w.LocationURL -match '/Downloads$') }
  return $false
}

function EnConfiguracionBluetooth {
  $tipo = [System.Windows.Automation.AutomationElement]::ControlTypeProperty
  $cw = New-Object System.Windows.Automation.PropertyCondition($tipo, [System.Windows.Automation.ControlType]::Window)
  foreach ($w in [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cw)) {
    if ($w.Current.Name -notmatch '^(Configuraci.n|Settings)$') { continue }
    $ci = New-Object System.Windows.Automation.PropertyCondition($tipo, [System.Windows.Automation.ControlType]::ListItem)
    foreach ($li in $w.FindAll([System.Windows.Automation.TreeScope]::Descendants, $ci)) {
      if ($li.Current.Name -notmatch '^Bluetooth') { continue }
      try { if ($li.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected) { return $true } }
      catch [System.InvalidOperationException] { }   # un ListItem sin SelectionItemPattern: no es la navegacion
    }
  }
  return $false
}

# Si el estado final de la tarea se cumple AHORA.
function Estado([string]$id) {
  if ($id -eq "T1" -or $id -eq "T4") { return [bool](@(Exploradores) | Where-Object { ExploradorEnDestino $_ $id }) }
  return (EnConfiguracionBluetooth)
}

# Lleva la app de la tarea a un estado de partida neutro cuando su estado final YA se cumple antes de empezar
# (V8: el 2026-09-12 habia tres exploradores abiertos antes de T4, y "Limpiar" solo cierra los nuevos).
# Devuelve "" si despues el estado final ya no se cumple, o el motivo por el que no se pudo.
#
# Medido el 2026-09-13 en esta maquina: Navigate2("shell:MyComputerFolder") lleva un explorador de Descargas a
# "Este equipo"; y "ms-settings:" deja Configuracion en la pagina donde estaba (Bluetooth seguia seleccionado),
# mientras que "ms-settings:home" la lleva a Inicio.
function LlevarAPartidaNeutra([string]$id) {
  $errores = @()
  if ($id -eq "T1" -or $id -eq "T4") {
    foreach ($w in @(Exploradores)) {
      if (-not (ExploradorEnDestino $w $id)) { continue }
      try { $w.Navigate2("shell:MyComputerFolder") } catch { $errores += "Navigate2 en la ventana $($w.HWND): $($_.Exception.Message)" }
    }
  } else {
    try { Start-Process "ms-settings:home" } catch { $errores += "ms-settings:home: $($_.Exception.Message)" }
  }
  for ($i = 0; $i -lt 12; $i++) {
    Start-Sleep -Milliseconds 500
    if (-not (Estado $id)) { return "" }
  }
  $motivo = "a los 6 s el estado final de $id se sigue cumpliendo"
  if ($errores) { $motivo += " (" + ($errores -join "; ") + ")" }
  return $motivo
}
