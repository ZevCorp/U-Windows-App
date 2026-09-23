# La espera mira lo que se ve

> Spec 047 · 2026-09-22 · rama `jero/jev-la-espera-mira-lo-que-se-ve` · promesas **351–359** (la 360 queda
> reservada y no entra al contrato) · Estado: **en construcción**
>
> Es la rama **B** de la nueva arquitectura de Jev en Ü (`arquitectura-jev-en-u.md`, §3: «la espera mira lo
> que se ve»). Va en paralelo con la A (spec 046, la distribución se valida entera) y antes que la C (048) y
> la D (049). Nace de la revisión del 21-09 (`informe-jev-rapido.md`, §2.A y mejora 2) y del log
> `u-20260921.log`, leído hoy línea a línea.
>
> **Marcas.** **(M)** medido: log, `grep`, sonda. **(D)** deducido del código o de sumar medidas. Un número
> sin marca es una meta, no un dato.
>
> **Números comprobados el 2026-09-22** (dos veces: por la mañana sobre 44 refs `origin/*`, y tras el
> `fetch` + `merge origin/main` de la tarde sobre 46): ni `047-` en `docs/specs` ni `"351.`…`"360.` en ningún
> `Contrato.cs` remoto. En `main` (`5574148`) la promesa más alta del grafo es ya la **343** (voz y log:
> 341 «dos recordatorios vencidos despiertan una sola sesión», 342 «una petición personal explícita se puede
> guardar», 343 «cada proceso de Ü escribe en su propio archivo de log»; las tres entraron hoy) —no la 340
> que dice `arquitectura-jev-en-u.md` §6—; los 351–359 de esta spec siguen libres. La rama A ya renumeró a
> 342–350 por el choque con la 341, y con `5574148` sus 342 y 343 vuelven a chocar: se avisa aquí y lo
> resuelve A al rebasar, no esta rama. Y la 343 cambia el nombre del log que esta spec pega en el nivel 4:
> ya no es `u-AAAAMMDD.log` sino `u-AAAAMMDD-<instancia>.log` (`LogBus.TodayFile`, con origen, PID y hora
> de arranque).

## De dónde sale

Tras pulsar, Ü espera **hasta 1.800 ms** a que cambie la *ubicación* de la ventana de trabajo
(`PulsarSegunElNucleo.EsperaMaximaMs`, `EsperarACambiar`, `:299-314`), y sale antes solo si cambia. En un
clic que no navega —un botón que abre un menú, «Minimizar», un selector de modelo— ese cambio no llega
nunca, y el tiempo se paga entero. Y como lo único que se mira es la ubicación, **un cambio que sí ocurrió
pero no movió la ubicación se declara «no cambió»**.

## Lo que se midió antes de escribir código

Las 13 pulsaciones con reloj del 21-09 (16:34:04–16:39:45, ChatGPT.exe y Gmail en Chrome, decisor apagado),
leídas hoy del log (M):

```
[16:34:04] ⏱ pulsar «Chrome»:              la mano 422 ms · esperar el cambio 1810 ms (17 sondeo(s)) · no cambió
[16:34:09] ⏱ pulsar «isabel»:              la mano 607 ms · esperar el cambio 1804 ms (14 sondeo(s)) · no cambió
[16:34:12] ⏱ pulsar «Jeronimo»:            la mano 333 ms · esperar el cambio 1799 ms (16 sondeo(s)) · no cambió
[16:35:00] ⏱ pulsar «Minimizar»:           la mano 506 ms · esperar el cambio 1810 ms (18 sondeo(s)) · no cambió
[16:36:04] ⏱ pulsar «GPT-5.6 Sol Medio»:   la mano 405 ms · esperar el cambio 1812 ms (16 sondeo(s)) · no cambió
[16:36:10] ⏱ pulsar «Seleccionar modelo»:  la mano 322 ms · esperar el cambio 1816 ms (14 sondeo(s)) · no cambió
[16:36:14] ⏱ pulsar «GPT-5.6 Sol Medio»:   la mano 415 ms · esperar el cambio 1800 ms (15 sondeo(s)) · no cambió
[16:36:23] ⏱ pulsar «Seleccionar modelo»:  la mano 328 ms · esperar el cambio 1799 ms (18 sondeo(s)) · no cambió
[16:36:58] ⏱ pulsar «GPT-5.6 Terra»:       la mano 296 ms · esperar el cambio 1806 ms (14 sondeo(s)) · no cambió
[16:38:43] ⏱ pulsar «Nuevo chat»:          la mano 318 ms · esperar el cambio 1806 ms (13 sondeo(s)) · no cambió
[16:39:00] ⏱ pulsar «Documents»:           la mano 360 ms · esperar el cambio 1811 ms (14 sondeo(s)) · no cambió
[16:39:25] ⏱ pulsar «Nuevo chat»:          la mano 305 ms · esperar el cambio 1797 ms (15 sondeo(s)) · no cambió
[16:39:45] ⏱ pulsar «Documents»:           la mano 327 ms · esperar el cambio 1825 ms (15 sondeo(s)) · no cambió
```

| Qué | Medida | Fuente |
|---|---|---|
| Clics que no navegaron | **13 de 13**; esperaron **1.797–1.825 ms** con 13–18 sondeos de «dónde» cada uno | log 21-09, arriba (M) |
| Un `map_take` que no navega, entero | mediana **2.837 ms** (2.651–3.612); 38,0 s en los 13; la espera es el **62 %** | `informe-jev-rapido.md` §2.A (M) |
| «No cambió» falsos | **≥2 de 13 (D, sobre líneas M)**. «isabel» (16:34:09): lo medido es que «señalar» leyó **137** elementos sobre el hwnd de Gmail (`:338`), que durante la espera el localizador rechazó 8 veces una ventana de Chrome «sin barra de direcciones» (`:348-356`), que el inventario de después contó **15** (`:370`, `LoQueVeo` → `PuertasDeAhora`, que lee el **primer plano**) y que el clic siguiente resolvió «Jeronimo» en `ventana='(sin título)'` (`:395`); lo deducido es que el selector de perfiles se abrió como **OTRA ventana de nivel superior** de Chrome, y que la ventana de Gmail no cambió por dentro. Los 137 → 15 no son dos lecturas de la misma ventana: son dos lectores sobre dos ventanas. En «Minimizar» (16:35:00): medido, «no cambió» y en la línea siguiente «la ventana de trabajo es ahora web://mail.google.com» (`:506-507`); deducido, que lo que cambió fue la ventana de delante. Ninguno de los dos lo vería una huella que solo mire el hwnd de trabajo: por eso la huella lleva una cuarta parte (regla, punto 1) | log 21-09 (M las líneas); informe §2.A y su revisión, reparo 8; refutación 3 («Revisiones», abajo) |
| Indicios por recuento, sin prueba | «Chrome» 111 → 133, «Jeronimo» 15 → 128, «Nuevo chat» 80 → 50 elementos entre señalar y el inventario de después | informe §2.A (M el recuento; D que sea un cambio real: los dos lectores pueden tener alcances distintos) |
| Por qué es falso | `EsperarACambiar` compara el STRING de ubicación de la ventana de trabajo y nada más | `PulsarSegunElNucleo.cs:299-314` (M, leído) |
| Cuánto tarda en cambiar la ubicación tras un clic que SÍ navega | «Discusión» cambió a los **388 ms**; mediana del 18-09, **405–510 ms** | spec 043 nivel 4; spec 040 y `RecorrerSegunElNucleo.cs:139-140` (M) |
| Cuánto cuesta mirar la ventana de trabajo | **34–243 ms** con el lector con caché (297/298); 17 líneas «señalar … leí la ventana» el 21-09, máximo 243 | specs 040/041; log 21-09 (M). El Explorador **sin medir** con el lector actual |
| Cuánto cuesta saber qué ventana está delante | `GetForegroundWindow` + `GetWindowText`: Win32, sin UIA | `UiaSurface.cs:17, 25, 425` (D: <1 ms, no cronometrado) |
| `_donde()` durante la espera | memoria de **400 ms** (`MemoriaCorta<string>(400)`; `Pide` sirve lo recordado dentro de la caducidad): un cambio de ubicación se ve con hasta 0,4 s de retraso, y la mediana del cambio de sitio es 405–510. **Afecta a la corrección del veredicto, no solo al ahorro**: con la regla de esta spec, dos huellas iguales con el sitio viejo y lo de dentro nuevo serían «asentada / dentro», la espera terminaría con `hasta == desde` y `Cruzar` no aprendería la puerta (`PulsarSegunElNucleo.cs:232-238`). Por eso el sitio de la huella no sale de la memoria (regla, punto 2b) | `FaceWindow.xaml.cs:5577-5583`, `MemoriaCorta.cs:38-46` (M, leído); refutación 1 |
| Lo que hacen seis referentes tras actuar | **dos** cortan con una condición barata sobre lo que se ve: jev-ultrafast (2 cuadros, tope 50 ms; `r1/…/browser.py:54-63`) y jkudish (**dos huellas iguales cada 250 ms, tope 1,5 s**; `r5/…/navigate.ts:385-404`). **Tres duermen un tiempo fijo** —awlevin 2 s, JetDesk 700 ms, PlayJev 150 ms— y eso no se copia | informe §4 (M sobre el código clonado) |

**Dónde están hoy las esperas fijas, contadas con `grep` el 22-09** (el encargo pedía contarlas en
`ComoSePulsa`, `PulsarSegunElNucleo` y `VentanaDeTrabajo`):

| Archivo | `Thread.Sleep`/`Task.Delay` | Presupuestos fijos | Qué hace esta spec |
|---|---|---|---|
| `windows-graph/src/Surfaces/ComoSePulsa.cs` | **0** (es pura) | 0 | nada: la premisa del encargo era falsa aquí |
| `windows-client/src/Navigation/VentanaDeTrabajo.cs` | **0** | 0 | nada |
| `Navigation/PulsarSegunElNucleo.cs` | **0** | **2**: `EsperaMaximaMs = 1800` (3 sitios: `:162`, `:201`, `:225`) y `EsperaDeCampoMs = 300`; más la cadencia `Respira(120)` | el 1.800 pasa a ser un **techo calculado** (359) y las tres esperas salen por **condición** (351, 357); la cadencia se queda: es el sondeo, y el compás lo acota |
| `Navigation/RecorrerSegunElNucleo.cs` | 0 | `EsperaMaximaMs` (1800; 4000 en la app) en `LlegoDondeTocaba` y en la compuerta | `LlegoDondeTocaba` sale por condición (356); la compuerta no se toca (299) |
| `Mcp/SurfaceMapTools.cs` | **6**: `:2200` (150), `:2340` (120), `:2729` (50), `:2873` (90), `:2906` (80), `:2923` (200) | los tres bucles `EsperarPantallaLista`/`EsperarCambio`/`Llego` cuentan **vueltas**, no reloj | los tres de `:2865-2926` (358); los otros tres son de otras herramientas y quedan anotados |
| `Navigation/PasoDelNucleo.cs:231` | 1 (600) | 3 esperas por ubicación (`:117-135`, `:164-172`, `:192-194`) | **fuera por alcance**: es el camino de «ir» (`map_go_to`: `ServidorDelNucleo.cs:55`, `FaceWindow.xaml.cs:742-756`), no el de `map_take`. No es de la rama de Jose («Revisiones», 6): queda como **pendiente con dueño** |
| `Navigation/AbrirSegunElNucleo.cs:290` | 1 (120) | 1 | fuera (misma clase, fuera del ciclo) |
| `Navigation/SurfaceNavigator.cs:85`, `MapaVivo.cs:506` | 1 + 1 | — | fuera (no son la espera tras pulsar) |
| `windows-graph/src/Surfaces/UiaSurface.cs` (la mano) | **27** | — | fuera: son ~160 ms por clic identificados de 333 (D), mejora 3 del informe, se miden antes de tocarlos |

Los sitios que calculan «cambió» por ubicación o recuento, sin huella: **11** (revisión del 22-09,
`arquitectura-jev-en-u.md` §3.2). Esta spec toca **7**: los 3 de `Pulsa`, `LlegoDondeTocaba` y los 3 de
`SurfaceMapTools`. Los 3 de `PasoDelNucleo` (más su `Sleep(600)`) quedan fuera **por alcance** —son el camino
de «ir», no el de pulsar— y se cuentan como pendientes con dueño; `Abrir` queda fuera. Y hay un sitio **más**
de la misma clase en camino: `origin/jose/ir-devuelve-la-pagina-asentada` trae `ComoSeContesta.InventarioAsentado`
(dos cabeceras de inventario iguales —dónde y cuántos—, 250 ms de pausa, 2 s de tope, por `Thread.Sleep`)
dentro de `SurfaceMapTools.Call`. Cuando entre son **12**, y se reconcilia abajo.

## Por qué esto va dirigido por especificación

Porque la espera **se juzga a sí misma**: «no cambió» lo dice el mismo criterio que decide cuánto esperar, y
el 21-09 dio 13 de 13 con al menos 2 falsos sin que nada lo señalara. Y porque acortar esta espera **ya se
decidió no hacerlo sin medir**: la spec 043 dejó escrito «acortar esa espera sin medir es la spec 038,
aparcada por el dueño», y la promesa 334 lo exige en su caso 4 («un botón sigue esperando el presupuesto
entero», `Contrato.cs:13010-13011` tras el merge de `5574148`). Una prueba escrita después del código se escribiría para que pasara;
aquí la primera fase **solo mide**, y la regla entra cuando la medida existe.

**La regla del flujo, y no tiene excepciones:** ninguna línea de producción entra antes que la promesa que
la juzga. Cada fase empieza con el contrato ROTO y termina con el contrato INTACTO.

## La regla

**Una pantalla que está cargando cambia entre dos miradas; una asentada, no.** Es la regla de la spec 040,
que hoy solo rige en la compuerta de vida (299). Se lleva a la espera de después de pulsar:

1. **La huella de lo que se ve** tiene cuatro partes: el **sitio** (la ubicación de trabajo, leída
   **fresca**, no de la memoria de 400 ms: punto 2b), la **ventana de delante** (hwnd + título, por Win32,
   sin UIA), **lo de dentro** (una huella **barata** de la ventana de trabajo: los `RuntimeId` + tipo de sus
   accionables, pedidos con UN `FindAll` bajo un `CacheRequest` que solo trae esas dos propiedades —una llamada
   entre procesos, sin `.Current` por nodo, sin `Reconocedor.SelectorDe`—; y cuando exista la observación de
   la 362 (rama C), su hash sin leer: `Observatorio.Reciente`) y **las ventanas del proceso de trabajo** (los
   hwnd visibles de nivel superior con el mismo pid que la ventana de trabajo, por `EnumWindows` +
   `GetWindowThreadProcessId`, sin UIA, ignorando las de Ü por `Propio.EsVentana`). La cuarta es la que ve
   un popup como el selector de perfiles de Chrome: es OTRA ventana (`(sin título)`, log 21-09 `:395`) y no
   cuelga del hwnd de Gmail, así que `UiaReader.Read(hwnd)` —`FromHandle(hwnd)` + hijas de `EnumChildWindows`
   de clases fijas + `MenuItem` del subárbol— no la vería. **Lo que cuesta cada parte no está medido** (D: las
   tres de Win32 <1 ms; el `FindAll` con caché, sin dato: el `CuantosAccionables` de hoy hace tres sobre el
   primer plano y nadie lo cronometró): la fase 0 lo mide por sondeo y por espera en las tres pantallas, y en
   el Explorador ANTES de la fase 2. Lo que NO se usa para «dentro» es la lectura completa (`UiaReader.Read`
   → selectores): son 144–420 ms medidos (Bloc 144, Configuración 155, Wikipedia 270, Explorador 420;
   `UiaReader.cs:127-131`, M 18-09), más que el respiro, y sería el patrón nº14 (subir la cadencia sin bajar
   el costo por iteración). Se construye y se compara en **un solo sitio**, `Navigation/HuellaDeLoQueSeVe.cs`,
   y la compuerta de vida (299) pasa a usar esa definición para sus dos partes de siempre (sitio y dentro).
   Aprendizaje nº16: hoy «cambió» se calcula en 11 sitios con criterios distintos.
2. Tras la mano, cada vuelta del compás (120 ms) toma las partes baratas; la parte de dentro solo cuando las
   otras tres no cambiaron, y como mucho cada `RespiroMs`. **Dos huellas iguales separadas por un respiro
   ≥ 250 ms = asentada**: la espera termina en ese instante. Antes de `PrimeraHuellaMs` no se declara nada:
   una página que aún no empezó a pintarse parece asentada (es el 400 de `EsperaDeAsentarMs` en la compuerta,
   elegido «del orden de lo que tarda en verse un cambio tras un clic»). **Los dos números salen de la fase
   0**, no de este papel.
   - **2b. El sitio se relee fresco** —sin pasar por `MemoriaCorta`: `_dondeTrabajo.Olvida()` y `DondeTrabajo()`,
     el mismo par que `FaceWindow.xaml.cs:5679` ya usa tras accionar— al menos una vez por respiro y, **siempre,
     justo antes de declarar «asentada»**: si el sitio fresco difiere del de partida, el veredicto es *cambió
     de sitio* aunque las dos huellas hubieran coincidido, y `Cruzar` aprende. Sin esto, la memoria de 400 ms
     y la mediana de 405–510 del cambio de sitio se cruzan justo en la ventana en que dos huellas de dentro
     pueden coincidir: «asentada / dentro», `hasta == desde`, ninguna arista — el mapa aprendería menos de lo
     que cree (aprendizaje nº16), y en silencio, porque «dentro» no cuenta para el bucle. El coste de un
     `DondeTrabajo` fresco **no está medido** (los 2.771 ms del 15-09 eran del localizador de `map_where_am_i`,
     no de resolver por hwnd): fase 0.
3. El veredicto ya no es «cambió / no cambió» sino **cuatro**: *cambió de sitio* (la ubicación; sale en el
   acto, `Cruzar` aprende: 44 intacta), *cambió delante* (otra ventana al frente, lo de «Minimizar»),
   *cambió dentro* (un menú que se abrió en la ventana de trabajo, o una ventana nueva de su proceso: lo de
   «isabel», que solo la cuarta parte ve; la cuenta dice cuál de las dos) y *nada*. Dentro y delante **no
   acuñan arista** y no cuentan como «nada cambió» para el detector de bucle del tramo.
4. **Se espera como hoy hasta el techo** —sin recortar— cuando el terreno ya sabe que la puerta lleva a
   algún sitio (`SabeQueLleva`: es justo la puerta cuyo clic perdido repite la 248, y una «asentada» falsa
   ahí dispararía una segunda navegación), cuando la ubicación es `sapgui://` (no se puede mirar desde UIA:
   `FaceWindow.xaml.cs:5629`), y cuando **nadie inyectó una huella**. Esto último es lo que mantiene verdes
   la 245, la 248, la 296 y la 334 tal como están: sus arneses construyen `PulsarSegunElNucleo` sin huella.
5. **El techo** deja de ser «lo que dura una espera que no cambia» y pasa a ser una red. Sale de la espera
   normal medida: `máx(3 × mediana de las últimas esperas que terminaron por condición, techo mínimo)`. Las
   esperas que llegaron al techo no lo alimentan (se alimentaría a sí mismo). El techo mínimo se queda en
   **1.800** hasta que la fase 0 mida; con la medida, esta spec propone `p95 del «ms hasta cambio de
   sitio» + 300` (D: hoy la mediana es 405–510). Al agotarlo, la cuenta dice **cuál de las causas fue**
   (patrón nº2): la pantalla no paró de moverse · nadie miraba · el terreno sabía que lleva a algún sitio ·
   no se pudo mirar, y por qué.
6. **Las tres esperas de `Pulsa`** —tras el clic, tras el ensayo del doble (83) y tras la repetición
   (248)— consumen la misma regla. La repetición cae siempre en el caso 4 (solo se repite lo que se sabe
   que navega): espera el techo, como hoy.
7. **Una web que no para de moverse** (un reloj, un vídeo) nunca parece asentada y se queda en el techo: es
   el lado seguro, como en la 299.
8. **Un desvío no se declara en el acto.** Al comprobar la llegada (`LlegoDondeTocaba`), «asentada en otra
   pantalla» no basta: una navegación web pasa a menudo por una pantalla intermedia que se asienta unos
   cientos de ms y salta —`map_go_to docs.google.com` «y ya en …/document/u/0 (redirigió)», 12:10:56 del
   18-09, spec 044 de Jose (M)—. Se sigue mirando durante el **presupuesto de redirección**
   (`PresupuestoDeRedireccionMs`), y solo si al agotarlo lo visto sigue asentado en esa otra pantalla se
   declara el desvío nombrando las dos; si en ese plazo llega a la esperada es llegada, no desvío. El
   presupuesto sale de la fase 0 (p95 del «ms entre dos cambios de sitio seguidos» en las navegaciones con
   redirección); **hasta medirlo vale el techo**, con lo que la 356 no recorta nada y lo dice.

Lo que la huella **no** ve, y se dice: un cambio de estado sin cambio de selectores (un botón que pasa a
«pulsado», un texto que cambia de valor) es «nada». Para lo que esta spec mide —menús que se abren,
ventanas que cambian, navegaciones— basta; para lo que no, la huella del inspector de SAP (`StructureFingerprint`,
también ciega al contenido: pendiente nº6 de `CLAUDE.md`) tiene la misma ceguera desde julio.

### Cómo se ve en el código

| Pieza | Nueva/tocada | Qué |
|---|---|---|
| `Navigation/HuellaDeLoQueSeVe.cs` | **nueva**, pura | `record Huella(Sitio, Delante, Dentro, Ventanas)`; `De(sitio, delante, identidades, ventanas)` ordena; `Comparar(antes, ahora) → (QueCambio { Nada, Dentro, Delante, DeSitio }, Parte)` con esa prioridad —una ventana nueva del proceso es `Dentro` con `Parte = Ventanas`—; `MismaPantallaQueVe(a, b)` = sitio y dentro (el criterio de la 299) |
| `Navigation/EsperaAsentada.cs` | **nueva** | el bucle: `Espera(Func<Huella?> huella, Func<string> sitioFresco, Huella antes, Compas, respiroMs, primeraMs, cadenciaMs) → Veredicto(QueCambio, Parte, MsHastaElVeredicto, PorQueDejoDeEsperar)`; relee `sitioFresco` una vez por respiro y antes de declarar `Asentada` (punto 2b). `PorQueDejoDeEsperar`: `Asentada`, `CambioDeSitio`, `TechoSeMovia`, `TechoNadieMiraba`, `TechoSabeQueLleva`, `TechoNoSePudoMirar(causa)` |
| `Navigation/TechoDeLaEspera.cs` | **nueva**, pura | `Registra(ms, porCondicion)`, `Mediana`, `Techo(minimoMs) = máx(3 × Mediana, minimoMs)`; guarda las últimas 20 |
| `Navigation/HuellaEnVivo.cs` | **nueva** | la huella real: `SitioFresco` (`_dondeTrabajo.Olvida()` + `DondeTrabajo()`), `UiaSurface.VentanaDeDelante()` (ignorando las ventanas de Ü por `Propio.EsVentana`, el mismo camino que `SeguirElFoco`), `HuellaBarata(hwnd)` (el `FindAll` con `CacheRequest` de `RuntimeId` + `ControlType`, aquí y no en `UiaReader`, para no tocar 297/298; cuando exista `Observatorio.Reciente(hwnd)` de la 362, lo consume y no lee) y `UiaSurface.VentanasDelProceso(pid)`. Cada parte deja su coste en ms para la línea de la 355 |
| `Navigation/PulsarSegunElNucleo.cs` | tocada | `Huella` (nulo = nadie mira), `SitioFresco`, `Diario` (nulo = `LogBus`), `RespiroMs`, `PrimeraHuellaMs`, `TechoMinimoMs`, `Techo`; `EsperarACambiar` consume `EsperaAsentada`; `Resultado` gana `QueCambio`, `Parte`, `MsHastaElVeredicto` y `PorQueDejoDeEsperar` con valores por defecto (los `new(...)` de hoy siguen compilando) |
| `Navigation/RecorrerSegunElNucleo.cs` | tocada | `:412-413`: `Huella` local → `HuellaDeLoQueSeVe.MismaPantallaQueVe`; `LlegoDondeTocaba` (`:317-341`) consume la huella con `PresupuestoDeRedireccionMs` (punto 8); `Resultado` lleva `QueCambio` |
| `Navigation/ElTramo.cs` | tocada | `Paso` gana `QueCambio` (por defecto: `Cambio ? DeSitio : Nada`); el detector cuenta `QueCambio == Nada`; `Cuenta` dice qué cambió |
| `Mcp/SurfaceMapTools.cs` | tocada | **`:343`** (`UnPasoDecidido` construye el `Paso` con `cambio = mano?.Logro`: pasa a llevar `mano?.QueCambio`; sin esta línea el detector sigue contando `!Cambio` en el camino del operador —aprendizaje nº11— y la 353 saldría verde solo con manos falsas), **`:2614`** (`record struct Mano` gana `QueCambio`), `:2638-2645` (`Anotar` lo copia del `Resultado` de `Recorrer`), `:2865-2926` (`EsperarPantallaLista` sobre `EsperaAsentada` y `Compas`; `Llego` con `Compas`; `EsperarCambio` **se borra**: 0 llamadores) |
| `windows-graph/src/Surfaces/UiaSurface.cs` | tocada, **2 métodos estáticos añadidos** junto a `VentanaExiste` (`:290`) | `VentanaDelanteAhora() → (Hwnd, Titulo)` y `VentanasDelProceso(hwndDeTrabajo) → hwnds`, los dos por Win32 (hoy 0 usos de `GW_OWNER`/`EnumThreadWindows` en el repo; `GetWindowThreadProcessId` ya se usa en 5 archivos). **Se llama `VentanaDelanteAhora` y no `VentanaDeDelante`** porque `VentanaDeDelante` ya es una CLASE de `windows-graph` (la regla de la 230, `VentanaDeDelante.Elegir`) y el método con ese nombre no compilaba (CS0119, fase 0). Y recibe el hwnd de trabajo, no el pid: quien la llama tiene el hwnd y no el pid, y sacarlo sería un tercer estático. No toca `Walk` ni los `Sleep` de la mano: `Walk` es de la rama C |
| `Ui/FaceWindow.xaml.cs:768-798` | **≤2 líneas** | `pulsar.Huella = new Navigation.HuellaEnVivo(SitioFresco, VentanaObjetivo).Tomar;` y `recorrer.Huella = pulsar.Huella;` — en el hunk de `pulsar`, que ni C ni D tocan (abajo) |

**No se toca:** `EsperarloVivo` salvo las dos líneas de `Huella`; `MapaVivo`; `UiaReader` (297/298);
`SurfaceReadiness`; `AbrirSegunElNucleo`; `PasoDelNucleo`; `Decision/*`; la UI salvo las dos líneas de
arriba; SAP.

**El hunk compartido `SurfaceMapTools.cs:341-343`, dicho.** La rama A declara tocar `:340-343` (la
composición de la línea `decisor:`, spec 046 §fases) y la C `:341-343` (el evento `AlDecidir`, spec 048 fase
7); esta rama cambia un argumento de la misma línea `:343`. Tres ramas en tres líneas rompe la regla de oro
del reparto (`arquitectura-jev-en-u.md` §7: hunks a más de 3 líneas). Resolución: **quien entre primero deja
el `Paso` construido con `QueCambio`** (si es B, A y C rebasan y conservan el argumento; si es A, B rebasa y
lo añade encima de su composición); C entra después de las dos, como ya estaba previsto. Se avisa en
`#miracle-updates` al abrir el PR y se nombra el hunk.

**El choque entre el encargo y la arquitectura, dicho.** El encargo de esta rama dice «NO toques
`Decision/` ni la UI»; `arquitectura-jev-en-u.md` §7 le permite a B `Ui/FaceWindow.xaml.cs:768-798` (≤4
líneas). Sin esas líneas la regla no llega al operador: «nadie inyectó una huella → se espera como hoy», y
las 351–359 quedarían verdes sobre una conducta de producción sin cambiar (un verde que no prueba el camino
del operador, aprendizaje nº11). Esta spec deja el cableado en **≤2 líneas**, en el hunk de `pulsar`
(`:768-798`) que ni C (`:434, 508-565, 5570-5684`) ni D (`~196, 1876-1892, 2869-2896, 4657-4690`) tocan, y
**lo decide el dueño al abrir el PR**: si dice que no, las dos líneas salen a una rama de UI propia, la 355
queda juzgada solo en el contrato, y el PR lo dice con esas palabras. Hoy tocan `FaceWindow.xaml.cs` **19 de
46** refs remotas (M, `git diff --name-only origin/main...rama`, 22-09; la revisión decía 29 y con este método
no se reproduce). Y para que el cableado no se dé por hecho: el nivel 4 de la fase 0 y el de cierre cuentan
las líneas «nadie miraba» de la 355 en el log de la corrida, y tienen que ser **0**.

## Reconciliación con lo que ya promete el contrato

Leído antes de escribir: la 334 (spec 043) y la 299 (spec 040), y de paso la 44, 83, 245, 248, 292 y 296.

- **La 334 choca de frente en su caso 4.** `Contrato.cs:13347-13349` (eran `:13009-13011` antes de las
  promesas de esta spec): «un botón sigue esperando el presupuesto entero», `ms4 >= Presupuesto - 100`, con
  «Guardar» y un `_donde` que nunca cambia — y su arnés **no inyecta huella**. Con la regla del punto 4 («sin
  huella, como hoy»), el fixture **sigue valiendo tal cual** y la promesa nueva 354 añade el caso «con huella
  asentada → menos de la mitad del techo» sobre el mismo «Guardar». Lo que quedaba impreciso era la última
  cláusula del **enunciado**: «y lo que no es un campo espera como siempre». Se reescribió **sin reciclar el
  número** en la fase 3 (2026-09-22). El literal, el mismo que va en `Contrato.cs`:

  > **334.** un campo de texto no navega: al pulsar un Edit o un ComboBox no se espera el presupuesto de un
  > cambio de pantalla —solo una espera corta, por si acaso—, no se consulta el terreno ni se repite el
  > clic, y la respuesta dice que es un campo y que tiene el foco; si aun así la pantalla cambió se cuenta
  > como cualquier navegación; **y lo que no es un campo espera a que lo que se ve se asiente —el
  > presupuesto entero si nadie mira, o en los demás casos en que la 351 espera como hoy—**

  Hasta ese día «como siempre» era el presupuesto entero: 1.797–1.825 ms en 13 de 13 clics que no navegaban
  (log del 21-09, M; va en el comentario de la 334, no en el enunciado, que dice lo que se promete y no su
  historia). **La propuesta de esta spec se quedaba corta**: decía «o el techo si nadie mira», y con huella
  también se espera entero cuando no se pudo tomar la huella de antes, en `sapgui://` y ante una puerta con
  destino (`SabeQueLleva`, 248) —la regla, punto 4, que juzga la 351—. Por eso el literal cita esos casos por
  la promesa que los juzga en vez de enumerarlos: una lista copiada en dos enunciados es una segunda definición
  que se desincroniza (aprendizaje nº16). El **cuerpo** de la 334 no cambió ni un byte: 54 líneas, iguales a
  las de `17560d0`, donde nació (M: md5 `1212778d…` en los dos lados, quitando los CR). Su caso 4 sigue
  exigiendo el presupuesto entero sin huella, y la cláusula nueva, «espera a que lo que se ve se asiente», la
  juzga la 354 sobre el mismo mundo y el mismo «Guardar». Anotado en la spec 043, «Lo que pasó después».
- **La 299 y la spec 040 no cambian.** Es la compuerta de vida (puerta ausente antes de pulsar) y sus casos
  4 («se movió: se espera entero») y 5 («sin poder mirar, nada cambia») siguen en pie. Esta spec **no toca
  `EsperarloVivo`** salvo `:412-413`, para que la huella sea la definición compartida (352); la 299 cita
  como origen y sigue juzgando solo sitio y dentro.
- **La 245 y la 296 siguen:** salir antes solo baja el tiempo; `Compas(int, Func<long>)`,
  `SeAcabo/Transcurrido`, los constructores de `PulsarSegunElNucleo` y `EsperaMaximaMs` se conservan (el
  contrato los instancia por reflexión y por nombre). `EsperaMaximaMs` pasa a ser el **techo mínimo**
  cuando no hay medida; su valor por defecto no cambia.
- **La 248 y la 83 siguen:** la repetición solo se hace con `SabeQueLleva`, y ese caso espera el techo; el
  ensayo del doble sigue solo sobre contenido, y lo único que cambia es que su espera sale al asentarse.
- **La 44** («pulsar y que no se mueva nada NO se cuenta como llegada»): «dentro» y «delante» **no** son
  llegada ni acuñan arista. La 353 lo exige por escrito.
- **La 292 caso 6** («se repitió la misma puerta tres veces»): su fixture construye un
  `RecorrerSegunElNucleo.Resultado` a mano con `Cambio = false` (`Contrato.cs:12204-12209`), así que
  `QueCambio == Nada` por defecto y sigue parando a la tercera. Con «dentro», un clic que abre un desplegable
  ya no es una repetición: la 353 lo exige y lo juzga por el mismo `MapaParaTramo`, con un `Resultado` que
  lleve `QueCambio = Dentro` —ese camino pasa por `Anotar` → `Mano` → `:343` → `Paso`, que es el del operador—.
- **La 044/335 de Jose** (`origin/jose/ir-devuelve-la-pagina-asentada`, «tras navegar se cuenta la página
  asentada, no su esqueleto»): trae una TERCERA definición de «asentada», `ComoSeContesta.InventarioAsentado`
  —dos cabeceras de inventario iguales (dónde y cuántos elementos), 250 ms de pausa, 2 s de tope, por
  `Thread.Sleep`—, en `SurfaceMapTools.Call`, el mismo archivo que esta rama toca. La 352 promete «un solo
  camino» para la compuerta y la espera tras pulsar; el inventario pegado es otro consumidor de la misma
  regla y **queda fuera de la 352 por escrito**: compara el TEXTO de una cabecera, no la pantalla, y la
  comprobación por `U_REPO` de la 352 no lo cuenta. Cuando la rama de Jose entre —antes o después de esta—,
  `InventarioAsentado` consume `HuellaDeLoQueSeVe.Comparar` y `EsperaAsentada` con `Compas` (y el `Sleep` se
  va), con su promesa: es su código y se habla con él **antes del PR**, como manda el reparto. Su 335 choca
  con la 335 de la voz en `main`; lo renumera él.

## Las promesas

Cada enunciado va **literal** a `tests/ContratoDelGrafo/Contrato.cs`, bajo `// ── Spec 047 ──`, con su
cuerpo en su propia región antes de `Debe`. Se juzgan sin pantalla, sin SAP y sin TypeSafe: huella, reloj y
manos inyectados; lo que aún no existe se pide por reflexión y cuenta `Pendiente`. Cada una lleva su
sabotaje de una línea, que se comprueba por diff (memoria del repo: copia, rompe, diff, compila sin
silenciar, veredicto ROTO nombrando esa promesa, restaura, diff idéntico, INTACTO).

| # | Promesa | Fase |
|---|---|---|
| 351 | pulsar no espera a una pantalla que ya se asentó: si la ubicación de trabajo no cambió y dos huellas de lo que se ve, tomadas con un respiro en medio, coinciden, la espera termina en ese instante y no al agotar el techo; el sitio se relee fresco antes de dar la pantalla por asentada, y si cambió manda el cambio de sitio y se aprende, aunque las dos huellas coincidieran; si entre huella y huella algo cambia, la pantalla se está moviendo y se espera hasta el techo; si el terreno ya sabe que esa puerta lleva a algún sitio, o la ubicación es de SAP, o nadie inyectó una huella, se espera como hoy; y en todos los casos queda dicho a los cuántos milisegundos dejó de esperar y por qué | 2 |
| 352 | hay una sola definición de «cambió»: la huella de lo que se ve —el sitio de trabajo, la ventana de delante, lo que se ve dentro y las ventanas del proceso de trabajo— se construye y se compara por un solo camino, y la compuerta de vida y la espera tras pulsar lo usan las dos; dos huellas con las mismas partes son iguales aunque las identidades lleguen en otro orden; una parte distinta las separa y dice cuál, y el sitio manda sobre las otras tres; y la compuerta sigue juzgando solo el sitio y lo de dentro, así que la 299 dice lo mismo que decía | 1 |
| 353 | cambiar de sitio, cambiar por dentro y cambiar la ventana de delante son tres veredictos distintos, y «nada cambió» es el cuarto: el resultado de pulsar dice cuál fue y a los cuántos milisegundos; solo el cambio de sitio acuña una arista, como exige la 44; un cambio por dentro o de delante no aprende nada y la cuenta lo dice con esas palabras; y el detector de bucle del tramo solo cuenta las repeticiones en las que nada cambió —también cuando el paso llega por el mapa, que es el camino del operador—, así que una puerta que abre un menú tres veces no es un bucle y una que no hace nada tres veces sí lo es, como exige la 292 | 4 |
| 354 | un botón que no navega no espera el techo cuando la pantalla se asentó: con una huella que ve lo mismo dos veces, pulsar «Guardar» contesta en menos de la mitad del techo, dice que la pantalla no cambió y no aprende nada; sin huella inyectada espera el techo entero, que es lo que la 334 exige hoy y sigue exigiendo; y un campo de texto sigue con su espera corta y su respuesta de campo | 3 |
| 355 | cada pulsación deja en el log la medida de su espera: la ubicación de trabajo y la ventana de delante antes y después, si lo que se ve dentro cambió y qué parte lo vio, cuánto costó cada parte de la huella, a los cuántos milisegundos se habría dado la pantalla por asentada —o que nunca se asentó—, y a los cuántos cambió la ubicación o que no cambió en el techo; la línea sale aunque la espera no se recorte; y sin huella inyectada dice que nadie miraba | 0 |
| 356 | comprobar la llegada no agota el techo mirando ni declara el desvío en el acto: cuando lo que se ve se asentó en OTRA pantalla —ni la esperada ni la de partida— se sigue mirando durante el presupuesto de redirección, porque una web pasa a menudo por una pantalla intermedia que se asienta y salta; si al agotarlo sigue allí se declara el desvío nombrando las dos, y si en ese plazo llega a la esperada es una llegada; si se asentó en la de partida se sigue esperando hasta el techo, porque una página que aún no empezó a pintarse parece asentada; llegar a la esperada contesta en cuanto la ubicación coincide, como hoy; y sin huella se espera como hoy | 6 |
| 357 | las tres esperas de pulsar —tras el clic, tras el ensayo del doble y tras la repetición— consumen la misma huella y la misma regla: con una huella que se asienta, un elemento de lista sin gesto aprendido se ensaya con el doble y esa segunda espera sale en cuanto se asienta en vez de agotar el techo; la repetición de una puerta con destino sigue esperando el techo las dos veces, porque solo se repite lo que se sabe que navega; y la 83, la 248 y la 296 dicen lo mismo que decían | 5 |
| 358 | esperar a que la pantalla esté lista en las herramientas del mapa gasta del reloj y no cuenta vueltas —con un sondeo lento, una espera de N milisegundos termina en N y no en N por el número de vueltas—, y decide «lista» con la misma huella de lo que se ve, dos iguales con un respiro, en vez de contar botones de la ventana de delante; comprobar la vuelta tras un Enter deshecho gasta del mismo reloj; y la espera de un cambio que nadie llamaba deja de existir | 7 |
| 359 | el techo de una espera sale de la espera normal medida y no de un número a ojo: es el triple de la mediana de lo que tardaron en asentarse o cambiar las últimas esperas que terminaron por condición, nunca menos que el techo mínimo; las esperas que llegaron al techo no lo alimentan; y al agotarlo la cuenta dice cuál de las causas fue: que la pantalla no paró de moverse, que nadie miraba, que el terreno sabía que la puerta lleva a algún sitio, o que no se pudo mirar y por qué | 8 |
| 360 | **reservada**: SAP asentada = `!Busy` en 3 sondeos consecutivos (pendiente nº1 de `CLAUDE.md`, la carrera del Busy) + dos `StructureFingerprint` iguales. **No entra al contrato** hasta una sonda de solo lectura en el hospital (aprendizaje nº13: 0 líneas `sapgui://` en 23 logs de esta máquina). El número no se recicla | — |

La que de verdad cierra el asunto es la **351**: mientras no exista, todo lo demás es contabilidad. La que
lo hace *honesto* es la **355**: sin ella, la 351 entraría sin saber cuántas «asentadas» son falsas.

**Desvío respecto al reparto de `arquitectura-jev-en-u.md` §7, dicho aquí:** allí la 359 estaba reservada
para «`PasoDelNucleo` sin `Sleep(600)`, si Jose cede». Dos correcciones: el encargo exige una promesa para el
techo por paso y el reparto no la tenía, así que el techo toma la 359; y `PasoDelNucleo` **no es de la rama de
Jose** (su diff son `SurfaceMapTools.cs`, `ComoSeContesta.cs`, `Contrato.cs` y su spec 044: 4 archivos, M
22-09) sino el camino de «ir», que pasa a «lo que queda fuera» por alcance, como pendiente con dueño.

### Con qué se juzga cada una

Todas con **mapa a mano** en la propia prueba (como la 334 y la 299): un `Nucleo.Grafo` con tres elementos,
un `_donde` controlado, una mano que cuenta toques, un `Diario` que acumula líneas y una **huella
inyectada** como `Func<Huella?>` que devuelve una secuencia según la llamada (`n => …`). Los presupuestos son
pequeños (techo 1.200, respiro 100, primera 100) para que el contrato corra en segundos, igual que la 299.

- **351** — `PulsarSegunElNucleo.Huella` por reflexión; sin la propiedad, `Pendiente("PulsarSegunElNucleo.Huella
  (la espera mira lo que se ve)", "351", "047")`. Seis casos sobre «Ir» (Button): huella constante →
  `ms < Techo/2` y el diario dice «asentada a los N ms»; huella que cambia en cada llamada → `ms ≥ Techo −
  100` y «no paró de moverse»; grafo con la arista «Ir» → otro sitio (`SabeQueLleva`) y huella constante →
  `≥ Techo − 100` en la primera espera y el diario dice «lleva a algún sitio»; ubicación `sapgui://…` y
  huella constante → `≥ Techo − 100`; sin huella → `≥ Techo − 100` y «nadie miraba»; y **el sitio tardío**:
  huella que se asienta desde los 300 ms (antes cambia en cada llamada) y un `_donde` con memoria falsa que sirve A
  hasta los 700 ms —300 más los 400 de `MemoriaCorta`— y B después (`SitioFresco` inyectado da B desde los 300; con
  primera 100 y respiro 100 las dos huellas coinciden a los ~400, y ahí el sitio fresco ya dice B mientras la memoria
  aún dice A: es justo la ventana de la refutación 1) → `QueCambio == DeSitio`, `Hasta == B`, `Aprendido == true`, y
  ninguna línea «asentada» con veredicto «dentro». **Sabotajes (dos, los dos por diff):** en `EsperaAsentada`,
  la comparación de las dos huellas devuelve siempre «distintas» (cae el caso 1 y ninguna otra promesa); y
  servir el sitio de la memoria en vez de `SitioFresco` al declarar «asentada» (cae solo el caso del sitio
  tardío).
- **352** — `Capacidad("U.WindowsClient.Navigation.HuellaDeLoQueSeVe")`: `De` con las mismas identidades en
  orden distinto → `Comparar == Nada`; solo delante distinta → `Delante`; solo dentro → `Dentro` con `Parte =
  Dentro`; solo ventanas del proceso distintas → `Dentro` con `Parte = Ventanas`; sitio distinto con todo lo
  demás distinto → `DeSitio`; `MismaPantallaQueVe` ignora «delante» y «ventanas». Y que la
  compuerta lo use: el cuerpo lee `RecorrerSegunElNucleo.cs` desde `U_REPO` (el mismo camino que la 341
  de la voz, ya en `main`) y exige que nombre `HuellaDeLoQueSeVe` y no defina ya su `string Huella(`; sin
  `U_REPO` dice «no pude juzgarla» y no cuenta rojo (aprendizaje nº17: «no pude» no es «culpable»).
  **Sabotaje:** invertir la prioridad en `Comparar` (dentro antes que sitio): cae el caso «sitio manda».
- **353** — Pulsar con huella que cambia «dentro» y luego se asienta → `Resultado.QueCambio == Dentro`,
  `CambioLaPantalla == false`, `Aprendido == false`, la cuenta contiene «dentro»; ídem «delante»; `_donde`
  cambia → `DeSitio` y `Aprendido == true` (44); nada → `Nada`. Y el detector por los dos caminos: (i) un
  `ElTramo` con manos falsas: cuatro pasos con el mismo selector y `QueCambio = Dentro` paran por «se agotó el
  tope», no por bucle; tres con `Nada` paran por «bucle»; `ElTramo.Paso` se construye por reflexión con su
  constructor de 12 parámetros (hoy tiene 11, `ElTramo.cs:39-41`); si no existe, `Pendiente`; (ii) **el camino
  del operador**: por `MapaParaTramo` —el arnés de la 292—, con una mano que devuelve un
  `RecorrerSegunElNucleo.Resultado` con `Cambio = false` y `QueCambio = Dentro` cuatro veces → el tramo NO
  para por bucle a la tercera (`Pulsados.Count == 4` y la cuenta sin «tres veces»); ese caso pasa por
  `Anotar` → `Mano` → `SurfaceMapTools.cs:343` → `Paso`. **Sabotajes (dos, por diff):** el detector vuelve a
  contar `!p.Cambio` en vez de `QueCambio == Nada` (cae el caso (i) del desplegable); y `:343` vuelve a
  construir el `Paso` sin `mano?.QueCambio` (cae solo el caso (ii)).
- **354** — El mundo de la 334 («Search», «Rename», «Guardar»), techo 1.200: «Guardar» con huella
  constante → `ms < Techo/2`, `!CambioLaPantalla`, `!Aprendido`, cuenta con «no cambió» y sin «campo»;
  «Guardar» sin huella → `ms ≥ Techo − 100` (el caso 4 de la 334, calcado); «Search» con y sin huella →
  `ms < Techo/2` y cuenta con «campo» y «foco». **Sabotaje:** `if (!esCampo) huella = null;` antes de la
  espera: cae solo el primer caso. (Medido en la fase 3: de la 354 cae solo ese caso, pero **no solo la
  354**: caen también la 351 y la 355, que juzgan la misma espera sobre un `Button`. La local `huella` no
  existe; la línea es `if (!esCampo) Huella = null;`, sobre la propiedad.)
- **355** — `Diario` y `Huella` inyectados; tras `Pulsa` el diario tiene UNA línea que contiene «antes»,
  «después», «delante», «asentada a los» + « ms», «ubicación», la parte que vio el cambio («dentro» /
  «delante» / «ventanas» / «nada») y «coste» con los ms de cada parte; con huella que nunca coincide dice
  «nunca se asentó»; sin huella dice «nadie miraba». En la fase 0 la espera sigue llegando al techo (`ms ≥ Techo −
  100` **también con huella**: se comprueba, y la aserción se retira en la fase 2 con la 351, dejándolo
  dicho en el commit). **Sabotaje:** quitar la línea que anota el instante de la asentada: la línea sale
  sin «asentada a los».
- **356** — `RecorrerSegunElNucleo.Huella` y `PresupuestoDeRedireccionMs` por reflexión (`Pendiente` si
  faltan); en la prueba, presupuesto 400 y techo 1.200. Un paso de escribir con `Llegada = B` desde A:
  `_donde` da C ≠ A ≠ B y huella constante → desvío en `[400, Techo/2)` nombrando B y C (no en el acto: se
  agotó el presupuesto sobre C); **la redirección**: `_donde` da C a los 200 ms con huella constante y B a
  los 700 → llegada, no desvío; `_donde` da A (la de partida) y huella constante → `≥ Techo − 100`; `_donde`
  pasa a B a los 200 ms → llegada en `< Techo/2`; sin huella y `_donde` = C → `≥ Techo − 100`. **Sabotajes
  (dos, por diff):** quitar la condición «ni la de partida» (cae el caso de la partida); y declarar el desvío
  en el acto sin agotar el presupuesto (cae el caso de la redirección).
- **357** — Mundo con un `TreeItem` sin gesto y una `Button` con destino aprendido a otro sitio. TreeItem
  con huella constante y `_donde` fijo → 2 toques (clic y `doubleclick`) y total `< Techo` (dos esperas,
  ninguna al techo); Button con destino → 2 toques (clic y repetición) y total `≥ 2 × Techo − 200`, y el
  diario lo dice dos veces; los fixtures de la 83, 248 y 296 no se tocan. **Sabotaje:** el ensayo del doble
  llama a la espera sin huella: cae el caso del TreeItem. **Añadido en la fase 5** (el porqué, en «Hallazgos»):
  tres aserciones, en rojo antes del código. Las dos esperas del TreeItem dicen «dejó de esperar a los N ms:
  asentada». Si el doble abre un menú, el resultado dice `Dentro` con la huella de la última espera, sin llegada
  ni arista. Y cada una de las dos esperas de la puerta dice «dejó de esperar … lleva a algún sitio». Una cuarta
  aserción se escribió **después** del código, y se dice: las dos esperas de la puerta llevan la medida de la
  huella («sondeo(s) de huella»). Su rojo lo midió un segundo sabotaje, la repetición sin huella.
- **358** — `Capacidad("U.WindowsClient.Navigation.EsperaAsentada")`: huella lenta (400 ms de `Sleep`)
  con techo 1.200 → termina en `< 1.200 + 3 × 400` (la forma de la 245); huella constante → `< Techo/2`
  con `Asentada`; huella cambiante → techo con `TechoSeMovia`. Y por `U_REPO`, `SurfaceMapTools.cs` no
  contiene `EsperarCambio(` ni `CuantosAccionables(` —los tres `FindAll(Descendants)` sobre el primer plano
  se sustituyen por la huella barata de la ventana de trabajo— y `EsperarPantallaLista` nombra
  `EsperaAsentada` (mismo camino que la 341 de la voz; sin `U_REPO`, «no pude juzgarla»). **Sabotaje:** el bucle de `EsperaAsentada` vuelve a `for (i <
  techo / cadencia)`: cae el primer caso.
- **359** — `Capacidad("U.WindowsClient.Navigation.TechoDeLaEspera")`: cinco `Registra(300, true)` →
  `Techo(1800) == 1800`; cinco `Registra(900, true)` → 2.700; cinco `Registra(5000, false)` no mueven la
  mediana. Y las cuatro causas en la cuenta de `Pulsa` al agotar el techo: huella cambiante → «no paró de
  moverse»; sin huella → «nadie miraba»; `SabeQueLleva` → «lleva a algún sitio»; huella que lanza → «no
  pude mirar» + el mensaje de la excepción (patrón nº3). **Sabotaje:** `Techo` devuelve `minimoMs` a
  secas: cae el caso de 2.700.

## Las fases

Una fase = un commit que pone verde UNA promesa sin romper las anteriores. Toda la spec vive en esta rama;
no se mergea fase a fase. El orden es el de dependencia, no el de los números: la medida primero (0), la
definición compartida (1), la regla (2), su reconciliación con la 334 (3), el veredicto en cuatro (4), las
otras dos esperas (5), la llegada (6), las herramientas del mapa (7) y el techo (8).

| Fase | Qué | Promesa | Toca | Terminado |
|---|---|---|---|---|
| 0 | la medida en el log, sin recortar nada | **355** | nuevas `HuellaDeLoQueSeVe`, `EsperaAsentada` (en sombra), `HuellaEnVivo` (sitio fresco, huella barata, ventanas del proceso); `UiaSurface.VentanaDeDelante` y `VentanasDelProceso` (+2 estáticos); `PulsarSegunElNucleo` (`Huella`, `SitioFresco`, `Diario`, la línea); `FaceWindow` ≤2 líneas (el choque con el encargo, dicho arriba; lo decide el dueño) | 355 verde; 245, 248, 296, 334 intactas; **nivel 4 medido** (abajo), con **0** líneas «nadie miraba» |
| 1 | una sola definición de «cambió» | **352** | `RecorrerSegunElNucleo.cs:412-413`; `HuellaDeLoQueSeVe.MismaPantallaQueVe` | 352 verde; 299 intacta |
| 2 | la espera sale al asentarse | **351** | `PulsarSegunElNucleo.EsperarACambiar` (el sitio del clic, `:162`), `RespiroMs`, `PrimeraHuellaMs` con los valores de la fase 0 | 351 verde; la aserción «también con huella llega al techo» de la 355 se retira en este commit y se dice |
| 3 | la 334 reescrita, sin reciclar | **354** | `Contrato.cs` (enunciado de la 334 + caso nuevo), `docs/specs/043` («lo que pasó después») | 354 verde; el fixture viejo de la 334 sigue byte a byte |
| 4 | el veredicto en cuatro | **353** | `PulsarSegunElNucleo.Resultado`, `RecorrerSegunElNucleo.Resultado`, `SurfaceMapTools.cs:2614` (`Mano`), `:2638-2645` (`Anotar`), **`:343`** (el `Paso` del mapa lleva `mano?.QueCambio`; hunk compartido con A y C, orden acordado arriba), `ElTramo.Paso`/detector/`Cuenta` | 353 verde por los dos caminos; 44 y 292 intactas |
| 5 | las otras dos esperas de `Pulsa` | **357** | `PulsarSegunElNucleo.cs:201` y `:225` | 357 verde; 83, 248, 296 intactas; **3 sitios** contados en el commit |
| 6 | la llegada no agota el techo ni declara en el acto | **356** | `RecorrerSegunElNucleo.LlegoDondeTocaba`, `Huella` y `PresupuestoDeRedireccionMs` en `Recorrer` (hasta la medida (d), = techo), `FaceWindow` (dentro de las ≤2 líneas de la fase 0: `recorrer.Huella = pulsar.Huella`) | 356 verde; 103 intacta |
| 7 | las herramientas del mapa gastan del reloj y miran la huella | **358** | `SurfaceMapTools.cs:2865-2926`: `EsperarPantallaLista` → `EsperaAsentada`+`Compas`; `Llego` → `Compas`; `EsperarCambio` borrado | 358 verde; **3 sitios, 1 borrado**, en el commit |
| 8 | el techo sale de la medida y dice su causa | **359** | nueva `TechoDeLaEspera`; `PulsarSegunElNucleo` (`Techo`, `TechoMinimoMs`, las cuatro causas) | 359 verde; 245 intacta |

### Fase 0 — la medida, y solo la medida

| | |
|---|---|
| **Promesa** | 355 |
| **Qué toca** | ver tabla; `EsperarACambiar` sigue esperando la ubicación hasta el techo, y **además** corre `EsperaAsentada` en sombra para anotar a los cuántos ms se habría asentado |
| **Coste que añade** | una huella por sondeo: las tres partes Win32 cada vuelta (<1 ms, D), la huella barata de dentro como mucho cada 250 ms y solo si las baratas no cambiaron (**sin medir**: es un `FindAll` con `CacheRequest`; no es la lectura completa de 144–420 ms), y un `DondeTrabajo` fresco por respiro (**sin medir**). El compás manda: la espera sigue acotada por el techo; el número de sondeos por espera baja de 13–18 a ~6–10 (D). Cada parte deja su coste en la línea de la 355, y se pega en «Lo que se midió» con (M) antes de la fase 2. Mientras tanto el `MapaVivo` sigue leyendo cada 900 ms sobre la misma ventana: se cuenta, no se arregla aquí (rama C) |
| **¿Núcleo congelado?** | no |
| **Nivel 4 obligatorio antes de la fase 2** | ChatGPT.exe y Gmail en Chrome (las dos pantallas del 21-09) + Explorador «Descargas» (su lectura está sin medir con el lector actual), Jev apagado, **≥30 clics que no navegan y ≥10 que sí**, de ellos ≥3 con redirección (`map_go_to docs.google.com`); de la línea nueva se cuentan: (a) «no cambió» falsos —dentro, delante y ventanas del proceso, **por separado**, y para los popups (el menú de perfiles de Chrome, «Seleccionar modelo» de ChatGPT) cuál de las partes los vio—, (b) **«asentada» falsas**: clics que navegaron y cuya huella coincidió ANTES de cambiar de sitio, con el instante de cada cosa y cuántas las atrapó la relectura fresca del sitio (2b), (c) el **coste** por parte y por sondeo, con el Explorador aparte, y el de un `DondeTrabajo` fresco, (d) «ms entre dos cambios de sitio seguidos» en las navegaciones con redirección (de ahí `PresupuestoDeRedireccionMs`), y (e) las líneas «nadie miraba»: **0**, o la huella no llegó a inyectarse y la medida no vale. De (b) salen `PrimeraHuellaMs` y `RespiroMs`; de la distribución de «ms hasta cambio de sitio», el techo mínimo (p95 + 300). Se pega el log (`u-AAAAMMDD-<instancia>.log`) en el PR con horas |
| **Terminado** | 355 verde; 245, 248, 296, 334 intactas; la medida (a)–(e) escrita en «Hallazgos» y en «Lo que se midió» con (M) |

### Fases 1–8

Cada una: la promesa ROJA primero (`Pendiente` o `Debe` en rojo), el código hasta INTACTO, el sabotaje
verificado por diff, y el commit citando la promesa y la medida («3 sitios», «0 llamadores»). Ninguna toca
`Decision/*`, SAP, ni la UI más allá de las dos líneas de la fase 0 (y solo si el dueño las acepta). La 2 no
entra sin el nivel 4 de la 0: es la condición que la 043 dejó escrita; y la 6 no recorta nada hasta que (d)
dé el presupuesto de redirección.

## Lo que queda fuera

- **SAP** (360): «asentada» allí es `!Busy` ×3 + `StructureFingerprint` ×2, y cuesta un recorrido COM. No
  entra sin la sonda del hospital; hasta entonces `sapgui://` espera como hoy, por diseño (regla, punto 4).
- **`PasoDelNucleo`** (3 esperas por ubicación en `:117-135`, `:164-172`, `:192-194`, y el `Sleep(600)` de
  `:231`): es el camino de «ir» (`map_go_to`), no el de pulsar, y por eso queda fuera **por alcance**. No es
  de la rama de Jose —su diff no lo toca—: son 3 + 1 sitios de esta clase **pendientes con dueño**, y el dueño
  hoy no existe; entran en una rama propia con número nuevo, consumiendo `EsperaAsentada`.
- **`ComoSeContesta.InventarioAsentado`** (rama de Jose, 044/335): es su código; la reconciliación de arriba
  dice cómo converge. Aquí no se toca.
- **`AbrirSegunElNucleo`** (`:282-293`, `Sleep(120)`) y **`SurfaceReadiness`** (`windows-graph`): la misma
  clase de error, fuera del ciclo del tramo. Anotados; otra rama.
- **Los `Sleep` de la mano** en `UiaSurface` (27 sitios; ~160 ms por clic identificados de 333, D): mejora 3
  del informe. Se miden antes de tocarlos, y `Walk` es de la rama C.
- **La memoria de 400 ms de `_donde`** (`MemoriaCorta`, mejora 6) **sí afecta a esta spec**, y no solo al
  ahorro: afecta a la corrección del veredicto (44, `Cruzar`), y por eso el sitio de la huella no sale de
  ella (regla, punto 2b). Lo que queda fuera es cambiar la memoria misma —su caducidad, o invalidarla por
  WinEvent—: otra rama, con su promesa.
- **Esperar con WinEvents** en vez de sondear (mejora 12): fase 2 del informe, después de medir la 351.
- Los otros tres `Sleep` de `SurfaceMapTools` (`:2200`, `:2340`, `:2729`): herramientas distintas de la
  espera tras pulsar. Anotados.
- **Que la huella vea el contenido** (un valor que cambia sin cambiar los selectores): la 360 y el
  pendiente nº6 de `CLAUDE.md` tienen la misma ceguera. Se dice; no se arregla aquí.
- **La UI** (el panel de Jev lee `Paso.QueCambio`, `Parte` y `MsHastaElVeredicto`: rama D) y **`Decision/*`**
  (A). Las dos líneas de `FaceWindow` no son «la UI»: son el cableado, y están dichas arriba con su choque.

## Nivel 4 pendiente

- El de la fase 0 (arriba), **antes** de la fase 2. Tres pantallas con nombre; las cuentas (a)–(e).
- Al cerrar: las mismas tres pantallas con la rama entera; «⏱ pulsar … esperar el cambio» antes y después
  sobre los mismos botones («Nuevo chat», «Minimizar», «Seleccionar modelo», «Documents», y el menú de
  perfiles de Chrome, contado con la parte que lo vio); un clic que navega («Discusión» en Wikipedia,
  «Descargas» en el Explorador) para ver que `Cruzar` sigue aprendiendo y que ninguna «asentada» se declara
  antes del cambio de sitio (351, 2b); una navegación con redirección (`docs.google.com`) que llega sin
  desvío (356); un `map_type` con Enter en el Explorador (358); un tramo con Jev **apagado**
  (`U_DECISOR=simulado`) que pulse tres veces un botón que abre un menú, para ver que no para por bucle
  (353, por el camino del operador); y el conteo de líneas «nadie miraba» de la 355 en ese log, pegado en el
  PR: **0**.
- Lo que este nivel 4 **no** puede cubrir y se dice: SAP (0 líneas `sapgui://` en 23 logs de esta máquina)
  y un monitor de otra escala.

## Hallazgos

- **2026-09-22.** `SurfaceMapTools.EsperarCambio` (`:2902-2913`) no tiene **ningún llamador** (`grep` en
  `windows-client/src`: solo su definición). Contaba vueltas y no reloj, y era uno de los tres sitios que
  la 040 dejó anotados. Patrón nº6: se borra, no se arregla (358).
- **2026-09-22.** `EsperarPantallaLista` (`:2865-2876`) cuenta `Button`/`ListItem`/`MenuItem` de la ventana
  **de delante** (`GetForegroundWindow`), no de la de trabajo: la misma clase que «la compuerta mira el
  primer plano» de `windows-client/CLAUDE.md`. Con la persona en otra ventana, «lista» juzga la de ella.
- **2026-09-22.** Las «esperas fijas» del encargo no viven en `ComoSePulsa` ni en `VentanaDeTrabajo` (0 y 0):
  la premisa era falsa y se dice. Viven en el techo de `PulsarSegunElNucleo` y en los tres bucles de
  `SurfaceMapTools`.
- **2026-09-22.** `main` ya tiene la 341, la 342 y la 343 (voz y log; `f811796` y `5574148`, las dos de hoy).
  El «341–385 libres» de `arquitectura-jev-en-u.md` §6 quedó viejo la misma mañana; la rama A renumeró a
  342–350 y vuelve a chocar en 342 y 343. Esta rama (351–359) no choca. Y con la 343 el log pasa a ser por
  instancia: `u-AAAAMMDD-<instancia>.log`.
- **2026-09-22, fase 0 escrita (355 verde).** Lo que entró y lo que no:
  - **Contrato**: `CONTRATO ROTO: 18 promesa(s) incumplida(s)` —el 18 cuenta `Debe` fallidos, no promesas—:
    282 ✔ / 8 ✘, y las 8 son 351–354 y 356–359 (M, `contrato-del-grafo.ps1`, tres corridas: base con las
    nueve en PENDIENTE = 9; fase 0 = 18; sabotaje de la 355 = 21). 355, 44, 83, 245, 248, 292, 296, 299 y 334
    en ✔. Las 351, 353, 354 y 357 pasan de PENDIENTE a rojas de verdad: ya existe `PulsarSegunElNucleo.Huella`
    y sus casos corren, pero la espera sigue llegando al techo (fase 0, sin recortar), que es lo que tienen que
    decir hasta la fase 2.
  - **Sabotaje de la 355, verificado por diff**: quitar `MsAsentada = t;` en `EsperaAsentada.Sondea` → la línea
    sale «nunca se asentó (se movió en 0 de 10 sondeo(s))» sin «asentada a los» → `✘ 355.` y `CONTRATO ROTO: 21`.
    Restaurado con diff vacío → `✔ 355.` y 18 otra vez. **Trampa encontrada al restaurar**: `Copy-Item` conserva
    la fecha del `.bak`, más vieja que el binario saboteado, y el build incremental **se quedó con la DLL
    saboteada**: el diff era vacío y el juez seguía diciendo 355 roja. Un sabotaje «restaurado» con diff vacío y
    veredicto rojo es la misma clase que el CRLF del 2026-08-21 (memoria `sabotaje-verificado-por-diff`): la
    comprobación no es el diff solo, es **diff vacío Y veredicto de vuelta al de antes**, y si no vuelve, tocar la
    fecha (`LastWriteTime = Get-Date`) antes de dudar del código.
  - **Sitios**: «cambió» se sigue calculando en los 11 sitios de `arquitectura-jev-en-u.md` §3.2; la fase 0 no
    recorta ninguno: añade la sombra en **1** (`EsperarACambiar` del clic, `:162`) y la línea de la 355 en ese
    mismo sitio. Las líneas «mano» de `PulsarSegunElNucleo` pasan por un solo `Anota` (**4** sitios: el ⏱, el
    terreno, «lleva aquí» y «lo repito») para que el `Diario` inyectado las vea; la 357 las contará.
  - **El diario y la ubicación del clic**: la línea de la 355 dice «cambió a los N ms» cuando la ubicación
    cambió, con el instante que anota `EsperarACambiar` (`_msCambioUbicacion`); y sin huella dice «nadie miraba»
    dos veces (delante y asentada), para que el conteo (e) del nivel 4 no dependa de una sola palabra.
  - **Las dos líneas de `FaceWindow`** (`:791-795`, hunk de `pulsar`) están puestas: `HuellaEnVivo` con el sitio
    sin memoria (`_dondeTrabajo.Olvida()` + `DondeTrabajo()`) y `VentanaObjetivo`. **Lo decide el dueño al abrir el
    PR**, como está escrito arriba; si dice que no, salen a una rama de UI propia y la 355 queda juzgada solo en el
    contrato.
  - **Nivel 4: NO medido.** El encargo de esta rama prohíbe ejecutar `U.exe`, y el nivel 4 de la fase 0 es
    exactamente eso: tres pantallas con nombre, ≥30 clics que no navegan, ≥10 que sí, ≥3 con redirección, y las
    cuentas (a)–(e) leídas de las líneas `👀 tras «…»` del log por instancia. Queda **para el dueño, antes de la
    fase 2**: es la condición que la 043 dejó escrita y esta rama no la puede saltar. Lo que la línea trae para
    esas cuentas: (a) «cambió dentro (lo vio: dentro | ventanas del proceso)» / «cambió delante» / «nada cambió»
    junto a «no cambió en el techo»; (b) «asentada a los N ms» con «sitio fresco cambió a los M ms» y «se movió K
    vez/veces DESPUÉS: asentada falsa»; (c) «coste por sondeo (media/máx ms): sitio · delante · dentro · ventanas»
    y «sitio fresco a/b ms × n», más «huella de antes N ms»; (d) los «cambió a los N ms» de las navegaciones con
    redirección (`map_go_to` no pasa por aquí: (d) se mide con la 356, en `Recorrer`); (e) `grep "nadie miraba"`
    sobre el log de la corrida: tiene que dar **0**. Con eso se fijan `PrimeraHuellaMs` (hoy 400, el de la
    compuerta) y `RespiroMs` (hoy 250, jkudish), que hasta entonces son **metas, no datos**.
  - **Lo que la huella barata lee** (`HuellaEnVivo.HuellaBarata`): un `FindAll(Descendants)` con `CacheRequest`
    de `RuntimeId` + `ControlType` en modo `None` sobre 14 tipos (Button, ListItem, MenuItem, TreeItem, TabItem,
    Hyperlink, Edit, ComboBox, CheckBox, RadioButton, SplitButton, DataItem, Menu, Window). **Coste sin medir**
    (D: una llamada entre procesos). Lo de dentro se paga como mucho cada respiro y solo si delante y ventanas no
    cambiaron; el sitio fresco, como mucho cada respiro. Un `hwnd` de trabajo a cero o una raíz UIA nula **lanzan**
    con la causa, y la espera lo anota como «no pude mirar: …» y sigue como hoy: aquí un catch mudo convertiría
    «la ventana se cerró» en «no cambió nada» (patrón nº3).
- **2026-09-22, fase 1 escrita (352 verde).** Lo que entró y lo que no:
  - **Qué cambió**: un archivo de producción, `RecorrerSegunElNucleo.cs` (la compuerta de la 299). El `string
    Huella(` local —texto «dónde + selectores unidos por |»— se va; la huella se construye por
    `HuellaDeLoQueSeVe.De` (sitio + selectores de las puertas vivas; delante y ventanas vacías, que la compuerta no
    mira) y se compara por `MismaPantallaQueVe`. `HuellaDeLoQueSeVe.cs` **no se toca**: `Comparar` y
    `MismaPantallaQueVe` existían desde la fase 0 con la prioridad de la spec (el sitio manda).
  - **Sitios** (M, `grep` del 22-09): definiciones propias de una huella de pantalla fuera de
    `HuellaDeLoQueSeVe`: **1** (`:412`), con **2** usos (`:478` y `:488`); quedan **0**. Los otros dos
    consumidores ya pasaban por ella (`EsperaAsentada` `:94`/`:135`, `PulsarSegunElNucleo` `:413`).
    `Teach/CamaraDeCuadros.Huella` es un hash de imagen de 16×16 y no juzga si la pantalla cambió: fuera. Los 11
    sitios de «cambió por ubicación o recuento» siguen siendo 11: la compuerta no era uno de ellos.
  - **La 299 dice lo mismo** (M: `✔ 299.` antes y después). Dos diferencias de borde, leídas en el código: `De`
    descarta identidades vacías, pero el grafo ya no guarda ninguna (`Grafo.cs:134` y `:263` saltan el selector en
    blanco), así que ahí no cambia nada; y la lista ya no puede confundir dos conjuntos cuya unión con «|»
    coincidiera —el selector es `uia:name={Label};ct=…` y una etiqueta es texto de pantalla, que puede llevar
    «|»—. Esa solo puede **quitar** «asentadas» falsas, no añadirlas (D: no se ha visto ninguna).
  - **Contrato** (M, `contrato-del-grafo.ps1` con `TEMP` propio): antes `CONTRATO ROTO: 18 promesa(s)
    incumplida(s)` (299 ✔ / 8 ✘: 351–354, 356–359); después `CONTRATO ROTO: 17 promesa(s) incumplida(s)` (300 ✔
    / 7 ✘: 351, 353, 354, 356–359, las de fases posteriores). La única línea de veredicto que cambia es `✘ 352.` →
    `✔ 352.`. Voz: `VOZ ÍNTEGRA: el collar promete lo que dice prometer.` (46 ✔).
  - **Sabotaje de la 352, el de la spec, verificado por diff**: en `Comparar`, la línea de «dentro» encima de la
    de «sitio» (diff 1+/1−; el patrón se buscó con `\r\n` y coincidió 1 vez) → `✘ el sitio manda sobre las otras
    tres: (Dentro, Dentro)` y `CONTRATO ROTO: 18`, con la 352 como única promesa nueva en rojo. Restaurado desde la
    copia (hash idéntico, diff vacío, fecha tocada para que el build incremental no se quede con la DLL rota:
    la trampa de la fase 0) → `CONTRATO ROTO: 17`, línea a línea igual que antes del sabotaje.
  - **Sabotaje extra, sobre la línea nueva**: la comprobación de la 352 sobre la compuerta lee el código, así que
    es textual. Para ver que la 299 pasa de verdad por `MismaPantallaQueVe`, se negó esa comparación →
    `✘ 299.` (6 aserciones, `CONTRATO ROTO: 23`) y ninguna otra; restaurado (hash idéntico) → 17.
  - **El parche a medias del workflow cortado** reordenaba `Comparar` poniendo «dentro» antes que «sitio»: es
    exactamente el sabotaje que esta spec prescribe para la 352, y contradice «el sitio manda sobre las otras
    tres». No se aplicó; de él se tomó solo la parte de la compuerta.
  - **Hallazgo del arnés, no arreglado aquí**: `contrato-del-grafo.ps1` compila a `%TEMP%\u-contrato` y
    `contrato-de-la-voz.ps1` a `%TEMP%\u-contrato-voz`, carpetas **compartidas por todos los worktrees** de la
    máquina. Con las cuatro ramas de Jev juzgándose a la vez, un juez puede cargar los binarios de otra rama y
    dar el veredicto de otro código (D, no observado). Esta fase corrió con `TEMP` apuntando a una carpeta propia.
    Los scripts quedan fuera del alcance de la 047: anotado para una rama `chore/` con su promesa.
- **2026-09-22, antes de la fase 2: dos «todavía no» que eran «nunca».** La corrida de la fase 1 decía, para una
  huella que nunca se asentaba, `sitio fresco: 0 veces` en 1.200 ms con respiro 100 (M, `grafo-fase1.txt`): la
  relectura del sitio «una vez por respiro» de la regla 2b no ocurría. La causa es aritmética: el «aún no se hizo»
  se guardaba como `long.MinValue` y se preguntaba `t - _t >= respiro`, y esa resta **desborda a negativo** (sonda
  de solo lectura del 22-09 con `Add-Type`: `ahora - long.MinValue = -9.223.372.036.479.354.230`, «≥ 250» → falso).
  - **Sitios de la clase** (M, `grep MinValue` en `windows-client/src`, `windows-graph/src` y `nucleo`): **4**
    marcas `long.MinValue` de «nunca»; **2** con guarda (`HuellaEnVivo._tDentro`, que pregunta `== long.MinValue`,
    y `MemoriaCorta._cuando`, que va detrás de `_hay`) y **2 sin guarda, las dos de la fase 0 de esta rama**:
    `EsperaAsentada._tFresco` (la relectura periódica no ocurría hasta la primera asentada) y `HuellaEnVivo._tSitio`
    (la **primera** huella de cada sesión salía con el sitio vacío; la segunda ya lo traía, así que la línea de la
    355 de la primera pulsación diría «cambió de sitio (lo vio: sitio, «» → «…»)» y «sitio fresco cambió» sin que
    nada cambiara, y con la regla de la 351 sería un veredicto falso). Las dos pasan a `long?` (nulo = aún no), y la
    clase queda en **0** sin guarda. Los `DateTime.MinValue` no son esta clase: su resta no desborda.
  - **La 355 lo juzga ahora** (dos aserciones nuevas, rojas antes del arreglo: `CONTRATO ROTO: 19`, la 355 como
    única promesa nueva en rojo, con «0 relectura(s)» y «0 lectura(s)»): con una huella que no se asienta la línea
    cuenta ≥5 relecturas del sitio fresco en 1.200 ms; y la primera toma de `HuellaEnVivo` lee el sitio (se juzga
    sin pantalla: sin ventana de trabajo `Tomar` lanza, y se mira si antes leyó el sitio; si lanza por otra causa
    el arnés dice «no pude juzgarla»). Tras el arreglo `CONTRATO ROTO: 17`, veredicto a veredicto igual que la fase 1.
    Dos sabotajes por diff, uno por línea, cada uno pone roja solo su aserción (`CONTRATO ROTO: 18`); restaurados
    con hash idéntico → 17. Voz: `VOZ ÍNTEGRA` (46 ✔).
  - **Para el nivel 4 de la fase 0**: se corre sobre **este commit** (`3d22018`), no sobre la fase 2. Sin este
    arreglo la primera pulsación de cada sesión mediría un cambio de sitio falso y la relectura del sitio no se mediría
    en las pantallas que no se asientan; y con la fase 2 la espera deja de mirar al asentarse, así que ya no puede
    contar las «asentadas falsas» de la cuenta (b).
- **2026-09-22, fase 2 escrita (351 verde) SIN su condición previa.** El nivel 4 de la fase 0 **no está medido**: en
  `%LOCALAPPDATA%\U\logs` de esta máquina hay 0 líneas «asentada a los» o «nadie miraba» y ningún log por instancia (M,
  `grep` del 22-09), y esta rama no ejecuta `U.exe`. Así que la fase entra **como `wip`**: escrita y juzgada en el
  contrato, y **no llega a `main` hasta que el dueño corra el nivel 4 sobre `3d22018`** y de él salgan
  `PrimeraHuellaMs` y `RespiroMs`. Hoy valen **400 y 250, metas y no datos**. Y el riesgo queda dicho (D): la mediana
  del cambio de sitio tras un clic que sí navega fue 405–510 ms el 18-09, y hasta que cambia la página vieja sigue
  quieta. Con 400, una navegación más lenta puede darse por asentada ANTES de cambiar de sitio: la relectura fresca solo
  la salva si el sitio ya cambió. Sobre una fila de lista eso sería peor, porque el ensayo del doble caería sobre la
  página nueva y aprendería el gesto equivocado. Es la cuenta (b), y es la razón de la condición.
  - **Qué cambió**: un archivo de producción, `PulsarSegunElNucleo.cs`, más una línea de comentario en `EsperaAsentada`.
    La primera espera de `Pulsa` (la del clic) **decide con la huella**: sale en el instante en que `Sondea` dice
    `Asentada`, o sale con el sitio fresco si dice `CambioDeSitio` y ese sitio no es el de partida (entonces `Pulsa`
    aprende la puerta). Se espera **como hoy**, con la huella en sombra, en los casos de la regla 4: nadie mira, no se
    pudo tomar la huella de antes, un campo de texto (su espera corta de la 334), `sapgui://` (por `Mundos.EsSap`, el
    sitio único de «de qué mundo es una URL») y `SabeQueLleva`. `Resultado` gana `QueCambio` (`init`, por defecto
    `Nada`; los `new(...)` de siempre compilan igual). El sitio lo decide la ubicación de trabajo, por el mismo camino
    con el que `Pulsa` decide aprender (nº16), y lo demás lo decide la huella. La línea de la 355 termina con «dejó de
    esperar a los N ms: <causa>», y cada causa tiene sus propias palabras (patrón nº2): cambió de sitio (y qué lo vio),
    asentada, «llegó al techo, como hoy: <cuál de los cinco casos>», no pude mirar, no paró de moverse, el sitio fresco
    no cuadra con la ubicación, o el techo no dio para juzgarla.
  - **Sitios** (M, `grep`): `EsperarACambiar` tiene **3** llamadas en `Pulsa`. Esta fase cambia **1**, la del clic
    (`:239`). Las del ensayo del doble (`:280`) y la repetición (`:304`) siguen al techo: son la fase 5 (357). De los 11
    sitios que calculan «cambió» por ubicación o recuento quedan **10** así; el del clic mira primero la ubicación y
    después la huella.
  - **Contrato** (M, con `TEMP` propio): antes, con las aserciones nuevas de la 351 y sin código, `CONTRATO ROTO: 23`
    (300 ✔ / 7 ✘). La 351 estaba roja por sus tres casos de siempre y por seis nuevos: su última cláusula, «en todos los
    casos queda dicho a los cuántos milisegundos dejó de esperar y por qué», no la juzgaba ningún caso de SAP ni del
    sitio tardío, y ahora se exige en los seis. Después, `CONTRATO ROTO: 10` (302 ✔ / 5 ✘: 353, 356, 357, 358, 359,
    todas de fases posteriores). Solo cambian dos veredictos: `✘ 351.` → `✔ 351.` y **`✘ 354.` → `✔ 354.`**. La 354 se
    pone verde **aquí y no en la fase 3**: su caso nuevo («Guardar» con huella asentada contesta en menos de la mitad
    del techo) es esta misma regla sobre un botón. A la fase 3 le queda reescribir el enunciado de la 334 y su sabotaje
    propio, que no puede separarse de la 351 porque las dos juzgan la misma línea. La 355 pierde la aserción «también
    con huella llega al techo», retirada como se dijo en la fase 0. 44, 83, 245, 248, 292, 296, 299, 334, 352 y 355 en
    ✔. Voz: `VOZ ÍNTEGRA` (46 ✔). 0 errores; los mismos 60 warnings.
  - **Sabotajes de la 351, los dos de la spec, verificados por diff** (1+/1− cada uno, restaurados con hash idéntico,
    y al volver `CONTRATO ROTO: 10` veredicto a veredicto):
    - (a) En `EsperaAsentada.Sondea`, la comparación de las dos huellas pasa a decir siempre «distintas»
      (`!Iguales(_referencia, null)`). Resultado: `CONTRATO ROTO: 16`. Caen el caso 1 de la 351 y su «asentada», y
      **también la 354 y la 355**. La spec decía «ninguna otra promesa», y lo medido la corrige: las tres juzgan la
      misma `EsperaAsentada`, que es justo lo que pide la 352 (una sola definición).
    - (b) Servir el sitio de la memoria en vez de `SitioFresco` (`new EsperaAsentada(Huella, _donde, …)`). Resultado:
      `CONTRATO ROTO: 13`, **solo la 351** y solo el sitio tardío: «Dentro, hasta «web://x/a»», una línea «asentada /
      dentro» y ningún «cambió de sitio». Es la refutación 1, reproducida.
  - **Lo que deja hecho para otras fases, dicho para que no se lea como suyo**: la 353 ya ve `QueCambio` correcto en
    sus cuatro casos. Le faltan la cuenta con «dentro»/«delante», `MsHastaElVeredicto` y `ElTramo` (fase 4). La 357
    ya cumple «el diario lo dice las dos veces», porque la línea de la primera espera dice «lleva a algún sitio» y la
    de la repetición también. Si la fase 5 hace que la espera de la repetición escriba su propia línea, serán tres, y
    esa aserción (`== 2`) tendrá que decidir qué cuenta.
- **2026-09-22, fase 3 escrita (la 334 reescrita; la 354 sigue verde).** Esta fase no tiene promesa que pase de
  roja a verde, y se dice: la 354 se puso verde en la fase 2 (`fd008c3`), porque su caso nuevo es la misma regla.
  Lo que la fase entrega es el registro y el sabotaje propio de la 354.
  - **Qué cambió**: **0 líneas de producción**. En `Contrato.cs`, solo el texto del `Prueba(...)` de la 334 (+9/−1:
    el enunciado y un comentario de 8 líneas con el porqué y la medida). El literal está en «Reconciliación», arriba,
    y es el mismo en la spec 043, «Lo que pasó después» (M: comparado con un script que une las líneas de la cita y las contrasta con el `Prueba(...)`).
  - **El fixture viejo de la 334 sigue byte a byte** (M): el cuerpo de `UnCampoDeTextoNoNavega`, 54 líneas, da el
    mismo md5 (`1212778d…`) aquí y en `17560d0`, donde nació, quitando los CR. **La 299 y la 040 sin tocar** (M): el
    cuerpo de `UnaPantallaAsentadaNoSeEspera` (95 líneas) y su enunciado son idénticos a los de `origin/main`, y
    `docs/specs/040*` no tiene diff contra `origin/main`.
  - **Contrato** (M, con `TEMP` propio): antes y después, `CONTRATO ROTO: 10 promesa(s) incumplida(s)` (302 ✔ /
    5 ✘: 353, 356, 357, 358 y 359, de fases posteriores); 307 veredictos, iguales uno a uno. 334, 351, 354, 355 y 299
    en ✔.
  - **Sabotaje de la 354, el de la spec, verificado por diff.** La spec dice `if (!esCampo) huella = null;`, y la local
    `huella` no existe: la línea es `if (!esCampo) Huella = null;`, sobre la propiedad, después de decidir el
    presupuesto y antes de construir la espera (`PulsarSegunElNucleo.cs:233`). Diff 1+/0−; el ancla se buscó con
    `\r\n` y coincidió 1 vez; compiló (0 errores, 36 warnings) → **`CONTRATO ROTO: 21`**. De la 354 cae **solo el
    primer caso** («Guardar» con la pantalla asentada: 1.205 ms de 1.200); siguen en pie «dice que no cambió, sin
    llamarle campo», «sin huella, el techo entero» y los dos campos. Pero **no cae solo la 354**: caen también la 351
    (7 aserciones) y la 355 (2), y una aserción más de la 357, que ya estaba roja. Es lo mismo que midió el sabotaje (a)
    de la fase 2, y por la misma razón: las tres juzgan la misma espera sobre un `Button`, que es lo que pide la 352.
    Restaurado desde la copia (md5 idéntico, diff vacío, fecha tocada para que el build incremental no se quede con la
    DLL rota) → `CONTRATO ROTO: 10`, veredicto a veredicto y aserción a aserción igual que antes del sabotaje.
  - **Sitios**: esta fase no arregla ninguna clase de error. De los 11 sitios que calculan «cambió» por ubicación o
    recuento siguen quedando **10**, como dejó la fase 2. Enunciados con «como siempre» (M, `grep` sobre los
    `Prueba(` de `Contrato.cs`): **4** → **3**. El que se va es el de la 334, el único que lo decía de la duración de
    la espera tras pulsar; los otros tres hablan de otra cosa (232: lanzar una app; 296: contar un cambio que sí
    ocurrió; 331: buscar por nombre exacto) y no se tocan.
- **2026-09-22, fase 4 (el veredicto en cuatro; la 353 en verde por los dos caminos).**
  - **Qué cambió**, en los cuatro archivos de la tabla y en nada más:
    - `PulsarSegunElNucleo.Resultado` gana `Parte` y `MsHastaElVeredicto` (`init`; −1 = no hubo espera porque la mano
      no pudo). Las **5** salidas de `Pulsa` después de la mano pasan por un único `Con(...)` que pone los tres datos
      (la de antes, «no pude pulsar», se queda con los valores por defecto). Las **3** que decían «la pantalla no
      cambió» —el campo, la puerta que lleva aquí y la de siempre— dicen ahora «no cambió de sitio, pero cambió dentro
      (…)» o «… cambió delante (…)», y añaden «No es una llegada: no aprendo ningún tramo.». Con «nada» dicen
      exactamente lo que decían, y por eso la 44, la 202, la 296, la 334 y la 354 no se enteran. La cuenta no lleva el
      título de la ventana de delante, a propósito: llega al modelo, y un título puede llevar datos.
    - `RecorrerSegunElNucleo.Resultado` gana `QueCambio`, que por defecto sale de `Cambio` (cambió = de sitio; no =
      nada). De sus **19** construcciones solo **1** lleva un pulsar detrás (la final, con `ultimoPulso`), y esa copia
      el `QueCambio` del pulsar. Las otras 18 tienen `Cambio = false` y dicen «nada».
    - `SurfaceMapTools`: `Mano` gana `QueCambio` (`init`). `Anotar` lo copia del `Resultado`, y es **1** de las **10**
      construcciones de `Mano`: la única por la que pasa `Take`. **`:343`** construye el `Paso` con `mano?.QueCambio`, y
      es **1** de las **2** construcciones de `ElTramo.Paso`. La otra (`Sin`, `:252`) no actúa y nunca llega al
      detector. El hunk sigue siendo una sola línea, la `:343`, compartida con A (`:340-343`) y C (`:341-343`), en el
      orden acordado arriba.
    - `ElTramo.Paso` pasa a tener **12** parámetros. El 12.º, `QueCambio`, por defecto hereda de `Cambio` mediante un
      inicializador de la propiedad posicional. El detector cuenta `QueCambio == Nada`, y la línea de cada paso dice
      «cambió de sitio / cambió dentro / cambió delante / no cambió». En `ElTramo` quedan **2 de 2** lecturas de
      `Cambio` pasadas a `QueCambio`.
  - **`PorQueDejoDeEsperar` no entra en esta fase.** La tabla de «Cómo se ve en el código» lo pone en el `Resultado`,
    pero ninguna aserción de la 353 lo juzga. Lo que sí juzga la 359 (fase 8) son las cuatro causas en la cuenta. Entra
    con ella, para no añadir un campo que ninguna promesa mira.
  - **Hallazgo: dos relojes en una misma cuenta** (M, aprendizaje nº16). La primera versión medía `MsHastaElVeredicto`
    con el `Compas`, que cuenta con `Environment.TickCount64` y avanza a saltos de ~15,6 ms. En la corrida del sabotaje
    (b), una pulsación de **137 ms** en total dijo que se decidió «a los **141**». La aserción «dice a los cuántos
    milisegundos» cayó por el reloj, no por el sabotaje: la línea `:343` no está en el camino de `Pulsa`. Ahora se mide
    con un `Stopwatch` que arranca al soltar la mano, en el instante en que vuelve la espera que decide. Asentada,
    cambio de sitio y techo salen en el sondeo que los ve, así que ese instante es el del veredicto. **Queda sin
    arreglar, y se dice**: la línea de la 355 («dejó de esperar a los N ms») y `EsperaAsentada` siguen midiendo con
    el `Compas`. Puede haber hasta ~16 ms de diferencia con `MsHastaElVeredicto`. Cambiar el reloj por defecto del
    `Compas` toca la 245 y todas sus esperas: no es de esta fase.
  - **Contrato** (M, con `TEMP` propio): antes, `CONTRATO ROTO: 10` (302 ✔ / 5 ✘). Después, **`CONTRATO ROTO: 6`**
    (303 ✔ / 4 ✘: 356, 357, 358 y 359, de fases posteriores). Solo cambia un veredicto, `✘ 353.` → `✔ 353.`, y las
    aserciones de las cuatro rojas son las mismas que antes. 44, 83, 202, 204, 207, 245, 248, 292, 296, 299, 334,
    351, 352, 354 y 355 siguen en ✔. Voz: `VOZ ÍNTEGRA` (46 ✔, 0 ✘). Compila con 0 errores, sin warnings nuevos en
    los archivos tocados.
  - **Sabotajes de la 353, los dos de la spec, verificados por diff** (1+/1− cada uno, ancla con una sola
    coincidencia, copia y restauración con md5 idéntico y diff vacío, fecha tocada):
    - (a) El detector vuelve a contar `!p.Cambio` (`ElTramo.cs`). Resultado: **`CONTRATO ROTO: 8`**, y solo cae la 353.
      Cae el caso (i), «una puerta que abre un menú cuatro veces NO es un bucle: 3 pasos». Cae también el caso (ii),
      porque el camino del operador acaba en el mismo detector. La spec decía «cae el caso (i)», y lo medido añade el
      (ii). La 292 sigue en ✔.
    - (b) `:343` vuelve a construir el `Paso` sin `mano?.QueCambio`. La primera corrida dio `CONTRATO ROTO: 8`: cayó el
      caso (ii) y además la aserción del reloj, que es el hallazgo de arriba. Arreglado el reloj, se repitió:
      **`CONTRATO ROTO: 7`**, y cae **solo el caso (ii)**, «por el mapa (…) tampoco para por bucle: 3 pulsados». Es lo
      que predijo la spec. Sin esa línea, la 353 saldría verde solo con manos falsas (aprendizaje nº11).
    - Restaurados los dos → `CONTRATO ROTO: 6`, veredicto a veredicto igual que la primera corrida en verde.
- **2026-09-22, fase 5 (las otras dos esperas de `Pulsa`; la 357 en verde).**
  - **Dónde estaban**: la tabla de fases cita `:201` y `:225`, y ya no eran esas líneas. Antes de la fase, las dos
    llamadas sin huella estaban en `:306` (ensayo del doble) y `:331` (repetición); ahora están en `:315` y `:349`.
  - **Qué cambió**: un archivo de producción, `PulsarSegunElNucleo.cs`.
    - Las dos esperas pasan por `EsperarOtraVez`. Construye la espera con el mismo `NuevaEspera` que la del clic y
      decide con el mismo `comoHoy`: la regla 4, calculada una vez sobre las mismas entradas.
    - La espera del doble sale al asentarse.
    - La de la repetición cae siempre en el caso 4 (`HayQueRepetir` exige `SabeQueLleva`). Espera el techo con la
      huella en sombra, como pide la promesa.
    - La sobrecarga sin huella, `EsperarACambiar(string)`, se borra: 0 llamadores.
  - **Una línea por espera.**
    - Formato: `↻ ensayé «X» con el doble (83: …) · <medida de la huella> · dejó de esperar a los N ms: <por qué>`.
      La repetición deja una línea con la misma forma.
    - El aviso «lo repito una vez» iba antes de la mano; ahora forma parte de esa línea, después de la espera.
    - Así, la aserción `== 2` de la 357 cuenta una línea por espera. El hallazgo de la fase 2 dejó dicho que esa
      aserción tendría que decidir qué contaba, y esta es la decisión.
    - No son líneas de medida (`antes → después`). Esa sigue siendo **una por pulsación**, que es lo que cuenta el
      nivel 4.
  - **Las palabras del caso `SabeQueLleva`.** Antes decían «cortar aquí haría repetir el clic (248)»; ahora dicen
    «una asentada falsa aquí daría por perdida una navegación que aún está en camino (248)». Tras la repetición ya no
    se repite nada, y las dos esperas usan la misma frase (patrón nº2).
  - **El veredicto sale de la última espera.** `QueCambio` y `Parte` salen de la última espera que miró, igual que
    `MsHastaElVeredicto` desde la fase 4. Con dos esperas mirando, el `Resultado` habría dado el veredicto de la
    primera con el reloj de la segunda: una caja que miente (patrón nº8).
  - **Rastro de la mano** (patrón nº10). Un doble o una repetición que la mano no pudo dar no dejaba ninguna línea.
    Ahora dice «quise … y la mano no pudo: <motivo>». **No lo juzga ninguna aserción**, y se dice.
  - **Sitios** (M, `grep`):
    - Las **3** esperas de `Pulsa` consumen la huella. Una sola construcción de `EsperaAsentada` (`NuevaEspera`) sirve
      a las tres, y una sola regla (`comoHoy`).
    - De los 11 sitios que calculan «cambió» por ubicación o recuento quedan **8**; tras la fase 2 eran 10. Salen los
      dos que quedaban en `Pulsa`.
    - Llamadas a `Anota` en `Pulsa`: 5 → 8. Se van «lo repito» antes de la mano; entran las dos líneas `↻` y los dos
      rastros de la mano.
    - `AbrirSegunElNucleo` tiene su propio `EsperarACambiar` (4 apariciones), fuera por alcance.
  - **Aserciones nuevas de la 357.**
    - Tres se escribieron **en rojo antes del código**: las dos esperas del TreeItem dicen «asentada»; un doble que
      abre un menú da `Dentro` con la huella de la última espera; cada espera de la puerta dice por qué llegó al techo.
    - La cuarta se escribió **después**: la repetición lleva la medida de la huella. Salió al buscar qué cláusula no
      tenía juez: con la huella quieta del contrato, el veredicto de la repetición sale igual con huella o sin ella.
      Su rojo lo midió el sabotaje (b), no una corrida anterior al código.
    - El enunciado no cambió.
  - **Contrato** (M, con `TEMP` propio):
    - **Antes**, con las tres aserciones nuevas y sin código: `CONTRATO ROTO: 9` (303 ✔ / 4 ✘). La 357 tenía 4
      aserciones rojas. Los 307 veredictos, iguales a la fase 4. El TreeItem tardó **1.325 ms** con techo 1.200: la
      espera del clic salió asentada a los 125 ms y la del doble agotó el techo.
    - **Después**: **`CONTRATO ROTO: 5`** (304 ✔ / 3 ✘: 356, 358 y 359, de fases posteriores). Solo cambia
      `✘ 357.` → `✔ 357.`, y las aserciones de las tres rojas son las mismas.
    - Siguen en ✔: 44, 83, 245, 248, 292, 296, 299, 334, 351, 352, 353, 354 y 355.
    - Voz: `VOZ ÍNTEGRA` (46 ✔, 0 ✘). Compila con 0 errores, los mismos 36 warnings y ninguno en el archivo tocado.
  - **Sabotajes, verificados por diff** (1+/1− contra la copia, ancla con `\r\n` y una sola coincidencia):
    - (a) **El de la spec**: el ensayo del doble llama a la espera sin huella, `EsperarOtraVez(desde, null, comoHoy)`.
      Resultado: **`CONTRATO ROTO: 8`**, y solo la 357. Caen el caso del TreeItem (1.316 ms de 1.200) y las dos
      aserciones nuevas del TreeItem. La línea del doble dijo «llegó al techo: no había huella con la que mirar», y el
      menú dio `Nada`. Las de la puerta siguen en ✔.
    - (b) La repetición espera sin huella. Resultado: **`CONTRATO ROTO: 6`**, solo la 357 y solo la aserción de la
      medida. La línea de la repetición sale sin «sondeo(s) de huella» y lo demás sigue igual.
    - Restaurados los dos: md5 `56BFA486…` idéntico, `cmp` byte a byte y fecha tocada. Después, `CONTRATO ROTO: 5`
      veredicto a veredicto y aserción a aserción.
  - **Visto y no tocado**: el ensayo del doble se hace aunque la primera espera haya visto «dentro». Su condición es
    `hasta == desde`, gesto vacío y contenido. Si un TreeItem se despliega con el clic simple, cambia dentro y aun así
    recibe el doble, que puede volver a plegarlo. Hasta la 047 no había forma de verlo; ahora la huella lo ve.
    Cambiarlo es cambiar lo que promete la 83: necesita su propia promesa, en otra fase u otra rama.

## Revisiones

**2026-09-22, tarde.** Siete refutaciones sobre la primera versión de esta spec (`ea70679`), comprobadas una a
una contra el código de `5574148`, el log del 21-09 y las ramas remotas antes de aplicarlas. Las siete son
ciertas en el fondo; lo que no se reproduce se dice.

| # | Afirmación de la spec que se refutaba | Comprobado | Qué se hizo |
|---|---|---|---|
| 1 | «cambió de sitio sale en el acto» y «la memoria de 400 ms no afecta a lo que esta spec ahorra» | **cierta.** `DondeTrabajo` es `MemoriaCorta<string>(400)` (`FaceWindow.xaml.cs:5577-5583`) y `Pide` sirve lo recordado dentro de la caducidad (`MemoriaCorta.cs:41`); `Pulsa` no llama a `Cruzar` sin `hasta != desde` (`PulsarSegunElNucleo.cs:232-238`); el log enseña la forma a escala grande («Minimizar», `:506-507`). Los cinco casos de la 351 no tenían ninguno con cambio de sitio tardío | regla 2b (sitio fresco por respiro y antes de declarar), caso 6 de la 351 con su sabotaje, fila de `_donde()` y «Lo que queda fuera» reescritas. **Bloqueaba** |
| 2 | `SurfaceMapTools` solo en `:2642` y `:2865-2926` | **cierta.** El `Paso` nace en `:343` con `cambio = mano?.Logro`; `Mano` es `(Termino, Logro, Intento)` en `:2614`; A declara `340-343` (046) y C `341-343` (048 fase 7) | `:343`, `:2614` y `:2638-2645` en la tabla; el hunk compartido y su orden, dichos; caso (ii) de la 353 por `MapaParaTramo` con sabotaje en `:343` |
| 3 | «isabel» como ejemplo de «cambió dentro», marcado (M) | **cierta.** `UiaReader.Read(hwnd)` es `FromHandle(hwnd)` + hijas por `EnumChildWindows` de clases fijas + `MenuItem` del subárbol (`UiaReader.cs:83-120, 175-190, 447-470`); el selector de perfiles resolvió en `ventana='(sin título)'` (`:395`), otra ventana de nivel superior; 137 y 15 son dos lectores sobre dos ventanas | fila marcada (D) con el origen de cada número; cuarta parte de la huella (ventanas del proceso de trabajo); en el nivel 4, los popups se cuentan aparte y con la parte que los vio |
| 4 | «lo de dentro sale del lector con caché, 34–243 ms» como huella barata | **cierta.** Es la lectura completa: 144–420 ms medidos (`UiaReader.cs:127-131`); en el Explorador cada sondeo superaría el respiro (patrón nº14); y la 048 dice que la huella de B sale de la observación de la 362 (`:216-217`) | «dentro» pasa a ser un `FindAll` con `CacheRequest` de `RuntimeId` + tipo (coste sin medir: fase 0, Explorador antes de la fase 2) o el hash de `Observatorio.Reciente` cuando C entre; la cadencia dicha (solo si las baratas no cambiaron) |
| 5 | la 356 «declara el desvío en el acto» | **cierta.** `LlegoDondeTocaba` agota hoy el `Compas(EsperaMaximaMs)` entero (`RecorrerSegunElNucleo.cs:317-341`), lo que sobrevive a una redirección; la 044 de Jose registra una (`map_go_to docs.google.com` → `/document/u/0`, 12:10:56 del 18-09, M) | regla, punto 8: presupuesto de redirección (hasta medirlo, = techo); la 356 reescrita; caso «C a los 200, B a los 700 → llegada» con su sabotaje |
| 6 | «los 3 de `PasoDelNucleo` son de Jose» | **cierta.** El diff de `origin/jose/ir-devuelve-la-pagina-asentada` son 4 archivos y ninguno es `PasoDelNucleo`; `PasoDelNucleo` es el camino de «ir» (`ServidorDelNucleo.cs:55`, `FaceWindow.xaml.cs:742-756`); y esa rama trae `ComoSeContesta.InventarioAsentado` (cabecera igual dos veces, 250 ms, 2 s, `Thread.Sleep`) en `SurfaceMapTools.Call` | reatribuido (fuera por alcance, 3 + 1 pendientes con dueño); la 044/335 de Jose en la reconciliación, con el alcance de la 352 dicho por escrito; hablar con Jose antes del PR |
| 7 | `FaceWindow.xaml.cs:768-798` ≤4 líneas sin decir que contradice el encargo | **cierta en el fondo; la cifra no.** Con `git diff --name-only origin/main...rama` sobre las 46 refs de hoy, `FaceWindow.xaml.cs` está en **19**, no en 29 (M, 22-09). El choque con el encargo era real y no estaba escrito | el choque y quién lo decide (el dueño), dichos; ≤2 líneas en un hunk que C y D no tocan; «0 líneas «nadie miraba»» en los dos niveles 4 |

**El merge de la tarde** (`5574148`, «fix(memory): persist explicit requests and isolate instance logs»)
trajo las promesas 342 y 343 y el log por instancia; ninguna toca los archivos de esta spec. Cabecera,
hallazgo y nivel 4 actualizados; 351–359 siguen libres.

## Cierre

- [x] Fase 0 **escrita**: 355 verde, sabotaje verificado por diff (2026-09-22)
- [x] Fase 1: 352 verde, 299 intacta, sabotaje de la spec verificado por diff y uno extra sobre la compuerta (2026-09-22)
- [x] Antes de la fase 2: los dos «aún no» restados a `long.MinValue` (2 de 4 sitios), juzgados por la 355 (2026-09-22)
- [ ] Fase 2 **escrita** (351 verde, y la 354 con ella; dos sabotajes por diff; 2026-09-22) — **no entra** hasta el nivel 4 de la fase 0: `PrimeraHuellaMs`/`RespiroMs` siguen siendo metas
- [x] Fase 3: la 334 reescrita sin reciclar el número, con el cuerpo byte a byte; la 354 verde y su sabotaje propio verificado por diff (cae su primer caso, y con él la 351 y la 355) (2026-09-22)
- [x] Fase 4: la 353 verde por los dos caminos (manos falsas y `MapaParaTramo`); 44 y 292 intactas; los dos sabotajes de la spec verificados por diff (2026-09-22)
- [x] Fase 5: la 357 verde; 83, 248 y 296 intactas; las 3 esperas de `Pulsa` con una sola huella y una sola regla (de los 11 sitios quedan 8); el sabotaje de la spec y uno más, verificados por diff (2026-09-22)
- [ ] Fase 0 **medida** en tres pantallas con nombre, con las cuentas (a)–(e) en «Hallazgos» y **0** líneas «nadie miraba» — la corre el dueño: esta rama no ejecuta `U.exe`
- [ ] El dueño decidió sobre las ≤2 líneas de `FaceWindow` (o salieron a una rama de UI propia, y el PR lo dice)
- [ ] Hablado con Jose sobre `InventarioAsentado` (044/335) antes del PR; el hunk `:343` acordado con A y C
- [ ] 351–359 verdes (`.\scripts\contrato-del-grafo.ps1` → CONTRATO INTACTO), 360 reservada fuera del contrato
- [ ] Un sabotaje por promesa, verificado por diff, con el veredicto literal en el PR
- [x] La 334 reescrita en el registro y anotada en la spec 043; la 299 y la 040 sin tocar (2026-09-22, fase 3)
- [ ] `.\scripts\verificar.ps1` pasa, con evidencia en `out\evidencia.md`
- [ ] Probado en ≥3 pantallas, con nombre: ChatGPT.exe, Gmail en Chrome, Explorador «Descargas»
- [ ] Estado de este documento: **implementado** (AAAA-MM-DD)
