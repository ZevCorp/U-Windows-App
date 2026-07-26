# Arranca Graph EN LOCAL y lanza la app Windows apuntada a él, en vez de al Graph de Vercel.
#
# Por qué existe: la app tiene DOS bases de backend distintas (el asistente y el modulo de
# workflows). Apuntar solo una deja la mitad del trafico yendo al Graph remoto, que es justo lo
# que hace imposible depurar una prueba local. Este script fija las dos.
#
#   .\scripts\dev-local.ps1                  # arranca Graph local + la app
#   .\scripts\dev-local.ps1 -SoloBackend     # solo Graph, para lanzar la app a mano desde el IDE
#   .\scripts\dev-local.ps1 -Puerto 3001

[CmdletBinding()]
param(
  [int]$Puerto = 3000,
  [string]$GraphRepo = "C:\Users\felip\OneDrive\Documentos\Code\Graph",
  [switch]$SoloBackend
)

$ErrorActionPreference = 'Stop'
$appRepo = Split-Path -Parent $PSScriptRoot
$baseLocal = "http://localhost:$Puerto"

if (-not (Test-Path $GraphRepo)) { throw "No encuentro el repo de Graph en $GraphRepo" }

# --- 1. Comprobaciones antes de arrancar nada -------------------------------
$envFile = Join-Path $GraphRepo ".env"
if (-not (Test-Path $envFile)) {
  Write-Warning "No hay .env en $GraphRepo."
  Write-Warning "Graph arrancara igual, pero SIN Neo4j no hay workflows (ni grabar ni ejecutar)."
  Write-Warning "Copia .env.example a .env y rellena NEO4J_URI / NEO4J_USER / NEO4J_PASSWORD."
}

$apiKey = $env:GRAPH_API_KEY
if (-not $apiKey) {
  $graphJson = Join-Path $env:APPDATA "U\graph.json"
  if (Test-Path $graphJson) {
    try { $apiKey = (Get-Content $graphJson -Raw | ConvertFrom-Json).ApiKey } catch {}
  }
}
if (-not $apiKey) {
  Write-Warning "Sin API key de Graph. Ponla en GRAPH_API_KEY o en $env:APPDATA\U\graph.json (campo ApiKey)."
}

if (Get-Process U -ErrorAction SilentlyContinue) {
  throw "U.exe ya esta corriendo. Cierralo antes: bloquea U.Graph.dll y ademas seguiria apuntando al backend viejo."
}

# --- 2. Graph local ---------------------------------------------------------
$yaArriba = $false
try {
  Invoke-WebRequest -Uri $baseLocal -TimeoutSec 2 -UseBasicParsing | Out-Null
  $yaArriba = $true
  Write-Host "Graph ya responde en $baseLocal - se reutiliza." -ForegroundColor Yellow
} catch {}

if (-not $yaArriba) {
  Write-Host "Arrancando Graph en $baseLocal ..." -ForegroundColor Cyan
  $env:PORT = "$Puerto"
  Start-Process -FilePath "node" -ArgumentList "web/server.js" -WorkingDirectory $GraphRepo `
                -WindowStyle Normal
  # Esperar a que escuche, en vez de dormir a ciegas.
  $listo = $false
  foreach ($i in 1..40) {
    Start-Sleep -Milliseconds 500
    try { Invoke-WebRequest -Uri $baseLocal -TimeoutSec 2 -UseBasicParsing | Out-Null; $listo = $true; break } catch {}
  }
  if (-not $listo) { throw "Graph no respondio en $baseLocal tras 20 s. Mira la ventana de node." }
  Write-Host "Graph escuchando en $baseLocal" -ForegroundColor Green
}

if ($SoloBackend) {
  Write-Host ""
  Write-Host "Backend listo. Para la app, en la MISMA consola donde la lances:" -ForegroundColor Cyan
  Write-Host "  `$env:U_BACKEND_URL = '$baseLocal'"
  Write-Host "  `$env:GRAPH_BASE_URL = '$baseLocal'"
  return
}

# --- 3. La app Windows, apuntada al backend local ---------------------------
# Las dos: U_BACKEND_URL manda en el asistente (Config.cs) y GRAPH_BASE_URL en el modulo de
# workflows (GraphConfig.cs). Solo viven en este proceso: no se toca %APPDATA%, asi que cerrar
# esta consola devuelve la app al Graph remoto sin deshacer nada.
$env:U_BACKEND_URL  = $baseLocal
$env:GRAPH_BASE_URL = $baseLocal
if ($apiKey) { $env:GRAPH_API_KEY = $apiKey }

Write-Host ""
Write-Host "Compilando y lanzando la app contra $baseLocal ..." -ForegroundColor Cyan
Push-Location (Join-Path $appRepo "windows-client")
try { dotnet run -c Debug } finally { Pop-Location }
