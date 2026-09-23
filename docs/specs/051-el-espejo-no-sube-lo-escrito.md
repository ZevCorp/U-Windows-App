# El espejo no sube lo escrito, y el log deja de guardarlo

Estado: **en construcción** (spec y fases escritas; contrato sin tocar) · Spec 051 · 2026-09-23 ·
Rama `jero/el-espejo-no-sube-lo-escrito`, desde `main` en `043addc` · promesas **394–399**

> **Qué se arregla.** Lo que Ü escribe en SAP, lo que la persona le dice y lo que el piloto narra con
> la nota delante acaba en el log local, y el log entero sale del equipo hacia el backend por el
> espejo. En un hospital eso es la historia clínica saliendo de la máquina del médico por un canal de
> diagnóstico que nadie miró con esos ojos. Se arregla **la clase de error, no el caso** (patrón nº5):
> se cuentan todos los sitios que escriben un valor en el log y todas las salidas del log hacia fuera.
>
> **Números.** 386–393 son de la rama A de Jev (`origin/jero/jev-la-distribucion-se-valida-entera`),
> 371–385 de la D, 361–370 de la C y 351–359 de la B; `main` tiene repetidas 335–345, que no son de
> esta spec. Comprobado el 2026-09-23 sobre las 53 refs `origin/*` tras `fetch` **(M)**: ningún
> `Contrato.cs` pasa de la 393 y ninguna spec tiene una fila `| 394 |` o superior. **Aviso de
> choque:** la 046 de la rama A *propone* que el documento sin commitear del checkout principal
> (`046-el-decisor-lleva-todo-el-computer-use.md`, nueve promesas hoy numeradas 335–343) pase a 050
> con sus promesas en **394+**. Si el dueño acepta esa propuesta, esta spec y aquella chocan en
> 394–399; esta las toma porque hoy están libres en `origin/*` y lo dice aquí para que se decida
> en el PR, sin reciclar ningún número.
>
> **Marcas.** **(M)** medido: `grep`, `git log`, conteo sobre el log. **(D)** deducido del código.

## Diagnóstico: qué se midió

Sobre `main` en `043addc`, en el worktree de esta rama, el 2026-09-23.

| Qué | Medida | Fuente |
|---|---|---|
| El espejo se engancha a **toda** línea del log | `LogBus.Anotado += Reflejar`; `Reflejar` solo calla la etiqueta `telemetry` y manda el texto entero en `label` **y** en `detail` | `Telemetry/EspejoDelLog.cs:43, 59-70` **(M, lectura)** |
| Se enciende siempre que hay correo | `TelemetryBus.Init` y en la línea siguiente `EspejoDelLog.Encender()`; sin correo `Init` no crea cliente y `Emit` es no-op | `Ui/FaceWindow.xaml.cs:1451, 1454`; `Telemetry/Telemetry.cs:37-47` **(M, lectura)** |
| Adónde va | `POST /api/v1/agent/events` en lotes de 100 cada 2 s, cola de 2000 | `Telemetry.cs:74-77, 125-150` **(M, lectura)** |
| El rellenador escribe en el log el valor de cada campo clínico | `«{campo.Label}» aceptó «{valor}»…` y `«{campo.Label}» = «{despues}» (pedido «{valor}»)`, etiqueta `dictado` | `Clinical/RellenadorSap.cs:405, 409` **(M)** |
| Cuánto log hay | **674** llamadas a `LogBus.Log(` en **77** archivos, **82** etiquetas literales distintas, y **26** embudos que meten entero en el log el texto de otro componente (`Log = s => LogBus.Log("workflow", s)`, `Diagnostic += … LogBus.Log("sap", msg)`…) | `grep` sobre `windows-client/src` y `windows-graph/src` **(M)** |
| Sitios que meten en el log lo escrito, lo dicho o lo narrado | **29** (tabla de abajo); más **4** que meten el objetivo, que esta spec deja en el log local a propósito (§*Diseño*) | `grep` + lectura de cada uno **(M)** |
| Salidas del equipo que llevan líneas o texto libre | **2 canales**: el espejo (1 sitio) y las llamadas directas a `TelemetryBus.Emit` (**10** sitios, 3 con texto libre: el objetivo, el resumen y el contexto) | `grep 'TelemetryBus\.Emit('` **(M)** |
| Otras salidas de líneas de log | **0** que lean el log: nadie más lee `LogBus.Snapshot()` (solo `LogWindow`, en pantalla) ni el archivo (`TodayFile()` solo lo usa `LogBus`) **(M)**; que ninguno de los 47 `PostAsync/SendAsync` del cliente lleve una línea suelta en su cuerpo es **(D)**: se leyeron por archivo, no uno a uno | `grep` |
| El log local de esta máquina lo guarda hoy | 17 archivos (2026-08-06 → 09-22): **107** líneas «usuario dijo:» y **92** «Ü dijo:» en 6 archivos; 1 «✓ escrito «» y 1 «→ map_type» | conteo con `grep -c`, **sin leer el contenido** **(M)** |
| Y el espejo estaba encendido en los mismos archivos | 09-07 (espejo ×3, «usuario dijo» ×19) y 09-21 (espejo ×1, «usuario dijo» ×19) | ídem **(M)**; que esas líneas llegaran al backend **no se midió** (hay líneas `telemetry: flush` de subidas fallidas) **(D)** |
| La spec 012 ya lo había visto | su diagnóstico nombra `RellenadorSap.cs:405, 409` y `EspejoDelLog.cs:59-71` y cuenta «6 sitios» | `docs/specs/012-…md` **(M, lectura)** |
| La promesa 170 de la 012 («el log del rellenador dice etiqueta y longitud») **no existe** | la 012 sigue «propuesto»; sus cuatro números 168–171 los tomó `c53f4c1` (#60) **el mismo 2026-09-07** para la lección («170. cada clic físico de la demostración deja UN evento…») | `Contrato.cs:470-473`; `git log -S` **(M)** |
| El espejo **no** es posterior a la 012 | nació el 2026-08-16 en `4b58344` (#11); la 012 es del 2026-09-07 | `git log --diff-filter=A` **(M)** |
| Un comentario que se cree guardia | «El TEXTO de un `type` se registra recortado […] Queda solo en el log LOCAL, nunca sale hacia Graph» — falso desde el 2026-08-16 | `Agent/AgentLoop.cs:300-303` **(M)**; aprendizaje nº18 |
| Precedentes que ya lo hacen bien | «frase de {f.Length} caracteres», «nota de {nota.Length} caracteres», «transcripción guardada · {largo} caracteres», «tecleado …: {n} carácter(es)» | `DictadoEnVivo.cs:235`, `EjecutorDeExportaciones.cs:194`, `ClinicaClient.cs:129`, `UiaSurface.cs:403` **(M)** |
| El contrato ya sabe juzgar el log y el código fuente | escucha `LogBus.Anotado` por reflexión (209–224) y lee las fuentes por `U_REPO` (la 164, sin carteles) | `Contrato.cs:9020`, `:7955-7985` **(M)** |
| Choque posible con trabajo abierto | el checkout principal (rama `experimento/reemplazo-jeff`) tiene `SapGuiSurface.cs` modificado sin commitear, y esta spec toca dos líneas de ese archivo (E13, E14) | `git status` del checkout principal **(M)** |

### El censo: los sitios que meten en el log lo escrito, lo dicho o lo narrado

Contados con estas búsquedas sobre `windows-client/src` y `windows-graph/src`, y **leído cada
resultado** (un `grep` de nombres no basta: `RelatoDeEscribir` repite el valor sin llamarse `valor`):

```
grep -rn 'LogBus.Log' … | grep -iE '\{[^}]*(valor|value|texto|text\b|\.Text|nota|note|transcri|dictad|frase|content|context|goal|objetivo|escrib|args|consulta|pedido|despues|dijo|input)'
grep -rnE '(error|err|porque|motivo)\s*=\s*\$".*\{(texto|valor|value|text|leido|pedido)[^}]*\}'
grep -rn '=> LogBus.Log\|+= (.*LogBus.Log'     # los 26 embudos, y lo que cada productor mete por ellos
```

**E — lo que se escribe en un campo o una ventana (16)**

| # | Sitio | Etiqueta | Qué lleva |
|---|---|---|---|
| E1 | `Clinical/RellenadorSap.cs:405` | `dictado` | «aceptó «{valor}» pero quedó vacío» |
| E2 | `Clinical/RellenadorSap.cs:409` | `dictado` | «= «{despues}» (pedido «{valor}»)» |
| E3 | `Mcp/SurfaceMapTools.cs:1132-1133` | `batch` | el recorrido: «escribir «{p.Texto}»» por paso |
| E4 | `Mcp/SurfaceMapTools.cs:2068-2069` | `mapa-mcp` | «→ {tool} {args_}»: **todos** los argumentos con su valor (`text=`, `decir=`, `pasos=`…) |
| E5 | `Mcp/SurfaceMapTools.cs:2158-2159` | `mapa-mcp` | «← … {r[..200]}»: la respuesta, que repite el valor (`RelatoDeEscribir`, `:2847`: «escribí «{texto}»»; `:2701`: «tecleé «{texto}»») |
| E6 | `Mcp/SurfaceMapTools.cs:2700` | `mapa-mcp` | «✓ tecleado «{texto}» en la terminal» |
| E7 | `Mcp/SurfaceMapTools.cs:2792` | `mapa-mcp` | «escrito «{texto}» pero sin Enter» |
| E8 | `Mcp/SurfaceMapTools.cs:2819` | `mapa-mcp` | «✓ escrito «{texto}» en {selector}» |
| E9 | `Ui/FaceWindow.xaml.cs:855` | `sentido-sap` | «no pude escribir «{texto}»» |
| E10 | `Agent/AgentLoop.cs:295` (vía `Describe`, `:307`) | `agent` | «type (x,y) «{Short(a.Text, 40)}»» |
| E11 | `Mcp/WorkflowMcpRunner.cs:36` | `workflow` | «context='{context}'»: los valores con que el cerebro rellena el workflow |
| E12 | `Voice/ConversacionEnVivo.cs:1327` | `voz-tiempo` | «→ {linea}»: la primera línea del resultado de la herramienta, que para escribir es «escribí «…»» |
| E13 | `windows-graph/src/Surfaces/SapGuiSurface.cs:535` | (error devuelto) | «puse «{texto}» y el campo dice «{leido}»» → entra al log por `RellenadorSap.cs:385` (`dictado`), por la línea ✗ del player (`WorkflowPlayer.cs:499`, embudo a `workflow`, `exportar`, `uia`…) y por las respuestas del mapa |
| E14 | `windows-graph/src/Surfaces/SapGuiSurface.cs:1035` | (error devuelto) | ««{valor}» no es ni la clave ni el texto de ninguna opción…», por los mismos caminos |
| E15 | `windows-graph/src/Surfaces/UiaSurface.cs:1542` | (error devuelto) | «no se pudo seleccionar «{value}»», por los mismos caminos |
| E16 | `Ui/WorkflowLibraryWindow.xaml.cs:174` | `workflow-ui` | «primer workflow crudo: {raw[0]}»; si el resumen trae los pasos, trae los valores grabados al enseñar **(D)** |

**D — lo que se dice (5)**

| # | Sitio | Etiqueta | Qué lleva |
|---|---|---|---|
| D1 | `Voice/ConversacionEnVivo.cs:1938` | `voz-viva` | «Ü dijo: {_fraseU}» |
| D2 | `Voice/ConversacionEnVivo.cs:1939` | `voz-viva` | «usuario dijo: {_fraseUsuario}» |
| D3 | `Voice/ConversacionEnVivo.cs:1868` | `voz-viva` | «← {plano[..400]}»: el mensaje crudo que no se traduce (entre ellos, deltas de texto y de argumentos del delegado) |
| D4 | `Voice/ConversacionEnVivo.cs:2416` | `recuerdo` | «LECCIÓN PERDIDA: «{corta}»»: la frase de la persona |
| D5 | `Voice/RecordatoriosEnVivo.cs:29` | `recordatorio` | «entregado …: {recordatorio.Text}» |

**N — lo que se narra con la nota delante (8)**

| # | Sitio | Etiqueta | Qué lleva |
|---|---|---|---|
| N1 | `Piloto/ElPiloto.cs:98` | `piloto` | «{tipo}: {texto[..300]}»: la narración del piloto, que recibe el encargo con la nota |
| N2 | `Piloto/ElPiloto.cs:101` | `piloto` | `e.Data` crudo cuando no es JSON |
| N3 | `Piloto/ElPiloto.cs:103` | `piloto-err` | la salida de error del piloto, cruda |
| N4 | `Ui/FaceWindow.xaml.cs:3186` | `comprobar` | «piloto: {relato}» |
| N5 | `Ui/FaceWindow.xaml.cs:3325` | `comprobar` | «piloto: {r.Ultimo}» |
| N6 | `Ui/FaceWindow.xaml.cs:5598-5599` | `envio` | «… · {r.Ultimo}» |
| N7 | `Ui/ConsultaWindow.cs:2090` | `consulta` | «cuenta del envío: {cuenta}» (la devuelve el piloto) |
| N8 | `Agent/AgentLoop.cs:180` | `agent` | «■ fin · {Short(summary, 160)}»: lo que Ü contestó |

**O — el objetivo (4), que se queda en el log local** (ver §*Diseño*, decisión 4)

| # | Sitio | Etiqueta | Qué lleva |
|---|---|---|---|
| O1 | `Agent/AgentLoop.cs:92` | `agent` | «▶ objetivo: «{Short(goal, 160)}»» |
| O2 | `Mcp/SurfaceMapTools.cs:1767` | `tramo` | «→ {r}», con «tramo en marcha: «{_objetivo}»…» (`ElTramo.cs:106`) |
| O3 | `Mcp/SurfaceMapTools.cs:1778` | `tramo` | «ALTO: {r}», con «paré el tramo «{_objetivo}»…» (`ElTramo.cs:117`) |
| O4 | `Actions/Freno.cs:128` | `freno` | «paro «{Tarea}»», con `Tarea` = «tramo: {objetivo}» (`ElTramo.cs:132`) |

Descartados al leerlos **(M)**: `memoria` (`ConversacionEnVivo.cs:2060, 2107, 2266`): `resultado.Response`
son frases fijas de `MemoriaPersonal.cs:40-62`, no repiten lo guardado. `exportar`, `clinica`,
`consulta` (salvo N7) y `DictadoEnVivo` ya registran longitudes.

### Las salidas del equipo

| # | Sitio | Qué manda hoy |
|---|---|---|
| S1 | `Telemetry/EspejoDelLog.cs:66` | **cada línea** del log, texto entero en `label` y `detail` |
| S2 | `Agent/AgentLoop.cs:96` | `conscious_run_start` · `label: goal` — **el objetivo entero**, sin recortar |
| S3 | `Agent/AgentLoop.cs:133` | `conscious_run_end` error · `label: e.Message` |
| S4 | `Agent/AgentLoop.cs:181` | `conscious_run_end` · `label: summary` — **lo que Ü contestó** |
| S5 | `Agent/AgentLoop.cs:225` | `analyze` · `surfaceUrl: loc.Id` (en `uia://`, el título de la ventana: clase L) |
| S6 | `Agent/AgentLoop.cs:275` | `mcp` · `label: a.Tool` |
| S7 | `Agent/AgentLoop.cs:277` | `action` · `label: a.Kind`, `detail: {x, y}` |
| S8 | `Mcp/WorkflowMcpRunner.cs:39` | `workflow_start` · `label: context` — **los valores del workflow** |
| S9 | `Mcp/WorkflowMcpRunner.cs:55` | `workflow_step` · `label: outcome.Label` |
| S10 | `Mcp/WorkflowMcpRunner.cs:69` | `workflow_end` · `label: result.Error` (lleva E13–E15 cuando el paso falló al escribir) |

## Por qué esto va dirigido por especificación

Porque el fallo **no se ve desde ningún sitio**: el log se escribe, el espejo sube, el panel enseña,
y nada falla. Es el patrón nº10 en su forma más pura —parece que funcionó— y el aprendizaje nº18 dos
veces: un comentario (`AgentLoop.cs:302`) juraba que el texto tecleado «nunca sale hacia Graph», y
una spec (la 012) lo había diagnosticado sin que su promesa llegara nunca al contrato; su número lo
tomó otra el mismo día y nadie lo notó, porque una promesa que no existe no se pone roja. Una prueba
escrita después del arreglo se escribiría mirando los sitios arreglados; aquí se escribe mirando el
censo, y el censo lo juzga el propio contrato.

**La regla del flujo, y no tiene excepciones:** ninguna línea de producción entra antes que la
promesa que la juzga. Cada fase empieza con el contrato ROTO y termina con el contrato INTACTO.

## Diseño

### Decisión 1: el espejo pasa a **lista blanca de etiquetas**, y lo de fuera sube solo como cadencia

El defecto de fondo es que el espejo **sube por defecto**: cualquier `LogBus.Log` que alguien añada
mañana sale del equipo sin que nadie lo decida. Arreglar los 29 sitios deja intacta esa puerta.

| Alternativa | Por qué no |
|---|---|
| Lista negra (callar las etiquetas malas) | Es lo que ya hay (calla `telemetry`) y es lo que falló: la etiqueta nueva de mañana sube. Un guardia que hay que acordarse de actualizar degrada en silencio a «siempre sí» (aprendizaje nº18). |
| Tapar en el espejo lo que va entre «» | Tapa también las etiquetas de campo («Talla»), que son justo lo que diagnostica, y deja pasar el valor que no va entre comillas (`context='…'`, `usuario dijo: …`). Un criterio por la forma del texto es una comparación que da falso para la forma que no se me ocurrió (aprendizaje nº16). |
| Solo arreglar en origen | Necesario (decisión 2), no suficiente: el sitio 30 que alguien escriba mañana sale. |
| **Lista blanca + cadencia** | Por defecto no sale nada. Una etiqueta entra **a propósito**, con su porqué, y el contrato congela la lista: añadir una es cambiar la promesa 394, que es el momento de leer sus sitios. El fallo posible pasa a ser «el panel no ve X» —visible e inocuo— en vez de «la historia clínica sale» —invisible—. |

**Qué sube de una línea fuera de la lista:** su etiqueta y su longitud, nada más —`label:
"‹voz-viva› · línea de 83 car."`, `detail: { tag, largo }`—. Así el panel sigue viendo **que** el
subsistema habla y cuándo (el pulso en vivo no se apaga), y no **qué** dice. El volumen de eventos
es el mismo que hoy; lo que se aligera es cada evento.

**La lista inicial (16), y la regla para entrar.** Entra una etiqueta si (a) ninguno de sus sitios
—ni de los embudos que la usan— lleva E, D, N ni O tras las fases de esta spec, **medido en el
censo**, y (b) hace falta para uno de los tres diagnósticos remotos para los que el espejo existe
(su propio comentario, `EspejoDelLog.cs:8-15`): el equipo vive y está al día; la exportación a SAP
llegó o dónde se paró; el workflow que falló y en qué paso.

| Etiqueta | Para qué | Nota |
|---|---|---|
| `arranque`, `fatal`, `unobserved-task`, `update`, `instalador`, `instancia`, `backend`, `espejo`, `reintento` | el equipo vive, arranca y está al día | `espejo` la comparten `EspejoDelLog` y `EspejoDeConsulta` (cuentas y códigos HTTP) |
| `exportar`, `consulta`, `clinica`, `dictado` | la exportación: reclamada, N escritos / sin llenar, reportada; el campo que no cuajó | `dictado` y `consulta` entran **después** de arreglar E1, E2 y N7 |
| `workflow`, `workflow-ui`, `align` | el workflow que falló, en qué paso, en qué ubicación, y si se pudo traer la app | `workflow` y `workflow-ui` entran después de E11 y E16; los ✗ del player, después de E13–E15 |

**Fuera por diseño**, y la 394 lo congela: las 14 etiquetas del censo que llevan lo escrito, lo dicho,
lo narrado o el objetivo — `batch`, `mapa-mcp`, `sentido-sap`, `agent`, `voz-tiempo`, `voz-viva`,
`recuerdo`, `recordatorio`, `piloto`, `piloto-err`, `comprobar`, `envio`, `tramo`, `freno`. Fuera
también, por la regla y no por el censo, otras 51 (`sap`, `decisor`, `inspector`, `clic-sap`,
`mano`, `teach`…): suben como cadencia hasta que alguien las lea y las meta. Y `telemetry` sigue
sin subir **nada, ni cadencia**: es la realimentación que ya explica `EspejoDelLog.cs:24-33` (un
`flush` fallido encolaría otro evento que fallaría igual), y la cadencia la reabriría.

### Decisión 2: en origen, el valor se registra por su **forma**, en un solo sitio

`U.WindowsClient.Diagnostics.SinValor`, estática y pura:

| Método | Devuelve | Para |
|---|---|---|
| `Forma(string? v)` | `‹N car.›`; `‹vacío›` si es nulo o vacío | todo valor que hoy se interpola |
| `Contraste(string? pedido, string? leido)` | `‹N car.›, igual a lo pedido` · `‹N car.›, igual salvo mayúsculas o espacios` · `‹N car.›, distinto de lo pedido (‹M car.›)` | el rellenador: lo que la línea de hoy servía para ver (¿SAP transformó el valor?) sin el valor |
| `Tapar(string texto, params string?[] valores)` | `texto` con cada aparición de cada valor cambiada por su `Forma` —entre «» siempre, suelta si tiene ≥ 2 caracteres— | una respuesta o una cuenta que **repite** un valor conocido (E5, E12) |

Un solo sitio porque el aprendizaje nº16 ya se pagó cuatro veces: dos formas de escribir «longitud»
acaban siendo dos criterios. Sin hash ni prefijo del valor: un hash de «170» se invierte probando.

**Lo que se pierde en el log local, dicho:** «qué se escribió» exacto. El incidente del 2026-07-26
(`AgentLoop.cs:300`) necesitaba «qué y dónde»; queda «dónde, cuánto y si coincide», y lo escrito sigue
donde vive a propósito: en SAP, en la nota del portal y en `ConversacionPersonal`. El log no tiene que
ser una segunda copia de la historia clínica.

### Decisión 3: las otras salidas se **censan**, y el texto libre sube por su forma

S2, S4 y S8 pasan a `SinValor.Forma(goal | summary | context)`. Y como cada `TelemetryBus.Emit` es una
salida del equipo y son pocas (10), el contrato congela **cuáles son y qué mandan** en `label` y
`detail`: una nueva, o una que cambie lo que manda, pone el contrato rojo nombrando archivo y línea.
Es la misma idea que la lista blanca aplicada al otro canal: **toda salida del equipo está censada**.
Para poder mirar lo que sale sin red, `TelemetryBus` gana un evento `Emitido(kind, phase, label,
detalleJson)` que se dispara en cada `Emit`, haya cliente o no, y solo serializa si alguien escucha.

### Decisión 4: el objetivo (O) se queda en el log local y **no sale**

El objetivo es lo que se le pidió a la máquina y es el centro del diagnóstico local («▶ objetivo»,
«tramo en marcha»). No es literalmente un valor escrito en un campo, ni la nota, ni el dictado, pero
puede citarlos («escribe 38,5 en temperatura»). Se decide: **en el log local se queda** (O1–O4 no
cambian), y **no sale por ningún canal**: sus etiquetas (`agent`, `tramo`, `freno`) están fuera de la
lista y S2 sube su forma. Es la decisión más discutible de la spec y se deja escrita para el crítico.

## La especificación

El enunciado es el que va **literalmente** en `tests/ContratoDelGrafo/Contrato.cs`.

| # | Promesa | Fase |
|---|---|---|
| **394** | el espejo del log sube por lista blanca: de una línea cuya etiqueta no está en EspejoDelLog.EtiquetasQueSuben solo sale del equipo su etiqueta y su longitud —ni en label ni en detail va una letra de su texto—; la lista vive en un solo sitio, cada etiqueta con su porqué, y ninguna de las catorce que llevan lo que se escribe, lo que se dice, lo que se narra o el objetivo (batch, mapa-mcp, sentido-sap, agent, voz-tiempo, voz-viva, recuerdo, recordatorio, piloto, piloto-err, comprobar, envio, tramo, freno) está en ella; y lo que sí está sube entero, con su texto en label y en detail | 2 |
| **395** | ninguna otra salida hacia la telemetría lleva lo escrito, lo dicho ni el objetivo: el objetivo y el resumen de una corrida consciente y el contexto de un workflow suben por su longitud; y cada TelemetryBus.Emit del código está en el censo de la 051 con lo que manda en label y en detail —uno nuevo, o uno que cambie lo que manda, pone el contrato rojo nombrando archivo y línea— | 3 |
| **396** | el log del rellenador dice etiqueta, longitud y si coincide, nunca el valor: la línea de un campo escrito lleva su etiqueta, la longitud de lo leído y si es igual a lo pedido, igual salvo mayúsculas o espacios, o distinto; la de un campo que quedó vacío, su etiqueta y la longitud de lo pedido; y ninguna de las dos contiene lo pedido ni lo leído | 1 |
| **397** | lo que las manos escriben no queda en el log: la línea de cada llamada al mapa nombra sus argumentos de lugar (target, selector, surface, app, path, exit, workflow_id) con su valor y cualquier otro solo por su longitud; la línea de la respuesta tapa esos valores aunque la respuesta los repita; y el recorrido por lotes, el «type» del agente consciente y el contexto con que se invoca un workflow van por su longitud | 4 |
| **398** | lo que se dice no queda en el log: lo que dijo la persona y lo que contestó Ü al cerrar un turno, el mensaje crudo del servidor que no se traduce y lo que narra el piloto se registran por su longitud —y su tipo—, nunca por su texto | 5 |
| **399** | el censo de la 051 queda cerrado en el código: ninguna línea que llega al log —LogBus.Log, el diario del player, el diagnóstico de una superficie— ni ningún error que una superficie devuelve interpola lo escrito, lo dicho o lo narrado salvo por SinValor, ningún «valor» ni «texto» aparece suelto en una de esas líneas en todo windows-client y windows-graph —su longitud sí—, y los 29 sitios contados el 2026-09-23 lo cumplen | 5 |

**La que cierra el asunto es la 394**: mientras el espejo suba por defecto, cualquier arreglo en
origen es una lista de 29 que el sitio 30 deja vieja. Las 396–399 cierran el log local; la 394 y la
395 cierran la salida del equipo aunque el log local volviera a llenarse.

## Con qué se juzga cada una (sin pantalla, sin SAP, sin red)

Todo por reflexión: lo que aún no existe se pide por nombre y cuenta como `Pendiente` (regla del
flujo). Los valores de los fixtures son **inventados y obvios**, y ninguno es subcadena de una
etiqueta: `ZZ-INVENTADO-051`, `Paciente Inventado Cero`, `999000111`, `38,5 inventado`. Una
comprobación de «no contiene» se hace sobre `label` **y** sobre `detail` serializado a JSON.

| # | Juez | Sabotaje de una línea (verificado por diff; ojo CRLF) |
|---|---|---|
| 394 | Suscribir `TelemetryBus.Emitido`, `EspejoDelLog.Encender()`, y `LogBus.Log` de: una línea con valor inventado bajo **cada una de las 14** etiquetas de fuera; otra bajo una etiqueta que no existe (`etiqueta-nueva-051`); una `telemetry` («flush: tiempo agotado»), que no debe producir **ningún** evento; y dos bajo etiquetas de la lista (`update`: «no se pudo comprobar actualizaciones: tiempo agotado»; `exportar`: «trabajo 7: 2 campo(s) escritos, 1 sin llenar»). Las 15 de fuera (las 14 y la nueva): su evento existe, lleva la etiqueta y la longitud, y **no** lleva el valor ni en `label` ni en `detail`. Las dos de la lista: llegan con el texto entero en `label` y en `detail`. `EtiquetasQueSuben` no tiene ninguna de las 14, cada entrada tiene un porqué no vacío, y es **exactamente** la lista de 16 de esta spec (añadir una es cambiar esta promesa). `Apagar()` en `finally` | en `Reflejar`, la condición de la lista (`EtiquetasQueSuben.ContainsKey(etiqueta)`) pasa a `true` |
| 395 | (a) `new WorkflowMcpRunner(new GraphConfig { ApiKey = "" }, vozFalsa).RunAsync("wf-051", "Paciente Inventado Cero · 999000111", ct)`: sin clave (`IsConfigured` falso, `GraphConfig.cs:104`; la clave embebida por CI se pisa a vacía) vuelve antes de tocar nada, **después** de emitir `workflow_start`; `Emitido` lo captura sin el contexto y con `‹35 car.›`. (b) Las fuentes (`U_REPO`, como la 164): cada `TelemetryBus.Emit(` de `windows-client/src` y `windows-graph/src` se extrae hasta su `;` y se compara con el censo congelado —10 entradas: archivo, `kind`, expresión de `label`, expresión de `detail`—; S2, S4 y S8 con `SinValor.Forma(…)`. Sin `U_REPO`: «NO PUDE JUZGARLA», que cuenta como incumplida (aprendizaje nº17) | en `WorkflowMcpRunner`, `label: SinValor.Forma(context)` vuelve a `label: context` |
| 396 | `RellenadorSap.LineaDeEscrito("Talla", "170", "170")`, `("Talla", "ZZ-INVENTADO-051", "zz-inventado-051 ")`, `("Talla", "170", "17")` y `LineaDeVacio("Talla", "ZZ-INVENTADO-051")`, estáticas y puras. Todas llevan «Talla» y `‹N car.›`; las tres primeras dicen, en orden, «igual a lo pedido», «igual salvo mayúsculas o espacios», «distinto de lo pedido»; **ninguna** contiene «170», «17», «ZZ-INVENTADO-051» ni su minúscula. Y `SinValor.Forma(null)` = `SinValor.Forma("")` = `‹vacío›` | `LineaDeEscrito` devuelve `$"«{etiqueta}» = «{leido}»"` en vez de pasar por `SinValor.Contraste` |
| 397 | (a) `new SurfaceMapTools(() => null).Call("herramienta_de_prueba_051", { text: "ZZ-INVENTADO-051", decir: "Paciente Inventado Cero", target: "Talla" })` con `LogBus.Anotado` escuchando (herramienta inexistente: cae en «herramienta de mapa no soportada», `SurfaceMapTools.cs:2130`, sin tocar nada; el contrato ya llama a `Call` en `:8513` y `:11000`): la línea «→» lleva `target=Talla`, `text=‹16 car.›`, `decir=‹23 car.›` y ninguno de los dos valores. (b) `SurfaceMapTools.LineaDeRespuesta(12, SurfaceMapTools.RelatoDeEscribir("ZZ-INVENTADO-051", "", ""), args)` no contiene el valor. (c) `LineaDelRecorrido` con un paso de texto y uno de salida: el de salida con su nombre, el de texto con su forma. (d) `AgentLoop.Describe` (privada, por reflexión) de un `type` con `Text` inventado: `‹N car.›`, sin el texto. (e) La línea «MCP invoca» de la ejecución de 395(a): sin el contexto | en `LineaDeLlamada`, la pertenencia a los argumentos de lugar pasa a `true` (todo argumento con su valor) |
| 398 | (a) `ConversacionEnVivo.Procesar` (reflexión, con los sobres de GPT-Live que ya usan 209–224): una transcripción de la persona «Paciente Inventado Cero tiene 38,5 inventado», un trozo de Ü «anoto ZZ-INVENTADO-051» y el cierre de turno → las líneas `voz-viva` dicen «usuario dijo: ‹N car.›» y «Ü dijo: ‹M car.›» y ninguna línea anotada contiene esas frases. (b) Un mensaje que no se traduce con la frase dentro (`session.instructions.appended` con un campo `nota`): la línea «←» lleva su `type` y su longitud, no la frase. (c) `ElPiloto.LineaDelPiloto(json)`, pura, con `{"tipo":"texto","texto":"Paciente Inventado Cero"}` y con una línea que no es JSON: tipo y forma, sin el texto | «usuario dijo: {SinValor.Forma(…)}» vuelve a «usuario dijo: {_fraseUsuario}» |
| 399 | Las fuentes (`U_REPO`). **(a) El censo:** los 29 fragmentos viejos de la tabla (`«{valor}»` en `RellenadorSap.cs`, `{args_}` y `«{texto}»` en `SurfaceMapTools.cs`, `{_fraseUsuario}` en `ConversacionEnVivo.cs`, `{recordatorio.Text}`, `«{texto}»` en `FaceWindow.xaml.cs`, `«{leido}»` en `SapGuiSurface.cs`, `«{value}»` en `UiaSurface.cs`, `{raw[0]}`…) ya no están en su archivo, y cada uno de los 29 sitios pasa por `SinValor.` — el juez dice cuántos de 29 y cuáles faltan. **(b) La regla:** en todo `windows-client/src` y `windows-graph/src`, en cada sentencia que llega al log (desde `LogBus.Log(`, ` L($"`, `Log?.Invoke(`, `Diagnostic?.Invoke(` hasta su `;`) y en cada `error = $"` / `err = $"`, ningún hueco `{…}` nombra como identificador entero `valor` o `texto` fuera de una llamada a `SinValor.` —salvo seguido de `.Length`, y sin mirar dentro de los literales del hueco—; el juez nombra archivo y línea de cada uno. **Solo esos dos nombres, y medido por qué:** con `value`, `despues`, `leido` y `pedido` la misma búsqueda daba 15 aciertos de los que **5 eran falsos** (`IconosDelEscritorio.cs:278`, el `value` de un setter booleano; `Desplazamiento.cs:84`, un porcentaje de scroll; `FaceWindow.Escritorio.cs:49`, un escritorio leído; `FaceWindow.xaml.cs:4326`, el literal «pedido»; `UiaSurface.cs:403`, que ya registra la longitud); con `valor` y `texto` y la excepción de `.Length`, **0 falsos** sobre `043addc` **(M)**. Los sitios con los otros nombres (E13, E15, E2) los cubre el censo de (a). Sin `U_REPO`: «NO PUDE JUZGARLA», incumplida | en `FaceWindow.xaml.cs:855`, «no pude escribir «{SinValor.Forma(texto)}»» vuelve a ««{texto}»»: solo la 399 se pone roja (ningún juez dinámico pasa por ahí) |

**Un sabotaje por promesa, y se comprueba el sabotaje** (CLAUDE.md, 2026-08-21): el diff contra la
copia de seguridad tiene que enseñar la línea cambiada antes de contar la roja, y el build no se
silencia. Los archivos de este repo son CRLF; un patrón con `\n` no aplica y deja un verde falso.

## Las fases

Una rama para la spec; cada fase un commit que pone verde su promesa sin romper las anteriores.

| Fase | Promesa que pone verde | Qué toca | Sitios | Terminado |
|---|---|---|---|---|
| **0** | las seis en **rojo**, por las razones escritas | `tests/ContratoDelGrafo/Contrato.cs` (394–399; `Pendiente` por reflexión para `SinValor`, `TelemetryBus.Emitido`, `EspejoDelLog.EtiquetasQueSuben`, `RellenadorSap.LineaDeEscrito/LineaDeVacio`, `SurfaceMapTools.LineaDeLlamada/LineaDeRespuesta/LineaDelRecorrido`, `ElPiloto.LineaDelPiloto`; la 399 roja por su propio escáner: 0 de 29) | — | `CONTRATO ROTO` con exactamente 394–399 rojas, cada una diciendo qué falta; las anteriores intactas |
| **1** | **396** | `windows-client/src/Diagnostics/SinValor.cs` (nuevo, puro) · `Clinical/RellenadorSap.cs` (`LineaDeEscrito`, `LineaDeVacio`, y `:405`, `:409` las usan) | 2 (E1, E2) | 396 verde; sabotaje visto rojo |
| **2** | **394** | `Telemetry/Telemetry.cs` (evento `Emitido`) · `Telemetry/EspejoDelLog.cs` (`EtiquetasQueSuben` con su porqué, cadencia para lo de fuera) | 1 (S1) | 394 verde; sabotaje visto rojo |
| **3** | **395** | `Agent/AgentLoop.cs:96, 181` · `Mcp/WorkflowMcpRunner.cs:39` | 3 de 10 (S2, S4, S8) | 395 verde; sabotaje visto rojo |
| **4** | **397** | `Mcp/SurfaceMapTools.cs` (E3–E8: `LineaDeLlamada`, `LineaDeRespuesta`, `LineaDelRecorrido`, y `:2700`, `:2792`, `:2819` con `Forma`) · `Ui/FaceWindow.xaml.cs:855` (E9) · `Agent/AgentLoop.cs:307` (E10) y **su comentario falso de `:300-303`** · `Mcp/WorkflowMcpRunner.cs:36` (E11) · `Voice/ConversacionEnVivo.cs:1327` (E12, con `Tapar`) · `windows-graph/src/Surfaces/SapGuiSurface.cs:535, 1035` y `UiaSurface.cs:1542` (E13–E15: el error dice qué regla falló y la forma, no el valor) · `Ui/WorkflowLibraryWindow.xaml.cs:174` (E16: id y número de pasos) | 14 (E3–E16) | 397 verde; sabotaje visto rojo |
| **5** | **398** y **399** | `Voice/ConversacionEnVivo.cs:1868, 1938, 1939, 2416` (D1–D4) · `Voice/RecordatoriosEnVivo.cs:29` (D5) · `Piloto/ElPiloto.cs:98, 101, 103` (N1–N3, con `LineaDelPiloto`) · `Ui/FaceWindow.xaml.cs:3186, 3325, 5599` (N4–N6) · `Ui/ConsultaWindow.cs:2090` (N7) · `Agent/AgentLoop.cs:180` (N8) | 13 (D1–D5, N1–N8) | 398 y 399 verdes (399: 29 de 29); sabotajes vistos rojos; `CONTRATO INTACTO` |

Total en origen: **29 sitios** (2 + 14 + 13) y **4 salidas** (S1, S2, S4, S8). El número va en
cada commit de fase (patrón nº5).

**Zonas.** `windows-graph/` (E13–E15): riesgo bajo, pero **el checkout principal tiene
`SapGuiSurface.cs` modificado sin commitear**: se avisa al dueño antes de la fase 4. UI de
`windows-client` (`FaceWindow`, `ConsultaWindow`, `WorkflowLibraryWindow`): riesgo **alto**; los
cambios son de una línea cada uno y se dice en el aviso. El resto son servicios (telemetría, voz,
clínico, mapa).

## Lo que el panel ve, antes y después

| | Antes (hoy) | Después |
|---|---|---|
| Arranque, actualización, instancia, errores fatales | ✓ con texto | ✓ con texto |
| La exportación: trabajo reclamado, campos escritos / sin llenar, resultado reportado | ✓ con texto | ✓ con texto |
| El campo que no cuajó | ✓ con **el valor** («= «38,5» (pedido «38.5»)») | ✓ con etiqueta, longitud y si coincide («‹4 car.›, distinto de lo pedido (‹4 car.›)») |
| El workflow: paso a paso, ✗ con motivo, ubicación | ✓, y el ✗ de un campo lleva el valor | ✓, y el ✗ dice la regla que falló sin el valor |
| La conversación (usuario dijo / Ü dijo), las manos del mapa, el tramo y Jev, el piloto, SAP e inspector, la enseñanza | ✓ con texto, **valores incluidos** | cadencia: etiqueta, hora y longitud; el texto, en el log local |
| Eventos directos (`conscious_run_*`, `workflow_*`, `action`, `mcp`, `analyze`) | ✓, con el objetivo, el resumen y el contexto enteros | ✓, con esos tres por su longitud |

Lo que se pierde en remoto es real: diagnosticar la voz, las manos o Jev de otra máquina vuelve a
pedir el archivo de log, como antes del 2026-08-16. Es el precio de que la historia clínica no salga;
cada etiqueta que se quiera recuperar entra por la regla de la decisión 1, con su lectura.

## Lo que queda fuera, y se dice

- **El backend** (otro repo). Lo que ya se subió **antes de hoy** no se puede recoger desde el
  cliente: purgar `agent/events` de tipo `log`, y un filtro en el servidor como segundo cinturón, son
  trabajo de Graph. Y que el panel pinte bien la cadencia (`‹etiqueta› · línea de N car.`), también.
- **Lo leído de la pantalla (clase L)**: filas de ALV, textos de nodo, títulos de ventana, etiquetas
  de paso grabadas. Puede llevar nombres y documentos, y no es lo escrito ni lo dicho. Sigue saliendo
  por las etiquetas de la lista que lo llevan en sus embudos (`workflow`, `exportar`: etiqueta de un
  paso que es una fila, `pathname` de `uia://` en `WorkflowPlayer.cs:248, 316`) y por S5 y S9.
  Ejemplos contados, **no un censo**: `SapGuiSurface.cs:1883, 2037`, `WorkflowPlayer.cs:193, 248,
  316, 499`, `FaceWindow.xaml.cs:332, 345, 358`, `InspectorDiagnostics.cs:104`. Pide su propia spec
  (propuesta: 052, «lo leído tampoco sube»), y `sap` es la primera etiqueta que entraría a la lista
  cuando esté.
- **El objetivo en el log local** (decisión 4).
- **Lo que viaja a los modelos** (Graph y su escudo, TypeSafe): es la 012 (168, 169, 171) y la 046
  (393). Esta spec no lo toca.
- **El `catch { }` mudo de `LogBus.Log`** alrededor de `Anotado` (`LogBus.cs:57`): patrón nº3, pero
  cambiarlo cambia a todos los oyentes del log; se anota como hallazgo.
- **La 012**: su fase 3 (el log del rellenador) la absorbe esta spec como 396. Sus 168, 169 y 171
  siguen sin contrato y **sin número**: los cuatro se reciclaron el 2026-09-07. Se le dice al dueño
  en el PR; esta spec no la renumera.

## Nivel 4: la corrida a mano (la hace el dueño; esta rama no ejecuta `U.exe`)

Con la copia de la rama compilada en Release, el correo puesto (telemetría encendida) y SAP QAS:

1. Rellenar dos pantallas con **valores inventados** —triage (`SAPLY000`) y admisión (`NV2000`)—,
   una de ellas por la exportación desde el portal y otra por la voz («escribe … en …»).
2. **Log local** (`%LOCALAPPDATA%\U\logs\u-AAAAMMDD-*.log`): `grep -c` de cada valor inventado → 0;
   las líneas `dictado` llevan la etiqueta y «igual a lo pedido»; «usuario dijo: ‹N car.›».
3. **Panel del Provider Studio**: las líneas de `update`, `exportar`, `dictado` y `workflow` con su
   texto; las de `voz-viva` y `mapa-mcp` como cadencia; buscar cada valor inventado → 0.
4. Pegar en el PR las líneas con **hora**, no «probado», y las dos pantallas con nombre.

## Hallazgos

- **2026-09-23 · La 012 no llegó al contrato, y nadie lo vio.** Sus promesas 168–171 se escribieron
  en la spec y nunca en `Contrato.cs`; `c53f4c1` (#60) usó esos cuatro números ese mismo día. Una
  promesa que no existe no puede ponerse roja: la fuga que la 012 diagnosticó siguió abierta 16 días.
  Corrige el encargo de esta spec: el espejo **no** es posterior a la 012 (nació el 2026-08-16) y la
  012 **sí** lo había nombrado; lo que faltó fue implementarla.
- **2026-09-23 · Un comentario guardián falso.** `AgentLoop.cs:300-303` afirma que el texto de un
  `type` «nunca sale hacia Graph»; sale desde que existe el espejo. Se corrige en la fase 4.
- **2026-09-23 · La etiqueta `espejo` la usan dos cosas distintas** (`EspejoDelLog` y
  `EspejoDeConsulta`). No molesta a la lista; confunde al leer el log.
- **2026-09-23 · Preguntas para el crítico.** (1) ¿El objetivo debe salir también del log local
  (decisión 4)? (2) ¿Es aceptable que `workflow` y `exportar` sigan subiendo la clase L hasta la 052,
  o deben ir como cadencia hasta entonces? (3) ¿Congelar la lista de 16 y el censo de los 10 `Emit`
  en el contrato es la fricción correcta o una que se acabará saltando? (4) La regla de la 399 busca
  dos nombres (se estrechó de seis por 5 falsos positivos medidos): ¿qué sitio real lleva un valor
  con otro nombre y se le escaparía?

## Cierre

- [ ] Todas las promesas verdes (`.\scripts\contrato-del-grafo.ps1` → CONTRATO INTACTO), y el de la voz intacto
- [ ] Un sabotaje por promesa, visto rojo y verificado por diff
- [ ] `.\scripts\verificar.ps1` pasa, con evidencia en `out\evidencia.md`
- [ ] Nivel 4 en ≥2 pantallas, con nombre: triage (`SAPLY000`) y admisión (`NV2000`)
- [ ] Estado de este documento: **implementado** (AAAA-MM-DD)
