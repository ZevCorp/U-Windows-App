# El nivel 4 de la spec 017 en un comando: la misma bateria con el binario de main y con el de la
# rama, cada uno sobre su propia copia de los datos de U (para que lo aprendido en una corrida no le
# de ventaja a la otra), y la tabla de las dos al final. ASCII puro (ver conducir.ps1).
#
#   powershell -ExecutionPolicy Bypass -File scripts\nivel4-voz\correr.ps1
#
# Hace falta: alguien delante o, al menos, sin salvapantallas ni bloqueo (conducir.ps1 lo comprueba),
# OPENAI_API_KEY en el entorno, auriculares o la compuerta de eco puesta (U_SIN_ECO=0), y Python 3.
param(
  [string]$Base = (Join-Path $env:LOCALAPPDATA "Temp\claude\U-base-main\U.exe"),
  [string]$Rama = (Join-Path $env:LOCALAPPDATA "Temp\claude\U-rama-017\U.exe"),
  [int]$Repeticiones = 3,
  [string]$Cuales = "T1,T3,T4,T5"
)
$ErrorActionPreference = 'Stop'
$aqui = $PSScriptRoot
foreach ($exe in @($Base, $Rama)) {
  if (-not (Test-Path $exe)) {
    Write-Host "No encuentro $exe." -ForegroundColor Red
    Write-Host "Compilalo: desde main  -> dotnet build windows-client\WindowsClient.csproj -c Release -o <carpeta>"
    Write-Host "           desde la rama -> lo mismo en otra carpeta, y pasa -Base y -Rama."
    exit 1
  }
}
$sal = Join-Path $env:LOCALAPPDATA ("Temp\nivel4-voz-" + (Get-Date -Format "yyyyMMdd-HHmm"))
New-Item -ItemType Directory -Force -Path $sal | Out-Null

# Los datos de partida: una copia de los de este equipo, sin logs, grabaciones ni contratos.
$pristinos = Join-Path $sal "datos-pristinos"
New-Item -ItemType Directory -Force -Path (Join-Path $pristinos "roaming\U"), (Join-Path $pristinos "local\U") | Out-Null
foreach ($n in @("config.json", "graph.json")) {
  $f = Join-Path $env:APPDATA "U\$n"; if (Test-Path $f) { Copy-Item $f (Join-Path $pristinos "roaming\U") }
}
Get-ChildItem (Join-Path $env:LOCALAPPDATA "U") -ErrorAction SilentlyContinue |
  Where-Object { @("logs", "teach-videos", "tomas", "contrato") -notcontains $_.Name } |
  ForEach-Object { Copy-Item $_.FullName (Join-Path $pristinos "local\U") -Recurse }

foreach ($par in @(@("base", $Base), @("rama", $Rama))) {
  $datos = Join-Path $sal ("datos-" + $par[0])
  Copy-Item $pristinos $datos -Recurse
  Write-Host "== $($par[0]): $($par[1])" -ForegroundColor Cyan
  & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $aqui "conducir.ps1") -Exe $par[1] -Datos $datos -Salida (Join-Path $sal $par[0]) -Tareas (Join-Path $aqui "tareas.json") -Repeticiones $Repeticiones -Cuales $Cuales
  # Codigo 5: la cuenta de OpenAI no tiene credito (conducir.ps1 lo lee en el log de U y para). No se corre la
  # otra pasada: fallaria igual, y dos tablas de "0 de N" no son un veredicto sobre nada (2026-09-12).
  if ($LASTEXITCODE -eq 5) { Write-Host "La corrida '$($par[0])' se paro: la cuenta de OpenAI no tiene credito. No es un resultado de la rama. Mira $sal\$($par[0])\conductor.log" -ForegroundColor Red; exit 5 }
  if ($LASTEXITCODE -ne 0) { Write-Host "La corrida '$($par[0])' no termino (codigo $LASTEXITCODE): mira $sal\$($par[0])\conductor.log" -ForegroundColor Red; exit $LASTEXITCODE }
  & python (Join-Path $aqui "analizar.py") (Join-Path $sal $par[0]) $par[0] --plan ($Repeticiones * $Cuales.Split(",").Count) | Out-File (Join-Path $sal ($par[0] + ".md")) -Encoding utf8
}
Write-Host ""
Get-Content (Join-Path $sal "base.md"), (Join-Path $sal "rama.md") -Encoding UTF8
Write-Host ""
Write-Host "Todo en $sal (logs por tarea, capturas, analisis.json)." -ForegroundColor Green
