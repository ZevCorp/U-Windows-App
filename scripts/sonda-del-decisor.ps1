# LA SONDA DEL DECISOR: ensena el interruptor funcionando, sin SAP y sin clave.
#
#   .\scripts\sonda-del-decisor.ps1              # luna, jev-sin-clave y simulado, todo en seco
#   .\scripts\sonda-del-decisor.ps1 -DeVerdad     # ademas llama a TypeSafe, SOLO si hay clave
#
# POR QUE EXISTE. El aprendizaje n13 de este repo: preguntale a la API antes de creerle al codigo.
# Una sonda de solo lectura contesto en veinte minutos tres preguntas que llevaban semanas resueltas
# "por deduccion". Esta hace lo mismo con el decisor: se ve que Luna no llama a nadie, que sin clave
# no se activa Jev, y que el modo simulado recorre la cadena entera sin red.
#
# NO LLAMA A TYPESAFE SALVO QUE SE LO PIDAS Y HAYA CLAVE. Sin -DeVerdad no sale ni un paquete.

param([switch]$DeVerdad)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$bin  = Join-Path $env:TEMP "u-sonda-decisor"

Write-Host "compilando el nucleo..." -ForegroundColor Cyan
dotnet build (Join-Path $repo "windows-client\WindowsClient.csproj") -c Release -o $bin --nologo -v quiet -nodeReuse:false
if ($LASTEXITCODE -ne 0) { throw "no compila (codigo $LASTEXITCODE)" }

Add-Type -Path (Join-Path $bin "U.WindowsClient.dll")

$asm      = [System.Reflection.Assembly]::LoadFrom((Join-Path $bin "U.WindowsClient.dll"))
$tCfg     = $asm.GetType("U.WindowsClient.Decision.ConfiguracionDelDecisor")
$tDecisor = $asm.GetType("U.WindowsClient.Decision.ElDecisor")
$tPeticion= $asm.GetType("U.WindowsClient.Decision.PeticionASystemOne")

# Las puertas son las del fallo real que cuenta CLAUDE.md: el puente pulso "Buscar pacientes"
# en vez de "Crear Triage Administrativo".
$puertas  = [string[]]@("Crear Triage Administrativo", "Buscar pacientes", "Salir")
$objetivo = "crear el triage administrativo del paciente"
$pantalla = "SAP/NWP1"

function Muestra($titulo, $cfg, $decision, $llamadas) {
  Write-Host ""
  Write-Host "  $titulo" -ForegroundColor Yellow
  if ($cfg) { Write-Host "    quien decide : $($cfg.Quien)"; Write-Host "    porque       : $($cfg.Porque)" }
  if ($decision -ne $null) {
    $color = if ($decision.Actuar) { "Green" } else { "DarkGray" }
    Write-Host "    actuar       : $($decision.Actuar)" -ForegroundColor $color
    if ($decision.Puerta) { Write-Host "    puerta       : $($decision.Puerta)" }
    Write-Host "    confianza    : $($decision.Confianza)"
    Write-Host "    porque       : $($decision.Porque)"
  }
  if ($llamadas -ne $null) { Write-Host "    llamadas a TypeSafe: $llamadas" -ForegroundColor Magenta }
}

# Un transporte que ANOTA. Lo unico que distingue "no se llamo" de "se llamo y se descarto".
$script:llamadas = 0
$contador = [Func[string,string]]{
  param($cuerpo)
  $script:llamadas++
  '{"model":"jev-1.13.0","answers":{"puerta":{"type":"choice","choice":"Crear Triage Administrativo","probabilities":{"Crear Triage Administrativo":0.93,"Buscar pacientes":0.05,"Salir":0.02},"confidence":0.91}},"usage":{"input_tokens":312,"output_tokens":0}}'
}

Write-Host ""
Write-Host "=== 1. SIN CONFIGURAR (lo que hay hoy en la maquina del hospital) ===" -ForegroundColor Cyan
$vacio = [Func[string,string]]{ param($n) $null }
$cfg = $tCfg::Leer($vacio)
$script:llamadas = 0
$d = $tDecisor::Elegir($cfg.Quien, $pantalla, $objetivo, $puertas, $cfg.Confianza, $contador)
Muestra "decide Luna, y a TypeSafe no se le llama" $cfg $d $script:llamadas

Write-Host ""
Write-Host "=== 2. U_DECISOR=jev PERO SIN CLAVE ===" -ForegroundColor Cyan
$sinClave = [Func[string,string]]{ param($n) if ($n -eq "U_DECISOR") { "jev" } else { $null } }
$cfg = $tCfg::Leer($sinClave)
$script:llamadas = 0
$d = $tDecisor::Elegir($cfg.Quien, $pantalla, $objetivo, $puertas, $cfg.Confianza, $contador)
Muestra "se queda en Luna y dice que falta la clave" $cfg $d $script:llamadas

Write-Host ""
Write-Host "=== 3. U_DECISOR=simulado (la cadena entera, sin red) ===" -ForegroundColor Cyan
$sim = [Func[string,string]]{ param($n) if ($n -eq "U_DECISOR") { "simulado" } else { $null } }
$cfg = $tCfg::Leer($sim)
$script:llamadas = 0
$d = $tDecisor::Elegir($cfg.Quien, $pantalla, $objetivo, $puertas, $cfg.Confianza, $contador)
Muestra "elige con la regla fija, sin llamar a nadie" $cfg $d $script:llamadas

Write-Host ""
Write-Host "=== 4. U_DECISOR=jev con clave, transporte de mentira ===" -ForegroundColor Cyan
$conClave = [Func[string,string]]{ param($n) switch ($n) { "U_DECISOR" { "jev" } "TYPESAFE_API_KEY" { "sk-de-mentira" } default { $null } } }
$cfg = $tCfg::Leer($conClave)
$script:llamadas = 0
$d = $tDecisor::Elegir($cfg.Quien, $pantalla, $objetivo, $puertas, $cfg.Confianza, $contador)
Muestra "aqui si se llama, y la puerta elegida se valida contra el inventario" $cfg $d $script:llamadas

Write-Host ""
Write-Host "=== 5. UNA PUERTA QUE NO ESTA EN PANTALLA (el pendiente n2) ===" -ForegroundColor Cyan
$inventa = [Func[string,string]]{
  param($cuerpo)
  '{"model":"jev-1.13.0","answers":{"puerta":{"type":"choice","choice":"Grabar","probabilities":{"Grabar":0.99},"confidence":0.99}},"usage":{}}'
}
$d = $tDecisor::Elegir("jev", $pantalla, $objetivo, $puertas, 0.7, $inventa)
Muestra "se rechaza, por segura que venga" $null $d $null

Write-Host ""
Write-Host "=== EL CUERPO QUE SE LE MANDARIA A TYPESAFE ===" -ForegroundColor Cyan
$estado = $tPeticion::EstadoDeLaPantalla($pantalla, $objetivo, $puertas)
$cuerpo = $tPeticion::CuerpoDeEleccion("jev-latest", $estado, "puerta", $tPeticion::InstruccionesDeLaPuerta($objetivo), $puertas)
Write-Host $cuerpo
if ($cuerpo -match "Bearer|TYPESAFE_API_KEY|api_key") { Write-Host "  !! LA CLAVE ESTA EN EL CUERPO" -ForegroundColor Red }
else { Write-Host "  la clave no aparece en el cuerpo (va en la cabecera)" -ForegroundColor Green }

if ($DeVerdad) {
  $clave = $env:TYPESAFE_API_KEY
  if ([string]::IsNullOrWhiteSpace($clave)) {
    Write-Host ""
    Write-Host "=== -DeVerdad PEDIDO, PERO NO HAY TYPESAFE_API_KEY: no se llama a nadie. ===" -ForegroundColor Red
  } else {
    Write-Host ""
    Write-Host "=== 6. LLAMADA REAL A TYPESAFE ===" -ForegroundColor Cyan
    $tCliente = $asm.GetType("U.WindowsClient.Decision.ClienteTypeSafe")
    $cliente = [Activator]::CreateInstance($tCliente, @($clave, 5000, $null))
    try {
      $reloj = [Diagnostics.Stopwatch]::StartNew()
      $transporte = [Func[string,string]]{ param($c) $cliente.Pregunta($c) }
      $d = $tDecisor::Elegir("jev", $pantalla, $objetivo, $puertas, 0.7, $transporte)
      $reloj.Stop()
      Muestra "contesto en $($reloj.ElapsedMilliseconds) ms" $null $d $null
    } finally { $cliente.Dispose() }
  }
}

Write-Host ""
Write-Host "listo." -ForegroundColor Green
