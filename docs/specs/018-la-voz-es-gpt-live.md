# Plan de implementación: la voz es GPT-Live, y lo que se escribe se contesta

Estado: **en construcción**. A 2026-09-12: medido contra el servidor; el juez del nivel 4 arreglado y saboteado; las promesas 40–43, 208–210 y 214 **en verde y saboteadas**, y la 208 juzga por fin su cableado. **Falta el nivel 4**: nada de esto se ha oído con micrófono ni con `U.exe` · Nace de dos peticiones del dueño (2026-09-11 y 2026-09-12) y de las sondas contra el servidor de esas dos noches · Rama: `jero/voz-gpt-live`, que integra `jero/voz-gpt-live-protocolo` (el traductor, 40–43) y `jero/voz-gpt-live-juez` (esta spec y el juez del nivel 4)

> **La petición, en la voz del dueño.** El 2026-09-11: *«cambia a GPT voice el último que salió…
> investiga»*. El 2026-09-12: *«ajusta todo y empuja a main»*, *«migra rápido»*.

## Qué es GPT-Live-1, y por qué no es un cambio de una línea

`gpt-live-1` es el modelo de voz de OpenAI posterior a `gpt-realtime`. La clave de esta máquina ve
`gpt-live-1` y `gpt-live-transcribe`; **no existe `gpt-live-1-mini`**. La tentación es cambiar el
`Modelo` de `ProtocoloOpenAI` y dar la migración por hecha. No se puede, y ninguna de las razones da
error; todas se manifiestan como «Ü no contesta» o «Ü contesta, pero la app no se entera»:

1. **Otro socket.** `gpt-live-1` por `wss://api.openai.com/v1/realtime` devuelve *«not supported in
   realtime mode»*. Vive en `wss://api.openai.com/v1/live/sessions`, sin `?model=`.
2. **Dos cerebros.** La voz **no acepta herramientas**. Las herramientas van en una *delegación*
   (`delegation.responses.tools`) y las llama un segundo modelo, el delegado (`gpt-5.6-luna`), por la
   Responses API. Sus eventos llegan envueltos en `response.event`.
3. **Otro vocabulario para todo.** Micrófono, audio de vuelta, transcripciones, resultados, pedir
   turno y dictar tienen nombres distintos. Un traductor que mande lo de Realtime a este socket no
   recibe nada útil: la sesión queda abierta y muda, que es el fallo que `IProtocolo` existe para
   impedir.
4. **No hay marcas de turno.** Ni `speech_started` ni `response.done`. `ConversacionEnVivo` cuelga
   de esas dos marcas el cierre del turno (`TurnoCerrado`, `Cerro`, `DijoElUsuario`, la línea
   «Ü dijo»), la medida del turno de la promesa 205 y el callar cuando le hablan encima.
5. **No hay modo silencio.** No hay `turn_detection` ni `create_response:false`: la voz contesta sola
   siempre. La voz prestada de la promesa 192 no se puede hacer con GPT-Live.
6. **La sesión es inmutable salvo la delegación.** Cambiar de modo a mitad de sesión (promesas 138 y
   192) ya no es reenviar la apertura.
7. **El consumo se mide en segundos**, no en fichas.

Y un fallo que **ya estaba en `main`** con Realtime, y que esta spec arregla de paso porque invalidó
el nivel 4 de la spec 017: **el texto escrito no pide respuesta**. `EnviarTextoAsync` manda el
mensaje y nada más; sin un `response.create` detrás, el modelo no contesta (cero eventos, medido).
Solo contestaba cuando el micrófono oía algo.

## Diagnóstico: qué se midió

Todo contra el servidor real, con la clave de esta máquina, el 2026-09-11 y el 2026-09-12. Las
sondas son PowerShell de un solo uso (`sonda-live.ps1`, `sonda-live-eventos.ps1`, `sonda-voz.ps1`,
`sonda-rt-audio.ps1` y una frase hablada de 2,4 s a 24 kHz) y **no están en el repo**.

| Qué | Medida | Fuente |
|---|---|---|
| Modelos de voz que ve la clave | `gpt-live-1` y `gpt-live-transcribe`; `gpt-live-1-mini` no existe | sonda, 09-11 |
| `gpt-live-1` por `/v1/realtime` | error *«not supported in realtime mode»* | sonda, 09-11 |
| Dónde abre | `wss://api.openai.com/v1/live/sessions`, sin `?model=`, cabecera `Authorization: Bearer` | `sonda-live.ps1` |
| Primer mensaje | un `session.start` con `model`, `instructions`, `audio.format` (`audio/pcm`, 24000), `audio.output.voice` (`marin`) y `delegation` → contesta `session.started` | ídem |
| Herramientas | la voz no las acepta: van en `delegation.responses.tools` (tipo `function`) con `tool_choice: auto`, y las llama el delegado (`gpt-5.6-luna`; `gpt-5.6-terra` también funciona) | ídem |
| Micrófono | `session.input_audio.append` con PCM16 mono 24 kHz en base64 | ídem |
| Audio de vuelta | `session.output_audio.delta` (campo `delta`), **continuo, también silencio** | `sonda-live-eventos.ps1` |
| Lo que dice Ü / el usuario | `session.output_transcript.delta` / `session.input_transcript.delta` (campo `delta`) | ídem |
| Otros eventos vistos | `session.delegation.created`, `response.event` (envuelve `response.created`, `in_progress`, `output_item.added/done`, `function_call_arguments.delta/done`, `output_text.*`, `content_part.*`, `completed`), `session.instructions/thinking/commentary.appended`, `session.usage.updated`, `session.closed {reason}`, `error` | ídem |
| Una llamada a herramienta | `response.event` con `event.type == response.output_item.done` y `event.item.type == function_call`: `call_id`, `name`, `arguments` (texto JSON) | ídem |
| Devolver el resultado | `response.item.create` con `function_call_output` y después `response.create`. Del resultado a la primera voz: **9–72 ms** | ídem |
| Texto escrito | `response.item.create` (message · user · `input_text`) + `response.create` → delega, llama la herramienta y habla. `session.thinking.append` y `session.instructions.append` **no** provocan respuesta | ídem |
| Dictar | `session.commentary.append {delegation_id: null, content: "Di exactamente esto, sin añadir nada: X"}` → dijo «X» literal | ídem |
| Fotos | `input_text` + `input_image` (data URL JPEG) + `response.create` → el delegado **sí la ve** (dijo el color y el texto de la imagen). Una foto sola sin `response.create`: aceptada en silencio, sin error. **Pero no caben** (revisión de fidelidad): un `Fotograma` de 117.962 B (JPEG 1024×576 a calidad 60; la captura real de esta máquina pesa 67–69 KB, 90–92 KB en base64) da `response_input_buffer_full` y el delegado contesta como si la viera borrosa; una de 520 px (32.146 B) se lee; de tres de 400 px (20.208 B) en una misma sesión, solo la 1ª | ídem; `fid\run-look*.txt`, 09-12 |
| Modo silencio | **no existe**: `session.instructions.append` «no hables por tu cuenta» no se respeta, la voz contestó sola; no hay `turn_detection` ni `create_response` | ídem |
| Marcas de turno | **ninguna**: ni `speech_started` ni `response.done`. Hablarle encima no produce ningún evento. `response.completed` cierra el trabajo del **delegado**, pero la voz sigue hablando segundos después; un turno solo de voz, sin delegar, no tiene ningún cierre | ídem |
| Consumo | `session.usage.updated {usage: {seconds}}`: duración, **no fichas** | ídem |
| Latencia, frase de 2,4 s, desde su fin | GPT-Live + luna: llama la herramienta a **1,0–1,4 s**, la voz empieza a **0,1–0,5 s**. `gpt-realtime-2.1-mini`: llama a **0,4–1,2 s**, la voz empieza a **0,4–1,1 s**, resultado→voz **0,46–0,61 s** | `sonda-rt-audio.ps1`, `sonda-live-eventos.ps1` |
| Realtime hoy, texto sin `response.create` | **cero eventos**: el modelo no contesta | `sonda-voz.ps1` |
| Transcripción en una sesión Realtime | `gpt-transcribe` y `gpt-live-transcribe` aceptados (`session.updated` sin error). `gpt-4o-mini-transcribe`, el de hoy, está deprecado y se apaga el 2027-02-26 | `sonda-voz.ps1` |
| `session.update` de la delegación a mitad de sesión | `{"type":"session.update","session":{"delegation":{"type":"responses","responses":{model, instructions, tools, tool_choice}}}}` → `session.updated` a ~150 ms, y en la frase siguiente el delegado llama la herramienta nueva. Las herramientas se **reemplazan**; dentro de `responses` cada campo se fusiona. Funciona con una herramienta pendiente. Un segundo `session.start` da `error` y la sesión sigue igual | sonda de la fase 1 y sonda de huecos, 09-12 |
| Un resultado de herramienta grande | un `function_call_output` de 40 KB (mensaje de 41.084 B) da `response_input_buffer_full` («…limited to 128 items and 32768 UTF-8 bytes per session») y, en el mismo milisegundo, `function_call_outputs_required`: la llamada queda pendiente y cada `response.create` posterior falla. Ocho de 17.741 B pasan en una misma sesión. Uno de 32.768 B exactos: **sin medir** | revisión de fidelidad, `fid\run-grande-40.txt` y `run-rondas.txt`, 09-12 |
| Un error antes de `session.started` | `credit_balance_exhausted` llega en lugar de `session.started`, sin `client_event_id`, y a los ~2,0 s el socket queda `Aborted` (medido dos veces; la última, a 604 ms y 2.598 ms). «Instructions must not exceed 16384 tokens» hace lo mismo | `fid\run-args.txt`, sonda del arreglo y sonda de huecos, 09-12 |

**Lo que dicen las latencias.** GPT-Live llama la herramienta más tarde (dos cerebros: la voz entiende,
el delegado decide) y empieza a hablar antes (la voz no espera al delegado). La suma para «lo hizo»
—de la frase a la acción— es de 0,2–0,6 s peor que Realtime en esta frase; la voz, en cambio, se oye
antes. Una sola frase y una sola sala: es un dato, no una distribución.

## Por qué va dirigido por especificación

Porque cambiar de proveedor es justo donde los traductores fallan en silencio —*«la sesión queda
abierta y Ü simplemente no contesta»*, dice `IProtocolo`— y porque la noche del 2026-09-11 un nivel 4
certificó lo que no había medido: la rama sacó 4/12 con tareas que resolvió el agente por coordenadas
con la voz ya cerrada. Tres cosas de esta migración se darían por buenas solas:

- **«La voz contesta»**, si se prueba hablando: el micrófono tapa el agujero del texto escrito.
- **Los turnos**: sin marcas, nada da error; simplemente `TurnoCerrado` no llega nunca.
- **El defecto**: una variable leída en un constructor con un respaldo sigue abriendo la voz vieja sin
  que nadie lo note, y el log de apertura es lo único que lo diría.

**La regla del flujo, y no tiene excepciones:** ninguna línea de producción entra antes que la
promesa que la juzga. Cada fase empieza con el contrato ROTO y termina con el contrato INTACTO.

## El diseño

Decidido antes de esta spec; lo que una sonda contradiga, manda la sonda y se anota en Hallazgos.

- **A · `voz/Realtime/IProtocolo.cs`** gana tres miembros **con implementación por defecto**, para que
  `ProtocoloOpenAI` no cambie de comportamiento:
  - `bool MarcaLosTurnos => true;` — el servidor manda `speech_started` y `response.done`.
  - `bool SabeEsperarTurno => true;` — sabe oír sin contestar hasta que se le pide turno (192).
  - `IEnumerable<string> CambioDeModo(string instrucciones, IReadOnlyList<Utensilio> utensilios, bool soloCuandoSeLePide) => Apertura(instrucciones, utensilios, "", soloCuandoSeLePide);`
- **B · `voz/Realtime/ProtocoloGptLive.cs`**, clase nueva, `sealed`, sin estado. `Quien` «OpenAI
  GPT-Live», `Modelo` «gpt-live-1», constructor `(string modelo = "gpt-live-1", string delegado =
  "gpt-5.6-luna")`, 24000/24000, `Mira => false` (el delegado ve las fotos, pero no caben: la 49), `SabeVolver => false`,
  `MarcaLosTurnos => false`, `SabeEsperarTurno => false`, voz `marin`.
  - `Apertura` → **un** `session.start`: la voz lleva unas instrucciones **cortas** de persona (español,
    frases cortas, delega todo lo que sea mirar u operar la pantalla); el delegado lleva las
    instrucciones completas de Ü, las herramientas (tipo `function`, parámetros `string`, como en
    `ProtocoloOpenAI`) y `tool_choice: auto`.
  - `CambioDeModo` → `session.update` de la delegación (instrucciones y herramientas nuevas del
    delegado), **nunca** otro `session.start`. La forma exacta la confirma una sonda (fase 1).
  - `Audio` → `session.input_audio.append`. `Texto` → `response.item.create` message user `input_text`.
    `Fotograma` → `response.item.create` message user `input_image`. `Resultados` → un
    `response.item.create` `function_call_output` por llamada, **sin pedir respuesta**.
    `PedirRespuesta("")` → `response.create`; `PedirRespuesta(instrucciones)` →
    `session.commentary.append {delegation_id: null, content: instrucciones}`.
  - `Leer`: `session.output_audio.delta` → `Suena` (delta vacío → nada); `session.output_transcript.delta`
    → `DiceU`; `session.input_transcript.delta` → `DiceElUsuario`; `response.event` con
    `response.output_item.done` de tipo `function_call` → `Pide` con la llamada y sus argumentos
    parseados como en `ProtocoloOpenAI.LaLlamada`; `error` → `Falla(message)`; `session.closed` →
    `Falla("sesión cerrada: <reason>")`. **No emite `CierraElTurno` ni `HablaronEncima`**: no hay
    marca fiable, y las sintetiza la conversación.
- **C · `windows-client/src/Voice`**:
  1. `TurnosSinMarca`, clase pura con reloj inyectado y silencio configurable (1500 ms por defecto):
     dice cuándo **empieza** un turno del usuario (el primer trozo de `DiceElUsuario` tras un cierre) y
     cuándo **toca cerrar** (hubo `DiceU` o `DiceElUsuario` y lleva ≥ silencio sin ninguno nuevo).
     `ConversacionEnVivo` la usa **solo si `!_protocolo.MarcaLosTurnos`**, en el hilo de recepción,
     después de procesar los hechos de cada mensaje —el audio llega continuo, así que se evalúa a
     menudo, sin temporizadores ni carreras—: al empezar, `EmpiezaUnTurnoDelUsuario("voz")` (sin
     callar); al tocar cerrar, `Reaccionar(new Hecho.CierraElTurno())`.
  2. **Lo escrito pide respuesta**: un estático puro `ConversacionEnVivo.MensajesDeTexto(IProtocolo p,
     string texto)` → `[p.Texto(texto), p.PedirRespuesta()]` sin vacíos, y `EnviarTextoAsync` manda eso.
  3. `CambiarModoAsync` usa `_protocolo.CambioDeModo(…)`; con `soloCuandoSeLePide && !SabeEsperarTurno`
     deja una línea `voz-viva` que dice que esta voz no sabe esperar turno y seguirá contestando sola.
     Sin fingir.
  4. `ProtocoloPorDefecto(Func<string, string?> variable)`, estático puro: `U_VOZ` vacío o `gpt-live` →
     `ProtocoloGptLive`; `realtime` → `ProtocoloOpenAI`. El constructor lo usa con
     `Environment.GetEnvironmentVariable` cuando no le pasan protocolo; el log de apertura ya dice el
     modelo.
- **D · `ProtocoloOpenAI`**: `audio.input.transcription.model` pasa de `gpt-4o-mini-transcribe` a
  `gpt-transcribe` (aceptado, medido).
- **E · `scripts/nivel4-voz/analizar.py`**: una tarea no se aprueba con cero acciones de voz. **Hecho en
  esta rama** (ver Hallazgos).

## La especificación

**Numeración.** El máximo de `main` es la **32** en el contrato de la voz y la **207** en el del grafo.
Las ramas abiertas de Jose usan números del contrato de la voz por encima de la 32; para no pisarlas,
esta spec deja el hueco **33–39** y empieza en la **40**. En el grafo sigue en la **208**. Los números
no se reciclan; los huecos están permitidos. Los arreglos de las revisiones del 2026-09-12 se repartieron
por ramas hijas los números **44–49** (voz) y **211–219** (grafo): `jero/voz-gpt-live-silencio` usa la **44**,
`jero/voz-gpt-live-persona` la **46**, la **47** y la **48**, `jero/voz-gpt-live-fidelidad` la **49**,
`jero/voz-gpt-live-turnos` la **211** y la **212**, `jero/voz-gpt-live-texto` la **214** y
`jero/voz-gpt-live-seleccion` la **217**, la del log. La **45** quedó sin usar; ningún número chocó al integrar.

**D tiene promesa: la 43.** Ninguna promesa fijaba el modelo de transcripción antiguo
(`gpt-4o-mini-transcribe` aparecía en 0 sitios de los dos contratos), y por eso el borrador la dejaba sin
usar. El traductor (`jero/voz-gpt-live-protocolo`) la escribió igual, en rojo antes que el cambio y
saboteada, y congela `gpt-transcribe` en la sesión de Realtime. Que el servidor lo acepta, y que con la
misma frase manda los mismos 9 `.delta`, no se puede medir sin red: lo midió la sonda.

| # | Contrato | Promesa | Fase |
|---|---|---|---|
| 40 | voz | GPT-Live abre por su propio endpoint con session.start: modelo gpt-live-1, voz marin, PCM 24 kHz, y las herramientas de Ü van en la delegación con su modelo delegado, no en la voz. | 1 |
| 41 | voz | GPT-Live traduce lo que manda el servidor: audio, lo que dice Ü, lo que dice el usuario, una llamada delegada con sus argumentos y un error; y no inventa marcas de turno que el servidor no manda. | 1 |
| 42 | voz | GPT-Live manda lo de Ü con sus eventos: micrófono, texto y foto como mensajes de usuario, resultados como function_call_output, pedir turno como response.create, dictar como commentary; y cambiar de modo a mitad de sesión es un session.update de la delegación, no otro session.start. | 1 |
| 43 | voz | GPT Realtime pide la transcripción de lo que dice el usuario a gpt-transcribe, no a gpt-4o-mini-transcribe, que se apaga el 2027-02-26. | 5 |
| 49 | voz | GPT-Live no manda lo que su servidor rechaza: no se declara capaz de mirar, porque una captura de pantalla no cabe y una segunda foto pequeña tampoco; un resultado de más de 32.768 bytes sale como un function_call_output de 32.768 bytes o menos, con su call_id, sin partir un carácter y diciendo cuánto se recortó; y declara que confirma la apertura: session.started es un Hecho.Abierta, y ni un error ni ningún otro mensaje lo es. | fidelidad |
| 208 | grafo | escribir con la voz abierta pide respuesta: el texto va seguido de pedir turno, con cualquier protocolo. | 2 |
| 209 | grafo | con una voz que no marca los turnos, la conversación los marca: el primer trozo de lo que dice el usuario abre un turno y un silencio lo cierra. | 3 |
| 210 | grafo | la voz por defecto es GPT-Live y U_VOZ=realtime vuelve a GPT Realtime. | 4 |
| 211 | grafo | sin marcas de turno, el turno no se cierra con trabajo en marcha: ni con una llamada a herramienta sin devolver ni mientras suena la voz de Ü, y el silencio se cuenta desde la devolución o desde lo último que sonó; el audio en silencio no cuenta. | 6 |
| 212 | grafo | sin marcas de turno, una pausa del usuario sin que Ü le haya contestado sigue siendo la misma petición: lo que dice después no abre turno ni reinicia el tope; lo que dice después de que Ü le conteste, sí. | 6 |
| 214 | grafo | con GPT-Live, varias llamadas pedidas a la vez se contestan todas antes de pedir turno, y el turno se pide una sola vez; con GPT Realtime una tanda sigue pidiendo turno detrás de sus resultados. | 6 |
| 217 | grafo | con GPT-Live el log no se inunda: ni los deltas del delegado ni el audio dejan una línea «←», y lo demás que no se traduce la sigue dejando. | 6 |

**La que cierra el asunto** es la **210**: sin ella todo lo demás existe y la voz sigue siendo la de
antes. **La que cierra el agujero del nivel 4** es la **208**: sin ella ningún nivel 4 escrito mide la voz.

### Con qué se juzga cada una

Todas sin socket, sin micrófono, sin clave y sin pantalla. Las capacidades nuevas se piden por nombre,
con reflexión, y nacen en rojo con `Pendiente(…, "018")`. Los mensajes del servidor se le dan al
traductor tal como los mandó el servidor real en las sondas.

| # | Se juzga con | Sabotaje que la tiene que poner roja |
|---|---|---|
| 40 | `ProtocoloGptLive` por nombre en el ensamblado de `Voz.Realtime`. `Direccion()` es `wss://api.openai.com/v1/live/sessions` sin consulta; `Cabeceras` lleva `Authorization: Bearer`; 24000/24000; `SabeVolver`, `MarcaLosTurnos` y `SabeEsperarTurno` no (`Mira` lo juzga la 49). `Apertura("instrucciones de Ü", [map_look con un argumento], "")` da **un** mensaje: `session.start`, `session.model` `gpt-live-1`, `audio.format` `audio/pcm` a 24000, `audio.output.voice` `marin`; **sin `tools` en la sesión**; `delegation.type` `responses`, `delegation.responses.model` `gpt-5.6-luna`, sus `tools` con `map_look` y el argumento como `string`, `tool_choice` `auto`; las instrucciones completas van en el delegado y las de la voz son otras y más cortas. Y `ProtocoloOpenAI` sigue marcando los turnos y sabiendo esperar (los valores por defecto) | las herramientas viajan en la sesión y no en la delegación; la dirección vuelve a `/v1/realtime?model=`; el ritmo pasa a 16000; las instrucciones completas van a la voz |
| 41 | `Leer` con mensajes capturados: `session.output_audio.delta` da un `Suena` con el PCM exacto y un delta vacío no da nada; `session.output_transcript.delta` da `DiceU`; `session.input_transcript.delta` da `DiceElUsuario`; `response.event` con `response.output_item.done` de tipo `function_call` da **un** `Pide` con `call_id`, `name` y los argumentos parseados del texto JSON; ese mismo sobre con un item `message` no da nada, y `response.function_call_arguments.done` **tampoco** (la llamada es una); `error` da `Falla` con su `message`; `session.closed` da `Falla` con su `reason`. Y ninguno de los mensajes capturados —`session.started`, `session.delegation.created`, `response.completed`, `session.usage.updated`…— da `CierraElTurno` ni `HablaronEncima` | leer también `function_call_arguments.done` (dos `Pide` por una llamada: se ejecutaría dos veces); `response.completed` vuelve a ser `CierraElTurno`; los argumentos se pasan sin parsear |
| 42 | `Audio(pcm)` es `session.input_audio.append` con el base64 exacto; `Texto` es `response.item.create` con un message `user` e `input_text`; `Fotograma` igual con `input_image` y `data:image/jpeg;base64,`; `Resultados` con dos llamadas da dos `function_call_output` con su `call_id` y su `output`, y **ninguno** pide respuesta; `PedirRespuesta()` es `response.create`; `PedirRespuesta("X")` es `session.commentary.append` con `content` X y `delegation_id` nulo, y no un `response.create`; `CambioDeModo(otras, [otra herramienta], false)` no contiene `session.start`, es un `session.update` y lleva las instrucciones y la herramienta nuevas en la delegación | `CambioDeModo` devuelve la apertura (otro `session.start`); `Resultados` pide respuesta dentro; dictar vuelve a ser `response.create`; el texto sale como `session.instructions.append` (medido: no provoca respuesta) |
| 49 | `Mira` es falso. `Resultados` con un resultado de 40 KB da **un** mensaje de 32.768 B o menos y de más de 32.752 (no recorta de más), `function_call_output` con su `call_id`, con el principio del resultado y la cola `…[recortado: N de M bytes]` con N y M verdaderos; con 6.000 emojis, lo guardado son emojis enteros; uno que cabe (150 frases con acentos y comillas) va entero. `ConfirmaQueAbrio` es verdadero en GPT-Live y falso por defecto en `ProtocoloOpenAI`; el `session.started` capturado es **un** `Hecho.Abierta`; el `credit_balance_exhausted` capturado es un `Falla` y no una apertura; `session.updated`, `session.delegation.created`, `response_input_buffer_full`, `response.completed`, `session.usage.updated` y `session.closed` no dan `Abierta` | `Mira` vuelve a sí; `Resultados` manda el resultado entero; el recorte parte un emoji; la cola dice que se mandó todo; la apertura se lee en `session.updated`; GPT-Live deja de declarar que confirma |
| 208 | `ConversacionEnVivo.MensajesDeTexto` por nombre. Con `ProtocoloOpenAI`: dos mensajes, **en ese orden**, `conversation.item.create` con el texto y `response.create`. Con `ProtocoloGptLive`: `response.item.create` con el texto y `response.create`. Con un protocolo falso del propio contrato cuyo `PedirRespuesta` es vacío: un solo mensaje, ninguno vacío. **Y su cableado**, añadido tras W208: la conversación deja sustituir su puerta de salida (`_puerta`) y el «hay socket» (`_puertaAbierta`). Con los dos protocolos, lo escrito (`EnviarTextoAsync`) y la nota al modelo (`EnviarTextoAlModeloAsync`) sacan por esa puerta el texto y detrás `response.create`; con la voz cerrada no sale nada | `MensajesDeTexto` deja de añadir la petición de turno; la petición va antes que el texto; se cuela el mensaje vacío; `EnviarTextoAsync` o la nota al modelo vuelven a mandar solo el texto; `EnviarAsync` se salta la puerta; lo escrito sale con la voz cerrada |
| 209 | `TurnosSinMarca` por nombre, con reloj inyectado y la secuencia como hechos con su hora: el primer trozo de lo que dice el usuario abre turno y el segundo no; con el silencio por defecto (**2000 ms**, medido: ver Hallazgos), a 1000 ms del último trozo no toca cerrar y a 2000 sí, **una vez**; lo que dice Ü también mantiene el turno abierto y también se cierra por silencio; el audio que llega continuo **no** cuenta como actividad; sin nada oído no se cierra nunca; un silencio configurado de 500 ms se respeta. Y la 40 ya juzga que `ProtocoloGptLive` no marca los turnos y `ProtocoloOpenAI` sí. **Y su uso, tal como lo construye la app** (acotado el 2026-09-12): `Procesar`, la puerta por la que entra lo del socket, recibe en una `ConversacionEnVivo` con `ProtocoloGptLive` un `session.input_transcript.delta` y mensajes sin hechos. **Se cambia solo `_relojDeLosTurnos`, nunca el marcador.** El reloj que trae la app tiene el origen de `Environment.TickCount64` y avanza con él. A 1700 ms no emite `Cerro`; a 2100 ms lo emite una vez, con la línea `voz-turno` «por voz» y `DijoElUsuario` con la frase. `EmpezarLosTurnosDeLaSesion` deja un marcador **nuevo**, y lo dicho antes no cierra después. Con un protocolo que marca sus turnos, y con `ProtocoloOpenAI`, no hay marcador ni cierre | se cierra sin haber oído nada; cada trozo abre turno; el audio cuenta como actividad; `Procesar` deja de consultar el marcador; la conversación no lo crea; el primer trozo no llama a `EmpiezaUnTurnoDelUsuario`; tocar cerrar no reacciona; **la app construye con 60 000 ms (G1) o con 1500 (G1b); el reloj de la app está parado (G2); el marcador no usa el reloj de la conversación (G3); otra sesión reutiliza el marcador (G4); hay marcador también con voces que marcan (G6)** |
| 210 | `ConversacionEnVivo.ProtocoloPorDefecto` por nombre, con una variable falsa que anota qué nombre se le pregunta. **Se juzga lo que abre, no el nombre del tipo** (acotado el 2026-09-12, tras la revisión): sin `U_VOZ`, con `U_VOZ` vacío o en blanco, con `gpt-live` y con un valor desconocido (`gemini`), sale `ProtocoloGptLive` con `Modelo` `gpt-live-1`, `Delegado` `gpt-5.6-luna` (por reflexión) y `Direccion()` `wss://api.openai.com/v1/live/sessions`; con `realtime` y con ` Realtime `, `ProtocoloOpenAI` con `Modelo` `gpt-realtime-2.1-mini` y `Direccion()` `wss://api.openai.com/v1/realtime?model=gpt-realtime-2.1-mini`; y el nombre preguntado es `U_VOZ`. **El constructor se puede llamar sin pantalla**, porque `LiveAudio` nace sin dispositivo, así que también se juzga, escuchando `LogBus.Anotado`: sin protocolo y sin `U_VOZ` abre lo mismo que GPT-Live de arriba y no deja ninguna línea `voz-viva` sobre `U_VOZ`; con `U_VOZ=realtime` abre lo de Realtime y tampoco la deja; con `U_VOZ=gemini` abre GPT-Live y deja **una** línea `voz-viva` que nombra «gemini» | el defecto vuelve a ser `ProtocoloOpenAI`; la variable se lee con otro nombre; no se normaliza; el constructor no usa el defecto; **el defecto abre un modelo que no existe (`gpt-live-1-mini`, G2)**; **otro delegado**; **Realtime con otro modelo**; **el aviso del valor desconocido invierte su condición (G3)**; **el aviso sale siempre** |
| 211 | `TurnosSinMarca` por nombre con silencio de 1500 y reloj inyectado. Una llamada pedida queda en curso (`LlamadasEnCurso`) y sujeta el turno 6000 ms. Tras `Devuelta`, a 1000 ms no cierra y a 1500 sí, una vez. Con dos llamadas y una devuelta sigue sin cerrar. Una devuelta **antes** de oírse no queda en curso. Un trozo de audio con pico 1152 (la palabra más floja medida) sujeta el turno aunque la transcripción terminara 3000 ms antes; uno con pico 45 (el silencio medido) no lo retrasa; sonido sin nada dicho no abre nada que cerrar. **Y su cableado**: en una `ConversacionEnVivo` con GPT-Live, un `self_mute` que el contrato sujeta dentro de `Autocontrol` no deja cerrar a los 3000 ms. Al soltarlo, la conversación se lo devuelve al marcador, y cierra a 2100 ms de la devolución, no a 1500 | el marcador no registra la llamada (T1); tocar cerrar la ignora (T2); devolver no cuenta como actividad (T3); la voz que suena no sujeta (T4); el umbral a 0 (T5); una devuelta antes de oírse se queda en curso (T6); **`EjecutarAsync` no devuelve (T7)** |
| 212 | `TurnosSinMarca` por nombre, silencio 1500. Lo que el usuario dice tras una pausa que cerró el turno, sin que Ü hablara después, **no** abre turno; tras contestar Ü y cerrar, sí. La respuesta de Ü cuenta aunque llegue antes del cierre. Si el usuario siguió hablando después de lo que dijo Ü, tras el cierre sigue siendo su petición. Lo primero que dice el usuario abre turno aunque Ü saludara antes. **Y en la conversación**: la pausa cierra el turno y deja una sola línea «turno nuevo (por voz)»; lo que dice tras la respuesta de Ü deja la segunda | cualquier cierre termina la petición (T8); lo que dice Ü no cuenta como contestar (T9); que el usuario siga hablando no borra el «contestó» (T10); el primer trozo no abre turno en la conversación (S209f) |
| 214 | `Procesar` de una `ConversacionEnVivo` con `ProtocoloGptLive`, la puerta sustituida y la mano de autocontrol retenida, recibe dos `response.event` con un `function_call` cada uno, como los manda el servidor: salen las dos `function_call_output` y **un** `response.create`, detrás de la última. Una llamada sola sigue pidiendo turno. Una llamada pedida con el token ya cancelado (nunca corre) y, tras cambiar `_sesionId`, otra de la sesión siguiente: sale su salida y un `response.create`. Con `ProtocoloOpenAI`, `EjecutarNucleoAsync` con una tanda de dos: dos salidas y detrás un `response.create` | no se anotan las llamadas al pedirlas; no se olvidan las de otra sesión; se retiene el turno aunque no falte ninguna |
| 217 | Por la puerta del socket: `Procesar` de una `ConversacionEnVivo` con `ProtocoloGptLive`, escuchando `LogBus.Anotado`. Un `response.event` con `response.output_text.delta`, otro con `response.function_call_arguments.delta` y un `session.output_audio.delta` vacío no dejan ninguna línea `voz-viva` que empiece por «← »; un `response.completed` del delegado, un `response.function_call_arguments.done`, un `session.delegation.created` y un `session.usage.updated` dejan una cada uno. Y la regla, pura, `ConversacionEnVivo.SeVuelcaCrudo(JsonElement)` por nombre: los tres primeros no se vuelcan y los cuatro siguientes sí; un `response.event` sin evento dentro sí; y un `.delta` que no viene dentro de `response.event` (el `response.function_call_arguments.delta` de Realtime) sigue volcándose, como en `main` | `Procesar` deja de consultar la regla; los deltas del delegado se vuelcan; el audio vacío se vuelca; se callan todos los `response.event`; se calla todo lo que acabe en `.delta` |

### Límites dichos, no escondidos

- **La forma del `session.update` de la delegación ya está medida** (ver Hallazgos). Coincide con la que
  manda `ProtocoloGptLive.CambioDeModo`, así que la 42 no hubo que ajustarla.
- **La 209 y la 208 juzgan su cableado; el cambio de modo, no.**
  - La 209 entra por `Procesar`, que no necesita socket, y cada sabotaje de su cableado la pone roja.
  - **Desde la fase 6 juzga también cómo construye la app el marcador**: cambia solo `_relojDeLosTurnos`,
    nunca el marcador. Construirlo con otro silencio (G1, G1b), con un reloj parado (G2) o sin el reloj de la
    conversación (G3), y reutilizarlo entre sesiones (G4), la ponen roja. Antes, G1 y G4 la dejaban INTACTA.
  - La 211 juzga su cableado del mismo modo: una herramienta que el contrato sujeta dentro de `Autocontrol`
    no deja cerrar, y sin la devolución en `EjecutarAsync` (T7) sale roja.
  - **Sin juez, medido (W5):** que `ArrancarAsync` llame a `EmpezarLosTurnosDeLaSesion`. Necesita clave y
    socket. Romperlo deja el contrato INTACTO; lo tiene que decir el nivel 4.
  - La 208 lo juzga desde el 2026-09-12 (rama `jero/voz-gpt-live-texto`): la conversación deja sustituir su
    puerta de salida, y lo que sale de `EnviarTextoAsync` y de la nota al modelo se compara con lo esperado.
    W208 (volver a mandar solo el texto), que dejaba el contrato INTACTO, ahora la pone roja (ver la tabla).
  - Lo que la 208 **no** juzga: que en la app «hay por dónde» sea exactamente la voz viva **y** el socket
    abierto. El contrato no tiene socket ni voz viva; solo comprueba que con la voz cerrada no sale nada.
    Mirar solo `Viva` (W208e) quedaría en verde.
  - `CambiarModoAsync` sigue saliendo sin mandar nada si no hay un socket abierto, y volver a mandar la
    apertura al cambiar de modo **deja el contrato INTACTO** (WModo, medido).
  - El log del nivel 4 lo sigue teniendo que decir: `llamada recibida` tras un texto escrito sin que el
    micrófono oiga nada, y tras 🎓 `modo cambiado` sin un `error` de `session.start`. El juez del nivel 4
    ya no aprueba sin acciones de voz (E), así que un cableado roto sale como suspenso.
- **La 49 juzga el traductor; lo que la conversación hace con él, no.**
  - Que un error antes de confirmar, seguido de la muerte del socket, no reconecte y diga la causa, y que
    «Te escucho.» y «Sigo» esperen a `session.started`, vive en `ConversacionEnVivo` (`RecibirAsync`,
    `EmpiezaUnaConexion`, `Reaccionar`). Ningún contrato lo juzga: a este arreglo no se le dio número del grafo.
  - Lo mide `sonda-conv`, fuera del repo: la `ConversacionEnVivo` real contra el servidor sin crédito, y por
    `Procesar`, sin red, el camino feliz. Sus sabotajes (W49a–d) están en «Sabotajes».
- **La 192 y la 142 siguen verdes, y con GPT-Live por defecto no se cumplen.** Las dos juzgan
  `ProtocoloOpenAI` por su nombre (`Contrato.cs`, `LaVozDeLaComprobacionEsPrestada` y
  `LaVozDeLaComprobacionEsLaDeU`), no el protocolo que abre la voz. Es un guardia que se cree puesto
  (aprendizaje nº18), y se dice aquí para que nadie lea su verde como «la voz prestada funciona».
- **Un valor desconocido** (`U_VOZ=gemini`) abre la voz por defecto, GPT-Live, y el constructor deja una
  línea `voz-viva` que lo dice. Se decidió al implementar. Hasta el 2026-09-12 esta frase decía que lo
  juzgaba la 210 y **no era verdad**: la 210 solo miraba qué tipo salía, y con la condición del aviso
  invertida seguía verde (G3 de la revisión). Desde entonces la 210 escucha el log y exige esa línea.
- **Con qué modelo abre la app de verdad** tampoco lo juzgaba nadie hasta el 2026-09-12: la 40 juzga una
  instancia que construye el propio contrato, y la 210 miraba el nombre del tipo. Con un modelo
  inexistente en `ProtocoloPorDefecto` los dos contratos quedaban en verde (G2). Ahora la 210 exige
  modelo, delegado y dirección de lo que devuelve `ProtocoloPorDefecto` y de lo que abre el constructor.
- **La línea `sesión abierta con «…»` de `ArrancarAsync` sigue sin juez**: se escribe con el socket ya
  abierto. Dice el modelo de la voz, no el del delegado; lo que abre de verdad lo juzga la 210 un paso
  antes, sobre el mismo `_protocolo`.

## Las fases

Una spec, una rama por encargo, una fase por commit. El portero exige los dos contratos intactos para
empujar: el rojo de cada fase se comprueba en local y se anota en su commit.

| Fase | Promesa | Qué toca | Terminado cuando |
|---|---|---|---|
| **0** | — (medir) | nada | la tabla de arriba — **hecha**, con el `session.update` de la delegación medido en la fase 1 |
| **1** | 40, 41, 42 | `voz/Realtime/IProtocolo.cs` (tres miembros con defecto), nuevo `voz/Realtime/ProtocoloGptLive.cs`; una sonda del `session.update` | **hecha**: rojo en `18071b8`, verde en `8fed7ae`, **VOZ ÍNTEGRA** |
| **2–4** | 208, 209, 210 | `Voice/ConversacionEnVivo.cs` (`MensajesDeTexto`, `ProtocoloPorDefecto`, constructor, recepción, `CambiarModoAsync`), nuevo `Voice/TurnosSinMarca.cs` | **hecha** en un solo paso, porque las tres tocan la misma clase: rojo en `b64972b`, verde en el commit siguiente, **CONTRATO INTACTO** con 0 pendientes |
| **5** | 43 | `ProtocoloOpenAI`: `gpt-transcribe` (D) | **hecha** con la fase 1. Con `U_VOZ=realtime`, que una sesión abra sin `error` es del nivel 4 |
| **6** | 208 (su cableado), 214 | `Voice/ConversacionEnVivo.cs`: la puerta de salida (`_puerta`, `_puertaAbierta`), `EnviarTextoAsync`, `EnviarTextoAlModeloAsync`, las llamadas sin contestar (`AnotarSinContestar`, `DarPorContestadas`) y la cola de `EjecutarNucleoAsync` | **hecha** en `jero/voz-gpt-live-texto`: rojo en `731c60f` (208 y 214 pendientes), 208 verde y 214 roja por su razón en `6c25642`, las dos verdes en `1a645ff`, **CONTRATO INTACTO** |
| **E** | — (el juez) | `scripts/nivel4-voz/analizar.py` y `autoprueba.py` | **hecha** en esta rama: rojo, verde y sabotaje (ver Hallazgos) |
| **6** | 211, 212 (y la 209 acotada) | `Voice/TurnosSinMarca.cs`, y de `Voice/ConversacionEnVivo.cs` lo que usa el marcador (`_relojDeLosTurnos`, `EmpezarLosTurnosDeLaSesion`, la devolución en `EjecutarAsync`); la sonda de los turnos | **hecha** en `jero/voz-gpt-live-turnos`: rojo en `1497dc1` (CONTRATO ROTO, 11), verde en `79fcea2` (**CONTRATO INTACTO**, VOZ ÍNTEGRA), saboteada (ver «Sabotajes») |
| **6** | 210 (acotada), 217 | `tests/ContratoDelGrafo/Contrato.cs`; `Voice/ConversacionEnVivo.cs` (`SeVuelcaCrudo` y su uso en `Procesar`) | **hecha** en `jero/voz-gpt-live-seleccion`, tras la revisión: la 210 nueva roja con G2 y G3 aplicados, la 217 roja antes de su código, las dos verdes después y saboteadas (ver «Sabotajes») |

**Sitios con la clase de error, contados con `grep`:**

- **Texto sin pedir turno.** 2 sitios de `ConversacionEnVivo` mandan `_protocolo.Texto(…)`:
  `EnviarTextoAlModeloAsync` ya pide respuesta y `EnviarTextoAsync` no. **Los dos pasan ahora por
  `MensajesDeTexto`**, y ya no queda ningún `_protocolo.Texto` suelto.
  - Detrás de `EnviarTextoAsync` hay 2 llamadas en `FaceWindow`: «Escríbele…» (`:2300`) y **el saludo de
    la presentación** (`:2532`).
  - El saludo tenía el mismo agujero. Se arregla sin tocar `FaceWindow`.
- **Modelo de transcripción deprecado.** 2 sitios: `voz/Realtime/ProtocoloOpenAI.cs:95` y
  `mac-client/Sources/U/VivoOpenAI.swift:181`. El segundo no es de esta máquina (ver «Lo que NO entra»).

## Lo que se pierde o se degrada con GPT-Live

Dicho antes de que alguien lo descubra en una demo. Cada uno con lo que se midió y lo que no.

1. **La voz prestada (promesa 192) no se puede hacer.**
   - **Medido**: no hay `create_response:false`, y pedirle por instrucciones que no hable por su cuenta
     no se respeta.
   - **Consecuencia**: durante una comprobación con el piloto, la voz contestará sola a lo que oiga. Es
     lo que la duodécima prueba de la spec 014 ya sufrió: dos manos y una voz que anunciaba pasos que no
     tocaban.
   - **Qué hace el código**: `CambiarModoAsync` lo deja escrito en el log y no finge.
   - **Mitigación, sin promesa**: comprobar con `U_VOZ=realtime`.
2. **Las marcas de turno las pone un silencio, no el servidor.**
   - El servidor sigue decidiendo cuándo contestar. El silencio decide solo cuándo la app da el turno por
     cerrado: `TurnoCerrado`, `Cerro`, «Ü dijo», la frase entregada a quien aprende (`DijoElUsuario`,
     promesa 105) y la respuesta que espera el piloto (`PreguntarYEsperar`).
   - **Desde el 2026-09-12 son 2000 ms y el turno no se cierra con trabajo en marcha** (211): ni con una
     llamada sin devolver ni mientras suena la voz. Medido con la sonda de los turnos (ver Hallazgos):
     con 1500 ms, aun con esas guardas, el turno se cerraba entre devolver una herramienta y hablar Ü
     (1658-1707 ms, 4 de 4).
   - **Lo que todavía parte**: una pausa del usuario de ~1,9 s deja un hueco de transcripción de 1868-2119 ms
     (1 de 2 por encima de 2000), y entonces el piloto o quien aprende recibe la primera mitad como frase.
     Es la avería que `semantic_vad` evitaba en Realtime, trasladada a la contabilidad de la app. Las pausas
     de ~1 s (1065-1138 ms) no parten. **Con voz sintetizada, no con una persona.**
   - **Una pausa ya no reinicia el tope** (212): lo que el usuario dice tras un cierre, sin que Ü le haya
     contestado, sigue siendo la misma petición.
   - El turno del usuario empieza con el primer trozo de **transcripción**, que llega después de que
     empezó a hablar (1,0-1,2 s después del inicio de la voz en la sonda). El tope de la 204 se reinicia
     más tarde que con `speech_started`.
   - Una petición que llega **encima** de la respuesta de Ü, antes del cierre, no abre turno: no hay forma
     de separarla de la frase del usuario que sigue tras un «Claro», porque las transcripciones se
     entrelazan. El tope no se reinicia en ese caso. Sin medir cuánto pasa con personas.
3. **No hay aviso de interrupción.**
   - Hablarle encima no produce evento, así que la conversación no calla la cola local por eso.
   - `Hecho.Retira` tampoco llega nunca: una llamada del delegado no se retira si se le habla encima.
   - Escape (`Interrumpir`) sigue funcionando, porque es local. El barge-in por energía sigue apagado en
     esta máquina, medido el 2026-08-31.
   - **Medido en la sonda de huecos (2026-09-12)**: si le hablan encima mientras lee, la voz se corta sola
     y no avisa con ningún evento.
   - Lo peor, también medido: si el usuario cambia de pedido mientras el delegado espera una herramienta,
     la voz contesta «Claro, lo abro ahora» y **no abre ninguna delegación** (2 de 2 veces). La delegación
     vieja no se cancela y termina lo suyo. Sin guardia en esta spec: la guardia necesita su propia
     promesa (reinyectar lo transcrito si la voz confirma y no llega `session.delegation.created`).
4. **El consumo se cuenta en segundos, no en fichas.**
   - `ReportarConsumo` suma fichas y no reporta si el total es cero. Con GPT-Live, `session.usage.updated`
     no se traduce, así que **el panel de costos no recibirá nada de estas sesiones**.
   - No se inventa una conversión de segundos a fichas. Traducirlo es otra spec.
5. **Dos cerebros, y su latencia.**
   - La voz entiende y el delegado decide y actúa. Llama la herramienta 0,2–0,6 s más tarde que
     Realtime en la frase medida, y la voz empieza antes, con lo que puede hablar antes de que la acción
     exista.
   - Las instrucciones de Ü —las que juzgan la 161 («Ü no anuncia lo que va a hacer») y la 206— van al
     **delegado**. Quien habla es la voz, con su persona corta.
   - Si la persona no lleva también «no anuncies; habla en pasado», la voz puede anunciar. **Sin medir.**
6. **Cambiar de modo solo cambia al delegado.** Con 🎓 (promesa 138) el delegado se queda sin
   herramientas y con las instrucciones del aprendiz. **La voz no cambia de persona**, y seguirá
   contestando cuando la oiga. Sin medir.
7. **Dictar ya no es «fuera de la conversación».** La 142 exige `conversation: none` porque el
   2026-09-08, con el triage en contexto, la voz parafraseó lo dictado. Con GPT-Live, lo dictado es un
   `commentary` dentro de la sesión. Dijo «X» literal en una sesión vacía; **con contexto, sin medir**.
8. **Con GPT-Live, Ü no ve la pantalla (la 49).** El delegado sí lee una foto, pero una captura de tamaño real
   no cabe (`response_input_buffer_full`) y una segunda foto pequeña tampoco, así que `Mira` es falso:
   `map_look` contesta que no puede y ofrece `map_what_i_see`, y la foto de un recuerdo nuevo no se manda.
   Volver a mirar exige medir antes una forma de mandar fotos que quepa siempre, no solo la primera vez.
   Este punto decía antes «una foto suelta tras un resultado, sin medir en esa forma», y lo dejaba al nivel 4.
   Lo midió antes la revisión de fidelidad, con la forma que usa `ConversacionEnVivo` (resultado, foto y
   `response.create`): una de 520 px se leyó y una de tamaño real no cupo.
9. **Escribir con una delegación en marcha abre un turno de más.**
   - Un `response.create` con una delegación corriendo no da error: **se encola**, medido en la sonda de huecos.
   - 150 ms después de terminar la anterior abre otra delegación entera, que en la sonda volvió a llamar `map_look`.
   - La 208 manda un `response.create` detrás de cada texto escrito, así que escribir mientras Ü trabaja
     cuesta un turno completo más. Sin medir con la app.
10. **El log de «mensajes sin hechos» crecía con GPT-Live. Arreglado, con la 217.**
    - `Procesar` escribe `← …` por cada mensaje que no traduce. Con GPT-Live eso incluía cada `response.event` del delegado.
    - **Medido el 2026-09-12** (sonda de solo lectura, un turno delegado: mirar la pantalla y contestar en
      cinco frases): 379 mensajes, **98 sin hechos** y, de ellos, **78 deltas** del delegado, uno por ficha
      (50 `response.output_text.delta` y 28 `response.function_call_arguments.delta`). Cada uno era una
      línea de hasta 400 caracteres; con cinco turnos así, el anillo de 500 líneas del panel se lleva las
      `voz-turno`, los topes y las `llamada recibida`.
    - **Ahora** `Procesar` pregunta a `SeVuelcaCrudo`: no se vuelcan los `response.event` cuyo evento
      interior acaba en `.delta` ni los `session.output_audio.delta` (que solo llegan sin hechos cuando
      vienen vacíos). Quedan **20 de 98** en ese turno: `response.created/in_progress/completed`,
      `output_item.*`, `content_part.*`, `*.done`, `session.started`, `session.delegation.created` y
      `session.usage.updated`. Con `U_VOZ=realtime` no cambia nada: sus deltas no vienen en `response.event`.
    - Lo que queda sin hacer y la revisión proponía: callar también los mensajes de uso y volcar los tipos
      desconocidos una vez por tipo. No entra: la regla que se pidió es solo la de los deltas.
11. **Un resultado de más de 32.768 bytes llega recortado al delegado (la 49)**, con la cola
    `…[recortado: N de M bytes]`. Mandarlo entero dejaba la llamada pendiente y la sesión sin delegado. Lo que
    queda fuera no lo ve; cuánto ocupa `map_what_i_see` (hasta 220 líneas) con etiquetas reales, sin medir.
12. **«Te escucho.» y «Sigo» esperan a `session.started`** (0,8–1,2 s después de mandar `session.start` en las
    sondas de la revisión). Si la sesión no abre, se dice la causa una vez y la voz se cierra, sin reintentar.

## La vuelta atrás: `U_VOZ=realtime`

Una variable de entorno, sin recompilar. Se lee al construir la conversación, así que exige reiniciar
`U.exe`:

```powershell
setx U_VOZ realtime          # para siempre, en este usuario; reiniciar U.exe
$env:U_VOZ = 'realtime'      # solo para lo que se arranque desde esta consola
```

Con ella la voz es la de `main` de hoy (`gpt-realtime-2.1-mini`), con dos diferencias que son arreglos:
lo escrito pide respuesta (208) y la transcripción es `gpt-transcribe` (D). La voz prestada (192), el
dictado fuera de la conversación (142) y las marcas de turno del servidor vuelven a ser las de siempre.
El log de apertura dice con qué modelo abrió: `sesión abierta con «…» (…)`. **Quitar la variable
devuelve GPT-Live.**

## El nivel 4

El kit de la spec 017 (`scripts/nivel4-voz/`), repetido **dos veces con el mismo binario**: con GPT-Live
y con `U_VOZ=realtime`. Mismas tareas (T1, T3, T4, T5), tres repeticiones, el plan como denominador, y
alguien delante (OLED Care). Lo que tiene que decir, además de la tabla:

- **Que la voz contestó a lo escrito**: `llamada recibida` después de cada Enter, sin que el micrófono
  oiga nada. Es el cableado de la 208.
- **Que los turnos se cerraron**: con GPT-Live, una línea `Ü dijo:` y una `voz-turno` por petición. Es
  el cableado de la 209.
- **Con qué modelo abrió cada corrida**, copiado del log.
- **Que no aprobó lo que resolvió otro camino**: con el juez arreglado (E), `Acciones = 0` no aprueba.

## Lo que NO entra

- **`mac-client/Sources/U/VivoOpenAI.swift:181`**, que también pide `gpt-4o-mini-transcribe`. Se
  apaga el 2027-02-26 igual. No compila en esta máquina (`.claude/rules/solo-mac.md`): lo abre quien
  tenga el lado Mac, en su rama.
- **La voz prestada con GPT-Live**: no hay forma, medido. Si hace falta con GPT-Live por defecto, la
  salida es cerrar la voz o volver a Realtime durante la comprobación: otra spec.
- **Traducir `session.usage.updated` a coste.**
- **Callar al hablarle encima con GPT-Live** más allá de Escape.
- **`FaceWindow.xaml.cs`**: zona de choque de los tres. La 208 arregla sus dos llamadas desde
  `ConversacionEnVivo`.
- **Las tareas largas (R8–R11 de la spec 017)**: siguen fuera.

## Hallazgos

- **2026-09-12 (el juez del nivel 4 deja de aprobar lo que la voz no tocó — E, hecho).**
  - **La autoprueba, primero.** `autoprueba.py` ganó el caso T3 r2: la sesión de voz se cierra, dos
    `agent: tap` y el estado final bueno.
  - **Rojo con el juez viejo, por las razones escritas:**
    ```
    FALLO T3 r2 NO aprobada: estado final bueno con cero acciones de voz (lo resolvió otro camino)
    FALLO la tabla dice cuántas llegaron al estado final sin la voz
    FALLO el denominador es el plan
    ```
    La tercera cae porque T3 r2 aprobada convierte «2 de 8» en «3 de 8».
  - **Verde con el arreglo, 12 de 12.** `a["acciones"] > 0` en la aprobación, y una línea que cuenta
    las que llegaron al estado final sin una sola acción de voz. Mismo cambio en el README.
  - **Sabotaje.** Se quitó ` and a["acciones"] > 0`. Por diff de bytes contra la copia: una línea
    cambiada. La autoprueba volvió a fallar por T3 r2 y por el denominador. Restaurado idéntico, con el
    mismo SHA-256; tras restaurar, 0 fallos. Commit `fcd5751`.
- **2026-09-12 (el saludo de la presentación tenía el agujero de «Escríbele…»).** Contando los sitios
  de la clase: `EnviarTextoAsync` lo llaman también `FaceWindow.xaml.cs:2532` (el saludo) y
  `:2300` (lo escrito). Con Realtime, el saludo solo tenía respuesta si el micrófono oía algo. La 208
  arregla los dos desde `ConversacionEnVivo`.
- **2026-09-12 (la 43 no hace falta).** Ninguna promesa fija `gpt-4o-mini-transcribe` (0 apariciones en
  los dos contratos); aparece en 2 sitios de producción, uno de ellos del lado Mac.
- **2026-09-12 (dos promesas verdes que no dicen lo que parece).** La 192 y la 142 juzgan
  `ProtocoloOpenAI` por su nombre. Con GPT-Live por defecto siguen verdes y no se cumplen (ver «Límites»).
- **2026-09-12 (lo que el piloto espera oír depende del cierre sintetizado).** `PreguntarYEsperar`
  (`FaceWindow.xaml.cs:3110`) espera la frase del usuario en `DijoElUsuario`, que solo se emite al cerrar
  el turno. Con GPT-Live ese cierre lo pone la 209: una pausa de 1,5 s decide qué parte de la respuesta
  lee el piloto. Declarado arriba (degradación 2).
- **2026-09-12 (el traductor, fase 1 — de `jero/voz-gpt-live-protocolo`).**
  - **La forma del `session.update` está confirmada contra el servidor.** Con los bytes exactos que genera
    `ProtocoloGptLive` pasó esto:
    - `session.start` devolvió `session.started` y `session.update` devolvió `session.updated`.
    - Texto más `response.create` llevó al delegado a llamar `map_where_am_i`.
    - El dictado terminó con la frase literal.
  - **La sesión es inmutable salvo la delegación.** `session.update` con `session.instructions` da
    `unknown_parameter`. Un tipo de evento desconocido devuelve la lista de los que existen: `session.start`,
    `session.update`, `session.input_audio.append/mute/unmute`, `session.instructions/thinking/commentary.append`,
    `response.item.create`, `response.create` y `session.close`.
  - **La llamada delegada llega dos veces** dentro de `response.event`.
    - Primero llega `response.function_call_arguments.done`, sin `call_id` ni `name`.
    - Después llega `response.output_item.done` con el item completo.
    - Solo se traduce la segunda. La 41 lo congela: con las dos, cada herramienta se ejecutaría dos veces.
  - **Cerrar normal deja una línea de fallo.** `session.closed` llega también cuando cerramos nosotros
    (`close_requested`). Como es un `Falla`, el log dice «el servidor dice: sesión cerrada: close_requested».
    Solo se registra; no rompe nada.
  - **El hueco de la 161 con GPT-Live.** La regla de no anunciar vive en las instrucciones de Ü, que ahora
    van al delegado. La voz tiene su persona corta, y en la sonda dijo «Dame un momento para revisarlo».
    Arreglarlo necesita su propia promesa sobre `ProtocoloGptLive.InstruccionesDeLaVoz`.
- **2026-09-12 (sonda de huecos, 20 corridas contra `/v1/live/sessions`, sin tocar el repo).**
  - **`session.update` de la delegación.**
    - Las herramientas se reemplazan, no se suman.
    - Dentro de `responses` cada campo se fusiona: basta con mandar solo `model` para cambiarlo.
    - Funciona con una herramienta pendiente.
    - Estas formas fallan sin cerrar la sesión: sin `delegation.type`, `session.delegation.update`,
      `session.audio`, `session.model`, `delegation.type:"none"`, y cambiar a `client` (`immutable_field_update`).
  - **Un segundo `session.start`** da `invalid_value`, y la sesión sigue con la configuración de antes.
    Es lo que habría mandado `CambiarModoAsync` sin la 42.
  - **Hablarle encima.** No hay `response.cancelled` ni ningún evento: solo llega la transcripción.
    Lo demás está en «Lo que se pierde», punto 3.
  - **`response.create`.**
    - Sin nada pendiente no da error: abre una delegación y la voz habla.
    - Con una delegación en marcha se encola (punto 9).
  - **Instrucciones.** La voz tiene un tope de 16 384 tokens: con 62 000 caracteres da `error` y el socket
    muere a los ~2 s. Las de Ü (20 694 caracteres) caben unas 2,5 veces. El delegado aceptó 103 000.
  - **`session.usage.updated`** trae además `context_window.usage_ratio` cada ~15 s.
- **2026-09-12 (revisión de fidelidad al servidor: lo que no cabe y lo que no abrió — la 49).**
  - **Contra el servidor solo se pudo volver a comprobar el tercero.** La cuenta sigue sin crédito: el
    `session.start` real (43.507 B) contestó `credit_balance_exhausted` a 604 ms y el socket quedó `Aborted` a
    2.598 ms. Las fotos y el resultado de 40 KB no se pudieron volver a mandar; se dan por reales con la
    evidencia de la revisión (`fid\run-look.txt`, `run-look-520.txt`, `run-look2-400.txt`, `run-grande-40.txt`,
    `run-rondas.txt`), que mandó los bytes exactos de `d38f564`.
  - **La conversación real, antes del arreglo** (`sonda-conv` sobre el `U.dll` de `d38f564`, contra el servidor
    sin crédito):
    - «Te escucho.» a 1.580 ms y el error a 2.362 ms;
    - 4 reconexiones con el mismo `session.start`, cada una con su «Sigo, pero olvidé…»;
    - a 21,3 s, «Se me cortó la conexión y no consigo volver»;
    - la causa: 5 veces en el log y ninguna en voz.
  - **Después:** 0 reconexiones, 0 «Sigo», y a 4.464 ms «No pude abrir la voz en vivo. El servidor dice: You
    have no credits remaining…». Sin red, por `Procesar`: «Te escucho.» y «Sigo» salen con `session.started`,
    una vez, y un error después de confirmar no se toma por «no abrió» (6 de 6; con `d38f564`, la capacidad no existe).
  - **Por qué `ConfirmaQueAbrio` y `Hecho.Abierta`, y no un traductor con memoria.**
    - `IProtocolo` no tiene estado a propósito, y «antes de `session.started`» es un orden del flujo, no una forma
      del mensaje. El error de crédito no trae `client_event_id` ni nada que lo distinga de uno de mitad de sesión:
      `response_input_buffer_full` tampoco lo trae.
    - Por eso el traductor dice qué es abrir, y la conversación, que ya lleva el estado, decide qué hacer.
  - **Sitios con la clase de error, contados:**
    - 2 dan la sesión por lista antes de confirmarla: «Te escucho.» en `ArrancarAsync` y «Sigo…» en
      `ReconectarAsync`. Los 2 esperan ahora a `Hecho.Abierta`.
    - 2 mandan fotos: `map_look` y la foto del recuerdo nuevo. Los 2 preguntan `Mira`.
    - 2 arman `function_call_output`. El de GPT-Live recorta; el de Realtime (`ProtocoloOpenAI.Resultados`) no se
      tocó, porque es otro servidor y no se midió.
  - **El comentario de `IProtocolo.Mira` mentía.** Decía que con `Mira` falso la herramienta no se ofrece, y
    `ConversacionEnVivo` sí ofrece `map_look`. Ya dice lo que hace el código.
  - **Sin medir:**
    - que pase un resultado de 32.768 B exactos;
    - qué pasa al llegar a los «128 items» por sesión (una sesión larga con muchas llamadas podría llenarlo, y nada lo vigila);
    - el camino feliz de «Te escucho.» contra el servidor (se comprobó por `Procesar`, sin red).
- **2026-09-12 (el cableado — fases 2 a 4).**
  - **Integrar las dos ramas no dio ningún conflicto:** una tocaba `voz/` y la otra `docs/` y `scripts/`.
  - **Decisiones tomadas al implementar**, todas juzgadas en la 210 o dichas en el log:
    - `U_VOZ` se normaliza en un solo sitio (`VozPedida`: sin espacios y en minúscula).
    - Un valor desconocido abre GPT-Live y deja una línea `voz-viva`.
  - **Dónde se evalúan los turnos.** `TurnosSinMarca` se evalúa también con los mensajes que no traen
    hechos, y se recrea en cada `ArrancarAsync`: lo dicho en una sesión no cierra un turno de la siguiente.
  - **El log de modo no finge.** «solo habla cuando se le pide» solo se anota si la voz sabe esperar.
    Si no sabe, se escribe que seguirá contestando sola.
  - **Un turno del usuario solo se abre tras un cierre**, como decía el diseño. Si el usuario repite o
    cambia el pedido encima de Ü sin 1,5 s de silencio en medio, el tope (204) y la medida (205) **no se
    reinician**. La otra regla («o tras hablar Ü») partiría una frase dicha encima de la voz en varios
    turnos, porque las transcripciones se entrelazan. Queda así hasta medirlo en el nivel 4.
    **Acotado el mismo día por la 212** (ver «los turnos, arreglados»): tras un cierre, lo que dice el
    usuario abre turno solo si Ü le había contestado. Lo de encima de Ü sin cierre sigue igual.
  - **`U_VOZ` significa otra cosa en `mac-client`**: allí fuerza una voz de macOS en el camino de texto
    (`mac-client/Sources/U/Voz.swift:52`). Los nombres coinciden y los clientes son distintos, así que no
    choca en el código. Pero una misma variable con dos sentidos confunde a quien lea los dos.
- **2026-09-12 (los turnos, arreglados — `jero/voz-gpt-live-turnos`, promesas 211 y 212, 209 acotada).**
  - **Qué fallaba** (revisa:regresiones): el cierre sintético saltaba con 1,5 s sin transcripción de nadie.
    Partía frases del usuario, cerraba a mitad de una herramienta y reiniciaba el tope de la 204 dentro de
    la misma petición. También partía lo que llega a la 105 y adelantaba «Ü dijo», la señal con la que
    `conducir.ps1` empieza a contar su quietud. Y la 209 no juzgaba cómo construye la app su marcador
    (revisa:contrato, G1 y G4 verdes).
  - **La sonda de los turnos** (`scratchpad/turnos/sonda-turnos.ps1`, fuera del repo). Tres corridas
    contra `/v1/live/sessions` con el delegado `gpt-5.6-luna`, dos herramientas (`map_open_app` y
    `map_look`) y una frase hablada con pausas (voz Helena, 24 kHz). Las herramientas contestan a los 2500 ms
    (r1, r3) o a los 300 ms (r2). Cada corrida guarda la línea de tiempo —transcripción de los dos, audio con
    voz por su pico, llamadas y devoluciones— y **reproduce la regla de cierre sobre esa línea real** con
    cuatro silencios.
  - **Lo que midió:**
    - **El audio llega a ritmo real, también mientras habla:** un delta de 100 ms cada ~100 ms, así que
      lo que llega es lo que suena. El silencio pica en 45. La voz, en 1152-10 944 en el núcleo de cada
      palabra, y sus colas bajan a 184-508 (207 deltas, `silencio-pico-1`).
    - **La transcripción de Ü va por delante de su voz:** la última palabra llega 650-750 ms antes de que calle.
    - **Entre devolver una herramienta y lo siguiente que dice Ü: 1658, 1690, 1705 y 1707 ms** (4 de 4). Entre
      devolver y la llamada siguiente del delegado: 902-1380 ms.
    - **Pausas del usuario.** Con ~0,9-1,1 s de voz callada, el hueco de transcripción es de 1065-1138 ms.
      Con ~1,9 s, de 1868 y 2119 ms. La transcripción del usuario empieza 1,0-1,2 s después de su voz.
  - **La regla reproducida** (cierres en r1 / r2 / r3; en cada corrida solo el último es el bueno):

    | Regla | r1 | r2 | r3 | A mitad de tarea |
    |---|---|---|---|---|
    | la de la rama antes de esto: 1500 ms, solo lo dicho | 2 antes del final | 1 | 1 | 4 |
    | con las guardas (llamada en curso, voz que suena) y 1500 ms | 3 | 2 | 2 | 4 |
    | con las guardas y **2000 ms** | 1 | 1 | 1 | **0** |
    | con las guardas y 2500 o 3000 ms | 1 | 1 | 1 | 0 |

  - **Decisiones:**
    - **2000 ms, no 2500.** Es el menor que deja cero cierres a mitad de tarea en las tres corridas. El
      margen son 293 ms sobre cuatro muestras de una tarde, y lo confirma o lo corrige el nivel 4. Cada 500 ms
      más retrasan `Cerro`, «Ü dijo» y `DijoElUsuario`, y no la voz, que contesta sola. Con 2500 se cubriría
      también la pausa de 1,9 s (2119 ms), pero ya no reinicia el tope (212).
    - **La voz se juzga por su pico, umbral 1000**, 22 veces el ruido medido y por debajo de toda palabra.
      Solo alarga una actividad que ya había: sonido sin nada dicho no abre un turno.
    - **Una llamada en curso se sigue por referencia, no por `call_id`** (que puede venir vacío). Una
      devuelta antes de oírse se recuerda, porque el hilo que ejecuta puede ganarle al que recibe. La
      devolución va en el `finally` de `EjecutarAsync`: sale igual contestada, retirada, frenada por el tope
      o reventada.
    - **Una petición nueva la decide si Ü contestó, no si hubo cierre.** Tras un cierre, lo que dice el
      usuario abre turno solo si Ü habló después de lo último suyo. No sirve «desde el último cierre»: con
      GPT-Live lo que contesta Ü cae **dentro** del mismo turno sintético, que solo se cierra cuando callan
      los dos (3 de 3 en la sonda).
    - **La 209 cambia solo el reloj.** `_relojDeLosTurnos` es un campo, y `EmpezarLosTurnosDeLaSesion`
      recrea el marcador en el constructor y en `ArrancarAsync`.
  - **Sitios contados:**
    - `new TurnosSinMarca(`: 1, en `NuevoMarcadorDeTurnos`, que se llama desde 2 sitios.
    - Una llamada se ejecuta por una sola entrada: `Reaccionar` → `EjecutarAsync` → `EjecutarNucleoAsync`.
  - **Sin medir:**
    - Nada de esto se ha visto con `U.exe` y una persona.
    - Una herramienta que no vuelve nunca deja el turno abierto para siempre. No hay tope, a propósito:
      el delegado también la estaría esperando, y cerrar el turno no arregla eso.
    - Tras un Escape (`Interrumpir`) la voz se calla en la cola, pero los deltas con voz que siguen
      llegando todavía sujetan el turno hasta 2 s.
- **2026-09-12 (lo escrito, juzgado de verdad, y las llamadas en paralelo — rama `jero/voz-gpt-live-texto`).**
  - **La 208 ya juzga su cableado.** Hasta aquí juzgaba solo `MensajesDeTexto`, y W208 lo demostraba. La
    conversación deja sustituir su puerta de salida (`_puerta`) y el «hay socket» (`_puertaAbierta`), las
    dos nulas en la app. No se llama `_salida` porque ese nombre ya es el contador de fichas de salida (el
    primer intento dio `CS0102`). El enunciado de la 208 no cambia: por fin juzga lo que dice.
  - **Sitios con la guarda del socket que la 208 necesitaba: 2** (`EnviarTextoAsync`,
    `EnviarTextoAlModeloAsync`). `DiEstoAsync`, `CambiarModoAsync` y `MandarFotoAsync` conservan la suya; el
    cableado del cambio de modo (WModo) sigue sin juez.
  - **Varias llamadas a la vez (promesa 214).** GPT-Live entrega cada `function_call` en su propio
    `response.event`, y la conversación contestaba cada `Hecho.Pide` con su propio `response.create`.
    Con `sonda-paralelo.ps1`, en las mismas condiciones:
    - como lo hacía la conversación: 2 llamadas a **52 ms** una de otra y **1 error**
      `function_call_outputs_required` («Missing function call outputs for: call_…»); la voz contestó igual
      tras el segundo;
    - una copia que contesta como el arreglo (`sonda-paralelo-un-turno.ps1`): 2 llamadas a **77 ms**, **un**
      `response.create` detrás de las dos salidas, **0 errores**, y la voz dijo los datos de las dos.
  - **El sobre de la llamada trae `delegation_id`, no el id de la respuesta**, así que la conversación no
    puede agrupar por respuesta: agrupa por lo que falta por contestar en la sesión.
  - **Se pregunta `MarcaLosTurnos`** porque hoy van juntas las dos cosas: GPT-Live es el único protocolo que
    ni marca turnos ni entrega las llamadas en tanda. Con Realtime no se anota nada. Si un protocolo las
    separa, esto pide su propio miembro en `IProtocolo`.
  - **Una carrera que queda, sin cerrar.** Si una herramienta termina antes de que llegue la llamada siguiente
    de la misma tanda (52–77 ms), el turno sale con una sola salida y el servidor contesta como antes: nunca
    peor que antes. Cerrarla del todo pide leer `response.completed`, que el traductor no da (la 41 lo prohíbe
    como cierre de turno, con razón).
- **2026-09-12 (qué voz abre y el log — `jero/voz-gpt-live-seleccion`, tras las revisiones).**
  - **La 210 certificaba menos de lo que decía.** Miraba el nombre del tipo. Dos sabotajes de la revisión
    «contrato» la dejaban verde:
    - **G2**: `ProtocoloPorDefecto` abre `gpt-live-1-mini`, un modelo que no existe. Repetido en esta rama
      con la 210 vieja: ✔ 210, **CONTRATO INTACTO, exit 0**.
    - **G3**: el aviso de un `U_VOZ` desconocido, con la condición invertida.
  - **Arreglo del juez, no del código.** El código ya abría lo correcto; faltaba quien lo mirara. La 210
    exige ahora modelo, delegado y dirección, y la línea del valor desconocido. El enunciado no cambia: «es
    GPT-Live» pasa a querer decir lo que abre. Con G2 y G3 aplicados, la 210 nueva sale roja (ver «Sabotajes»).
  - **La 217 nace de la revisión «regresiones»** (baja). La sonda de solo lectura de hoy midió un turno
    delegado: 98 líneas «←», 78 de ellas deltas (degradación 10). Se juzga sin socket: por `Procesar` y por
    la regla pura `SeVuelcaCrudo`.

## Sabotajes

Cada uno es un cambio de una línea hecho sobre el árbol ya commiteado.

- **Antes de juzgar**, se comprobó que el cambio estaba puesto: `git diff --numstat` = `1 1` y el SHA-256
  cambió. El sabotaje que no se aplicó el 2026-08-21 dio un verde falso.
- **Después**, se exigió la línea de veredicto del juez y se restauraron los bytes originales. En los 16
  casos el SHA-256 volvió a ser idéntico y el diff quedó vacío.
- **Al final**, se recompiló y se volvió a juzgar.

El guion vive fuera del repo: `sabotea.ps1` y `sabotajes.json`, en el scratchpad de la sesión.

**Cableado (2026-09-12, 16:23–16:32), `scripts\contrato-del-grafo.ps1`:**

| Id | Rotura | Veredicto |
|---|---|---|
| S208a | `MensajesDeTexto` deja de añadir la petición de turno | ✘ 208 (salió `conversation.item.create` / `response.item.create` solo). ROTO, exit 2 |
| S208b | `MensajesDeTexto` pide turno antes del texto | ✘ 208 (salió `response.create · conversation.item.create`). ROTO, exit 2 |
| S208c | `MensajesDeTexto` deja pasar el mensaje vacío | ✘ 208 («salieron 2»). ROTO, exit 1 |
| S209a | `TocaCerrar` cierra sin haber oído nada | ✘ 209 («sin nada oído no se cierra nunca…»). ROTO, exit 2 |
| S209b | cada trozo del usuario abre turno, no solo el primero | ✘ 209 («…y el segundo de la misma frase no»). ROTO, exit 1 |
| S209c | el audio continuo cuenta como actividad | ✘ 209: no cierra nunca, cinco comprobaciones rojas. ROTO, exit 5 |
| S209d | **cableado**: `Procesar` deja de consultar el marcador | ✘ 209 («cerró 0 y luego 0», «entregó 0», sin línea «por voz»). ROTO, exit 3 |
| S209e | **cableado**: la conversación nunca crea el marcador | ✘ 209 («con GPT-Live la conversación lleva su marcador…»). ROTO, exit 4 |
| S209f | **cableado**: el primer trozo no llama a `EmpiezaUnTurnoDelUsuario` | ✘ 209 («…línea voz-turno «por voz»»). ROTO, exit 1 |
| S209g | **cableado**: tocar cerrar no reacciona con `CierraElTurno` | ✘ 209 («cerró 0 y luego 0»). ROTO, exit 2 |
| S210a | el defecto vuelve a ser `ProtocoloOpenAI` | ✘ 210: sin variable, vacío, en blanco, desconocido y el constructor. ROTO, exit 5 |
| S210b | la variable se lee como `U_VOICE` | ✘ 210 («se preguntó: U_VOICE»). ROTO, exit 2 |
| S210c | la variable no se normaliza | ✘ 210 («escrito a mano, con mayúscula o espacios…»). ROTO, exit 1 |
| S210d | el constructor sin protocolo abre `ProtocoloOpenAI` | ✘ 210 («construida sin protocolo, sin U_VOZ…»). ROTO, exit 1 |
| **W208** | **cableado sin juez**: `EnviarTextoAsync` vuelve a mandar solo el texto | **✔ 208, 209, 210 · CONTRATO INTACTO, exit 0** |
| **WModo** | **cableado sin juez**: `CambiarModoAsync` vuelve a mandar la apertura (con GPT-Live, otro `session.start`) | **✔ 208, 209, 210 · CONTRATO INTACTO, exit 0** |

**Las dos últimas eran el límite, medido.** Los dos métodos salen antes de mandar nada si no hay un socket
abierto, así que ningún contrato llegaba a ellos. El de la voz tampoco: juzga `ProtocoloGptLive.CambioDeModo`,
no quién lo llama. **W208 dejó de serlo** con la puerta de salida (tabla siguiente); WModo sigue sin juez y lo
tiene que decir el nivel 4 (ver «Límites»).

**Lo escrito y las llamadas en paralelo (2026-09-12, rama `jero/voz-gpt-live-texto`, sobre `1a645ff`),
`scripts\contrato-del-grafo.ps1`.** Guion `sabotea-texto.ps1` y `sabotajes-texto.json`, en el scratchpad. Los
diez con `git diff --numstat` = `1 1` sobre `ConversacionEnVivo.cs`, SHA-256 `3D638397…` cambiado antes de
juzgar y el mismo `3D638397…` al restaurar; después, recompilado: **CONTRATO INTACTO** (180 ✔, 0 ✘, 0 ⧗) y
**VOZ ÍNTEGRA** (36 ✔, 0 ✘, 0 ⧗).

| Id | Rotura | Veredicto |
|---|---|---|
| W208 | **cableado**: `EnviarTextoAsync` vuelve a mandar solo el texto (la línea de `main`) | ✘ 208 con GPT Realtime y con GPT-Live («lo escrito (EnviarTextoAsync) sale… con el texto y DESPUÉS response.create»). ROTO, exit 2 |
| W208b | **cableado**: la nota al modelo manda solo el texto | ✘ 208 con los dos protocolos («la nota al modelo…»). ROTO, exit 2 |
| W208c | `EnviarAsync` solo usa la puerta si además hay socket | ✘ 208 ×4 y ✘ 214 ×6 (no sale nada). ROTO, exit 10 |
| W208d | lo escrito deja de mirar si hay por dónde | ✘ 208 («con la voz cerrada no sale nada… (salieron 2)»). ROTO, exit 1 |
| **W208e** | **sin juez, declarado**: en la app «hay por dónde» mira solo `Viva` y no el socket | **✔ 208 · CONTRATO INTACTO, exit 0** |
| S214a | **cableado**: al pedir no se anotan las llamadas | ✘ 214 («el turno se pide UNA vez, no una por llamada…»). ROTO, exit 1 |
| S214b | no se olvidan las llamadas de otra sesión | ✘ 214 («una llamada de una sesión anterior que nunca llegó a correr no retiene el turno…»). ROTO, exit 1 |
| S214c | se retiene el turno aunque no falte ninguna (`>= 0`) | ✘ 214 ×5, también la llamada sola y la tanda de Realtime. ROTO, exit 5 |
| S214d | la condición al revés: se anota con Realtime y no con GPT-Live | ✘ 214 («el turno se pide UNA vez…»). ROTO, exit 1 |
| **S214e** | **sin juez, declarado**: una llamada que revienta no se da por contestada (`finally` vacío) | **✔ 214 · CONTRATO INTACTO, exit 0** |

**Los dos verdes son el límite, medido.** W208e: el contrato no tiene socket ni voz viva, así que no distingue
«voz viva» de «voz viva y socket abierto». S214e: ninguna mano del contrato revienta a mitad de una tanda. Quedan
dichos aquí, y W208e también en «Límites»; el código no los nombra.

**Qué voz abre y el log (2026-09-12, `jero/voz-gpt-live-seleccion`), `scripts\contrato-del-grafo.ps1`.**
Con el mismo guion (diff `1 1`, SHA-256 cambiado, veredicto exigido, restaurado con el SHA idéntico).

Primero el hueco y el rojo de la 210 nueva, sobre el código sin tocar:

| Id | Rotura | Con la 210 vieja | Con la 210 nueva |
|---|---|---|---|
| G2 | `ProtocoloPorDefecto` abre `new ProtocoloGptLive("gpt-live-1-mini")` | **✔ 210 · CONTRATO INTACTO, exit 0** | ✘ 210 en 7 comprobaciones («abre … modelo gpt-live-1-mini …»). ROTO |
| G3 | el aviso de `U_VOZ` desconocido, con la condición invertida (`protocolo != null`) | **✔ 210 · INTACTO, exit 0** (medido por la revisión) | ✘ 210 («con U_VOZ=gemini el constructor deja UNA línea voz-viva que nombra «gemini»… (dejó 0: )»). ROTO |

Y la 217, antes de su código: ✘ en las tres comprobaciones de los deltas por `Procesar` («dejó 1») y
`PENDIENTE: «Voice.ConversacionEnVivo.SeVuelcaCrudo»`. ROTO, exit 4.

Después, sobre `5737413` (el arreglo commiteado), 10 sabotajes:

| Id | Rotura | Veredicto |
|---|---|---|
| G2 | el defecto abre `gpt-live-1-mini` | ✘ 210, 7 comprobaciones (sin variable, vacía, en blanco, `gpt-live`, `gemini` y el constructor dos veces). ROTO, exit 7 |
| S210e | el defecto abre GPT-Live con otro delegado (`gpt-5.6-terra`) | ✘ 210, las mismas 7 («… delegado gpt-5.6-terra …»). ROTO, exit 7 |
| S210f | `U_VOZ=realtime` abre Realtime con otro modelo (`gpt-realtime-mini`) | ✘ 210, 3 comprobaciones (`realtime`, ` Realtime ` y el constructor). ROTO, exit 3 |
| G3 | el aviso del valor desconocido invierte su condición | ✘ 210 («… nombra «gemini» … (dejó 0: )»). ROTO, exit 1 |
| S210g | el aviso sale siempre que no se pasa protocolo (`\|\|` en vez de `&&`) | ✘ 210 («sin U_VOZ no deja ninguna línea voz-viva sobre U_VOZ… (dejó 1…)» y lo mismo con `realtime`). ROTO, exit 2 |
| S217a | **cableado**: `Procesar` deja de consultar `SeVuelcaCrudo` | ✘ 217 por la puerta del socket: los dos deltas y el audio vacío («dejó 1»). ROTO, exit 3 |
| S217b | los deltas del delegado se vuelcan (`.deltas` en vez de `.delta`) | ✘ 217: los dos deltas, por `Procesar` y por la regla pura. ROTO, exit 4 |
| S217c | el audio vacío se vuelca | ✘ 217: el audio vacío, por `Procesar` y por la regla. ROTO, exit 2 |
| S217d | se callan todos los `response.event`, no solo los deltas | ✘ 217: `response.completed` y `function_call_arguments.done` dejan de dejar su línea («dejó 0»), por `Procesar` y por la regla. ROTO, exit 4 |
| S217e | se calla todo lo que acabe en `.delta`, venga o no dentro de `response.event` | ✘ 217 («y un .delta que no viene dentro de response.event sigue como estaba…»). ROTO, exit 1 |

En todos, las promesas que no se rompían siguieron verdes (✔ 217 con los de la 210, ✔ 210 con los de la
217), así que el rojo es de la rotura y no del arnés. **S217e solo lo ve la regla pura**: por `Procesar`
se juzga una conversación con GPT-Live, y un `.delta` de Realtime no pasa por ahí.

**Traductor (fase 1, `jero/voz-gpt-live-protocolo`), `scripts\contrato-de-la-voz.ps1`**, los seis
aplicados por diff y restaurados:

| Sabotaje | Veredicto |
|---|---|
| `Apertura` manda `session.update` en vez de `session.start` | ✘ 40 |
| `MarcaLosTurnos => true` | ✘ 41 |
| se lee `function_call_arguments.done` en vez de `output_item.done` | ✘ 41 |
| `CambioDeModo` manda `session.start` | ✘ 42 |
| el defecto de `SabeEsperarTurno` pasa a falso | ✘ 42 |
| vuelve `gpt-4o-mini-transcribe` | ✘ 43 |

**Juez del nivel 4 (E)**: se quitó ` and a["acciones"] > 0` y la autoprueba falló en T3 r2 y en el
denominador (ver Hallazgos).

**Los turnos (fase 6, `jero/voz-gpt-live-turnos`, 2026-09-12), `scripts\contrato-del-grafo.ps1` sobre
`79fcea2`.** El guion es `scratchpad/turnos/sabotea-turnos.ps1`, fuera del repo, y hace lo mismo para cada
rotura:
- comprueba que el patrón aparece **exactamente una vez**;
- la aplica sobre los bytes y exige `git diff --numstat` = `1 1` y un SHA-256 distinto;
- juzga y guarda las líneas de veredicto;
- restaura los bytes originales y exige **el mismo SHA-256 y un diff vacío**.

SHA de partida: `TurnosSinMarca.cs` 6812A4A7, `ConversacionEnVivo.cs` B2A0E400.

| Id | Rotura | Veredicto |
|---|---|---|
| T1 | el marcador no registra la llamada en curso | ✘ 211 («una llamada que pide el delegado queda en curso (en curso: 0)», y en la conversación «cerró 1» con la llamada ejecutándose). ROTO, exit 7 |
| T2 | tocar cerrar ignora las llamadas en curso | ✘ 211 («con una llamada a herramienta sin devolver no se cierra…», «…3000 ms sin decir nada el turno no se cierra (cerró 1)»). ROTO, exit 4 |
| T3 | devolver no cuenta como actividad | ✘ 211 («a 1000 ms de devolverla no se cierra…»). ROTO, exit 3 |
| T4 | la voz que suena no sujeta el turno (umbral inalcanzable) | ✘ 211 («mientras suena la voz de Ü no se cierra…»). ROTO, exit 2 |
| T5 | el audio en silencio cuenta como voz (umbral a 0) | ✘ 211 («el audio en silencio que manda el servidor (pico 45, medido) no retrasa el cierre», y cuatro más). ROTO, exit 5 |
| T6 | una llamada devuelta antes de oírse se queda en curso | ✘ 211 («…no queda en curso (en curso: 1)», «…en vez de quedarse abierto para siempre»). ROTO, exit 2 |
| T7 | **cableado**: `EjecutarAsync` no le devuelve la llamada al marcador | ✘ 211 («al terminar de ejecutarla, la conversación se la devuelve al marcador…», «cerró 0 y luego 0»). ROTO, exit 2 |
| T8 | cualquier cierre termina la petición (la regla de antes) | ✘ 212 («tras una pausa que cerró el turno…», y en la conversación «líneas 3»). ROTO, exit 4 |
| T9 | lo que dice Ü no cuenta como contestar | ✘ 212 («cuando Ü ya le contestó…», «líneas 1») y ✘ 209 («tras un cierre, lo siguiente que dice el usuario abre otro turno»). ROTO, exit 4 |
| T10 | que el usuario siga hablando no borra el «contestó» de Ü | ✘ 212 («si el usuario siguió hablando después de lo que dijo Ü…»). ROTO, exit 1 |
| **G1** | **construcción**: la app construye el marcador con 60 000 ms (el sabotaje de revisa:contrato que antes quedaba INTACTO) | ✘ 209 («con el silencio con que la construye la app: a 1700 ms no cierra y a 2100 ms cierra una vez…», «entregó 0»), ✘ 211 y ✘ 212 en la conversación. ROTO, exit 5 |
| G1b | el silencio por defecto vuelve a 1500 ms | ✘ 209 (seis en la regla y «…a 1700 ms no cierra…» en la conversación) y ✘ 211 («cerró 1» a 1500 ms de devolver). ROTO, exit 8 |
| G2 | **construcción**: el reloj de la app está parado (`() => 0`) | ✘ 209 («y lo construye con el reloj del sistema…»). ROTO, exit 1 |
| G3 | **construcción**: el marcador no usa el reloj de la conversación (`Environment.TickCount64` directo) | ✘ 209, ✘ 211 y ✘ 212 (en la conversación no cierra: «cerró 0», «líneas 1»). ROTO, exit 5 |
| **G4** | **construcción**: otra sesión reutiliza el marcador (`??=`, el otro sabotaje que antes quedaba INTACTO) | ✘ 209 («al empezar otra sesión el marcador es otro… (cerró 1)»). ROTO, exit 1 |
| G6 | hay marcador también con voces que marcan sus turnos | ✘ 209 («con una voz que marca sus turnos…» y «y con GPT Realtime… tampoco»). ROTO, exit 2 |
| S209f | **cableado**: el primer trozo no abre turno en la conversación (repetido tras el cambio) | ✘ 209 («…línea voz-turno «por voz»») y ✘ 212 («líneas 0»). ROTO, exit 3 |
| **W5** | **cableado sin juez**: `ArrancarAsync` deja de llamar a `EmpezarLosTurnosDeLaSesion` | **CONTRATO INTACTO, exit 0** |

Los 18 sabotajes se aplicaron con diff 1/1 y cambio de SHA, y se restauraron con el SHA idéntico y el diff vacío.
**W5 es el límite, medido:** `ArrancarAsync` necesita clave y socket, y el contrato no llega a él. La 209 juzga
`EmpezarLosTurnosDeLaSesion`, que es donde vivía el `??=` de G4, pero no que `ArrancarAsync` lo llame. Si se
rompe, el síntoma en `U.exe` es un «Ü dijo» o un `Cerro` fantasma al abrir la voz menos de 2 s después de
cerrarla con el usuario hablando.

**Revisión de fidelidad (la 49, 2026-09-12).** Todos los sabotajes se hicieron sobre `da7a6c1`, con el mismo
método de arriba: numstat `1 1` y cambio de SHA-256 antes de juzgar, y los bytes y el SHA de antes al
restaurar, en los 10. El guion es `sabotea-fidelidad.ps1`, en el scratchpad.

- Los del traductor se juzgan con `scripts\contrato-de-la-voz.ps1`.
- Los del cableado se juzgan con `scripts\contrato-del-grafo.ps1` y con `sonda-conv`:
  - **red**: la `ConversacionEnVivo` recompilada, contra el servidor sin crédito;
  - **local**: por `Procesar`, sin red.

| Id | Rotura | Veredicto |
|---|---|---|
| V49a | `Mira` vuelve a ser verdadero | ✘ 49 («no se declara capaz de mirar»). VOZ ROTA, exit 1 |
| V49b | `Resultados` manda el resultado entero | ✘ 49 («salió de 41066», la cola, los emojis «mensaje de 72105 B»). VOZ ROTA, exit 3 |
| V49c | el recorte puede partir un emoji | ✘ 49 («mensaje de 32766 B, 5437 char guardados»). VOZ ROTA, exit 1 |
| V49d | la cola dice que se mandó todo | ✘ 49 («…[recortado: 40960 de 40960 bytes]»). VOZ ROTA, exit 1 |
| V49e | la apertura se lee en `session.updated` | ✘ 49 (dos comprobaciones). VOZ ROTA, exit 2 |
| V49f | GPT-Live deja de declarar que confirma | ✘ 49. VOZ ROTA, exit 1 |
| **W49a** | **cableado**: un error antes de confirmar no se guarda | **CONTRATO INTACTO, exit 0** · red: MAL (4 reconexiones, causa 0) · local: MAL (1) |
| **W49b** | **cableado**: la conexión se da por confirmada al mandar la apertura | **CONTRATO INTACTO, exit 0** · red: MAL (4 «Sigo», 4 reconexiones, causa 0) · local: MAL (3) |
| **W49c** | **cableado**: al morir sin abrir se reconecta igual | **CONTRATO INTACTO, exit 0** · red: MAL (4 reconexiones, causa 0) · local: BIEN |
| **W49d** | **cableado**: al confirmar no se dice «Te escucho.» ni «Sigo» | **CONTRATO INTACTO, exit 0** · red: BIEN · local: MAL (2) |

**Las cuatro W son el límite, medido.** Ningún contrato ve el cableado de la 49 en la conversación.
- Cada rotura la ve una de las dos sondas: la red ve lo que pasa cuando no abre, y la local, lo que pasa cuando abre.
- Ninguna sonda las ve todas sola, y por eso van las dos.
- Las sondas no están en el repo ni corren en el portero.

## Cierre

- [x] Fase 0: medido contra el servidor real (2026-09-11/12), y el `session.update` de la delegación después
- [x] E: el juez del nivel 4 no aprueba sin acciones de voz; autoprueba en rojo, verde y saboteada
- [x] Promesas 40–43 escritas en `voz/Contrato/Contrato.cs`, en rojo (`18071b8`), y después verdes (**VOZ ÍNTEGRA**, 0 pendientes)
- [x] Promesas 208–210 escritas en `tests/ContratoDelGrafo/Contrato.cs`, en rojo (`b64972b`), y después verdes (**CONTRATO INTACTO**, 0 pendientes)
- [x] Cada una rota a propósito, comprobada por diff de bytes, restaurada y recompilada (ver «Sabotajes»; el cableado de 208 y del cambio de modo, medido sin juez)
- [x] Fase 6 (`jero/voz-gpt-live-turnos`): 211 y 212 escritas en rojo (`1497dc1`) y verdes (`79fcea2`); la 209 juzga el marcador que construye la app; el silencio de cierre, medido con la sonda de los turnos (2000 ms); todas saboteadas (ver «Sabotajes»)
- [ ] Nivel 4 de los turnos con `U.exe` y una persona: una línea «Ü dijo» por petición y ninguna a mitad de una herramienta, y una pausa a media frase sin segunda línea «turno nuevo (por voz)»
- [x] El cableado de la 208 con juez y la promesa 214 (llamadas en paralelo), en rojo (`731c60f`), verdes (`1a645ff`, **CONTRATO INTACTO**) y saboteadas (ver «Sabotajes», rama `jero/voz-gpt-live-texto`); el cambio de modo sigue sin juez
- [x] La sonda del `session.update` de la delegación, pegada en Hallazgos
- [x] Fase 6 (tras las revisiones): la 210 juzga lo que abre (modelo, delegado, dirección y el aviso del valor desconocido), roja con G2 y G3 aplicados; la 217 del log, roja antes de su código; las dos verdes y saboteadas (**CONTRATO INTACTO**, **VOZ ÍNTEGRA**, 0 pendientes)
- [x] Promesa 49 (revisión de fidelidad) en rojo (`21e9ecf`), en verde (`da7a6c1`) y saboteada: 6 roturas del traductor, rojas; 4 del cableado de la conversación, sin juez de contrato y rojas en `sonda-conv`
- [ ] Las fotos y el resultado de 40 KB, vueltos a medir contra el servidor cuando la cuenta tenga crédito, y el camino feliz de «Te escucho.» con `session.started` de verdad
- [ ] Nivel 4 con GPT-Live y con `U_VOZ=realtime`, con el juez arreglado, con horas y el modelo de cada corrida
- [ ] Estado de este documento: **implementado** (AAAA-MM-DD)
