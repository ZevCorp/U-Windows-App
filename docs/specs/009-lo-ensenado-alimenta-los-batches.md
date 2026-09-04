# Plan de implementación: lo enseñado alimenta la ejecución por batches

Estado: **propuesto** · Nace de la validación del 2026-09-02 · Decisiones del dueño: 2026-09-03 ·
Ramas: `jose/un-solo-ejecutor` (A) y `jose/comprobar-el-aprendizaje` (B)

> Lo que se validó el 2026-09-02 con el triage real: **enseñarle a Ü qué es cada elemento hace que
> lo llene bien y no se trabe**. Seis recuerdos colgados de seis campos, y el ✓ pasó de tres campos
> a todos. Esta spec convierte ese hallazgo en arquitectura: la enseñanza produce CONTEXTO
> —objetivos y recuerdos— y hay UN solo ejecutor, `map_batch`, con computer use como rescate
> hablando identidades.

## La duda, resuelta con hechos

**El botón 🎓 que ilumina los bordes y el viejo grabador de workflows son el mismo sistema.**
`WorkflowTeachSession` existe desde el commit inicial (2026-07-20). Al pulsar 🎓 arrancan dos
grabadoras en paralelo: `WorkflowRecorder` (pasos por identidad → Graph) y `TeachSession` (pantalla +
voz → video). La spec 005 (2026-09-01) no lo reemplazó: le añadió el aura (promesa 107), el anclaje
de lo dicho a cada paso (105) y un segundo empaquetado local, la **skill** (`SkillEnsenada`, 102 y
106), pensada para `map_batch`. Un botón, una sesión, **tres salidas**:

| Salida | Quién la consume | Estado en `main` |
|---|---|---|
| Workflow en Graph | `WorkflowPlayer` (el ejecutor viejo) | funciona; 5 workflows de SAP grabados |
| Video + resumen del LLM | nadie todavía | se graba; el proceso con IA está **apagado** por los 504 |
| Skill local (`%LOCALAPPDATA%\U\skills\*.skill.json`) | `map_batch` (el ejecutor nuevo) | juzgada, **sin cablear**: la sesión de demo no llama al empaquetador (rama aparcada `jose/la-demo-se-graba-entera`) |

No hay otro sistema que estudiar. Hay que cerrar la tercera salida, encender la segunda y congelar
la primera.

## La tesis

Un workflow **no es un plan que se ejecuta: es una lista de objetivos**. Cada paso enseñado dice a
dónde se llegó (`Llegada`), y eso es un objetivo comprobable por máquina. El ejecutor viejo falla
porque exige lo exacto —esta ventana, este elemento— y la pantalla cambia. `map_batch` no exige lo
exacto: exige que el objetivo se cumpla contra lo que está vivo ahora, y cuando no puede, para
honesto y dice qué sí ve. Lo que falta es que **el rescate hable el mismo idioma**: cuando el batch
para, computer use no recibe «termina la tarea», recibe el objetivo que faltó y lo accionable por
identidad, y su aterrizaje lo juzga la misma compuerta.

Los **recuerdos** son la otra mitad del contexto: lo que un elemento ES. **Se siguen pudiendo enseñar
uno a uno, señalando y hablando, exactamente como hoy** — eso no cambia y no se retira. Lo que se
añade es que la enseñanza por demostración los rellene mejor que nadie: al **comprobar el
aprendizaje**, Ü recorre la tarea que le enseñaron y cuelga de cada elemento lo que se dijo sobre él
y lo que el video contó de ese momento.

## La experiencia

1. **🎓 Enseñar.** Pulsas, haces la tarea hablando, pulsas otra vez. Queda una skill: pasos con su
   llegada y lo dicho en cada uno, más el video con su resumen.
2. **Comprobar aprendizaje** (botón nuevo, **obligatorio**). Ü recorre la skill por batches,
   narrando. En cada paso cuelga del elemento lo que dijiste sobre él y lo que el video aportó. Si un
   paso no llega, el rescate por identidades lo intenta y se anota. Al final: «aprendí N pasos y M
   recuerdos; me atasqué en X». **Nunca graba**: se detiene en la puerta peligrosa y te la deja.
3. **Ejecutar** (el ✓, o cualquier petición). Batches con objetivos y recuerdos. Una skill sin
   comprobar no se ejecuta: el catálogo la anuncia como pendiente y dice qué falta.

## Diagnóstico: qué se midió (2026-09-02)

| Qué | Medida | Fuente |
|---|---|---|
| Enseñar con recuerdos arregla el relleno | ✓ con 6 recuerdos sobre el triage: todos los campos, sin trabarse (el dueño) | log `envio`, `recuerdo` |
| Sin recuerdos, 3 de 9 campos | «Temperatura, Sat. O2, Conducta»; Frec. Cardíaca reventó | log 18:10 y 18:28 |
| `map_batch` ya exige la llegada por paso | promesa 103, cableada en `Recorre` | `RecorrerSegunElNucleo.cs` |
| La skill ya guarda lo dicho anclado al paso | promesa 105 (`AncladorDeVoz`); el cableado de la hora real está en la rama aparcada | `Navigation/AncladorDeVoz.cs` |
| El rescate improvisa | «retoma y termina la tarea» sin objetivo ni inventario; pulsó «Buscar pacientes» por «Crear Triage» | `CLAUDE.md` §pendientes 2-3 |
| El proceso del video está apagado y por qué | `ProcessVideo` desde `Config.ProcessTeachVideo`; el post-procesado de Graph se pasa del límite de Vercel con flujos largos (504 con 6 pantallas y 3 min) | `WorkflowTeachSession.cs:38-46, 150-190` · log 16:33:28 |
| Enseñar por nombre funciona dentro de SAP | promesa 117 | `Navigation/ElCampoQueNombras.cs` |
| Señalar e iluminar despachan por mundo, y las filas de árbol son candidatas | promesas 118-119 | `MundoQueToca.cs`, `FaceWindow.LeerCajasDeSap` |
| Las filas de una REJILLA no tienen geometría en SAP | hueco dicho, no tapado | `windows-graph/CLAUDE.md` |
| Contrato | 119 promesas (21-119); siguiente libre: **120** | `Contrato.cs` |

## La especificación

| # | Promesa | Rama · Fase |
|---|---|---|
| 120 | cuando un batch para, el relato lleva el objetivo que faltó y lo accionable por identidad: el rescate no recibe «termina la tarea» | A · 1 |
| 121 | el aterrizaje del rescate lo juzga la misma compuerta que el batch: «terminé» no es un veredicto | A · 1 |
| 122 | **reproducir una skill es un batch: el mismo ejecutor, la misma compuerta, la misma cuenta** | A · 2 |
| 123 | un valor tecleado en la demo jamás se reproduce: es un hueco, y sin dato queda vacío | A · 2 |
| 124 | lo dicho durante la demo se vuelve recuerdo del elemento DONDE se dijo, sin que nadie señale | B · 3 |
| 125 | un recuerdo creado al comprobar se cuelga del elemento que el paso TOCÓ, nunca de uno que el modelo elija | B · 3 |
| 126 | comprobar no graba: una skill que termina en Grabar se recorre hasta esa puerta y ahí se detiene | B · 3 |
| 127 | una skill sin comprobar no se ejecuta: el catálogo la anuncia como pendiente y decir «no» dice qué falta | B · 4 |
| 128 | si el video no se pudo procesar, la comprobación lo dice y sigue con lo dicho: no se inventa contexto ni se calla el hueco | B · 4 |

**La que cierra el asunto es la 122.** Mientras una skill se reproduzca por otro ejecutor hay dos
opiniones sobre el mismo hecho. La **123** es su guardarraíl y no es teórica: la demo tecleó «70» en
un paciente de prueba, y reproducir ese valor sobre un paciente real sería una catástrofe silenciosa.
La **125** es el guardarraíl del video: un resumen en prosa no puede elegir a qué elemento se cuelga
un significado — el selector lo pone el paso, el significado lo pone lo dicho.

### Con qué se juzga cada una

Todas con el arnés `BatchCon` y grafos a mano, sin pantalla ni red:

| # | Cómo |
|---|---|
| 120 | batch que para en el paso 3 con llegada «B» → el relato contiene «B» como objetivo y las puertas vivas con su selector; no contiene «termina» |
| 121 | rescate que declara «llegué» con la ubicación en «A» y objetivo «B» → no cuenta como llegada, y el motivo nombra las dos |
| 122 | skill de 3 pasos → `map_skill_run` produce los 3 pasos del batch con su llegada, y la cuenta es «N de 3» sobre el plan |
| 123 | paso tecleado «70» → hueco; instanciar sin datos → nada escrito; «70» no aparece en ningún paso instanciado |
| 124 | skill que empieza en A, paso 1 sobre X con dicho «aquí va el peso» y llegada B → produce (A, X, «aquí va el peso»): el recuerdo se cuelga donde el paso se DIO, no donde llegó. Paso sin dicho → nada |
| 125 | el paso tocó X y el resumen del video habla de Y → el recuerdo se cuelga de X; un significado sin paso que lo sostenga se descarta |
| 126 | skill cuyo último paso es «Grabar» → el recorrido se detiene antes, cuenta N-1 de N y nombra la puerta |
| 127 | skill sin comprobar → el catálogo la marca pendiente y `map_skill_run` la rehúsa diciendo «hay que comprobarla»; comprobada → corre |
| 128 | comprobación con resumen de video vacío o fallido → se dice, y los recuerdos de lo dicho se crean igual |

Nivel 4: el botón «Comprobar aprendizaje», la narración por voz, el rescate real con screenshots, y
el proceso del video con IA sobre una demo larga (donde vive el riesgo del 504).

## Las fases

**Dos ramas.** A cierra la ejecución —lo que el dueño pidió primero: que la ejecución sea muy buena y
limpia—. B cierra el aprendizaje.

| Fase | Promesas | Qué toca |
|---|---|---|
| 0 | las nueve en rojo | `Contrato.cs` |
| 0b | cablear lo aparcado (102, 105, 106): empaquetar al cerrar la demo, la hora del golpe y de la frase, el catálogo `map_skills` | traer del stash `jose/la-demo-se-graba-entera` |
| 1 | 120, 121 | `RecorrerSegunElNucleo` (el relato lleva objetivo y accionable) · `AgentLoop` y el puente (el rescate los recibe, y su llegada la juzga la compuerta) |
| 2 | 122, 123 | `map_skill_run` en `SurfaceMapTools` · `InstanciarSkill` con huecos (del stash) · el ✓ y el piloto dejan de usar `WorkflowPlayer` |
| **→ aquí se corta la rama A y va a `main`** | | |
| 3 | 124, 125, 126 | `Navigation/RecuerdosDeUnaSkill.cs` (puro) · `PuertasPeligrosas` (del stash) · el botón «Comprobar aprendizaje» → piloto: `map_skill_run` + `map_esto_es` por paso |
| 4 | 127, 128 | `SkillEnsenada` gana su estado (`Comprobada`) y el catálogo lo anuncia · `TeachSession.ProcessVideo` encendido por defecto, con el fallo visible |
| 5 | nivel 4 | corrida real: enseñar el triage → comprobar → ✓ sin haber señalado nada, con log y LA MÉTRICA |

## Decisiones del dueño (2026-09-03)

| Decisión | Lo decidido | Qué implica |
|---|---|---|
| **El ejecutor viejo** | **Congelar ahora, eliminar por completo cuando esto funcione entero** | Fase 2 lo desconecta del ✓ y del piloto; su panel sigue reproduciéndolo. El borrado es su propia rama, cuando la 122 lleve kilómetros |
| **El video con IA** | **Que se procese** | Fase 4 enciende `ProcessTeachVideo` por defecto. El riesgo conocido es el 504 en flujos largos, y por eso la 128: si no se pudo procesar, se dice y la comprobación sigue con lo dicho. El aviso de «grabación larga, pártela en dos» ya existe a los 30 pasos |
| **Dónde vive la skill** | **Local ahora, Graph más adelante** | Sigue en `%LOCALAPPDATA%\U\skills`. Mudarla a Graph es otra spec, cuando haya que compartirla entre máquinas |
| **Comprobar** | **Obligatorio** | Promesa 127: una skill sin comprobar no se ejecuta. Es una compuerta, así que el «no» tiene que decir qué falta — no basta con negarse |

## Lo que NO entra

| Qué | Por qué |
|---|---|
| **Retirar los recuerdos a mano** | Se quedan tal cual. La demo los rellena mejor, no los sustituye |
| **Señalar filas de rejilla** | SAP no da geometría por fila de un ALV. Dicho como hueco |
| **Borrar `WorkflowPlayer`** | Decisión del dueño: congelar ahora, borrar después |
| **Skills compartidas por Graph** | Decisión del dueño: más adelante |
| **Que el video decida a qué elemento se cuelga un recuerdo** | Promesa 125. El selector lo pone el paso |

## Hallazgos

1. **2026-09-02** — La validación real: con recuerdos el emparejador llenó todos los campos y no se
   trabó; sin ellos, 3 de 9. El canal es la etiqueta (promesa 115) y no hay ni una regla de dominio
   en el código.
2. **2026-09-02** — Señalar en SAP solo veía componentes, y en SAP el contenido navegable —las filas—
   no son componentes. Desde el 2026-09-02 son candidatas con la caja que SAP les da.
3. **2026-09-02** — `FindByPosition` estaba documentado como inútil en este SAP desde julio y el
   código lo llamaba mal (pedía `.Id` a una colección de dos cadenas) con un `catch` mudo. Se dejó
   de usar.
4. **2026-09-02** — El despacho entre mundos tenía tres verbos de cinco. La regla del dueño
   (2026-08-26) dice que vive en un solo sitio; señalar e iluminar se habían quedado fuera.

## Segunda tanda: lo que la primera corrida real destapó (2026-09-03)

> El dueño enseñó cuatro tareas sobre SAP y pulsó «Comprobar». Su lectura fue **«no hizo nada»**.
> El log dice otra cosa peor: **sí corrió, hizo CERO pasos de cuatro, y aun así estampó la skill como
> COMPROBADA.** Eso no es un botón que no responde: es un juez que absuelve sin haber mirado — el
> aprendizaje nº10 de este repo con otra cara.

### Lo medido, con hora

| Hora | Qué dijo el log | Qué significa |
|---|---|---|
| 12:12:47 | `hice 0 de 4 y paré en el paso 1: «sap:wnd[0]/tbar[0]/btn[3]» lo conozco aquí pero AHORA no lo veo` | el «Atrás» de la demo está gris en Easy Access. La compuerta acertó |
| 12:14:08 | `hice 0 de 4 y paré en el paso 1: «key:enter» no lo conozco` | **`key:enter` no es una puerta y nunca lo será**: el grabador lo emite y el batch no tiene forma de ejecutarlo |
| 12:14:08 | `5 paso(s)` en la skill, `0 de 4` en el plan | el `okcd` con «nwp1» se instanció como hueco sin dato → omitido. **La skill perdió justo el paso que la hace arrancar** |
| 12:12:47 · 12:14:08 · 12:17:53 | `COMPROBADA en 4187 ms · 0 recuerdo(s)` | se certificó tres veces un camino que no se anduvo |
| 11:30 · 12:02 · 12:08 · 12:16 | `Gemini: upload start HTTP 429: Your prepayment credits are depleted` | el video no se procesa hoy, se implemente como se implemente |

Y la causa de que «nwp1» acabara de hueco: la regla del narrado (`SkillEnsenada.HuecosDe`) dice
*lo que narras es un dato*. El dueño narraba **todo el rato** —está enseñando, para eso habla—, así
que el código de transacción quedó marcado como dato del paciente. La regla acierta en el caso que
la inspiró y falla en el siguiente, que es exactamente lo que la decisión del 2026-09-03 anticipó:
**ese juicio es del modelo, no de una regla.**

### La especificación

| # | Promesa | Fase |
|---|---|---|
| 131 | **comprobar no certifica un camino que no se anduvo**: si el recorrido no completó el plan, la skill sigue SIN comprobar y se dice hasta dónde llegó | 6 |
| 132 | una tecla que sigue a lo tecleado viaja CON ese paso —y hereda su llegada—: `key:enter` deja de ser una puerta que nadie puede abrir | 6 |
| 133 | lo tecleado va a SU campo: el paso lleva el selector donde la demo escribió, y quien escribe lo recibe en vez de adivinar el foco | 6 |
| 134 | lo que el modelo interpreta manda sobre la regla del narrado, y solo sobre los campos que la demo tocó; sin interpretación, la regla sigue mandando | 7 |
| 135 | al video se le pregunta por los pasos de ESTA demo, y lo que conteste de un paso que no existe se descarta antes de tocar la skill | 7 |

La **131** es la que cierra el agujero que el dueño vio. Las **132** y **133** son por qué hizo cero
pasos. La **134** es la decisión del 2026-09-03 —«que el LLM decida qué es dato fijo y qué no»—
puesta a trabajar: `LoQueElModeloInterpreta` (129/130) ya sabe LEER la respuesta; lo que faltaba era
preguntar y aplicar.

### Con qué se juzga

| # | Cómo |
|---|---|
| 131 | recorrido que hace 1 de 3 → la skill NO queda comprobada y el motivo dice «1 de 3»; recorrido 3 de 3 → comprobada. Plan de 0 pasos → tampoco comprobada |
| 132 | skill `[okcd «nwp1» → A]`, `[key:enter → B]` → UN paso con campo `okcd`, texto «nwp1», tecla «enter» y llegada **B**. Un `key:f3` que no sigue a nada tecleado se queda como paso propio |
| 133 | batch con un paso de texto sobre `campo:x` → quien escribe recibe («campo:x», «hola»), no solo «hola» |
| 134 | skill con dos tecleados narrados; el modelo dice que uno es navegación → un solo hueco, y el otro se reproduce tal cual. Sin respuesta del modelo → los dos huecos de la regla, intactos |
| 135 | lo preguntado lleva los N pasos de la demo con su selector, su valor y lo dicho — y nada más; un campo que la demo no tocó no viaja |

### Las fases

| Fase | Promesas | Qué toca |
|---|---|---|
| 6 | 131, 132, 133 | `Navigation/LaComprobacion.cs` (nuevo, puro) · `InstanciarSkill` pliega la tecla · `RecorrerSegunElNucleo.Paso` gana `Tecla` y el escritor recibe el campo · `FaceWindow` deja de certificar a ciegas |
| 7 | 134, 135 | `Teach/LoQueSePregunta.cs` (nuevo, puro) · `TeachSession` manda los pasos y devuelve lo interpretado · `SkillEnsenada.ConLoInterpretado` · **Graph**: `process-video` acepta `steps` y contesta `campos`/`recuerdos` |
| 8 | 136 | `LoQueSePregunta.SinPantalla` · `TeachSession.InterpretarPasosAsync` · **Graph**: ruta `teach/interpret-steps` (solo texto) y el prompt a un solo sitio (`src/domain/teach/interpretarPasos.js`) |

**La cuenta de Gemini está sin créditos desde el 2026-09-03** (`429: Your prepayment credits are
depleted`, cuatro veces), y eso destapó algo que estaba mal planteado: la interpretación colgaba del
video, cuando el juicio que se le pide —«`nwp1` es cómo se llega», «70 es el peso de este
paciente»— se decide con los pasos y con lo narrado, sin ver la pantalla. De ahí la fase 8 y la
promesa **136**: tres peldaños, video → texto → la regla del narrado, y cada uno solo se pisa si
falló el de arriba. Con eso se puede trabajar hoy, sin saldo. **Lo que sigue pendiente de saldo es
solo el peldaño de arriba**, que es el que además ve la pantalla.

**Medido contra producción el 2026-09-03**, ruta `teach/interpret-steps`, tres pasos:

| campo | veredicto del modelo | significado |
|---|---|---|
| `sap:wnd[0]/tbar[0]/okcd` = «nwp1» | `esDato: false` — navegación, se reproduce | el código de transacción |
| `…/txtY0000000-ZTXTPESO` = «70» | `esDato: true` — hueco | el peso en kilos |
| `…/txtY0000000-ZTXTTASIS` = «120» | `esDato: true` — hueco | la presión sistólica |

Y un hallazgo de esa misma medición: **omitir un campo no es neutral**. En la primera llamada el
modelo contestó por el peso y se saltó `okcd`, porque las reglas decían «si dudas, déjalo fuera» —
y del otro lado un campo ausente significa «no llegó a mirarlo», así que mandaba la regla del
narrado, que es la que rompía la skill. El silencio no llegaba como silencio: llegaba como la
respuesta contraria. Ahora se pide una entrada **por cada** paso que tecleó algo, con la duda
resuelta explícitamente hacia `esDato: true`, que es el lado prudente.

## Tercera tanda: enseñar es dar contexto, comprobar es alcanzar el objetivo (2026-09-03, noche)

> El dueño, tras la primera demo con video: *«siento que todavía estamos tratando la enseñanza como
> si fueran workflows»*. Y tenía razón: `InstanciarSkill.Pasos → batch` ES un guion que se
> reproduce. Lo que se quiere no son workflows hardcodeados sino **contexto**: objetivo, elementos
> tocados, lo dicho, lo que el video entendió — y que al comprobar Ü **alcance el objetivo** con
> ese contexto, colgando de cada elemento que use un recuerdo, para que la vez siguiente el batch
> lo pueda planificar entero antes del primer clic.

### Lo que la demo del 19:49 dejó claro

| Hecho | Medida | Qué significa |
|---|---|---|
| Ü actuó mientras se le enseñaba | `19:51:47 llamada recibida: map_scroll` · tres batches intentados | la voz seguía en modo asistente: la narración se leía como órdenes |
| 26 clics en el árbol, 1 paso observado | `clic-sap … espero a la observación` ×26 · `teach-sap: observado` ×1 | lo que Ü hizo con sus manos y lo que se dijo no entró en la skill |
| La comprobación pasó en 77 ms | `hice los 1 paso(s): quedaste en «…SESSION_MANAGER…»` | la llegada del último paso era la pantalla de SALIDA: no exigía nada |
| Los recuerdos existían y no se veían | `2 recuerdo(s) colgado(s)` · `vista de recuerdos: 0 de 2 localizados` | la vista miraba solo por UIA (resuelto trayendo la 008 a esta rama) |

### Las dos etapas, dichas por el dueño

1. **Enseñar.** Ü es un aprendiz que escucha: no toca nada, asiente («ajá», «entiendo») y solo
   habla si le hablan directamente. Todo por prompt. Lo que ve —qué se señala, qué se toca— y lo
   que oye es el contexto.
2. **Comprobar.** Ü es activo: con el objetivo y todo el contexto de la demo, **va hacia el
   objetivo** narrando lo que entendió («lo primero que me dijiste es escribir NWP1 aquí…»), y
   cuelga un recuerdo de cada elemento que usa. Para antes de cualquier puerta peligrosa.

### La especificación

| # | Promesa | Fase |
|---|---|---|
| 138 | mientras enseñas, Ü no tiene manos: ninguna herramienta que mueva la pantalla está en su catálogo, sus instrucciones son las de un aprendiz que escucha, y al terminar vuelve todo | 9 |
| 139 | comprobar es un ENCARGO al piloto con el objetivo y todo el contexto de la demo —pasos, lo dicho, lo que el video entendió—, nunca un guion que se reproduce; el encargo pide colgar un recuerdo de cada elemento que use y parar en las puertas peligrosas | 10 |
| 140 | la llegada del último paso es donde ACABÓ la demo, y se sabe, no se estima: una skill siempre tiene un destino que exigir | 11 |

**La asimetría que hace segura la 139**, y va al contrato en sus palabras: *el modelo puede nombrar
un DESTINO; nunca un VALOR.* Nombrar un destino que no existe cuesta una parada honesta de la
compuerta; nombrar un campo que no existe costaría un dato clínico en el sitio equivocado. El
encargo lleva selectores y lo dicho; los valores de la demo no viajan.

### Con qué se juzga

| # | Cómo |
|---|---|
| 138 | `ModoAprendiz.Utensilios(todos)` no contiene `map_take`, `map_type`, `map_scroll`, `map_go_to`, `map_unblock`, `map_open_app`, `file_open`; sí contiene `map_pointing_at` y `map_where_am_i`. Las instrucciones no contienen «Tienes manos» ni «NO PIDAS PERMISO» y sí piden callar |
| 139 | `ElEncargoDeComprobar.Texto(skill)` contiene la descripción, cada selector tocado con lo que se dijo, las sugerencias del video, la palabra «recuerdo»; NO contiene ningún valor tecleado de la demo, ni «reproduce». Y lleva a dónde tiene que llegar |
| 140 | `Empaquetar(…, dondeTermina)` deja `DondeTermina`; el último paso gana esa llegada al instanciarse; sin `dondeTermina` la skill no se empaqueta |

### Las fases

| Fase | Promesas | Qué toca |
|---|---|---|
| 9 | 138 | `Teach/ModoAprendiz.cs` (puro) · `ConversacionEnVivo.CambiarModoAsync` (re-envía `session.update`) · 🎓 entra y sale del modo |
| 10 | 139 | `Navigation/ElEncargoDeComprobar.cs` (puro) · `OnComprobarAprendizaje` → `_loop.RunAsync(encargo, origen)` y veredicto con `ElRescate.Aterrizo` |
| 11 | 140 | `SkillEnsenada.DondeTermina` · `WorkflowTeachSession` lo lee al parar · `InstanciarSkill` lo exige en el último paso |


### Lo que quedó medido (2026-09-03, 20:45)

- Contrato **140/140** con las dos ramas juntas (la 008 se trajo por fast-forward: mismo `main`, sin conflicto de código; solo el contrato chocaba por añadir promesas en el mismo sitio).
- Sabotajes, uno por uno: devolverle las manos al aprendiz enrojece la 138 (ocho comprobaciones, todas suyas); meter el «70» en el encargo enrojece la 139; quitar el destino al último paso enrojece la 140 con el texto exacto del «comprobada en 77 ms». Cero `.bak` sueltos.
- El video volvió a funcionar (saldo recargado): `procesado: 2 nota(s), interpretación de 330 car.` y el modelo dejó «NWP1» como navegación (`1 hueco por la regla → 0 tras su criterio`).

## Cuarta tanda: la comprobación se ve y se oye (2026-09-03, 21:00)

> **Primera comprobación que llega al final.** 25 s, del menú de SAP a la ficha de triage de
> GIRALDO, aterrizaje verificado por la compuerta. Y el dueño pidió tres cosas, las tres justas.

### Lo que hizo, medido

```
20:57:39  encargo de 4093 car. · atado a «sapgui://QAS»
20:57:45  turno 1: 1 acción(es)
            paso 1 [click] «IS-H: Pto.tbjo.clínico»   676 ms
            paso 2 [click] «Triage»                    602 ms
            paso 3 [click] «GIRALDO»                   151 ms
            paso 4 [click] «Triage»                    759 ms
20:58:05  ■ fin · 2 acción(es) · ATERRIZÓ en «sapgui://QAS/NWP1/SAPLY000/0001»
```

**Los cuatro pasos en UNA acción**: el piloto usó `map_batch`. Y lo usó porque el encargo se lo
ofrecía — la frase *«map_batch si tienes varios pasos claros»* la escribí yo. Comprobar no es
correr: es aprender delante de alguien. Un batch de cuatro clics en dos segundos no deja narrar, no
deja mover la cara, y no deja colgar un recuerdo por elemento.

Y **cero `map_esto_es` durante la corrida**: los 3 recuerdos del log son los que el cliente cuelga
ANTES de arrancar, sacados de la demo. El piloto no aprendió nada por su cuenta.

| Lo pedido | Qué pasó | Por qué |
|---|---|---|
| Narrar elemento a elemento | solo habló al final | el encargo ofrecía `map_batch`, y el piloto optimizó |
| Con la voz de Ü | voz del sintetizador de Windows | `Speak` cae al sintetizador cuando la voz viva está cerrada, y comprobar la había cerrado |
| Un recuerdo por elemento usado | 3, todos de la demo, ninguno del recorrido | sin pasar por cada elemento no hay dónde colgarlo |
| El recuerdo, lo entendido | la transcripción cruda: *«Sí, perfecto. Entonces, um Entonces...hay otra forma de entrar y es con esto de aquí, de ISH, um le das slowly click aquí»* | `RecuerdosDeUnaSkill` cuelga `Dicho` tal cual |

Lo que duele del último: para ESE MISMO elemento el modelo ya había entendido algo mucho mejor y
estaba guardado en la skill — *«Al hacer doble clic aquí, se ingresa directamente a la misma
pantalla que con el comando NWP1»*. Se colgó el balbuceo y se ignoró la frase buena.

### La especificación

| # | Promesa | Fase |
|---|---|---|
| 141 | comprobar va elemento a elemento y no en tanda: el encargo no ofrece el batch, y por cada elemento pide decir en voz qué entendió, actuar, y colgar ahí el recuerdo | 12 |
| 142 | durante la comprobación Ü habla con SU voz: si la voz viva está abierta, lo que el piloto narra sale por ella y no por el sintetizador del sistema | 12 |
| 143 | el recuerdo que se cuelga es lo ENTENDIDO, no la transcripción: donde el modelo tiene un significado para ese elemento, gana al balbuceo de la demo | 13 |

### Con qué se juzga

| # | Cómo |
|---|---|
| 141 | `Texto(skill)` NO contiene «map_batch» ni «varios pasos»; contiene «uno», «en voz» y `map_esto_es` en la misma instrucción del elemento |
| 142 | `PedirRespuesta("di exactamente: X")` lleva `response.create` **y** el texto; `PedirRespuesta()` sin argumentos sigue siendo el de siempre (el contrato de la voz lo llama así) |
| 143 | skill con `Dicho` largo y una sugerencia del modelo para el MISMO selector → el recuerdo es el del modelo; sin sugerencia, lo dicho sigue colgándose (no se pierde nada) |

### El hueco que se dice y no se tapa

El botón `#tbbtn=ZMEDTRIAGE` tiene su recuerdo y **no se puede dibujar**: SAP no da geometría para
los botones de la barra de un ALV (`vista de recuerdos …: 0 de 1 localizados`). Está guardado y el
piloto lo usa; lo que no hay es caja que iluminar. Es el mismo hueco ya escrito para las filas de
rejilla, y se deja dicho en vez de fingir una caja.


## Quinta tanda: la caja del botón de barra, preguntándole a la API (2026-09-03, 21:30)

> El dueño: *«Soluciona esto de la geometría de los botones. Tenemos que lograr conseguirla.»* Y
> antes de dar por buena una limitación documentada, se le pregunta a la API (aprendizaje nº13).

### Lo medido con una sonda de solo lectura

**1. La Scripting API de SAP no da la caja, y ahora está MEDIDO.** `DumpState("Toolbar")` sobre el
ALV del triage devuelve 85 entradas para 12 botones, y son exactamente los siete getters que ya
usábamos —`GetToolbarButtonId/Icon/Type/Enabled/Text/Checked/Tooltip`— repetidos por índice, más
`ToolbarButtonCount`. 7×12+1 = 85. Ni un `Left`, ni un `Top`, ni un `Rect`. Los getters geométricos
que se probaron (`GetToolbarButtonLeft/Top/Width/Height/Position/Rect/Bounds`) no existen.

**2. UIA SÍ la da.** La misma pantalla, por Automation:

```
ZMEDTRIAGE   «Triage»              → Button [793,227 84x30]
PSRC         «Buscar pacientes»    → Button [620,227 171x30]
APPST        «Pasar a Consulta»    → Button [1037,227 170x30]
Z_URG_ADULTOS «Imp Manilla ADULTOS»→ Button [353,267 210x30]
```

8 de 12 casaron por nombre a la primera; UIA ve 96 elementos dentro de la ventana de SAP, 89 con
caja. **La creencia «dentro de SAP, UIA no ve nada» es cierta para los campos del DYNPRO y falsa
para la barra de un ALV**, que son botones de Windows de verdad. Nadie lo había mirado porque la
frase se escribió una vez para el dynpro y se heredó para todo.

### La conclusión, que es la promesa

**La identidad la pone SAP; la caja la pone UIA.** Ninguno de los dos puede solo: SAP sabe que ese
botón se llama `ZMEDTRIAGE` y que su texto es «Triage», pero no dónde está; UIA sabe que hay un
botón «Triage» en `793,227` pero no que se llama `ZMEDTRIAGE` ni cómo pulsarlo sin coordenadas.

| # | Promesa | Fase |
|---|---|---|
| 144 | la caja de un botón de barra de SAP la da UIA y su identidad la da SAP: se casa por el texto que SAP declara, y si no se encuentra NO se dibuja nada | 14 |

**El guardarraíl es el aprendizaje nº4**: una caja que miente es peor que no tener caja. De los 12
botones, 4 no casaron por nombre a la primera; ésos no se dibujan, y no se estima su posición a
partir del ancho del texto ni del rectángulo del shell.

## Cierre

- [ ] Rama A: 120-123 verdes · rotas a propósito una por una
- [ ] Rama B: 124-128 verdes
- [ ] `.\scripts\verificar.ps1` con evidencia en `out\evidencia.md`
- [ ] Corrida real: enseñar → comprobar → ✓ sobre el triage de QAS, con log y horas
- [ ] LA MÉTRICA: viajes al modelo por sección, antes y después
- [ ] Aviso en `#miracle-updates` con `/avisa`
- [ ] Estado: **implementado** (AAAA-MM-DD)
