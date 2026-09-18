# LA ANATOMIA DEL CLIC: cuanto cuesta cada acto, fase a fase, leido del log. Fase 1 de
# docs/plan-clics-en-tiempo-real.md.
#
#   .\scripts\anatomia-del-clic.ps1 -Log C:\U-decisor\local\U\logs\u-20260918.log
#   .\scripts\anatomia-del-clic.ps1 -Log <log> -Desde 03:30 -Hasta 03:40     # una corrida concreta
#   .\scripts\anatomia-del-clic.ps1 -Log <log> -Detalle                        # cada acto, linea a linea
#
# POR QUE EXISTE. La spec 029 midio segundos por herramienta y la 030 midio que el clic era lo mas
# caro; las dos lo hicieron a mano sobre el log. Esto lo hace repetible: se corre antes y despues de
# cada corte, y si un corte no mueve estos numeros, no era un corte (spec 029 s7).
#
# QUE MIDE, por cada acto (map_take, map_decidir, map_type, map_go_to, map_open_app, map_batch):
#   - total: los ms que el propio despacho anota en la linea "<- (N ms)".
#   - fases, por los sellos de las lineas intermedias (resolucion de UN SEGUNDO, que es la del log):
#       antes   : desde "->" hasta la primera senal de mano/decisor (leer la pantalla, coreografia)
#       mano    : desde "mano: Execute" hasta "resultado accion"
#       despues : desde el resultado hasta "<-" (esperar el cambio, foto del album, inventario)
#   - senales: decisor (ms de Jev), homonimos, "hice 0 de", SATURADO, "mire otra vez", "lei la ventana".
# Un segundo de resolucion es poco para una fase de 300 ms; por eso el total sale del despacho y las
# fases se leen como orden de magnitud. Lo que necesite decimas se mide con su propio reloj en el codigo.
#
# Sin acentos a proposito: PowerShell 5.1 lee los .ps1 como ANSI (ver memoria del repo).

param(
  [Parameter(Mandatory=$true)][string]$Log,
  [string]$Desde = "",
  [string]$Hasta = "",
  [switch]$Detalle
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path $Log)) { throw "no existe el log: $Log" }

function Seg($hhmmss) { $p = $hhmmss.Split(':'); return [int]$p[0]*3600 + [int]$p[1]*60 + [int]$p[2] }
$desdeS = if ($Desde) { Seg($Desde + ":00".Substring(0, [Math]::Max(0, 8 - $Desde.Length))) } else { -1 }
$hastaS = if ($Hasta) { Seg($Hasta + ":59".Substring(0, [Math]::Max(0, 8 - $Hasta.Length))) } else { 999999 }

$lineas = Get-Content $Log -Encoding utf8
$actos = @()
$actual = $null
$rxSello = '^\[(\d\d:\d\d:\d\d)\]\s+(\S+?):\s+(.*)$'
foreach ($l in $lineas) {
  if ($l -notmatch $rxSello) { continue }
  $t = Seg($Matches[1]); $canal = $Matches[2]; $texto = $Matches[3]
  if ($t -lt $desdeS -or $t -gt $hastaS) { continue }

  if ($canal -eq 'mapa-mcp' -and $texto -match '^→ (map_take|map_decidir|map_type|map_go_to|map_open_app|map_batch)\b(.*)$') {
    if ($actual) { $actos += $actual }
    $actual = [ordered]@{ Hora=$l.Substring(1,8); T0=$t; TFin=$null; Herramienta=$Matches[1]; Args=$Matches[2].Trim(); TotalMs=$null; Lineas=@()
                          TMano=$null; TResultado=$null; TPrimeraSenal=$null; Jev=$null; Homonimos=$false; CeroDe=$false
                          Saturado=0; MiroOtraVez=$null; LeiVentana=$null; Cambio=$null; Cuenta="" }
    continue
  }
  if (-not $actual) { continue }
  $actual.Lineas += $l
  if ($canal -eq 'mapa-mcp' -and $texto -match '^← \((\d+) ms\)\s*(.*)$') {
    # La respuesta de OTRA herramienta (map_esto_es en paralelo) tambien empieza por "<-": solo cierra
    # el acto la que no es un "nuevo recuerdo". Emparejar mal fue el error de la 029 que la 030 corrigio.
    if ($Matches[2] -match '^nuevo recuerdo') { continue }
    $actual.TotalMs = [int]$Matches[1]; $actual.Cuenta = $Matches[2]
    $actual.Homonimos = $actual.Cuenta -match 'puertas vivas para'
    $actual.CeroDe = $actual.Cuenta -match 'hice 0 de'
    $actual.Cambio = if ($actual.Cuenta -match 'ahora estás en') { 'si' } elseif ($actual.Cuenta -match 'no cambió') { 'no' } else { '' }
    $actual.TFin = $t
    $actos += $actual; $actual = $null
    continue
  }
  if ($canal -eq 'decisor' -and $texto -match '· (\d+) ms →') { $actual.Jev = [int]$Matches[1]; if (-not $actual.TPrimeraSenal) { $actual.TPrimeraSenal = $t } }
  if ($canal -eq 'mano' -and $texto -match '^Execute ') { $actual.TMano = $t; if (-not $actual.TPrimeraSenal) { $actual.TPrimeraSenal = $t } }
  if ($canal -eq 'mano' -and $texto -match 'resultado acci') { $actual.TResultado = $t }
  if ($canal -eq 'mapa-vivo' -and $texto -match 'SATURADO') { $actual.Saturado++ }
  if ($texto -match 'miré otra vez.*?en (\d+) ms') { $actual.MiroOtraVez = [int]$Matches[1] }
  if ($texto -match 'leí la ventana en (\d+) ms') { $actual.LeiVentana = [int]$Matches[1] }
}
if ($actual) { $actos += $actual }

# A objetos: una tabla hash no agrupa ni se mide por propiedad (Group-Object daba UN grupo sin nombre).
$cerrados = @($actos | Where-Object { $_.TotalMs -ne $null } | ForEach-Object { [pscustomobject]$_ })
if (-not $cerrados) { Write-Host "no hay actos cerrados en ese tramo del log."; exit 0 }

function Mediana($xs) { $s = @($xs | Where-Object { $_ -ne $null } | Sort-Object); if (-not $s) { return $null }; return $s[[int][Math]::Floor(($s.Count-1)/2)] }

Write-Host ""
Write-Host ("ANATOMIA DEL CLIC · {0} · {1} acto(s) cerrado(s)" -f (Split-Path $Log -Leaf), $cerrados.Count) -ForegroundColor Cyan
Write-Host ""
Write-Host ("{0,-8} {1,-12} {2,7} {3,6} {4,5} {5,7} {6,6} {7,-6} {8}" -f "hora","herramienta","total","antes","mano","despues","jev","cambio","senales")
foreach ($a in $cerrados) {
  $antes = if ($a.TPrimeraSenal) { $a.TPrimeraSenal - $a.T0 } else { $null }
  $mano = if ($a.TMano -and $a.TResultado) { $a.TResultado - $a.TMano } else { $null }
  $despues = if ($a.TResultado) { $a.TFin - $a.TResultado } else { $null }
  $sen = @()
  if ($a.Homonimos) { $sen += "homonimos" }
  if ($a.CeroDe) { $sen += "0-de-N" }
  if ($a.Saturado) { $sen += "SATURADO x$($a.Saturado)" }
  if ($a.MiroOtraVez) { $sen += "miro-otra-vez $($a.MiroOtraVez)ms" }
  if ($a.LeiVentana) { $sen += "lei-ventana $($a.LeiVentana)ms" }
  Write-Host ("{0,-8} {1,-12} {2,7} {3,6} {4,5} {5,7} {6,6} {7,-6} {8}" -f $a.Hora, $a.Herramienta, $a.TotalMs, $antes, $mano, $despues, $a.Jev, $a.Cambio, ($sen -join ' · '))
  if ($Detalle) { foreach ($x in $a.Lineas) { Write-Host ("    " + $x.Substring(0, [Math]::Min(150, $x.Length))) -ForegroundColor DarkGray } }
}

Write-Host ""
Write-Host "MEDIANAS por herramienta (ms del despacho) · y las fases en segundos enteros" -ForegroundColor Cyan
foreach ($g in ($cerrados | Group-Object Herramienta)) {
  $xs = $g.Group
  $medTotal = Mediana ($xs | ForEach-Object { $_.TotalMs })
  $medAntes = Mediana ($xs | Where-Object { $_.TPrimeraSenal } | ForEach-Object { $_.TPrimeraSenal - $_.T0 })
  $medMano  = Mediana ($xs | Where-Object { $_.TMano -and $_.TResultado } | ForEach-Object { $_.TResultado - $_.TMano })
  $medDesp  = Mediana ($xs | Where-Object { $_.TResultado } | ForEach-Object { $_.TFin - $_.TResultado })
  $medJev   = Mediana ($xs | ForEach-Object { $_.Jev })
  $hom = ($xs | Where-Object { $_.Homonimos }).Count; $cero = ($xs | Where-Object { $_.CeroDe }).Count
  Write-Host ("  {0,-12} n={1,-3} total {2,6} ms · antes {3} s · mano {4} s · despues {5} s · jev {6} ms · homonimos {7} · 0-de-N {8}" -f $g.Name, $xs.Count, $medTotal, $medAntes, $medMano, $medDesp, $medJev, $hom, $cero)
}
$sat = ($cerrados | Measure-Object -Property Saturado -Sum).Sum
Write-Host ("  SATURADO dentro de actos: {0} vuelta(s) descartadas" -f $sat)
Write-Host ""
Write-Host "Los tres numeros que se comparan entre cortes: segundos de herramienta, vueltas del modelo y segundos en llamadas que acabaron en nada (spec 029 s7)." -ForegroundColor DarkGray
$segHerr = [math]::Round((($cerrados | Measure-Object -Property TotalMs -Sum).Sum)/1000, 1)
$enNada = [math]::Round((($cerrados | Where-Object { $_.CeroDe -or $_.Homonimos } | Measure-Object -Property TotalMs -Sum).Sum)/1000, 1)
Write-Host ("  segundos de herramienta (actos): {0} s · en llamadas que acabaron en nada (0-de-N u homonimos): {1} s" -f $segHerr, $enNada)
