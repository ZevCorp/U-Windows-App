# CI DEL TERRENO: levanta una instancia limpia de la app y comprueba que el terreno CONTESTA por
# sus dos puertas reales — el servidor MCP (8790) y el servidor del nucleo (8792).
#
#   .\scripts\ci-terreno.ps1              # compila a un directorio de usar y tirar y juzga
#   .\scripts\ci-terreno.ps1 -Exe <ruta>  # juzga un U.exe ya compilado
#
# Es el heredero del ci-local viejo (gran limpieza, 2026-08-30): aquel reproducia escenarios del
# mapa por niveles (map_learn_app + minimos de plata), maquinaria que ya no existe. Este juzga lo
# que el sistema nuevo promete de verdad:
#
#   1. el MCP se presenta y publica el catalogo del terreno (y NO las herramientas retiradas)
#   2. map_where_am_i contesta donde estamos, sin inventar
#   3. map_what_i_see contesta que hay, sin inventar
#   4. el 8792 sirve el terreno como arbol para el visor
#
# Es LOCAL porque abre la app en tu escritorio; y es LIMPIO porque corre con un U_DATA_DIR de usar
# y tirar y SIN credencial de Graph/Neo4j: ni hereda tu memoria ni escribe en ella (la regla de
# «pruebas desde cero»). Por lo mismo NO mata nada: si el puerto ya tiene dueno —tu app viva—, se
# niega y te lo dice, porque juzgar tu instancia real contaminaria tu terreno.

[CmdletBinding()]
param(
  [string]$Exe = ""
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$fallos = 0

function Juzgar($nombre, $ok, $detalle) {
  $color = if ($ok) { "Green" } else { "Red" }
  Write-Host ("  {0}  {1} — {2}" -f ($(if ($ok) { "OK " } else { "FALLO" })), $nombre, $detalle) -ForegroundColor $color
  if (-not $ok) { $script:fallos++ }
}

# --- el puerto tiene que estar libre: no se juzga (ni contamina) la app viva ---
$ocupado = $false
try { Invoke-RestMethod -Uri "http://127.0.0.1:8790/mcp/" -Method Post -Body '{"jsonrpc":"2.0","id":0,"method":"initialize"}' -ContentType "application/json" -TimeoutSec 2 | Out-Null; $ocupado = $true } catch {}
if ($ocupado) {
  Write-Host "El 8790 ya tiene dueno: hay una app viva. Cierra U (la de desarrollo; la estable no se toca) y repite." -ForegroundColor Red
  exit 1
}

# --- el binario ---------------------------------------------------------------
if ($Exe -eq "") {
  $bin = Join-Path $env:TEMP "u-ci-terreno\bin"
  Write-Host "compilando a $bin ..." -ForegroundColor Cyan
  dotnet build (Join-Path $repo "windows-client\WindowsClient.csproj") -c Release -o $bin --nologo -v quiet -nodeReuse:false
  if ($LASTEXITCODE -ne 0) { throw "el cliente no compila (codigo $LASTEXITCODE)" }
  $Exe = Join-Path $bin "U.exe"
}
if (-not (Test-Path $Exe)) { throw "no existe $Exe" }

# --- instancia limpia ----------------------------------------------------------
$datos = Join-Path $env:TEMP "u-ci-terreno\datos"
Remove-Item $datos -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $datos | Out-Null

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $Exe; $psi.WorkingDirectory = (Split-Path $Exe); $psi.UseShellExecute = $false
$psi.EnvironmentVariables["U_DATA_DIR"] = $datos
# SIN credencial de Graph ni de Neo4j a proposito: el terreno de esta corrida nace y muere aqui.
$app = [Diagnostics.Process]::Start($psi)

function Mcp($cuerpo, $timeoutSec = 10) {
  $bytes = [Text.Encoding]::UTF8.GetBytes($cuerpo)
  Invoke-RestMethod -Uri "http://127.0.0.1:8790/mcp/" -Method Post -Body $bytes `
    -ContentType "application/json; charset=utf-8" -TimeoutSec $timeoutSec
}

try {
  # Se espera a que el MCP conteste, no un rato fijo.
  $listo = $false
  for ($i = 0; $i -lt 60 -and -not $listo; $i++) {
    Start-Sleep -Milliseconds 500
    try { Mcp '{"jsonrpc":"2.0","id":0,"method":"initialize","params":{}}' 3 | Out-Null; $listo = $true } catch {}
  }
  if (-not $listo) { Juzgar "arranque" $false "la app no levanto el MCP en 30 s"; exit 1 }
  Write-Host "la app contesta; juzgando el terreno:" -ForegroundColor Cyan

  # 1. el catalogo: lo del terreno esta, lo retirado no
  $lista = Mcp '{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}'
  $nombres = @($lista.result.tools | ForEach-Object { $_.name })
  $exigidas = @("map_where_am_i", "map_what_i_see", "map_go_to", "map_take", "map_batch", "map_ahead")
  $muertas  = @("map_run", "map_learn_app", "map_set_level", "map_places", "map_routes_from")
  $faltan = @($exigidas | Where-Object { $nombres -notcontains $_ })
  $zombis = @($muertas  | Where-Object { $nombres -contains $_ })
  Juzgar "catalogo" ($faltan.Count -eq 0 -and $zombis.Count -eq 0) `
    ("{0} herramientas; faltan: [{1}]; zombis: [{2}]" -f $nombres.Count, ($faltan -join ","), ($zombis -join ","))

  # 2. donde estamos: contesta algo, y no un error del despacho
  $donde = Mcp '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"map_where_am_i","arguments":{}}}' 30
  $textoDonde = ($donde.result.content | ForEach-Object { $_.text }) -join " "
  Juzgar "map_where_am_i" ($textoDonde.Length -gt 0 -and -not $donde.result.isError) `
    ($textoDonde.Substring(0, [Math]::Min(80, $textoDonde.Length)) -replace "`n", " | ")

  # 3. que se ve: idem
  $veo = Mcp '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"map_what_i_see","arguments":{}}}' 30
  $textoVeo = ($veo.result.content | ForEach-Object { $_.text }) -join " "
  Juzgar "map_what_i_see" ($textoVeo.Length -gt 0 -and -not $veo.result.isError) `
    ($textoVeo.Substring(0, [Math]::Min(80, $textoVeo.Length)) -replace "`n", " | ")

  # 4. el 8792 sirve el terreno para el visor
  try {
    $terreno = Invoke-RestMethod -Uri "http://127.0.0.1:8792/terreno" -TimeoutSec 10
    Juzgar "8792/terreno" ($null -ne $terreno) "el visor tiene de donde pintar"
  } catch { Juzgar "8792/terreno" $false "$_" }
}
finally {
  try { [void]$app.CloseMainWindow(); if (-not $app.WaitForExit(8000)) { $app.Kill() } } catch {}
}

if ($fallos -gt 0) { Write-Host "`nEL TERRENO NO CONTESTA COMO PROMETE ($fallos fallo(s))." -ForegroundColor Red; exit 1 }
Write-Host "`nEL TERRENO CONTESTA: catalogo, situarse, mirar y visor." -ForegroundColor Green
exit 0
