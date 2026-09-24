# El espejo no sube lo escrito, y el log deja de guardarlo

Estado: **en construcción** (spec y fases revisadas tras el crítico: 9 refutaciones, las 9
comprobadas ciertas y aplicadas) · **fase 0 hecha**: las seis promesas en `Contrato.cs`, rojas por sus
razones, con los censos como datos y un lector de fuentes que corrigió tres medidas de esta spec
(el ancla de E17, la quinta ruta `/agent/` y un sitio que la 399(b) encuentra fuera del censo) ·
**fase 1 hecha** (396 verde) · **fase 2 hecha** (394 verde: el espejo sube por línea marcada) ·
**fase 3 hecha** (395 verde: las salidas directas suben por forma, tipo y número) · Spec 051 · 2026-09-23 ·
Rama `jero/el-espejo-no-sube-lo-escrito`, desde `main` en `043addc` · promesas **394–399**

> **Qué se arregla.** Lo que Ü escribe en SAP, lo que la persona le dice y lo que el piloto narra con
> la nota delante acaba en el log local, y el log entero sale del equipo hacia el backend por el
> espejo. En un hospital eso es la historia clínica saliendo de la máquina del médico por un canal de
> diagnóstico que nadie miró con esos ojos. Se arregla **la clase de error, no el caso** (patrón nº5):
> se cuentan todos los sitios que escriben un valor en el log y todas las salidas del log hacia fuera.
>
> **Números.** 386–393 son de la rama A de Jev (`origin/jero/jev-la-distribucion-se-valida-entera`),
> 371–385 de la D, 361–370 de la C y 351–359 de la B; `main` tiene repetidas 335–345, que no son de
> esta spec. Comprobado el 2026-09-23 sobre las 53 refs `origin/*` y las ramas locales tras `fetch`
> **(M)**: ningún `Contrato.cs` pasa de la 393 (el más alto: `jero/jev-la-distribucion-se-valida-entera`,
> 393) y solo esta spec tiene filas `| 394 |` o superiores. **Aviso de choque:** la 046 de la rama A
> *propone* que el documento sin commitear del checkout principal
> (`046-el-decisor-lleva-todo-el-computer-use.md`, nueve promesas hoy numeradas 335–343) pase a 050
> con sus promesas en **394+**. Si el dueño acepta esa propuesta, esta spec y aquella chocan en
> 394–399; esta las toma porque hoy están libres en `origin/*` y lo dice aquí para que se decida
> en el PR, sin reciclar ningún número.
>
> **Marcas.** **(M)** medido: `grep`, `git log`, conteo sobre el log. **(D)** deducido del código.

## Diagnóstico: qué se midió

Sobre `main` en `043addc`, en el worktree de esta rama, el 2026-09-23. **Ámbito de todos los
recuentos:** `windows-client/**/*.cs` sin `bin` ni `obj` —que incluye `windows-client/App.xaml.cs`,
fuera de `src` y compilado en `U.exe`— y `windows-graph/src`.

| Qué | Medida | Fuente |
|---|---|---|
| El espejo se engancha a **toda** línea del log | `LogBus.Anotado += Reflejar`; `Reflejar` solo calla la etiqueta `telemetry` y manda el texto entero en `label` **y** en `detail` (`kind: "log"`, `phase: etiqueta`) | `Telemetry/EspejoDelLog.cs:43, 58-70` **(M, lectura)** |
| Se enciende siempre que hay correo | `TelemetryBus.Init` y en la línea siguiente `EspejoDelLog.Encender()`; sin correo `Init` no crea cliente y `Emit` es no-op | `Ui/FaceWindow.xaml.cs:1451, 1454`; `Telemetry/Telemetry.cs:37-47` **(M, lectura)** |
| Adónde va | `POST /api/v1/agent/events` en lotes de 100 cada 2 s, cola de 2000; cada evento lleva **siete** campos de texto (`kind`, `phase`, `appId`, `surfaceUrl`, `workflowId`, `runId`, `label`) más `detail` | `Telemetry.cs:48-62, 74-77, 125-150` **(M, lectura)** |
| Cómo se serializa | `BackendClient.Json` solo fija `WhenWritingNull`: el codificador es el de defecto de `System.Text.Json`, que escapa lo no ASCII (`é` → `é`) | `Backend/BackendClient.cs:36-39` **(M, lectura)**; el escape es comportamiento documentado de la biblioteca **(D)** |
| El rellenador escribe en el log el valor de cada campo clínico | `«{campo.Label}» aceptó «{valor}»…` y `«{campo.Label}» = «{despues}» (pedido «{valor}»)`, etiqueta `dictado` | `Clinical/RellenadorSap.cs:405, 409` **(M)** |
| Cuánto log hay | **683** llamadas a `LogBus.Log(` en **78** archivos (676 en los `src`, 7 en `App.xaml.cs`), **85** etiquetas literales distintas (3 solo en `App.xaml.cs`: `fatal`, `unobserved-task`, `instalador`), y **26** embudos que meten entero en el log el texto de otro componente (`Log = s => LogBus.Log("workflow", s)`, `Diagnostic += … LogBus.Log("sap", msg)`…) | `grep` **(M)** |
| Etiquetas y líneas no deciden lo mismo | con la lista blanca de 16 etiquetas de la primera versión de esta spec, **111** sentencias `LogBus.Log("<etiqueta de la lista>"` subirían enteras, más 4 embudos del player; **37** de las 111 interpolan texto ajeno cuyo contenido no está en el código (`.Message`, `{ex}`, `ToString()`, `{err}`, `{porque}`, `Recortar(cuerpo)`, `hecho.Porque`, `result.Error`) | `grep` **(M)**; casos: `EspejoDeConsulta.cs:141-142` (300 car. del cuerpo de error de Supabase), `RellenadorSap.cs:129` con `:454` (200 car. del cuerpo de `/api/v1/pipeline`, cuya petición lleva la nota), `App.xaml.cs:61, 80, 90` (`ex.ToString()`), `BackendClient.cs:117, 128` (el cuerpo entero en el mensaje) |
| El diario del player lleva la etiqueta de cada paso, y la de un select **es** el valor | `Publish(el, "select", el.Current.Name)`; `Label` sale de `LabelOf`, que devuelve primero `info.Name`: `Label == Value`. El player la escribe en `· paso N: … «{s.Label}»`, `→ paso N «{step.Label}»`, `⏸ {pause.Headline}` (= `paso N · select «{Label}»`) y `eligió «{step.Label}»`, y esas líneas entran por los embudos `workflow` (×3) y `exportar` (×1) | `UiaSurface.cs:2359, 2385, 828`; `WorkflowPlayer.cs:193, 316, 351, 511`; `StepPause.cs:55`; `WorkflowMcpRunner.cs:50`, `FaceWindow.xaml.cs:4309`, `WorkflowLibraryWindow.xaml.cs:58`, `EjecutorDeExportaciones.cs:263` **(M, lectura)** |
| Sitios que meten en el log lo escrito, lo dicho, lo narrado o el objetivo | **35** (tabla de abajo): 18 E, 5 D, 8 N y 4 O | `grep` + lectura de cada uno **(M)** |
| Salidas del equipo que llevan líneas o texto libre | **2 canales**: el espejo (1 sitio) y las llamadas directas a `TelemetryBus.Emit` (**10** sitios; 6 con texto libre o ajeno: el objetivo, el resumen, el contexto, el `Message` de un error, la etiqueta de un paso y el error de un workflow) | `grep 'TelemetryBus\.Emit('` **(M)** |
| Los canales, hoy | oyentes del log: `EspejoDelLog.cs:43` (`Anotado`) y `LogWindow.xaml.cs:16` (`Logged`, en pantalla); `Snapshot()` solo en `LogWindow.xaml.cs:13`; `TodayFile()` solo dentro de `LogBus`; rutas `/agent/`: `Telemetry.cs:111` (`/agent/register`), `:135` (`/agent/events`), `FaceWindow.xaml.cs:1014` (`/agent/usage`, solo cifras), `BackendClient.cs:92` (`/agent/turn`, el turno del cerebro) y `Credenciales/ClavesDelBackend.cs:221` (`GET /agent/claves`, las claves del backend: no lleva nada del log; la quinta la encontró el lector de fuentes de la fase 0, el `grep` de esta tabla no la había visto) **(M)**. Que ninguno de los 47 `PostAsync/SendAsync` del cliente lleve una línea suelta del log en su cuerpo es **(D)**: se leyeron por archivo, no uno a uno | `grep` |
| El log local de esta máquina lo guarda hoy | 17 archivos (2026-08-06 → 09-22): **107** líneas «usuario dijo:» y **92** «Ü dijo:» en 6 archivos; 1 «✓ escrito «» y 1 «→ map_type» | conteo con `grep -c`, **sin leer el contenido** **(M)** |
| Y el espejo estaba encendido en los mismos archivos | 09-07 (espejo ×3, «usuario dijo» ×19) y 09-21 (espejo ×1, «usuario dijo» ×19) | ídem **(M)**; que esas líneas llegaran al backend **no se midió** (hay líneas `telemetry: flush` de subidas fallidas) **(D)** |
| La spec 012 ya lo había visto | su diagnóstico nombra `RellenadorSap.cs:405, 409` y `EspejoDelLog.cs:59-71` y cuenta «6 sitios» | `docs/specs/012-…md` **(M, lectura)** |
| La promesa 170 de la 012 («el log del rellenador dice etiqueta y longitud») **no existe** | la 012 sigue «propuesto»; sus cuatro números 168–171 los tomó `c53f4c1` (#60) **el mismo 2026-09-07** para la lección («170. cada clic físico de la demostración deja UN evento…») | `Contrato.cs:470-473`; `git log -S` **(M)** |
| El espejo **no** es posterior a la 012 | nació el 2026-08-16 en `4b58344` (#11); la 012 es del 2026-09-07 | `git log --diff-filter=A` **(M)** |
| Un comentario que se cree guardia | «El TEXTO de un `type` se registra recortado […] Queda solo en el log LOCAL, nunca sale hacia Graph» — falso desde el 2026-08-16 | `Agent/AgentLoop.cs:300-303` **(M)**; aprendizaje nº18 |
| `windows-graph` es otro ensamblado | `windows-client` lo referencia (`ProjectReference` a `GraphWorkflows.csproj`, ensamblado `U.Graph`, espacio `U.Graph`); una clase de `U.WindowsClient` **no** se puede llamar desde `SapGuiSurface` ni `UiaSurface` | `WindowsClient.csproj:114`; `GraphWorkflows.csproj:24-25` **(M)** |
| Precedentes que ya lo hacen bien | «frase de {f.Length} caracteres», «nota de {nota.Length} caracteres», «transcripción guardada · {largo} caracteres», «tecleado …: {n} carácter(es)» | `Clinical/Transcripcion/DictadoEnVivo.cs:235`, `EjecutorDeExportaciones.cs:194`, `ClinicaClient.cs:129`, `UiaSurface.cs:403` **(M)** |
| El contrato ya sabe juzgar el log y el código fuente | escucha `LogBus.Anotado` por reflexión (209–224) y lee las fuentes por `U_REPO` (la 164, sin carteles) | `Contrato.cs:9020`, `:7955-7985` **(M)** |
| Choque posible con trabajo abierto | el checkout principal (rama `experimento/reemplazo-jeff`) tiene `SapGuiSurface.cs` modificado sin commitear, y esta spec toca dos líneas de ese archivo (E13, E14) | `git status` del checkout principal **(M)** |

### El censo: los sitios que meten en el log lo escrito, lo dicho, lo narrado o el objetivo

Contados con estas búsquedas sobre el ámbito de arriba, y **leído cada resultado** (un `grep` de
nombres no basta: `RelatoDeEscribir` repite el valor sin llamarse `valor`, y `FaceWindow.xaml.cs:735`
lo lleva como `dato`):

```
grep -rn 'LogBus.Log' … | grep -iE '\{[^}]*(valor|value|texto|text\b|\.Text|nota|note|transcri|dictad|frase|content|context|goal|objetivo|escrib|args|consulta|pedido|despues|dijo|input|dato|result)'
grep -rnE '(error|err|porque|motivo)\s*=\s*\$".*\{(texto|valor|value|text|leido|pedido)[^}]*\}'
grep -rn '=> LogBus.Log\|+= (.*LogBus.Log'     # los 26 embudos, y lo que cada productor mete por ellos
```

Cada fila lleva **el ancla** por la que la 399 encuentra la sentencia después del arreglo (un
literal o una llamada de la sentencia **nueva**, único en su archivo salvo donde se dice; medido
sobre `043addc` con `grep -cF`, y con la etiqueta delante donde el literal solo se repetía en un
comentario o en otra etiqueta: `✓ escrito`, `no pude escribir`, `piloto terminó`) y **los
huecos permitidos** en ella además de los que empiezan por `SinValor.` o `Linea…(`, o terminan en
`.Length`. Un hueco es cada `{…}` de una cadena interpolada y cada operando no literal de un `+`.

**E — lo que se escribe en un campo o una ventana (18)**

| # | Sitio | Etiqueta | Qué lleva hoy | Ancla | Huecos permitidos |
|---|---|---|---|---|---|
| E1 | `Clinical/RellenadorSap.cs:405` | `dictado` | «aceptó «{valor}» pero quedó vacío» | `LineaDeVacio(` | — |
| E2 | `Clinical/RellenadorSap.cs:409` | `dictado` | «= «{despues}» (pedido «{valor}»)» | `LineaDeEscrito(` | — |
| E3 | `Mcp/SurfaceMapTools.cs:1132-1133` | `batch` | el recorrido: «escribir «{p.Texto}»» por paso | `LineaDelRecorrido(` | — |
| E4 | `Mcp/SurfaceMapTools.cs:2068-2069` | `mapa-mcp` | «→ {tool} {args_}»: **todos** los argumentos con su valor (`text=`, `decir=`, `pasos=`…) | `LineaDeLlamada(` | — |
| E5 | `Mcp/SurfaceMapTools.cs:2158-2159` | `mapa-mcp` | «← … {r[..200]}»: la respuesta, que repite el valor (`RelatoDeEscribir`, `:2847`: «escribí «{texto}»»; `:2701`: «tecleé «{texto}»») | `LineaDeRespuesta(` | — |
| E6 | `Mcp/SurfaceMapTools.cs:2700` | `mapa-mcp` | «✓ tecleado «{texto}» en la terminal» | `✓ tecleado` | `tituloVentana` |
| E7 | `Mcp/SurfaceMapTools.cs:2792` | `mapa-mcp` | «escrito «{texto}» pero sin Enter» | `pero sin Enter` | `errEnter` |
| E8 | `Mcp/SurfaceMapTools.cs:2819` | `mapa-mcp` | «✓ escrito «{texto}» en {selector}» | `"mapa-mcp", $"✓ escrito` | `selector` |
| E9 | `Ui/FaceWindow.xaml.cs:855` | `sentido-sap` | «no pude escribir «{texto}»» | `"sentido-sap", $"no pude escribir` | `porque` |
| E10 | `Agent/AgentLoop.cs:307` (`Describe`, que usa `:295`) | `agent` | «type (x,y) «{Short(a.Text, 40)}»» | `"type" => $"type (` | `a.X`, `a.Y` |
| E11 | `Mcp/WorkflowMcpRunner.cs:36` | `workflow` | «context='{context}'»: los valores con que el cerebro rellena el workflow | `MCP invoca workflow_id=` | `workflowId` |
| E12 | `Voice/ConversacionEnVivo.cs:1327` | `voz-tiempo` | «→ {linea}»: la primera línea del resultado de la herramienta, que para escribir es «escribí «…»» | `"voz-tiempo", $"{ms,6} ms` | `ms,6`, `tool`, `(donde.Length > 0 ? $" «{donde}»" : "")` |
| E13 | `windows-graph/src/Surfaces/SapGuiSurface.cs:535` | (error devuelto) | «puse «{texto}» y el campo dice «{leido}»» → entra al log por `RellenadorSap.cs:385` (`dictado`), por la línea ✗ del player (`WorkflowPlayer.cs:499`, embudos `workflow`, `exportar`…) y por las respuestas del mapa | `no se quedó con lo puesto` | — |
| E14 | `windows-graph/src/Surfaces/SapGuiSurface.cs:1035` | (error devuelto) | ««{valor}» no es ni la clave ni el texto de ninguna opción…», por los mismos caminos | `no es ni la clave ni el texto` | `etiqueta`, `hay` (las opciones del desplegable: metadatos del campo) |
| E15 | `windows-graph/src/Surfaces/UiaSurface.cs:1542` | (error devuelto) | «no se pudo seleccionar «{value}»», por los mismos caminos | `no se pudo seleccionar` | — |
| E16 | `Ui/WorkflowLibraryWindow.xaml.cs:174` | `workflow-ui` | «primer workflow crudo: {raw[0]}»; si el resumen trae los pasos, trae los valores grabados al enseñar **(D)** | `primer workflow crudo` | — |
| E17 | `Ui/FaceWindow.xaml.cs:735` | `nucleo-http` | «no pude {accion} «{dato}»: {e.Message}», con `dato` = lo tecleado por el servidor del núcleo (`(sel, texto) => accionar("input", sel, texto)`, `:997`) | `"nucleo-http", $"no pude {accion}` (corregida en la fase 0: `"nucleo-http", $"no pude` está en **4** sentencias, `:596, 622, 735, 787` **(M)**) | `accion`, `e.Message` |
| E18 | `Agent/AgentLoop.cs:295` | `agent` | «… → {Short(result, 120)}»: para una acción `mcp`, `result` es la respuesta del mapa (`LocalMcp.cs:38` → `SurfaceMapTools.Call`), que repite el texto (`RelatoDeEscribir`, `:2847`) | `en '{where}' →` | `Describe(a)`, `where`, `Short(SinValor.Tapar(result, …), 120)` |

**D — lo que se dice (5)**

| # | Sitio | Etiqueta | Qué lleva hoy | Ancla | Huecos permitidos |
|---|---|---|---|---|---|
| D1 | `Voice/ConversacionEnVivo.cs:1938` | `voz-viva` | «Ü dijo: {_fraseU}» | `Ü dijo:` | — |
| D2 | `Voice/ConversacionEnVivo.cs:1939` | `voz-viva` | «usuario dijo: {_fraseUsuario}» | `usuario dijo:` | — |
| D3 | `Voice/ConversacionEnVivo.cs:1868` | `voz-viva` | «← {plano[..400]}»: el mensaje crudo que no se traduce (entre ellos, deltas de texto y de argumentos del delegado) | `"voz-viva", $"← ` | `tipo` (el `type` del mensaje, vocabulario del servidor) |
| D4 | `Voice/ConversacionEnVivo.cs:2416` | `recuerdo` | «LECCIÓN PERDIDA: «{corta}»»: la frase de la persona | `LECCIÓN PERDIDA` | — |
| D5 | `Voice/RecordatoriosEnVivo.cs:29` | `recordatorio` | «entregado …: {recordatorio.Text}» | `entregado a las` | `DateTimeOffset.Now:HH:mm:ss` |

**N — lo que se narra con la nota delante (8)**

| # | Sitio | Etiqueta | Qué lleva hoy | Ancla | Huecos permitidos |
|---|---|---|---|---|---|
| N1 | `Piloto/ElPiloto.cs:98` | `piloto` | «{tipo}: {texto[..300]}»: la narración del piloto, que recibe el encargo con la nota | `LineaDelPiloto(` (N1 y N2 pasan a **una** sentencia: `LogBus.Log("piloto", LineaDelPiloto(e.Data))`) | — |
| N2 | `Piloto/ElPiloto.cs:101` | `piloto` | `e.Data` crudo cuando no es JSON | ídem; y ninguna sentencia `LogBus.Log("piloto"` del archivo interpola `texto` ni `e.Data` fuera de `LineaDelPiloto` | — |
| N3 | `Piloto/ElPiloto.cs:103` | `piloto-err` | la salida de error del piloto, cruda | `"piloto-err"` | — |
| N4 | `Ui/FaceWindow.xaml.cs:3186` | `comprobar` | «piloto: {relato}» | `"comprobar", $"piloto:` (dos sentencias en el archivo: N4 y N5) | — |
| N5 | `Ui/FaceWindow.xaml.cs:3325` | `comprobar` | «piloto: {r.Ultimo}» | ídem | — |
| N6 | `Ui/FaceWindow.xaml.cs:5598-5599` | `envio` | «… · {r.Ultimo}» | `"envio", $"piloto terminó` | `(r.Termino ? "bien" : $"salida {r.Salida}")`, `reloj.ElapsedMilliseconds`, `r.CostoUsd:0.000` |
| N7 | `Ui/ConsultaWindow.cs:2090` | `consulta` | «cuenta del envío: {cuenta}» (la devuelve el piloto) | `cuenta del envío` | — |
| N8 | `Agent/AgentLoop.cs:180` | `agent` | «■ fin · {Short(summary, 160)}»: lo que Ü contestó | `■ fin` | `actions` |

**O — el objetivo (4)** (ver §*Diseño*, decisión 4: **ya no** se queda en el log local)

| # | Sitio | Etiqueta | Qué lleva hoy | Ancla | Huecos permitidos |
|---|---|---|---|---|---|
| O1 | `Agent/AgentLoop.cs:92` | `agent` | «▶ objetivo: «{Short(goal, 160)}»»; con el dictado de respaldo el objetivo **es** lo dictado (`FaceWindow.xaml.cs:2604-2607`) | `▶ objetivo` | `requireOrigin` |
| O2 | `Mcp/SurfaceMapTools.cs:1767` | `tramo` | «→ {r}», con «tramo en marcha: «{_objetivo}»…» (`ElTramo.cs:106`); el objetivo lo escribe el modelo de voz (`:1766`, argumento `objetivo`) | `"tramo", $"→ ` | — |
| O3 | `Mcp/SurfaceMapTools.cs:1778` | `tramo` | «ALTO: {r}», con «paré el tramo «{_objetivo}»…» (`ElTramo.cs:117`) | `ALTO:` | — |
| O4 | `Actions/Freno.cs:128` | `freno` | «paro «{Tarea}»», con `Tarea` = «tramo: {objetivo}» (`ElTramo.cs:132`) | `alto pedido` | `porque` |

Descartados al leerlos **(M)**: `memoria` (`ConversacionEnVivo.cs:2060, 2107, 2266`): `resultado.Response`
son frases fijas de `MemoriaPersonal.cs:40-62`, no repiten lo guardado. `exportar`, `clinica`,
`consulta` (salvo N7) y `DictadoEnVivo` ya registran longitudes.

### Las salidas del equipo

| # | Sitio | Qué manda hoy |
|---|---|---|
| S1 | `Telemetry/EspejoDelLog.cs:66` | **cada línea** del log, texto entero en `label` y `detail` |
| S2 | `Agent/AgentLoop.cs:96` | `conscious_run_start` · `label: goal` — **el objetivo entero**, sin recortar |
| S3 | `Agent/AgentLoop.cs:133` | `conscious_run_end` error · `label: e.Message` — el mensaje de `BackendClient`, que lleva **el cuerpo entero** de la respuesta (`BackendClient.cs:117`) |
| S4 | `Agent/AgentLoop.cs:181` | `conscious_run_end` · `label: summary` — **lo que Ü contestó** |
| S5 | `Agent/AgentLoop.cs:225` | `analyze` · `surfaceUrl: loc.Id` (en `uia://`, el título de la ventana: clase L; el de un navegador puede llevar lo tecleado en una búsqueda **(D)**) |
| S6 | `Agent/AgentLoop.cs:275` | `mcp` · `label: a.Tool` |
| S7 | `Agent/AgentLoop.cs:277` | `action` · `label: a.Kind`, `detail: {x, y}` |
| S8 | `Mcp/WorkflowMcpRunner.cs:39` | `workflow_start` · `label: context` — **los valores del workflow** |
| S9 | `Mcp/WorkflowMcpRunner.cs:55` | `workflow_step` · `label: outcome.Label` — la etiqueta del paso, que en un select es **la opción elegida** |
| S10 | `Mcp/WorkflowMcpRunner.cs:69-72` | `workflow_end` · `label: result.Error` (lleva E13–E15 cuando el paso falló al escribir, y la clase L del paso) |

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

### Decisión 1: el espejo sube por **línea marcada**, no por etiqueta; lo demás sube solo como cadencia

El defecto de fondo es que el espejo **sube por defecto**: cualquier `LogBus.Log` que alguien añada
mañana sale del equipo sin que nadie lo decida. Arreglar los 35 sitios deja intacta esa puerta.

| Alternativa | Por qué no |
|---|---|
| Lista negra (callar las etiquetas malas) | Es lo que ya hay (calla `telemetry`) y es lo que falló: la etiqueta nueva de mañana sube. Un guardia que hay que acordarse de actualizar degrada en silencio a «siempre sí» (aprendizaje nº18). |
| Tapar en el espejo lo que va entre «» | Tapa también las etiquetas de campo («Talla»), que son justo lo que diagnostica, y deja pasar el valor que no va entre comillas (`context='…'`, `usuario dijo: …`). Un criterio por la forma del texto es una comparación que da falso para la forma que no se me ocurrió (aprendizaje nº16). |
| Solo arreglar en origen | Necesario (decisión 2), no suficiente: el sitio 36 que alguien escriba mañana sale. |
| Lista blanca **de etiquetas** (la primera versión de esta spec) | Decide por etiqueta y no por línea: dentro de una etiqueta de la lista, **cualquier** línea sube entera, y esas etiquetas son justo las que manejan valores clínicos (`dictado`, `exportar`, `consulta`, `workflow`). Medido: 111 sentencias y 4 embudos del player subirían enteros, 37 de ellas con texto ajeno (mensajes de excepción, cuerpos HTTP) cuyo contenido no está en el código, y el diario del player lleva la etiqueta del paso, que en un select es el valor. Mañana un `LogBus.Log("exportar", $"… {v}")` subiría con el contrato verde. |
| **Línea marcada + cadencia** | Por defecto no sale nada entero. Sale entera **solo** la línea escrita con `LogBus.Publico(etiqueta, mensaje)`, y el contrato congela **cada** sentencia `LogBus.Publico` con su etiqueta, su ancla y sus huecos: añadir una o cambiar un hueco es cambiar la promesa 394, que es el momento de leerla. Un `LogBus.Log` nuevo, con cualquier etiqueta, sale como cadencia. El fallo posible pasa a ser «el panel no ve X» —visible e inocuo— en vez de «la historia clínica sale» —invisible—. |

**Mecánica.** `LogBus.Publico(tag, mensaje)` escribe la línea igual que `Log` (anillo, archivo,
`Logged`, `Anotado`) y además la marca. El espejo deja `Anotado` y se engancha a un evento que trae la
marca, `LogBus.AnotadoConMarca(tag, texto, publica)`, que dispara tanto `Log` (`publica: false`) como
`Publico` (`publica: true`). Un solo `Emit` en el espejo:

- `publica`: `label: texto`, `detail: { tag, text }` —lo de hoy—.
- no `publica`: `label` **exactamente** `‹{tag}› · línea de {texto.Length} car.` y `detail` con
  **exactamente** las claves `tag` y `largo`. El panel sigue viendo **que** el subsistema habla y
  cuándo (el pulso en vivo no se apaga), y no **qué** dice. El volumen de eventos es el mismo; lo que
  se aligera es cada evento.
- `telemetry`: **nada, ni cadencia**: es la realimentación que ya explica `EspejoDelLog.cs:24-33` (un
  `flush` fallido encolaría otro evento que fallaría igual), y la cadencia la reabriría.

**La regla para entrar al censo de `LogBus.Publico`.** Una sentencia entra si (a) cada hueco es una
cifra (cuentas, longitudes, milisegundos, código HTTP, número de intento), un identificador
(trabajo, workflow, versión), el tipo de una excepción (`GetType().Name` o `SinValor.Excepcion`), un
literal de un vocabulario cerrado del código (`desenlace` ∈ {`ok`, `needs_doctor`, `error`}), un
booleano o una llamada a `SinValor`; **nunca** un `Message`, un cuerpo HTTP, una etiqueta de paso, un
título de ventana ni un `ToString()` de algo ajeno —el criterio es medible porque se mira el hueco,
no el contenido—; y (b) hace falta para uno de los tres diagnósticos remotos para los que el espejo
existe (`EspejoDelLog.cs:8-15`): el equipo vive y está al día; la exportación a SAP llegó o dónde se
paró; el workflow que falló y en qué paso. Donde hoy la sentencia lleva el `Message`, la pública
lleva el tipo y el mensaje se queda en una línea `LogBus.Log` aparte, que sale como cadencia.

**El censo inicial de `LogBus.Publico` (26: 23 sentencias que cambian de `Log` a `Publico`, 3
nuevas).** La 394 lo congela entero: cada fila es (archivo, etiqueta, ancla, huecos exactos).

| P | Archivo | Etiqueta | Ancla | Huecos |
|---|---|---|---|---|
| P1 | `App.xaml.cs` (junto a `:61`, **nueva**) | `fatal` | `Ü no pudo arrancar ·` | `SinValor.Excepcion(ex)` |
| P2 | `App.xaml.cs` (junto a `:80`, **nueva**) | `fatal` | `Ü tropezó ·` | `SinValor.Excepcion(ex.Exception)` |
| P3 | `App.xaml.cs` (junto a `:90`, **nueva**) | `unobserved-task` | `tarea sin observar ·` | `SinValor.Excepcion(ex.Exception)` |
| P4 | `Update/Updater.cs:109` | `update` | `auto-update desactivado` | — |
| P5 | `Update/Updater.cs:128` | `update` | `no se pudo comprobar actualizaciones` | `ex.GetType().Name` |
| P6 | `Update/Updater.cs:141` | `update` | `versión nueva disponible` | `version` |
| P7 | `Update/Updater.cs:146` | `update` | `descargada y lista para aplicar` | `version` |
| P8 | `Update/Updater.cs:207` | `update` | `aplicando actualización y reiniciando` | — |
| P9 | `Update/Updater.cs:226` | `update` | `no se pudo dejar la actualización aplicándose` | `ex.GetType().Name` |
| P10 | `Ui/GuardiaDeInstancia.cs:131` | `instancia` | `segunda apertura ignorada` | — |
| P11 | `Clinical/ArranqueDeConsulta.cs:57` | `arranque` | `sesión restaurada del disco` | — (ternario de dos literales) |
| P12 | `Clinical/ArranqueDeConsulta.cs:90` | `arranque` | `ventana de consulta abierta` | — |
| P13 | `Telemetry/EspejoDelLog.cs:46` | `espejo` | `se está reflejando en el panel` | — |
| P14 | `Ui/FaceWindow.xaml.cs:4452` | `backend` | `sonda de vida falló` | `ex.GetType().Name` |
| P15 | `Clinical/Transcripcion/PoliticaDeReintento.cs:61` | `reintento` | `lanzó` | `Intentos`, `e.GetType().Name` |
| P16 | `Clinical/Transcripcion/PoliticaDeReintento.cs:75` | `reintento` | `se agotaron los` | `Intentos` |
| P17 | `Clinical/EjecutorDeExportaciones.cs:130` | `exportar` | `el ejecutor no arranca` | — |
| P18 | `Clinical/EjecutorDeExportaciones.cs:142` | `exportar` | `ejecutor de exportaciones parado` | — |
| P19 | `Clinical/EjecutorDeExportaciones.cs:194` | `exportar` | `reclamado · workflow` | `id`, `workflow`, `nota.Length` |
| P20 | `Clinical/EjecutorDeExportaciones.cs:239` | `exportar` | `campo(s) escritos` | `id`, `escritos.Count`, `sinLlenar.Count` |
| P21 | `Clinical/EjecutorDeExportaciones.cs:305` | `exportar` | `reportado` | `id`, `desenlace` |
| P22 | `Clinical/EjecutorDeExportaciones.cs:311` | `exportar` | `el servidor rechazó el resultado` | `id`, `(int)res.StatusCode` |
| P23 | `Clinical/EjecutorDeExportaciones.cs:316` | `exportar` | `no pude reportar (intento` | `id`, `intento`, `e.GetType().Name` |
| P24 | `Mcp/WorkflowMcpRunner.cs:36` (es E11) | `workflow` | `MCP invoca workflow_id=` | `workflowId`, `SinValor.Forma(context)` |
| P25 | `Mcp/WorkflowMcpRunner.cs:68` | `workflow` | `resultado: ok=` | `result.Ok`, `result.Tally`, `result.AlignedConsciously`, `fallo?.StepOrder`, `fallo?.ActionType` (`fallo`: el último paso no omitido con `Ok` falso) |
| P26 | `Mcp/WorkflowMcpRunner.cs:77` | `workflow` | `aprendiendo alineación` | `workflowId` |

**Fuera del censo, y por qué.** Las 14 etiquetas del censo de sitios (`batch`, `mapa-mcp`,
`sentido-sap`, `agent`, `voz-tiempo`, `voz-viva`, `recuerdo`, `recordatorio`, `piloto`, `piloto-err`,
`comprobar`, `envio`, `tramo`, `freno`) no pueden llevar un `Publico`, y la 394 lo juzga. Los
**cuatro embudos del player** siguen siendo `LogBus.Log` —su diario lleva la etiqueta de cada paso,
que en un select es el valor—, así que el paso a paso del player sale como cadencia y el workflow se
diagnostica por P24–P26 y por `workflow_step`/`workflow_end` (decisión 3). `align` (targets con
títulos de ventana: clase L), `clinica`, `consulta`, `dictado`, `espejo` de `EspejoDeConsulta`,
`instalador` y las otras 54 etiquetas de las 85 salen como cadencia hasta que alguien lea sus sitios
y los meta por la regla.

### Decisión 2: en origen, el valor se registra por su **forma**, en un solo sitio

`U.Graph.SinValor` (`windows-graph/src/SinValor.cs`), estática y pura. **En `windows-graph` y no en
`U.WindowsClient`**: `windows-graph` es otro ensamblado (`U.Graph`) al que `windows-client` referencia,
y E13–E15 viven en él; una clase del cliente no se podría llamar desde allí, y dos copias serían dos
criterios (aprendizaje nº16).

| Método | Devuelve, exactamente | Para |
|---|---|---|
| `Forma(string? v)` | `‹N car.›` con `N = v.Length`; `‹vacío›` si es nulo o vacío | todo valor que hoy se interpola |
| `Contraste(string? pedido, string? leido)` | `‹N car.›, igual a lo pedido` · `‹N car.›, igual salvo mayúsculas o espacios` · `‹N car.›, distinto de lo pedido (‹M car.›)`, con `N` la longitud de lo leído y `M` la de lo pedido | el rellenador y E13: lo que la línea de hoy servía para ver (¿SAP transformó el valor?) sin el valor |
| `Tapar(string texto, params string?[] valores)` | `texto` con cada `«valor»` —comillas incluidas— y cada aparición suelta de un valor de ≥ 2 caracteres cambiadas por su `Forma`, sin distinguir mayúsculas | una respuesta o una cuenta que **repite** un valor conocido (E5, E12, E18, O2, O3) |
| `Excepcion(Exception e)` | los tipos de la cadena (`A ← B`) y el primer marco de la pila, **sin** ningún `Message` | una excepción que sale del equipo (P1–P3) |

Sin hash ni prefijo del valor: un hash de «170» se invierte probando.

**Lo que se pierde en el log local, dicho:** «qué se escribió» exacto. El incidente del 2026-07-26
(`AgentLoop.cs:300`) necesitaba «qué y dónde»; queda «dónde, cuánto y si coincide», y lo escrito sigue
donde vive a propósito: en SAP, en la nota del portal y en `ConversacionPersonal`. El log no tiene que
ser una segunda copia de la historia clínica.

### Decisión 3: las otras salidas se **censan**, igual que sus canales

S2, S4 y S8 pasan a `SinValor.Forma(goal | summary | context)`; S3 sube `e.GetType().Name` y no el
`Message`; S9 sube `paso {StepOrder} · {ActionType}` y la fase `ok`/`error`, sin `Label`; S10 sube
`completado ({Tally})` o `se paró en el paso {StepOrder} ({ActionType})`, sin `Error`. S5 se queda
como está (el título de ventana es clase L, 052) pero **congelado**. Y como cada `TelemetryBus.Emit`
es una salida del equipo y son pocas (10), el contrato congela **cuáles son y todos sus argumentos**
—`phase`, `appId`, `surfaceUrl`, `workflowId`, `runId`, `label`, `detail`—, no solo `label` y
`detail`: un valor metido en `surfaceUrl` o en `workflowId` también sale. El censo que la fase 0
escribe en el contrato **es esta tabla**, y la comparación es sobre el texto de cada expresión con
los espacios quitados:

| S | Archivo | `kind` | Argumentos con nombre, expresión exacta |
|---|---|---|---|
| S1 | `Telemetry/EspejoDelLog.cs` | `"log"` | `phase: etiqueta` · `label: publica ? texto : Cadencia(etiqueta, texto)` · `detail: publica ? (object)new { tag = etiqueta, text = texto } : new { tag = etiqueta, largo = texto.Length }` |
| S2 | `Agent/AgentLoop.cs` | `"conscious_run_start"` | `runId: runId` · `label: SinValor.Forma(goal)` |
| S3 | `Agent/AgentLoop.cs` | `"conscious_run_end"` | `phase: "error"` · `runId: runId` · `label: e.GetType().Name` |
| S4 | `Agent/AgentLoop.cs` | `"conscious_run_end"` | `runId: runId` · `label: SinValor.Forma(summary)` |
| S5 | `Agent/AgentLoop.cs` | `"analyze"` | `runId: runId` · `appId: loc != null ? AppAligner.ProcessFromOrigin(loc.Origin) : ""` · `surfaceUrl: loc?.Id ?? ""` (sin cambio) |
| S6 | `Agent/AgentLoop.cs` | `"mcp"` | `runId: runId` · `label: a.Tool ?? ""` (sin cambio) |
| S7 | `Agent/AgentLoop.cs` | `"action"` | `runId: runId` · `label: a.Kind` · `detail: new { x = a.X, y = a.Y }` (sin cambio) |
| S8 | `Mcp/WorkflowMcpRunner.cs` | `"workflow_start"` | `workflowId: workflowId` · `runId: runId` · `label: SinValor.Forma(context)` |
| S9 | `Mcp/WorkflowMcpRunner.cs` | `"workflow_step"` | `workflowId: workflowId` · `runId: runId` · `phase: outcome.Ok ? "ok" : "error"` · `label: $"paso {outcome.StepOrder} · {outcome.ActionType}"` |
| S10 | `Mcp/WorkflowMcpRunner.cs` | `"workflow_end"` | `workflowId: workflowId` · `runId: runId` · `phase: result.Ok ? "ok" : "error"` · `label: result.Ok ? $"completado ({result.Tally})" : $"se paró en el paso {fallo?.StepOrder} ({fallo?.ActionType})"` · `detail: new { completed = result.Completed, omitted = result.Omitted, steps = result.Total, aligned = result.AlignedConsciously }` |

Congelar las sentencias no basta si se abre **otro canal**. La 395 congela también los canales:
los oyentes de `LogBus.Anotado`, `LogBus.AnotadoConMarca` y `LogBus.Logged`, los lectores de
`LogBus.TodayFile()` y `LogBus.Snapshot()`, y las llamadas a rutas `/agent/`. Tras la fase 2 son:
`AnotadoConMarca` → `EspejoDelLog`; `Anotado` → ninguno en el código de producción; `Logged` y
`Snapshot()` → `LogWindow` (pantalla); `TodayFile()` → solo `LogBus`; `/agent/` → `register` y
`events` (`Telemetry.cs`), `usage` (`FaceWindow.xaml.cs`), `turn` (`BackendClient.cs`) y `claves`
(`Credenciales/ClavesDelBackend.cs`, un `GET` que no manda nada del log). Un segundo
oyente que suba, un `PostAsync("/agent/events")` directo o un botón que adjunte `TodayFile()` a un
reporte ponen el contrato rojo nombrando archivo y línea.

Para poder mirar lo que sale sin red, `TelemetryBus` gana un evento `Emitido(string json)` que se
dispara en cada `Emit`, haya cliente o no, con el `TelemetryEvent` **entero** serializado con **las
mismas opciones que el POST** (las de `BackendClient`, expuestas en un solo sitio), y que solo
serializa si alguien escucha.

### Decisión 4: el objetivo se registra por su **forma**, siempre

La primera versión lo dejaba entero en el log local. Es falso que no sea lo dictado: con el dictado
de respaldo, `StartGoal(heard)` (`FaceWindow.xaml.cs:2607`) hace del objetivo **la frase dicha**, y el
objetivo de un tramo lo escribe el modelo de voz a partir de lo que dijo la persona
(`SurfaceMapTools.cs:1766`). Se decide: O1–O4 van por su forma **en todos los casos**, sin distinguir
el origen. Distinguirlo pediría un parámetro que alguien olvidará pasar —un guardia que se cree puesto
(aprendizaje nº18)—, y lo que se pierde es poco: el objetivo del puente lo construye el código con
piezas que ya están en el log (`FaceWindow.xaml.cs:4387` y la línea `resultado:` del workflow), y el de
la voz vive en la conversación. La línea queda `▶ objetivo: ‹N car.›` más la compuerta, y S2 sube lo
mismo.

## La especificación

El enunciado es el que va **literalmente** en `tests/ContratoDelGrafo/Contrato.cs`.

| # | Promesa | Fase |
|---|---|---|
| **394** | el espejo del log sube por línea marcada, no por etiqueta: de una línea anotada con LogBus.Log —sea cual sea su etiqueta, también exportar, workflow o dictado, y también la que llega por el diario del player— solo sale del equipo su etiqueta y su longitud, con label exactamente «‹etiqueta› · línea de N car.» y un detail con exactamente las claves tag y largo; entera sale solo la anotada con LogBus.Publico, y cada LogBus.Publico del código está en el censo de la 051 con su etiqueta, su ancla y sus huecos —uno nuevo, o uno que cambie un hueco, pone el contrato rojo nombrando archivo y línea—; ninguno lleva una de las catorce etiquetas de lo escrito, lo dicho, lo narrado o el objetivo; y una línea de la etiqueta telemetry no produce ningún evento | 2 |
| **395** | ninguna otra salida del equipo lleva lo escrito, lo dicho, lo narrado ni el objetivo: el objetivo y el resumen de una corrida consciente y el contexto de un workflow suben por su longitud, un error sube por su tipo y no por su mensaje, y un paso de workflow por su número y su tipo de acción, nunca por su etiqueta; cada TelemetryBus.Emit del código está en el censo de la 051 con todos sus argumentos; y los canales por los que algo puede salir —los oyentes de LogBus, los lectores del archivo y del anillo del log y las llamadas a rutas /agent/— son exactamente los censados: uno nuevo, o uno que cambie lo que manda, pone el contrato rojo nombrando archivo y línea | 3 |
| **396** | el log del rellenador dice etiqueta, longitud y si coincide, nunca el valor: la línea que el rellenador anota al escribir un campo es exactamente su etiqueta, la longitud de lo leído y si es igual a lo pedido, igual salvo mayúsculas o espacios, o distinto con la longitud de lo pedido; la de un campo que quedó vacío, exactamente su etiqueta y la longitud de lo pedido; y se juzga la línea que el rellenador de verdad anota, no una función aparte | 1 |
| **397** | lo que las manos escriben no queda en el log: la línea de cada llamada al mapa nombra sus argumentos de lugar (target, selector, surface, app, path, exit, workflow_id) con su valor y cualquier otro solo por su longitud; la línea de su respuesta y la del resultado de una acción mcp del agente consciente tapan esos valores aunque la respuesta los repita; y el recorrido por lotes, el «type» del agente consciente, el contexto con que se invoca un workflow y el dato que el núcleo no pudo escribir van por su longitud | 4 |
| **398** | lo que se dice y lo que se pide no queda en el log: lo que dijo la persona y lo que contestó Ü al cerrar un turno, el mensaje crudo del servidor que no se traduce, lo que narra el piloto, el objetivo de una corrida consciente y su resumen final se registran por su longitud —y su tipo—, nunca por su texto | 5 |
| **399** | el censo de la 051 queda cerrado en el código: cada uno de los 35 sitios contados el 2026-09-23 se encuentra por su ancla y su sentencia no tiene más huecos que SinValor, una longitud o los permitidos de su fila; ninguna línea que llega al log —por LogBus.Log o por cualquier embudo que acabe en él— ni ningún error que una superficie devuelve interpola un valor ni un texto sueltos en todo windows-client y windows-graph —su longitud sí—; y un embudo hacia el log que el juez no sabe seguir lo pone rojo | 5 |

**La que cierra el asunto es la 394**: mientras el espejo suba por defecto, cualquier arreglo en
origen es una lista de 35 que el sitio 36 deja vieja. Las 396–399 cierran el log local; la 394 y la
395 cierran la salida del equipo aunque el log local volviera a llenarse.

## Con qué se juzga cada una (sin pantalla, sin SAP, sin red)

Todo por reflexión: lo que aún no existe se pide por nombre y cuenta como `Pendiente` (regla del
flujo). Los valores de los fixtures son **inventados y obvios**, y ninguno es subcadena de una
etiqueta: `ZZ-INVENTADO-051` (16), `Paciente Inventado Cero` (23), `999000111`, `38,5 inventado`, y
uno **no ASCII**, `Ñandú Inventado 051` (19), para que un «no contiene» sobre JSON escapado no dé un
verde falso. Tres reglas de juez:

- **Igualdad exacta** donde esta spec fija el formato. Un «no contiene» deja pasar un prefijo del
  valor y choca con las cifras de la propia forma (`‹17 car.›` contiene «17»).
- **«No contiene» sobre los valores deserializados** de un evento —los siete campos y cada cadena
  de `detail`—, sin distinguir mayúsculas; nunca sobre el JSON crudo.
- **El oyente del juez no calla**: sus propias excepciones las guarda y las dice como «el juez
  reventó escuchando», distinto de «no llegó ningún evento». `LogBus.Log` se traga lo que lance un
  oyente (`LogBus.cs:57`), y sin esto un fallo del arnés se leería como fallo del núcleo
  (aprendizaje nº17).

Cada promesa lleva **dos sabotajes**: uno en la función y otro **en el cableado**, porque una función
pura puede estar intacta y nadie llamarla. Los dos se ven rojos, verificados por diff.

| # | Juez | Sabotajes (una línea cada uno; verificados por diff, ojo CRLF) |
|---|---|---|
| 394 | **(a)** Suscribir `TelemetryBus.Emitido`, `EspejoDelLog.Encender()`, y `LogBus.Log` de: una línea con valor inventado bajo **cada una de las 14** etiquetas de fuera; otra bajo `etiqueta-nueva-051`; tres bajo etiquetas que hoy diagnostican (`exportar`: «trabajo 7: escribí ZZ-INVENTADO-051»; `dictado`: ««Talla» = «Ñandú Inventado 051»»; `workflow`, con la forma del diario del player: «→ paso 3 «Paciente Inventado Cero» (select) · ubicación ANTES='uia://x'»); y una `telemetry` («flush: tiempo agotado»). Por cada línea salvo la de `telemetry`: **exactamente un** evento, `kind == "log"`, `phase ==` su etiqueta, `label ==` `‹{etiqueta}› · línea de {texto.Length} car.`, `detail` con exactamente las claves `{tag, largo}` y esos valores, y ningún valor deserializado del evento contiene el valor inventado. La de `telemetry`: ningún evento. **(b)** `LogBus.Publico("update", "versión 9.9.9-inventada descargada y lista para aplicar")`: exactamente un evento con `label ==` el texto y `detail.text ==` el texto, y `LogBus.Anotado` también la recibe (sigue en el log local). **(c)** Las fuentes (`U_REPO`): cada `LogBus.Publico(` del ámbito está en el censo P1–P26 (archivo, etiqueta, ancla, huecos exactos, normalizados sin espacios) y cada fila del censo existe; ninguno con una de las 14 etiquetas; el juez nombra archivo y línea de cada sobra o falta. Sin `U_REPO`: «NO PUDE JUZGARLA», que cuenta como incumplida. `Apagar()` en `finally` | **Función:** en el `Emit` del espejo, la condición `publica` pasa a `true` (toda línea sube entera) → (a) roja. **Cableado:** `EspejoDelLog.Encender` se engancha **también** a `LogBus.Anotado` con el texto entero → (a) roja (dos eventos, uno con el valor) |
| 395 | **(a)** `new WorkflowMcpRunner(new GraphConfig { ApiKey = "" }, vozFalsa).RunAsync("wf-051", "Paciente Inventado Cero · 999000111", ct)`: sin clave (`IsConfigured` falso, `GraphConfig.cs:104`; la clave embebida por CI se pisa a vacía) vuelve antes de tocar nada, **después** de emitir `workflow_start`; `Emitido` lo captura con `label == "‹35 car.›"` y ningún valor deserializado contiene el contexto. **(a′)** `AgentLoop.RunAsync("escribe ZZ-INVENTADO-051 en Talla", ctYaCancelado)` con dependencias falsas: el bucle no da ninguna vuelta, así que sin red emite `conscious_run_start` con `label == "‹33 car.›"` y `conscious_run_end` con `label == "‹70 car.›"` (el resumen fijo de `AgentLoop.cs:176`); si el contrato no puede construir `AgentLoop` sin pantalla, dice «NO PUDE JUZGARLA» **(D)**. **(b)** Las fuentes (`U_REPO`): cada `TelemetryBus.Emit(` del ámbito, extraído hasta su `;`, se compara con el censo congelado —10 entradas: archivo, `kind` y la expresión de **cada** argumento con nombre, normalizada sin espacios, tal como la fija la tabla de §*Decisión 3*—. **(c)** Las fuentes: los oyentes (`+=`) de `LogBus.Anotado`, `LogBus.AnotadoConMarca` y `LogBus.Logged`, las llamadas a `LogBus.TodayFile()` y `LogBus.Snapshot()` y los literales de ruta que contienen `/agent/` son exactamente los de §*Decisión 3*; uno de más o de menos, nombrado con archivo y línea. Sin `U_REPO`: «NO PUDE JUZGARLA», incumplida | **Función:** en `WorkflowMcpRunner`, `label: SinValor.Forma(context)` vuelve a `label: context` → (a) y (b) rojas. **Cableado:** en `EspejoDelLog.Encender`, `LogBus.Logged += (_, l) => TelemetryBus.Emit("log", label: l)` → (c) y (b) rojas |
| 396 | Conducir **el productor real**: `RellenadorSap` construido por su costura de prueba (fase 1: un constructor que recibe `ejecutar` y `leer` en vez de `SapGuiSurface`; hoy `_sap` es concreto, `RellenadorSap.cs:40`. La firma la fija la fase 0, porque el contrato la pide por reflexión: `RellenadorSap(GraphConfig, Func<PlanStep, (bool Ok, string Error)> ejecutar, Func<string, string?> leer)`, con `ejecutar` = `Execute(paso, out err)` y `leer(selector)` = `ValorActual(selector)`) con una lectura falsa, y su `Escribir` (privado, por reflexión) sobre un `DetectedField` de etiqueta «Talla» y tipo `input`, con `LogBus.Anotado` escuchando. Cuatro casos, cada línea `dictado` comparada por **igualdad exacta**: pedido «170», leído «170» → `«Talla» = ‹3 car.›, igual a lo pedido`; pedido «ZZ-INVENTADO-051», leído «zz-inventado-051 » → `«Talla» = ‹17 car.›, igual salvo mayúsculas o espacios`; pedido «170», leído «17» → `«Talla» = ‹2 car.›, distinto de lo pedido (‹3 car.›)`; pedido «ZZ-INVENTADO-051», leído vacío → `«Talla» aceptó ‹16 car.› pero quedó vacío: no se cuenta`. Y `SinValor.Forma(null) == SinValor.Forma("") == "‹vacío›"`. Sin la costura: `Pendiente` | **Función:** `LineaDeEscrito` devuelve `$"«{etiqueta}» = «{leido}»"` en vez de pasar por `SinValor.Contraste` → roja. **Cableado:** en `RellenadorSap.cs:405`, `var v = valor;` y `$"«{campo.Label}» aceptó «{v}» pero quedó vacío: no se cuenta"` en vez de `LineaDeVacio(…)` → roja (y la 399(a), que no encuentra el ancla) |
| 397 | **(a)** `new SurfaceMapTools(() => null).Call("herramienta_de_prueba_051", { text: "ZZ-INVENTADO-051", decir: "Paciente Inventado Cero", target: "Talla" })` con `LogBus.Anotado` escuchando (herramienta inexistente: cae en «herramienta de mapa no soportada», `SurfaceMapTools.cs:2130`, sin tocar nada; el contrato ya llama a `Call` en `:8513` y `:11000`): la línea «→» es **exactamente** `→ herramienta_de_prueba_051 text=‹16 car.› decir=‹23 car.› target=Talla`. **(b)** `SurfaceMapTools.LineaDeRespuesta(12, SurfaceMapTools.RelatoDeEscribir("ZZ-INVENTADO-051", "", ""), args)` es exactamente `← (12 ms) escribí ‹16 car.› y confirmé con Enter`. **(c)** `LineaDelRecorrido` con un paso de texto «ZZ-INVENTADO-051» y uno de salida «Guardar»: exactamente `recorrido de 2 paso(s): escribir ‹16 car.› → «Guardar»`. **(d)** `AgentLoop.Describe` (privada, por reflexión) de un `type` en (10,20) con `Text` «Ñandú Inventado 051»: exactamente `type (10,20) ‹19 car.›`. **(e)** La línea «MCP invoca» de la ejecución de 395(a): exactamente `MCP invoca workflow_id='wf-051' context=‹35 car.›`. **(f)** `SinValor.Tapar("escribí «ZZ-INVENTADO-051» y confirmé con Enter; zz-inventado-051 quedó", "ZZ-INVENTADO-051")` es exactamente `escribí ‹16 car.› y confirmé con Enter; ‹16 car.› quedó` (E18 usa esto sobre los argumentos de la acción; su cableado lo juzga la 399(a)) | **Función:** en `LineaDeLlamada`, la pertenencia a los argumentos de lugar pasa a `true` (todo argumento con su valor) → (a) roja. **Cableado:** en `Call`, la línea «→» vuelve a `string.Join(" ", args.Select(kv => $"{kv.Key}={kv.Value}"))` en vez de `LineaDeLlamada(…)` → (a) roja (y la 399(a)) |
| 398 | **(a)** `ConversacionEnVivo.Procesar` (reflexión, con los sobres de GPT-Live que ya usan 209–224): una transcripción de la persona «Paciente Inventado Cero tiene 38,5 inventado», un trozo de Ü «anoto ZZ-INVENTADO-051» y el cierre de turno → las líneas `voz-viva` son de la forma exacta `usuario dijo: ‹N car.›` y `Ü dijo: ‹M car.›` (`N`, `M`: la longitud de lo acumulado) y ninguna línea anotada contiene esas frases. **(b)** Un mensaje que no se traduce con la frase dentro (`session.instructions.appended` con un campo `nota`): la línea «←» es de la forma exacta `← session.instructions.appended · ‹N car.›`. **(c)** `ElPiloto.LineaDelPiloto(json)`, pura: `{"tipo":"texto","texto":"Paciente Inventado Cero"}` → exactamente `texto: ‹23 car.›`; «ZZ-INVENTADO-051 sin json» → exactamente `línea sin JSON: ‹25 car.›`. **(d)** La corrida de 395(a′) con `LogBus.Anotado` escuchando: la línea `agent` del objetivo es exactamente `▶ objetivo: ‹33 car.› · SIN compuerta de superficie`, la del fin es exactamente `■ fin · 0 acción(es) · ‹70 car.›`, y ninguna línea contiene «ZZ-INVENTADO-051» | **Función:** `ElPiloto.LineaDelPiloto` devuelve `$"{tipo}: {texto}"` → (c) roja. **Cableado:** «usuario dijo: {SinValor.Forma(…)}» vuelve a «usuario dijo: {_fraseUsuario}» → (a) roja; y, tercero y opcional, `▶ objetivo: «{Short(goal, 160)}»` restaurado → (d) roja |
| 399 | Las fuentes (`U_REPO`). **(a) El censo:** por cada uno de los 35 sitios, el ancla de su fila aparece en su archivo (las veces que dice la fila), y la sentencia que la contiene —desde el inicio de la sentencia hasta su `;`, o hasta su `,` en un brazo de `switch` (E10)— no tiene más huecos que los que empiezan por `SinValor.` o `Linea…(`, los que terminan en `.Length` y los permitidos de su fila, comparados como texto normalizado sin espacios. El juez dice cuántos de 35 y, de cada uno que falla, si falta el ancla o qué hueco sobra. **(b) La regla:** en todo el ámbito, en cada sentencia que llega al log y en cada `error = $"` / `err = $"`, ningún hueco nombra como identificador entero `valor` o `texto` fuera de una llamada a `SinValor.`/`Linea…(` —salvo seguido de `.Length`, y sin mirar dentro de los literales del hueco—. **Las formas de llegar al log no son una lista fija: se derivan de los embudos.** Cada lambda cuyo cuerpo llama a `LogBus.Log(` se resuelve al nombre que la recibe —el miembro asignado (`X = s => …`), el evento suscrito (`X += (_, m) => …`) o el parámetro al que se pasa, seguido hasta el campo que lo guarda— y ese nombre da las formas `X(`, `X?.Invoke(` y `X.Invoke(`; más `LogBus.Log(` y ` L($"`. Una lambda que no se deja resolver pone la 399 roja nombrando archivo y línea («embudo sin seguir»). Hoy, por esas formas y fuera de `LogBus.Log`, pasan **30** sentencias (`_log(`, `_log?.Invoke(`, `anotar?.Invoke(`, `Cuenta?.Invoke(`, `Diario?.Invoke(`, `LogGlobal?.Invoke(`…) **(M)**. **Solo esos dos nombres en (b), y medido por qué:** con `value`, `despues`, `leido` y `pedido` la misma búsqueda daba 15 aciertos de los que **5 eran falsos** (`IconosDelEscritorio.cs:278`, `Desplazamiento.cs:84`, `FaceWindow.Escritorio.cs:49`, `FaceWindow.xaml.cs:4326`, `UiaSurface.cs:403`); con `valor` y `texto` y la excepción de `.Length`, **0 falsos** sobre `043addc` **(M)**. (b) es la red para el caso obvio de mañana; el juez de verdad es (a), y lo que (b) no ve —`dato` en E17, `result` en E18, `despues`/`leido`/`value` en E2, E13, E15— lo lleva el censo. Sin `U_REPO`: «NO PUDE JUZGARLA», incumplida. **Medido con el juez de la fase 0 sobre `043addc`** (un lector de C# que distingue literal, comentario y hueco, no un `grep`): 36 lambdas-embudo resueltas (42 nombres por los que se les llama, cada uno con su ámbito), 1 externa (`PlanesALaMano.cs:62`, `ContinueWith`, que no se declara aquí) y 0 sin seguir; por las formas derivadas y `L(` pasan **147** sentencias fuera de `LogBus.Log` y de los errores (las 30 de arriba contaban seis formas con nombre; esta cuenta incluye las 80 de `L(` en `UiaSurface`/`WorkflowPlayer`). Un parámetro que solo llega al log dentro de `SinValor.`/`Linea…(` o por su `.Length` no hace embudo de su lambda (si no, E9 arreglado seguiría «embudando» `MundoQueToca.cs:102`). Un nombre de embudo que es un **parámetro** vale dentro de su método, y el **campo** que lo guarda dentro de su tipo, siguiéndolo a los métodos a los que se pasa tal cual: `MundoQueToca.cs` tiene dos `_sap` en dos clases, y `MiradaSubida.Real` no invoca `anotar`, se lo pasa a `SubirAOpenAI`. `(texto ?? "").Length` cuenta como longitud (`UiaSurface.cs:403, 1499`, precedentes buenos que si no darían falso). Hoy (b) da **12**: 9 del censo (E1, E2, E6–E9, E13, E14, N1), 2 que pasan por las lambdas de E9 y E17 y se van con ellas (`MundoQueToca.cs:102`, `_sap(campo, texto)`; `FaceWindow.xaml.cs:998`, `accionar("input", sel, texto)`) y **1 fuera del censo**: `Voice/MiradaSubida.cs:87`, el cuerpo de un error de OpenAI (`Corto(texto)`) por el embudo `anotar` (`ConversacionEnVivo.cs:1618` → `MiradaSubida.Real` → `SubirAOpenAI`). No es lo escrito, pero es texto ajeno entero en el log: la fase 5 lo registra por su código y su longitud | **Función:** en `FaceWindow.xaml.cs:855`, «no pude escribir «{SinValor.Forma(texto)}»» vuelve a ««{texto}»» → (a) y (b) rojas (ningún juez dinámico pasa por ahí). **Cableado:** en `FaceWindow.xaml.cs:735`, `var d = dato;` y «no pude {accion} «{d}»» → (a) roja (hueco `d` no permitido) mientras (b) sigue verde: es la prueba de que el juez es (a) |

**Dos sabotajes por promesa, y se comprueba el sabotaje** (CLAUDE.md, 2026-08-21): el diff contra la
copia de seguridad tiene que enseñar la línea cambiada antes de contar la roja, y el build no se
silencia. Los archivos de este repo son CRLF; un patrón con `\n` no aplica y deja un verde falso.

## Las fases

Una rama para la spec; cada fase un commit que pone verde su promesa sin romper las anteriores.

| Fase | Promesa que pone verde | Qué toca | Sitios | Terminado |
|---|---|---|---|---|
| **0** | las seis en **rojo**, por las razones escritas | `tests/ContratoDelGrafo/Contrato.cs` (394–399; `Pendiente` por reflexión para `U.Graph.SinValor`, `LogBus.Publico`, `LogBus.AnotadoConMarca`, `TelemetryBus.Emitido`, la costura de `RellenadorSap`, `RellenadorSap.LineaDeEscrito/LineaDeVacio`, `SurfaceMapTools.LineaDeLlamada/LineaDeRespuesta/LineaDelRecorrido`, `ElPiloto.LineaDelPiloto`; los censos —26 `Publico`, 10 `Emit`, los canales, los 35 sitios con ancla y huecos— escritos como datos en el contrato **desde esta spec**; la 399 roja por su propio escáner: 0 de 35) | — | `CONTRATO ROTO` con exactamente 394–399 rojas, cada una diciendo qué falta; las anteriores intactas |
| **1** | **396** | `windows-graph/src/SinValor.cs` (nuevo, puro) · `Clinical/RellenadorSap.cs` (costura de prueba, `LineaDeEscrito`, `LineaDeVacio`, y `:405`, `:409` las usan) | 2 (E1, E2) | 396 verde; los dos sabotajes vistos rojos |
| **2** | **394** | `Diagnostics/LogBus.cs` (`Publico`, `AnotadoConMarca`) · `Telemetry/EspejoDelLog.cs` (cadencia y un solo `Emit`) · `Telemetry/Telemetry.cs` (`Emitido`) · `Backend/BackendClient.cs` (sus opciones JSON, en un solo sitio) · los 26 `Publico`: `App.xaml.cs`, `Update/Updater.cs`, `Ui/GuardiaDeInstancia.cs`, `Clinical/ArranqueDeConsulta.cs`, `Ui/FaceWindow.xaml.cs:4452`, `Clinical/Transcripcion/PoliticaDeReintento.cs`, `Clinical/EjecutorDeExportaciones.cs`, `Mcp/WorkflowMcpRunner.cs:36, 68, 77` | 1 (E11) + S1 + 26 líneas públicas | 394 verde; los dos sabotajes vistos rojos |
| **3** | **395** | `Agent/AgentLoop.cs:96, 133, 181` · `Mcp/WorkflowMcpRunner.cs:39, 55, 69-72` | 6 de 10 salidas (S2, S3, S4, S8, S9, S10) | 395 verde; los dos sabotajes vistos rojos |
| **4** | **397** | `Mcp/SurfaceMapTools.cs` (E3–E8: `LineaDeLlamada`, `LineaDeRespuesta`, `LineaDelRecorrido`, y `:2700`, `:2792`, `:2819` con `Forma`) · `Ui/FaceWindow.xaml.cs:735, 855` (E17, E9) · `Agent/AgentLoop.cs:295, 307` (E18, E10) y **su comentario falso de `:300-303`** · `Voice/ConversacionEnVivo.cs:1327` (E12, con `Tapar`) · `windows-graph/src/Surfaces/SapGuiSurface.cs:535, 1035` y `UiaSurface.cs:1542` (E13–E15: el error dice qué regla falló y la forma, no el valor) · `Ui/WorkflowLibraryWindow.xaml.cs:174` (E16) | 15 (E3–E10, E12–E18) | 397 verde; los dos sabotajes vistos rojos |
| **5** | **398** y **399** | `Voice/ConversacionEnVivo.cs:1868, 1938, 1939, 2416` (D1–D4) · `Voice/RecordatoriosEnVivo.cs:29` (D5) · `Piloto/ElPiloto.cs:98, 101, 103` (N1–N3, con `LineaDelPiloto`) · `Ui/FaceWindow.xaml.cs:3186, 3325, 5599` (N4–N6) · `Ui/ConsultaWindow.cs:2090` (N7) · `Agent/AgentLoop.cs:92, 180` (O1, N8) · `Mcp/SurfaceMapTools.cs:1767, 1778` (O2, O3, con `Tapar`) · `Actions/Freno.cs:128` (O4) · y `Voice/MiradaSubida.cs:87`, que no es del censo pero la 399(b) lo encuentra por el embudo `anotar` (el cuerpo de un error de OpenAI: por su código y su longitud) | 17 (D1–D5, N1–N8, O1–O4) + 1 de la regla | 398 y 399 verdes (399: 35 de 35); sabotajes vistos rojos; `CONTRATO INTACTO` |

Total en origen: **35 sitios** (2 + 1 + 15 + 17), **7 salidas** (S1, S2, S3, S4, S8, S9, S10) y
**26 líneas públicas** (23 que pasan de `Log` a `Publico`, 3 nuevas). El número va en cada commit de
fase (patrón nº5).

**Zonas.** `windows-graph/` (`SinValor`, E13–E15): riesgo bajo, pero **el checkout principal tiene
`SapGuiSurface.cs` modificado sin commitear**: se avisa al dueño antes de la fase 4. UI de
`windows-client` (`FaceWindow`, `ConsultaWindow`, `WorkflowLibraryWindow`, `App.xaml.cs`): riesgo
**alto**; los cambios son de una línea cada uno y se dice en el aviso. El resto son servicios
(telemetría, voz, clínico, mapa, actualización).

## Lo que el panel ve, antes y después

| | Antes (hoy) | Después |
|---|---|---|
| Arranque, actualización, instancia | ✓ con texto | ✓ con texto (P4–P13); un fallo, con el tipo de la excepción —el mensaje, en el log local y como cadencia— |
| Errores fatales y tareas sin observar | ✓ con `ex.ToString()`, mensaje incluido | ✓ con los tipos de la cadena y el primer marco de la pila (P1–P3) |
| La exportación: trabajo reclamado, N escritos / sin llenar, reportado, rechazo HTTP | ✓ con texto | ✓ con texto (P17–P23) |
| El campo que no cuajó | ✓ con **el valor** («= «38,5» (pedido «38.5»)») | la cuenta «1 sin llenar» en la línea pública; la línea `dictado`, como cadencia; etiqueta, longitud y si coincide, en el log local |
| El workflow | ✓ invocado con su contexto, paso a paso con la etiqueta de cada paso (la opción de un select, la fila de un ALV), ✗ con el valor | ✓ invocado con el contexto por su longitud (P24), resultado con `Tally` y **el número y el tipo del paso en que se paró** (P25), alineación aprendida (P26); el paso a paso, como cadencia |
| La conversación, las manos del mapa, el tramo y Jev, el piloto, SAP e inspector, la enseñanza, la alineación | ✓ con texto, **valores incluidos** | cadencia: etiqueta, hora y longitud; el texto, en el log local (sin valores tras las fases 1–5) |
| Eventos directos (`conscious_run_*`, `workflow_*`, `action`, `mcp`, `analyze`) | ✓, con el objetivo, el resumen, el contexto, el mensaje del error y la etiqueta del paso enteros | ✓, con el objetivo, el resumen y el contexto por su longitud, el error por su tipo, el paso por su número y su tipo; `analyze` sigue con el título de la ventana (clase L, 052) |

Lo que se pierde en remoto es real: diagnosticar la voz, las manos, Jev o el paso a paso del player
de otra máquina vuelve a pedir el archivo de log, como antes del 2026-08-16. Es el precio de que la
historia clínica no salga; cada línea que se quiera recuperar entra al censo de `Publico` por la
regla de la decisión 1, con su lectura.

## Lo que queda fuera, y se dice

- **El backend** (otro repo). Lo que ya se subió **antes de hoy** no se puede recoger desde el
  cliente: purgar `agent/events` de tipo `log`, y un filtro en el servidor como segundo cinturón, son
  trabajo de Graph. Y que el panel pinte bien la cadencia (`‹etiqueta› · línea de N car.`), también.
- **Lo leído de la pantalla (clase L)**: filas de ALV, textos de nodo, títulos de ventana, etiquetas
  de paso grabadas. Puede llevar nombres y documentos (el fixture «GIRALDO HERNAN · 2394346» que cita
  la 046 de la rama A, `docs/specs/046-la-distribucion-se-valida-entera.md:60`). Tras esta spec **ya no
  sale** por el espejo —los embudos del player y `align` son cadencia y ningún `Publico` lleva un hueco
  de clase L—, pero sigue en el log local y sale por **S5** (`surfaceUrl` de `analyze`). Ejemplos
  contados, **no un censo**: `SapGuiSurface.cs:1883, 2022, 2037`, `WorkflowPlayer.cs:193, 248, 316,
  351, 499, 511`, `FaceWindow.xaml.cs:332, 345, 358`, `InspectorDiagnostics.cs:104`. Pide su propia
  spec (propuesta: 052, «lo leído tampoco sube»), que empieza por S5.
- **Lo que viaja a los modelos y a la enseñanza.** **Sin dueño y sin promesa** —la primera versión
  de esta spec lo atribuía a la 012 (168, 169, 171) y a la 046 (393), y es un guardia que se cree
  puesto: 168–171 de `Contrato.cs` son hoy la lección (`:470-473`), las 168 y 169 de la 012 no tratan
  de qué viaja, y la 393 de la rama A solo cubre al decisor Jev/TypeSafe. Cada salida, con su archivo:
  - `/agent/turn` (`AgentLoop.cs:96-126` → `BackendClient.cs:92`): el objetivo —que puede ser lo
    dictado, `FaceWindow.xaml.cs:2607`— y en cada turno los campos de la pantalla SAP y, si se pide,
    una captura (`AgentLoop.cs:230-240`). **Sin dueño, sin promesa.**
  - `Voice/MiradaSubida.cs`: capturas que se dejan en OpenAI. **Sin dueño, sin promesa.**
  - `Teach/TeachSession.cs:23-24`: el mp4 de la demostración va a Gemini «y también al archivo, para
    que lo veamos después». **Sin dueño, sin promesa** —es telemetría con otro nombre, y con humanos
    mirando, igual que el panel—.
  - `/learning/sessions/:id/steps` con `value` y `selectedValue` (`windows-graph/src/Contracts.cs:45-75`).
    **Sin dueño, sin promesa.**
  - `/api/v1/pipeline` con la nota (`RellenadorSap.cs:446`). **Sin dueño, sin promesa** (la 168 de la
    012 solo trata del `consultation_id`).
  - El audio del dictado a Deepgram (`Clinical/Transcripcion/DictadoEnVivo.cs`). **Sin dueño, sin
    promesa.**
  - El encargo del piloto, con la nota (`Piloto/ElPiloto.cs`, `--encargo=`). **Sin dueño, sin
    promesa.**

  Propuesta: una spec **053, «lo que viaja a los modelos y a la enseñanza»**, que empiece por el vídeo
  de enseñanza guardado «para que lo veamos».
- ~~**El `catch { }` mudo de `LogBus.Log`** alrededor de `Anotado` (`LogBus.cs:57`)~~ — **lo tomó la
  fase 2**, porque reescribía esa misma línea y añadía otra igual para `AnotadoConMarca`: el oyente
  que revienta deja una línea `logbus:` en el anillo y el archivo, sin repartirla (repartirla a quien
  acaba de reventar es un bucle). No cambia a ningún oyente; los jueces de esta spec siguen
  protegiéndose igual (§*Con qué se juzga*).
- **La 012**: su fase 3 (el log del rellenador) la absorbe esta spec como 396. Sus 168, 169 y 171
  siguen sin contrato y **sin número**: los cuatro se reciclaron el 2026-09-07. Se le dice al dueño
  en el PR; esta spec no la renumera.

## Nivel 4: la corrida a mano (la hace el dueño; esta rama no ejecuta `U.exe`)

Con la copia de la rama compilada en Release, el correo puesto (telemetría encendida) y SAP QAS:

1. Rellenar dos pantallas con **valores inventados** —triage (`SAPLY000`) y admisión (`NV2000`)—,
   una de ellas por la exportación desde el portal y otra por la voz («escribe … en …»).
2. **Log local** (`%LOCALAPPDATA%\U\logs\u-AAAAMMDD-*.log`): `grep -c` de cada valor inventado → 0;
   las líneas `dictado` llevan la etiqueta y «igual a lo pedido»; «usuario dijo: ‹N car.›»;
   «▶ objetivo: ‹N car.›».
3. **Panel del Provider Studio**: las líneas de `update`, `exportar` (P17–P23) y `workflow`
   (P24–P26) con su texto; las de `dictado`, `voz-viva`, `mapa-mcp` y el paso a paso del player como
   cadencia; buscar cada valor inventado → 0.
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
  `EspejoDeConsulta`). Con la línea marcada deja de importar para lo que sale (P13 es de
  `EspejoDelLog`; `EspejoDeConsulta` sale como cadencia); confunde al leer el log.
- **2026-09-23 · `SinValor` no podía vivir en el cliente.** La primera versión la ponía en
  `U.WindowsClient.Diagnostics`; `windows-graph` es otro ensamblado al que el cliente referencia, y
  E13–E15 están en él. Se encontró comprobando las refutaciones, no la señaló el crítico: pasa a
  `U.Graph.SinValor`.
- **2026-09-23 · Un juez que dice «no contiene» se contradice con su propia forma.** `‹17 car.›`
  contiene «17»: la primera versión de la 396 prohibía «17» y exigía una forma que lo escribe. La
  igualdad exacta lo evita y además atrapa las fugas por prefijo.

- **2026-09-23 · Fase 0: el juez de fuentes corrige tres medidas de esta spec, las tres con `grep`.**
  (1) El ancla de E17, `"nucleo-http", $"no pude`, está en **4** sentencias de `FaceWindow.xaml.cs`
  (`:596, 622, 735, 787`), no en una: pasa a `"nucleo-http", $"no pude {accion}`. (2) Las rutas
  `/agent/` son **5**: faltaba `Credenciales/ClavesDelBackend.cs:221` (`GET /agent/claves`), que no
  lleva nada del log pero es un canal y entra al censo de la 395(c). (3) La 399(b) con los embudos
  derivados encuentra un sitio **fuera del censo**: `Voice/MiradaSubida.cs:87` mete en el log el cuerpo
  de un error de OpenAI por el embudo `anotar`; no es lo escrito, pero es texto ajeno entero, y lo
  arregla la fase 5. Ninguna cambia un enunciado: cambian la tabla y el juez.
- **2026-09-23 · Dos `_sap` en un archivo.** `MundoQueToca.cs` tiene `ManoPorMundo` y `EscribirPorMundo`,
  cada una con su campo `_sap`. Un juez que resolviera los embudos por nombre suelto los confundiría
  (aprendizaje nº16) y dejaría la 399(b) roja para siempre por la mano que pulsa etiquetas: el nombre
  de un parámetro vale en su método y el de un campo en su tipo.
- **2026-09-23 · Fase 0: romper a propósito cuando todo ya está rojo.** Un rojo no se puede «poner
  rojo»; lo que se comprueba es que el juez **ve la línea rota** —que su rojo cambia justo ahí, con
  archivo y línea— y que el arreglo equivocado no le basta. Un sabotaje por promesa, los seis a la vez
  (archivos disjuntos), aplicados con edición que conserva la CRLF y verificados por `git diff` antes
  de juzgar; el veredicto, exigido; restaurados con `git checkout` y el contrato vuelto a correr
  **(M)**. Cada uno dio exactamente el cambio previsto y ninguno tocó las 298 anteriores:
  (394) un `LogBus.Publico` de mentira (`=> Log(…)`) y uno con `«sentido-sap»` en `RellenadorSap.cs:410`
  → el Pendiente de `Publico` desaparece y (c) nombra `:410` dos veces, por la etiqueta de fuera y por
  no estar en el censo (1 en el código); (395) `LogBus.Logged += (_, l) => TelemetryBus.Emit("log",
  label: l)` en `Encender` → (b) 11 en el código con `EspejoDelLog.cs:44` fuera del censo, y (c)
  `:44: Logged += no es un canal censado`; (396) `SinValor.Forma` que devuelve el valor → el Pendiente
  pasa a dos rojos por contenido (`Forma(null)` y `Forma("")` no dan `‹vacío›`), prueba de que el
  nombre pedido por reflexión es el que la fase 1 va a escribir; (397) callar la línea «→» de `Call`
  → (a) sigue roja con «anotó: » vacío: dejar de anotar no es anotar por la forma, y un «no contiene»
  habría salido verde; (398) `usuario dijo: {…Length} car.`, la forma casi buena → roja por igualdad
  exacta, y la 399(a) pasa a 1 de 35 (D2 solo pide que no haya hueco entero: es la 398 la que fija la
  forma); (399) quitar el `.Length` de `UiaSurface.cs:403` → la 399(b) nombra `:403 (L()` con el hueco
  `(texto??"")`, 13 en vez de 12.
- **2026-09-23 · El embudo `Log` no tiene ámbito, y casa cualquier `Log(`.** Visto en el sabotaje de
  la 394: el recuento de la 399(b) pasó de 147 a 148 sentencias sin ningún hueco nuevo. Una sonda de
  solo lectura sobre los dos árboles (el saboteado y `HEAD`) enseñó la de más: el `=> Log(tag,
  message)` del `Publico` de mentira, en `LogBus.cs` **(M)**. Siete embudos por inicializador
  (`new UiaSurface { Log = s => LogBus.Log(…) }`, `WorkflowPlayer`, …: `EjecutorDeExportaciones.cs:263`,
  `SurfaceMapTools.cs:31`, `WorkflowMcpRunner.cs:50`, `FaceWindow.xaml.cs:85, 4297, 4309`,
  `WorkflowLibraryWindow.xaml.cs:58`) se resuelven al miembro `Log` de **otro** tipo y salen **sin
  ámbito** —el octavo `Log`, `SurfaceMapTools.cs:1754`, sí está acotado— **(M, la misma sonda)**, así
  que su forma `Log(` vale en todo el ámbito. Hoy no cambia ningún
  veredicto y el error va hacia el rojo —un `Log($"… {texto}")` de una clase ajena saldría culpable,
  con archivo y línea—, nunca hacia el verde. Si la fase 2 escribe `Publico` con un `Log(` pelado, el
  148 no es una sentencia nueva. Se anota, no se arregla aquí: acotarlo al tipo del inicializador es
  cambiar el juez, y va con la fase que lo necesite.
- **2026-09-24 · Fase 1: la 396 en verde, y los dos sabotajes de la tabla de jueces dan exactamente
  lo previsto.** `SinValor` (`Forma`, `Contraste`) en `windows-graph/src/SinValor.cs`; en
  `RellenadorSap.cs`, la costura `(GraphConfig, ejecutar, leer)` —el constructor público pasa por
  ella con `Execute` y `ValorActual`, así que lo que se juzga es la escritura que corre con el médico
  delante— y E1/E2 anotan por `LineaDeVacio`/`LineaDeEscrito`. Contrato: 298 → **299** cumplidas;
  rojas solo 394, 395, 397, 398 y 399, las de las fases 2–5; la 399(a) pasa de 0 a **2 de 35** y la
  399(b) de 12 a **10** sitios con un hueco entero (se van `RellenadorSap.cs:405` y `:409`) **(M)**.
  Sabotajes, cada uno aplicado con edición que conserva la CRLF, visto por `diff` contra una copia
  (una sola línea cambiada), juzgado y restaurado por copia con `diff` vacío **(M)**: (función)
  `LineaDeEscrito` devuelve `«{etiqueta}» = «{leido}»` → la 396 roja en sus tres casos de escrito,
  con «anotó 1: ««Talla» = «170»»», y el de vacío verde porque no pasa por ella; (cableado)
  `var v = valor;` y la línea vieja en `:405` en vez de `LineaDeVacio` → la 396 roja solo en el
  caso de vacío, y la 399(a) de 2 a **1 de 35** nombrando E1. De paso, el `catch { }` de
  `Deshacer` —la sentencia que la costura tocó— deja de ser mudo: anota la cadena de tipos y
  mensajes con la etiqueta del campo, nunca el valor que se restauraba; la 399(b) no cambia (10).
- **2026-09-24 · Fase 2: la 394 en verde a la primera, y lo único que cambia de veredicto es ella.**
  `LogBus.Log` y `LogBus.Publico` pasan por un solo `Anotar(tag, mensaje, publica)` —así el `Publico`
  no es un `Log(` pelado, y el recuento de la 399(b) sigue en **147** (el hallazgo del embudo `Log`
  sin ámbito no se dispara)—; `AnotadoConMarca` lo oye el espejo, que emite **un** `Emit` (el S1 del
  censo, que la 395(b) ya encuentra); `TelemetryBus.Emitido` serializa con `BackendClient.Json`, que
  pasa de privado a interno para ser una sola fuente. Los 26 `Publico` del censo: **23** que eran
  `Log` y **3** nuevos (P1–P3, con `SinValor.Excepcion`: tipos de la cadena y método del primer
  marco de la más honda —el método y no la línea del archivo: la ruta es la de quien compiló—). En
  **6** de ellos el `Message` salía entero y ahora se queda en una línea `Log` aparte (P5, P9, P14,
  P15, P23 y el `result.Error` de P25); en P1–P3 el `ex.ToString()` sigue en local. P25 escribe «sin
  paso fallido: se paró antes o fuera de los pasos» cuando `fallo` es nulo, para no imprimir «se paró
  en el paso ()» (una rama literal: no añade hueco). Contrato: 299 → **300** cumplidas; rojas solo
  395, 397, 398 y 399; la 399(a) pasa de 2 a **3 de 35** (E11) y la 399(b) sigue en 10; la 395(c)
  deja de nombrar canales y la 397(e) deja de fallar **(M)**. Sabotajes, cada uno visto por `diff`
  contra una copia (una sola línea), juzgado y restaurado con `diff` vacío **(M)**: (función)
  `publica = true;` antes del `Emit` del espejo → la 394 roja con **70** líneas: cada línea de (a)
  sube entera, con los valores inventados en `label` y en `detail.text` (escapados como
  `«` en el JSON: el juez los ve porque deserializa); (cableado) `LogBus.Anotado += (e, t) =>
  TelemetryBus.Emit("log", phase: e, label: t);` en `Encender` → la 394 roja con **21**: «produjo 2»
  en cada línea, una con el valor; y la 395 nombra `EspejoDelLog.cs:56` dos veces (un `Emit` fuera
  del censo y un `Anotado +=` que no es canal). En los dos, ningún otro veredicto cambió. Y el
  `catch { }` de `LogBus.cs:57`, que la spec dejaba fuera, lo tomó esta fase (§*Lo que queda fuera*).
- **2026-09-24 · Fase 3: la 395 en verde a la primera, con la tabla de §*Decisión 3* tal cual.**
  De los **10** `TelemetryBus.Emit` del código (`grep`, igual que el censo de la 395(b)), **7**
  llevaban texto libre (S1, S2, S3, S4, S8, S9, S10): S1 lo cerró la fase 2 y los **6** restantes
  esta —`AgentLoop.cs:99, 139, 188` y `WorkflowMcpRunner.cs:41, 59, 84`—; S5, S6 y S7 quedan como
  estaban, congelados. `AgentLoop.cs` gana `using U.Graph;` para `SinValor`. Contrato: 300 →
  **301** cumplidas; rojas solo 397, 398 y 399, las de las fases 4–5; la 399(a) sigue en **3 de 35**
  y la 399(b) en **10** (esta fase no toca ninguna línea de log) **(M)**. Sabotajes, cada uno visto
  por `diff` contra una copia (una sola línea, CRLF conservada), juzgado y restaurado con `diff` vacío
  **(M)**: (función) `label: SinValor.Forma(context)` → `label: context` en `WorkflowMcpRunner.cs:41`
  → la 395 roja por (b), `:41` fuera del censo y «falta S8», y por (a), `label` con el contexto entero
  y los tres valores inventados dentro; ningún otro veredicto cambió (la 397(e) sigue verde: la línea
  «MCP invoca» es P24, no S8); (cableado) `LogBus.Logged += (_, l) => TelemetryBus.Emit("log", label:
  l);` en `Encender` → la 395 roja por (b), `EspejoDelLog.cs:56` fuera del censo (11 en el código),
  por (c), `:56: Logged += no es un canal censado`, y además por (a′): la línea `▶ objetivo:` de O1
  —que hasta la fase 5 lleva el objetivo entero en el log local— salió por ese oyente con
  «ZZ-INVENTADO-051» dentro.
- **2026-09-24 · El juez dinámico de la 394 no ve un evento con otra `phase`.** Visto en el sabotaje
  de cableado de la fase 3: con `LogBus.Logged += (_, l) => TelemetryBus.Emit("log", label: l)`
  activo —`Encender` lo suscribe dentro del propio juez de la 394— cada línea de la 394(a) salió
  **dos** veces, una con el texto entero y `phase` vacía, y la 394 siguió **verde**: filtra los
  eventos por `x.Phase == etiqueta` antes de contarlos y de buscar el valor **(M)**. Lo atrapan la
  395(b) y (c) por las fuentes, así que hoy no hay agujero; pero el enunciado de la 394 dice «solo
  sale del equipo su etiqueta y su longitud», y su juez dinámico solo lo comprueba para los eventos
  que ya llevan la etiqueta. El arreglo es de juez, no de enunciado —contar y buscar el valor en
  **todos** los eventos emitidos durante la línea, sea cual sea su `phase`—; se anota y no se hace
  aquí: cambiar el juez de otra promesa no es de esta fase. Tras la fase 5 la 395(a′) deja de verlo
  también (O1 ya no lleva el objetivo), y la red queda solo en las fuentes.
- **2026-09-24 · S10 con el paso vacío.** El censo congela el `label` de `workflow_end` como
  `result.Ok ? $"completado ({result.Tally})" : $"se paró en el paso {fallo?.StepOrder}
  ({fallo?.ActionType})"`. Cuando falla **sin** paso fallido (`fallo` nulo: se paró antes de los pasos
  o fuera de ellos) sale «se paró en el paso  ()» —no miente, pero no dice dónde—. P25 ya lo distingue
  («sin paso fallido: se paró antes o fuera de los pasos») y sube entera por el espejo, pero como
  evento `log` **sin** `runId` (S1 no lo lleva): en el panel se casa con el `workflow_end` por la
  hora, no por la corrida. El diagnóstico no se pierde; se deja escrito en el código y aquí.
  Distinguirlo también en S10 es cambiar una fila del censo de la 395: decisión del dueño.

## Revisiones

**2026-09-23 · El crítico, nueve refutaciones.** Las nueve se comprobaron contra `043addc` antes de
aplicarlas; ninguna resultó falsa. Donde la medida propia difiere de la del crítico, se dice.

| # | Refutación | Veredicto | Qué se midió | Qué cambió |
|---|---|---|---|---|
| 1 | La 396 se contradice: «zz-inventado-051 » mide 17 y la forma correcta escribe «‹17 car.›», que contiene «17» (bloquea) | **cierta** | `printf 'zz-inventado-051 ' \| wc -c` → 17 **(M)** | la 396 compara cada línea por **igualdad exacta** con su línea esperada, escrita en la tabla de jueces |
| 2 | La lista blanca decide por etiqueta, no por línea; y el criterio (a) no es medible en líneas con texto ajeno (bloquea) | **cierta** | 111 sentencias bajo las 16 etiquetas, 37 con `.Message`, `{ex}`, `ToString()`, `{err}`, `{porque}`, `Recortar(cuerpo)`, `hecho.Porque` o `result.Error`; `EspejoDeConsulta.cs:141-142`, `RellenadorSap.cs:129`+`:454`, `App.xaml.cs:61, 80, 90`, `BackendClient.cs:117, 128` leídos **(M)** | decisión 1 reescrita: **línea marcada** (`LogBus.Publico`) con su censo congelado P1–P26; en lo público una excepción sube por su tipo, nunca por `Message` ni cuerpo; el criterio de entrada mira el hueco, no el contenido |
| 3 | El diario del player no es solo clase L: la etiqueta de un select **es** el valor; `workflow`/`exportar` hasta la 052 no es aceptable | **cierta** | `UiaSurface.cs:2359, 2385, 828`; `WorkflowPlayer.cs:193, 316, 351, 511`; `StepPause.cs:55`; los cuatro embudos; `WorkflowMcpRunner.cs:55`; `SapGuiSurface.cs:2022`; el fixture de la 046 de la rama A, línea 60 **(M)** | los embudos del player siguen en `LogBus.Log` → cadencia; `workflow` y `exportar` solo suben sus líneas propias (P17–P26); S9 sube número y tipo, sin `Label`; S10, sin `Error`; la 394(a) juzga una línea con la forma del diario del player |
| 4 | Faltan sitios (`FaceWindow.xaml.cs:735`, el `→ {result}` de `AgentLoop.cs:295`) y el ámbito deja fuera `App.xaml.cs` | **cierta** | `:735` y `:997` leídos; `AgentLoop.cs:291` → `LocalMcp.cs:38` → `SurfaceMapTools.Call` → `RelatoDeEscribir` (`:2847`) **(M)**; 676 en `src`, **683** con `App.xaml.cs`, 82 → **85** etiquetas, `fatal`/`unobserved-task`/`instalador` 0 veces en `src`, otras **54** (85 − 16 − 14 − `telemetry`) **(M)** | E17 y E18 al censo (35 sitios con las O); ámbito `windows-client/**/*.cs` sin `bin`/`obj` + `windows-graph/src`; recuentos corregidos. Respuesta a la pregunta 4 del crítico: `dato` (E17) |
| 5 | Un sabotaje de una línea en el cableado no pone roja ninguna promesa; la 399(a) no ancla; la 399(b) mira cuatro formas fijas | **cierta** | el sabotaje `var v = valor;` en `:405` deja verdes la 396 (funciones puras), la 399(a) (el archivo contiene `SinValor.` en otra línea) y la 399(b) (`v` no es `valor`) **(D, leyendo los jueces)**; por `_log(`, `_log?.Invoke(`, `anotar?.Invoke(`, `Cuenta?.Invoke(`, `Diario?.Invoke(`, `LogGlobal?.Invoke(` pasan **30** sentencias con este patrón (el crítico contó 27 con otro; la diferencia es de patrón, no de fondo) **(M)** | la 396 conduce el productor real por una costura; la 399(a) ancla cada sitio y limita sus huecos; la 399(b) deriva las formas de los embudos y pone rojo el que no sabe seguir; **dos sabotajes por promesa**, uno en el cableado |
| 6 | El juez de la 394 es más débil que su enunciado; `Emitido` no ve cuatro de los siete campos; JSON escapado; el `catch { }` de `LogBus` | **cierta** | `Telemetry.cs:48-62` (siete campos), `AgentLoop.cs:225-227` (S5), `BackendClient.cs:36-39` (sin `Encoder`: el de defecto escapa lo no ASCII **(D, documentado)**), `LogBus.cs:57` **(M)** | igualdad exacta de `label` y claves exactas de `detail`; `Emitido(json)` con el evento entero y las opciones del POST; la 395(b) compara todos los argumentos; «no contiene» sobre lo deserializado; fixture no ASCII; el oyente del juez no calla |
| 7 | Ninguna promesa congela los canales | **cierta** | oyentes `EspejoDelLog.cs:43` y `LogWindow.xaml.cs:16`; `Snapshot()` en `LogWindow.xaml.cs:13`; `TodayFile()` solo en `LogBus`; rutas `/agent/` en `Telemetry.cs:111, 135` y `FaceWindow.xaml.cs:1014` **(M)**; y una cuarta que el crítico no listó: `BackendClient.cs:92` (`/agent/turn`) **(M)** | la 395 gana la cláusula (c): oyentes, lectores y rutas `/agent/` exactamente los de hoy (con `AnotadoConMarca` en lugar de `Anotado` para el espejo) |
| 8 | «Lo que viaja a los modelos es la 012 y la 046» es un guardia que se cree puesto | **cierta** | `Contrato.cs:470-473` (168–171 son la lección); filas 168–171 de la 012; `TeachSession.cs:23-24`; `MiradaSubida.cs:12, 38`; `DictadoEnVivo.cs:17`; `RellenadorSap.cs:446`; `Contracts.cs:45-75`; la 393 de la rama A trata de la política de Jev **(M)** | «lo que queda fuera» lista cada salida con «sin dueño, sin promesa»; se quita la atribución; se propone la 053 |
| 9 | Con el dictado de respaldo el objetivo **es** lo dictado; O1 no puede quedarse entero | **cierta** | `FaceWindow.xaml.cs:2603-2607` (`StartGoal(heard)`), `AgentLoop.cs:92`, `FaceWindow.xaml.cs:4351-4356` **(M)**; y el objetivo del tramo lo escribe el modelo de voz (`SurfaceMapTools.cs:1766`) **(M)** | decisión 4 invertida: O1–O4 por su forma, siempre; la 398(d) conduce `AgentLoop.RunAsync` con un objetivo inventado. De las dos correcciones que el crítico ofrecía (forma con origen, o solo cuando no viene de la voz) se toma una tercera, más estricta: forma siempre, sin parámetro de origen que olvidar |

**Las cuatro preguntas que la primera versión dejó al crítico, contestadas:** (1) sí, el objetivo
sale también del log local (decisión 4); (2) no es aceptable: el diario del player sale como
cadencia desde la fase 2; (3) congelar es la fricción correcta, pero **por línea** y con **todos**
los argumentos y **los canales**: congelar etiquetas congelaba lo que no importa; (4) `dato` en
`FaceWindow.xaml.cs:735` (E17), y `result` en `AgentLoop.cs:295` (E18).

## Cierre

- [ ] Todas las promesas verdes (`.\scripts\contrato-del-grafo.ps1` → CONTRATO INTACTO), y el de la voz intacto
- [ ] Dos sabotajes por promesa —función y cableado—, vistos rojos y verificados por diff
- [ ] `.\scripts\verificar.ps1` pasa, con evidencia en `out\evidencia.md`
- [ ] Nivel 4 en ≥2 pantallas, con nombre: triage (`SAPLY000`) y admisión (`NV2000`)
- [ ] Estado de este documento: **implementado** (AAAA-MM-DD)
