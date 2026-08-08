# Levanta una SEGUNDA app de Ü para desarrollar, SIN tocar la app estable que ya está corriendo.
#
# Por qué existe (y por qué no sirve dev-local.ps1 para esto): dev-local.ps1 ABORTA si U.exe ya
# está vivo, porque asume que se cierra la app para desarrollar. Desde 2026-07-28 hay una versión
# estable en producción local que NO se cierra nunca (ver VOLVER-A-LA-VERSION-ESTABLE.md en el
# escritorio). Este script hace lo contrario: da por hecho que la estable sigue viva y se aparta de
# su camino en los TRES sitios donde las dos apps chocarían.
#
#   1. BINARIOS. El proceso vivo tiene bloqueados U.dll y System.Speech.dll dentro de
#      bin\x64\Release\net8.0-windows. Compilar ahí falla — y si algún día NO falla es porque la
#      app estable ya no está viva. Se compila a un directorio aparte y se rechaza cualquier salida
#      que caiga dentro del repo.
#   2. DATOS. Config, credenciales, logs, step-shots y vídeos se piden todos a U.Graph.UserPaths,
#      que obedece a U_DATA_DIR. Sin esto, la app de desarrollo le reescribe el config.json a la
#      estable y las dos escriben en el mismo log diario.
#      OJO — redirigir %APPDATA%/%LOCALAPPDATA% en el proceso hijo NO sirve, y se comprobó fallando
#      el 2026-07-28: Environment.GetFolderPath no lee esas variables, llama a la API de carpetas
#      conocidas de Windows. La segunda app siguió escribiendo en el log de la primera. Por eso el
#      aislamiento vive en el código (UserPaths) y aquí solo se pone la variable.
#   3. IDENTIDAD. El aislamiento se siembra con un correo distinto ("DEV (paralelo)"), para que la
#      telemetría de Windows Live no mezcle las dos apps como si fueran el mismo usuario.
#
# Las variables van en el entorno del PROCESO HIJO, no en esta consola: nada de lo que haga este
# script sobrevive a cerrarlo, y la app estable no se entera de que existe.
#
#   .\scripts\dev-paralelo.ps1                              # compila y lanza contra el Graph desplegado
#   .\scripts\dev-paralelo.ps1 -Backend http://localhost:3000  # contra un Graph local
#   .\scripts\dev-paralelo.ps1 -SoloCompilar                # compila y no lanza nada

[CmdletBinding()]
param(
  [string]$Salida  = "C:\U-dev2\bin",
  [string]$Datos   = "C:\U-dev2",
  [string]$Backend,
  [switch]$SoloCompilar
)

# Instalaciones que NO se cierran ni se compilan encima, pase lo que pase. No es una promesa de
# quien escribe el comando: es una lista que el script comprueba.
#   · bin\x64\Release del repo → la app ESTABLE (commit 3080777), viva desde el 2026-07-27.
#   · C:\U-dev\bin             → la app de desarrollo BUENA, la que ya hace la cadena completa.
# Las dos tienen copia congelada y verificada en Desktop\U-ROLLBACK-2026-07-28.
$Protegidas = @(
  (Join-Path (Split-Path -Parent $PSScriptRoot) "windows-client\bin"),
  "C:\U-dev\bin"
)

$ErrorActionPreference = 'Stop'
$appRepo  = Split-Path -Parent $PSScriptRoot
$proyecto = Join-Path $appRepo "windows-client\WindowsClient.csproj"
$release  = Join-Path $appRepo "windows-client\bin"

# --- 1. No compilar NUNCA encima de la app estable --------------------------
# Comparación por ruta completa y no por texto suelto: "C:\U-dev\bin" no debe confundirse con
# ningún subdirectorio del repo, y un -Salida relativo tiene que resolverse antes de juzgarlo.
$salidaAbs = [System.IO.Path]::GetFullPath($Salida)
foreach ($p in $Protegidas) {
  $pAbs = [System.IO.Path]::GetFullPath($p)
  if ($salidaAbs.StartsWith($pAbs, [StringComparison]::OrdinalIgnoreCase)) {
    throw "El directorio de salida ($salidaAbs) cae dentro de una instalacion PROTEGIDA ($pAbs). Elige otro."
  }
}

# --- 2. Estado de la app estable (informativo, no bloqueante) ---------------
$estable = Get-Process U -ErrorAction SilentlyContinue |
           Where-Object { $_.Path -and $_.Path.StartsWith($release, [StringComparison]::OrdinalIgnoreCase) }
if ($estable) {
  Write-Host ("App ESTABLE viva: PID {0}, arrancada {1}. No se toca." -f $estable.Id, $estable.StartTime) -ForegroundColor Green
} else {
  Write-Warning "No veo la app estable corriendo desde bin\. Si esperabas que siguiera viva, compruébalo ANTES de seguir."
}

# --- 3. Cerrar TODA instancia de desarrollo, no solo la de este directorio ---
# Una app viva bloquea su propio U.Graph.dll, asi que la segunda compilacion falla con MSB3027. Pero
# hay una razon mas fuerte que la compilacion, y costo una hora de diagnostico equivocado el
# 2026-08-07: cada instancia PINTA SU PROPIA CAPA ENCIMA DE LA PANTALLA. Dos vivas = dos tiras de
# niveles superpuestas, con ordenes distintos, que se leen como elementos duplicados de UNA sola
# tira. El sintoma no dice "hay dos apps": dice "el grafo esta mal". Un build viejo de otra rama
# (C:\U-dev3) llevaba un dia entero vivo porque este bucle solo miraba $salidaAbs.
#
# Se cierra por RUTA y no por nombre de proceso: "U" se llaman todas, y matar por nombre se llevaria
# por delante la app estable. Lo PROTEGIDO no se toca jamas; lo demas se cierra y se dice de donde
# era, para que cerrar una instancia ajena nunca sea silencioso.
foreach ($p in (Get-Process U -ErrorAction SilentlyContinue)) {
  $protegido = $false
  foreach ($d in $Protegidas) {
    if ($p.Path -and $p.Path.StartsWith([System.IO.Path]::GetFullPath($d), [StringComparison]::OrdinalIgnoreCase)) { $protegido = $true }
  }
  if ($protegido) { Write-Host ("PROTEGIDA, no se toca: PID {0}" -f $p.Id) -ForegroundColor Green; continue }
  if (-not $p.Path) { continue }
  $propia = $p.Path.StartsWith($salidaAbs, [StringComparison]::OrdinalIgnoreCase)
  if ($propia) { Write-Host ("Cerrando app de desarrollo anterior (PID {0})..." -f $p.Id) -ForegroundColor DarkGray }
  else { Write-Host ("Cerrando OTRA instancia de desarrollo (PID {0}) desde {1}: pintaria una segunda capa encima." -f $p.Id, $p.Path) -ForegroundColor Yellow }
  try { [void]$p.CloseMainWindow(); if (-not $p.WaitForExit(4000)) { $p.Kill() } } catch {}
}

# --- 4. Compilar a la salida aislada ----------------------------------------
Write-Host "Compilando Debug -> $salidaAbs ..." -ForegroundColor Cyan
dotnet build $proyecto -c Debug -o $salidaAbs --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw "La compilacion fallo (codigo $LASTEXITCODE)." }

if ($SoloCompilar) { Write-Host "Compilado. No se lanza nada (-SoloCompilar)." -ForegroundColor Yellow; return }

# --- 4. Datos aislados ------------------------------------------------------
# Los nombres "roaming" y "local" NO son decorativos: son exactamente los que compone UserPaths a
# partir de U_DATA_DIR. Cambiarlos aqui deja la semilla en un sitio donde la app no la busca.
$appdata = Join-Path $Datos "roaming"
$local   = Join-Path $Datos "local"
New-Item -ItemType Directory -Force -Path (Join-Path $appdata "U"), (Join-Path $local "U") | Out-Null

# La API key: sin ella la app de desarrollo no habla con Graph. Un build local sale con la key
# embebida VACIA (solo el CI la inyecta), asi que se hereda de la instalacion real una sola vez.
$graphJsonDev = Join-Path $appdata "U\graph.json"
if (-not (Test-Path $graphJsonDev)) {
  $graphJsonReal = Join-Path $env:APPDATA "U\graph.json"
  if (Test-Path $graphJsonReal) { Copy-Item $graphJsonReal $graphJsonDev }
  else { Write-Warning "Sin graph.json real que heredar. Pon GRAPH_API_KEY en el entorno o la app saldra sin credencial." }
}

# Identidad propia: que la telemetria distinga las dos apps.
$configDev = Join-Path $appdata "U\config.json"
if (-not (Test-Path $configDev)) {
  $base = Join-Path $env:APPDATA "U\config.json"
  $c = if (Test-Path $base) { Get-Content $base -Raw | ConvertFrom-Json } else { [pscustomobject]@{} }
  $c.DisplayName = "DEV (paralelo)"
  $c.Email       = "dev-paralelo@itsmiracleai.com"
  $c.UserId      = "dev-paralelo@itsmiracleai.com"
  $c.InstallId   = "dev0000000000000000000000000dev1"
  $c | ConvertTo-Json | Set-Content $configDev -Encoding utf8
}

# --- 5. Lanzar con entorno propio -------------------------------------------
# ProcessStartInfo y no Start-Process: hay que fijar variables SOLO para el hijo. Cambiar
# $env:APPDATA en esta consola contaminaria todo lo demas que se lance desde aqui.
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName        = Join-Path $salidaAbs "U.exe"
$psi.WorkingDirectory = $salidaAbs
$psi.UseShellExecute = $false
$psi.EnvironmentVariables["U_DATA_DIR"] = $Datos
# La instancia de desarrollo arranca con el explorador del grafo YA abierto, para que una prueba
# automatica pueda lanzar un mapeo nada mas recompilar, sin depender de encontrar y pulsar el boton
# de la carita por UIA (que no siempre se expone). En la app del usuario esta variable no existe.
$psi.EnvironmentVariables["U_AUTO_EXPLORER"] = "1"
# Sonda MCP local (127.0.0.1:8791/mcp): deja llamar a las herramientas del mapa desde fuera para
# comprobar que el terreno es NAVEGABLE, por el mismo camino que las usa el asistente.
$psi.EnvironmentVariables["U_MCP_PROBE"] = "1"
if ($env:GRAPH_API_KEY) { $psi.EnvironmentVariables["GRAPH_API_KEY"] = $env:GRAPH_API_KEY }

# La clave de la voz (Gemini Live) se lee del ENTORNO, y por eso hay que ir a buscarla al nivel de
# USUARIO en vez de heredarla: una consola abierta ANTES del `setx` no la tiene, así que la app
# arrancaba diciendo que no hay API key aunque estuviera puesta desde hacía rato (2026-08-05). El
# valor de la sesión manda si existe, para poder probar otra clave sin tocar la del usuario.
$gemini = $env:GEMINI_API_KEY
if (-not $gemini) { $gemini = [Environment]::GetEnvironmentVariable("GEMINI_API_KEY", "User") }
if ($gemini) {
  $psi.EnvironmentVariables["GEMINI_API_KEY"] = $gemini
  Write-Host ("Clave de voz (GEMINI_API_KEY): presente, {0} caracteres" -f $gemini.Length) -ForegroundColor DarkGray
} else {
  Write-Warning "Sin GEMINI_API_KEY: la voz en vivo no arrancara. Ponla con: setx GEMINI_API_KEY <clave>"
}

# Las DOS bases hacen falta: U_BACKEND_URL manda en el asistente (Config.cs) y GRAPH_BASE_URL en el
# modulo de workflows (GraphConfig.cs). Apuntar solo una deja la mitad del trafico en el Graph
# remoto, que es justo lo que hace imposible depurar.
if ($Backend) {
  $psi.EnvironmentVariables["U_BACKEND_URL"]  = $Backend
  $psi.EnvironmentVariables["GRAPH_BASE_URL"] = $Backend
  Write-Host "Backend de desarrollo: $Backend" -ForegroundColor Cyan
} else {
  Write-Warning "Sin -Backend: la app de DESARROLLO habla con el Graph DESPLEGADO, el mismo que la estable."
  Write-Warning "Los workflows viven en Neo4j y son COMPARTIDOS: no borres ni reescribas workflows desde aqui."
}

$p = [System.Diagnostics.Process]::Start($psi)
Write-Host ("App de DESARROLLO lanzada: PID {0} desde {1}" -f $p.Id, $psi.FileName) -ForegroundColor Green
Write-Host ("  datos aislados en {0}" -f $Datos) -ForegroundColor DarkGray
Write-Host ("  logs en {0}\U\logs" -f $local) -ForegroundColor DarkGray
