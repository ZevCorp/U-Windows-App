# GUARDIA DEL NUCLEO DEL GRAFO (hook PreToolUse de Claude Code, Edit|Write).
#
# El 2026-08-08 el grafo de navegacion alcanzo su punto estable y se declaro CONGELADO: SurfaceMap
# es la base sobre la que se construye el resto de la app, y sus invariantes estan escritos como
# pruebas en tests\ContratoDelGrafo. Este hook hace que editar el nucleo exija que el DUENO teclee
# una contrasena en un popup, a mano: no es criptografia contra atacantes, es friccion deliberada
# para que ningun agente (ni ningun descuido) toque la base sin una decision humana consciente.
#
# VIVE DENTRO DEL REPO (no en ~\.claude\hooks personal) para que CUALQUIERA que lo clone tenga la
# misma proteccion, no solo quien la escribio. Cada maquina crea su PROPIA contrasena la primera
# vez que hace falta —el hash se guarda junto a este script pero NUNCA se commitea (ver
# .gitignore)— asi que un desarrollador nuevo queda con su nucleo protegido de su propio agente
# desde el primer clone, sin heredar ni conocer la contrasena de nadie mas (2026-08-08, pedido por
# el usuario al entregar el repo a otro desarrollador).
#
# Como funciona:
#   - Recibe por stdin el JSON del tool call; si el archivo no esta en la lista, deja pasar (exit 0).
#   - Si esta protegido, muestra un dialogo con campo de contrasena (TopMost).
#   - La primera vez pide CREARLA (dos veces); el hash SHA-256 queda en nucleo-clave.hash, AL LADO
#     de este script (via $PSScriptRoot) y no en el perfil de usuario: asi cada copia del repo —la
#     tuya, la de otro desarrollador, la de otra maquina— tiene su propia contrasena local.
#   - Contrasena correcta -> exit 0 (se permite). Incorrecta o cancelar -> exit 2 (se bloquea, y el
#     mensaje de stderr le llega al agente para que sepa por que).
#
# La lista protege tambien al propio guardian, al contrato y a la configuracion que lo activa
# (.claude\settings.json): un candado que se puede editar —o cuyo interruptor se puede apagar— para
# quitarle los dientes no es un candado. (Sigue existiendo la via honesta: teclear la contrasena.)
#
# LIMITE HONESTO: esto protege de un AGENTE de IA usando Claude Code sobre este repo. No protege de
# una edicion humana directa con otro editor, ni de borrar estos archivos por fuera de Claude Code
# —eso ninguna configuracion de hooks lo puede impedir—. Es friccion para el agente, no un candado
# de sistema de archivos.

$ErrorActionPreference = 'Stop'

# --- 1. Que archivo se quiere editar -----------------------------------------
$crudo = [Console]::In.ReadToEnd()
try { $j = $crudo | ConvertFrom-Json } catch { exit 0 }
$fp = $j.tool_input.file_path
if (-not $fp) { exit 0 }
$ruta = ($fp -replace '/', '\')

# --- 1b. Lo que NO se edita NI CON CONTRASENA --------------------------------
# Las instantaneas de versiones (versiones\nucleo\vN.cs) y el registro de versiones son la memoria
# del sistema de versiones: el agente no puede ni PEDIR permiso sobre ellas (2026-08-08, pedido
# por el usuario). La unica via de editar el nucleo es el archivo de trabajo, que por construccion
# ES la version que el dueno eligio con -Editar. Cambiar de version en edicion es un acto del
# dueno (scripts\version-nucleo.ps1), jamas del agente.
$vetados = @(
  '*\versiones\nucleo\*.cs',
  '*\U-versiones\versiones.json',
  '*\U-versiones\*version.txt'
)
foreach ($pat in $vetados) {
  if ($ruta -like $pat) {
    [Console]::Error.WriteLine("Ese archivo es del sistema de versiones del nucleo y NO se edita ni con contrasena. La unica version editable es la que el dueno eligio (script version-nucleo.ps1 -Editar N), y se edita en src\Navigation\SurfaceMap.cs.")
    exit 2
  }
}

$protegidos = @(
  '*\windows-app\windows-client\src\Navigation\SurfaceMap.cs',
  '*\windows-app\tests\ContratoDelGrafo\*',
  '*\.claude\hooks\guardia-nucleo.ps1',
  '*\.claude\hooks\nucleo-clave.hash',
  # El INTERRUPTOR tambien se protege: sin esto, apagar el candado seria tan facil como borrar tres
  # lineas de settings.json, y ninguna contrasena lo hubiera notado.
  '*\.claude\settings.json'
)
$esNucleo = $false
foreach ($pat in $protegidos) { if ($ruta -like $pat) { $esNucleo = $true; break } }
if (-not $esNucleo) { exit 0 }

# --- 1c. SIN DECLARAR QUE VAS A HACER, NO SE PIDE PERMISO --------------------
# Autorizar «se va a editar SurfaceMap.cs» no es autorizar nada: dice el archivo, no el CAMBIO. El
# agente tiene que dejar escrito ANTES que se propone hacer, y eso es lo que el popup enseña, para
# que la contrasena se teclee sabiendo a que se dice que si (2026-08-08, pedido por el usuario).
#
# Es exigible, no un ruego: sin declaracion fresca el hook bloquea y le explica al agente como
# declararla. La ventana de 20 minutos deja que un mismo cambio abarque varias ediciones sin tener
# que redeclarar en cada una; pasada, hay que volver a decir a que se viene.
$intencionFile = 'C:\U-versiones\intencion.txt'
$intencion = ''
if (Test-Path $intencionFile) {
  $edad = (Get-Date) - (Get-Item $intencionFile).LastWriteTime
  if ($edad.TotalMinutes -le 20) {
    # -Encoding UTF8 NO ES OPCIONAL: PowerShell 5.1 lee con la codificacion ANSI del sistema, y
    # este archivo lo escribe un agente en UTF-8. Sin esto, «anadir» salia «aÃ±adir» en el popup —
    # y una descripcion que se lee mal es una descripcion que no se lee (2026-08-08, visto por el
    # usuario en pantalla).
    $intencion = (Get-Content $intencionFile -Raw -Encoding UTF8).Trim()
    # Y los saltos de linea a CRLF: el TextBox de WinForms solo corta con CRLF, asi que un archivo
    # con LF salia todo pegado en un parrafo ilegible.
    $intencion = $intencion -replace "`r`n", "`n" -replace "`n", "`r`n"
  }
}
if (-not $intencion) {
  [Console]::Error.WriteLine("Antes de tocar el nucleo tienes que DECLARAR que vas a hacer: escribe en $intencionFile una descripcion corta del cambio (que se cambia y por que), y vuelve a intentar la edicion. El dueno vera esa descripcion en el popup y autorizara sabiendo a que dice que si. Si ya la escribiste hace mas de 20 minutos, esta caducada: vuelve a escribirla.")
  exit 2
}

# Que version del nucleo se esta editando, para decirlo en el popup: el dueno autoriza sabiendo
# SOBRE QUE version esta autorizando.
$versionEnEdicion = ''
try {
  $reg = Get-Content 'C:\U-versiones\versiones.json' -Raw -ErrorAction Stop | ConvertFrom-Json
  if ($reg.EnEdicion -ge 0) { $versionEnEdicion = " (version del grafo en edicion: v$($reg.EnEdicion))" }
} catch {}

# --- 2. El dialogo -----------------------------------------------------------
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

function Pedir([string]$titulo, [string]$texto, [string]$declarado = '') {
  $alto = if ($declarado) { 380 } else { 190 }
  $f = New-Object System.Windows.Forms.Form
  $f.Text = $titulo
  $f.Size = New-Object System.Drawing.Size(520, $alto)
  $f.StartPosition = 'CenterScreen'
  $f.TopMost = $true
  $f.FormBorderStyle = 'FixedDialog'
  $f.MaximizeBox = $false; $f.MinimizeBox = $false

  $l = New-Object System.Windows.Forms.Label
  $l.Text = $texto
  $l.Location = New-Object System.Drawing.Point(12, 12)
  $l.Size = New-Object System.Drawing.Size(480, 50)
  $f.Controls.Add($l)

  $y = 66
  if ($declarado) {
    # LO QUE EL AGENTE DICE QUE VA A HACER: es lo que de verdad se esta autorizando. Va en un
    # cuadro de solo lectura y no en una etiqueta para que un cambio largo se lea entero con
    # scroll en vez de quedarse cortado.
    $cab = New-Object System.Windows.Forms.Label
    $cab.Text = 'El agente declara que va a hacer esto:'
    $cab.Location = New-Object System.Drawing.Point(12, $y)
    $cab.Size = New-Object System.Drawing.Size(480, 18)
    $f.Controls.Add($cab)
    $y += 20

    $d = New-Object System.Windows.Forms.TextBox
    $d.Multiline = $true; $d.ReadOnly = $true
    $d.ScrollBars = 'Vertical'
    $d.Text = $declarado
    $d.BackColor = [System.Drawing.Color]::FromArgb(245, 245, 245)
    $d.Location = New-Object System.Drawing.Point(12, $y)
    $d.Size = New-Object System.Drawing.Size(480, 150)
    $f.Controls.Add($d)
    $y += 160
  }

  $t = New-Object System.Windows.Forms.TextBox
  $t.Location = New-Object System.Drawing.Point(12, $y)
  $t.Size = New-Object System.Drawing.Size(480, 24)
  $t.UseSystemPasswordChar = $true
  $f.Controls.Add($t)
  $y += 32

  $ok = New-Object System.Windows.Forms.Button
  $ok.Text = 'Autorizar'; $ok.DialogResult = 'OK'
  $ok.Location = New-Object System.Drawing.Point(306, $y)
  $f.Controls.Add($ok); $f.AcceptButton = $ok

  $no = New-Object System.Windows.Forms.Button
  $no.Text = 'Cancelar'; $no.DialogResult = 'Cancel'
  $no.Location = New-Object System.Drawing.Point(402, $y)
  $f.Controls.Add($no); $f.CancelButton = $no

  $f.Add_Shown({ $f.Activate(); $t.Focus() })
  if ($f.ShowDialog() -eq 'OK') { return $t.Text } else { return $null }
}

function Hash([string]$s) {
  $sha = [System.Security.Cryptography.SHA256]::Create()
  ($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($s)) |
    ForEach-Object { $_.ToString('x2') }) -join ''
}

$archivoCorto = Split-Path -Leaf $ruta
# JUNTO A ESTE SCRIPT, no en el perfil del usuario. Antes vivia en ~\.claude\hooks —una sola
# contrasena, la de quien lo escribio—. Ahora el script viaja con el repo y cada copia (cada
# maquina, cada desarrollador) necesita SU PROPIO hash: si se leyera del perfil de usuario, un
# desarrollador nuevo heredaria el candado sin poder crear su propia contrasena, o peor, ninguna
# proteccion en absoluto si su perfil no tiene el archivo (2026-08-08).
$hashFile = Join-Path $PSScriptRoot 'nucleo-clave.hash'

# --- 3. Primera vez: crear la contrasena -------------------------------------
if (-not (Test-Path $hashFile)) {
  $a = Pedir 'Proteger el nucleo del grafo' ("Se va a editar {0}, que es parte del NUCLEO congelado.`nNo hay contrasena todavia: crea una (solo tu la sabras)." -f $archivoCorto)
  if (-not $a) { [Console]::Error.WriteLine('El nucleo esta protegido y no se creo contrasena: edicion bloqueada.'); exit 2 }
  $b = Pedir 'Confirmar contrasena' 'Escribela otra vez para confirmarla.'
  if ($a -cne $b) { [Console]::Error.WriteLine('Las dos contrasenas no coinciden: edicion bloqueada.'); exit 2 }
  Set-Content -Path $hashFile -Value (Hash $a) -Encoding ascii
  exit 0   # quien acaba de crearla, autoriza esta edicion
}

# --- 4. Las siguientes: pedirla ----------------------------------------------
$intento = Pedir 'Nucleo del grafo protegido' ("Se quiere editar: {0}{1}`n`nEste archivo es parte del nucleo CONGELADO del grafo." -f $archivoCorto, $versionEnEdicion) $intencion
if ($intento -and (Hash $intento) -eq ((Get-Content $hashFile -Raw).Trim())) { exit 0 }

[Console]::Error.WriteLine("El nucleo del grafo esta congelado y el dueno no autorizo esta edicion de $archivoCorto. Los invariantes viven en tests\ContratoDelGrafo; si el cambio de verdad hace falta, pide al usuario que autorice con su contrasena y pasa el contrato (scripts\contrato-del-grafo.ps1) antes de darlo por bueno.")
exit 2
