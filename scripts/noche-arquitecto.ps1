# CINCO CORRIDAS DEL ARQUITECTO, cada una respondiendo una pregunta distinta.
#
# No son cinco repeticiones: repetir lo mismo cinco veces mide la varianza del modelo, que ya
# sabemos que existe. Lo que hace falta saber es si el SISTEMA sostiene lo que el agente construye.
#
#   1. explorer, TODO a cero (terreno + enseñanzas)  → linea base: ¿cuanto ordena partiendo de nada?
#   2. explorer, sin tocar nada                      → ¿CONVERGE? la lista deberia bajar, no repetirse
#   3. explorer, terreno a cero pero CON enseñanzas  → ¿la plata sobrevive al borrado y se repone sola?
#   4. github.com, todo a cero                       → ¿funciona la via web, con landmarks?
#   5. github.com, sin tocar nada                    → ¿converge tambien en web?
#
# Entre corrida y corrida se anota el estado del grafo, que es lo que despues se lee para juzgar.

$ErrorActionPreference = 'Continue'
$repo   = Split-Path -Parent $PSScriptRoot
$agente = Join-Path $repo "agente-arquitecto\arquitecto.mjs"
$datos  = "C:\U-dev2"
$diario = "C:\U-versiones\noche-arquitecto.md"

function Sonda($tool, $args) {
  $body = @{ tool = $tool; args = $args } | ConvertTo-Json -Compress
  try { Invoke-RestMethod -Uri "http://127.0.0.1:8791/mcp/" -Method Post `
        -Body ([Text.Encoding]::UTF8.GetBytes($body)) -ContentType "application/json; charset=utf-8" -TimeoutSec 60 }
  catch { "ERROR: $_" }
}

function Apunta($txt) { Add-Content -Path $diario -Value $txt -Encoding utf8; Write-Host $txt }

function Arranca-App {
  Get-Process U -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "C:\U-versiones\v1\*" } |
    ForEach-Object { try { $_.Kill() } catch {} }
  Start-Sleep -Seconds 3
  $psi = New-Object System.Diagnostics.ProcessStartInfo
  $psi.FileName = "C:\U-versiones\v1\bin\U.exe"; $psi.WorkingDirectory = "C:\U-versiones\v1\bin"
  $psi.UseShellExecute = $false
  $psi.EnvironmentVariables["U_DATA_DIR"] = $datos
  $psi.EnvironmentVariables["U_MCP_PROBE"] = "1"
  $psi.EnvironmentVariables["U_AUTO_EXPLORER"] = "1"
  $g = [Environment]::GetEnvironmentVariable("GEMINI_API_KEY","User")
  if ($g) { $psi.EnvironmentVariables["GEMINI_API_KEY"] = $g }
  [void][System.Diagnostics.Process]::Start($psi)
  # Se espera a que la SONDA conteste, no un rato fijo: lo que hace falta es que este lista.
  for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Milliseconds 500
    try { Invoke-RestMethod -Uri "http://127.0.0.1:8791/mcp/" -Method Post `
          -Body ([Text.Encoding]::UTF8.GetBytes('{"tool":"map_where_am_i","args":{}}')) `
          -ContentType "application/json" -TimeoutSec 5 | Out-Null; return $true } catch {}
  }
  return $false
}

function Limpia-Terreno { Move-Item "$datos\local\U\surface-map.json" `
  "$datos\local\U\grafos-archivados-noche-$(Get-Date -Format HHmmss).json" -Force -ErrorAction SilentlyContinue }

function Olvida-Ensenanzas($app) {
  $f = "$datos\local\U\jerarquias-ensenadas.json"
  if (-not (Test-Path $f)) { return }
  $j = Get-Content $f -Raw | ConvertFrom-Json
  if ($j.PSObject.Properties.Name -contains $app) { $j.PSObject.Properties.Remove($app) }
  $j | ConvertTo-Json -Depth 6 -Compress | Set-Content $f -Encoding utf8
}

function Delante($app) {
  if ($app -like "*.exe") {
    Add-Type @"
using System; using System.Runtime.InteropServices; using System.Text;
public class Fg {
 public delegate bool Cb(IntPtr h, IntPtr l);
 [DllImport("user32.dll")] public static extern bool EnumWindows(Cb cb, IntPtr l);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
 [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool f);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
 [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
 public static IntPtr Carpeta() { IntPtr r = IntPtr.Zero;
   EnumWindows((h,l) => { if (!IsWindowVisible(h)) return true;
     var c = new StringBuilder(200); GetClassName(h,c,200);
     if (c.ToString()=="CabinetWClass") { r=h; return false; } return true; }, IntPtr.Zero);
   return r; }
 public static bool Forzar(IntPtr h) { uint p;
   uint t = GetWindowThreadProcessId(GetForegroundWindow(), out p);
   uint m = GetCurrentThreadId(); AttachThreadInput(m,t,true);
   ShowWindow(h,9); bool ok = SetForegroundWindow(h); AttachThreadInput(m,t,false); return ok; } }
"@ -ErrorAction SilentlyContinue
    $h = [Fg]::Carpeta()
    if ($h -eq [IntPtr]::Zero) { Start-Process explorer.exe; Start-Sleep 3; $h = [Fg]::Carpeta() }
    if ($h -ne [IntPtr]::Zero) { [void][Fg]::Forzar($h) }
  } else {
    # Una web se alcanza por su pestaña: lo resuelve quien ya sabe (PestanasAbiertas), via sonda.
    Sonda "map_open_app" @{ app = "chrome" } | Out-Null
    Start-Sleep -Seconds 2
  }
  Start-Sleep -Seconds 3
}

function Estado($app) {
  $j = "" + (Sonda "map_hierarchy" @{ app = $app })
  $s = "" + (Sonda "map_unsituated" @{ app = $app })
  $pant = ([regex]::Match($j, 'PANTALLAS \((\d+)\)')).Groups[1].Value
  $dec  = ([regex]::Match($j, 'SALIDAS CON NIVEL DECLARADO \((\d+)\)')).Groups[1].Value
  $pend = if ($s -match 'ENTERA situada') { "0" } else { ([regex]::Match($s, 'SALIDAS sin nivel \((\d+)\)')).Groups[1].Value }
  "pantallas=$pant declaradas=$dec pendientes=$pend"
}

# ── Las cinco ────────────────────────────────────────────────────────────────
$corridas = @(
  @{ n=1; app="explorer.exe"; limpiar=$true;  olvidar=$true;  pregunta="LINEA BASE — partiendo de cero absoluto" },
  @{ n=2; app="explorer.exe"; limpiar=$false; olvidar=$false; pregunta="CONVERGENCIA — la lista debe BAJAR, no repetirse" },
  @{ n=3; app="explorer.exe"; limpiar=$true;  olvidar=$false; pregunta="PERSISTENCIA — la plata debe reponerse sola sobre terreno nuevo" },
  @{ n=4; app="github.com";   limpiar=$true;  olvidar=$true;  pregunta="VIA WEB — con landmarks, partiendo de cero" },
  @{ n=5; app="github.com";   limpiar=$false; olvidar=$false; pregunta="CONVERGENCIA WEB" }
)

Apunta "`n# Noche del arquitecto · $(Get-Date -Format 'yyyy-MM-dd HH:mm')`n"

foreach ($c in $corridas) {
  Apunta "`n## Corrida $($c.n) · $($c.app) · $($c.pregunta)"
  Get-Process U -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "C:\U-versiones\v1\*" } |
    ForEach-Object { try { $_.Kill() } catch {} }
  Start-Sleep -Seconds 2
  if ($c.limpiar) { Limpia-Terreno; Apunta "- terreno a cero" }
  if ($c.olvidar) { Olvida-Ensenanzas $c.app; Apunta "- enseñanzas de $($c.app) olvidadas" }

  if (-not (Arranca-App)) { Apunta "- FALLO: la sonda no levanto"; continue }
  Delante $c.app
  $antes = Estado $c.app
  Apunta "- ANTES:  $antes"

  $t0 = Get-Date
  $salida = & node $agente $c.app 160 2>&1 | Out-String
  $dur = [int]((Get-Date) - $t0).TotalSeconds

  $turnos = ([regex]::Match($salida, 'turnos: (\d+)')).Groups[1].Value
  $fin = if ($salida -match 'CORRIDA COMPLETA') { "completa" }
         elseif ($salida -match 'SE ACABARON LOS TURNOS') { "sin turnos" } else { "cortada" }
  $despues = Estado $c.app
  Apunta "- DESPUES: $despues"
  Apunta "- corrida: $fin · $turnos turnos · $dur s"
  $salida | Out-File "C:\U-versiones\noche-corrida-$($c.n).log" -Encoding utf8
}

Apunta "`n## Fin · $(Get-Date -Format 'HH:mm')"
