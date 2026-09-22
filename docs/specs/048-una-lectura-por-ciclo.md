# Una lectura por ciclo

> Spec 048 · 2026-09-22 · rama `jero/jev-una-lectura-por-ciclo` · promesas **361–370** · Estado: **en construcción**
> (ninguna línea de producción escrita; el contrato todavía no las conoce).
>
> Es la rama **C** de la nueva arquitectura de Jev (`scratchpad/jev/arquitectura-jev-en-u.md`, §4 y §7).
> Nace desde `main` `dde8c40` y trae `f811796` (voz: un `SemaphoreSlim` en `ConversacionEnVivo`, y la
> promesa **341** en el registro del contrato). Va **después de A** (spec 046) **y de B** (spec 047) a `main`.
>
> **Marcas.** **(M)** medido: log, `grep`, sonda o spec anterior con fecha. **(D)** deducido del código o de
> sumar medidas. Un número sin marca es una meta.

## De dónde sale

El dueño: «trae main, revisa y cambia la arquitectura al nuevo Jev». La arquitectura común reparte el trabajo
en cuatro ramas; a esta le toca **la lectura**: hoy un paso del tramo puede pagar hasta cinco lecturas UIA de
la misma ventana, el reproductor de workflows recorre el árbol tres veces con tres funciones que hacen el
mismo recorrido, el mapa vivo está saturado casi todo el tiempo que la app corre con Neo4j caído, y lo que
va al decisor no lleva caja —así que la rama D no tiene qué pintar— ni se funde por identidad.

El encargo, en cuatro frases, y lo que cada una promete abajo:

1. **Una lectura de pantalla por ciclo, compartida.** `ReadFields`, `ReadinessCount` y `StructureFingerprint`
   salen del mismo barrido (361); la lectura del tramo se comparte con versión y caduca a 1 s (362); lo que el
   paso acaba de leer, la compuerta no lo vuelve a mirar (363). El número de lecturas por ciclo queda en el log.
2. **Cuando el paso ya trae selector, se pregunta por ese elemento y no se inventaría la ventana** (364), y la
   cuenta dice cuál camino se usó y cuánto tardó.
3. **Las candidatas que van al decisor salen de esa misma lectura**, con selector, etiqueta, tipo y caja, ya
   filtradas —vivas, con nombre, sin duplicar por identidad y nunca por texto— y con su número y coste en el
   log (365); y se publican para quien pinte (368).
4. **Neo4j no bloquea el hilo de la ubicación: primero se MIDE** (366) y solo si bloquea se saca del hilo con
   su promesa (367, reservada). De paso, el «dónde» se pregunta una vez por instante (369).

## Lo que se midió antes de escribir código

| Qué | Medida | Fuente | |
|---|---|---|---|
| Lecturas UIA completas que un paso del tramo PUEDE pagar sobre la misma ventana | **hasta 5**: `PuertasDeAhora` (`SurfaceMapTools.cs:172`), `ObservarLaVentanaDeTrabajo` (`FaceWindow.xaml.cs:5662`, freno 800 ms, solo con ventana fijada ≠ foco), `MirarOtraVezLaVentana` ×1–3 (`:5638`, `new UiaReader()` cada vez), señalar (`SurfaceMapTools.cs:1179`) y el inventario pegado (`:2150` → `LoQueVeo` → `PuertasDeAhora`) | `grep` de `_lector.Read()` y `lector.Read(`: 8 sitios en `SurfaceMapTools`, 2 en `FaceWindow`, 1 en `AgentLoop`, 3 en `UiInspector` | M |
| Lo que cuesta cada una | 34–89 ms (Instagram, 45 elementos, spec 040); **77–113 ms** en las 8 líneas «señalar … leí la ventana» del 21-09 (48–99 elementos); máximo 243 ms en las 17 del informe | `u-20260921.log`; `040…md:33` | M |
| El Explorador con el lector actual (#90–#91) | **sin medir**: su 959–1.343 ms es de antes de #90 | `038…md`, informe §2.B | — |
| `Walk` en `UiaSurface` | **3 sitios** hacen el mismo recorrido nodo a nodo con `TreeWalker` + `.Current`: `ReadFields` (`:470`), `ReadinessCount` (`:493`), `StructureFingerprint` (`:510`). Ninguno está en el camino de `map_decidir` ni del tramo: los llaman `SurfaceReadiness` (`:139, :191`), `WorkflowPlayer` (`:330, :340`), `WorkflowDryRun` (`:130`), `WorkflowLibraryWindow` (`:245`) y los lectores de SAP (`RellenadorSap`, `SapContextReader`, `FaceWindow:953, :5403` → `SapGuiSurface.ReadFields`, otra clase) | `grep` | M |
| Barridos por paso del reproductor | huella (1) + por sondeo de 120 ms mientras el elemento no resuelve, un `ReadinessCount` (1 cada uno) + `IsStepReady` (un `Resolve`, sin barrido) | `WorkflowPlayer.cs:330`, `SurfaceReadiness.cs:139-160`, `PollMs = 120` | D |
| Mapa vivo saturado | **183** avisos «SATURADO» el 21-09 y **197** el 22-09, **todos** «lectura de pantalla», de **45–57 vueltas descartadas** cada uno; con el latido a 900 ms son ~67 vueltas por minuto: se pierden entre el 67 y el 85 % | `grep -c SATURADO` sobre `u-20260921.log`, `u-20260922.log`; `MapaVivo.cs:69` | M |
| Por qué: `Proyectar` es un POST síncrono en el hilo del latido y en el de la ubicación | 2.023–2.121 ms por fallo con Neo4j rechazando en `127.0.0.1:7474`; en esta máquina Neo4j está caído («no pude leer de Neo4j: … denegó expresamente dicha conexión») | informe §2.C (sonda `percibir\neo`); `u-20260921.log`; `MapaVivo.cs:296, :439, :569, :586` | M el POST; D que sea la causa de todos los SATURADO (hay días con SATURADO y Neo4j arriba: 26-08, 03-09) |
| «Neo4j no responde» en el log | **0 veces en 23 logs**, y el porqué está en el código: `AsegurarIndices()` corre en el constructor, `Cuenta` es null en ese momento, y `Mandar` gasta ahí el único aviso (`_yaAvise = true`) sin que nadie lo lea | `ProyectorNeo4j.cs:79, :105, :645-649` | M |
| Ubicación ciega tras cada salto | ráfagas de «descarté una vuelta» (375 → 562 → 843 → 1264 ms) mientras «localizar cuesta ~12–194 ms» | `u-20260922.log` 03:44, 09:13, 09:42 | M |
| Cuántos sitios preguntan «dónde estoy» sin memoria | **25** llamadas a `DondeEstoy()` (24 en `FaceWindow`, 1 en `SurfaceLocator`) y `_where()` en `SurfaceMapTools` es ese mismo crudo (`FaceWindow.xaml.cs:434`); `DondeTrabajo` sí recuerda 400 ms (`:5577`) | `grep -c` | M |
| Preguntar por UN selector frente a inventariar | 7–126 ms según la posición (`bench-uia-3.txt`); en Chrome, preguntar y fallar 60–107 ms frente a 232–272 ms de inventariar | arquitectura §4.2, informe mejora 5 (`Resolve` 23–106 ms) | M |
| La segunda mejor se dispara por una subcadena | `!cuenta.Contains("puertas vivas para")` (`SurfaceMapTools.cs:329`), redundante con `Mano.Intento = !r.Ambiguo` | código | M |
| Dónde se funde por TEXTO | **3 sitios**: `FundirPuertas` descarta una puerta del terreno si UIA ya nombró esa etiqueta, aunque sea otro elemento (`SurfaceMapTools.cs:146-156`); los menús se deduplican por etiqueta + `MenuItem` (`UiaReader.cs:215`, `:461`) | `grep` | M |
| Dos definiciones de «accionable» | `UiaSurface.Interactive` (Edit, ComboBox, CheckBox, RadioButton, Button, List, ListItem, MenuItem, Hyperlink, TabItem, SplitButton, Document) y `UiaReader.Actionable` (las mismas menos List y Document, más Text) | `UiaSurface.cs:220`, `UiaReader.cs:45` | M |
| El lector cae a «nodo a nodo» | 41 veces sobre `LockApp` (la pantalla de bloqueo) por `Operation timed out`, 2 sobre `claude` por `E_UNEXPECTED` | `u-20260921/22.log`, línea `lector:` | M |
| Jev en un paso | 322–350 ms en caliente; 1.066 en frío. El 21-09 no hubo ninguna decisión de Jev («sin U_DECISOR: decide Luna») | anatomía §2-3; informe §2.B | M |

**Lo que NO se pudo medir, y se dice:**

- **SAP.** 0 líneas `sapgui://` en los 23 logs de esta máquina. `ReadFields` de SAP cuesta ~5 s en el triage
  (comentario del 2026-08-14 en `SapGuiSurface.cs:462-470`, D). Nada de esta spec toca SAP: el barrido de
  la 361 es de `UiaSurface`; `SapGuiSurface` tiene sus propios `Walk` (5 sitios) y se quedan.
- **El efecto de Neo4j caído en un clic.** El presupuesto medido del clic (13 `map_take` del 21-09) no le
  atribuye ni un milisegundo, porque la espera de pulsar sondea `DondeTrabajo`, no el mapa vivo. Lo que sí
  es D: un latido saturado deja el grafo con puertas vivas viejas, y eso dispara `MirarOtraVez` en la
  compuerta —una lectura completa— más veces de las que haría falta. La 366 existe para convertir esa D
  en M antes de escribir la 367.
- **U.exe no se ejecutó en esta sesión** (regla del encargo). Todo lo medido es de logs anteriores; el
  «antes/después» por paso es el nivel 4.

## Por qué esto va dirigido por especificación

Porque cada pieza que se toca hoy **se da por buena a sí misma**: una lectura reutilizada que caduca tarde
no falla, devuelve una pantalla vieja con cara de nueva (patrón nº8); una compuerta que «ya no mira» puede
pasar a no mirar nunca sin que ningún test lo note (la 264 cuenta miradas: hay que seguir contándolas); un
barrido compartido que devuelva otra huella que la de antes rompería en silencio los workflows grabados; y
un proyector «fuera del hilo» sin medida sería la mejora 1 del informe hecha por fe.

La frase falsa hoy y verdadera después, para cada una, está en la tabla de promesas. **Ninguna línea de
producción entra antes que la promesa que la juzga.** Cada fase empieza con el contrato ROTO y termina INTACTO.

## La regla

**Leer es publicar; reutilizar es pedir por ventana con edad; accionar invalida.** En concreto:

1. **Una observación con versión** (`windows-client/src/Uia/Observacion.cs`, nuevo; vive en `Uia/` porque
   quien la produce es el lector, y `Navigation/` ya depende de `Uia/` en 3 archivos —`ClickWatcher`,
   `Interrupcion`, `SurfaceNavigator`—, así que no se abre ninguna dependencia nueva):
   `Observacion(Version, Hwnd, Donde, Pantalla, LeidaEn, MsDeLectura, ComoSeLeyo, Candidatas, Completa)` y
   `Candidata(Selector, Etiqueta, Tipo, Caja, Identidad)`. `Identidad` es el `RuntimeId` que la petición ya
   trae (298); `Caja` es la geometría **leída** (`Bounds`), nunca estimada.
2. **Quien lee, publica.** `UiaReader.Read(hwnd)` termina publicando su resultado en `Observatorio`
   (estático, con candado y versión monótona). No cambia lo que `Read` devuelve ni cuándo lee: **`Read` es
   siempre una lectura fresca** —eso es lo que mantiene la 299 (dos miradas que ven lo mismo = asentada)
   sin trampa— y la 297/298 se quedan como están.
3. **Quien puede reutilizar, pide:** `Observatorio.Reciente(hwnd, edadMaxMs: 1000)`. La piden
   `PuertasDeAhora`, señalar y el inventario pegado (los tres en `SurfaceMapTools`, que es quien conoce
   `aqui`). Más de 1 s, otra ventana, o invalidada → se lee y la nueva reemplaza a la vieja. **1 s** es la
   cifra del patrón nº8 («una lectura reutilizada caduca en menos de un segundo»); se fija como constante
   con nombre (`Observacion.VigenciaMs`) para que el nivel 4 la pueda discutir con números.
4. **Accionar invalida.** `Take` y `Type` llaman a `Observatorio.Invalida(porque)` en cuanto la mano vuelve
   (la misma regla que `_dondeTrabajo.Olvida()`, promesa 246). `MapaVivo.MirarDonde` invalida al cambiar de
   sitio. El siguiente que pida, lee.
5. **El paso lleva su vista.** `RecorrerSegunElNucleo.Paso` gana `Vista` (una `Observacion` opcional). En
   `EsperarloVivo`, si la vista es fresca y de esta misma pantalla: si es **completa**, la compuerta le
   cuenta al núcleo lo que vio (`Grafo.Observar` con la misma criba que el latido, que pasa a ser
   `MapaVivo.LoQueEsPuerta`, pública: **una sola regla de qué es una puerta**) y sigue por el camino de
   siempre, que ahora la encuentra viva sin mirar; si es **de uno** (la respuesta de preguntar por selector),
   la puerta se da por viva con `Grafo.Recordar` y **sin tocar la lista de vivos** de esa ubicación —observar
   con un solo elemento borraría las demás—. En los dos casos el diario dice que se usó lo que el paso
   acababa de leer y cuántas miradas se pagaron: **0**. Sin vista, o vieja, o de otra pantalla: la 264 y la
   299 tal cual.
6. **Selector primero.** En `Take`, si `exit` es un selector de UIA (`UiaSelector.Owns`) y hay ventana de
   trabajo (`VentanaDeTrabajo`, gancho que ya existe), se pregunta por ese elemento
   (`PreguntaPorSelector(hwnd, selector)`, gancho con implementación por defecto en `UiaSurface.Preguntar`:
   un `FindFirst` con caché de Name/ControlType/BoundingRectangle, 3 propiedades en una petición). Está →
   `Vista` de uno, y la mano pulsa como hoy. No está, o no es de UIA, o no hay ventana → se inventaría como
   hoy. La cuenta y el log dicen cuál camino y cuántos ms. **SAP no entra**: sus selectores resuelven por
   su API y su compuerta ya no mira (`MirarOtraVezLaVentana` devuelve false en `sapgui://`).
7. **Las candidatas, con caja y por identidad.** `PuertasDeAhora` sale de la observación: UIA aporta
   candidatas con caja e identidad; el terreno (`PuertasVivas`) y los campos de SAP aportan candidatas sin
   caja, y se dice. La fusión deja de ser por etiqueta y pasa a ser **por selector**, que es la identidad que
   la mano usa (287): dos «Detalles» con distinto tipo son dos candidatas. Los ids numerados que van al
   decisor y la lista de `map_what_i_see` se construyen de la misma lista, en el mismo orden (285). La
   tupla `(Selector, Etiqueta, Tipo)` que hoy ven `Puertas`, `PuertasVivas` y los ayudantes del contrato
   **no cambia**: la caja va en una lista paralela `Candidatas` de la que la tupla se deriva, para no tocar
   los ayudantes de 284–292.
8. **El barrido del reproductor es uno.** `UiaSurface` gana `BarridoUia` (genérico y puro, como
   `UiaReader.Recoge<T>`): recorre una vez y de ese único recorrido salen los tres derivados —campos, cuántos
   y huella— **iguales a los de hoy** (mismo filtro `Interactive`, visibles y habilitados, 40 niveles, 300
   elementos, huella por `AutomationId|tipo` o `@ruta|tipo`, nunca texto). Vigencia corta con nombre
   (`BarridoUia.VigenciaMs = 250`, menos que `SurfaceReadiness.PollMs = 120` × 2: un sondeo de carga tiene que
   ver el árbol de verdad, no el de hace un segundo). El log (`Log`/`LogGlobal`, tag `uia`) dice
   «barrido nº N: M elementos en X ms · servido a ReadFields/ReadinessCount/StructureFingerprint».
9. **Neo4j, medido antes que movido.** `ProyectorNeo4j.Proyectar(grafo, desde)` cronometra cada envío y lo
   cuenta (`Cuenta`) cuando pasa de 200 ms o falla, con causa y con `desde` («ubicación», «latido»,
   «cruce»); «Neo4j no responde» se dice **una vez por minuto**, y el aviso ya no se puede gastar en el
   constructor con `Cuenta` a null (`Cuenta` entra por el constructor). Un resumen por minuto —cuántas,
   cuánto sumaron, cuántas fallaron— es lo que el nivel 4 compara con Neo4j arriba y abajo. **Solo si esa
   medida dice que bloquea**, la 367 (reservada) lo saca del hilo.
10. **El «dónde» se pregunta una vez por instante.** `SurfaceLocator.Ahora()` sirve la última ubicación
    calculada mientras la ventana de delante y su título sean los mismos y tenga menos de 400 ms; con SAP
    delante se calcula siempre (la misma excepción que `Probe` hace hoy en `:249`). Es la misma memoria de
    400 ms que `DondeTrabajo` ya tiene, puesta donde la comparten los 25 sitios.

## Las promesas

El enunciado es el que va **literalmente** en `tests/ContratoDelGrafo/Contrato.cs`. Se juzgan sin pantalla,
sin SAP y sin TypeSafe: con árboles de mentira, lectores falsos que cuentan, relojes inyectados y un
proyector apuntado a `127.0.0.1:1`.

| # | Promesa | Fase |
|---|---|---|
| 361 | leer la ventana en foco para el reproductor es UN barrido: ReadFields, ReadinessCount y StructureFingerprint salen del mismo recorrido —el árbol se recorre una vez y sirve a los tres— y recogen lo mismo que hoy: interactivos, visibles y habilitados, hasta 40 niveles y 300 elementos, con la huella por id o ruta más tipo y nunca por texto; un barrido que ya caducó se repite; y el log dice cuántos barridos hubo, cuánto costó cada uno y a quién sirvió | 1 |
| 362 | una lectura se comparte: leer una ventana publica una observación con versión —ventana, dónde, cuándo, cuánto costó, cómo se leyó y sus candidatas con selector, etiqueta, tipo, caja e identidad—; quien puede reutilizar la pide por ventana con una edad máxima de un segundo y la recibe sin leer; más vieja, de otra ventana o después de accionar, se lee de nuevo y la nueva reemplaza a la vieja; y el mapa cuenta por paso cuántas lecturas fueron nuevas y cuántas reutilizadas | 2 |
| 363 | lo que el paso acaba de leer, la compuerta no lo vuelve a mirar: si el paso lleva la observación de su pantalla, la compuerta le cuenta al núcleo lo que esa observación vio —sin leer— y da por viva la puerta que estaba en ella, con 0 miradas y diciéndolo; si la puerta no estaba en la observación, o la observación es de otra pantalla o tiene más de un segundo, mira como hoy; y nunca se mira más veces que antes (264 y 299 intactas) | 3 |
| 364 | un paso que ya trae el selector no inventaría la ventana: se pregunta solo por ese elemento en la ventana de trabajo; si está, se pulsa con esa respuesta como observación de uno —sin leer la ventana entera y sin tocar la lista de vivos del núcleo—; si no está, o el selector no es de UIA, o no hay ventana de trabajo, se lee e inventaría como hoy; y la cuenta y el log dicen cuál de los dos caminos se usó y cuántos ms costó | 4 |
| 365 | las candidatas que van al decisor salen de esa misma lectura, con selector, etiqueta, tipo y caja; se ofrecen vivas —con nombre y con geometría leída— y sin duplicar por identidad: dentro de una lectura por su RuntimeId y al fundir con el terreno por su SELECTOR, nunca por su texto, así que dos puertas con el mismo nombre y distinto tipo son dos candidatas; una del terreno o de SAP sin caja leída se ofrece sin caja y se dice; los ids ofrecidos y la lista de map_what_i_see salen de la misma lista en el mismo orden (285); y el log dice cuántas se ofrecieron de cuántas, cuántas sin caja y cuánto costó | 5 |
| 366 | proyectar en Neo4j se mide y se dice: cada proyección —desde la ubicación, desde el latido o desde un cruce— deja su coste, y si tarda más de 200 ms o falla deja una línea con la causa y desde dónde se pagó; Neo4j caído se dice una vez por minuto y no una vez por proceso, también cuando cae antes de que haya quien lo cuente; y un resumen por minuto dice cuántas proyecciones hubo, cuánto sumaron y cuántas fallaron | 6 |
| 367 | **reservada, condicionada al nivel 4 de la 366**: proyectar no bloquea el hilo de la ubicación —con Neo4j caído Proyectar vuelve en menos de 50 ms, hay una sola escritura en vuelo y tras un fallo no se reintenta en 30 s—. No se escribe en el contrato hasta que la medida diga que hace falta; el número no se recicla | — |
| 368 | cada paso decidido se publica como un evento tipado —dónde, objetivo, las candidatas con su caja, los ids ofrecidos en su orden, la decisión tal como la devolvió el decisor, cuánto costó leer, decidir y pulsar, y el paso que contó el tramo—, también cuando no se acciona; sin suscriptor no cuesta nada ni cambia nada; y lo que el evento lleva es exactamente lo que se ofreció (285) | 7 |
| 369 | dónde estoy se pregunta una vez por instante: mientras la ventana de delante y su título sean los mismos y la respuesta tenga menos de 400 ms, se sirve la última calculada sin volver a leer; cambia la ventana o el título, o pasan los 400 ms, y se calcula de nuevo; con SAP delante se calcula siempre; y el log dice por minuto cuántas veces se calculó y cuántas se sirvió de memoria | 8 |
| 370 | **reservada**: los lectores de fondo se suscriben a la observación o se pausan durante un tramo (`RastroDelCursor`, `UiInspector`, `Probe`, `MirarDonde`), y SAP guarda los campos del dynpro por pantalla. Requiere `Ui/` (vetado en este encargo) y una sonda en el hospital. El número no se recicla | — |

**La que de verdad cierra el asunto es la 362**: sin observación compartida, 363, 364, 365 y 368 son
parches en sitios distintos que volverían a desincronizarse (dos caminos para enumerar el mismo terreno,
que es justo lo que la 285 prohíbe).

### Con qué se juzga cada una, y el sabotaje de una línea

| # | Cómo se juzga (sin pantalla) | Sabotaje que la pone roja |
|---|---|---|
| 361 | `BarridoUia.Recorre<T>` sobre un árbol de mentira con un delegado `hijos` que cuenta llamadas: de UN barrido se piden campos, cuantos y huella → `hijos` se llamó las mismas veces que con uno solo; la huella es `Fingerprints.Of` de `id\|tipo` o `@ruta\|tipo` (dos árboles que solo difieren en texto dan la misma); un nodo `IsOffscreen`, uno deshabilitado y un `Text` no cuentan; con reloj inyectado, pasado `VigenciaMs` se recorre otra vez y el `Cuenta` capturado trae «barrido nº1 … nº2 … servido a» | en `BarridoUia`, quitar el `\|{tipo}` de la huella (dos controles idénticos con distinto tipo dan la misma) — o hacer que cada derivado recorra de nuevo |
| 362 | `Observatorio` con reloj inyectado: `Publica` sube la versión; `Reciente(h, 1000)` devuelve la misma dentro del segundo y null después; `Invalida` deja null; otra ventana → null. Y `SurfaceMapTools` con el gancho `Lee` falso que cuenta: dos `map_what_i_see` seguidos → 1 lectura; reloj +1.100 ms → 2; un `map_take` con mano falsa → el siguiente `map_what_i_see` lee (3); la cuenta del paso del tramo lleva «lecturas: 1 nueva · 1 reutilizada» | en `Reciente`, ignorar la edad (`return _ultima;`): el caso «+1.100 ms lee de nuevo» falla |
| 363 | `RecorrerSegunElNucleo` con `MiraOtraVez` falso que cuenta y un grafo que NO tiene la puerta viva: `Paso("uia:name=X;ct=Button") { Vista = observación completa con X }` → se pulsa X, miradas = 0, el diario dice que usó lo que el paso acababa de leer; el mismo paso sin `Vista` → miradas ≥ 1 como hoy; con `Vista` de hace 1.500 ms → miradas ≥ 1; con `Vista` de otra pantalla → miradas ≥ 1; y las miradas nunca superan las de hoy en ningún caso | en `EsperarloVivo`, no leer `paso.Vista`: el caso «miradas = 0» falla |
| 364 | `SurfaceMapTools` con `VentanaDeTrabajo = () => 1`, `PreguntaPorSelector` falso que cuenta y `Lee` falso que cuenta: `map_take exit=uia:name=A;ct=Button` → preguntas 1, lecturas 0, la mano recibió ese selector, la cuenta dice «pregunté por … está … ms», y `DesdeAqui` conserva los vivos que tenía; `map_take exit=Etiqueta` → preguntas 0; `exit=sap:…` → preguntas 0; sin `VentanaDeTrabajo` → preguntas 0; pregunta que contesta «no está» → se lee (lecturas 1) | en `Take`, preguntar DESPUÉS de leer (o leer siempre): el caso «lecturas 0» falla |
| 365 | `Lee` falso con «Detalles» (Button, caja) y `PuertasVivas` con «Detalles» (RadioButton) y «Guardar» (Button): candidatas = 3 —la del terreno con el mismo nombre y otro tipo NO se descarta—; «Guardar» sale con `Caja.IsEmpty` y la cuenta dice «1 sin caja»; los ids que recibe el decisor son los de `map_what_i_see` en su orden; dos elementos con la misma identidad (RuntimeId) en la lectura salen una vez; el log trae «candidatas: 3 de 3 (2 con caja · 1 sin caja) en N ms» | en `FundirPuertas`, volver a comparar por etiqueta: «Detalles (RadioButton)» del terreno desaparece |
| 366 | `new ProyectorNeo4j("http://127.0.0.1:1", cuenta: captura, reloj: falso)`: el constructor (índices) NO gasta el aviso; `Proyectar(g, "latido")` → una línea «Neo4j no responde … (N ms) · desde latido»; otro `Proyectar` dentro del minuto → 0 líneas nuevas; reloj +61 s → 1 más; `UltimaProyeccionMs ≥ 0`; el resumen por minuto trae «proyecciones: 2 · N ms · 2 fallidas» | volver a `_yaAvise` una vez por proceso: el caso «+61 s → 1 más» falla |
| 368 | `MapaParaDecidir` + `AlDecidir += e => …`: tras `map_decidir`, `e.Ofrecidas` es lo que recibió el decisor, `e.Candidatas.Count == 3` con sus cajas, `e.Decision.Puerta == "2) …"`, `e.Ms.Leer/Decidir/Pulsar ≥ 0`, `e.Paso.Actuo`; con un decisor que dice No, el evento también sale y `e.Paso.Actuo == false`; sin suscriptor, la cuenta de `map_decidir` es byte a byte la de hoy | disparar el evento solo cuando se acciona: el caso «decisor que dice No» falla |
| 369 | `MemoriaDeUbicacion` pura con reloj inyectado: `Sirve(h, "t", esSap:false, ahora)` calcula la primera; la segunda a +200 ms con el mismo `(h, "t")` se sirve de memoria; cambia el título → calcula; +400 ms → calcula; `esSap:true` → calcula siempre; el `Cuenta` capturado trae «dónde: N calculadas · M de memoria» | devolver siempre «calcula»: el caso «+200 ms se sirve de memoria» falla |

## Las fases

Una fase = un commit que pone verde UNA promesa sin romper las anteriores. Toda la spec vive en esta rama;
no se mergea fase a fase.

| Fase | Qué | Promesa | Archivos | Terminado |
|---|---|---|---|---|
| 0 | las promesas, en ROJO | 361–366, 368, 369 | `tests/ContratoDelGrafo/Contrato.cs` | el contrato dice `CONTRATO ROTO: 8 promesa(s)` y cada una es `PENDIENTE` por el nombre que pide (`BarridoUia`, `Observatorio`, `Paso.Vista`, `PreguntaPorSelector`, `Candidatas`, `ProyectorNeo4j(url, cuenta, reloj)`, `AlDecidir`, `MemoriaDeUbicacion`); 341 y las anteriores siguen verdes |
| 1 | el barrido del reproductor es uno | 361 | `windows-graph/src/Surfaces/UiaSurface.cs` (+ `BarridoUia.cs` junto a `Fingerprints.cs`) | 361 verde; **3 sitios** (`:470, :493, :510`) llaman al barrido compartido; `WorkflowPlayer`/`SurfaceReadiness` sin tocar |
| 2 | la observación compartida | 362 | **nuevo** `windows-client/src/Uia/Observacion.cs`; `Uia/UiaReader.cs` (publica al final de `Read(hwnd)`); `Mcp/SurfaceMapTools.cs` (`Lee`/`Observa` en `PuertasDeAhora`, señalar e inventario; `Invalida` en `Take` y `Type`; cuenta de lecturas en `tiempos`); `Navigation/MapaVivo.cs` (`Invalida` al cambiar de sitio) | 362 verde; 297/298 intactas (`Read` sigue leyendo siempre); **5 sitios** de lectura del paso pasan por el observatorio (3 en `SurfaceMapTools` reutilizan; los 2 de `FaceWindow` publican sin tocarse) |
| 3 | la compuerta no vuelve a mirar | 363 | `Navigation/RecorrerSegunElNucleo.cs` (`Paso.Vista`; `EsperarloVivo(paso)`); `Navigation/MapaVivo.cs` (`LoQueEsPuerta` pública); `Mcp/SurfaceMapTools.cs` (`Take` adjunta la vista) | 363 verde; 264 y 299 intactas (mismo número de miradas sin vista) |
| 4 | selector primero | 364 | `windows-graph/src/Surfaces/UiaSurface.cs` (`Preguntar(hwnd, selector)`, público); `Mcp/SurfaceMapTools.cs` (`PreguntaPorSelector`, `Take`) | 364 verde; 203 (homónimos con `which`) y 331 (nombre recortado) intactas: preguntar usa `UiaSelector` como `Resolve` |
| 5 | candidatas con caja, por identidad | 365 | `Mcp/SurfaceMapTools.cs` (`Candidatas`, `FundirPuertas` por selector, la línea «candidatas:»); `Uia/UiaReader.cs` (los 2 deduplicados de menú pasan a identidad; mismo resultado) | 365 verde; 285/287/288 intactas (misma tupla, mismo orden); **3 sitios** de fusión por texto → 0 |
| 6 | Neo4j medido | 366 | `nucleo/Grafo/ProyectorNeo4j.cs` (constructor con `Cuenta` y reloj; `Proyectar(grafo, desde)`; aviso por minuto; resumen); `Navigation/MapaVivo.cs` (los **4 sitios** de `Proyectar` pasan `desde`) | 366 verde; el contrato del núcleo (`nucleo/Contrato`) sigue igual: la firma vieja `ProyectorNeo4j(url, usuario, clave)` se conserva |
| 7 | el evento para quien pinta | 368 | `Mcp/SurfaceMapTools.cs` (`AlDecidir`, `PasoDecidido`) | 368 verde; D tiene qué consumir |
| 8 | el dónde con memoria | 369 | `windows-client/src/Uia/SurfaceLocator.cs` (`MemoriaDeUbicacion` pura + `Ahora()`) | 369 verde; 230 (la ventana de delante con la carita) y 233 (`Identificar`) intactas: `Identificar` no pasa por la memoria |
| 9 | sabotajes por diff, uno por promesa; `verificar.ps1`; `out\evidencia.md` | todas | — | ocho veredictos `CONTRATO ROTO` nombrando cada promesa, ocho diffs idénticos al restaurar, `CONTRATO INTACTO` al final |

**Orden y por qué.** La 1 va primero porque no toca nada del camino de Jev y deja el patrón (barrido genérico
+ vigencia + cuenta) que la 2 copia. La 2 antes que la 3, 4, 5 y 7 porque todas consumen la observación. La
6 y la 8 son independientes y van al final para que un rebase sobre A/B no las arrastre.

**Sitios con esta clase de error, contados (patrón nº5) y para el commit:** lecturas completas por paso, 5;
`Walk` en `UiaSurface`, 3; fusión por texto, 3; `Proyectar` síncrono, 4; «dónde» sin memoria, 25 llamadas
por 1 función; aviso de Neo4j que nunca sale, 1.

## Lo que NO entra, y por qué

- **`FaceWindow.xaml.cs` y todo `Ui/`.** El encargo lo veta y la arquitectura dice que solo haya una feature
  de UI abierta (D espera a C). Consecuencias, dichas: `ObservarLaVentanaDeTrabajo` (`:5650-5671`) sigue
  leyendo por su cuenta (con su freno de 800 ms) —publica en el observatorio y **cuenta como lectura nueva**
  en la línea del paso, así que el nivel 4 la verá—; `_where` (`:434`) sigue siendo el crudo, pero la 369 le
  pone la memoria por debajo sin tocarlo. Las tres líneas que D o el dueño pueden cablear cuando entren a
  `Ui/`: `ObservarLaVentanaDeTrabajo` pidiendo `Observatorio.Reciente` antes de leer; el latido (`:516`)
  igual; `MirarOtraVezLaVentana` no cambia (tiene que ser fresca por la 299).
- **`Decision/`** (calentar la conexión, `input_tokens`, un cliente por proceso: mejora 7): vetado; es de A o
  de otra rama.
- **`PulsarSegunElNucleo` y la espera tras pulsar**: es B (spec 047). Cuando B entre, su huella barata
  sale de la observación de la 362 —lo dice la arquitectura— y no al revés.
- **La 367** hasta que la 366 mida en el nivel 4 con Neo4j arriba y abajo. El informe ya avisa de que la
  mejora 1 «está medida en latidos, no en clics».
- **Pausar `RastroDelCursor` y `UiInspector` en tramo** (370): `Ui/`.
- **El cuerpo sin duplicados** (mejora 18), **candidatas por relevancia** (19, roza la 285 «en su orden») y
  el **historial en el `state`** (8, choca con el caso 6 de la 292): se miden en sombra antes, y con el dueño.
- **SAP**: `ReadFields` por pantalla (mejora 14), `GetObjectTree` (15), la carrera del `Busy`. Sin sonda en el
  hospital no hay medida, y sin medida no hay promesa.
- **El `Contains("puertas vivas para")`** de `SurfaceMapTools.cs:329`: es redundante con `Mano.Intento`
  (que ya es `!r.Ambiguo`), así que quitarlo no cambia comportamiento; se quita en la fase 5 sin promesa
  propia, y se dice en el commit.
- **Unificar `Interactive` y `Actionable`**: dos definiciones de accionable en dos lectores (hallazgo). La
  361 promete «lo mismo que hoy» a propósito: cambiar qué cuenta como interactivo cambiaría las huellas de
  todos los workflows grabados. Es otra promesa, con su medida.
- **`LockApp` leído nodo a nodo 41 veces**: el latido lee la pantalla de bloqueo, que rechaza la caché por
  timeout. Hallazgo para el dueño; no es de esta spec.

## Nivel 4, pendiente (lo que el contrato no puede juzgar)

Con Jev **apagado o en simulado** (A no ha entrado: con `U_DECISOR=jev` sobre `sapgui://` viajan filas con
nombre) y **fuera de SAP**:

1. **Lecturas por paso, antes y después**, en ≥2 pantallas con nombre: **Explorador «Descargas»** (la
   lectura con el lector actual está sin medir: es la primera cifra que hay que traer) y **Chrome** (Gmail o
   ChatGPT.exe, las del 21-09). Línea `tramo:` con «lecturas: N nuevas · K reutilizadas» y las de
   `compuerta:` con «0 miradas». Meta (D): de 2–5 lecturas por paso a 1 (+1 si hay ventana de trabajo ≠ foco).
2. **Selector primero**: `map_take exit=uia:name=…;ct=…` en las dos pantallas; la línea «pregunté por … en N
   ms» y que no salga «leí la ventana» en ese `map_take`.
3. **Neo4j arriba y abajo**: un `map_take` que no navega y un tramo de 3 pasos en cada estado; comparar el
   resumen por minuto de `proyectar`, los `SATURADO` por hora y el «⏱ pulsar» del clic. Es la medida que
   decide si la 367 se escribe.
4. **El reproductor**: un workflow UIA de la biblioteca sobre el Explorador (paso a paso), contando
   «barrido nº» por paso. Sin SAP en esta máquina, el lado SAP de 361 queda **sin probar** y se dice en el PR.
5. **La memoria del dónde**: «dónde: N calculadas · M de memoria» por minuto en reposo y durante un tramo.

## Hallazgos

- **2026-09-22 · numeración.** `f811796` (voz, Felipe) usa la **341** en `main`. La rama A tiene reservadas
  341–350 en la arquitectura: **A tiene que desplazarse** (342–351 o lo que esté libre al abrirla). Los
  361–370 de esta spec están libres en las 44 refs `origin/*` (`git grep` de `Prueba("36`/`Pendiente(…, "36`)
  y `docs/specs/046-049` no existen en ninguna. El `046` sin commitear del dueño reserva 335–343, que ya
  usan la 044 y la voz: también hay que renumerarlo.
- **2026-09-22 · «Neo4j no responde» no puede salir nunca**: el aviso se gasta en el constructor
  (`AsegurarIndices`) cuando `Cuenta` todavía es null. Lo arregla la 366; es la razón de que 23 logs con
  Neo4j caído no lo digan ni una vez.
- **2026-09-22 · `FundirPuertas` funde por texto**: una puerta del terreno con el mismo nombre que una de UIA
  desaparece aunque sea otro elemento. Es el «Analytics 0,91/0,09» de TipTour, en casa. Lo arregla la 365.
- **2026-09-22 · el mapa vivo pierde el 67–85 % de sus latidos** mientras Neo4j esté caído (197 avisos hoy).
  Con Neo4j arriba también hay días con SATURADO (26-08: 32; 03-09: 51): la 366 es lo que dirá cuánto es
  Neo4j y cuánto es la lectura.

## Cierre

- [ ] Todas las promesas escritas verdes (`.\scripts\contrato-del-grafo.ps1` → `CONTRATO INTACTO`)
- [ ] Un sabotaje por promesa, comprobado por diff (copia, rompe, diff, compila sin silenciar, `ROTO`
      nombrándola, restaura, diff idéntico, recompila, `INTACTO`)
- [ ] `.\scripts\verificar.ps1` pasa, con evidencia en `out\evidencia.md`
- [ ] Probado en ≥2 pantallas, con nombre: …
- [ ] Merge de `origin/main` antes del push; A y B ya dentro
- [ ] Estado de este documento: **implementada** (AAAA-MM-DD)
