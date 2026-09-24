# Sin Bluetooth, el collar lo dice una vez y espera

Estado: **implementado** (2026-09-24) · Nace del log de una usuaria del 2026-09-23 · Rama: `jose/collar-sin-bluetooth`

> Spec 050 y promesa 411. Hasta la 049 y la 410 están tomadas en ramas abiertas.

## Diagnóstico: qué se midió

Telemetría de Supabase (`graph_windows_events`), usuaria Geraldine, equipo `SALA-DE-JUNTAS`, 2026-09-23:

| Qué | Medida |
|---|---|
| Fallo al abrir el collar | `COMException (0x800710DF): sin mensaje`, **12 veces en 42 s** (14:53:05 → 14:53:47) |
| Cadencia | cada ~6 s: es `CollarPermanente.ProgramarReintento`, que no se rinde mientras `Permanente` |
| Qué es 0x800710DF | `HRESULT_FROM_WIN32(4319)`, `ERROR_DEVICE_NOT_AVAILABLE`: lo que contesta WinRT al arrancar un rastreo BLE con la radio apagada o sin adaptador |
| Lo que vio la usuaria | nada: el estado decía «no se encontró el collar», que manda a buscar el collar y no a encender el Bluetooth |

**La causa:** el servicio del collar trata «no hay radio» igual que «el collar no está cerca».
Reintenta cada 6 s para siempre —y se recuerda entre arranques— contra algo que no va a cambiar
hasta que la persona encienda el Bluetooth, y el mensaje no dice que eso es lo que falta
(aprendizaje nº2: un mensaje que no distingue sus causas manda la investigación al sitio equivocado).

## La especificación

| # | Promesa | Fase |
|---|---|---|
| 411 | sin Bluetooth el collar lo dice una vez y deja de buscar: una radio apagada —o el fallo 0x800710DF, que es lo que Windows contesta cuando lo está— espera a que se encienda sin reintentar cada 6 s; un equipo sin adaptador no reintenta; el estado nombra cuál de las dos es y qué hacer; y el mismo estado repetido no vuelve al log | 1 |

| 412 | con el collar elegido y sin conectar, el menú del micrófono enseña por qué —el estado del collar— aunque no haya ningún collar enlazado; conectado, sin elegirlo, o con su tarjeta de enlazado a la vista, no se añade nada | 2 |

La 412 nace el 2026-09-24: el dueño eligió «Collar Omi» en su propia Ü con el Bluetooth apagado y
el menú no enseñó nada. La tarjeta «Dispositivos enlazados» solo aparece tras una primera
conexión (`collar.json` → `enlazado:false`), y el estado del collar vivía dentro de ella: sin
collar enlazado, el motivo no salía en ninguna parte.

Se juzga con `U.WindowsClient.Voice.ElBluetooth`, pura. La radio real (encendida, apagada, sin
adaptador) es de la máquina y se prueba a mano.

## Fase 1

| | |
|---|---|
| **Promesa** | 411 |
| **Qué toca** | `windows-client/src/Voice/ElBluetooth.cs` (nuevo), `CollarPermanente.cs`, `FuenteOmi.cs` |
| **Terminado** | 411 verde, el resto intactas |
| **Sitios con esta clase de error** | 1 bucle de reintento (`ProgramarReintento`); lo alimentan 3 llamadores de `ConectarAsync` (arranque, menú de la consulta, abrir la voz), y los tres pasan por la misma comprobación de radio |

## Lo que NO entra

- **El micrófono local de ese equipo** (`BadDeviceId calling waveInOpen` a las 14:52:57, y «el
  dictado estaba conectado pero no llegó ni una palabra»). Es el micrófono de Windows, no el collar.
- **Encender el Bluetooth por la persona.** Es configuración del sistema: se le dice, no se toca.

## Hallazgos

- **Sin emparejar, el collar se cae cada ~30 s, y NO es Windows (2026-09-24, medido).** Una sonda
  de solo lectura sobre el collar ya conectado: `emparejado=False`, `CanMaintainConnection=True`,
  `SessionStatus=Active`, `MaintainConnection=True` — y aun así `Disconnected` a los 30,4 s y a los
  64,4 s, con `SessionStatus → Closed (error Success)`: un cierre limpio desde el collar. Cerrar la
  app oficial de Omi no lo cambió (30,7-30,9 s). Tras emparejarlo en Configuración de Windows aguantó
  4 min 28 s seguidos. Cada caída costaba ~10 s de audio: la fuente se reconstruye desde el rastreo,
  y el micrófono local solo releva a los 30 s. **Queda fuera de esta spec**: emparejar desde la app
  y reenganchar sin rastrear son el corte siguiente.
- El collar puede guardar audio en su memoria durante una desconexión, pero `FuenteOmi` solo lee el
  audio en vivo y el botón: ese audio no se recupera hoy. `Reposicion` rellena los huecos con
  silencio para que el tiempo cuadre; no recupera lo dicho.

## Cierre

- [x] 411 y 412 verdes, contrato intacto; sabotaje comprobado en cuatro sitios (el fallo no concluye,
      apagada reintenta, el log repite, el menú calla): cada uno pone roja solo su promesa.
- [x] A mano: la radio de esta máquina leída con las mismas APIs → adaptador MediaTek, estado `Off`.
- [x] A mano, app completa con el Bluetooth apagado y el collar recordado: una sola línea
      `omi: el Bluetooth de este equipo está apagado: enciéndelo y el collar se conecta solo` y
      ninguna más en 50 s. Antes: `0x800710DF: sin mensaje` cada 6 s.
- [ ] Sin probar a mano: la 412 (elegir «Collar Omi» en el menú) — obligaba a buscar un collar que
      estaba en una consulta en otro PC. Juzgada por el contrato.
