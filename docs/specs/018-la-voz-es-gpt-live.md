# Plan de implementación: la voz es GPT-Live, y lo que se escribe se contesta

Estado: **en construcción**. A 2026-09-12: medido contra el servidor; el juez del nivel 4 arreglado y saboteado; las promesas 40–43 y 208–210 **en verde y saboteadas**. **Falta el nivel 4**: nada de esto se ha oído con micrófono ni con `U.exe` · Nace de dos peticiones del dueño (2026-09-11 y 2026-09-12) y de las sondas contra el servidor de esas dos noches · Rama: `jero/voz-gpt-live`, que integra `jero/voz-gpt-live-protocolo` (el traductor, 40–43) y `jero/voz-gpt-live-juez` (esta spec y el juez del nivel 4)

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
| Fotos | `input_text` + `input_image` (data URL JPEG) + `response.create` → el delegado **sí la ve** (dijo el color y el texto de la imagen). Una foto sola sin `response.create`: aceptada en silencio, sin error | ídem |
| Modo silencio | **no existe**: `session.instructions.append` «no hables por tu cuenta» no se respeta, la voz contestó sola; no hay `turn_detection` ni `create_response` | ídem |
| Marcas de turno | **ninguna**: ni `speech_started` ni `response.done`. Hablarle encima no produce ningún evento. `response.completed` cierra el trabajo del **delegado**, pero la voz sigue hablando segundos después; un turno solo de voz, sin delegar, no tiene ningún cierre | ídem |
| Consumo | `session.usage.updated {usage: {seconds}}`: duración, **no fichas** | ídem |
| Latencia, frase de 2,4 s, desde su fin | GPT-Live + luna: llama la herramienta a **1,0–1,4 s**, la voz empieza a **0,1–0,5 s**. `gpt-realtime-2.1-mini`: llama a **0,4–1,2 s**, la voz empieza a **0,4–1,1 s**, resultado→voz **0,46–0,61 s** | `sonda-rt-audio.ps1`, `sonda-live-eventos.ps1` |
| Realtime hoy, texto sin `response.create` | **cero eventos**: el modelo no contesta | `sonda-voz.ps1` |
| Transcripción en una sesión Realtime | `gpt-transcribe` y `gpt-live-transcribe` aceptados (`session.updated` sin error). `gpt-4o-mini-transcribe`, el de hoy, está deprecado y se apaga el 2027-02-26 | `sonda-voz.ps1` |
| `session.update` de la delegación a mitad de sesión | `{"type":"session.update","session":{"delegation":{"type":"responses","responses":{model, instructions, tools, tool_choice}}}}` → `session.updated` a ~150 ms, y en la frase siguiente el delegado llama la herramienta nueva. Las herramientas se **reemplazan**; dentro de `responses` cada campo se fusiona. Funciona con una herramienta pendiente. Un segundo `session.start` da `error` y la sesión sigue igual | sonda de la fase 1 y sonda de huecos, 09-12 |

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
  "gpt-5.6-luna")`, 24000/24000, `Mira => true` (el delegado ve las fotos), `SabeVolver => false`,
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
no se reciclan; los huecos están permitidos.

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
| 208 | grafo | escribir con la voz abierta pide respuesta: el texto va seguido de pedir turno, con cualquier protocolo. | 2 |
| 209 | grafo | con una voz que no marca los turnos, la conversación los marca: el primer trozo de lo que dice el usuario abre un turno y un silencio lo cierra. | 3 |
| 210 | grafo | la voz por defecto es GPT-Live y U_VOZ=realtime vuelve a GPT Realtime. | 4 |

**La que cierra el asunto** es la **210**: sin ella todo lo demás existe y la voz sigue siendo la de
antes. **La que cierra el agujero del nivel 4** es la **208**: sin ella ningún nivel 4 escrito mide la voz.

### Con qué se juzga cada una

Todas sin socket, sin micrófono, sin clave y sin pantalla. Las capacidades nuevas se piden por nombre,
con reflexión, y nacen en rojo con `Pendiente(…, "018")`. Los mensajes del servidor se le dan al
traductor tal como los mandó el servidor real en las sondas.

| # | Se juzga con | Sabotaje que la tiene que poner roja |
|---|---|---|
| 40 | `ProtocoloGptLive` por nombre en el ensamblado de `Voz.Realtime`. `Direccion()` es `wss://api.openai.com/v1/live/sessions` sin consulta; `Cabeceras` lleva `Authorization: Bearer`; 24000/24000; `Mira` sí, `SabeVolver`, `MarcaLosTurnos` y `SabeEsperarTurno` no. `Apertura("instrucciones de Ü", [map_look con un argumento], "")` da **un** mensaje: `session.start`, `session.model` `gpt-live-1`, `audio.format` `audio/pcm` a 24000, `audio.output.voice` `marin`; **sin `tools` en la sesión**; `delegation.type` `responses`, `delegation.responses.model` `gpt-5.6-luna`, sus `tools` con `map_look` y el argumento como `string`, `tool_choice` `auto`; las instrucciones completas van en el delegado y las de la voz son otras y más cortas. Y `ProtocoloOpenAI` sigue marcando los turnos y sabiendo esperar (los valores por defecto) | las herramientas viajan en la sesión y no en la delegación; la dirección vuelve a `/v1/realtime?model=`; el ritmo pasa a 16000; las instrucciones completas van a la voz |
| 41 | `Leer` con mensajes capturados: `session.output_audio.delta` da un `Suena` con el PCM exacto y un delta vacío no da nada; `session.output_transcript.delta` da `DiceU`; `session.input_transcript.delta` da `DiceElUsuario`; `response.event` con `response.output_item.done` de tipo `function_call` da **un** `Pide` con `call_id`, `name` y los argumentos parseados del texto JSON; ese mismo sobre con un item `message` no da nada, y `response.function_call_arguments.done` **tampoco** (la llamada es una); `error` da `Falla` con su `message`; `session.closed` da `Falla` con su `reason`. Y ninguno de los mensajes capturados —`session.started`, `session.delegation.created`, `response.completed`, `session.usage.updated`…— da `CierraElTurno` ni `HablaronEncima` | leer también `function_call_arguments.done` (dos `Pide` por una llamada: se ejecutaría dos veces); `response.completed` vuelve a ser `CierraElTurno`; los argumentos se pasan sin parsear |
| 42 | `Audio(pcm)` es `session.input_audio.append` con el base64 exacto; `Texto` es `response.item.create` con un message `user` e `input_text`; `Fotograma` igual con `input_image` y `data:image/jpeg;base64,`; `Resultados` con dos llamadas da dos `function_call_output` con su `call_id` y su `output`, y **ninguno** pide respuesta; `PedirRespuesta()` es `response.create`; `PedirRespuesta("X")` es `session.commentary.append` con `content` X y `delegation_id` nulo, y no un `response.create`; `CambioDeModo(otras, [otra herramienta], false)` no contiene `session.start`, es un `session.update` y lleva las instrucciones y la herramienta nuevas en la delegación | `CambioDeModo` devuelve la apertura (otro `session.start`); `Resultados` pide respuesta dentro; dictar vuelve a ser `response.create`; el texto sale como `session.instructions.append` (medido: no provoca respuesta) |
| 208 | `ConversacionEnVivo.MensajesDeTexto` por nombre. Con `ProtocoloOpenAI`: dos mensajes, **en ese orden**, `conversation.item.create` con el texto y `response.create`. Con `ProtocoloGptLive`: `response.item.create` con el texto y `response.create`. Con un protocolo falso del propio contrato cuyo `PedirRespuesta` es vacío: un solo mensaje, ninguno vacío | `MensajesDeTexto` deja de añadir la petición de turno; la petición va antes que el texto; se cuela el mensaje vacío |
| 209 | `TurnosSinMarca` por nombre, con reloj inyectado y la secuencia como hechos con su hora: el primer trozo de lo que dice el usuario abre turno y el segundo no; a 1000 ms del último trozo no toca cerrar y a 1500 sí, **una vez**; lo que dice Ü también mantiene el turno abierto y también se cierra por silencio; el audio que llega continuo **no** cuenta como actividad; sin nada oído no se cierra nunca; un silencio configurado de 500 ms se respeta. Y la 40 ya juzga que `ProtocoloGptLive` no marca los turnos y `ProtocoloOpenAI` sí. **Y su uso**, añadido al implementar: `Procesar`, la puerta por la que entra lo del socket, recibe en una `ConversacionEnVivo` con `ProtocoloGptLive` un `session.input_transcript.delta` y dos mensajes sin hechos. A 800 ms no emite `Cerro`; a 1600 ms lo emite una vez, con la línea `voz-turno` «por voz» y `DijoElUsuario` con la frase. Con un protocolo que marca sus turnos no hay marcador ni cierre | se cierra sin haber oído nada; cada trozo abre turno; el audio cuenta como actividad; `Procesar` deja de consultar el marcador; la conversación no lo crea; el primer trozo no llama a `EmpiezaUnTurnoDelUsuario`; tocar cerrar no reacciona |
| 210 | `ConversacionEnVivo.ProtocoloPorDefecto` por nombre, con una variable falsa que anota qué nombre se le pregunta: sin `U_VOZ`, con `U_VOZ` vacío o en blanco, y con `gpt-live`, sale `ProtocoloGptLive`; con `realtime`, `ProtocoloOpenAI`; y el nombre preguntado es `U_VOZ`. ` Realtime `, con mayúscula y espacios, también vuelve a Realtime, y un valor desconocido (`gemini`) abre GPT-Live. **El constructor se puede llamar sin pantalla**, porque `LiveAudio` nace sin dispositivo, así que también se juzga: sin protocolo y sin `U_VOZ` abre `ProtocoloGptLive`, y con `U_VOZ=realtime` abre `ProtocoloOpenAI` | el defecto vuelve a ser `ProtocoloOpenAI`; la variable se lee con otro nombre; no se normaliza; el constructor no usa el defecto |

### Límites dichos, no escondidos

- **La forma del `session.update` de la delegación ya está medida** (ver Hallazgos). Coincide con la que
  manda `ProtocoloGptLive.CambioDeModo`, así que la 42 no hubo que ajustarla.
- **La 209 juzga su cableado; la 208, no.**
  - La 209 entra por `Procesar`, que no necesita socket, y cada sabotaje de su cableado la pone roja.
  - `EnviarTextoAsync` y `CambiarModoAsync` salen sin mandar nada si no hay un socket abierto, y el contrato
    no llega a ellos. Romper la llamada a `MensajesDeTexto` dentro de `EnviarTextoAsync`, o volver a mandar
    la apertura al cambiar de modo, **deja el contrato INTACTO** (medido; ver la tabla de sabotajes).
  - Ese cableado lo tiene que decir el log del nivel 4: `llamada recibida` tras un texto escrito sin que
    el micrófono oiga nada, y tras 🎓 `modo cambiado` sin un `error` de `session.start`. El juez del nivel 4
    ya no aprueba sin acciones de voz (E), así que un cableado roto de la 208 sale como suspenso.
- **La 192 y la 142 siguen verdes, y con GPT-Live por defecto no se cumplen.** Las dos juzgan
  `ProtocoloOpenAI` por su nombre (`Contrato.cs`, `LaVozDeLaComprobacionEsPrestada` y
  `LaVozDeLaComprobacionEsLaDeU`), no el protocolo que abre la voz. Es un guardia que se cree puesto
  (aprendizaje nº18), y se dice aquí para que nadie lea su verde como «la voz prestada funciona».
- **Un valor desconocido** (`U_VOZ=gemini`) abre la voz por defecto, GPT-Live, y el constructor deja una
  línea `voz-viva` que lo dice. Se decidió al implementar y lo juzga la 210.

## Las fases

Una spec, una rama por encargo, una fase por commit. El portero exige los dos contratos intactos para
empujar: el rojo de cada fase se comprueba en local y se anota en su commit.

| Fase | Promesa | Qué toca | Terminado cuando |
|---|---|---|---|
| **0** | — (medir) | nada | la tabla de arriba — **hecha**, con el `session.update` de la delegación medido en la fase 1 |
| **1** | 40, 41, 42 | `voz/Realtime/IProtocolo.cs` (tres miembros con defecto), nuevo `voz/Realtime/ProtocoloGptLive.cs`; una sonda del `session.update` | **hecha**: rojo en `18071b8`, verde en `8fed7ae`, **VOZ ÍNTEGRA** |
| **2–4** | 208, 209, 210 | `Voice/ConversacionEnVivo.cs` (`MensajesDeTexto`, `ProtocoloPorDefecto`, constructor, recepción, `CambiarModoAsync`), nuevo `Voice/TurnosSinMarca.cs` | **hecha** en un solo paso, porque las tres tocan la misma clase: rojo en `b64972b`, verde en el commit siguiente, **CONTRATO INTACTO** con 0 pendientes |
| **5** | 43 | `ProtocoloOpenAI`: `gpt-transcribe` (D) | **hecha** con la fase 1. Con `U_VOZ=realtime`, que una sesión abra sin `error` es del nivel 4 |
| **E** | — (el juez) | `scripts/nivel4-voz/analizar.py` y `autoprueba.py` | **hecha** en esta rama: rojo, verde y sabotaje (ver Hallazgos) |

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
   - El servidor sigue decidiendo cuándo contestar. El silencio de 1,5 s decide solo cuándo la app da el
     turno por cerrado: `TurnoCerrado`, `Cerro`, «Ü dijo», la frase entregada a quien aprende
     (`DijoElUsuario`, promesa 105) y la respuesta que espera el piloto (`PreguntarYEsperar`).
   - Una pausa de más de 1,5 s a media frase la parte en dos: el piloto se quedaría con la primera mitad
     como respuesta. Es la avería que `semantic_vad` evitaba en Realtime («cortar a media frase o no
     contestar nunca»), trasladada a la contabilidad de la app. **Sin medir con voz real.**
   - El turno del usuario empieza con el primer trozo de **transcripción**, que llega después de que
     empezó a hablar. El tope de la 204 se reinicia más tarde que con `speech_started`.
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
8. **Una foto suelta tras un resultado, sin medir en esa forma.** Lo medido es una foto con texto y
   `response.create` detrás. `ConversacionEnVivo` manda las fotos después de una tanda de herramientas;
   que el delegado las vea así lo dice el nivel 4.
9. **Escribir con una delegación en marcha abre un turno de más.**
   - Un `response.create` con una delegación corriendo no da error: **se encola**, medido en la sonda de huecos.
   - 150 ms después de terminar la anterior abre otra delegación entera, que en la sonda volvió a llamar `map_look`.
   - La 208 manda un `response.create` detrás de cada texto escrito, así que escribir mientras Ü trabaja
     cuesta un turno completo más. Sin medir con la app.
10. **El log de «mensajes sin hechos» crece con GPT-Live.**
    - `Procesar` escribe `← …` por cada mensaje que no traduce. Con GPT-Live eso incluye cada `response.event` del delegado.
    - Entre esos eventos hay deltas de texto y de argumentos, que llegan uno por ficha.
    - Es anterior a esta spec y no se tocó. Sin medir cuántas líneas deja una sesión real.

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
  - **`U_VOZ` significa otra cosa en `mac-client`**: allí fuerza una voz de macOS en el camino de texto
    (`mac-client/Sources/U/Voz.swift:52`). Los nombres coinciden y los clientes son distintos, así que no
    choca en el código. Pero una misma variable con dos sentidos confunde a quien lea los dos.

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

**Las dos últimas son el límite, medido.** Los dos métodos salen antes de mandar nada si no hay un socket
abierto, así que ningún contrato llega a ellos. El de la voz tampoco: juzga `ProtocoloGptLive.CambioDeModo`,
no quién lo llama. Que ese cableado está bien lo tiene que decir el nivel 4 (ver «Límites»).

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

## Cierre

- [x] Fase 0: medido contra el servidor real (2026-09-11/12), y el `session.update` de la delegación después
- [x] E: el juez del nivel 4 no aprueba sin acciones de voz; autoprueba en rojo, verde y saboteada
- [x] Promesas 40–43 escritas en `voz/Contrato/Contrato.cs`, en rojo (`18071b8`), y después verdes (**VOZ ÍNTEGRA**, 0 pendientes)
- [x] Promesas 208–210 escritas en `tests/ContratoDelGrafo/Contrato.cs`, en rojo (`b64972b`), y después verdes (**CONTRATO INTACTO**, 0 pendientes)
- [x] Cada una rota a propósito, comprobada por diff de bytes, restaurada y recompilada (ver «Sabotajes»; el cableado de 208 y del cambio de modo, medido sin juez)
- [x] La sonda del `session.update` de la delegación, pegada en Hallazgos
- [ ] Nivel 4 con GPT-Live y con `U_VOZ=realtime`, con el juez arreglado, con horas y el modelo de cada corrida
- [ ] Estado de este documento: **implementado** (AAAA-MM-DD)
