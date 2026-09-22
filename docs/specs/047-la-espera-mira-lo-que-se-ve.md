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
> **Números comprobados el 2026-09-22** sobre las 44 refs `origin/*` tras `fetch`: ni `047-` en `docs/specs`
> ni `"351.`…`"360.` en ningún `Contrato.cs` remoto. En `main` (`f811796`) la promesa más alta del grafo es
> ya la **341** (voz, «dos recordatorios vencidos despiertan una sola sesión»; entró hoy con `f811796`) —no
> la 340 que dice `arquitectura-jev-en-u.md` §6—; los 351–359 de esta spec siguen libres. La 341 de la rama
> A choca con esa 341 de `main`: se avisa aquí y lo resuelve A al rebasar, no esta rama.

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
| «No cambió» falsos, probados | **≥2 de 13**: «isabel» (16:34:09) abrió el selector de perfiles de Chrome —el propio recuerdo del modelo lo dice— y el inventario bajó de 137 a 15 elementos; en «Minimizar» (16:35:00) cambió la **ventana de delante** mientras la de trabajo seguía siendo chatgpt.com minimizada: el álbum guardó «mirada de web://mail.google.com… al llegar» a las 16:34:59 y un segundo después «no cambió» | log 21-09; informe §2.A y su revisión, reparo 8 (M) |
| Indicios por recuento, sin prueba | «Chrome» 111 → 133, «Jeronimo» 15 → 128, «Nuevo chat» 80 → 50 elementos entre señalar y el inventario de después | informe §2.A (M el recuento; D que sea un cambio real: los dos lectores pueden tener alcances distintos) |
| Por qué es falso | `EsperarACambiar` compara el STRING de ubicación de la ventana de trabajo y nada más | `PulsarSegunElNucleo.cs:299-314` (M, leído) |
| Cuánto tarda en cambiar la ubicación tras un clic que SÍ navega | «Discusión» cambió a los **388 ms**; mediana del 18-09, **405–510 ms** | spec 043 nivel 4; spec 040 y `RecorrerSegunElNucleo.cs:139-140` (M) |
| Cuánto cuesta mirar la ventana de trabajo | **34–243 ms** con el lector con caché (297/298); 17 líneas «señalar … leí la ventana» el 21-09, máximo 243 | specs 040/041; log 21-09 (M). El Explorador **sin medir** con el lector actual |
| Cuánto cuesta saber qué ventana está delante | `GetForegroundWindow` + `GetWindowText`: Win32, sin UIA | `UiaSurface.cs:17, 25, 425` (D: <1 ms, no cronometrado) |
| `_donde()` durante la espera | memoria de **400 ms** (`MemoriaCorta`): un cambio de ubicación se ve con hasta 0,4 s de retraso | `FaceWindow.xaml.cs:5577` (M, leído) |
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
| `Navigation/PasoDelNucleo.cs:231` | 1 (600) | 3 esperas por ubicación | **fuera**: es la rama de Jose (abajo) |
| `Navigation/AbrirSegunElNucleo.cs:290` | 1 (120) | 1 | fuera (misma clase, fuera del ciclo) |
| `Navigation/SurfaceNavigator.cs:85`, `MapaVivo.cs:506` | 1 + 1 | — | fuera (no son la espera tras pulsar) |
| `windows-graph/src/Surfaces/UiaSurface.cs` (la mano) | **27** | — | fuera: son ~160 ms por clic identificados de 333 (D), mejora 3 del informe, se miden antes de tocarlos |

Los sitios que calculan «cambió» por ubicación o recuento, sin huella: **11** (revisión del 22-09,
`arquitectura-jev-en-u.md` §3.2). Esta spec toca **7**: los 3 de `Pulsa`, `LlegoDondeTocaba` y los 3 de
`SurfaceMapTools`. Los 3 de `PasoDelNucleo` son de Jose; `Abrir` queda fuera.

## Por qué esto va dirigido por especificación

Porque la espera **se juzga a sí misma**: «no cambió» lo dice el mismo criterio que decide cuánto esperar, y
el 21-09 dio 13 de 13 con al menos 2 falsos sin que nada lo señalara. Y porque acortar esta espera **ya se
decidió no hacerlo sin medir**: la spec 043 dejó escrito «acortar esa espera sin medir es la spec 038,
aparcada por el dueño», y la promesa 334 lo exige en su caso 4 («un botón sigue esperando el presupuesto
entero», `Contrato.cs:12987-12988`). Una prueba escrita después del código se escribiría para que pasara;
aquí la primera fase **solo mide**, y la regla entra cuando la medida existe.

**La regla del flujo, y no tiene excepciones:** ninguna línea de producción entra antes que la promesa que
la juzga. Cada fase empieza con el contrato ROTO y termina con el contrato INTACTO.

## La regla

**Una pantalla que está cargando cambia entre dos miradas; una asentada, no.** Es la regla de la spec 040,
que hoy solo rige en la compuerta de vida (299). Se lleva a la espera de después de pulsar:

1. **La huella de lo que se ve** tiene tres partes: el **sitio** (la ubicación de trabajo, `_donde()`), la
   **ventana de delante** (hwnd + título, por Win32, sin UIA) y **lo de dentro** (los selectores de lo
   visible en la ventana de trabajo, ordenados; sale del lector con caché, 34–243 ms). Se construye y se
   compara en **un solo sitio**, `Navigation/HuellaDeLoQueSeVe.cs`, y la compuerta de vida (299) pasa a
   usar esa definición para sus dos partes de siempre (sitio y dentro). Aprendizaje nº16: hoy «cambió» se
   calcula en 11 sitios con criterios distintos.
2. Tras la mano, cada vuelta del compás (120 ms) toma la huella; la parte cara (dentro) como mucho cada
   `RespiroMs`. **Dos huellas iguales separadas por un respiro ≥ 250 ms = asentada**: la espera termina en
   ese instante. Antes de `PrimeraHuellaMs` no se declara nada: una página que aún no empezó a pintarse
   parece asentada (es el 400 de `EsperaDeAsentarMs` en la compuerta, elegido «del orden de lo que tarda en
   verse un cambio tras un clic»). **Los dos números salen de la fase 0**, no de este papel.
3. El veredicto ya no es «cambió / no cambió» sino **cuatro**: *cambió de sitio* (la ubicación; sale en el
   acto, `Cruzar` aprende: 44 intacta), *cambió delante* (otra ventana al frente, lo de «Minimizar»),
   *cambió dentro* (lo de «isabel»: se abrió algo) y *nada*. Dentro y delante **no acuñan arista** y no
   cuentan como «nada cambió» para el detector de bucle del tramo.
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

Lo que la huella **no** ve, y se dice: un cambio de estado sin cambio de selectores (un botón que pasa a
«pulsado», un texto que cambia de valor) es «nada». Para lo que esta spec mide —menús que se abren,
ventanas que cambian, navegaciones— basta; para lo que no, la huella del inspector de SAP (`StructureFingerprint`,
también ciega al contenido: pendiente nº6 de `CLAUDE.md`) tiene la misma ceguera desde julio.

### Cómo se ve en el código

| Pieza | Nueva/tocada | Qué |
|---|---|---|
| `Navigation/HuellaDeLoQueSeVe.cs` | **nueva**, pura | `record Huella(Sitio, Delante, Dentro)`; `De(sitio, delante, selectores)` ordena; `Comparar(antes, ahora) → QueCambio { Nada, Dentro, Delante, DeSitio }` con esa prioridad; `MismaPantallaQueVe(a, b)` = sitio y dentro (el criterio de la 299) |
| `Navigation/EsperaAsentada.cs` | **nueva** | el bucle: `Espera(Func<Huella?> huella, Huella antes, Compas, respiroMs, primeraMs, cadenciaMs) → Veredicto(QueCambio, MsHastaElVeredicto, PorQueDejoDeEsperar)`. `PorQueDejoDeEsperar`: `Asentada`, `CambioDeSitio`, `TechoSeMovia`, `TechoNadieMiraba`, `TechoSabeQueLleva`, `TechoNoSePudoMirar(causa)` |
| `Navigation/TechoDeLaEspera.cs` | **nueva**, pura | `Registra(ms, porCondicion)`, `Mediana`, `Techo(minimoMs) = máx(3 × Mediana, minimoMs)`; guarda las últimas 20 |
| `Navigation/HuellaEnVivo.cs` | **nueva** | la huella real: `_donde()`, `UiaSurface.VentanaDeDelante()` (ignorando las ventanas de Ü por `Propio.EsVentana`, el mismo camino que `SeguirElFoco`), y el lector con caché sobre la ventana de trabajo (`UiaReader.Read(hwnd)` → `Reconocedor.SelectorDe`) |
| `Navigation/PulsarSegunElNucleo.cs` | tocada | `Huella` (nulo = nadie mira), `Diario` (nulo = `LogBus`), `RespiroMs`, `PrimeraHuellaMs`, `TechoMinimoMs`, `Techo`; `EsperarACambiar` consume `EsperaAsentada`; `Resultado` gana `QueCambio`, `MsHastaElVeredicto` y `PorQueDejoDeEsperar` con valores por defecto (los `new(...)` de hoy siguen compilando) |
| `Navigation/RecorrerSegunElNucleo.cs` | tocada | `:412-413`: `Huella` local → `HuellaDeLoQueSeVe.MismaPantallaQueVe`; `LlegoDondeTocaba` (`:317-341`) consume la huella; `Resultado` lleva `QueCambio` |
| `Navigation/ElTramo.cs` | tocada | `Paso` gana `QueCambio` (por defecto: `Cambio ? DeSitio : Nada`); el detector cuenta `QueCambio == Nada`; `Cuenta` dice qué cambió |
| `Mcp/SurfaceMapTools.cs` | tocada | `:2642` (`Mano` lleva `QueCambio`), `:2865-2926` (`EsperarPantallaLista` sobre `EsperaAsentada` y `Compas`; `Llego` con `Compas`; `EsperarCambio` **se borra**: 0 llamadores) |
| `windows-graph/src/Surfaces/UiaSurface.cs` | tocada, **1 método estático añadido** junto a `VentanaExiste` (`:290`) | `VentanaDeDelante() → (Hwnd, Titulo)` por Win32. No toca `Walk` ni los `Sleep` de la mano: `Walk` es de la rama C |
| `Ui/FaceWindow.xaml.cs:768-798` | ≤4 líneas | `pulsar.Huella = new Navigation.HuellaEnVivo(DondeTrabajo, VentanaObjetivo).Tomar;` y `recorrer.Huella = pulsar.Huella;` |

**No se toca:** `EsperarloVivo` salvo las dos líneas de `Huella`; `MapaVivo`; `UiaReader` (297/298);
`SurfaceReadiness`; `AbrirSegunElNucleo`; `PasoDelNucleo`; `Decision/*`; la UI; SAP.

## Reconciliación con lo que ya promete el contrato

Leído antes de escribir: la 334 (spec 043) y la 299 (spec 040), y de paso la 44, 83, 245, 248, 292 y 296.

- **La 334 choca de frente en su caso 4.** `Contrato.cs:12986-12988`: «un botón sigue esperando el
  presupuesto entero», `ms4 >= Presupuesto - 100`, con «Guardar» y un `_donde` que nunca cambia — y su
  arnés **no inyecta huella**. Con la regla del punto 4 («sin huella, como hoy»), el fixture de hoy **sigue
  valiendo tal cual** y la promesa nueva 354 añade el caso «con huella asentada → menos de la mitad del
  techo» sobre el mismo «Guardar». Lo que sí queda impreciso es la última cláusula del **enunciado**: «y lo
  que no es un campo espera como siempre». Se propone reescribirla, **sin reciclar el número**, en la fase 3,
  cuando la fase 0 haya medido lo que la 043 pedía medir:

  > **334.** un campo de texto no navega: al pulsar un Edit o un ComboBox no se espera el presupuesto de un
  > cambio de pantalla —solo una espera corta, por si acaso—, no se consulta el terreno ni se repite el
  > clic, y la respuesta dice que es un campo y que tiene el foco; si aun así la pantalla cambió se cuenta
  > como cualquier navegación; **y lo que no es un campo espera a que lo que se ve se asiente, o el techo
  > si nadie mira** (2026-09-2x: hasta hoy «como siempre» era el presupuesto entero, medido 1.797–1.825 ms
  > en 13 de 13 clics que no navegaban).

  El cambio de enunciado se anota también en la spec 043 («lo que pasó después»). Hasta esa fase, la 334
  convive en verde con la regla: solo actúa cuando alguien inyecta la huella, y su contrato no la inyecta.
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
- **La 292 caso 6** («se repitió la misma puerta tres veces»): su fixture no cambia nada entre pasos, así
  que `QueCambio == Nada` y sigue parando a la tercera. Con «dentro», un clic que abre un desplegable ya no
  es una repetición: la 353 lo exige y lo juzga con un tramo falso.

## Las promesas

Cada enunciado va **literal** a `tests/ContratoDelGrafo/Contrato.cs`, bajo `// ── Spec 047 ──`, con su
cuerpo en su propia región antes de `Debe`. Se juzgan sin pantalla, sin SAP y sin TypeSafe: huella, reloj y
manos inyectados; lo que aún no existe se pide por reflexión y cuenta `Pendiente`. Cada una lleva su
sabotaje de una línea, que se comprueba por diff (memoria del repo: copia, rompe, diff, compila sin
silenciar, veredicto ROTO nombrando esa promesa, restaura, diff idéntico, INTACTO).

| # | Promesa | Fase |
|---|---|---|
| 351 | pulsar no espera a una pantalla que ya se asentó: si la ubicación de trabajo no cambió y dos huellas de lo que se ve, tomadas con un respiro en medio, coinciden, la espera termina en ese instante y no al agotar el techo; si entre huella y huella algo cambia, la pantalla se está moviendo y se espera hasta el techo; si el terreno ya sabe que esa puerta lleva a algún sitio, o la ubicación es de SAP, o nadie inyectó una huella, se espera como hoy; y en todos los casos queda dicho a los cuántos milisegundos dejó de esperar y por qué | 2 |
| 352 | hay una sola definición de «cambió»: la huella de lo que se ve —el sitio de trabajo, la ventana de delante y lo que se ve dentro— se construye y se compara por un solo camino, y la compuerta de vida y la espera tras pulsar lo usan las dos; dos huellas con las mismas partes son iguales aunque los selectores lleguen en otro orden; una parte distinta las separa y dice cuál, y el sitio manda sobre las otras dos; y la compuerta sigue juzgando solo el sitio y lo de dentro, así que la 299 dice lo mismo que decía | 1 |
| 353 | cambiar de sitio, cambiar por dentro y cambiar la ventana de delante son tres veredictos distintos, y «nada cambió» es el cuarto: el resultado de pulsar dice cuál fue y a los cuántos milisegundos; solo el cambio de sitio acuña una arista, como exige la 44; un cambio por dentro o de delante no aprende nada y la cuenta lo dice con esas palabras; y el detector de bucle del tramo solo cuenta las repeticiones en las que nada cambió, así que una puerta que abre un menú tres veces no es un bucle y una que no hace nada tres veces sí lo es, como exige la 292 | 4 |
| 354 | un botón que no navega no espera el techo cuando la pantalla se asentó: con una huella que ve lo mismo dos veces, pulsar «Guardar» contesta en menos de la mitad del techo, dice que la pantalla no cambió y no aprende nada; sin huella inyectada espera el techo entero, que es lo que la 334 exige hoy y sigue exigiendo; y un campo de texto sigue con su espera corta y su respuesta de campo | 3 |
| 355 | cada pulsación deja en el log la medida de su espera: la ubicación de trabajo y la ventana de delante antes y después, si lo que se ve dentro cambió, a los cuántos milisegundos se habría dado la pantalla por asentada —o que nunca se asentó—, y a los cuántos cambió la ubicación o que no cambió en el techo; la línea sale aunque la espera no se recorte; y sin huella inyectada dice que nadie miraba | 0 |
| 356 | comprobar la llegada declara el desvío en el acto cuando lo que se ve se asentó en OTRA pantalla —ni la esperada ni la de partida—, nombrando las dos, en vez de agotar el techo mirando; si se asentó en la de partida se sigue esperando hasta el techo, porque una página que aún no empezó a pintarse parece asentada; llegar a la esperada contesta en cuanto la ubicación coincide, como hoy; y sin huella se espera como hoy | 6 |
| 357 | las tres esperas de pulsar —tras el clic, tras el ensayo del doble y tras la repetición— consumen la misma huella y la misma regla: con una huella que se asienta, un elemento de lista sin gesto aprendido se ensaya con el doble y esa segunda espera sale en cuanto se asienta en vez de agotar el techo; la repetición de una puerta con destino sigue esperando el techo las dos veces, porque solo se repite lo que se sabe que navega; y la 83, la 248 y la 296 dicen lo mismo que decían | 5 |
| 358 | esperar a que la pantalla esté lista en las herramientas del mapa gasta del reloj y no cuenta vueltas —con un sondeo lento, una espera de N milisegundos termina en N y no en N por el número de vueltas—, y decide «lista» con la misma huella de lo que se ve, dos iguales con un respiro, en vez de contar botones de la ventana de delante; comprobar la vuelta tras un Enter deshecho gasta del mismo reloj; y la espera de un cambio que nadie llamaba deja de existir | 7 |
| 359 | el techo de una espera sale de la espera normal medida y no de un número a ojo: es el triple de la mediana de lo que tardaron en asentarse o cambiar las últimas esperas que terminaron por condición, nunca menos que el techo mínimo; las esperas que llegaron al techo no lo alimentan; y al agotarlo la cuenta dice cuál de las causas fue: que la pantalla no paró de moverse, que nadie miraba, que el terreno sabía que la puerta lleva a algún sitio, o que no se pudo mirar y por qué | 8 |
| 360 | **reservada**: SAP asentada = `!Busy` en 3 sondeos consecutivos (pendiente nº1 de `CLAUDE.md`, la carrera del Busy) + dos `StructureFingerprint` iguales. **No entra al contrato** hasta una sonda de solo lectura en el hospital (aprendizaje nº13: 0 líneas `sapgui://` en 23 logs de esta máquina). El número no se recicla | — |

La que de verdad cierra el asunto es la **351**: mientras no exista, todo lo demás es contabilidad. La que
lo hace *honesto* es la **355**: sin ella, la 351 entraría sin saber cuántas «asentadas» son falsas.

**Desvío respecto al reparto de `arquitectura-jev-en-u.md` §7, dicho aquí:** allí la 359 estaba reservada
para «`PasoDelNucleo` sin `Sleep(600)`, si Jose cede». El encargo exige una promesa para el techo por paso
y el reparto no la tenía; el techo toma la 359 y `PasoDelNucleo` pasa a «lo que queda fuera», donde ya
vive con su propia promesa en la rama de Jose.

### Con qué se juzga cada una

Todas con **mapa a mano** en la propia prueba (como la 334 y la 299): un `Nucleo.Grafo` con tres elementos,
un `_donde` controlado, una mano que cuenta toques, un `Diario` que acumula líneas y una **huella
inyectada** como `Func<Huella?>` que devuelve una secuencia según la llamada (`n => …`). Los presupuestos son
pequeños (techo 1.200, respiro 100, primera 100) para que el contrato corra en segundos, igual que la 299.

- **351** — `PulsarSegunElNucleo.Huella` por reflexión; sin la propiedad, `Pendiente("PulsarSegunElNucleo.Huella
  (la espera mira lo que se ve)", "351", "047")`. Cinco casos sobre «Ir» (Button): huella constante →
  `ms < Techo/2` y el diario dice «asentada a los N ms»; huella que cambia en cada llamada → `ms ≥ Techo −
  100` y «no paró de moverse»; grafo con la arista «Ir» → otro sitio (`SabeQueLleva`) y huella constante →
  `≥ Techo − 100` en la primera espera y el diario dice «lleva a algún sitio»; ubicación `sapgui://…` y
  huella constante → `≥ Techo − 100`; sin huella → `≥ Techo − 100` y «nadie miraba». **Sabotaje:** en
  `EsperaAsentada`, la comparación de las dos huellas devuelve siempre «distintas» (una línea): cae el
  caso 1 y ninguna otra promesa.
- **352** — `Capacidad("U.WindowsClient.Navigation.HuellaDeLoQueSeVe")`: `De` con los mismos selectores en
  orden distinto → `Comparar == Nada`; solo delante distinta → `Delante`; solo dentro → `Dentro`; sitio
  distinto con todo lo demás distinto → `DeSitio`; `MismaPantallaQueVe` ignora «delante». Y que la
  compuerta lo use: el cuerpo lee `RecorrerSegunElNucleo.cs` desde `U_REPO` (el mismo camino que la 341
  de la voz, ya en `main`) y exige que nombre `HuellaDeLoQueSeVe` y no defina ya su `string Huella(`; sin
  `U_REPO` dice «no pude juzgarla» y no cuenta rojo (aprendizaje nº17: «no pude» no es «culpable»).
  **Sabotaje:** invertir la prioridad en `Comparar` (dentro antes que sitio): cae el caso «sitio manda».
- **353** — Pulsar con huella que cambia «dentro» y luego se asienta → `Resultado.QueCambio == Dentro`,
  `CambioLaPantalla == false`, `Aprendido == false`, la cuenta contiene «dentro»; ídem «delante»; `_donde`
  cambia → `DeSitio` y `Aprendido == true` (44); nada → `Nada`. Y un `ElTramo` con manos falsas: cuatro
  pasos con el mismo selector y `QueCambio = Dentro` paran por «se agotó el tope», no por bucle; tres con
  `Nada` paran por «bucle». `ElTramo.Paso` se construye por reflexión con su constructor de 12
  parámetros; si no existe, `Pendiente`. **Sabotaje:** el detector vuelve a contar `!p.Cambio` en vez de
  `QueCambio == Nada`: cae el caso del desplegable.
- **354** — El mundo de la 334 («Search», «Rename», «Guardar»), techo 1.200: «Guardar» con huella
  constante → `ms < Techo/2`, `!CambioLaPantalla`, `!Aprendido`, cuenta con «no cambió» y sin «campo»;
  «Guardar» sin huella → `ms ≥ Techo − 100` (el caso 4 de la 334, calcado); «Search» con y sin huella →
  `ms < Techo/2` y cuenta con «campo» y «foco». **Sabotaje:** `if (!esCampo) huella = null;` antes de la
  espera: cae solo el primer caso.
- **355** — `Diario` y `Huella` inyectados; tras `Pulsa` el diario tiene UNA línea que contiene «antes»,
  «después», «delante», «asentada a los» + « ms» y «ubicación»; con huella que nunca coincide dice «nunca
  se asentó»; sin huella dice «nadie miraba». En la fase 0 la espera sigue llegando al techo (`ms ≥ Techo −
  100` **también con huella**: se comprueba, y la aserción se retira en la fase 2 con la 351, dejándolo
  dicho en el commit). **Sabotaje:** quitar la línea que anota el instante de la asentada: la línea sale
  sin «asentada a los».
- **356** — `RecorrerSegunElNucleo.Huella` por reflexión (`Pendiente` si falta). Un paso de escribir con
  `Llegada = B` desde A: `_donde` da C ≠ A ≠ B y huella constante → desvío en `< Techo/2` nombrando B y C;
  `_donde` da A (la de partida) y huella constante → `≥ Techo − 100`; `_donde` pasa a B a los 200 ms →
  llegada en `< Techo/2`; sin huella y `_donde` = C → `≥ Techo − 100`. **Sabotaje:** quitar la condición
  «ni la de partida»: cae el segundo caso.
- **357** — Mundo con un `TreeItem` sin gesto y una `Button` con destino aprendido a otro sitio. TreeItem
  con huella constante y `_donde` fijo → 2 toques (clic y `doubleclick`) y total `< Techo` (dos esperas,
  ninguna al techo); Button con destino → 2 toques (clic y repetición) y total `≥ 2 × Techo − 200`, y el
  diario lo dice dos veces; los fixtures de la 83, 248 y 296 no se tocan. **Sabotaje:** el ensayo del doble
  llama a la espera sin huella: cae el caso del TreeItem.
- **358** — `Capacidad("U.WindowsClient.Navigation.EsperaAsentada")`: huella lenta (400 ms de `Sleep`)
  con techo 1.200 → termina en `< 1.200 + 3 × 400` (la forma de la 245); huella constante → `< Techo/2`
  con `Asentada`; huella cambiante → techo con `TechoSeMovia`. Y por `U_REPO`, `SurfaceMapTools.cs` no
  contiene `EsperarCambio(` y `EsperarPantallaLista` nombra `EsperaAsentada` (mismo camino que la 341 de
  la voz; sin `U_REPO`, «no pude juzgarla»). **Sabotaje:** el bucle de `EsperaAsentada` vuelve a `for (i <
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
| 0 | la medida en el log, sin recortar nada | **355** | nuevas `HuellaDeLoQueSeVe`, `EsperaAsentada` (en sombra), `HuellaEnVivo`; `UiaSurface.VentanaDeDelante` (+1 estático); `PulsarSegunElNucleo` (`Huella`, `Diario`, la línea); `FaceWindow` ≤2 líneas | 355 verde; 245, 248, 296, 334 intactas; **nivel 4 medido** (abajo) |
| 1 | una sola definición de «cambió» | **352** | `RecorrerSegunElNucleo.cs:412-413`; `HuellaDeLoQueSeVe.MismaPantallaQueVe` | 352 verde; 299 intacta |
| 2 | la espera sale al asentarse | **351** | `PulsarSegunElNucleo.EsperarACambiar` (el sitio del clic, `:162`), `RespiroMs`, `PrimeraHuellaMs` con los valores de la fase 0 | 351 verde; la aserción «también con huella llega al techo» de la 355 se retira en este commit y se dice |
| 3 | la 334 reescrita, sin reciclar | **354** | `Contrato.cs` (enunciado de la 334 + caso nuevo), `docs/specs/043` («lo que pasó después») | 354 verde; el fixture viejo de la 334 sigue byte a byte |
| 4 | el veredicto en cuatro | **353** | `PulsarSegunElNucleo.Resultado`, `RecorrerSegunElNucleo.Resultado`, `SurfaceMapTools.cs:2642` (`Mano`), `ElTramo.Paso`/detector/`Cuenta` | 353 verde; 44 y 292 intactas |
| 5 | las otras dos esperas de `Pulsa` | **357** | `PulsarSegunElNucleo.cs:201` y `:225` | 357 verde; 83, 248, 296 intactas; **3 sitios** contados en el commit |
| 6 | la llegada declara el desvío en el acto | **356** | `RecorrerSegunElNucleo.LlegoDondeTocaba`, `Huella` en `Recorrer`, `FaceWindow` +1 línea | 356 verde; 103 intacta |
| 7 | las herramientas del mapa gastan del reloj y miran la huella | **358** | `SurfaceMapTools.cs:2865-2926`: `EsperarPantallaLista` → `EsperaAsentada`+`Compas`; `Llego` → `Compas`; `EsperarCambio` borrado | 358 verde; **3 sitios, 1 borrado**, en el commit |
| 8 | el techo sale de la medida y dice su causa | **359** | nueva `TechoDeLaEspera`; `PulsarSegunElNucleo` (`Techo`, `TechoMinimoMs`, las cuatro causas) | 359 verde; 245 intacta |

### Fase 0 — la medida, y solo la medida

| | |
|---|---|
| **Promesa** | 355 |
| **Qué toca** | ver tabla; `EsperarACambiar` sigue esperando la ubicación hasta el techo, y **además** corre `EsperaAsentada` en sombra para anotar a los cuántos ms se habría asentado |
| **Coste que añade** | una huella por sondeo: la parte Win32 cada vuelta (<1 ms, D), la parte UIA como mucho cada 250 ms (34–243 ms, M). El compás manda: la espera sigue acotada por el techo; el número de sondeos por espera baja de 13–18 a ~6–10 (D). Se dice en el PR |
| **¿Núcleo congelado?** | no |
| **Nivel 4 obligatorio antes de la fase 2** | ChatGPT.exe y Gmail en Chrome (las dos pantallas del 21-09) + Explorador «Descargas» (su lectura está sin medir con el lector actual), Jev apagado, **≥30 clics que no navegan y ≥10 que sí**; de la línea nueva se cuentan: (a) «no cambió» falsos —dentro y delante—, (b) **«asentada» falsas**: clics que navegaron y cuya huella coincidió ANTES de cambiar de sitio, con el instante de cada cosa. De (b) salen `PrimeraHuellaMs` y `RespiroMs`; de la distribución de «ms hasta cambio de sitio», el techo mínimo (p95 + 300). Se pega el log en el PR con horas |
| **Terminado** | 355 verde; 245, 248, 296, 334 intactas; la medida escrita en «Hallazgos» |

### Fases 1–8

Cada una: la promesa ROJA primero (`Pendiente` o `Debe` en rojo), el código hasta INTACTO, el sabotaje
verificado por diff, y el commit citando la promesa y la medida («3 sitios», «0 llamadores»). Ninguna toca
`Decision/*`, la UI ni SAP. La 2 no entra sin el nivel 4 de la 0: es la condición que la 043 dejó escrita.

## Lo que queda fuera

- **SAP** (360): «asentada» allí es `!Busy` ×3 + `StructureFingerprint` ×2, y cuesta un recorrido COM. No
  entra sin la sonda del hospital; hasta entonces `sapgui://` espera como hoy, por diseño (regla, punto 4).
- **`PasoDelNucleo`** (3 esperas por ubicación + `Sleep(600)` en `:231`): es de
  `origin/jose/ir-devuelve-la-pagina-asentada` (5 commits `wip`, «tras navegar se cuenta la página asentada,
  no su esqueleto», promesa 335 en su rama —número que en `main` ya usa la voz: lo renumera él—). Misma
  idea; se habla, no se abre una rama paralela. Si Jose cede, entra aquí como fase 9 con número nuevo.
- **`AbrirSegunElNucleo`** (`:282-293`, `Sleep(120)`) y **`SurfaceReadiness`** (`windows-graph`): la misma
  clase de error, fuera del ciclo del tramo. Anotados; otra rama.
- **Los `Sleep` de la mano** en `UiaSurface` (27 sitios; ~160 ms por clic identificados de 333, D): mejora 3
  del informe. Se miden antes de tocarlos, y `Walk` es de la rama C.
- **La memoria de 400 ms de `_donde`** (`MemoriaCorta`, mejora 6): en un clic que sí navega, el cambio de
  sitio se ve con hasta 0,4 s de retraso. No afecta a lo que esta spec ahorra (los clics que no navegan);
  acota lo que puede ahorrar en los que sí. Otra rama, con su promesa.
- **Esperar con WinEvents** en vez de sondear (mejora 12): fase 2 del informe, después de medir la 351.
- Los otros tres `Sleep` de `SurfaceMapTools` (`:2200`, `:2340`, `:2729`): herramientas distintas de la
  espera tras pulsar. Anotados.
- **Que la huella vea el contenido** (un valor que cambia sin cambiar los selectores): la 360 y el
  pendiente nº6 de `CLAUDE.md` tienen la misma ceguera. Se dice; no se arregla aquí.
- **La UI** (el panel de Jev lee `Paso.QueCambio` y `MsHastaElVeredicto`: rama D) y **`Decision/*`** (A).

## Nivel 4 pendiente

- El de la fase 0 (arriba), **antes** de la fase 2. Tres pantallas con nombre; las cuentas (a) y (b).
- Al cerrar: las mismas tres pantallas con la rama entera; «⏱ pulsar … esperar el cambio» antes y después
  sobre los mismos botones («Nuevo chat», «Minimizar», «Seleccionar modelo», «Documents»); un clic que
  navega («Discusión» en Wikipedia, «Descargas» en el Explorador) para ver que `Cruzar` sigue aprendiendo
  y que ninguna «asentada» se declara antes del cambio de sitio; un `map_type` con Enter en el Explorador
  (358); y un tramo con Jev **apagado** (`U_DECISOR=simulado`) que pulse tres veces un botón que abre un
  menú, para ver que no para por bucle (353).
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
- **2026-09-22.** `main` ya tiene la 341 (voz, `f811796`, hoy). El «341–385 libres» de
  `arquitectura-jev-en-u.md` §6 quedó viejo la misma mañana; la rama A tiene que renumerar su 341. Esta
  rama (351–359) no choca.
- *(Se rellena durante la implementación: las cuentas (a) y (b) de la fase 0, los valores de
  `PrimeraHuellaMs`/`RespiroMs` y el techo mínimo, con fecha y con el log.)*

## Cierre

- [ ] Fase 0 medida en tres pantallas con nombre, con las cuentas (a) y (b) en «Hallazgos»
- [ ] 351–359 verdes (`.\scripts\contrato-del-grafo.ps1` → CONTRATO INTACTO), 360 reservada fuera del contrato
- [ ] Un sabotaje por promesa, verificado por diff, con el veredicto literal en el PR
- [ ] La 334 reescrita en el registro y anotada en la spec 043; la 299 y la 040 sin tocar
- [ ] `.\scripts\verificar.ps1` pasa, con evidencia en `out\evidencia.md`
- [ ] Probado en ≥3 pantallas, con nombre: ChatGPT.exe, Gmail en Chrome, Explorador «Descargas»
- [ ] Estado de este documento: **implementado** (AAAA-MM-DD)
