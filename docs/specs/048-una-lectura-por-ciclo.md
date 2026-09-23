# Una lectura por ciclo

> Spec 048 · 2026-09-22 · rama `jero/jev-una-lectura-por-ciclo` · promesas **361–370** · reescribe la **264**
> · Estado: **en construcción** (2026-09-22, noche): fases 1–8 y 10 hechas, 306/306 y `CONTRATO INTACTO`; falta
> `verificar.ps1` con su evidencia (fase 9) y todo el nivel 4, que no se ha corrido (U.exe no se ejecuta en este encargo).
>
> Es la rama **C** de la nueva arquitectura de Jev (`scratchpad/jev/arquitectura-jev-en-u.md`, §4 y §7).
> Nace desde `main` `dde8c40` y trae por merge `f811796` (voz: la **341**) y `5574148` (Felipe, 22-09 09:58:
> las **342** y **343**, memoria explícita y un log por instancia). Va **después de A** (spec 046) **y de B**
> (spec 047) a `main`.
>
> **Revisada el 2026-09-22** con doce refutaciones (sección «Revisiones», al final): once ciertas y una
> cierta con matiz. Lo que cambió respecto a la primera versión (`8f16c48`): la fusión con el terreno
> sigue siendo por etiqueta porque la **183** lo exige y su fixture lo juzga; la **264** se reescribe sin
> reciclar; `UiaReader` **no se toca** (0 líneas); la 364 pregunta solo cuando el grafo no tiene la puerta
> viva; la 369 olvida al accionar y arranca en sombra; la 366 separa el contador de la línea limitada; la
> 361 baja la vigencia por debajo del sondeo y gana una costura juzgable; y las cifras del log se
> vuelven a contar con sus comandos.
>
> **Marcas.** **(M)** medido: log, `grep`, sonda o spec anterior con fecha. **(D)** deducido del código o de
> sumar medidas. Un número sin marca es una meta.

## De dónde sale

El dueño: «trae main, revisa y cambia la arquitectura al nuevo Jev». La arquitectura común reparte el trabajo
en cuatro ramas; a esta le toca **la lectura**: hoy un paso del tramo puede pagar hasta cinco lecturas UIA de
la misma ventana, el reproductor de workflows recorre el árbol con tres funciones que hacen el mismo
recorrido, el mapa vivo está saturado la mayor parte del tiempo que la app corre con Neo4j caído, y lo que
va al decisor no lleva caja —así que la rama D no tiene qué pintar—.

El encargo, en cuatro frases, y lo que cada una promete abajo:

1. **Una lectura de pantalla por ciclo, compartida.** `ReadFields`, `ReadinessCount` y `StructureFingerprint`
   salen del mismo barrido (361); la lectura del paso se comparte con versión por la misma ventana que se
   leyó (362); lo que el paso acaba de leer, la compuerta no lo vuelve a mirar (363, con la 264 reescrita).
   El número de lecturas por paso queda en el log.
2. **Cuando el paso ya trae selector y el grafo no tiene la puerta viva, se pregunta por ese elemento y no se
   inventaría la ventana** (364); la cuenta dice cuál de los tres caminos se usó y cuánto costó.
3. **Las candidatas que van al decisor salen de esa misma lectura**, con selector, etiqueta, tipo y caja,
   vivas, con nombre, sin duplicar por identidad —y fundidas con el terreno **como la 183 manda**— y con su
   número y coste en el log (365); y se publican para quien pinte (368).
4. **Neo4j no bloquea el hilo de la ubicación: primero se MIDE** (366) y solo si bloquea se saca del hilo con
   su promesa (367, reservada). De paso, el «dónde» se pregunta una vez por instante, olvidando al accionar y
   **en sombra hasta medir** (369).

## Lo que se midió antes de escribir código

Recontado el **2026-09-22 entre las 10:20 y las 10:25** sobre `%LOCALAPPDATA%\U\logs` (el log del día crece:
por eso la hora). Comandos y salidas, literales:

```
$ ls $LOCALAPPDATA/U/logs/u-*.log | wc -l                                   → 17   (no 23)
$ grep -c SATURADO u-20260921.log                                           → 183
$ grep -c 'SATURADO (ubicación)' u-20260921.log                             → 12
$ grep -c 'SATURADO (lectura de pantalla)' u-20260921.log                   → 171
$ grep -oE 'lectura de pantalla\): [0-9]+' u-20260921.log | sort | uniq -c  → 14×20 2×21 … 31×45 6×46 … 13×48 … 15×53 … 2×60
   (n=171 · mín 20 · mediana 48 · máx 60)
$ grep -c SATURADO u-20260922.log   (10:20)                                 → 232  (20 ubicación · 212 lectura)
   (a las 10:25, solo lectura: n=217 · mín 45 · mediana 52 · máx 71)
$ grep -c SATURADO u-20260903.log                                           → 58   (7 ubicación · 51 lectura)
$ ls u-20260826.log                                                         → no existe
$ grep -l 'Neo4j no responde' u-*.log | wc -l                               → 0 de 17
$ grep -l 'sapgui://' u-*.log | wc -l                                       → 0 de 17
$ grep -ci neo4j u-20260903.log                                             → 0   (ni «no pude leer» ni «no responde»)
```

| Qué | Medida | Fuente | |
|---|---|---|---|
| Lecturas UIA completas que un paso del tramo PUEDE pagar sobre la misma ventana | **hasta 5**: `PuertasDeAhora` (`SurfaceMapTools.cs:172`), `ObservarLaVentanaDeTrabajo` (`FaceWindow.xaml.cs:5650-5671`, freno 800 ms, solo con ventana fijada ≠ foco), `MirarOtraVezLaVentana` ×1–3 (`:5626-5648`, `new UiaReader()` cada vez), señalar (`SurfaceMapTools.cs:1179`) y el inventario pegado (`:2147-2151` → `LoQueVeo` → `PuertasDeAhora`) | `grep` de `_lector.Read()` y `lector.Read(`: 8 sitios en `SurfaceMapTools`, 2 en `FaceWindow`, 1 en `AgentLoop`, 3 en `UiInspector` | M |
| Lo que cuesta cada una | 34–89 ms (Instagram, 45 elementos, spec 040); **77–113 ms** en las 8 líneas «señalar … leí la ventana» del 21-09 (48–99 elementos); máximo 243 ms en las 17 del informe | `u-20260921.log`; `040…md:33` | M |
| El Explorador con el lector actual (#90–#91) | **sin medir**: su 959–1.343 ms es de antes de #90 | `038…md`, informe §2.B | — |
| **Qué lee `Take`** | **nada**: construye el `Paso` y lo entrega a `RecorrerPorElNucleo` (`SurfaceMapTools.cs:2586-2602`); las lecturas de un `map_take` viven en `FaceWindow` (`ObservarLaVentanaDeTrabajo` antes de `Recorre`, `:894-901`; `MirarOtraVezLaVentana` solo desde la compuerta, `:5636-5638`) y en señalar (solo con coreografía) | código | M |
| **Cuándo mira la compuerta** | solo si el grafo NO tiene la puerta viva: con `porSelector is { Vivo: true }` devuelve `Hallado` sin mirar (`RecorrerSegunElNucleo.cs:436-438`); después la mano resuelve el mismo selector otra vez (`UiaSurface.Execute` → `Resolve(sel)`, `:988-996`) | código | M |
| `Walk` en `UiaSurface` | **3 sitios** hacen el mismo recorrido nodo a nodo con `TreeWalker` + `.Current`: `ReadFields` (`:470`), `ReadinessCount` (`:493`), `StructureFingerprint` (`:510`); tope `depth > 40 \|\| acc.Count > 300` (`:574`). Ninguno está en el camino de `map_decidir` ni del tramo: los llaman `SurfaceReadiness` (`:139, :191`), `WorkflowPlayer` (`:330, :340`), `WorkflowDryRun`, `WorkflowLibraryWindow` y los lectores de SAP (otra clase) | `grep` | M |
| Barridos por paso del reproductor | huella (1 `Walk`, `WorkflowPlayer.cs:330`, ANTES de la compuerta `:382`); en la compuerta `SafeReady` va primero (`SurfaceReadiness.cs:146`, un `Resolve`, sin barrido) y `SafeCount` —el único `ReadinessCount` de la superficie— **solo si no está listo** (`:153-155`), uno por sondeo de `PollMs = 120`. **En el caso común (listo en el sondeo 0) la huella es el único `Walk`**: el barrido compartido ahorra como mucho el primer sondeo cuando el elemento no está listo, y nada en el caso común. El ahorro real se mide con «barrido nº» en el nivel 4 | `SurfaceReadiness.cs:139-161`, `WorkflowPlayer.cs:330-386` | D |
| Mapa vivo saturado | ver los comandos de arriba. 21-09: **183** avisos, **12** de ubicación y **171** de lectura, de **20 a 60** vueltas descartadas (mediana **48**; 14 avisos de exactamente 20 y 31 de 45). 22-09 a las 10:25: **217** de lectura, mediana **52**, máximo **71**. Con el latido a 900 ms son ~67 vueltas por minuto: por aviso se pierde entre el **30 % y el 90 %**, mediana **72 %** —y es **cota superior**, porque `_descartadas` es UN contador compartido por los dos relojes (`MapaVivo.cs:62-66`: el de ubicación también lo incrementa y el rótulo es el del reloj que cruzó el umbral) | `grep` sobre `u-20260921.log`, `u-20260922.log`; `MapaVivo.cs:60-71` | M |
| Por qué: `Proyectar` es un POST síncrono en el hilo del latido y en el de la ubicación | 2.023–2.121 ms por fallo con Neo4j rechazando en `127.0.0.1:7474`; en esta máquina Neo4j está caído el 21-09 («no pude leer de Neo4j: … denegó expresamente dicha conexión», 16:25:29) | informe §2.C (sonda `percibir\neo`); `u-20260921.log`; `MapaVivo.cs:296, :439, :569, :586` | M el POST; **D** que sea la causa de todos los SATURADO: el 03-09 hay 58 avisos y **ninguna** línea de Neo4j en el log, ni «no pude leer» ni «no responde», así que si estaba arriba o abajo ese día no se sabe |
| «Neo4j no responde» en el log | **0 veces en 17 logs**, y el porqué está en el código: `AsegurarIndices()` corre en el constructor (`ProyectorNeo4j.cs:79`), `Cuenta` se asigna después (`MapaVivo.cs:29` construye; `:191` asigna), y `Mandar` gasta ahí el único aviso (`_yaAvise = true`, `:645-649`) sin que nadie lo lea | código | M |
| `Proyectar` con el mismo grafo no toca la red | `if (grafo.Version == _ultimaVersion) return false;` y `_ultimaVersion = grafo.Version` **antes** de mandar (`:170, :177`): tras un fallo, la misma versión no se reintenta hasta que el grafo cambie | código | M |
| Ubicación ciega tras cada salto | ráfagas de «descarté una vuelta» (375 → 562 → 843 → 1264 ms) mientras «localizar cuesta ~12–194 ms» | `u-20260922.log` 03:44, 09:13, 09:42 | M |
| Cuántos sitios preguntan «dónde estoy» sin memoria | **25** llamadas a `DondeEstoy()` (24 en `FaceWindow`, 1 en `SurfaceLocator`) y `_where()` en `SurfaceMapTools` es ese mismo crudo (`FaceWindow.xaml.cs:434`); `DondeTrabajo` sí recuerda 400 ms (`:5577`) **y olvida al accionar** (`SeguirElFoco`, `:5679`) y justo después vuelve a preguntar `DondeEstoy()` (`:5680`) | `grep -c` | M |
| **Cuántas de esas 25 caen dentro de 400 ms de otra** | **sin medir**: hoy `DondeEstoy` no deja línea. Es lo que la fase 8 cuenta en sombra antes de servir nada | — | — |
| Dos funciones para «la ventana de la persona» | `UiaReader.Read()` lee `AppAligner.VentanaDelUsuario()` (`UiaReader.cs:56`): `GetForegroundWindow()` **sin** subir a la raíz y, con Ü delante, la última que la persona activó (`AppAligner.cs:217-232`); `_where()` → `DondeEstoy()` → `Ahora()` sube a `GA_ROOT` (`SurfaceLocator.cs:164-167`) y con Ü delante baja por el orden Z (`LaVentanaDeDelante`, `:175`). Son dos caminos (aprendizaje nº16). **Que difieran a menudo es D**: sin medir | código | M/D |
| Preguntar por UN selector frente a inventariar | 7–126 ms según la posición (`bench-uia-3.txt`); en Chrome, preguntar y fallar 60–107 ms frente a 232–272 ms de inventariar | arquitectura §4.2, informe mejora 5 (`Resolve` 23–106 ms) | M |
| La segunda mejor se dispara por una subcadena | `!cuenta.Contains("puertas vivas para", Ordinal)` (`SurfaceMapTools.cs:331`), redundante con `Mano.Intento = !r.Ambiguo` (`:2642`) | código | M |
| Dónde se funde por TEXTO, y por qué se queda | `FundirPuertas` (`SurfaceMapTools.cs:146-156`) descarta una puerta del terreno si UIA ya nombró esa etiqueta —**a propósito y por la promesa 183**: UIA ve los botones y campos del Pane de SAP y el terreno los lee por Scripting, así que el mismo elemento físico llega por los dos caminos; su fixture exige que «Triage» (`sap:…#tbbtn=ZMEDTRIAGE`, `GuiGridBoton`) desaparezca y `fundido.Count == 2` (`Contrato.cs:7117-7145`)—; los menús se deduplican por etiqueta + `MenuItem` en los dos caminos del lector (`UiaReader.cs:215-216` con caché, `:461-462` nodo a nodo, donde **no hay identidad**). **0 sitios a cambiar** | `grep`; fixture | M |
| Identidad vacía | `IdentidadDe` devuelve «» si el `RuntimeId` no vino en la petición (`UiaReader.cs:226-230`); el camino nodo a nodo no la trae nunca; el 21-09/22-09 el lector cayó a nodo a nodo **43** veces (41 `LockApp`, 2 `claude`) | `UiaReader.cs`; log, línea `lector:` | M |
| Cribas de «qué es una puerta» | **3 en el cliente**: `PuertasDeAhora` (con nombre, ni `text` ni `image`, `SurfaceMapTools.cs:174-176`), `SinEtiquetasDeControles` (el latido y `MirarOtraVez`: conserva un Text suelto y solo descarta el Text que duplica a un control, `MapaVivo.cs:590-602, :619`) y `UiaReader.Actionable` (`:45`); **1 en `windows-graph`**: `UiaSurface.Interactive` (`:220`). Las dos primeras tienen propósitos distintos (qué se ofrece a un decisor · qué está en la pantalla para el mapa) y se aplican hoy sobre listas distintas | `grep` | M |
| Edad de la observación al llegar a la compuerta | **sin medir**: es el dato del que sale `Observacion.VigenciaMs`. Provisional 1.000 (patrón nº8); la spec 040 midió que la ubicación cambia 405–510 ms después de un clic que navega, así que 1 s no es una cifra de este camino | — | — |
| Jev en un paso | 322–350 ms en caliente; 1.066 en frío. El 21-09 no hubo ninguna decisión de Jev («sin U_DECISOR: decide Luna») | anatomía §2-3; informe §2.B | M |

**Lo que NO se pudo medir, y se dice:**

- **SAP.** 0 líneas `sapgui://` en los 17 logs de esta máquina. `ReadFields` de SAP cuesta ~5 s en el triage
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
pasar a no mirar nunca sin que ningún test lo note (la 264 cuenta miradas: hay que seguir contándolas, y
por eso se reescribe en vez de dejarla falsa); un barrido compartido que devuelva otra huella que la de
antes rompería en silencio los workflows grabados; y un proyector «fuera del hilo» sin medida sería la
mejora 1 del informe hecha por fe.

La frase falsa hoy y verdadera después, para cada una, está en la tabla de promesas. **Ninguna línea de
producción entra antes que la promesa que la juzga.** Cada fase empieza con el contrato ROTO y termina INTACTO.

## La regla

**Leer para el mapa es publicar; reutilizar es pedir por la misma ventana y el mismo dónde, con edad;
accionar invalida las dos memorias.** En concreto:

1. **Una observación con versión y con crudos** (`windows-client/src/Uia/Observacion.cs`, nuevo; vive en `Uia/`
   porque `Navigation/` ya depende de `Uia/` en 3 archivos —`ClickWatcher`, `Interrupcion`,
   `SurfaceNavigator`— y no se abre ninguna dependencia nueva):
   `Observacion(Version, Hwnd, Donde, Pantalla, LeidaEn, MsDeLectura, ComoSeLeyo, Elementos, Completa)` y
   `ElementoVisto(Selector, Etiqueta, Tipo, Caja, Identidad)`. `Elementos` son **los crudos** de la lectura,
   **sin criba ni tope**: cada consumidor aplica la suya (regla 7). `Identidad` es el `RuntimeId` que la
   petición trajo (298), y **vacía significa «no vino», no «la misma»**. `Caja` es geometría **leída**
   (`Bounds`), nunca estimada. `Hwnd` es **la ventana que se leyó**, tal como la devolvió quien leyó.
2. **Quien lee para el mapa, publica; `UiaReader` no cambia.** La publicación vive en `SurfaceMapTools`,
   detrás del gancho `Lee` (por defecto `_lector.Read()`, que devuelve además el hwnd que leyó), y no dentro
   de `UiaReader.Read(hwnd)`: la 297/298 se quedan byte a byte, la arquitectura §7 y la 047 dicen «nadie
   toca `UiaReader`», y la rama de Jose no se cruza ahí. Consecuencia dicha: **los tres lectores de
   `FaceWindow` (latido `:515-517`, `MirarOtraVez` `:5636`, `ObservarLaVentanaDeTrabajo` `:5662`) NO
   publican** —`Ui/` está vetado en este encargo— y el contrato no puede juzgarlos; lo que sí puede es contar
   en la línea del paso cuántas lecturas fueron nuevas y de dónde (nivel 4).
3. **Quien puede reutilizar, pide por la misma identidad:** `Observatorio.Reciente(hwnd, donde, edadMaxMs)`.
   El `hwnd` de la petición sale de la **misma función** que usa `Lee` para elegir qué leer (gancho
   `VentanaQueLeeria`, por defecto `AppAligner.VentanaDelUsuario`), y el `donde` es el `aqui` de `_where()`:
   los dos lados de la comparación salen del mismo camino (aprendizaje nº16). La piden `PuertasDeAhora`,
   señalar y el inventario pegado, los tres a través de un método propio (`VistaReciente()`) declarado lejos
   del hunk `:2147` de la rama de Jose. Más vieja que `Observacion.VigenciaMs`, otra ventana, otro dónde o
   invalidada → se lee, la nueva reemplaza a la vieja y la cuenta dice **por qué** no se reutilizó
   («otra ventana h1 → h2», «otro dónde», «vieja N ms», «invalidada: accionar»).
   **`VigenciaMs` es provisional (1.000, patrón nº8)** y se fija con la medida propia de este camino: la
   edad de la Vista al llegar a la compuerta, por paso, en el nivel 4 (p95 + margen), como hace B con su
   techo. Es constante con nombre para que el nivel 4 la discuta con números.
4. **Accionar invalida las dos memorias.** `Take` y `Type` llaman a `Observatorio.Invalida(porque)` en cuanto
   la mano vuelve, y esa misma llamada olvida también la ubicación memorizada de la regla 10 (la regla de
   `_dondeTrabajo.Olvida()`, promesa 246, que hoy ya existe para la ventana de trabajo). `MapaVivo.MirarDonde`
   invalida al cambiar de sitio y al atribuir un clic. El siguiente que pida, lee.
5. **El paso lleva su vista.** `RecorrerSegunElNucleo.Paso` gana `Vista` (una `Observacion` opcional). En
   `EsperarloVivo`, si la vista es más fresca que `VigenciaMs` y de esta misma pantalla: si es **completa**,
   la compuerta le cuenta al núcleo lo que vio —`Grafo.Observar(aqui, MapaVivo.LoQueEsPuerta(crudos))`,
   la criba del latido (`SinEtiquetasDeControles`) hecha pública y aplicada sobre los **crudos**, así que un
   Text suelto que el latido daba por vivo sigue vivo y la `Huella` de la 299 coincide con la que
   `MirarOtraVez` produciría— y sigue por el camino de siempre, que ahora la encuentra viva sin mirar; si es
   **de uno** (la respuesta de preguntar por selector), la puerta se da por viva con `Grafo.Recordar` y **sin
   tocar la lista de vivos** de esa ubicación. En los dos casos el diario dice que se usó lo que el paso
   acababa de leer y cuántas miradas se pagaron: **0**. Sin vista, o vieja, o de otra pantalla: la 264 y la
   299 tal cual. **La 264 se reescribe** (abajo) para que su enunciado siga siendo literalmente cierto.
6. **Selector primero, solo cuando el grafo no la tiene viva.** En `Take`, si `exit` es un selector de UIA
   (`UiaSelector.Owns`), hay ventana de trabajo (`VentanaDeTrabajo`, gancho que ya existe en `:1795`) **y
   `PuertasVivas(aqui)` no contiene ese selector**, se pregunta por ese elemento (`PreguntaPorSelector(hwnd,
   selector)`, gancho con implementación por defecto en `UiaSurface.Preguntar`: un `FindFirst` por la
   condición del selector con caché de Name/ControlType/BoundingRectangle, 3 propiedades en una petición, y
   devuelve **cómo buscó**). Está → `Vista` de uno, y la mano pulsa como hoy (una consulta de 60–107 ms en
   lugar de la lectura completa de 232–272 ms que hoy paga `MirarOtraVez`; la mano resuelve después, como
   hoy). Ya viva en el grafo → **ni se pregunta ni se lee**, exactamente como hoy (`:438`). No está, no es de
   UIA, o no hay ventana → **`Take` no lee nada**: la compuerta mira como hoy. La cuenta y el log dicen cuál
   de los **tres** caminos y cuántos ms. **SAP no entra**: sus selectores resuelven por su API y su compuerta
   ya no mira (`MirarOtraVezLaVentana` devuelve false en `sapgui://`).
7. **Las candidatas, con caja, por identidad, y el terreno como la 183 manda.** `PuertasDeAhora` sale de la
   observación: aplica su criba de siempre (con nombre, ni `text` ni `image`) sobre los crudos; UIA aporta
   candidatas con caja e identidad; el terreno (`PuertasVivas`) y los campos de SAP aportan candidatas sin
   caja, y se dice. **`FundirPuertas` no cambia**: el terreno se funde con UIA por la etiqueta que UIA ya
   nombró, porque en SAP el mismo elemento físico llega por los dos caminos y ofrecerlo dos veces sería
   exactamente el «Analytics 0,91/0,09» de TipTour. Dentro de UIA nunca se funde por texto: dos «Detalles»
   con distinto selector son dos candidatas (ya lo son hoy, y así se queda). La deduplicación por
   `RuntimeId` es la que el lector ya hace (298); la candidata la hereda y **una identidad vacía nunca
   funde**. Los ids numerados que van al decisor y la lista de `map_what_i_see` se construyen de la misma
   lista, en el mismo orden (285). La tupla `(Selector, Etiqueta, Tipo)` que hoy ven `Puertas`, `PuertasVivas`
   y los ayudantes del contrato **no cambia**: la caja y la identidad van en una lista paralela `Candidatas`
   de la que la tupla se deriva, para no tocar los ayudantes de 284–292.
8. **El barrido del reproductor es uno, y se puede juzgar.** `UiaSurface` gana `BarridoUia` (genérico y puro,
   como `UiaReader.Recoge<T>`): recorre una vez, captura por nodo lo que los tres consumidores leen hoy de
   `.Current` (nombre, `AutomationId`, tipo, caja, visible, habilitado, ruta) y de ese único recorrido salen
   los tres derivados —campos, cuántos y huella— **iguales a los de hoy** (mismo filtro `Interactive`, visibles y
   habilitados, 40 niveles, 300 elementos, huella por `AutomationId|tipo` o `@ruta|tipo`, nunca texto).
   El recorrido real es inyectable (`UiaSurface.Barre`, estático; null = `Walk` de verdad) y el reloj también,
   para que el contrato juzgue **los tres métodos reales** y no un ayudante. Vigencia con nombre
   **`BarridoUia.VigenciaMs = 100`, menor que `SurfaceReadiness.PollMs = 120`**: un sondeo de carga nunca
   recibe el árbol del sondeo anterior (la versión anterior decía 250 «menor que 120 × 2», que es falso y
   además lo contrario de lo que argumentaba). Lo que ahorra, dicho: como mucho un `Walk` por paso del
   reproductor (el primer sondeo cuando el elemento no está listo), y nada en el caso común; la línea
   «barrido nº N: M elementos en X ms · servido a ReadFields/ReadinessCount/StructureFingerprint» (`Log`,
   tag `uia`) es lo que lo mide.
9. **Neo4j, medido antes que movido; el contador y el aviso son dos cosas.** `ProyectorNeo4j.Proyectar(grafo,
   desde)` cronometra cada envío y lo apunta en un **contador** (ms, `desde` —«índices», «ubicación», «latido»,
   «cruce»—, ok o fallo) sin escribir una línea por fallo; un **resumen por minuto** dice cuántos envíos, cuánto
   sumaron, cuál fue el más lento y cuántos fallaron. La **única línea limitada** es «Neo4j no responde»: una
   vez por minuto, no por proceso. `Cuenta` y el reloj entran **por el constructor**, así que la caída de
   `AsegurarIndices()` en el arranque **sí** se dice (es el primer aviso del minuto y el primer envío del
   contador); la firma vieja `ProyectorNeo4j(url, usuario, clave)` se conserva para el contrato del núcleo.
   **Solo si esa medida dice que bloquea**, la 367 (reservada) lo saca del hilo.
10. **El «dónde» se pregunta una vez por instante, olvida al accionar y arranca en sombra.**
    `MemoriaDeUbicacion` (pura, en `Uia/SurfaceLocator.cs`) sirve la última ubicación calculada mientras la
    ventana de delante y su título sean los mismos, tenga menos de 400 ms **y nadie haya accionado desde
    entonces**; con SAP delante se calcula siempre (la excepción que `Probe` ya hace en `:246`). Se cablea bajo
    `DondeEstoy()` (`:147`) **en sombra por defecto**: calcula siempre y cuenta cuántas «habrían salido de
    memoria». `U_DONDE_MEMORIA=si` la enciende. La medida del nivel 4 —«dónde: N calculadas · M de memoria · K
    en sombra» por minuto— decide si se enciende por defecto en el PR o si la 369 se queda en sombra; si se
    queda, el PR lo dice con el número. Lo que no se sabe hoy es cuántas de las 25 llamadas caen dentro de
    400 ms de otra: sin ese dato, encenderla sería una promesa que no compra nada.
    **Cómo quedó (fase 8, 2026-09-22):** «nadie ha accionado» es un número, no un evento:
    `Observatorio.Invalida` sube `Observatorio.Invalidaciones` y la memoria sirve solo si sigue siendo el que
    había al calcular (y si nadie llamó a su `Olvida()`); los dos se toman ANTES de calcular, así que un cálculo
    en vuelo cuando se acciona nace caducado. `InvalidaLoQueNoSeaDe` no lo sube: la llama el mapa vivo al notar un
    cambio de sitio, y para notarlo acaba de calcular la ubicación nueva. **En sombra se cuenta además cuántas de
    las que habrían salido de memoria traían otra respuesta** («K en sombra (J con otra respuesta)»), y lo
    recordado no se renueva en sombra —la memoria encendida tampoco lo habría renovado—: sin J, K no dice si
    encenderla compra lecturas o sitios viejos (patrón nº8).

## Las promesas

El enunciado es el que va **literalmente** en `tests/ContratoDelGrafo/Contrato.cs`. Se juzgan sin pantalla,
sin SAP y sin TypeSafe: con árboles de mentira, lectores falsos que cuentan, relojes inyectados y un
proyector apuntado a `127.0.0.1:1`. **Lo que el contrato NO puede juzgar está en su propia sección**, abajo.

| # | Promesa | Fase |
|---|---|---|
| 361 | leer la ventana en foco para el reproductor es UN barrido: ReadFields, ReadinessCount y StructureFingerprint salen del mismo recorrido —pedidos dentro de la vigencia del barrido, el árbol se recorre una vez y sirve a los tres— y recogen lo mismo que hoy: interactivos, visibles y habilitados, hasta 40 niveles y 300 elementos, con la huella por id o ruta más tipo y nunca por texto; la vigencia es menor que el paso del sondeo de carga, así que un sondeo nunca recibe el árbol del sondeo anterior; accionar lo invalida, así que lo barrido antes de ejecutar un paso no se sirve después; y el log dice cuántos barridos hubo, cuánto costó cada uno, a quién sirvió y por qué no se reutilizó uno invalidado | 1, 10 |
| 362 | una lectura se comparte por la misma ventana que se leyó: leer para el mapa publica una observación con versión —la ventana que se leyó, dónde, cuándo, cuánto costó, cómo se leyó y sus elementos crudos con selector, etiqueta, tipo, caja e identidad, sin criba ni tope—; quien puede reutilizar la pide por esa misma ventana y ese mismo dónde, con una edad máxima, y la recibe sin leer; más vieja, de otra ventana o de otro dónde, o después de accionar —con cualquier mano: pulsar, escribir, desplazar, ir, abrir, desbloquear, un batch o una skill—, se lee de nuevo, la nueva reemplaza a la vieja y se dice por qué no se reutilizó; y el mapa cuenta por paso cuántas lecturas fueron nuevas y cuántas reutilizadas | 2, 10 |
| 363 | lo que el paso acaba de leer, la compuerta no lo vuelve a mirar: si el paso lleva la observación completa de su pantalla, más fresca que la vigencia, la compuerta le cuenta al núcleo lo que esa observación vio —con la misma criba que el latido, sin leer— y da por viva la puerta que estaba en ella, con 0 miradas y diciéndolo; un texto suelto que el latido daba por vivo sigue vivo; si la puerta no estaba en la observación, o la observación es de otra pantalla o más vieja que la vigencia, mira como hoy; en SAP no cuenta —SAP se lee por su API—, así que lo que UIA ve de su Pane nunca deja muerta una puerta de SAP; y nunca se mira más veces que antes (264 reescrita, 299 intacta) | 3, 10 |
| **264** (reescrita, sin reciclar) | vivo se juzga mirando AHORA: si el mapa no tiene una puerta como viva, antes de rendirse la compuerta vuelve a mirar la ventana —salvo que el paso traiga una observación de ESTA pantalla más fresca que la vigencia, en cuyo caso esa observación cuenta como la mirada y se dice (2026-09-22: hasta hoy no había observación que traer)—; si al mirar aparece, se pulsa en el acto; si no aparece ni mirando, se dice que no se ve, como hasta hoy | 3 |
| 364 | un paso que ya trae el selector y cuya puerta el grafo no tiene viva no inventaría la ventana: se pregunta solo por ese elemento en la ventana de trabajo; si está, se pulsa con esa respuesta como observación de uno —sin leer la ventana entera y sin tocar la lista de vivos del núcleo—; si el grafo ya la tenía viva, no se pregunta ni se lee, como hoy; «viva» y «dónde» son los de la ubicación de trabajo, la misma con la que juzga la compuerta; si no está, o el selector no es de UIA, o no hay ventana de trabajo, el paso no lee nada y la compuerta mira como hoy; un selector de SAP ni se sitúa, ni pregunta, ni lleva observación; y la cuenta y el log dicen cuál de los tres caminos se usó, cuántos ms costó situarse y cuántos preguntar | 4, 10 |
| 365 | las candidatas que van al decisor salen de esa misma lectura, con selector, etiqueta, tipo y caja; se ofrecen vivas —con nombre y con geometría leída— y sin duplicar por identidad: dentro de una lectura por su RuntimeId, y una identidad vacía nunca funde; con el terreno se funde como la 183 manda —por la etiqueta que UIA ya nombró, porque en SAP el mismo elemento llega por los dos caminos—; dos puertas de UIA con el mismo nombre y distinto selector son dos candidatas; una del terreno o de SAP sin caja leída se ofrece sin caja y se dice; los ids ofrecidos y la lista de map_what_i_see salen de la misma lista en el mismo orden (285); y el log dice cuántas se ofrecieron de cuántas, cuántas sin caja, cuántas sin identidad y cuánto costó | 5 |
| 366 | proyectar en Neo4j se mide y se dice: cada envío —los índices del arranque, desde la ubicación, desde el latido o desde un cruce— deja su coste y su resultado en un contador, y un resumen por minuto dice cuántos hubo, cuánto sumaron, cuál fue el más lento y cuántos fallaron; la única línea limitada es «Neo4j no responde», que se dice una vez por minuto y no una vez por proceso, también cuando cae en el arranque, porque quien lo cuenta entra por el constructor | 6 |
| 367 | **reservada, condicionada al nivel 4 de la 366**: proyectar no bloquea el hilo de la ubicación —con Neo4j caído Proyectar vuelve en menos de 50 ms, hay una sola escritura en vuelo y tras un fallo no se reintenta en 30 s—. No se escribe en el contrato hasta que la medida diga que hace falta; el número no se recicla | — |
| 368 | cada paso decidido se publica como un evento tipado —dónde, objetivo, las candidatas con su caja, los ids ofrecidos en su orden, la decisión tal como la devolvió el decisor, cuánto costó leer, decidir y pulsar, y el paso que contó el tramo—, también cuando no se acciona; sin suscriptor no cuesta nada ni cambia nada; y lo que el evento lleva es exactamente lo que se ofreció (285) | 7 |
| 369 | dónde estoy se pregunta una vez por instante, y accionar lo olvida: mientras la ventana de delante y su título sean los mismos, la respuesta tenga menos de 400 ms y nadie haya accionado desde que se calculó, se sirve la última calculada sin volver a leer; cambia la ventana o el título, pasan los 400 ms, o se acciona, y se calcula de nuevo; con SAP delante se calcula siempre; en sombra se calcula siempre y solo se cuenta; y el log dice por minuto cuántas veces se calculó, cuántas se sirvieron de memoria y cuántas habrían salido de memoria en sombra | 8 |
| 370 | **reservada**: los lectores de fondo se suscriben a la observación o se pausan durante un tramo (`RastroDelCursor`, `UiInspector`, `Probe`, `MirarDonde`), y SAP guarda los campos del dynpro por pantalla. Requiere `Ui/` (vetado en este encargo) y una sonda en el hospital. El número no se recicla | — |

**La que de verdad cierra el asunto es la 362**: sin observación compartida, 363, 364, 365 y 368 son
parches en sitios distintos que volverían a desincronizarse (dos caminos para enumerar el mismo terreno,
que es justo lo que la 285 prohíbe). Y **la 183 no se toca**: la 365 la cita como regla, no la contradice.

### Con qué se juzga cada una, y el sabotaje de una línea

| # | Cómo se juzga (sin pantalla) | Sabotaje que la pone roja |
|---|---|---|
| 361 | `UiaSurface.Barre` inyectado con un árbol de mentira que **cuenta llamadas** y `UiaSurface.Reloj` inyectado; sobre la misma superficie se piden `ReadFields`, `ReadinessCount` y `StructureFingerprint` dentro de la vigencia → `Barre` se llamó **1** vez y los tres resultados son coherentes (tantos campos como cuenta); reloj + `VigenciaMs` + 1 → `ReadinessCount` → **2**; `VigenciaMs < SurfaceReadiness.PollMs`; la huella es `Fingerprints.Of` de `id\|tipo` o `@ruta\|tipo` (dos árboles que solo difieren en nombre dan la misma; distinto tipo, distinta); un nodo `IsOffscreen`, uno deshabilitado y un `Text` no cuentan; el `Cuenta` capturado trae «barrido nº1 … nº2 … servido a»; **(fase 10)** con `UiaSurface.HayQueParar = () => true` (Execute vuelve en el acto, sin tocar la pantalla): `ReadinessCount` → `Execute` → `ReadinessCount` en el mismo instante → **2** barridos, y el log dice «… no se reutilizó: invalidado al ejecutar …» | en `ReadinessCount`, llamar a `Barre` directamente sin pasar por `BarridoUia.Toma`: «1 barrido para los tres» falla; **(fase 10)** que `BarridoUia.Toma` sirva una captura invalidada (quitar `invalidada == null &&`): «2 barridos» falla. Quitar solo la invalidación al ENTRAR en `Execute` no la pone roja, y se dice: la del `finally` invalida la misma captura en un solo hilo; la de la entrada cubre a quien barra MIENTRAS la mano actúa, que el contrato no alcanza |
| 362 | `Observatorio` con reloj inyectado: `Publica` sube la versión; `Reciente(h, aqui, 1000)` devuelve la misma dentro de la vigencia y null después; `Invalida` deja null; otro `h` → null; otro `aqui` → null; y `SurfaceMapTools` con `Lee` falso que cuenta y devuelve el hwnd que «leyó», `VentanaQueLeeria = () => 1` y `_where` mutable en el arnés: dos `map_what_i_see` seguidos → 1 lectura; `VentanaQueLeeria = () => 2` → la siguiente lee y la cuenta dice «reutilizada: no (otra ventana 1 → 2)»; `_where` cambia de `Id` → lee («otro dónde»); reloj + `VigenciaMs` + 100 → lee («vieja»); un `map_take` con mano falsa → el siguiente lee («invalidada: accionar»); la cuenta del paso lleva «lecturas: 1 nueva · 1 reutilizada»; **(fase 10)** con `Desplaza` falso, un `map_scroll` → la siguiente lee («invalidada: accionar (map_scroll …)»); con `PorElNucleo` falso, un `map_go_to` → lee («… map_go_to …»); y un `Recorre` de `RecorrerSegunElNucleo` que pulsa → lee («… pulsar …») | en `Reciente`, ignorar el hwnd (`return _ultima;`): «otra ventana → se lee» falla; **(fase 10)** quitar la invalidación tras `_pulsar.Pulsa` en `RecorrerSegunElNucleo`: «pulsar dentro de un batch invalida» falla |
| 363 | `RecorrerSegunElNucleo` con `MiraOtraVez` falso que cuenta y un grafo que tiene vivos en `aqui` un Text suelto «Total» y un «Guardar» (Button) pero NO la puerta X: `Paso("uia:name=X;ct=Button") { Vista = observación completa con X (Button), «Total» (Text), «Guardar» (Button) y «Guardar» (Text) }` → se pulsa X, **miradas = 0**, el diario dice que usó lo que el paso acababa de leer, «Total» sigue vivo y el «Guardar» Text **no** entró (la criba del latido); el mismo paso sin `Vista` → miradas ≥ 1 como hoy; con `Vista` más vieja que `VigenciaMs` → miradas ≥ 1; con `Vista` de otra pantalla → miradas ≥ 1; y las miradas nunca superan las de hoy en ningún caso. El fixture de la 264 sigue **byte a byte** (no inyecta Vista) y su enunciado gana la cláusula. **(fase 10)** En `sapgui://QAS/NWP1/…` con `sap:…#tbbtn=NV44` y un campo `sap:` vivos, y un paso a NV44 con una `Vista` completa de UIA (el Pane y un botón `aid=200`): se pulsa NV44 con **0 miradas**, las dos `sap:` siguen vivas, ninguna `uia:` entra, y el diario dice «SAP se lee por su API» | en `EsperarloVivo`, no leer `paso.Vista`: «miradas = 0» falla; y aplicar la criba de `PuertasDeAhora` en vez de la del latido: «Total sigue vivo» falla; **(fase 10)** quitar la guarda de `sapgui://` en `PorQueLaVistaNoCuenta`: «en SAP la puerta de SAP viva se pulsa» falla |
| 364 | `SurfaceMapTools` con `VentanaDeTrabajo = () => 1`, `PreguntaPorSelector` falso que cuenta, `Lee` falso que cuenta y `PuertasVivas` falso: (i) `PuertasVivas` sin A, `map_take exit=uia:name=A;ct=Button` → preguntas 1, **lecturas 0**, la mano recibió el paso con `Vista` de uno, la cuenta dice «pregunté por … está · N ms»; (ii) `PuertasVivas` con A viva → preguntas **0**, lecturas 0, cuenta «viva en el grafo, sin preguntar»; (iii) la pregunta contesta «no está» → preguntas 1, **lecturas 0**, sin `Vista`, cuenta «pregunté … no está · N ms; la compuerta mira»; (iv) `exit=Etiqueta` → 0/0; (v) `exit=sap:…` → 0/0; (vi) sin `VentanaDeTrabajo` → 0/0. Y en `RecorrerSegunElNucleo`: paso con `Vista` de uno y grafo sin la puerta → `Recordar`, miradas 0, `DesdeAqui` conserva los vivos que tenía. **(fase 10)** Con `_where` en Chrome y `DondeDeTrabajo = () => A`: (vii) viva en A → 0 preguntas, 0 lecturas, «viva en el grafo», `DondeDeTrabajo` 1 vez y `VentanaDeTrabajo` **0** veces, «situarse N ms»; (viii) no viva y está → la `Vista` de uno lleva `Donde == A`, no Chrome; (ix) `exit=sap:…#tbbtn=NV44` con una observación compartida fresca → sin `Vista`, 0 veces `DondeDeTrabajo`, 0 `VentanaDeTrabajo`, 0 preguntas, 0 lecturas, y el log dice «selector de SAP … su API» | en `Take`, preguntar también cuando está viva (quitar la condición de `PuertasVivas`): el caso (ii) «preguntas 0» falla; **(fase 10)** sacar el `aqui` de `_where()` en vez de `DondeDeTrabajo`: (vii) «viva en el grafo» falla |
| 365 | `Lee` falso con «Detalles» (Button, caja, id `1.2`), «Detalles» (Hyperlink, caja, id `1.3`), dos elementos con id «» y distinto selector, y dos con el mismo id `7.7`; `PuertasVivas` con «Detalles» (RadioButton, `sap:`) y «Guardar» (Button, `sap:`): los dos «Detalles» de UIA salen (distinto selector); el «Detalles» del terreno **no** (183, y `ElTerrenoCompletaLoQueUiaNoVe` sigue verde); «Guardar» sale con `Caja.IsEmpty`; las dos con identidad «» salen **las dos**; el `7.7` sale una vez; los ids que recibe el decisor son los de `map_what_i_see` en su orden; el log trae «candidatas: 6 de 8 (5 con caja · 1 sin caja · 2 sin identidad) en N ms» (recontado al escribir el juez, 2026-09-22: 6 crudos de UIA + 2 del terreno = 8 vistas; sale 1 «Repetido» de los 2 con `7.7` y no sale el «Detalles» del terreno = 6 ofrecidas, 5 con caja) | tratar la identidad «» como clave al deduplicar: «las dos con «» salen» falla |
| 366 | `new ProyectorNeo4j("http://127.0.0.1:1", cuenta: captura, reloj: falso)`: el constructor (índices) falla y deja **1** línea «Neo4j no responde … · desde índices» —arranca el minuto—; `g.Observar(…)`; `Proyectar(g, "latido")` → 0 líneas nuevas, `Envios == 2`, `Fallidos == 2`; `g.Observar(…)` otra vez (el grafo tiene que cambiar de versión: `:170` corta si no); reloj +61 s; `Proyectar(g, "ubicación")` → 1 línea más; `Resumen()` trae «envíos: 3 · N ms · más lento M ms (índices) · 3 fallidos»; `UltimaProyeccionMs ≥ 0`; y `new ProyectorNeo4j(url, usuario, clave)` sigue construyendo; **(fase 10)** y entre las líneas capturadas está la del minuto: «en los últimos 61 s: envíos: 2 · N ms · más lento … · 2 fallidos» | volver a `_yaAvise` una vez por proceso: «+61 s → 1 más» falla; **(fase 10)** dejar vacío el `decir.Add` de `CerrarElMinutoSiToca`: «el resumen POR MINUTO sale por la cuenta» falla |
| 368 | `MapaParaDecidir` + `AlDecidir += e => …`: tras `map_decidir`, `e.Ofrecidas` es lo que recibió el decisor, `e.Candidatas.Count == 3` con sus cajas, `e.Decision.Puerta == "2) …"`, `e.Ms.Leer/Decidir/Pulsar ≥ 0`, `e.Paso.Actuo`; con un decisor que dice No, el evento también sale y `e.Paso.Actuo == false`; sin suscriptor, la cuenta de `map_decidir` es byte a byte la de hoy | disparar el evento solo cuando se acciona: «decisor que dice No» falla |
| 369 | `MemoriaDeUbicacion` pura con reloj inyectado: `Sirve(h, "t", esSap:false)` calcula la primera; +200 ms con el mismo `(h, "t")` → de memoria; **`Olvida()` (accionar) y +200 ms con el mismo `(h, "t")` → calcula**; cambia el título → calcula; +400 ms → calcula; `esSap:true` → calcula siempre; con `EnSombra = true` cada llamada calcula y `HabrianSidoDeMemoria` sube; el `Cuenta` capturado trae «dónde: N calculadas · M de memoria · K en sombra»; **(añadido en la fase 8, antes que su código)** en sombra, si lo recién calculado difiere de lo que habría servido, se devuelve lo calculado y la cuenta dice «1 con otra respuesta»; y `Observatorio.Invalida` —la llamada de `Take` y `Type` al volver la mano— también la olvida (con control: sin accionar, a los 100 ms sale de memoria); **(fase 10)** la memoria se construye con `cuenta`: dentro del minuto no dice nada, y a los 60,5 s la primera pregunta cierra el minuto con «dónde: N calculadas · M de memoria · K en sombra (1 con otra respuesta) … en los últimos 60 s» | no olvidar al accionar (`Olvida` vacío): «tras accionar se calcula» falla; **(fase 10)** dejar vacío el `decir.Add` de `CerrarElMinutoSiToca`: «pasado el minuto la cuenta dice…» falla |

### Qué juzga el contrato y qué solo la máquina

Cuatro costuras viven donde está el ahorro y el contrato no las alcanza sin pantalla. Para cada una, o hay
inyección juzgable, o hay una línea de log con nombre que el PR **tiene que pegar con horas**:

| Costura | Contrato | Solo la máquina (línea del nivel 4) |
|---|---|---|
| (a) `UiaSurface.ReadFields/ReadinessCount/StructureFingerprint` volviendo a recorrer cada uno | **sí**: `Barre` inyectado, «1 barrido para los tres» sobre los tres métodos reales (361) | «barrido nº N … servido a …» por paso del reproductor |
| (b) `SurfaceLocator.DondeEstoy()` sin pasar por `MemoriaDeUbicacion` (`Ahora()` es privado, `:155`) | solo la clase pura (369) | «dónde: N calculadas · M de memoria · K en sombra» por minuto, en reposo y durante un tramo |
| (c) `UiaSurface.Preguntar` haciendo un `FindAll` del árbol entero | solo `Take` con `PreguntaPorSelector` falso (364) | «pregunté por … (FindFirst) · está/no está · N ms», y que en ese `map_take` no salga «leí la ventana» |
| (d) los tres lectores de `FaceWindow` publicando o no | **no** (`Ui/` vetado; no publican) | «lecturas: N nuevas (de: paso · trabajo · compuerta) · K reutilizadas · reutilizada: no (causa)» por paso |

## Las fases

Una fase = un commit que pone verde UNA promesa sin romper las anteriores. Toda la spec vive en esta rama;
no se mergea fase a fase.

| Fase | Qué | Promesa | Archivos | Terminado |
|---|---|---|---|---|
| 0 | las promesas, en ROJO; la 264 reescrita | 361–366, 368, 369; 264 | `tests/ContratoDelGrafo/Contrato.cs` | el contrato dice `CONTRATO ROTO: 8 promesa(s)` y cada una es `PENDIENTE` por el nombre que pide (`BarridoUia`/`UiaSurface.Barre`, `Observatorio`, `Paso.Vista`, `PreguntaPorSelector`, `Candidatas`, `ProyectorNeo4j(url, cuenta, reloj)`, `AlDecidir`, `MemoriaDeUbicacion`); la 264 con su cláusula nueva y su fixture byte a byte sigue verde; 343 y las anteriores siguen verdes |
| 1 | el barrido del reproductor es uno | 361 | `windows-graph/src/Surfaces/UiaSurface.cs` (+ `BarridoUia.cs` junto a `Fingerprints.cs`; `Barre` y `Reloj` inyectables; `Describe` lee lo capturado) | 361 verde; **3 sitios** (`:470, :493, :510`) pasan por `BarridoUia.Toma`; `WorkflowPlayer`/`SurfaceReadiness` sin tocar |
| 2 | la observación compartida | 362 | **nuevo** `windows-client/src/Uia/Observacion.cs`; `Mcp/SurfaceMapTools.cs` (`Lee` devuelve el hwnd leído, `VentanaQueLeeria`, `VistaReciente()` declarado lejos de `:2147`, `PuertasDeAhora` pide y publica, `Invalida` en `Take` y `Type`, cuenta de lecturas en `tiempos`); `Navigation/MapaVivo.cs` (`Invalida` al cambiar de sitio y al atribuir clic). **`Uia/UiaReader.cs`: 0 líneas** | 362 verde; 297/298 intactas sin tocar su archivo; **3 sitios** de lectura del paso en `SurfaceMapTools` reutilizan; los 2 de `FaceWindow` **no publican**, dicho en el commit |
| 3 | la compuerta no vuelve a mirar | 363, 264 | `Navigation/RecorrerSegunElNucleo.cs` (`Paso.Vista`; `EsperarloVivo(paso)`); `Navigation/MapaVivo.cs` (`LoQueEsPuerta` pública = `SinEtiquetasDeControles`); `Mcp/SurfaceMapTools.cs` (`Take` adjunta la vista); `Contrato.cs` (enunciado de la 264) | 363 verde; 264 reescrita verde con su fixture intacto; 299 intacta (mismo número de miradas sin vista); **3 cribas** contadas en el commit: la del latido se hace pública, la de la lista se aplica sobre los mismos crudos, `Actionable` no cambia |
| 4 | selector primero, solo sin puerta viva | 364 | `windows-graph/src/Surfaces/UiaSurface.cs` (`Preguntar(hwnd, selector)`, público, devuelve cómo buscó); `Mcp/SurfaceMapTools.cs` (`PreguntaPorSelector`, `Take` con la condición de `PuertasVivas`) | 364 verde; 203 (homónimos con `which`) y 331 (nombre recortado) intactas: preguntar usa `UiaSelector` como `Resolve` |
| 5 | candidatas con caja, por identidad | 365 | `Mcp/SurfaceMapTools.cs` (`Candidatas`, la línea «candidatas:», fuera el `Contains("puertas vivas para")` de `:331`). **`FundirPuertas` no cambia. `UiaReader` no cambia** | 365 verde; 183, 285, 287, 288 intactas (misma tupla, mismo orden, misma fusión con el terreno); el commit dice «fusión por texto: 0 sitios cambiados, y por qué» |
| 6 | Neo4j medido | 366 | `nucleo/Grafo/ProyectorNeo4j.cs` (constructor con `Cuenta` y reloj; `Proyectar(grafo, desde)`; contador; aviso por minuto; `Resumen()`); `Navigation/MapaVivo.cs` (`_proyector` construido con `Cuenta`; los **4 sitios** de `Proyectar` pasan `desde`) | 366 verde; el contrato del núcleo (`nucleo/Contrato`) sigue igual: la firma vieja se conserva |
| 7 | el evento para quien pinta | 368 | `Mcp/SurfaceMapTools.cs` (`AlDecidir`, `PasoDecidido`) | 368 verde; D tiene qué consumir |
| 8 | el dónde con memoria, en sombra | 369 | `windows-client/src/Uia/SurfaceLocator.cs` (`MemoriaDeUbicacion` pura; bajo `DondeEstoy()`, en sombra por defecto, `U_DONDE_MEMORIA=si`); `Uia/Observacion.cs` (`Invalida` olvida también la ubicación) | 369 verde; 230 y 233 intactas: `Identificar` no pasa por la memoria; **el nivel 4 decide si se enciende por defecto**, con el número en el PR |
| 9 | sabotajes por diff, uno por promesa; `verificar.ps1`; `out\evidencia.md` | todas | — | ocho veredictos `CONTRATO ROTO` nombrando cada promesa, ocho diffs idénticos al restaurar, `CONTRATO INTACTO` al final |
| 10 | la revisión de la noche del 2026-09-22 (sección «Revisión de la noche», abajo): SAP no entra en la compuerta, cualquier mano invalida, el dónde de la 364 es el de trabajo, accionar invalida el barrido, y los dos «por minuto» se juzgan | 361, 362, 363, 364 (enunciados ampliados); 366, 369 (jueces reforzados) | `RecorrerSegunElNucleo.cs` (guarda `sapgui://`; `LaManoVolvio` tras pulsar, escribir y teclear); `SurfaceMapTools.cs` (`DondeDeTrabajo`, `Desplaza`, `VistaDelPaso` en orden de coste, invalidar en el despacho); `UiaSurface.cs` + `BarridoUia.cs` (accionar invalida la captura); **`FaceWindow.xaml.cs`: 2 líneas** (cablear `DondeDeTrabajo`) | las seis promesas verdes; un sabotaje por diff para cada una; `CONTRATO INTACTO` |

**Orden y por qué.** La 1 va primero porque no toca nada del camino de Jev y deja el patrón (barrido genérico
+ inyección + cuenta) que la 2 copia. La 2 antes que la 3, 4, 5 y 7 porque todas consumen la observación. La
6 y la 8 son independientes y van al final para que un rebase sobre A/B no las arrastre.

**Sitios con esta clase de error, contados (patrón nº5) y para el commit:** lecturas completas por paso, 5
(3 en `SurfaceMapTools` pasan por el observatorio; 2 en `FaceWindow` no, vetados); `Walk` en `UiaSurface`, 3;
fusión por texto, **0 a cambiar** (1 a propósito por la 183, 2 de menús sin identidad); cribas de puerta, 3 en
el cliente + 1 en `windows-graph`; `Proyectar` síncrono, 4; «dónde» sin memoria, 25 llamadas por 1 función;
aviso de Neo4j que nunca sale, 1; dos funciones para «la ventana de la persona», 2 (hallazgo, no se unifican
aquí).

## Cruces con ramas vivas, y quién va primero

| Archivo | Rama ajena | Sus líneas | Las de C | Orden |
|---|---|---|---|---|
| `Mcp/SurfaceMapTools.cs` | `origin/jose/ir-devuelve-la-pagina-asentada` (Jose; 5 commits, último `a52dee5` «se aparca»; su 335 «tras navegar se cuenta la página asentada» **no está en `main`**, donde la 335 es la de la voz: viva, no integrada) | `@@ -2056`, `@@ -2078`, `@@ -2147,10 +2154,35` (el inventario pegado: `mirar()`, `InventarioAsentado`, `_dondeDijoElUltimoInventario`, un «dónde» sacado del inventario) | fase 2: `PuertasDeAhora` `:165-197`, `Take` `:2586-2602`, `Type` `:2655+`; el inventario pegado y señalar llaman a `VistaReciente()`, declarado junto a `PuertasDeAhora` y **no** en `:2147` | **se habla con Jose antes del PR** (regla del repo); si su rama entra antes, C rebasa; su «dónde» del inventario y la memoria de la 369 se reconcilian entonces (accionar olvida, así que el inventario no lee un «dónde» viejo) |
| `Mcp/SurfaceMapTools.cs` | A (spec 046) | `:297-309` (cumplido por dato; `EsPeligrosa`) | fase 5 `:263-274` (numera con caja), `:331` | A primero; hunks a >20 líneas |
| `Mcp/SurfaceMapTools.cs` | B (spec 047) | `:2642` (`Mano` lleva `QueCambio`), `:2865-2926` | `Take` `:2586-2602`, a 40 líneas de `:2642`; `Type` `:2655+`, a 13 | B primero; el hunk de `Type` **es adyacente**: C rebasa y resuelve a mano |
| `Navigation/RecorrerSegunElNucleo.cs` | B | `:412-413` (`Huella` → compartida), `:317-341` (`LlegoDondeTocaba`), `Resultado` `:61-71` (`QueCambio`) | `Paso` `:43-53` (`Vista`), cabeza de `EsperarloVivo` `:398-440` (**adyacente** a `:412-413`) | B primero; C rebasa y resuelve a mano |
| `windows-graph/src/Surfaces/UiaSurface.cs` | A (`RealClick` ~`:1954-1984`, juez del LEFTDOWN), B (+1 estático junto a `VentanaExiste`, `:290`) | | fase 1 `:461-526` (+ `Barre`, `BarridoUia`), fase 4 (`Preguntar`) | hunks lejanos |
| `Navigation/MapaVivo.cs` | nadie | — | fases 2, 3, 6 | — |
| `Uia/UiaReader.cs` | **nadie** (297/298; arquitectura §7 y 047:151) | — | **0 líneas** desde esta revisión | — |
| `tests/ContratoDelGrafo/Contrato.cs` | A, B, D, y `main` (`5574148` metió 342 y 343 en el registro) | cada una su bloque `// ── Spec 04N` | bloque `// ── Spec 048` + la 264 en su sitio | conflicto textual seguro al final; se resuelve a mano sin tocar promesas ajenas |

## Lo que NO entra, y por qué

- **`FaceWindow.xaml.cs` y todo `Ui/`, salvo 2 líneas.** El encargo lo veta y la arquitectura dice que solo haya
  una feature de UI abierta (D espera a C). **Excepción de la fase 10, dicha:** `mcp.Map.DondeDeTrabajo =
  DondeTrabajo` (1 línea y su comentario, junto a `VentanaDeTrabajo`, lejos de las líneas de D): sin ella la 364
  decide con el dónde de delante y la compuerta con el de trabajo (aprendizaje nº16), y no hay forma de sacar
  `DondeTrabajo` de `FaceWindow` sin cablearlo. Consecuencias, dichas: `ObservarLaVentanaDeTrabajo` (`:5650-5671`) sigue
  leyendo por su cuenta (con su freno de 800 ms) y **no publica**; el latido (`:515-517`) tampoco;
  `MirarOtraVezLaVentana` no cambia (tiene que ser fresca por la 299); `_where` (`:434`) sigue siendo el
  crudo, con la memoria de la 369 por debajo. Las tres líneas que D o el dueño pueden cablear cuando entren
  a `Ui/`: `ObservarLaVentanaDeTrabajo` pidiendo `Observatorio.Reciente` antes de leer; el latido publicando;
  y `PuertasVivas`/`RecorrerPorElNucleo` con el paso que ya lleva vista.
- **`Uia/UiaReader.cs`**: 0 líneas. La publicación no vive en `Read`, y los deduplicados de menú (`:215`,
  `:461`) se quedan: el nodo a nodo no tiene identidad con la que sustituirlos.
- **`FundirPuertas`**: no cambia. La 183 manda y su fixture la juzga.
- **Unificar `VentanaDelUsuario()` y `DondeEstoy().Hwnd`**: dos funciones para la misma pregunta
  (aprendizaje nº16), una en `AppAligner` y otra en `SurfaceLocator`. Esta spec compara siempre por el mismo
  camino y deja el hallazgo; unificarlas toca `Ui/` y el latido.
- **`Decision/`** (calentar la conexión, `input_tokens`, un cliente por proceso: mejora 7): vetado; es de A o
  de otra rama.
- **`PulsarSegunElNucleo` y la espera tras pulsar**: es B (spec 047). Cuando B entre, su huella barata
  sale de la observación de la 362 —lo dice la arquitectura— y no al revés.
- **La 367** hasta que la 366 mida en el nivel 4 con Neo4j arriba y abajo. El informe ya avisa de que la
  mejora 1 «está medida en latidos, no en clics».
- **Encender la memoria del dónde por defecto** hasta que la sombra de la 369 diga cuántas llamadas compra.
- **Pausar `RastroDelCursor` y `UiInspector` en tramo** (370): `Ui/`.
- **El cuerpo sin duplicados** (mejora 18), **candidatas por relevancia** (19, roza la 285 «en su orden») y
  el **historial en el `state`** (8, choca con el caso 6 de la 292): se miden en sombra antes, y con el dueño.
- **SAP**: `ReadFields` por pantalla (mejora 14), `GetObjectTree` (15), la carrera del `Busy`. Sin sonda en el
  hospital no hay medida, y sin medida no hay promesa.
- **El `Contains("puertas vivas para")`** de `SurfaceMapTools.cs:331`: es redundante con `Mano.Intento`
  (que ya es `!r.Ambiguo`), así que quitarlo no cambia comportamiento; se quita en la fase 5 sin promesa
  propia, y se dice en el commit.
- **Unificar `Interactive` y `Actionable`**: la 361 promete «lo mismo que hoy» a propósito: cambiar qué cuenta
  como interactivo cambiaría las huellas de todos los workflows grabados. Es otra promesa, con su medida.
- **`LockApp` leído nodo a nodo 41 veces**: el latido lee la pantalla de bloqueo, que rechaza la caché por
  timeout. Hallazgo para el dueño; no es de esta spec.
- **El contador `_descartadas` compartido** por los dos relojes del mapa vivo (`MapaVivo.cs:62`): hace que
  «SATURADO (lectura): N vueltas» mezcle vueltas de ubicación. Es un mensaje que no distingue sus causas
  (patrón nº2) y toca el latido; se anota, no se arregla aquí.

## Nivel 4, pendiente (lo que el contrato no puede juzgar)

Con Jev **apagado o en simulado** (A no ha entrado: con `U_DECISOR=jev` sobre `sapgui://` viajan filas con
nombre) y **fuera de SAP**. Cada punto nombra la línea del log que el PR pega, con hora:

1. **Lecturas por paso, antes y después**, en ≥2 pantallas con nombre: **Explorador «Descargas»** (la
   lectura con el lector actual está sin medir: es la primera cifra que hay que traer) y **Chrome** (Gmail o
   ChatGPT.exe, las del 21-09). Línea `tramo:` con «lecturas: N nuevas (de: …) · K reutilizadas ·
   reutilizada: no (causa)» y las de `compuerta:` con «0 miradas · vista de N ms». Meta (D): de 2–5 lecturas
   por paso a 1 (+1 si hay ventana de trabajo ≠ foco, que no publica). **La edad de la Vista** por paso es
   lo que fija `Observacion.VigenciaMs` (p95 + margen) en lugar del 1.000 provisional.
2. **Selector primero**: `map_take exit=uia:name=…;ct=…` en las dos pantallas, con la puerta viva y sin ella
   en el grafo; las líneas «viva en el grafo, sin preguntar» y «pregunté por … (FindFirst) · N ms», y que no
   salga «leí la ventana» ni «miré otra vez» en el `map_take` que preguntó y acertó.
3. **Neo4j arriba y abajo**: un `map_take` que no navega y un tramo de 3 pasos en cada estado; comparar el
   resumen por minuto de `proyectar` («envíos: N · ms · más lento · fallidos»), los `SATURADO` por hora y el
   «⏱ pulsar» del clic. Es la medida que decide si la 367 se escribe.
4. **El reproductor**: un workflow UIA de la biblioteca sobre el Explorador (paso a paso), contando
   «barrido nº» por paso; se espera **≤1 barrido menos por paso** y ninguno menos en el caso común, y así se
   dice. Sin SAP en esta máquina, el lado SAP de 361 queda **sin probar** y se dice en el PR.
5. **La memoria del dónde, en sombra**: «dónde: N calculadas · M de memoria · K en sombra (J con otra
   respuesta)» por minuto (tag `donde`) en reposo y durante un tramo; al arrancar, la línea «memoria del dónde en
   sombra…» dice el modo. Si K/N es pequeño, la 369 se queda en sombra y el PR lo dice con el número. **Si J no es
   ~0, tampoco se enciende**, aunque K/N sea grande: serían sitios viejos servidos como nuevos (ver hallazgo
   «accionar sin olvidar», abajo).
6. **Cuántas veces `VentanaDelUsuario()` ≠ `DondeEstoy().Hwnd`**: la causa «otra ventana h1 → h2» de la
   línea de reutilización, contada por sesión. Es el dato que hoy es D.

## Hallazgos

- **2026-09-22 · numeración.** `f811796` (voz, Felipe) usa la **341** en `main`, y `5574148` (Felipe, 22-09
  09:58, traído por el merge `44d7dd9` de esta rama) usa la **342** y la **343** (memoria explícita; un log por
  instancia). La rama A tiene escrito «promesas 342–350» en su 046: **A tiene que desplazarse otra vez**
  (344–352 o lo que esté libre al abrirla). Los 361–370 de esta spec siguen libres en las 44 refs `origin/*`
  (`git grep` de `Prueba("36[1-9]`/`370.` sobre cada `origin/*`: 0 resultados) y `docs/specs/046-049` no
  existen en ninguna. El `046` sin commitear del dueño reserva 335–343, que ya usan la 044 y la voz: también
  hay que renumerarlo.
- **2026-09-22 · «Neo4j no responde» no puede salir nunca**: el aviso se gasta en el constructor
  (`AsegurarIndices`) cuando `Cuenta` todavía es null. Lo arregla la 366; es la razón de que 17 logs no lo
  digan ni una vez.
- **2026-09-22 · `FundirPuertas` funde por texto a propósito, y está bien**: la primera versión de esta spec lo
  llamó «el Analytics 0,91/0,09 en casa» y era al revés. En SAP el mismo elemento físico llega por UIA (el
  Pane) y por Scripting; fundirlos por etiqueta es lo que evita ofrecer la misma puerta dos veces. La 183 lo
  exige y su fixture lo juzga. Hallazgo corregido.
- **2026-09-22 · el mapa vivo pierde entre el 30 % y el 90 % de sus latidos por aviso (mediana 72 %) mientras
  Neo4j está caído** (21-09: 171 avisos de lectura; 22-09 a las 10:25: 217). Y con los mismos síntomas sin
  saber si Neo4j estaba arriba: el 03-09 hay 58 avisos (51 de lectura) y ninguna línea de Neo4j en el log. El
  26-08 que citaba la primera versión **no existe en esta máquina**. La 366 es lo que dirá cuánto es Neo4j y
  cuánto es la lectura.
- **2026-09-22 · el contador `_descartadas` es compartido** por el reloj de ubicación y el de lectura
  (`MapaVivo.cs:62-66`): el rótulo del aviso es el del reloj que cruzó el umbral y la cifra mezcla los dos. Todo
  «% perdido» calculado sobre él es cota superior.
- **2026-09-22 · dos funciones contestan «cuál es la ventana de la persona»**: `AppAligner.VentanaDelUsuario()`
  (la que lee `UiaReader.Read()`) y `SurfaceLocator.Ahora()` (la de `_where()`), con reglas distintas cuando
  Ü está delante o una hija tiene el foco. Aprendizaje nº16 en potencia; esta spec compara siempre por el
  mismo lado y lo cuenta en el nivel 4 (punto 6).
- **2026-09-22 · fase 8 · accionar sin olvidar la ubicación: la memoria del dónde NO se puede encender tal como
  está.** Contados los sitios donde se acciona y después se pregunta «dónde»: **2** pasan por
  `Observatorio.Invalida` (`Take` y `Type`, en su `finally`: cuando el tramo ENTERO ya terminó); **no pasan**
  (a) `SeguirElFoco` (`FaceWindow.xaml.cs:5545`, `Ui/` vetado), que olvida `_dondeTrabajo` y pregunta
  `DondeEstoy()` en el acto desde **5** sitios (`:687`, `:754`, `:756`, `:797`, `:901`) —el de `:901` corre
  dentro de `RecorrerPorElNucleo`, o sea ANTES del `finally` de `Take`— y (b) cada pulsación dentro de un tramo,
  cuya espera sondea `DondeTrabajo`, que sin ventana de trabajo fijada cae a `DondeEstoy()`
  (`VentanaDeTrabajo.Resolver`, `:49-52`). Con la memoria encendida, en esos sitios una pulsación que cambia la
  pantalla sin cambiar ventana ni título (Configuración, SAP no: SAP se calcula siempre) vería el sitio de antes
  hasta 400 ms —la refutación nº5, que la fase 8 cierra para `Take`/`Type` y no para estos—. En sombra no hace
  daño y lo MIDE: es «J con otra respuesta». Para encenderla: que la pulsación (B, `PulsarSegunElNucleo`) y
  `SeguirElFoco` (`Ui/`) llamen a `Observatorio.Invalida` al volver la mano. No es de esta rama.
  **Actualizado en la fase 10:** `RecorrerSegunElNucleo` invalida ahora al volver CADA mano (pulsar, escribir,
  teclear), así que el `SeguirElFoco` de `:903` —que corre después de `recorrer.Recorre`— ya no puede recoger el sitio
  de antes. Siguen sin cubrir: la espera DENTRO de `PulsarSegunElNucleo` (B), y los `SeguirElFoco` de `map_go_to`
  (`:756`, `:758`) y `map_open_app` (`:689`), que corren antes de que el despacho invalide al volver.
- **2026-09-22 · fase 8 · el juez de la 369 no alcanzaba el cableado.** Juzgaba `Olvida()` sobre la clase pura,
  pero en producción nadie llama a `Olvida()`: lo que dice «accioné» es `Observatorio.Invalida`. Una `Olvida()`
  verde que nadie llama es el guardia que se cree puesto (aprendizaje nº18). Se añadieron al juez, antes que su
  código, dos comprobaciones (la de `Observatorio.Invalida` y la de «con otra respuesta»); el enunciado no cambió.

## Revisiones

Doce refutaciones recibidas el 2026-09-22 y comprobadas una a una contra el código y el log antes de
aplicarlas. Todas se aplicaron; la décima con un matiz que se anota.

| # | Afirmaba la spec | Veredicto | Qué cambió |
|---|---|---|---|
| 1 | 365 funde con el terreno por selector; «3 sitios de fusión por texto → 0» | **cierta, bloqueaba.** `FundirPuertas` compara por etiqueta a propósito (`:146-156`) y el fixture de la 183 (`Contrato.cs:7117-7145`; el refutador citaba `:7095-7116`, que es `LaFilaDeAlvSeElige`: las líneas se movieron, el fondo no) exige que «Triage» desaparezca y `Count == 2`. Con fusión por selector, `sap:` nunca coincide con `uia:` y la 183 sale ROTA | 365 reescrita: el terreno se funde como la 183 manda; solo UIA↔UIA no funde por texto (y ya era así); fase 5 no toca `FundirPuertas`; hallazgo corregido |
| 2 | 364 pregunta siempre que haya selector; «si no está… se lee e inventaría como hoy» | **cierta.** `Take` no lee (`:2586-2602`); la compuerta encuentra la puerta viva sin mirar (`:436-438`); `Execute` resuelve otra vez (`:988-996`). Preguntar siempre añadía 7–126 ms al camino común, y «como hoy» era falso | 364: se pregunta solo si `PuertasVivas(aqui)` no tiene la puerta; «no está» → lecturas 0 en `Take`, la compuerta mira como hoy; tres caminos en la cuenta |
| 3 | 363: «la misma criba que el latido» sobre `Candidatas` ya filtradas | **cierta.** Son dos cribas con propósitos distintos (`:174-176` y `SinEtiquetasDeControles`); la observación sin crudos no puede aplicar la del latido y mataría los Text sueltos vivos | la observación lleva crudos sin criba ni tope; `LoQueEsPuerta` sobre crudos en la compuerta; `PuertasDeAhora` aplica la suya sobre los mismos crudos; juez con un Text suelto vivo; 3 cribas contadas |
| 4 | 363 exige 0 miradas con la puerta no viva, y la 264 dice «vuelve a mirar» | **cierta.** `Debe(miradas >= 1)` en el cuerpo de la 264; con Vista el enunciado sería falso para todo paso con vista; y el «1 s» era del patrón nº8, no de este camino | la 264 se reescribe sin reciclar (cláusula «salvo que el paso traiga una observación de ESTA pantalla…»); `VigenciaMs` provisional y se fija con la edad de la Vista medida en el nivel 4 |
| 5 | 369: «la misma memoria de 400 ms que `DondeTrabajo`» | **cierta.** `DondeTrabajo` olvida al accionar (`:5679`) y recalcula por `DondeEstoy()` (`:5680`): con la 369 sin invalidación recogería lo viejo; los sondeos de `_where()` (`:2786, :2805, :2907, :2921`) verían el cambio ≥400 ms tarde; y no está medido cuántas de las 25 llamadas caen dentro de 400 ms de otra | 369 gana «accionar lo olvida» (misma llamada que `Observatorio.Invalida`), arranca **en sombra** y el nivel 4 decide si se enciende |
| 6 | 366: «cada fallo deja una línea» y «0 líneas nuevas dentro del minuto»; el constructor «no gasta el aviso» pero «también cuando cae antes» | **cierta.** Tres contradicciones internas; además `Proyectar` con el mismo grafo corta en `:170` sin tocar la red (`_ultimaVersion` se fija en `:177` antes de mandar): el juez no podía ponerse verde | 366 separa el contador (sin línea por fallo) de la única línea limitada; la caída del constructor cuenta como primer aviso; el juez muta el grafo con `Observar` entre llamadas |
| 7 | 361: `VigenciaMs = 250` «menor que 120 × 2»; «el árbol se recorre una vez y sirve a los tres» | **cierta.** 250 > 240; con 250 dos sondeos seguidos verían el mismo recuento; en el caso común `ReadinessCount` de la superficie no se llama nunca (`SafeReady` primero, `:146`); y el juez solo veía `BarridoUia`: un `Walk` de vuelta en `ReadFields` seguía verde | `VigenciaMs = 100 < PollMs`; `Barre`/`Reloj` inyectables en `UiaSurface` y el juez sobre los tres métodos reales; el ahorro esperado dicho (≤1 `Walk` por paso, 0 en el caso común) |
| 8 | 365: «sin duplicar… por su RuntimeId»; «los 2 deduplicados de menú pasan a identidad» | **cierta.** `IdentidadDe` devuelve «» sin caché (`:226-230`); el nodo a nodo no tiene identidad (`:447-467`); en el camino con caché ya existe (`:194-210`) | «una identidad vacía nunca funde» en enunciado y juez; los deduplicados de menú se quedan; el log cuenta «sin identidad» |
| 9 | 362: «`UiaReader.Read(hwnd)` termina publicando»; se pide por `_where().Hwnd` | **cierta en el código; que difieran a menudo es D.** `Read()` lee `VentanaDelUsuario()` (`UiaReader.cs:56`; sin `GA_ROOT`, última activada con Ü delante) y `_where()` calcula `GA_ROOT`/orden Z (`SurfaceLocator.cs:164-175`): dos funciones (aprendizaje nº16); el arnés con `Lee` falso no lo vería | la clave es (hwnd que `Lee` leyó, `aqui`), pedido por la misma función (`VentanaQueLeeria`); juez con dos hwnd; línea «reutilizada: no (otra ventana h1 → h2)»; publicación fuera de `UiaReader`; nivel 4 cuenta las divergencias |
| 10 | 183/197 SATURADO «todos lectura», «45–57 cada uno», «67–85 %», «23 logs», «26-08: 32; 03-09: 51» | **cierta con matiz.** 17 logs; 21-09: 183 = 12 ubicación + 171 lectura, vueltas 20–60 (14×20, 31×45), mediana 48; 22-09 crece (232 a las 10:20); no existe `u-20260826.log`; «Neo4j no responde» 0/17 y `sapgui://` 0/17 se reproducen. **Matiz:** el 03-09 da 58 en total y **51 de lectura**: la cifra de la spec era la de lectura, no un error; lo que sí era D es «con Neo4j arriba» ese día (0 líneas de Neo4j) | comandos y salidas pegados con hora; lectura y ubicación separadas; 26-08 y «23» fuera; % por aviso como rango con mediana y con el hallazgo del contador compartido |
| 11 | ningún cruce con ramas ajenas nombrado; fase 2 en `:2148-2151`; `UiaReader` tocado en dos fases | **cierta.** `origin/jose/ir-devuelve-la-pagina-asentada` (5 commits, `a52dee5`) reescribe `@@ -2147,10 +2154,35` y su 335 no es la de `main`; B toca `RecorrerSegunElNucleo.cs:412-413`/`Resultado` y `Mano :2642` junto a lo de C; arquitectura §7 y 047:151: «nadie toca `UiaReader`» | tabla de cruces con líneas y orden; Jose se nombra antes del PR; `VistaReciente()` lejos de `:2147`; `UiaReader`: 0 líneas |
| 12 | «se juzgan sin pantalla» (las ocho) | **cierta.** Cuatro costuras fuera del alcance del contrato (`UiaSurface` ×3, `Ahora()` privado, `Preguntar`, los lectores de `FaceWindow`), justo donde vive el ahorro | sección «Qué juzga el contrato y qué solo la máquina»: (a) inyección juzgable; (b), (c), (d) línea de log con nombre en el nivel 4 que el PR pega con horas |

## Revisión de la noche (2026-09-22, fase 10)

Ocho hallazgos sobre las fases 1–8, comprobados uno a uno contra el código antes de tocar nada. **Ninguno está
medido** —U.exe no se ejecuta en este encargo y hay 0 líneas `sapgui://` en los 17 logs de esta máquina—: todos son
**D**, y así se dicen. Dos pares eran el mismo defecto visto dos veces (1 y 6; 4 y 5 comparten causa). Ninguno resultó
falso. Lo que cambió lo que se promete entró primero al contrato, en rojo: `CONTRATO ROTO: 7 promesa(s)` con la 361,
362, 363 y 364 rotas por las razones escritas; la 366 y la 369 salieron verdes con sus jueces reforzados porque su
código ya decía la línea del minuto (sus sabotajes prueban que ahora sí pueden ponerse rojas).

| # | Hallazgo | Veredicto | Qué cambió | Promesa |
|---|---|---|---|---|
| 1 y 6 | en SAP la compuerta volcaba la observación de UIA sobre `sapgui://`: `Grafo.Observar` sustituye los vivos que el latido lee por Scripting, y `sap:…#tbbtn=NV44` quedaba muerta en cada paso del hospital | **cierto, bloqueaba** (D). Sitios que vuelcan una lectura de UIA al grafo: **4** —`MirarOtraVezLaVentana`, `ObservarLaVentanaDeTrabajo`, el latido (elige el sentido por mundo) y la compuerta—; 3 tenían la guarda y la compuerta no. Además `VistaDelPaso` adjuntaba la compartida a un selector `sap:` («no es de UIA» → `deUno ?? compartida`) | guarda `sapgui://` en `PorQueLaVistaNoCuenta` («SAP se lee por su API»); `VistaDelPaso` no adjunta nada a un selector `sap:` ni en una ubicación `sapgui://` | 363, enunciado ampliado |
| 2 | el juez de la 369 no juzgaba la línea por minuto, solo `Resumen()` (acumulado) | **cierto** | la memoria del juez se construye con `cuenta`; dentro del minuto no dice nada; a los 60,5 s, «… en los últimos 60 s» con «(1 con otra respuesta)» | 369, juez |
| 3 | lo mismo en la 366 | **cierto** (2 sitios con el «por minuto» sin juzgar) | la línea del minuto de los +61 s, juzgada | 366, juez |
| 4 | la 364 decidía «viva» con el dónde de DELANTE (`_where`) y la compuerta juzga con el de TRABAJO (`DondeTrabajo`) | **cierto** (D; aprendizaje nº16). El comentario «un solo dónde» era falso para la tercera decisión | `DondeDeTrabajo`, cableado a `DondeTrabajo` (2 líneas en `FaceWindow`); la respuesta de uno se sella siempre con ese dónde | 364, enunciado ampliado |
| 5 | `Take` pagaba `_where()` en cada `map_take` y, sin ventana fijada, `VentanaObjetivo` otro `DondeEstoy`, antes de saber si hacía falta | **cierto** (D, sin medir) | orden de coste: un selector `sap:` sale sin situarse; el dónde se pide una vez —y al ser `DondeTrabajo`, la compuerta lo encuentra después en su memoria de 400 ms—; `VentanaDeTrabajo` solo para preguntar; «situarse N ms» y «hallar la ventana N ms» en la cuenta del paso y en el log | 364 |
| 7 | accionar solo invalidaba en `Take` y `Type` | **cierto**. Manos contadas: 2 invalidaban; **8** herramientas accionan sin ser ni una ni otra (`map_scroll`, `map_go_to`, `map_open_app`, `map_unblock`, `file_open`, `map_batch`, `map_skill_run`, `leccion_plan`); y dentro de la compuerta del núcleo, **3** manos (pulsar, escribir, teclear) | `LaManoVolvio` al volver cada una de las 3 manos de `RecorrerSegunElNucleo` (cubre batch, skill, plan, tramo y los pasos de `FaceWindow`); el despacho invalida al volver de las 8, antes del inventario pegado; `map_scroll` va por `Desplaza`, inyectable | 362, enunciado ampliado |
| 8 | el barrido compartido del reproductor no se invalidaba al ejecutar: la huella del paso N+1 podía recibir la pantalla de antes del Enter del paso N | **cierto** (D). Manos de `UiaSurface`: **4** (`Execute`, `EjecutarSobre`, `TeclearEnLaVentana`, `TeclearEnElCampo`) | la captura se marca invalidada al entrar en las 4 (y al salir de `Execute`); `BarridoUia.Toma` no sirve una invalidada y la línea del barrido nuevo dice por qué; la numeración «barrido nº» sigue | 361, enunciado ampliado |

**Lo que el evento tipado de la 368 lleva no cambió** (la rama D lo consume): ni su forma ni sus campos.

## Cierre

- [ ] Todas las promesas escritas verdes (`.\scripts\contrato-del-grafo.ps1` → `CONTRATO INTACTO`), la 264
      reescrita incluida
- [ ] Un sabotaje por promesa, comprobado por diff (copia, rompe, diff, compila sin silenciar, `ROTO`
      nombrándola, restaura, diff idéntico, recompila, `INTACTO`)
- [ ] `.\scripts\verificar.ps1` pasa, con evidencia en `out\evidencia.md`
- [ ] Probado en ≥2 pantallas, con nombre: …
- [ ] Jose avisado del cruce en `SurfaceMapTools.cs:2147` antes del PR
- [ ] Merge de `origin/main` antes del push; A y B ya dentro
- [ ] Estado de este documento: **implementada** (AAAA-MM-DD)
