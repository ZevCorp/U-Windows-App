# EN QUE ESTADO ESTA LA VOZ DE U, LEIDO DEL LOG. Lo usa conducir.ps1 para decidir si pulsa Ctrl+Alt+M y
# cuando da la voz por abierta; lo juzga autoprueba-apertura.ps1 con trozos de log de verdad.
#
# POR QUE (2026-09-13). En el nivel 4 del 2026-09-12 el conductor esperaba "sesion abierta" y nada mas.
# Esa linea se escribia al CONECTAR el socket, antes de que el servidor dijera nada: con la cuenta sin
# credito salio en el mismo segundo que "el servidor dice: You have no credits remaining", y el conductor
# la tomo por voz abierta y tecleo tareas a una voz que no existia (patron n.2). Desde la promesa 220 del
# grafo, U escribe al conectar "socket conectado, esperando confirmacion de ..." y "sesion abierta con
# ...: el servidor la confirmo" solo al confirmarla. La linea VIEJA -la de main y la de cualquier binario
# anterior a la 220- se sigue aceptando para poder medir main, pero como "sin-confirmar": el conductor
# la da por buena solo si no la sigue un error.
#
# ASCII PURO: PowerShell 5.1 lee un .ps1 sin BOM como ANSI. Los acentos del log se casan con "." en las
# expresiones regulares.
#
# Estados, del ultimo suceso de la voz que diga algo sobre la apertura:
#   nada           ninguna linea de apertura: la voz no se ha tocado
#   esperando      el socket conecto y el servidor aun no confirma (NO pulsar Ctrl+Alt+M: la cerraria)
#   confirmada     el servidor confirmo la sesion
#   sin-confirmar  la linea vieja, o un protocolo que no confirma: abierta segun U, sin que el servidor lo diga
#   fallando       un error o un corte antes de confirmar: no va a abrir
#   cerrada        la voz se cerro, o no llego a abrir y se dejo
function EstadoDeLaVoz([string]$texto) {
  $estado = "nada"
  foreach ($l in ($texto -split "`r?`n")) {
    if ($l -notmatch 'voz-viva: ') { continue }
    if ($l -match 'voz-viva: socket conectado, esperando confirmaci') { $estado = "esperando" }
    elseif ($l -match 'voz-viva: sesi.n abierta con .*: el servidor la confirm') { $estado = "confirmada" }
    elseif ($l -match 'voz-viva: sesi.n abierta con ') { $estado = "sin-confirmar" }
    elseif ($l -match 'voz-viva: (la sesi.n no lleg. a abrir|no se pudo abrir la sesi.n|la conexi.n se cay. \d+ veces seguidas|sesi.n cerrada)') { $estado = "cerrada" }
    elseif ($l -match 'voz-viva: (el servidor dice: |el servidor cerr. la conexi.n|se cort. la escucha|reconectada SIN continuidad|no pude reconectar)') {
      # Antes de confirmar, un error o un corte es que no abre. Despues de confirmar es de la conversacion.
      if ($estado -eq "esperando" -or $estado -eq "sin-confirmar") { $estado = "fallando" }
    }
  }
  return $estado
}
