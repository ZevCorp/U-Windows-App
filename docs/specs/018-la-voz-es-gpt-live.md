# Plan de implementación: la voz es GPT-Live, y lo que se escribe se contesta

Estado: **en construcción** (2026-09-12: medido contra el servidor; el juez del nivel 4 arreglado y saboteado; las promesas 40–42 y 208–210 escritas aquí, **sin código de producción en esta rama**) · Nace de dos peticiones del dueño (2026-09-11 y 2026-09-12) y de las sondas contra el servidor de esas dos noches · Rama: `jero/voz-gpt-live-juez` (esta spec y el juez del nivel 4)

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
| `session.update` de la delegación a mitad de sesión | **sin medir** | — |

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

**D no necesita la 43.** Ninguna promesa fija el modelo de transcripción antiguo: `gpt-4o-mini-transcribe`
aparece en 0 sitios de `voz/Contrato/Contrato.cs` y de `tests/ContratoDelGrafo/Contrato.cs`. Lo que vale
de ese cambio —que el servidor lo acepta— lo midió la sonda y el contrato no lo puede medir sin red. La
43 queda sin usar.

| # | Contrato | Promesa | Fase |
|---|---|---|---|
| 40 | voz | GPT-Live abre por su propio endpoint con session.start: modelo gpt-live-1, voz marin, PCM 24 kHz, y las herramientas de Ü van en la delegación con su modelo delegado, no en la voz. | 1 |
| 41 | voz | GPT-Live traduce lo que manda el servidor: audio, lo que dice Ü, lo que dice el usuario, una llamada delegada con sus argumentos y un error; y no inventa marcas de turno que el servidor no manda. | 1 |
| 42 | voz | GPT-Live manda lo de Ü con sus eventos: micrófono, texto y foto como mensajes de usuario, resultados como function_call_output, pedir turno como response.create, dictar como commentary; y cambiar de modo a mitad de sesión es un session.update de la delegación, no otro session.start. | 1 |
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
| 209 | `TurnosSinMarca` por nombre, con reloj inyectado y la secuencia como hechos con su hora: el primer trozo de lo que dice el usuario abre turno y el segundo no; a 1000 ms del último trozo no toca cerrar y a 1500 sí, **una vez**; lo que dice Ü también mantiene el turno abierto y también se cierra por silencio; el audio que llega continuo **no** cuenta como actividad; sin nada oído no se cierra nunca; un silencio configurado de 500 ms se respeta. Y la 40 ya juzga que `ProtocoloGptLive` no marca los turnos y `ProtocoloOpenAI` sí | el silencio se cuenta desde el primer trozo y no desde el último; el audio reinicia el silencio (con audio continuo, nunca cierra); se cierra sin haber oído nada |
| 210 | `ConversacionEnVivo.ProtocoloPorDefecto` por nombre, con una variable falsa que anota qué nombre se le pregunta: sin `U_VOZ`, con `U_VOZ` vacío o en blanco, y con `gpt-live`, sale `ProtocoloGptLive`; con `realtime`, `ProtocoloOpenAI`; y el nombre preguntado es `U_VOZ`. Si el constructor se puede llamar sin pantalla, se juzga también que sin protocolo usa ese defecto; si no, se dice | el defecto vuelve a ser `ProtocoloOpenAI`; la variable se lee con otro nombre; el blanco cuenta como un valor (patrón nº9) |

### Límites dichos, no escondidos

- **La forma del `session.update` de la delegación está sin medir.** La 42 juzga lo que es seguro —no es
  otro `session.start` y lleva la delegación nueva—; si la sonda de la fase 1 dice otra forma, manda la
  sonda y la 42 se ajusta antes del código.
- **La 208 y la 209 juzgan la regla, no su cableado** (como la 204 y la 205 de la spec 017). Que
  `EnviarTextoAsync` mande `MensajesDeTexto`, y que la recepción consulte `TurnosSinMarca` solo cuando el
  protocolo no marca, lo dicen las líneas del log en el nivel 4: `llamada recibida` tras un texto escrito,
  y `Ü dijo:` / `voz-turno` con GPT-Live. El juez del nivel 4 ya no aprueba sin acciones de voz (E), así
  que un cableado roto de la 208 sale como suspenso y no como aprobado por otro camino.
- **La 192 y la 142 siguen verdes, y con GPT-Live por defecto no se cumplen.** Las dos juzgan
  `ProtocoloOpenAI` por su nombre (`Contrato.cs`, `LaVozDeLaComprobacionEsPrestada` y
  `LaVozDeLaComprobacionEsLaDeU`), no el protocolo que abre la voz. Es un guardia que se cree puesto
  (aprendizaje nº18), y se dice aquí para que nadie lea su verde como «la voz prestada funciona».
- **La 210 no decide qué pasa con un valor desconocido** (`U_VOZ=gemini`): el diseño no lo cubre. Lo que
  haga el código se anota en Hallazgos; el log de apertura dice el modelo, así que al menos se ve.

## Las fases

Una spec, una rama por encargo, una fase por commit. El portero exige los dos contratos intactos para
empujar: el rojo de cada fase se comprueba en local y se anota en su commit.

| Fase | Promesa | Qué toca | Terminado cuando |
|---|---|---|---|
| **0** | — (medir) | nada | la tabla de arriba — **hecha**, salvo el `session.update` de la delegación |
| **1** | 40, 41, 42 | `voz/Realtime/IProtocolo.cs` (tres miembros con defecto), nuevo `voz/Realtime/ProtocoloGptLive.cs`; una sonda del `session.update` | las tres verdes, **VOZ ÍNTEGRA** con 8–10 y el resto intactas, y la sonda pegada en Hallazgos |
| **2** | 208 | `Voice/ConversacionEnVivo.cs` (`MensajesDeTexto`, `EnviarTextoAsync`) | 208 verde; siguen verdes 138, 141, 142, 161 y 192 |
| **3** | 209 | nuevo `Voice/TurnosSinMarca.cs`; su uso en la recepción de `ConversacionEnVivo` | 209 verde; 204 y 205 intactas |
| **4** | 210 | `ConversacionEnVivo` (`ProtocoloPorDefecto`, constructor, `CambiarModoAsync` con `CambioDeModo` y la línea de «no sabe esperar turno») | 210 verde; **CONTRATO INTACTO** |
| **5** | — | `ProtocoloOpenAI`: `gpt-transcribe` (D) | los dos contratos intactos; con `U_VOZ=realtime`, una sesión abre sin `error` |
| **E** | — (el juez) | `scripts/nivel4-voz/analizar.py` y `autoprueba.py` | **hecha** en esta rama: rojo, verde y sabotaje (ver Hallazgos) |

**Sitios con la clase de error, contados con `grep`:**

- **Texto sin pedir turno.** 2 sitios de `ConversacionEnVivo` mandan `_protocolo.Texto(…)`:
  `EnviarTextoAlModeloAsync` ya pide respuesta y `EnviarTextoAsync` no.
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
   - Si el servidor corta su propia voz al oír encima: **sin medir**.
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

## Cierre

- [x] Fase 0: medido contra el servidor real (2026-09-11/12), salvo el `session.update` de la delegación
- [x] E: el juez del nivel 4 no aprueba sin acciones de voz; autoprueba en rojo, verde y saboteada
- [ ] Promesas 40–42 escritas en `voz/Contrato/Contrato.cs`, en rojo, y después verdes (**VOZ ÍNTEGRA**, 0 pendientes)
- [ ] Promesas 208–210 escritas en `tests/ContratoDelGrafo/Contrato.cs`, en rojo, y después verdes (**CONTRATO INTACTO**, 0 pendientes)
- [ ] Cada una rota a propósito, comprobada por diff de bytes, restaurada y recompilada
- [ ] La sonda del `session.update` de la delegación, pegada en Hallazgos
- [ ] Nivel 4 con GPT-Live y con `U_VOZ=realtime`, con el juez arreglado, con horas y el modelo de cada corrida
- [ ] Estado de este documento: **implementado** (AAAA-MM-DD)
