# AUTOPRUEBA DE apertura.ps1: el conductor del nivel 4 decide si la voz abrio leyendo el log de U, y
# esa decision se juzga aqui con trozos de log de verdad, sin U.exe, sin clave y sin pantalla.
#
#   powershell -ExecutionPolicy Bypass -File scripts\nivel4-voz\autoprueba-apertura.ps1
#
# POR QUE EXISTE (2026-09-13). En el nivel 4 del 2026-09-12 la linea "sesion abierta con" salio en el
# mismo segundo que "el servidor dice: You have no credits remaining", y conducir.ps1 la tomo por voz
# abierta: siguio tecleando tareas a una voz que no existia. Desde la promesa 220 del grafo la linea
# solo sale cuando el servidor confirma; esto juzga que el conductor la espera, que no pulsa Ctrl+Alt+M
# (que alterna, y la cerraria) mientras la voz se esta abriendo, y que la linea VIEJA -la de main, que
# sale antes de confirmar- se acepta solo si no la sigue un error.
#
# ASCII PURO, como conducir.ps1: los acentos y las comillas latinas del log se construyen por codigo.
$ErrorActionPreference = 'Stop'
$helper = Join-Path $PSScriptRoot "apertura.ps1"
if (-not (Test-Path $helper)) {
  Write-Host "PENDIENTE: apertura.ps1 todavia no existe (EstadoDeLaVoz)."
  Write-Host "APERTURA ROTA: la autoprueba no tiene que juzgar."
  exit 1
}
. $helper
if (-not (Get-Command EstadoDeLaVoz -ErrorAction SilentlyContinue)) {
  Write-Host "PENDIENTE: apertura.ps1 no define EstadoDeLaVoz."
  Write-Host "APERTURA ROTA: la autoprueba no tiene que juzgar."
  exit 1
}

$o = [char]0xF3; $a = [char]0xE1; $i_ = [char]0xED; $ab = [char]0xAB; $cb = [char]0xBB
function V([string]$hora, [string]$texto) { return "[$hora] voz-viva: $texto" }
$vieja     = V "23:36:53" "sesi${o}n abierta con ${ab}gpt-live-1${cb} (OpenAI GPT-Live)"
$viejaRt   = V "23:40:19" "sesi${o}n abierta con ${ab}gpt-realtime-2.1-mini${cb} (OpenAI)"
$micro     = V "23:36:53" "micr${o}fono abierto a 24000 Hz"
$sinCred   = V "23:36:53" "el servidor dice: You have no credits remaining. Add credits to continue using the API at https://platform.openai.com/settings/organization/billing/."
$corte     = V "23:36:55" "se cort${o} la escucha: The remote party closed the WebSocket connection without completing the close handshake."
$noAbrio   = V "23:36:55" "la sesi${o}n no lleg${o} a abrir: el servidor contest${o} ${ab}You have no credits remaining.${cb} en vez de confirmarla, y cerr${o}. No se reintenta: la misma apertura fallar${i_}a igual"
$cerrada   = V "23:36:55" "sesi${o}n cerrada"
$cerro1013 = V "23:40:19" "el servidor cerr${o} la conexi${o}n: 1013 ${ab}insufficient_quota.credit_balance_exhausted${cb}"
$reconecta = V "23:40:20" "reconectada SIN continuidad: la conversaci${o}n empieza de cero (intento 1)"
$sedeja    = V "23:40:24" "la conexi${o}n se cay${o} 5 veces seguidas: se deja"
$esperando = V "10:48:01" "socket conectado, esperando confirmaci${o}n de ${ab}gpt-live-1${cb} (OpenAI GPT-Live)"
$confirmo  = V "10:48:02" "sesi${o}n abierta con ${ab}gpt-live-1${cb} (OpenAI GPT-Live): el servidor la confirm${o}"
$sinConf   = V "10:48:01" "sesi${o}n abierta con ${ab}ninguno${cb} (de mentira), sin confirmaci${o}n: este protocolo no la manda"
$cuenta    = "[10:47:50] cuenta: cuenta creada y sesi${o}n abierta " + [char]0x00B7 + " u_123"
$oyeDecir  = V "10:48:05" "el servidor dice: Invalid value: 'voz_que_no_existe'."

$casos = @(
  @{ n = "log vacio: la voz no se ha tocado";                                              l = @();                                              e = "nada" },
  @{ n = "otra 'sesion abierta' que no es de la voz (la cuenta)";                         l = @($cuenta);                                       e = "nada" },
  @{ n = "rama nueva: socket conectado, sin confirmar todavia";                            l = @($esperando);                                    e = "esperando" },
  @{ n = "rama nueva: el servidor confirma";                                               l = @($esperando, $confirmo);                         e = "confirmada" },
  @{ n = "rama nueva: un error antes de confirmar es que no abre";                         l = @($esperando, $micro, $sinCred);                  e = "fallando" },
  @{ n = "rama nueva: no abrio y se cerro";                                                l = @($esperando, $sinCred, $corte, $noAbrio, $cerrada); e = "cerrada" },
  @{ n = "rama nueva: un error DESPUES de confirmar no deshace la apertura";               l = @($esperando, $confirmo, $oyeDecir);              e = "confirmada" },
  @{ n = "rama nueva: se corta, reconecta y espera otra confirmacion";                     l = @($esperando, $confirmo, $cerro1013, $reconecta, $esperando); e = "esperando" },
  @{ n = "rama nueva: protocolo que no confirma";                                          l = @($sinConf);                                      e = "sin-confirmar" },
  @{ n = "main (linea vieja) sin nada detras: se acepta, sin confirmar";                   l = @($viejaRt);                                      e = "sin-confirmar" },
  @{ n = "9324d97 sin credito (el nivel 4 del 12): la linea vieja y el error en el mismo segundo"; l = @($vieja, $micro, $sinCred);            e = "fallando" },
  @{ n = "Realtime sin credito (el nivel 4 del 12): la linea vieja y el cierre 1013";      l = @($viejaRt, $cerro1013, $reconecta);              e = "fallando" },
  @{ n = "Realtime sin credito, al final: se deja";                                        l = @($viejaRt, $cerro1013, $reconecta, $sedeja);     e = "cerrada" },
  @{ n = "cerrada y vuelta a abrir con la linea vieja";                                    l = @($vieja, $sinCred, $noAbrio, $cerrada, $viejaRt); e = "sin-confirmar" }
)

$rotas = 0
foreach ($c in $casos) {
  $texto = ($c.l -join "`r`n") + $(if ($c.l.Count -gt 0) { "`r`n" } else { "" })
  $sale = EstadoDeLaVoz $texto
  if ($sale -eq $c.e) { Write-Host ("  OK   " + $c.n + " -> " + $sale) }
  else { $rotas++; Write-Host ("  MAL  " + $c.n + ": se esperaba '" + $c.e + "' y salio '" + $sale + "'") }
}
Write-Host ""
if ($rotas -eq 0) { Write-Host ("APERTURA INTACTA: " + $casos.Count + " de " + $casos.Count + " casos.") }
else { Write-Host ("APERTURA ROTA: " + $rotas + " de " + $casos.Count + " casos.") }
exit $rotas
