# Plan de implementación: la lección que Claude ve — un solo cerebro aprende del video y comprueba con sus manos

Estado: **implementada** (168–179 verdes; cinco pruebas reales sobre SAP QAS el 2026-09-07, la quinta COMPROBADA 2/2 en 105 s y $1,37) · Nace de la investigación y la medida del 2026-09-06 · Decisiones del dueño:
2026-09-06 · Rama: `jose/la-leccion-que-claude-ve`

> Lo que el dueño pidió, en sus palabras: «un solo agente, un solo modelo, una sola ventana de
> contexto», con el Claude Agent SDK —«el único que se ha validado»—, «sí o sí capaz de ver video»,
> «de la forma más comprobada y con más *social proof*». Y la preocupación que lleva años pagando:
> «que los screenshots se tomen cuando ya un elemento cambió por el clic». Esta spec contesta las
> dos cosas con **un reloj, una cámara que mira al pasado y una lección en disco**.

## Diagnóstico: qué se midió

### Lo que la API contesta (2026-09-06, documentación oficial y tipos instalados)

| Qué | Medida | Fuente |
|---|---|---|
| ¿Claude acepta video? | **No, en ningún sitio.** Messages: `text`, `image`, `document`, tool blocks. Files API: `application/pdf`, `text/plain`, `image/*` y `container_upload` (solo para el sandbox de código). | `platform.claude.com/docs/.../vision` y `/files`; `@anthropic-ai/sdk` 0.116.0 |
| Imágenes por petición | 600 (100 en modelos de 200k). Con más de 20 imágenes, **cada una ≤ 2000 px** de lado. 10 MB por imagen. | vision docs |
| Coste por cuadro | Parches de 28 px: `⌈w/28⌉ × ⌈h/28⌉`. **1280×720 = 1.196 tokens**; 1920×1080 = 2.691 en la capa de alta resolución. | vision docs |
| Agent SDK 0.3.226 | `query({ prompt: AsyncIterable<SDKUserMessage> })`; `SDKUserMessage.message` es un `MessageParam` → **admite bloques `image`** en el mensaje del usuario. MCP por `{type:'http', url}`. Sin video ni computer-use nativos. | `sdk.d.ts` |
| Gemini 3.8 Flash | Video nativo a **1 cuadro/segundo** (~100 tok/s en baja, ~300 en alta), 1M de contexto, $0,75/$3,75 por millón hasta 2026-12-31. | docs de Gemini (2026-09-02) |
| Estándar de la comunidad para que Claude «vea» video | `bradautomates/claude-video` (16.700 ★) y cinco repos parecidos: **cuadros de ffmpeg + marca de tiempo `t=MM:SS` + transcripción whisper**. Presupuesto de cuadros por duración (≤30 s → ~30; 1–3 min → ~60; >10 min → 100). Detección de cambio de escena para elegirlos. | GitHub |

### Lo que el mp4 real contesta (2026-09-06, sobre `teach_2026-09-03_21-32-33.mp4`, 52 s, 1920×1080)

| Qué | Medida | Fuente |
|---|---|---|
| Detección de cambio de escena (la técnica de la comunidad) | **1 cambio** en 52 s con umbral 0,20 — y en ese video hubo **al menos 5 pantallas distintas** (árbol → Pto. tbjo. clínico → Urgencias Adultos/Triage → lista → formulario). SAP cambia pocos píxeles: el árbol y la barra se quedan. | `ffmpeg -vf select='gt(scene,0.20)'` |
| Cortar el cuadro justo **antes** de un clic conocido | `t=33.6 s`: el cursor sobre el botón «Triage» de la barra del ALV, con su tooltip, y la lista del paciente detrás. `t=34.8 s`: el formulario de triage abierto. Ninguna carrera: el cuadro de antes se elige del pasado. | `ffmpeg -ss 33.6` sobre el mp4 |
| El clic de árbol «Triage» | `t=15.6 s`: el cursor sobre «Triage» en el árbol, pantalla del Puesto de trabajo. | igual |
| Quién sabe cuándo se hizo clic | El vigía (`ClickWatcher`) escucha `WM_LBUTTONDOWN` **siempre**, con `x,y`, y ya resuelve la identidad SAP/UIA en el instante de pulsar — «después del clic la identidad ya no existe». Hoy sella la hora con `DateTime.UtcNow` al resolver, no con el `time` del gancho. | `ClickWatcher.cs` |
| Quién sabe qué se decía | `WorkflowTeachSession.Oyo(frase)` guarda cada frase con `_reloj.ElapsedMilliseconds`: **un solo reloj** para frases y pasos (promesa 105). La transcripción ya viene de la voz en vivo; no hace falta whisper. | `WorkflowTeachSession.cs:55` |
| Cuántos pasos salieron de 26 clics | **1** (medido dos veces, spec 009). El grabador de SAP emite un paso por *viaje*, no por clic; los clics de árbol que no navegan se pierden. | spec 009, tanda 3 |
| El pantallazo por paso de hoy | `StepShotCamera.Capture` se dispara **al observar el paso**, es decir, después del clic y de su efecto. Es exactamente la carrera que el dueño describe. | `WorkflowTeachSession.cs:186` |

### La comparativa, resuelta

| | Claude Agent SDK + cuadros anclados al clic | Gemini 3.8 Flash con video nativo |
|---|---|---|
| Ve el video | No como mp4. Ve **los cuadros que importan**, elegidos por quien sabe cuándo pasó cada cosa: el gancho del ratón. | Sí, pero a **1 cuadro por segundo**: un clic dura 80–150 ms entre pulsar y soltar, así que el cuadro de antes y el de después caen en el mismo segundo — **es la carrera del screenshot, con otro nombre**. |
| Sabe DÓNDE se hizo clic | Sí: `x,y` del gancho, dibujado sobre el cuadro de antes; y el selector SAP/UIA cuando lo hay. | Tiene que adivinarlo del cursor en el cuadro, si es que cayó en el cuadro. |
| Un solo agente para aprender, comprobar y ejecutar | Sí: el mismo `query()` recibe la lección, cuelga recuerdos, hace de uno en uno por MCP y empaqueta. Validado en este repo (`agente-arquitecto` navegó la app real por MCP). | Habría que montar el harness (Interactions API / ADK) sobre nuestro MCP desde cero. Nada validado aquí. |
| Coste por lección de 10 clics | 20 cuadros de 1280×720 ≈ 24.000 tokens ≈ **$0,12** en Opus 5 (a $5/M). | Menor por token, pero no es el cuello de botella. |
| Social proof | El patrón cuadros+tiempo+transcripción es el más usado de la comunidad (16.700 ★). Aquí se mejora en las dos piezas que fallan sobre SAP. | — |

**Decisión (dueño, 2026-09-06): Claude Agent SDK.** El video sigue siendo el centro: se graba entero
(mp4, para el humano y como respaldo) y de él —o de la cámara que graba en paralelo— salen los cuadros
que el agente ve, anclados a los clics. Lo que se descarta de la receta de la comunidad es la
detección de escena (ciega en SAP) y whisper (ya tenemos la transcripción con hora).

## Por qué esto va dirigido por especificación

Porque el fallo que queremos matar es **silencioso y ya se pagó**: un pantallazo tomado tras el
efecto del clic parece un pantallazo válido. Nadie lo ve mal en el disco; se ve mal tres pasos
después, cuando el modelo describe el botón que apareció en vez del que se pulsó. La única forma de
que no vuelva es que la elección del cuadro sea **una función pura sobre horas**, y que el contrato
la juzgue con el caso exacto: «el clic fue en t; ningún cuadro con hora ≥ t puede ser el de antes».

Y porque «26 clics, 1 paso» es la otra mentira silenciosa: el sistema aprendía menos de lo que
creía. La lección tiene que dejar rastro de **cada clic físico**, tenga o no paso de SAP.

**La regla del flujo, y no tiene excepciones:** ninguna línea de producción entra antes que la
promesa que la juzga. Cada fase empieza con el contrato ROTO y termina con el contrato INTACTO.

## La segunda petición del dueño (2026-09-06, tarde)

> «Yo puedo solo señalar algo y decir "esta es la opción que usarías y x, pero como no la vamos a
> usar, lo que vamos a hacer es y" […] lo que quiero es que el SDK tenga acceso a todas las fotos de
> cada doscientos cincuenta milisegundos […] que, según la transcripción, él pueda ir y buscar una
> imagen que no pertenece directamente a un clic, sino que necesita ver una imagen de otro segundo
> exacto.»

Lo que se señala sin pulsar no deja evento; deja una frase con hora. Por eso la cámara guarda
**todos** los cuadros (4 por segundo, ~60 KB cada uno, ~70 MB por cinco minutos), con el anillo del
ratón pintado donde estaba, y el piloto tiene `ver_momento(MM:SS)` y `ver_alrededor(MM:SS)`: el
cuadro más cercano a ese instante, con dónde estaba el ratón. Es la promesa 177. Los cuadros se
pueden borrar después de comprobar; hoy no se borran.

## La tesis

Una demostración es **una línea de tiempo con un solo reloj**. Sobre ella caen cuatro cosas que ya
existen y hoy no se juntan: los clics del vigía (con `x,y` y hora), lo que SAP observa (selector,
texto tecleado, hora), lo que la persona dice (frase, hora) y la pantalla (mp4, y una cámara de
cuadros). **La lección** es esa línea de tiempo escrita en disco, con dos cuadros por clic: el de
*antes* —elegido del pasado, marcado con un círculo donde cayó el clic— y el de *después* —el
primero en que la pantalla se asentó—.

**Un solo cerebro** (Claude, por el Agent SDK) recibe la lección como un tutorial: texto y cuadros en
orden, `t=MM:SS` en cada uno. En la misma conversación (1) dice lo que entendió y cuelga un recuerdo
en cada elemento con `map_esto_es`, preguntando cuando dude; (2) lo hace de uno en uno con las manos
de la app por MCP, y cada aterrizaje lo juzga la app, no el modelo; (3) lo empaqueta como skill con
objetivos, a partir de lo que **se verificó**, no de lo que dijo. Los identificadores (selectores) son
pistas opcionales: el video manda.

## La especificación

| # | Promesa | Fase que la pone verde |
|---|---|---|
| 168 | el cuadro de ANTES de un clic se elige del pasado: es el más nuevo con hora ≤ hora del clic menos el margen, y ningún cuadro tomado en o después del clic puede ser elegido; si no hay ninguno, no hay cuadro de antes (null), nunca uno de después disfrazado | 1 |
| 169 | el cuadro de DESPUÉS es el primero, pasado el clic más la espera mínima, en que la pantalla se asentó (dos cuadros seguidos iguales); si no se asienta antes del techo, se entrega el último y se dice `asentado=false` — nunca se calla | 1 |
| 170 | cada clic físico de la demostración deja UN evento en la lección, con su hora, su punto y sus dos cuadros, aunque SAP no haya emitido paso: 26 clics son 26 eventos, no 1 | 2 |
| 171 | lo que SAP observó (selector, texto tecleado, tecla) y lo que la persona dijo se cuelgan del evento por cercanía en el mismo reloj; una frase se cuelga de UN clic, el más cercano, y un paso de SAP sin clic cercano queda como evento propio con `porTeclado=true` | 2 |
| 172 | la lección se escribe en disco entera o no se escribe: `leccion.json` + `cuadros/` + `demo.mp4` + donde empezó y donde terminó; una lección sin cuadros o sin mp4 no se entrega, y el motivo queda escrito | 2 |
| 173 | el mensaje que recibe el piloto se arma de la lección en orden de tiempo, con una etiqueta `t=MM:SS · clic N en (x,y) · selector · «lo dicho»` delante de cada cuadro; ningún cuadro pasa de 2000 px de lado, y si hay más cuadros que el presupuesto se quitan primero los de después repetidos (mismo hash) y NUNCA el de antes de un clic con selector | 3 |
| 174 | el piloto tiene UNA caja con manos desde el principio —entender es ir, colgar el recuerdo donde vive el elemento y declarar la llegada— y lo único prohibido, por su nombre, es lo que va en tanda: `map_batch` y `map_skill_run`; las manos, los ojos, la voz y mirar cualquier momento de la demo están todos | 3 |
| 175 | la comprobación es hacer de uno en uno lo que la lección enseña y que cada llegada la juzgue la app contra la llegada de la lección; el recuento es `hechos/total` con el total = eventos que navegan, y COMPROBADA solo si todos aterrizaron (extiende la 131) | 4 |
| 176 | la skill se empaqueta de lo VERIFICADO: cada paso lleva la llegada real medida al comprobar, y un paso que no aterrizó no entra en la skill; lo que el modelo dijo entender se guarda aparte, como recuerdos, no como pasos | 5 |
| 177 | el piloto puede pedir la pantalla de cualquier momento de la demo, aunque ahí no hubiera clic: se le da el cuadro más cercano a ese instante con dónde estaba el ratón, y sin cuadros se dice que no hay | 3 |
| 178 | a dónde llevó un clic lo dice el TERRENO —la misma arista que el batch verifica—, no la lección: si el terreno aprendió que esa puerta lleva a aquella pantalla, esa es la llegada; si no aprendió nada, queda vacía y se dice; solo el último clic tiene de respaldo donde acabó la demo; los clics sobre Ü no cuentan | 2 |
| 179 | comprobar es un PLAN que el piloto entrega en el idioma del ejecutor y la app recorre: por cada paso la voz, el recuerdo antes de tocar, el paso por el mismo ejecutor de tanda y el juez; si un paso no se puede dar la app para ahí y le devuelve al piloto dónde quedó y qué faltó, y solo entonces el piloto actúa con las manos | 4 |

La que cierra el asunto es la **170**: mientras un clic pueda no dejar rastro, todo lo demás describe
con exactitud una demostración que no ocurrió.

### Con qué se juzga cada una

- **168, 169, 171, 173, 175, 176, 177 — mapa a mano en la prueba.** Son funciones puras sobre listas de
  `(hora, dato)`: se construyen las horas a mano —clic en 1000, cuadros en 700, 950, 1010, 1300— y se
  afirma cuál sale. El sabotaje de la 168 es quitar el `≤`: el cuadro de 1010 pasa a poder elegirse y
  la promesa tiene que ponerse roja.
- **170, 172 — el fixture de 26 clics se construye en la propia prueba** (26 clics con hora, 1 paso de SAP,
  cuadros cada 250 ms → 26 eventos); no hizo falta carpeta en `bronce/`. Y una lección sin mp4 → `Entrega() == null`
  con motivo.
- **174 — por reflexión sobre el catálogo**: la caja tiene las manos y no tiene `map_batch`; y `map_batch` está
  además en la lista de prohibidas por su nombre, porque el Agent SDK busca herramientas por su cuenta.
- **Lo que el contrato no puede juzgar y se mide a mano (nivel 4):** que la cámara de antes cueste
  menos de 20 ms por cuadro a 4 cuadros/s (`⏱ TIEMPOS` en el log), y que la lección de una demo real
  de triage tenga tantos eventos como clics dio el médico. Se pega el log en el PR.

## Las fases

Una spec = una rama. Las fases son commits dentro de ella.

### Fase 1 — La cámara que mira al pasado (168, 169)

| | |
|---|---|
| Dónde | `windows-client/src/Teach/CuadroDeAntes.cs`, `CuadroDeDespues.cs` (puros) + `CamaraDeCuadros.cs` (el anillo, BitBlt cada 250 ms, 8 cuadros, reducidos a 1280 de ancho) |
| Cómo | Reutiliza el BitBlt de `StepShotCamera`. El anillo vive solo mientras se enseña. Al `WM_LBUTTONDOWN` del vigía se congela el cuadro de antes; el de después se busca a partir de +400 ms comparando hashes baratos (16×16 gris) hasta +2000 ms. |
| Por qué no ffmpeg en producción | El mp4 sigue grabándose (ScreenRecorderLib) pero no hace falta decodificarlo: la cámara ya tiene los cuadros. ffmpeg añade ~50 MB y una zona gris de licencia (ya dicho en `ScreenRecorder.cs`). Sirvió para PROBAR la idea sobre el video del 3 de septiembre; no para correrla. |
| Termina cuando | 168 y 169 verdes; sabotaje comprobado (quitar `≤` → 168 roja; quitar el techo → 169 roja). |

### Fase 2 — La lección en disco (170, 171, 172)

| | |
|---|---|
| Dónde | `windows-client/src/Teach/Leccion.cs` (el modelo y el anclaje, puro) + `LeccionEnDisco.cs` (escribe `%LOCALAPPDATA%\U\lecciones\<id>\`) |
| Cómo | El vigía sella la hora **del gancho** (`MSLLHOOKSTRUCT.time` traducido al reloj de la demo), no la de resolver. `WorkflowTeachSession` suscribe al vigía además de a `StepObserved`; cada clic es un evento. El anclaje de frases reutiliza `AncladorDeVoz` (105). `SesionDeDemo.Cerrar()` es quien entrega la lección; `Descartar()` la borra entera (104). |
| Termina cuando | fixture de 26 clics → 26 eventos; lección sin mp4 → no se entrega y dice por qué. |

### Fase 3 — El piloto lee la lección (173, 174)

| | |
|---|---|
| Dónde | **`agente-piloto/`**, nuevo y desde cero —no se recicla `agente-arquitecto`—: `piloto.mjs` (Agent SDK, `query()` con entrada en streaming), `leccion-a-mensaje.mjs` (puro: lección → bloques), `herramientas.mjs` (servidor MCP en proceso con `decir`, `preguntar`, `guardar_skill`). Y en el cliente `windows-client/src/Piloto/ElPiloto.cs` que lo lanza y le habla por stdin/stdout. |
| Cómo | El mensaje del usuario es la lección: texto + `image` (base64 png ≤ 2000 px). MCP real en `127.0.0.1:8790/mcp/` para las manos y los recuerdos. `decir` habla por la voz viva (`DiEstoAsync`); `preguntar` abre el micrófono y espera. `systemPrompt` en el propio `piloto.mjs` —el dueño decidió sacar el cerebro de Graph para la enseñanza: «todo aquí en nuestra aplicación local, directo a servidores». |
| Termina cuando | 173 verde sobre la lección fixture; 174 verde por reflexión sobre los dos catálogos. Y a mano: el piloto describe la lección del triage con los cuatro elementos correctos y cuelga ≥ 4 recuerdos con `map_esto_es`. |

### Fase 4 — Comprobar es hacer de uno en uno con juez (175)

| | |
|---|---|
| Dónde | `agente-piloto/piloto.mjs` (segunda etapa de la misma conversación) + `windows-client/src/Navigation/LaComprobacion.cs` (extiende 131) |
| Cómo | El piloto pide `map_go_to`/`map_take`/`map_type` de uno en uno; tras cada una la app compara `map_where_am_i` con la llegada de la lección y devuelve el veredicto como resultado de la herramienta. El piloto no puede declararse comprobado: lo dice la app con `Juzgar(hechos, total, relato)`. |
| Termina cuando | 175 verde; y a mano: la comprobación del triage llega al formulario y el recuento dice `N/N` con N = clics que navegaron, no 1. |

### Fase 5 — La skill nace de lo verificado (176)

| | |
|---|---|
| Dónde | `windows-client/src/Navigation/SkillEnsenada.cs` (`DeLoVerificado(leccion, veredictos)`) + `guardar_skill` en el piloto |
| Cómo | Solo entran pasos con aterrizaje juzgado. Los recuerdos del modelo ya viven en el grafo (fase 3). La skill queda en `%LOCALAPPDATA%\U\skills\` como hoy, para que `map_batch`/`map_skill_run` la lean sin cambios. |
| Termina cuando | 176 verde; y la skill del triage ejecutada por `map_batch` llega al formulario sin narrar y sin toques ciegos (contraste con los 72 del 2026-09-06). |

## Lo que quedó implementado (2026-09-06, rama `jose/la-leccion-que-claude-ve`)

| Pieza | Archivo | Promesa |
|---|---|---|
| El cuadro de antes y el de después, puros | `windows-client/src/Teach/CuadroDeAntes.cs`, `CuadroDeDespues.cs` | 168, 169 |
| La cámara del anillo: BitBlt cada 250 ms, 1280 px, huella 8×8, anillo del ratón, TODOS los cuadros a `cuadros/` | `Teach/CamaraDeCuadros.cs` | — |
| El vigía avisa cada pulsación humana con la hora del gancho (`ClickWatcher.AlPulsar`) | `Navigation/ClickWatcher.cs` | 170 |
| La lección: eventos por clic, pasos de SAP por cercanía (1,5 s), frases por el anclador de la 105, llegada leída 1,2 s tras el clic | `Teach/Leccion.cs`, `WorkflowTeachSession.cs` | 170, 171 |
| Entera o nada, y en disco: `%LOCALAPPDATA%\U\lecciones\<id>\{leccion.json, cuadros/, mensaje.json}` | `Teach/LeccionEnDisco.cs` | 172 |
| El mensaje-tutorial: bloques en orden, etiqueta `t=MM:SS · clic N en (x,y) · selector · «dicho»`, presupuesto, ≤ 2000 px | `Teach/MensajeDeLaLeccion.cs` | 173 |
| La caja única con manos, y la lista de prohibidas (`map_batch`, `map_skill_run`) | `Piloto/CajasDelPiloto.cs` | 174 |
| El juez: `leccion_llegue(n)` compara con la llegada grabada; total = eventos que navegan | `Piloto/RegistroDeLaComprobacion.cs` | 175 |
| La skill de lo verificado, con la llegada real | `Piloto/SkillDeLoVerificado.cs` | 176 |
| El cuadro de un momento cualquiera | `Teach/Leccion.cs` (`CuadroDelMomento`) | 177 |
| Las cuatro herramientas MCP nuevas: `voz_decir`, `voz_preguntar`, `leccion_llegue`, `leccion_guardar_skill` | `Mcp/SurfaceMapTools.cs`, `Ui/FaceWindow.xaml.cs` | 174, 175, 176 |
| El piloto, desde cero: Agent SDK, `query()` con la lección como mensaje (texto + `image`), MCP http 8790 + servidor en proceso con `ver_momento`, una sola conversación: entender es ir y colgar el recuerdo donde vive el elemento | `agente-piloto/piloto.mjs` | 173–176 |
| Quien lo lanza y lee su stdout (JSON por línea) | `Piloto/ElPiloto.cs` | — |

**Comprobar elige el camino:** si la demo dejó lección y `agente-piloto/piloto.mjs` está a mano
(o `U_PILOTO` apunta a él), comprueba el piloto; si no, el camino viejo de la spec 009 sigue intacto.
El modelo del piloto sale de `U_PILOTO_MODELO` (por defecto `claude-opus-5`). El Agent SDK usa la
sesión de Claude Code de la máquina; en el PC de un médico habrá que llevar Node, el script y una
credencial — **pendiente y dicho**.

**Lo que el contrato no puede juzgar y falta medir sobre el PC real (nivel 4):** el coste por
cuadro de la cámara (`⏱ TIEMPOS cámara` en el log), que una demo real de triage deje tantos eventos
como clics dio el médico, que el piloto cuelgue recuerdos y que `leccion_llegue` aterrice sobre SAP.

## La primera prueba real completa (2026-09-07, 11:09 → 11:26)

| Qué | Medida |
|---|---|
| Clics del vigía · pasos de SAP · frases | 7 · 3 · 11 |
| Eventos en la lección (la skill vieja de la misma demo: 3 pasos) | **10** |
| Cámara | 236 cuadros · media 109 ms · peor 278 ms (intervalo 250) |
| Comprobación | 333 s · **$4,06** en Opus 5 · 19 cuadros en el mensaje |
| Aterrizados según el juez | 5, con el total en 2 → «5/2» |
| Skill guardada | ninguna: «no hay pasos verificados con identidad» |

Cuatro fallos, todos míos y todos medidos:

1. **La identidad de un clic la da el vigía, no SAP.** Siete clics, cero con selector: tomaba x,y del
   gancho y esperaba que SAP nombrara el clic. SAP nombra viajes. El vigía resuelve al pulsar la
   MISMA etiqueta con la que el terreno nombra sus puertas (`comando`, `Pto.tbjo.clínico`), que es
   lo único que `map_take` entiende. El piloto tardó tres minutos tanteando nombres. → El evento
   lleva la etiqueta del vigía (`ClickWatcher.AlResolver`), y el mensaje la pone delante como «puerta».
2. **Un paso de SAP cuelga del último clic ANTERIOR, no del más cercano en 1,5 s.** Medido: el input
   de `okcd` llegó 3,07 s después del clic; el nodo del árbol, 2,54 s. Ventana de 8 s hacia atrás, y
   el Enter se pliega en el mismo evento (como la 132).
3. **La llegada se leía a 1,2 s fijos** y grabó `…/0100` donde la verdad era `…/0100/ssub…Triage`; al
   comprobar, el piloto llegó a la correcta y el juez lo castigó. → Promesa 178: se lee hasta que se
   asienta (dos lecturas iguales, SAP sin viaje), techo 5 s, y se dice si no se asentó.
4. **«5/2»: dos denominadores.** `Hechos` contaba todos los veredictos; el total solo los que navegan.
   Ahora los dos cuentan sobre el plan.

### La asunción que la prueba pone en duda, y que decide el dueño

**Que el piloto ejecute paso a paso.** Costó 333 s y $4,06; el ejecutor de tanda hizo la misma ruta
el 2026-09-03 en 25 s con compuerta y cuenta honesta. El «de uno en uno» (promesa 141, decisión del
2026-09-03) nació porque el batch iba ciego: 72 toques ciegos, sin recuerdos. Con la lección el batch
ya no está ciego: trae cuadros, puertas por su nombre y lo dicho. El valor del modelo está en
INTERPRETAR (video → puertas con significado), no en ejecutar. La alternativa: el piloto lee la
lección y produce el plan —puertas, textos, recuerdos, llegadas—; el batch lo corre narrando y
colgando el recuerdo en cada paso; el piloto vuelve solo cuando el batch para (el rescate de la
spec 009). Estimación: ~30 s y ~$0,30 por comprobación. **Decisión del dueño (2026-09-07, tarde): «me gusta mucho».** Es la promesa 179. La 141 sigue
valiendo para el encargo viejo (skills sin lección); para las lecciones manda la 179.

### La segunda prueba real (2026-09-07, 15:33 → 15:38), ya con los cuatro arreglos

| Qué | Medida |
|---|---|
| Clics · con identidad · pasos de SAP | 5 · 3 · 2 → 5 eventos |
| Cámara | 183 cuadros · media 90 ms · peor 179 ms |
| Comprobación | **114 s · $1,75** (antes 333 s · $4,06) |
| Tanteo de nombres | ninguno: `map_take exit=«Favoritos/IS-H: Pto.tbjo.clínico»` a la primera |

Dos fallos más, ambos míos: (1) el bucle de la llegada se daba por asentado sobre la pantalla VIEJA
—SAP la deja leer dos veces antes de arrancar el viaje— y grabó el origen como llegada en los
eventos 1 y 3 → (primero un bucle con origen, luego la regla del clic siguiente; los dos se
borraron cuando la búsqueda de más abajo mostró que el terreno ya tenía la respuesta, promesa 178). (2) La skill exigía selector y la identidad ahora es la puerta → el `Exit` de un
paso es la puerta por su nombre. Y el clic de parar la demo entraba como evento: ahora se mira qué
ventana hay bajo el punto (`WindowFromPoint`) y lo de Ü queda fuera.

### La tercera prueba real (2026-09-07, 15:49 → 15:52): la primera COMPROBADA, y con trampa

| Qué | Medida |
|---|---|
| Clics · sobre Ü fuera · con identidad | 5 · 1 · 3 → 4 eventos |
| El plan | 2 pasos, 2 recuerdos colgados en su pantalla, **1,8 s** |
| Total | 123 s · $1,67 · COMPROBADA · skill guardada |

La trampa: las llegadas de los eventos 2 y 3 quedaron grabadas como su ORIGEN (techo de 6 s y SAP
tardó más: el siguiente clic llegó a los 21 s). El juez dijo 0/2 dentro del plan, y el piloto
—que llegó bien— VOLVIÓ con `map_go_to` a la pantalla falsa para que el juez lo aprobara. La skill
se guardó con esa llegada. Una comprobación que parece que funcionó: patrón nº10 otra vez.
→ Y el dueño preguntó lo justo: «¿cuál es la forma determinística?». No hay que medir tiempo. La
llegada del clic N es la pantalla que había en el instante del clic N+1, y la del último es donde
acabó la demo. Es la regla que el grabador de pasos ya usaba. Se borró el bucle entero con sus tres
techos (1,2 s → 6 s → 20 s, los tres fallaron): `ArmarLaLeccion.Llegadas`, promesa 178 reescrita. Y un hueco anotado: el resolutor de SAP no nombra filas de la rejilla ni botones de la
barra del ALV, así que un clic ahí queda sin puerta y sin recuerdo (el dueño: «no veo aprendizajes
colgados del botón triage»).

### La búsqueda que pidió el dueño (2026-09-07, tarde): ¿es esta la solución estándar?

No lo era. Había **tres** respuestas a «¿a dónde llevó este clic?» en el repo: (1) el terreno vivo,
que desde agosto atribuye cada cambio de ubicación al último clic del vigía y guarda la arista
(promesas 46 y 77) —y es la que `map_batch` verifica—; (2) el grabador de SAP, que sella la
superficie del paso siguiente; (3) la lección, calculando la suya. En las demos de las 15:33 y
15:49 el terreno aprendió bien («Favoritos/IS-H: Pto.tbjo.clínico» lleva de SESSION_MANAGER a
NWP1/0100; «Urgencias Adultos/Triage» lleva de NWP1/0100 a la vista de Triage; y de un día antes,
el botón «Triage» de la barra lleva al formulario SAPLY000/0001) mientras la lección grababa mal.
→ La 178 se reescribe: la llegada la dice el terreno; la lección no calcula ninguna. El plan pasa
esa llegada al batch en cada paso, así que el juez de la comprobación y la compuerta del batch
miran el MISMO dato.

**El límite del terreno, dicho aparte y sin parche:** atribuye un cambio al último clic solo si
tiene menos de 6 s (a las 11:09:36 perdió una transición de 6,6 s). No es algo que se añada: ya
está ahí desde agosto y protege contra atribuir a un clic un refresco de minutos después. Si SAP
tarda más de 6 s con frecuencia, se mejora en el terreno con sus promesas —por ejemplo con la señal
`Busy` de SAP como prueba de que el viaje seguía—, no en la lección.

### La cuarta prueba real (2026-09-07, 16:45): el escritorio delante

La demo arrancó con el escritorio en primer plano («explorer», no es SAP), el detector eligió UIA y
la lección de SAP salió con 36 pasos por PULSACIÓN («n», «nw», «nwp», «nwp1», «1001», «200»), 35
eventos, 70 cuadros, $3,13, y llegadas `uia://` que ningún juez podía casar con `sapgui://`. Dos
arreglos: el escritorio (Progman, WorkerW, la barra) no cuenta como «la app delante» al arrancar
(137 ampliada), y los pasos seguidos sobre el mismo campo se pliegan en uno (171 ampliada).

### La quinta prueba real (2026-09-07, 17:15 → 17:18): con la llegada del terreno

| Qué | Medida |
|---|---|
| Clics · sobre Ü fuera · con identidad | 8 · 1 · 3 → 7 eventos |
| Plan | 3 pasos; paró en el 2º porque el piloto llamó «1001» (nombre UIA) al campo de comandos, que el terreno conoce como «comando»; siguió con las manos |
| Juez | 2/2 aterrizados, sin volver atrás a ninguna pantalla falsa |
| Total | **105 s · $1,37 · COMPROBADA** · skill «Ir al triage de Urgencias Adultos» con 2 pasos verificados |

Hueco anotado: el vigía nombra el campo de comandos de SAP por su etiqueta UIA («1001») y el terreno
por su puerta («comando»). La lección debería casar la etiqueta del vigía con la puerta del terreno
antes de dársela al piloto.

## Lo que la primera corrida real enseñó (2026-09-06, noche)

Contra el MCP vivo del 8790 y con una lección de un solo evento (los dos cuadros del triage), el
piloto **entendió la tarea con solo dos cuadros** y distinguió el botón Triage de la barra del
nodo Triage del árbol —la ambigüedad que rompía las corridas de la spec 009—. Y dos correcciones:

1. **Tenía dos cajas y sobraba una.** Separé «entender sin manos» de «hacer», por analogía con el
   aprendiz de la 138. El piloto abrió SAP mientras «entendía» y el dueño dijo que era lo correcto.
   Lo era: un recuerdo se cuelga de (pantalla, selector), así que para colgar el del botón Triage hay
   que estar en Urgencias Adultos. La 174 se reescribió: una caja con manos, y lo único prohibido es
   lo que va en tanda.
2. **Ofrecer menos no es prohibir.** Con las manos fuera de la lista de permitidas, el piloto las
   buscó por su cuenta (nueve `ToolSearch`) y las llamó. El Agent SDK trae búsqueda de herramientas.
   La lista de prohibidas es la que manda, y ahora viaja desde C#.

Y un chequeo previo: antes de gastar un token, el piloto pregunta `tools/list` a la app; si no
contesta, para y lo dice (la primera corrida en seco gastó 27 centavos descubriendo que no tenía
manos).

## Decisiones del dueño (2026-09-06)

- **Un agente, un modelo, una conversación**: Claude Agent SDK. No Gemini como piloto. No reciclar
  `agente-arquitecto`: se escribe `agente-piloto` limpio.
- **El video siempre.** Se graba entero; el agente lo ve por cuadros anclados a los clics. Las
  imágenes por clic *sueltas* (el `StepShotCamera` de hoy) no sirven porque se toman tras el efecto.
- **Graph fuera del camino del aprendizaje** por ahora: el prompt del piloto vive en `agente-piloto/`.
  El anti-copia «no importa». `mcpCatalog.js` queda como deuda documentada (spec 009).
- **Comprobar puede preguntar** cuando dude (`preguntar`), y narra con la voz de Ü (`decir`).
- **Los recuerdos se cuelgan de uno en uno** sobre cada elemento, con lo que Ü *entendió*.
- **Todos los cuadros quedan a disposición del piloto**, no solo los de los clics; se puede pedir un
  segundo exacto según la transcripción (`ver_momento`). Borrarlos después es opcional.
- **Toda la implementación de una vez**, sin fase a fase con el dueño en medio.

## Lo que NO entra

- Excluir la ventana de Ü de los cuadros (hoy sale la carita y el panel del chat en el mp4). Es
  ruido, no error; se anota para otra rama.
- Cambiar el grabador de SAP para que emita un paso por clic de árbol. **Ya no hace falta**: la
  lección toma los clics del vigía. El grabador sigue aportando selector y texto cuando los tiene.
- La ejecución por batches (`map_batch`) no se toca: lee las skills como hoy.
- Whisper / ffmpeg en producción.

## Hallazgos

1. **La detección de escena es ciega en SAP.** 1 cambio detectado en 52 s con 5 pantallas reales.
   La receta de la comunidad (16.700 ★) presupone videos donde la imagen cambia; en SAP cambia el
   contenido de un panel y el resto se queda. El ancla correcta no es la imagen: es el clic.
2. **El cuadro de antes ya existía y nadie lo cortaba.** El mp4 llevaba el cursor sobre «Triage» con
   su tooltip en `t=33.6`. La información estaba grabada; el pantallazo por paso llegaba tarde.
3. **El reloj único ya estaba puesto** (promesa 105) y el vigía ya elegía el instante correcto para la
   identidad (`WM_LBUTTONDOWN`). Esta spec junta dos decisiones correctas que vivían separadas.
