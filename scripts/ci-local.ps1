# CI LOCAL: reproduce los escenarios grabados contra una version del nucleo, en un entorno limpio.
#
#   .\scripts\ci-local.ps1              # contra la version en edicion
#   .\scripts\ci-local.ps1 -Version 0   # contra la v0 (la original): ¿lo nuevo rompio lo viejo?
#
# Que es esto: la mitad reproductora del CI. La mitad grabadora es la casilla «guardar esta prueba
# para CI» del paso a paso: al terminar un mapeo que salio bien, lo logrado queda congelado como
# MINIMO exigible en C:\U-versiones\escenarios\<app>.json. Este script vuelve a correr cada
# escenario y comprueba que la version pedida siga estando a la altura.
#
# Por que es LOCAL y no un servidor: estas pruebas controlan el PC de verdad — abren apps, mueven
# el foco, le preguntan a Gemini. Un runner en la nube no tiene este escritorio ni estas apps.
# Lo que si corre en la nube es el contrato (.github\workflows\contrato.yml), que no toca la
# pantalla. Dos niveles: el contrato vigila las PROMESAS del nucleo; esto vigila el RESULTADO
# sobre el terreno real.
#
# El entorno es LIMPIO Y APARTE (C:\U-ci\vN): ni el terreno del usuario se contamina con la
# prueba, ni la prueba hereda historia que esconda una regresion. La clave de Gemini y la
# credencial de Graph se heredan de la instalacion real, igual que hace dev-paralelo.
#
# OJO: mientras corre, el mapeo usa tu escritorio de verdad (abre la app, la recorre). No es un
# defecto: es la naturaleza de probar sobre el entorno real. No toques el raton durante la prueba.

[CmdletBinding()]
param(
  [int]$Version = -1,     # -1 = la version en edicion
  [string]$Solo = ""      # correr solo el escenario de esta app (p. ej. "explorer")
)

$ErrorActionPreference = 'Stop'
$raiz = if ($env:U_VERSIONES_DIR) { $env:U_VERSIONES_DIR } else { "C:\U-versiones" }

# --- que version se juzga -----------------------------------------------------
$reg = Get-Content (Join-Path $raiz "versiones.json") -Raw | ConvertFrom-Json
$n = if ($Version -ge 0) { $Version } else { $reg.EnEdicion }
$exe = Join-Path $raiz ("v{0}\bin\U.exe" -f $n)
if (-not (Test-Path $exe)) { throw "v$n no esta compilada ($exe). Corre version-nucleo.ps1 -Construir $n" }

# --- que escenarios hay -------------------------------------------------------
$escenarios = Get-ChildItem (Join-Path $raiz "escenarios") -Filter *.json -ErrorAction SilentlyContinue |
  Where-Object { $Solo -eq "" -or $_.BaseName -like "*$Solo*" }
if (-not $escenarios) { throw "no hay escenarios grabados. Haz un mapeo con paso a paso y marca «guardar esta prueba para CI»." }

Write-Host ("CI local: v{0} contra {1} escenario(s)" -f $n, @($escenarios).Count) -ForegroundColor Cyan

# --- proteger lo intocable, cerrar lo demas ------------------------------------
# Mismo criterio que dev-paralelo: jamas la app estable; todo lo demas estorba (dos instancias
# pintan dos capas y la sonda MCP solo puede tener un dueno del puerto).
$repo = Split-Path -Parent $PSScriptRoot
$protegidas = @((Join-Path $repo "windows-client\bin"), "C:\U-dev\bin")
function Cerrar-Instancias {
  foreach ($p in (Get-Process U -ErrorAction SilentlyContinue)) {
    $tocar = $true
    foreach ($d in $protegidas) {
      if ($p.Path -and $p.Path.StartsWith([IO.Path]::GetFullPath($d), [StringComparison]::OrdinalIgnoreCase)) { $tocar = $false }
    }
    if ($tocar) { try { [void]$p.CloseMainWindow(); if (-not $p.WaitForExit(4000)) { $p.Kill() } } catch {} }
  }
}

function Probe($tool, $a, $timeoutSec) {
  $body  = @{ tool = $tool; args = $a } | ConvertTo-Json -Compress
  $bytes = [Text.Encoding]::UTF8.GetBytes($body)
  Invoke-RestMethod -Uri "http://127.0.0.1:8791/mcp/" -Method Post -Body $bytes `
    -ContentType "application/json; charset=utf-8" -TimeoutSec $timeoutSec
}

$resultados = @()
foreach ($esc in $escenarios) {
  $e = Get-Content $esc.FullName -Raw | ConvertFrom-Json
  Write-Host ("`n=== {0}: exige >={1} pantallas, >={2} declarados, >={3} con accion ===" -f `
    $e.app, $e.minimos.pantallas, $e.minimos.declarados, $e.minimos.conAccion) -ForegroundColor Cyan

  # Entorno limpio por version Y por corrida: la prueba no hereda historia.
  $datos = "C:\U-ci\v$n"
  Cerrar-Instancias
  Remove-Item $datos -Recurse -Force -ErrorAction SilentlyContinue
  New-Item -ItemType Directory -Force -Path (Join-Path $datos "roaming\U"), (Join-Path $datos "local\U") | Out-Null
  $graphReal = Join-Path $env:APPDATA "U\graph.json"
  if (Test-Path $graphReal) { Copy-Item $graphReal (Join-Path $datos "roaming\U\graph.json") }

  $psi = New-Object System.Diagnostics.ProcessStartInfo
  $psi.FileName = $exe; $psi.WorkingDirectory = (Split-Path $exe); $psi.UseShellExecute = $false
  $psi.EnvironmentVariables["U_DATA_DIR"] = $datos
  $psi.EnvironmentVariables["U_MCP_PROBE"] = "1"
  $psi.EnvironmentVariables["U_AUTO_EXPLORER"] = "1"
  $gemini = $env:GEMINI_API_KEY; if (-not $gemini) { $gemini = [Environment]::GetEnvironmentVariable("GEMINI_API_KEY", "User") }
  if ($gemini) { $psi.EnvironmentVariables["GEMINI_API_KEY"] = $gemini }
  $app = [Diagnostics.Process]::Start($psi)

  # La sonda tarda en abrir: se espera a que conteste, no un rato fijo.
  $lista = $false
  for ($i = 0; $i -lt 60 -and -not $lista; $i++) {
    Start-Sleep -Milliseconds 500
    try { Probe "map_where_am_i" @{} 5 | Out-Null; $lista = $true } catch {}
  }
  if (-not $lista) { $resultados += [pscustomobject]@{ App = $e.app; Paso = $false; Detalle = "la app no levanto la sonda MCP" }; continue }

  # El mapeo entero, por el mismo camino que lo usaria el asistente. Tarda minutos.
  $proc = $e.app -replace '\.exe$', ''
  try { $r = Probe "map_learn_app" @{ app = $proc } 900 } catch { $r = "ERROR: $_" }
  Write-Host ("  mapeo: {0}" -f ("$r" -replace "`n", " ")) -ForegroundColor DarkGray

  # Cerrar con ventana para que el mapa haga su Save final, y juzgar por el ARCHIVO: los numeros
  # salen de lo que quedo en disco, no de lo que la app diga de si misma.
  try { [void]$app.CloseMainWindow(); if (-not $app.WaitForExit(8000)) { $app.Kill() } } catch {}
  Start-Sleep -Seconds 1
  $mapa = Get-Content (Join-Path $datos "local\U\surface-map.json") -Raw | ConvertFrom-Json

  $appId = $e.app.ToLowerInvariant()
  $pantallas = @($mapa.Nodes.PSObject.Properties | Where-Object { $_.Name -like "uia://$appId*" }).Count
  $aristas = $mapa.Edges.PSObject.Properties
  $declarados = @($aristas | Where-Object { $_.Name -like "uia://$appId*" -and $_.Value.NivelFijado -and $_.Value.NivelNav -ge 0 }).Count
  $conAccion  = @($aristas | Where-Object { $_.Name -like "uia://$appId*" -and $_.Value.Selector -and -not ($_.Name -split "`n")[1].StartsWith("?") }).Count

  $ok = ($pantallas -ge $e.minimos.pantallas) -and ($declarados -ge $e.minimos.declarados) -and ($conAccion -ge $e.minimos.conAccion)
  $detalle = "pantallas $pantallas/$($e.minimos.pantallas) · declarados $declarados/$($e.minimos.declarados) · con accion $conAccion/$($e.minimos.conAccion)"
  Write-Host ("  {0}  {1}" -f ($(if ($ok) { "OK " } else { "FALLO" })), $detalle) -ForegroundColor ($(if ($ok) { "Green" } else { "Red" }))
  $resultados += [pscustomobject]@{ App = $e.app; Paso = $ok; Detalle = $detalle }
}

Write-Host "`n================ RESUMEN (nucleo v$n) ================" -ForegroundColor Cyan
$resultados | ForEach-Object {
  Write-Host ("  {0}  {1} — {2}" -f ($(if ($_.Paso) { "OK " } else { "FALLO" })), $_.App, $_.Detalle) `
    -ForegroundColor ($(if ($_.Paso) { "Green" } else { "Red" }))
}
$rotos = @($resultados | Where-Object { -not $_.Paso }).Count
if ($rotos -gt 0) { Write-Host "v${n} NO esta a la altura de lo que ya funcionaba." -ForegroundColor Red; exit 1 }
Write-Host "v$n sostiene todo lo que ya funcionaba." -ForegroundColor Green
exit 0
