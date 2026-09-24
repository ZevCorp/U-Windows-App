# Se ve como TipTour: el overlay, el panel y la flecha de Jev, alimentados por un modelo que el contrato juzga

Estado: **en construcción** · Nace del encargo del dueño del 2026-09-22 («trae main, revisa y cambia la arquitectura al nuevo Jev») · Rama: `jero/jev-se-ve-como-tiptour` · Base: `main` `dde8c40` + `f811796` + `5574148` (fusionados el 22-09: `bb3d0dc`) · Promesas **371–385** · **Revisada el 2026-09-22** con nueve refutaciones (§Revisiones)

> Rama **D** de las cuatro de la nueva arquitectura de Jev (`scratchpad\jev\arquitectura-jev-en-u.md`, §7). Entra
> **después de C** (spec 048): consume su evento tipado `AlDecidir` (368) y la candidata con caja (365). Hasta que C
> entre, esta rama se alimenta por un puente provisional que se describe abajo y que deja las cajas verdes **vacías y
> diciéndolo**, nunca pintando cajas inventadas. Lo que el puente sí ve con geometría real es **lo pulsado**: la mano
> avisa con su caja (`UiaSurface.Pulso`), y eso es lo único que se pinta en rosa.
>
> Todo lo visual sale de `scratchpad\jev\plano-para-wpf.md` (medido sobre el vídeo de TipTour y leído en su código
> MIT, `5258246`). Aquí no se discute otra vez: se cita el token, la medida o la duración, y se dice qué promesa la
> juzga. **Marcas:** (M) medido —log, fotograma, `grep`, `Measure`, HSL—; (D) deducido; sin marca, una meta.

## Diagnóstico: qué se midió

| Qué | Medida | Fuente |
|---|---|---|
| Lo que hoy se **ve** de una decisión de Jev en Ü | una línea de texto en el notch (`Progreso`) y un botón «Jev · on/off»; **0** cajas, **0** barras, **0** medidores | `Mcp/SurfaceMapTools.cs:1730`, `Ui/FaceWindow.xaml.cs:464-469, 2889-2896` (M, grep) |
| Lo que el vídeo de TipTour enseña por paso | cajas verdes sobre cada candidato, rosa para el elegido, panel de 340 pt con «step k · N detected · NNNms $acumulado», medidores `done`/`absent` y 5 barras; una flecha azul que vuela 1,05–2,2 s al elegido | `plano-para-wpf.md` §Tokens, §Medidas, §Duraciones (M sobre fotogramas) |
| Cuánto tarda decidir en Ü, que es el «ms» de la cabecera | p50 322–350 ms con 20/60/160 puertas; 1.066 ms en frío | `docs/anatomia-del-clic-2026-09-18.md` §3 (M) |
| Cuánto cuesta un paso, que es el «$» | `usage.input_tokens` × **$0,042 / M** tokens de entrada; 875 tokens con 20 puertas = $0,000037 por paso: con 4 decimales el contador parecería roto | `docs/computer-use-con-jev-2026-09-21.md:41`; anatomía §3 (M × precio publicado) |
| Quién lee hoy `usage.input_tokens` | **nadie**: `ClienteTypeSafe.Pregunta` devuelve la respuesta cruda y `ElDecisor` no la lee | `Decision/ClienteTypeSafe.cs:78`, `Decision/ElDecisor.cs:163-186` (M, lectura) |
| **Quién pulsa de verdad**, y si la costura del decisor lo ve | `UnPasoDecidido` prueba la elegida y, si la mano dice «no está», la **segunda mejor** (≥ 0,25) sin volver al decisor; en la costura `Decisor(pantalla, objetivo, ids)` solo se ve `d.Puerta`: **cuál se pulsó no pasa por ahí** | `Mcp/SurfaceMapTools.cs:304-309, 328-331` (M, lectura) |
| Qué avisa de la **caja real de lo pulsado** | `UiaSurface.Pulso(x, y, ancho, alto)`: cuatro `double` en físicos, disparado por la mano al pulsar; la carita ya lo escucha | `windows-graph/src/Surfaces/UiaSurface.cs:139`, `FaceWindow.xaml.cs:1318, 4657` (M, lectura) |
| Qué dice la línea de progreso del tramo tras pulsar | `paso k: «etiqueta» (n) conf 0.91 · cambió`, donde `n` es el **número del id** ofrecido (`id.Substring(0, id.IndexOf(')'))`) | `Navigation/ElTramo.cs:204`, `SurfaceMapTools.cs:318` (M, lectura) |
| Cuántas veces reasigna el interruptor el `Decisor` del mapa | **2**: en `Encender` y en `Apagar`; un decorador puesto antes desaparece con cada una | `Decision/InterruptorDelDecisor.cs:66, 81` (M, lectura) |
| Ventanas de Ü que vigilan «siempre delante» **por su cuenta**, cada una con su reloj | **2**: `Muelle.cs:175` y `PanelDeAcciones.cs:247`; con overlay, panel y flecha serían 5 relojes compitiendo | grep `\.Vigilar\(\)` (M) |
| Conversiones de DPI escritas a mano fuera de `Pantallas` | **7**: `FaceWindow.Escritorio.cs:111`, `FaceWindow.xaml.cs:1885, 2211, 4682, 4706`, `InspectorOverlay.cs:100`, `PanelDeAcciones.cs:282`; todas válidas solo en el monitor primario | grep `TransformFromDevice\|DpiScaleX` (M; la arquitectura decía 6) |
| Pesos de fuente que WPF resuelve de verdad | `Segoe UI Variable Text` + `Medium` → **SemiBold**; `Consolas` + `Medium` → **Regular**; Cascadia Mono **no está** en esta máquina | `recortes/revision/medir-panel.txt` (M) |
| Alto natural de una línea en WPF | Consolas 10 → 11,71; Segoe 9 → 11,97: las filas de 11 **recortan** sin `BlockLineHeight` | mismo guion (M, `Measure`) |
| El XAML del panel medido sin pantalla | 340×**64** sin resultados —con `MinHeight="64"` en el `Border` y relleno 13/10/13/10— y 340×**198,6** con cinco barras y relleno inferior 7,6; **sin el mínimo las partes suman 60 con relleno 10 (M) y 57,6 con 7,6 (D)**; la versión con `BorderThickness` en el mismo `Border` daba 202,6 y recortaba `Resultados` a 312,4 | `recortes/revision/medir-panel.ps1:26`, `medir-panel.txt:2,6,9` (M) |
| Los **tonos** de los 16 tokens del plano, en HSL | 3 sin tono (`Pista`, los dos fondos de etiqueta); 6 grises a 150–157,5° con saturación 0,01–0,06, a **0,6–8,1°** de `Cumplido` (158,1°); los tres azules a 3,7–8,1° entre sí; `Ausente` 38,9°, `Candidata` 117,1°, `Elegida` 348,3°. Con la regla escrita antes de la revisión: **24 pares** con distinto significado a menos de 30°; exigiendo saturación ≥ 0,25 en los dos: **3**, los azules | cálculo HSL sobre los ARGB de la tabla de la 371 (M, guion de la revisión) |
| Los choques con **otras** paletas de Ü | `Ausente` ≈ `Atencion` del inspector (`0xFFA51F`): **3,0°**; `Candidata` ≈ shell mapeado (`0x3FBF6F`): **25,4°** y ≈ `Vivo` (`0x2FB457`): 20,9°; `Elegida` ≈ `Fallo` (`0xFF3B30`): **14,9°** | ídem, contra `InspectorOverlay.cs:50, 56` y `windows-client/CLAUDE.md` §Paleta (M) |
| Cómo trata hoy el arnés un «NO PUDE JUZGARLA» | **3 sitios y dos conductas**: la 340 (`:911`) y la 341 (`:925`) hacen `return` sin contar y la promesa sale **✔** (inocente); la 164 (`:7912`) hace `_fallos++` y sale **✘** (culpable). Ninguna dice «no sé», y el resumen de CI solo cuenta `PENDIENTE:` | `tests/ContratoDelGrafo/Contrato.cs:905-933, 7908-7917`, `.github/workflows/contrato.yml:58` (M, lectura) |
| Qué hace el script con un juez sin veredicto | si la salida no trae `CONTRATO (INTACTO\|ROTO)` sale con **99**, y `verificar.ps1` y CI rotulan 99 como «no se pudo juzgar» | `scripts/contrato-del-grafo.ps1:119-124`, `verificar.ps1:135-141`, `contrato.yml:81-84` (M, lectura) |
| El «elegido» rosa en el vídeo | **nunca aparece** (barrido t=62–134): lo que parece elegido es el objetivo del cursor burbuja, la caja más cercana al ratón real | `plano` §El estado «elegido» (M) |
| Cuántos cuerpos viajan hoy por clic | 1: la carita plegada sigue al cursor sintético y viaja a cada clic (promesa 240) | `FaceWindow.xaml.cs:1876-1892, 4657-4658` (M, lectura) |
| Qué hace hoy el atajo de invocar a Ü | guarda el primer plano, despliega el muelle y abre el globo **con el foco en el campo**: es la vía de teclado para escribirle a Ü | `FaceWindow.xaml.cs:1587-1600` (M, lectura) |
| Lo que las ramas hermanas tienen commiteado | A, B y C: **solo su spec** (`git diff --name-only origin/main...HEAD`); 365 y 368 de C y 350 de A están PENDIENTES sin código | worktrees `jev-seguro`, `jev-espera`, `jev-lectura` (M) |
| Monitores en esta máquina | **1**: dos escalas de DPI no se pueden probar aquí | (M) |

## Por qué esto va dirigido por especificación

Una vista se da por buena a sí misma más que ningún otro subsistema: se abre, «se ve bien», y nadie comprueba que
lo pintado sea lo ofrecido. Este repo ya pagó esa clase de fallo tres veces —la caja que miente (aprendizaje nº4), el
destello amarillo indistinguible del ámbar (`windows-client/CLAUDE.md` §Paleta) y el «29 de 30» con 19 pasos comidos
(nº10)—. Y aquí hay cinco sitios concretos donde una vista escrita a ojo mentiría:

1. **Barras sin distribución.** Con Luna o con la regla local no hay probabilidades; pintar sus «confianzas» como
   barras sería un gráfico de nada.
2. **Un coste inventado.** Nadie lee hoy `input_tokens`: un `$` calculado con un número supuesto es una cifra falsa
   en la pantalla de un hospital.
3. **El elegido por parecido.** Dos «Buscar» existen en SAP; resaltar por subcadena ilumina el que no se va a pulsar.
4. **La elegida que no se pulsó.** La mano pulsa la segunda mejor cuando la primera «no está» (288), y la costura
   del decisor no se entera: un panel que resalta la elegida mientras la mano pulsa otra es la caja que miente con
   distribución y todo.
5. **La vista en el camino del clic.** Un `Dispatcher.Invoke` en la costura devuelve al ciclo los segundos que B y C
   están quitando.

Por eso la vista sale de un **modelo puro** (`EstadoDeLaDecision`, `CajasDelOverlay`, `DondeVaElPanel`,
`PlanDeVuelo`, `OrdenEnZ`): dado un ciclo, produce exactamente lo que se pinta, y eso se juzga en un segundo sin
pantalla, sin SAP y sin TypeSafe. Las ventanas WPF solo dibujan lo que el modelo devuelve. **Y lo que el modelo no
puede juzgar —el gancho, la máscara aplicada, el `if` en FaceWindow— se dice en cada promesa y se juzga por fuente
con `U_REPO`, como ya hacen la 164 y la 340; lo que ni eso alcanza va al nivel 4 con nombre** (§Revisiones, 5).

**La regla del flujo, y no tiene excepciones:** ninguna línea de producción entra antes que la promesa que la
juzga. Cada fase empieza con el contrato ROTO y termina con el contrato INTACTO.

## Lo que se ve, en una frase

Un **overlay** por monitor, transparente y *click-through*, que pinta una caja verde con su etiqueta sobre cada
candidata que se le ofreció al decisor y una rosa sobre **lo pulsado**; un **panel** de 340 DIP que enseña el
objetivo, el estado del paso, «paso k · N detectados · {ms}ms ${acumulado}», los medidores `cumplido`/`ausente` y
cinco barras; y una **flecha** azul que vuela a lo pulsado mientras la mano ya pulsó. Todo alimentado por un modelo
puro, por `Dispatcher`, sin que el ciclo espere a nada de ello.

> **Nota de nombres.** El encargo llama «cursor burbuja» a lo que vuela. En el plano lo que vuela es **la flecha**
> (`OverlayWindow.swift`), y «el burbuja» es el anillo verde que respira alrededor del ratón **real** en reposo. Aquí
> se promete la flecha; el burbuja en reposo queda fuera (ver *Lo que NO entra*).
>
> **Elegida y pulsada no son la misma cosa.** La *elegida* es la que devolvió el decisor; la *pulsada* es la que la
> mano pulsó, que puede ser la segunda mejor (288). El rosa del overlay, el `SemiBold` del panel y el destino de la
> flecha siguen a **la pulsada**; la elegida se marca aparte solo mientras no se sabe qué se pulsó.

## La especificación

El enunciado es el que va **literalmente** en `tests/ContratoDelGrafo/Contrato.cs`, bajo `// ── Spec 049 ──`.

| # | Promesa | Fase que la pone verde |
|---|---|---|
| **371** | la paleta de Jev es la del plano y se juzga sin pincel: cada token guarda su ARGB exacto, su significado y si es cromático —saturación HSL de 0,25 o más—, dos tokens cromáticos con distinto significado distan al menos 30° de tono salvo los tres pares aceptados por escrito —los azules entre sí—, los neutros y los acromáticos se juzgan solo por su valor, no hay token para lo que no existe —el cian del OCR—, y el overlay de Jev y el inspector no se encienden a la vez: la regla es pura, el inspector y la vista de Jev la consultan antes de encenderse y ponen su bandera, que se lee en sus fuentes, y un overlay impedido no se da por visible | 1 |
| **372** | el panel de Jev sale de un modelo puro: dado un ciclo —objetivo, paso, candidatas, decisión, lo pulsado y tiempos— produce exactamente lo que se pinta: la cabecera «paso k» «· N detectados» «{ms}ms», los medidores cumplido y ausente, y cinco barras como mucho ordenadas de mayor a menor probabilidad con su valor a dos decimales y la etiqueta sola —numerada solo si dos de las cinco la comparten—; la resaltada es la pulsada por su id —la elegida solo mientras no se conozca la pulsada—, nunca una por parecido de texto; y si la mano pulsó otra que la elegida, el ticker lo dice | 2 |
| **373** | sin distribución no hay gráfico: con Luna, con la regla local o con una respuesta sin probabilidades no se pintan cabecera, medidores ni barras; el coste es el acumulado de la tarea a partir de los tokens facturados que devuelva el cliente, con cinco decimales, y sin tokens se enseña «—» y no crece; el ms de la cabecera es el de decidir; y un medidor sin dato enseña «—», nunca 0 | 2 |
| **374** | los textos del panel describen el paso y no concluyen: cada estado tiene su cadena exacta, el motivo con que el tramo para se enseña tal cual llega —sin reescribirlo—, «Jev cree que ya está» nunca se convierte en «listo» ni en «terminado», pulsar la segunda mejor se dice como lo que es, una decisión vetada se dice vetada con el porqué del veto tal cual —nunca «pulsando» ni «no estoy seguro»— y sin barra resaltada, y el punto del ticker va a 0,82 mientras el tramo sigue y a 0,42 cuando para | 2 · ampliada al juntar A, C y D |
| **375** | el overlay pinta exactamente la lista que se ofreció al decisor: una caja por candidata con caja leída y ninguna más, en su orden, con su etiqueta sola; una candidata sin caja se ofrece pero no se pinta y se cuenta; la rosa es la de lo pulsado —por su id, o por la caja que la mano dice haber pulsado— y nunca la elegida antes de pulsar ni una por etiqueta —dos «Buscar» dan una sola rosa—; y las cajas caducan al cambiar la ventana de delante: la caducidad es pura y el gancho que la dispara se lee en el fuente del overlay | 3 |
| **376** | el panel de Jev mide lo que dice el plano y su alto sale de sus partes y de un mínimo declarado, no del contenido: 340 de ancho, 64 sin resultados —el mínimo, que calca los 63,7 del vídeo; las partes suman 57,6— y 198,6 con cinco barras; el XAML medido sin pantalla da lo mismo y la zona de resultados recibe sus 314 enteros porque el borde no ocupa sitio; y si WPF no mide en el arnés, la promesa queda SIN JUZGAR, ni verde ni roja | 4 (pura) · 8 (XAML) |
| **377** | el panel se pone donde no estorba, y se calcula sin pantalla: junto a la carita a (56, 32) de su borde por la escala —un ancla-punto es una carita de 0×0—, probando las cuatro esquinas en orden, sin tapar la carita ni los 22 de alrededor —el cuadrado de 44 de un ancla-punto—, a 12 del borde del área de trabajo, sin cruzar el notch ni ningún obstáculo ni la caja de lo pulsado si alguna esquina cabe, y si ninguna cabe se va a la esquina opuesta; nunca se sale del área de trabajo | 4 |
| **378** | un solo vigilante sube las capas de Ü en un orden fijo —overlays de cada monitor, carita, notch, panel de Jev, flecha— con un solo reloj, reordena en el acto cuando una del grupo se muestra, y ninguna vigila por su cuenta: el muelle y el notch entran al grupo y dejan de hacerlo, que se lee en sus fuentes | 7 |
| **379** | el overlay de Jev es una ventana por monitor en píxeles físicos, transparente, click-through, no activable, de herramienta y excluida de la captura —las máscaras son puras y el overlay las aplica, que se lee en su fuente—, y por defecto está apagado: lo enciende U_JEV_OVERLAY=si al encender Jev, la configuración es pura y dice por qué quedó como quedó, y el estado de la vista lo nombra | 6 |
| **380** | todo lo de Jev se dibuja por Pantallas: la inversa —de físicos a la unidad de un monitor concreto, restando su origen— existe y con la ida da la identidad, un monitor secundario con otra escala convierte bien, no hay ninguna conversión a mano nueva bajo Ui/Jev, y al cambiar el DPI el overlay vuelve a aplicar el rect calculado y no el sugerido: la regla es pura y su llamada desde OnDpiChanged se lee en el fuente | 4 |
| **381** | la flecha vuela a lo pulsado con la duración del plano y no cuando la app no está delante: la duración es la distancia entre 520 por la escala, acotada entre 1,05 y 2,2 s; aterriza a 42 por la escala del centro de lo pulsado; el camino sale y llega parado y no se devuelve; con la ventana de trabajo detrás de otra no hay vuelo; y quién está delante se le pregunta a Windows, no se da por hecho, que se lee en el fuente de la vista | 5 |
| **382** | nada de la vista bloquea el ciclo: la vista oye cada paso decidido por el evento del mapa —AlDecidir, con la decisión ya vetada— y no envuelve ni reasigna el decisor, oírlo dos veces se suscribe una, y un oyente que lanza no sale al paso y queda en el log con su tipo y su mensaje; del evento sale el ciclo que se pinta —la pulsada es la del paso, solo si la mano terminó, y la caja de una candidata solo si el paso no cambió lo que se ve— y la línea que llega pegada a él pinta las mismas cajas; publicar un ciclo solo encola y nunca ejecuta en el acto —con un despachador que lanza si se le pide ejecutar ya, publicar no lanza—, con la cola sin vaciar se pinta solo el último ciclo, una excepción al pintar no sale al ciclo y queda en el log con su tipo y su mensaje, y el único BeginInvoke de la vista vive en su adaptador a WPF | 6 · reescrita al juntar C y D |
| **383** | apagar Jev cierra las tres ventanas —overlay, panel y flecha— y Escape o soltar lo señalado vacían el overlay, esconden la flecha y dejan el panel sin corrida: la máquina es pura y sus dos ganchos se leen en el parcial; el interruptor del decisor sigue apagando el catálogo byte a byte | 6 |
| **384** | una sola cosa vuela por clic: mientras corre un tramo con Jev la carita ni viaja al clic ni sigue al cursor sintético, fuera de tramo sigue haciéndolo, y la carita no cambia de tamaño: la regla es pura y sus dos llamadas en FaceWindow se cuentan en el fuente | 6 (regla) · 9 (cableado) |
| **385** | el panel de Jev nunca toma el ratón ni el foco ni sale en las capturas —click-through, no activable y excluido de la captura siempre—, nace al encender Jev sin tocar el atajo de invocar a Ü —InvocarPorAtajo sigue abriendo el globo y no sabe de Jev, que se lee en su fuente—, y se aparta de lo pulsado en cuanto lo conoce, con el alto que pinta | 6 (regla) · 9 (cableado) |

**La que de verdad cierra el asunto es la 372**: mientras la vista no salga de un modelo que se pueda juzgar, todo lo
demás —tokens, medidas, orden en Z— es cosmético sobre algo que nadie sabe si dice la verdad. La segunda es la
**382**: si la vista se cuela en el camino del clic, deshace lo que B y C consiguen.

### Con qué se juzga cada una

Todo con **mapa a mano en la propia prueba** y datos de mentira: ni pantalla, ni SAP, ni TypeSafe. Las capacidades se
piden por nombre con `Capacidad("U.WindowsClient.Ui.Jev.…")` y, si no existen, `Pendiente(…, "NNN", "049")`. Los
ARGB van en `uint`, como `PaletaDelNotch`, para que el contrato no levante WPF. **Ninguna prueba ni ninguna línea de
log de esta rama lleva una etiqueta de candidata**: los fixtures usan etiquetas de mentira («Detalles», «Buscar»,
«Grabar») y el log de la vista escribe cuentas y tiempos, nunca texto de pantalla.

**Dos clases de juicio, y cada promesa dice cuál usa.** (a) **Puro**: se instancia la clase y se comprueba lo que
devuelve. (b) **Por fuente**: se lee el archivo con `U_REPO` —lo pone `contrato-del-grafo.ps1:26`, como para la
164/340/341— y se exige que contenga la llamada de una línea que engancha la regla pura al gancho de Windows; **sin
`U_REPO` la prueba dice `SIN JUZGAR` y cuenta como tal** (fase 0), nunca ✔. Lo que ni (a) ni (b) alcanzan —que el
gancho de Windows dispare de verdad— está en la tabla del nivel 4 con nombre.

| # | Cómo se juzga (sin pantalla) | Sabotaje de una línea |
|---|---|---|
| 371 | (a) `PaletaDeJev.Todos` trae cada token con `Nombre`, `Argb`, `Significado` y `EsCromatico` (= `SaturacionHsl(Argb) ≥ 0.25`); se comprueba el valor exacto de los del plano (`FondoDelPanel = 0xFF101211`, `BordeDelPanel = 0xB8373B39`, `TextoPrimario = 0xFFECEEED`, `TextoSecundario = 0xFFADB5B2`, `TextoTerciario = 0xFF6B736F`, `Placeholder = 0xFF939594`, `BarraElegida = 0xFF60A5FA`, `BarraNoElegida = 0x8C2563EB`, `Pista = 0x0DFFFFFF`, `Cumplido = 0xE634D399`, `Ausente = 0xE6FFB224`, `AzulDelCursor = 0xFF4F8EF7`, `Candidata = 0x9438FF2E`, `Elegida = 0xE6FF476B`, `FondoDeEtiqueta = 0x7A000000`, `FondoDeEtiquetaElegida = 0xB8000000`); los cromáticos son exactamente 7 (`BarraElegida`, `BarraNoElegida`, `Cumplido`, `Ausente`, `AzulDelCursor`, `Candidata`, `Elegida`); para cada par de cromáticos con `Significado` distinto, `DistanciaDeTono(a, b) ≥ 30` **o** el par está en `ChoquesAceptados` con su porqué, y `ChoquesAceptados` son exactamente los tres azules entre sí («los tres son Jev trabajando; se distinguen por alfa y por sitio: dos viven dentro del panel y uno fuera»); no existe ningún token cuyo nombre contenga «Ocr» ni «Cian»; `ExclusionConElInspector.PuedeEncender(Cual.Overlay)` es falso con el inspector marcado activo y viceversa. (b) `Uia/UiInspector.cs` contiene `ExclusionConElInspector.`; **añadidas en la revisión del 2026-09-23, antes que su código** —la (b) de arriba se cumplía con el inspector *nombrando* la clase, y nadie en la app ponía `OverlayActivo` (M, 0 asignaciones)—: (b) en líneas de código, no en comentarios (`LineasDeCodigo`), `UiInspector.cs` pregunta `PuedeEncender(ExclusionConElInspector.Cual.Inspector)` y pone y quita `InspectorActivo`, y `Ui/Jev/VistaDeJev.cs` pregunta `PuedeEncender(ExclusionConElInspector.Cual.Overlay)` y pone `OverlayActivo = true` y `= false`; (a) `MaquinaDeLaVista.ImpedirElOverlay(porque)` con el overlay encendido por configuración deja `OverlayVisible == false` y `CajasVisibles == 0` tras `AlPintarCajas(3)`, su `Estado` dice «impedido» y el porqué, y el siguiente `Encender()` vuelve a preguntar | cambiar `Elegida` a `0xE6FFA020`: tono 34,4°, a **4,5°** de `Ausente` (M), par cromático no aceptado → 371 roja. (También rompe `Candidata → 0x943FBF6F`: S 0,50, a 15,6° de `Cumplido`.) |
| 372 | (a) un ciclo de mentira con 7 candidatas (dos con la etiqueta «Buscar», ids `3) Buscar (Button)` y `6) Buscar (Button)`), decisión `Actuar` sobre `6) Buscar (Button)` con `Alternativas` de 7 probabilidades y `MsDecidir = 399`: `EstadoDeLaDecision.De(ciclo, coste)` devuelve `Cabecera.Paso == "paso 2"`, `Detectados == "· 7 detectados"`, `Ms == "399ms"`; `Barras.Count == 5`, ordenadas de mayor a menor, `Valor` con `"0.00"` invariante; las dos «Buscar» salen como `"3) Buscar"` y `"6) Buscar"` y las demás sin número; con `Pulsada == null` exactamente **una** `Resaltada`, la de id `6) Buscar (Button)`, marcada `PorElegida`; con `Pulsada = "6"` (el número que trae la línea de progreso) la resaltada es la misma y `PorPulsada`; con `Pulsada = "3"` la resaltada pasa a `3) Buscar (Button)` —comparación por el **número del id**, `Ordinal`, por el mismo camino que `SurfaceMapTools.cs:318`— y el ticker es `"la 1.ª no estaba: pulsé la 2.ª «Buscar» (3)"`; y con 1.450 ms la cabecera dice `"1,450ms"`; **añadidas en la revisión del 2026-09-23, antes que su código** —el modelo se juzgaba con la pulsada puesta a mano y en la app nadie la ponía—: `CicloDeJev.ConLaLinea(decidido, objetivo, línea)` con `"paso 2: «Buscar» (3) conf 0.40 · cambió"` da un ciclo `Linea` con `Pulsada == "3"` y la línea tal cual, y pintado resalta la `3) Buscar (Button)` `PorPulsada` con el ticker `"la 1.ª no estaba: pulsé la 2.ª «Buscar» (3)"`; una etiqueta con paréntesis («Guardar (F5)») no confunde el número; `sin acción`, `· no pudo` y la línea de parada no traen pulsada; y sin decisión el ciclo nace del objetivo, sin decisión inventada. (b) `Ui/Jev/VistaDeJev.cs` pasa cada línea por `CicloDeJev.ConLaLinea(` en una línea de código | quitar el `OrderByDescending` de las barras, o resaltar por `Etiqueta ==` en vez de por el número del id |
| 373 | (a) tres ciclos: con `Alternativas` vacías (Luna) y con la regla local → `Resultados == null` (ni cabecera, ni medidores, ni barras); dos ciclos con `TokensFacturados = 875` → `Coste == "$0.00007"` (875·2·0,042/10⁶ redondeado a 5 decimales) y `Acumulado` crece; tras ellos, un ciclo `Fase = linea` con los mismos 875 tokens → `Coste == "$0.00007"` y `Acumulado` **no** cambia (una decisión se factura una vez: solo el ciclo `Decidido` suma; añadida en la fase 2); un ciclo con `TokensFacturados = null` → `Coste == "—"` y `Acumulado` **no** cambia; `Ausente = null` → el medidor enseña `"—"` y su relleno es 0 | devolver `"$0.00000"` cuando no hay tokens |
| 374 | (a) `TextosDeJev` expone las cadenas y `EstadoDeLaDecision` las usa: estado «mirando» → `"Mirando la pantalla"` con punto 0,82; «eligiendo» con N=150 → `"Paso 1 — 150 elementos"`; decisión con `Cumplido ≥ 0,70` → `"Jev cree que ya está (0.94). No acciono más."` con punto 0,42; `Peligro ≥ 0,50` → `"«Grabar» no se deshace (peligro 0.80). Me detengo."`; confianza bajo umbral → `"No estoy seguro (0.31). Me detengo."`; pulsada ≠ elegida → `"la 1.ª no estaba: pulsé la 2.ª «Detalles» (2)"`; una línea de progreso del tramo `"paso 3: «Detalles» (2) conf 0.91 · no cambió"` → ticker **idéntico** y punto 0,82; una línea `"tramo: 3 paso(s) · se agotó el tope de 4 paso(s)."` → ticker `"se agotó el tope de 4 paso(s)."` y punto 0,42; **añadidas en la fase 2, antes que su código**: una decisión que no actúa y no trae distribución (`"TypeSafe no contestó: …"`) → su `Porque` **tal cual** y punto 0,42; una que no actúa con distribución cuya primera **no se ofreció** (`"9) Grabar (Button)"`) → su `Porque` tal cual y 0,42, no «No estoy seguro»; una que actúa, sin pulsada ni línea → `"Pulsando «Buscar» (6)"` y 0,82; y ninguna cadena de `TextosDeJev` contiene «listo», «terminado», «hecho» ni «éxito» | escribir «Listo: ya está» en el caso cumplido |
| 375 | (a) `CajasDelOverlay.De(candidatas, pulsadaId: null)` con 5 candidatas de las que 2 vienen sin caja (`Caja == null`): devuelve **3** cajas en el orden de la lista, `SinCaja == 2`, cada una con `Etiqueta` sola (sin «2) » ni tipo) y **ninguna** rosa aunque el ciclo traiga elegida; con dos «Buscar» y `pulsadaId = "6) Buscar (Button)"` exactamente **una** `Rosa`; `ConPulsada(cajaDeLaMano)` sin candidatas (el puente) → una sola caja, rosa, con la geometría que trajo la mano y `EsLeida = true`; `Caducar()` deja la lista vacía; y una candidata con caja `EsLeida = false` (estimada) se pinta **sin** punto; **añadidas en la fase 3, antes que su código**: la pulsada como **número** (`"6"`, `"3"`, la forma de `CicloDeJev.Pulsada`) da la misma rosa que su id entero, y ni `"Buscar"` ni `"6) Guardar (Button)"` dan ninguna; la mano que pulsa **dentro** de un «Formulario» de 800×600 no lo vuelve rosa (su caja va sola) y la que calza con unos píxeles de diferencia sí es la candidata; con la 6 rosa por id y la mano sobre la caja de la 3, una sola rosa y es la 3, y dos pulsaciones sin candidatas dejan una caja; `Rect.Empty`, una caja sin ancho y una de 2×2 cuentan en `SinCaja` y no se pintan, y una mano así no deja rosa. (b) `Ui/Jev/OverlayDeJev.cs` contiene `SetWinEventHook`, `EVENT_SYSTEM_FOREGROUND` y `.Caducar()` | resaltar por `Etiqueta.Contains(...)`, o pintar las de `Caja == null` con un rect vacío, o poner la rosa en la elegida con `pulsadaId == null` |
| 376 | (a) `MedidaDelPanelDeJev.Ancho == 340`, `AltoMinimo == 64`, `SumaDePartes(0) == 57.6` (±0,05: `PaddingArriba 10` + 18 + 8 + 14 + `PaddingAbajo 7.6`), `AltoDe(0) == 64` (= `max(AltoMinimo, SumaDePartes(0))`), `AltoDe(5) == 198.6` (±0,05: partes 18/14/11/11/15×5 y huecos 8/8/(1+5)/5/5/5) y `AltoDe(3) < AltoDe(5)` con paso 20 por barra; `AnchoDeResultados == 314`; **añadida en la fase 4, antes que su código**: `AltoDe(6)` y `AltoDe(-1)` lanzan `ArgumentOutOfRangeException` (cinco barras como mucho, 372: un alto para seis mediría algo que nadie pinta). **Parte XAML** (fase 8): en un hilo STA del arnés, `XamlReader.Parse` del recurso del panel + `Measure/Arrange` da `64` sin resultados —por el `MinHeight="64"` del `Border`, citado como parte— y `198.6` con cinco barras (±0,5), y el `StackPanel` `Resultados` recibe `314.0`; si WPF no arranca en el runner, la prueba llama `NoPudeJuzgar` (fase 0): ni ✔ ni ✘, y el veredicto final lo dice | pura: `AltoMinimo` 64 → 60 (da `AltoDe(0) == 60`); XAML: quitar el `MinHeight` del `Border` (mide 57,6 sin resultados), cambiar `PaddingAbajo` a 10 (da 201 con cinco barras), o poner el `BorderThickness` en el `Border` con hijos (recorta a 312,4) |
| 377 | (a) `DondeVaElPanel.Calcular(ancla, tamaño, rcWork, escala, obstáculos, objetivo)` con `rcWork = (0,0,1920,1040)`, escala 1, panel 340×199 y ancla (600, 400): esquina superior izquierda en (656, 432) y `Esquina == AbajoDerecha`; con el ancla en (1800, 400) no cabe a la derecha → `AbajoIzquierda` y `Right ≤ 1908`; con el notch en `ReglaDeLaBandeja.ArribaAlCentro` como obstáculo y el ancla arriba al centro, el resultado no lo cruza; con `objetivo = (700, 450, 200, 40)` (inflado 8) el resultado no lo cruza si alguna esquina cabe; con un objetivo tan grande que ninguna cabe → la esquina de `rcWork` opuesta al objetivo; en todos los casos `rcWork.Contains(resultado)` y el cuadrado de 44 alrededor del ancla queda libre; con escala 1,5 los 56/32/44/12 salen multiplicados; **añadidas en la fase 4, antes que su código**: el resultado dice `JuntoAlAncla` (verdadero junto al ancla, falso en la esquina del área de trabajo); con el ancla en (100, 100) y lo pulsado en (300, 200, 1600, 820) la esquina opuesta taparía el ancla, y se va a la más lejana de lo pulsado que no la tapa: (12, 829), abajo a la izquierda; y una escala 0, NaN o −1 lanza `ArgumentOutOfRangeException`; **añadidas en la revisión del 2026-09-23, antes que su código** —el ancla era el centro de la ventana de la carita, y a 56 de él el panel caía encima de la barra y del menú—: `DondeVaElPanel.JuntoALaCarita(carita, …)` con una carita de 0×0 da lo mismo que `Calcular` con ese punto; con la carita de 292×400 en (1500, 500) → (1104, 269, 340, 199), `ArribaIzquierda`, a (56, 32) de su borde; a escala 1,5, a 84 y 48; ocupando el centro, ninguna esquina junto a ella; y en todos, dentro del área de trabajo y sin tapar la carita ni los 22 de alrededor; `MaquinaDeLaVista.Carita = (1500, 500, 292, 400)` hace nacer el panel en (1104, 269, 340, 199). (b) `VistaDeJev.cs` le da `Maquina.Carita = ` en una línea de código | devolver siempre la primera esquina sin probar las demás; o que la máquina vuelva a `Calcular(Ancla, …)` |
| 378 | (a) `OrdenEnZ.Capas` es exactamente `[Overlays, Carita, Notch, PanelDeJev, Flecha]` de abajo arriba; `VigilanteEnOrden` construido con handles de mentira y un `subir(handle)` que anota: `Tick()` llama `subir` en ese orden y una vez por ventana visible; `AlMostrar(PanelDeJev)` provoca un reordenado completo en el acto; una ventana escondida no se sube; **añadidas en la fase 7, antes que su código**: la misma ventana registrada dos veces sube una vez por tick; `Sale(handle)` saca la que se cierra —el tick no la sube aunque su «visible» diga que sí, y mostrarla después no reordena—; `AlMostrar` recibe el **handle** de la que se mostró (con la capa sola, «una de las overlays» no dice cuál) y una que no es del grupo no reordena; y con un `subir` que lanza para la carita y un «visible» que lanza para el panel, el tick no lanza, el notch y la flecha suben igual y en orden en cada tick, y el log dice cuál no subió con su tipo, su mensaje y su capa **una vez** en dos ticks. (b) `Ui/Muelle.cs` y `Ui/PanelDeAcciones.cs` contienen `SiempreDelante.EntraAlGrupo(this` y **no** contienen `.Vigilar()`; `Ui/SiempreDelante.cs` contiene `VigilanteEnOrden`; **añadidas en la fase 7**: `Ui/SiempreDelante.cs` crea exactamente **1** `new DispatcherTimer` (un solo reloj: el de cada ventana se borra, no se deja al lado) y **ningún** fuente de `windows-client/src` contiene `.Vigilar()`, no solo esos dos | invertir el orden de la lista; o quitar `EntraAlGrupo` de `Muelle.cs` (la (b) lo ve aunque `.Vigilar()` tampoco esté) |
| 379 | (a) `EstilosDeVentana.ExtendidosDelOverlay == 0x080800A0` (= `WS_EX_TRANSPARENT 0x20 | LAYERED 0x80000 | TOOLWINDOW 0x80 | NOACTIVATE 0x08000000`), `Afinidad == 0x11`; `EstilosDeVentana.UnaPorMonitor([(0,0,1920,1080), (1920,0,2560,1440)])` devuelve dos rects físicos iguales a los `rcMonitor`; `ConfiguracionDeLaVista.Leer(n => n == "U_JEV_OVERLAY" ? "si" : null).OverlayEncendido == true` y `Leer(_ => null).OverlayEncendido == false` con `Porque` que nombra la variable («overlay apagado: sin U_JEV_OVERLAY»); `new MaquinaDeLaVista(Leer(_ => null)).OverlayEncendido == false` y `Estado` dice «overlay: apagado (sin U_JEV_OVERLAY)»; con la configuración encendida, `Encender()` deja `OverlayEncendido == true` y `Estado` dice «overlay: encendido (U_JEV_OVERLAY=si)»; **añadidas en la fase 6, antes que su código**: `U_JEV_OVERLAY` a «no», «1» o en blanco deja el overlay apagado y el `Porque` dice lo que se leyó («U_JEV_OVERLAY=no», ««1»», «vacía») y no «sin U_JEV_OVERLAY»; « SI » enciende (sin mayúsculas ni espacios, como `U_DECISOR`); y con el overlay apagado `AlPintarCajas(3)` deja `CajasVisibles == 0`. (b) `Ui/Jev/OverlayDeJev.cs` contiene `EstilosDeVentana.ExtendidosDelOverlay` y `SetWindowDisplayAffinity`; `Ui/Jev/VistaDeJev.cs` contiene `ConfiguracionDeLaVista.Leer(` | quitar `NOACTIVATE` de la máscara; o que `Leer(_ => null)` devuelva encendido |
| 380 | (a) `Pantallas.DelMonitor(rcMonitorFisico, escala)` da un conversor: `AUnidad((2000, 300))` con monitor en (1920, 0) y escala 1,5 → (53.33, 200); `AFisico(AUnidad(p)) == p` (±0,01) para 20 puntos; `ReglaDeDpi.RectTrasCambio(calculado, sugerido) == calculado`; **añadida en la fase 4, antes que su código**: `DelMonitor` con escala 0, NaN o −1,5 lanza `ArgumentOutOfRangeException`. (b) el fuente bajo `windows-client/src/Ui/Jev/` no contiene `TransformFromDevice` ni `DpiScaleX`, y `OverlayDeJev.cs` contiene `OnDpiChanged` y `ReglaDeDpi.RectTrasCambio(` | no restar el origen del monitor en `AUnidad`; o quitar la llamada a `RectTrasCambio` de `OnDpiChanged` (la (b) la echa en falta) |
| 381 | (a) `PlanDeVuelo.Calcular(inicio, centroPulsado, escala, appDelante)`: con `appDelante = false` → `null`; con distancia 260 y escala 1 → `Duracion == 1.05 s` (el mínimo); con 1.500 → `2.2 s` (el máximo); con 780 → `1.5 s`; `Fin` dista `42·escala` del centro; `PosicionEn(0) == inicio`, `PosicionEn(1) == Fin`; la distancia al fin es **no creciente** en 100 muestras de p (no se devuelve); y la velocidad en p=0 y p=1 es 0 (`|PosicionEn(0.01) − PosicionEn(0)| < |PosicionEn(0.5) − PosicionEn(0.49)|`); **añadidas en la fase 5, antes que su código**: tampoco se devuelve ni deja de posarse a `42·escala` en **120 vuelos cortos** —lo pulsado a 0, 10, 60, 100 y 160 DIP, cada 30°, a escala 1 y 1,5; a 0 la flecha ya está encima y no hay dirección de llegada—; `PosicionEn(1.5) == Fin` y `PosicionEn(−0.5) == inicio`; una escala 0, NaN o −1 lanza `ArgumentOutOfRangeException`; y un inicio o un centro con NaN o infinito lanza `ArgumentException`. (b) `Ui/Jev/VistaDeJev.cs` contiene `GetForegroundWindow` y **no** contiene `appDelante: true` | cambiar `Math.Clamp(d / 520, 1.05, 2.2)` por `d / 520` a secas; o pasar `appDelante: true` |
| 382 | **Reescrita al juntar C y D (2026-09-24, §Revisiones):** (a) sobre un `SurfaceMapTools` de verdad —la pantalla de mentira de la 368 con Buscar, Crear Triage, Salir y Guardar, cajas leídas, decisor y mano inyectados—, `ObservadorDelDecisor.Oir(mapa, publicar)` dos veces devuelve `true` y `false` y un `map_decidir` llega como **1** ciclo; con «4) Guardar (Button)» a 0,99 y peligro 0 el ciclo trae `Actuar=false`, `Veto` no vacío y ninguna pulsada, y pintado dice «Vetada: …» sin «Pulsando» y sin barra resaltada; con la mano que cambia de sitio la pulsada es «2» y **ninguna** de las 4 candidatas lleva caja; con la que no cambia, «2» y las 4 con su caja leída, y `CajasDelOverlay.De` da una sola rosa, la de «2) Crear Triage…», y la línea de ese paso (`ConLaLinea`) da por `CajasDelOverlay.DelCiclo` las mismas 4 cajas con la misma rosa, y un ciclo «Mirando», `null` —el evento y su línea llegan pegados y el conector pinta solo el último—; con la elegida que no está y la segunda a 0,35, la pulsada es «3» y el ticker dice «la 1.ª no estaba: pulsé la 2.ª «Salir» (3)»; con la mano que no termina, ninguna pulsada; y un `publicar` que lanza no sale del `map_decidir`, que cuenta lo que hizo la mano, y queda en el log con su tipo y su mensaje. La traducción `CicloDe(objetivo, ofrecidas, decision, msDecidir, cajas)` se juzga como abajo y además copia el `Veto` de una `ConVeto`. (b) bajo `Ui/Jev` ningún fuente tiene `.Decisor =` ni `Envolver(` en código, y `FaceWindow.Jev.cs` lleva `ObservadorDelDecisor.Oir(` hasta `Publicar` y ni `.Decisor =` ni `Envolver(`; `VistaDeJev.cs` pinta el overlay con `CajasDelOverlay.DelCiclo(`. **Lo que juzgaba antes, y ya no, porque el puente se fue:** ~~(a) `ObservadorDelDecisor.Envolver(interno, alDecidir)`: llamar al envuelto devuelve **el mismo objeto** `DecisionDeUnPaso` que devolvió el interno, `alDecidir` recibió un ciclo con `MsDecidir ≥ 0` y las mismas ids, un interno que lanza `InvalidOperationException` la deja pasar tal cual (mismo tipo, mismo mensaje), y `Envolver(Envolver(f))` tiene por `Interno` a `f` (no a un envoltorio: `EstaEnvuelto` distingue)~~; **añadidas en la fase 6, antes que su código** —la traducción, `ObservadorDelDecisor.CicloDe(objetivo, ofrecidas, decision, msDecidir, cajas)`, cuyos argumentos son los campos del `PasoDecidido` de la 368—: una decisión que no actúa con distribución lleva por `Puerta` la primera de la distribución y no «», y conserva `Cumplido` y `Peligro`; sin distribución `Cumplido` es `null`, y `Ausente` es `null` siempre; el ciclo sale `Decidido`, sin `Pulsada` ni `TokensFacturados` y con los ms y el objetivo que llegaron; etiqueta y tipo salen del id por el camino que lo formó («3) Guardar (F5) (Button)» → «Guardar (F5)» + «Button»; «Grabar» → «Grabar» sin tipo), sin caja y sin `EsLeida`; con las cajas del evento, la leída va con su caja y `EsLeida` y la `Rect.Empty` sin caja, y si no son tantas como las ofrecidas lanza `ArgumentException` diciendo cuántas de cada; ~~y un `alDecidir` que lanza no toca la decisión (el mismo objeto) y queda en el log con su tipo y su mensaje~~ (ahora es el caso del `publicar` que lanza, sobre el mapa). `ConectorDeLaVista(IDespachador)` con dos operaciones, `Encolar(Action)` y `Ahora(Action)`: con un doble cuyo `Ahora` lanza, `Publicar(ciclo)` no lanza; con un doble que acumula sin ejecutar, 10 `Publicar` seguidos y luego `Vaciar()` → el pintor recibe **1** ciclo, el último; con un doble que no ejecuta nunca, `Publicar` retorna (sin medir ms); un pintor que lanza deja en el log una línea con `InvalidOperationException` y el mensaje, y `Publicar` no lanza. (b) bajo `windows-client/src/Ui/Jev/` hay exactamente **1** aparición de `BeginInvoke` (en `DespachadorDeWpf.cs`) y **0** de `Dispatcher.Invoke(` | llamar `Ahora` en vez de `Encolar` en el conector (el doble lanza), pintar la cola entera, o suscribirse sin mirar si ya se oía, traducir con las cajas de un paso que cambió lo que se ve, o sacar la pulsada de la línea en vez del paso |
| 383 | (a) `MaquinaDeLaVista` con las tres visibles: `Apagar()` → `OverlayVisible`, `PanelVisible` y `FlechaVisible` en `false`; con corrida en marcha, `Suelta()` → overlay vacío (`CajasVisibles == 0`), `FlechaVisible == false`, `PanelEnCorrida == false` y el panel sigue visible; la promesa 290 sigue verde tal cual (apagar el interruptor no cambia por esta rama); **añadidas en la fase 6, antes que su código**: con Jev **apagado**, un tramo, tres cajas y una pulsación no encienden nada (la mano pulsa también para Luna y `UiaSurface.Pulso` avisa igual); y con `AlConocerPulsada(caja, appDelante: false)` la flecha no se enseña y el panel se aparta de la caja igual. (b) `Ui/FaceWindow.Jev.cs` contiene `Senalador.Suelta +=` y `Freno.SePulso +=` (Escape siempre, haya tramo o no: `Freno.cs:82`) y las dos llevan a `.Suelta()`; **añadidas en la revisión del 2026-09-23, antes que su código** —la máquina decía «sin corrida» y la vista pintaba igual: la línea de parada llega después de Escape y traía de vuelta objetivo y barras—: (a) `MaquinaDeLaVista.QueSePinta(ciclo)` en corrida devuelve el mismo ciclo; tras `Suelta()`, la línea «tramo: 2 paso(s) · paraste tú con Escape; no sigo.» se pinta sin objetivo ni resultados y con el ticker «paraste tú con Escape; no sigo.»; una decisión que llega después no se pinta; y con Jev apagado no se pinta ni el motivo. (b) `VistaDeJev.cs` pinta lo que dice `Maquina.QueSePinta(` en una línea de código | que `Apagar()` deje `PanelVisible = true`; o quitar la suscripción a `Freno.SePulso`; o que `QueSePinta` devuelva el ciclo con Jev encendido, esté o no en corrida |
| 384 | (a) `ReglaDeQuienVuela.LaCaritaViaja(enTramoConJev: true) == false` y `(false) == true`; la promesa 240 sigue verde (su fixture no está en tramo) y la 163 también (la carita no cambia de tamaño: esta rama no toca su ventana); **añadida en la fase 6, antes que su código**: `MaquinaDeLaVista.EnTramo` —lo que `FaceWindow` le pasará a la regla— es falso en un tramo con Jev apagado, cierto si Jev se enciende a mitad, sigue cierto tras `Suelta()` (el tramo lo termina él), falso tras `AlTerminarTramo()`, cierto en el siguiente tramo y falso tras `Apagar()`; **añadidas en la revisión del 2026-09-23, antes que su código** —la carita miraba «en tramo» y la flecha «encendido», así que con Jev encendido y sin tramo volaban las dos al mismo clic—: con Jev encendido y sin tramo, `AlConocerPulsada(caja, true)` deja `FlechaVisible == false` y `LaCaritaViaja(EnTramo) == true`; en un tramo con Jev, al revés. (b) `Ui/FaceWindow.xaml.cs` contiene exactamente **2** líneas con `ReglaDeQuienVuela.LaCaritaViaja(`, y `Ui/Jev/VistaDeJev.cs` contiene `if (!Maquina.EnTramo) return;` en una línea de código | devolver siempre `true`; o borrar uno de los dos `if`; o que `AlConocerPulsada` vuelva a mirar `Encendida` |
| 385 | (a) `EstilosDeVentana.ExtendidosDelPanel` incluye `WS_EX_TRANSPARENT` y `WS_EX_NOACTIVATE` sin condición de estado; `MaquinaDeLaVista.Encender()` deja `PanelVisible == true` (nace con Jev); `MaquinaDeLaVista.AlConocerPulsada(caja)` recoloca el panel llamando a `DondeVaElPanel` con esa caja como `objetivo` (se comprueba que el rect nuevo no la cruza); **añadidas en la fase 6, antes que su código**: al encender con la pantalla puesta, `RectDelPanel` es ya el de `DondeVaElPanel` junto al ancla, (656, 432, 340, 199), y sin área de trabajo ni tamaño es `null` (no se inventa un sitio). (b) en `Ui/FaceWindow.xaml.cs`, el cuerpo de `InvocarPorAtajo` (del `private void InvocarPorAtajo()` al siguiente `private void`) **no** contiene «Jev» ni `_vistaDeJev`, y abre el chat con el foco, `AbrirChat(true)` —hasta la fase 6 decía `ShowTalk(`, que `main` sustituyó en #113 (Hallazgos, fase 6)—; **añadidas en la revisión del 2026-09-23, antes que su código** —el sitio se calculaba siempre con el alto del panel vacío (64) y el panel crece a 198,6; y el panel, opaco y dentro del área de trabajo, salía en las fotos que se mandan al modelo—: (a) `MaquinaDeLaVista.AlPintarBarras(5)` deja `TamanoDelPanel` en 340×198,6 (510×297,9 a escala 1,5) y un `RectDelPanel` de ese alto dentro del área de trabajo y sin tapar el ancla junto al borde de abajo, y al crecer se recoloca sin cruzar lo pulsado que ya conocía; en un tramo, porque una pulsación fuera de tramo ya no aparta nada (384). (b) `VistaDeJev.cs` llama `Maquina.AlPintarBarras(` y `PanelDeJev.cs` llama `SetWindowDisplayAffinity(_handle, EstilosDeVentana.Afinidad)`, las dos en líneas de código | que el panel en reposo pierda `WS_EX_TRANSPARENT`; o meter un `if` de Jev en `InvocarPorAtajo`; o que `AlPintarBarras` calcule con `AltoDe(0)` |

## Diseño

### Las piezas, y dónde viven

Todo lo nuevo va bajo **`windows-client/src/Ui/Jev/`** (espacio `U.WindowsClient.Ui.Jev`), salvo el parcial de
`FaceWindow` y los retoques a piezas que ya existen.

| Pieza | Qué es | ¿Pura? | Promesas |
|---|---|---|---|
| `PaletaDeJev` | los tokens del plano en `uint` ARGB, con significado, `EsCromatico`, `SaturacionHsl`, `DistanciaDeTono`, `ChoquesAceptados` (los tres azules) | sí | 371 |
| `ExclusionConElInspector` | dos banderas y `PuedeEncender(cual)`: el overlay de Jev y `UiInspector` nunca a la vez | sí | 371 |
| `CicloDeJev` | el registro de entrada: `Objetivo`, `Paso`, `Candidatas(Id, Etiqueta, Tipo, Caja?, EsLeida)`, `Decision?(Actuar, Puerta, Confianza, Alternativas, Cumplido?, Ausente?, Peligro, Porque)`, **`Pulsada?`** (número del id, de la línea de progreso) y **`CajaPulsada?`** (de la mano), `MsDecidir`, `TokensFacturados?`, `Fase`, `Linea` (progreso del tramo, tal cual) | datos | 372–375 |
| `EstadoDeLaDecision` | `De(ciclo, coste) → LoQuePinta(Objetivo, Ticker, Punto, Resultados?(Cabecera, Medidores, Barras))`; la resaltada es la pulsada, la elegida solo mientras no haya pulsada | sí | 372, 373, 374 |
| `TextosDeJev` | las cadenas, una por estado, con formato invariante; incluye «la 1.ª no estaba: pulsé la 2.ª «x» (n)» | sí | 374 |
| `CosteDeJev` | `PrecioPorMillon = 0.042`, `Acumular(tokens?)`, `Texto` («$0.00004» / «—») | sí | 373 |
| `CajasDelOverlay` | `De(candidatas, pulsadaId?) → cajas`, `ConPulsada(caja)`, `SinCaja`, `Caducar()` | sí | 375 |
| `MedidaDelPanelDeJev` | constantes, `AltoMinimo = 64`, `SumaDePartes(barras)` y `AltoDe(barras) = max(AltoMinimo, SumaDePartes)` | sí | 376 |
| `DondeVaElPanel` | `Calcular(ancla, tamaño, rcWork, escala, obstáculos, objetivo?) → (Rect, Esquina)` | sí | 377, 385 |
| `OrdenEnZ` + `VigilanteEnOrden` | la lista de capas y el vigilante de grupo con `subir` inyectable: `Registra(handle, capa, visible)`, `Sale(handle)`, `Tick()`, `AlMostrar(handle)` (fase 7) | sí | 378 |
| `EstilosDeVentana` | las máscaras `GWL_EXSTYLE`, la afinidad, `UnaPorMonitor` | sí | 379, 385 |
| `ConfiguracionDeLaVista` | `Leer(entorno)` → `OverlayEncendido` + `Porque`, como `ConfiguracionDelDecisor.Leer`; lee `U_JEV_OVERLAY` | sí | 379 |
| `ReglaDeDpi` | `RectTrasCambio(calculado, sugerido)` | sí | 380 |
| `PlanDeVuelo` | la curva del vuelo en físicos, con `s_fin` | sí | 381 |
| `ObservadorDelDecisor` | `Envolver(interno, alDecidir)`: cronometra, arma el ciclo, devuelve la decisión intacta; idempotente (`Interno`, `EstaEnvuelto`) | sí | 382 |
| `IDespachador` + `ConectorDeLaVista` | `Encolar`/`Ahora`; `Publicar(ciclo)` solo encola, con *coalescing*; catch que cuenta | sí (despachador inyectable) | 382 |
| `DespachadorDeWpf` | el adaptador: `Encolar` → `Dispatcher.BeginInvoke`; el único `BeginInvoke` bajo `Ui/Jev/` | no | 382 (por fuente) |
| `MaquinaDeLaVista` | el estado: encendida, overlay encendido (por configuración), en tramo, visibles, `Encender`, `Apagar`, `Suelta`, `AlConocerPulsada` | sí | 379, 383, 385 |
| `ReglaDeQuienVuela` | una regla de una línea, para que el `if` de `FaceWindow` no sea código suelto | sí | 384 |
| `OverlayDeJev`, `PanelDeJev`, `FlechaDeJev` | las tres `Window` | no: solo dibujan lo que el modelo devuelve; sus ganchos se juzgan por fuente | 375, 376 (XAML), 379, 380, 381 |
| `VistaDeJev` | el coordinador: crea las ventanas, sostiene conector y máquina, lee la configuración, pregunta a `GetForegroundWindow` antes de volar, se sincroniza con el interruptor | no; por fuente | 379, 381 |
| `Ui/FaceWindow.Jev.cs` | parcial, como `FaceWindow.Escritorio.cs`: todo el cableado que cabe fuera del archivo grande, incluidas las suscripciones a `UiaSurface.Pulso`, `Senalador.Suelta` y `Freno.SePulso` | no; por fuente | 383 |
| `Ui/Pantallas.cs` | añade la **inversa** por monitor (`DelMonitor`, `AUnidad`, `AFisico`) | sí | 380 |
| `Ui/SiempreDelante.cs` | añade `EntraAlGrupo(Window, Capa)` sobre `VigilanteEnOrden` —el reloj nace con la primera ventana que entra, sin un `VigilarEnOrden()` aparte— y **borra** el reloj por ventana (fase 7) | no | 378 |
| `Ui/Muelle.cs:175`, `Ui/PanelDeAcciones.cs:247` | `this.Vigilar()` → `SiempreDelante.EntraAlGrupo(this, Capa.Carita/Notch)` | — | 378 |
| `Uia/UiInspector.cs:59-63` | `Toggle` pregunta a `ExclusionConElInspector` | — | 371 |
| `tests/ContratoDelGrafo/Contrato.cs` | `NoPudeJuzgar(que, promesa)` + `_sinJuzgar` + tercer veredicto; `EnSta`/`MedirXaml` | — | fase 0 |

### El ciclo, visto desde la vista

```
AlEmpezarTramo("tramo: <objetivo>")  → panel: Objetivo, ticker «Mirando la pantalla», punto 0,82, Resultados como estaban
Decisor(pantalla, objetivo, ids)     → ObservadorDelDecisor: cronómetro → decisión intacta → ciclo «decidido» (Pulsada = null)
                                       → ConectorDeLaVista.Publicar (Encolar → BeginInvoke en el adaptador, coalescing)
                                       → EstadoDeLaDecision.De → panel: cabecera · medidores · 5 barras · la elegida marcada «por elegida»
                                       → CajasDelOverlay.De   → overlay: cajas verdes (solo con caja leída); NINGUNA rosa todavía
UiaSurface.Pulso(x, y, w, h)         → SOLO EN UN TRAMO CON JEV (fuera de él pulsó Luna o el player, y viaja la carita: 384)
                                       → ciclo «pulsado» con CajaPulsada (geometría real de la mano)
                                       → overlay: la rosa sobre esa caja (por id si la caja calza con una candidata; sola si no)
                                       → PlanDeVuelo.Calcular(…, appDelante: GetForegroundWindow() es la de trabajo) → flecha vuela MIENTRAS la mano ya pulsó
                                       → MaquinaDeLaVista.AlConocerPulsada → el panel se aparta si la tapa
Progreso("paso k: «x» (n) conf … · cambió")  → ciclo con Pulsada = n → panel: la resaltada es la n; si n ≠ elegida, ticker
                                               «la 1.ª no estaba: pulsé la 2.ª «x» (n)»; si no, la línea tal cual; punto 0,82
Progreso("tramo: N paso(s) · <motivo>")      → ticker: el motivo tal cual, punto 0,42, ninguna barra resaltada
AlTerminarTramo()                    → flecha vuelve/se esconde; overlay se queda hasta que cambie la ventana de delante
Escape (Freno.SePulso) / Senalador.Suelta → overlay vacío, flecha escondida, panel sin corrida (383); lo que llegue
                                       después —la línea con que el tramo para— se pinta SOLO como motivo: sin objetivo
                                       ni barras (MaquinaDeLaVista.QueSePinta), y una decisión tardía no se pinta
Apagar Jev (PintarBotonJev off → Sincronizar(false)) → las tres ventanas cerradas (383)
```

Cuatro reglas del plano que este diseño respeta y hay que poder señalar en el código:

- **Lo que se pinta es exactamente lo que se ofreció** (285, aprendizaje nº16): las candidatas del ciclo son las ids
  que `UnPasoDecidido` numera (`SurfaceMapTools.cs:266-274`), y la pulsada se compara por **el número del id**, por
  el mismo camino que `SurfaceMapTools.cs:318` (`id.Substring(0, id.IndexOf(')'))`); la elegida por
  `string.Equals(Ordinal)` con la id, como `ElDecisor.cs:203-205`.
- **Lo resaltado y lo pulsado coinciden** (plano §El estado «elegido»): la rosa y la flecha nacen con `UiaSurface.Pulso`,
  no con la decisión. La elegida antes del clic solo se marca en el panel, y como «elegida», no como pulsada.
- **El overlay caduca al cambiar la ventana de delante** (`SetWinEventHook(EVENT_SYSTEM_FOREGROUND)`), al revés que
  TipTour (t=73, cajas de la terminal sobre Music).
- **Las cajas quedan debajo del panel**, al revés que TipTour y a propósito: el panel es opaco y lo que tapa no se pulsa.
  Si el dueño quiere el calco, es mover un elemento en `OrdenEnZ.Capas`.

### El puente provisional hasta que C entre

> **Retirado el 2026-09-24, al juntar A, B, C y D en la rama de pruebas `jero/jev-todo-junto`** (§Revisiones, entrada
> de esa fecha). Con C dentro, el parcial se suscribe a `AlDecidir` con `ObservadorDelDecisor.Oir` y ya no decora el
> `Decisor`; la pulsada llega con el paso del evento (`Paso.Numero`) y la línea de progreso ya no se lee para sacarla.
> Las dos partes sobraban y las dos mentían juntas con A y B: el envoltorio ve la decisión **antes** del veto de la 390
> —con «Guardar» a 0,99 el panel habría dicho «Pulsando «Guardar»»— y la regex de la línea no casaba con las palabras
> de la 353 («cambió de sitio», «dentro», «delante»), así que toda pulsación que cambió algo perdía la pulsada. Lo que
> **no** se retira: `UiaSurface.Pulso`. No es parte de la decisión sino del clic, y es el único aviso que llega AL
> pulsar —el evento sale al terminar el paso, espera incluida—: la flecha sigue volando por él (381). Lo de abajo queda
> como estaba escrito, para que se entienda qué juzgaba la 382 antes.

`AlDecidir` (368) y la candidata con caja (365) son de C. Mientras no estén en `main`, `FaceWindow.Jev.cs` decora el
`Decisor` del mapa con `ObservadorDelDecisor.Envolver` **desde fuera** (`Mcp/SurfaceMapTools.cs:1722` es una
propiedad pública; ni `Decision/` ni `Navigation/` ni `SurfaceMapTools` se tocan). Tres cosas que el puente tiene que
hacer bien, y las tres se juzgan:

1. **Envolver después del interruptor, y una sola vez.** `InterruptorDelDecisor` reasigna `_mapa.Decisor` en cada
   `Encender` (`:66`) y `Apagar` (`:81`): un envoltorio puesto antes desaparece. Por eso `Sincronizar(on)` cuelga de
   `PintarBotonJev`, que corre **después** de las dos (`FaceWindow.xaml.cs:476-477, 2872-2882`), y `Envolver` es
   idempotente: si lo que hay ya es un envoltorio, no envuelve otra vez (382).
2. **Lo pulsado no viene por la decisión.** La costura solo ve `d.Puerta`; la mano puede pulsar la segunda mejor
   (`SurfaceMapTools.cs:304-309`). El puente saca la pulsada de dos sitios que ya existen: **su caja** de
   `UiaSurface.Pulso` (la mano avisa en físicos al pulsar, `UiaSurface.cs:139`) y **su número** de la línea de
   progreso `paso k: «x» (n) …` (`ElTramo.cs:204`), que ya llega a la vista por `Progreso`.
3. **Lo que no se ve, se dice.** El puente no ve las cajas de las candidatas ni los tokens facturados: el overlay no
   pinta ninguna caja verde y el panel dice «· N detectados (N sin caja)»; el coste dice «—» hasta que la decisión
   traiga `input_tokens` (350 de A, 367 de C). Es la regla de la 373 y de la 375 funcionando, no un fallo.

Cuando C entre, el parcial se suscribe a `AlDecidir` en lugar de decorar, y las candidatas llegan con caja: las cajas
verdes aparecen sin cambiar el modelo. **Y a C se le pide una cosa** (Hallazgos): que el evento 368 lleve también la
pulsada, para que el puente por `Progreso` deje de hacer falta.

### Lo que pinta cada ventana, con sus números

**`OverlayDeJev`** — una por monitor, `rcMonitor` en físicos por `SetWindowPos`, `EstilosDeVentana.ExtendidosDelOverlay`,
`WDA_EXCLUDEFROMCAPTURE` con línea de log si falla (como `AuraDeAprendizaje.cs:161-162`). Cuatro capas
`DrawingVisual` (plano §Cómo se pinta): fijo, pulso (una sola `DoubleAnimation` 0→1 de 1,428 s `AutoReverse`
`SineEase EaseInOut`), etiqueta, cajas. Una caja: radio 6, trazo 0,9 a `Candidata`, halos +3,8 y +1,6, relleno 0,018,
corchetes `clamp(0,28·min(w,h), 8, 18)`; etiqueta si ≥ 42×12, Consolas 8,3 `Normal`, cápsula `FondoDeEtiqueta` de alto
14,5 con ancho `min(FormattedText + 14, max(58, anchoPantalla − x1 − 8))`; punto de 3,0 solo si la geometría es
**leída** (aprendizaje nº4). La pulsada: `Elegida` (el token conserva su nombre del plano) trazo 2,0, relleno 0,08,
etiqueta Consolas 10,5 `Bold` blanca sobre `FondoDeEtiquetaElegida`, sin punto. Texto con `TextRenderingMode=Grayscale`
y `TextFormattingMode=Display`. `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` → `CajasDelOverlay.Caducar()`;
`OnDpiChanged` → `ReglaDeDpi.RectTrasCambio`.

**`PanelDeJev`** — 340 de ancho, `SizeToContent=Height` con `MinHeight="64"` en el `Border` (el mínimo declarado de la
376), fondo opaco `FondoDelPanel` (desviación declarada frente al 0,96 del vídeo: `Nitida()` exige opaco,
`Estudio.cs:431`), trazo 0,8 en un `Border` superpuesto sin hijos, **sin sombra**. Filas del XAML del plano: entrada 18
(glifo ⌘ en `Segoe UI Symbol` 11 sin peso, objetivo en Segoe UI Variable Text 13), ticker 14 (punto 4×4 a 0,82/0,42,
texto 11), divisor 1, cabecera 11 (Consolas 10 `Normal`, `BlockLineHeight=11`), medidores 11 (título Segoe 9 `Normal`,
pista 64×4 `Pista` con relleno `max(2, 64·v)`, valor Consolas 10), cinco barras de 15 con paso 20 (etiqueta en 118,
pista 150×5, relleno `max(2, 150·p)`, `SemiBold` + `TextoPrimario` solo en la resaltada). Padding 13/10/13/7,6.
Cambios de texto, barras y alto con 0,16 s *easeInOut*. Números con `InvariantCulture`; ms con `"N0"`; `$` con
`"0.00000"`. Siempre `ExtendidosDelPanel` (click-through, no activable): **no tiene campo de texto** (ver *Lo que NO
entra*), así que nunca necesita el foco. **Nace al encender Jev** (`Sincronizar(true)` → `MaquinaDeLaVista.Encender`)
y muere al apagarlo (383); el atajo de invocar a Ü no lo conoce (385).

**`FlechaDeJev`** — ventana propia de 128 de alto y hasta 544 de ancho (píldora `MaxWidth=240` con elipsis), centrada
en la flecha y movida con `SetWindowPos` cada fotograma con `SWP_NOZORDER`… salvo que va arriba del todo y puede
pasar `HWND_TOPMOST`. Silueta Lucide `mouse-pointer-2` en caja de 24 escalada con `Geometry.Transform` (44/24), trazo
4,2 `AzulDelCursor`, relleno blanco, dos halos `BlurEffect` 16 y 6 (equivalencia con SwiftUI **sin medir**); píldora
`CornerRadius 6`, `Padding 8,4`, Segoe 11 `Normal` blanca. Modos: siguiendo (ratón + (35, 25)·s, solo en reposo con
Jev encendido), volando (`PlanDeVuelo`), señalando (3 s y vuelve; escala 1,035 cada 1,4 s). **El clic no espera al
vuelo**: `PlanDeVuelo` se lanza desde el pintor al recibir `UiaSurface.Pulso`, y la mano ya salió. `appDelante` sale
de `GetForegroundWindow()` comparado con la ventana de trabajo, nunca de una constante.

### Orden en Z y DPI

`SiempreDelante.EntraAlGrupo(window, capa)`: un reloj de 3 s, uno para todo el grupo y nacido con la primera ventana
que entra, que en cada tick sube en el orden de `OrdenEnZ.Capas` con `SetWindowPos(HWND_TOPMOST,
SWP_NOMOVE|NOSIZE|NOACTIVATE)`, y reordena en el acto tras cada `Show()` del grupo (`IsVisibleChanged`). La ventana se
registra con su handle en cuanto lo tiene (`SourceInitialized`) y sale al cerrarse (`Closed`). **Así quedó en la
fase 7**: sin un `VigilarEnOrden()` aparte, y con el `Vigilar` por ventana borrado. `Muelle.cs:175` y
`PanelDeAcciones.cs:247` cambian `this.Vigilar()` por `EntraAlGrupo` (**2 sitios**, patrón nº5; la (b) de la 378
exige la llamada nueva, no solo la ausencia de la vieja). El grupo se arma al
arrancar aunque Jev esté apagado —la carita y el notch siguen necesitando vigilancia— y las tres de Jev entran cuando
nacen.

Posiciones en **físicos** del escritorio virtual, longitudes de diseño en DIP × la escala del monitor que manda, todo
por `SetWindowPos`, márgenes contra `rcWork`; `OnDpiChanged` reaplica el rect calculado (`ReglaDeDpi`). Las 7
conversiones a mano de hoy **no se tocan** en esta rama (no son suyas) y quedan contadas como deuda; bajo `Ui/Jev/`
no nace ninguna (380).

### El cableado en `FaceWindow.xaml.cs`: contado

Es la zona de choque alta del repo; el parcial `FaceWindow.Jev.cs` se lleva todo lo que puede, incluidas las
suscripciones a `UiaSurface.Pulso`, `Senalador.Suelta` y `Freno.SePulso` (0 líneas en el archivo grande). Lo que queda
en el archivo grande, y va contado en el commit de la fase 9 (**≤ 7 líneas**):

| Dónde (líneas de `bb3d0dc`) | Qué | Líneas |
|---|---|---|
| junto a `:457` (nace el interruptor) | `_vistaDeJev = new VistaDeJev(mcp.Map, …)` | +1 |
| `:462` y `:463` (`AlEmpezarTramo`/`AlTerminarTramo`) | además de `Freno`, avisan a la vista (objetivo y fin de tramo) | 2 editadas |
| `:464-469` (`Progreso`) | la misma línea que va al notch va al ticker, tal cual | +1 |
| `:1883` (`OnAutomationCursorMoved`) y `:4667` (`IrJuntoA` con `alClic`) | `if (!ReglaDeQuienVuela.LaCaritaViaja(_vistaDeJev?.EnTramo == true)) return;` | +2 |
| `:2895` (`PintarBotonJev`) | `SetStatus("Jev " + Estado + _vistaDeJev?.Sincronizar(on))`: envuelve/desenvuelve el `Decisor` (idempotente), abre/cierra las tres ventanas, arma el grupo en Z la primera vez, y devuelve su estado («· overlay apagado (sin U_JEV_OVERLAY)») para la línea de estado | 1 editada |

**`InvocarPorAtajo` (`:1587-1600`) no se toca** (§Revisiones, 3). Cruces con las otras ramas en este archivo: B toca
`768-798`, C `434, 508-565, 5570-5684`; ninguna de las líneas de arriba está a menos de 10 de esas. D entra después
de C y rebasa.

**Así quedó en la fase 9: 4 líneas añadidas y 0 editadas** (M, `git diff --numstat`: `4 0`), no 7. Las tres de los
avisos del tramo (`AlEmpezarTramo`, `AlTerminarTramo`, `Progreso`) no se tocan: `NacerLaVistaDeJev` las **encadena**
desde el parcial —primero lo que había, el freno y el notch; después la vista—, porque `ElTramo` lee esas propiedades
en cada aviso (`SurfaceMapTools.cs:1751-1756`, L). Por eso la línea que nace va **después** de ellas (junto a `:477`,
no en `:457`), y lo dice en su comentario. `PintarBotonJev` no escribe en la línea de estado: la vista lo lleva al log
(`jev-vista: Jev encendido · …`), y su línea va **antes** del `if (JevBtn == null) return;`, para que un botón sin
pintar no deje la vista sin sincronizar. Lo que se paga, y se dice: quien reasigne uno de los tres avisos después de
esa línea deja a la vista sin él (hoy nadie: 3 asignaciones, en `:463-465`, M).

| Dónde (líneas de la fase 9) | Qué | Líneas |
|---|---|---|
| `:477` | `NacerLaVistaDeJev(mcp.Map);` —después de los avisos del tramo, que encadena— | +1 |
| `:1906` (`OnAutomationCursorMoved`, dentro del `BeginInvoke`) | `if (!Jev.ReglaDeQuienVuela.LaCaritaViaja(_vistaDeJev?.EnTramo == true)) return;` | +1 |
| `:2874` (`PintarBotonJev`) | `SincronizarLaVistaDeJev(_interruptorDelDecisor?.Encendido == true);` | +1 |
| `:4606` (`IrJuntoA`) | `if (alClic && !Jev.ReglaDeQuienVuela.LaCaritaViaja(_vistaDeJev?.EnTramo == true)) return;` | +1 |

## Las fases

Una fase = un commit que pone verde sus promesas sin romper las anteriores. Antes de la fase 1 va la etapa
`/promesas`: las 15 en `Contrato.cs`, todas `PENDIENTE`, y el veredicto literal «CONTRATO ROTO: 15 promesa(s)
incumplida(s)» pegado en el commit `test(jev-vista): …`.

### Fase 0 — el arnés mide XAML sin pantalla y sabe decir «no pude»

| | |
|---|---|
| **Promesa que pone verde** | ninguna: habilita la parte XAML de la 376 y la clase de juicio (b) |
| **Qué toca** | `tests/ContratoDelGrafo/Contrato.cs`: ayudantes `EnSta(Action)` + `MedirXaml(string)`; `NoPudeJuzgar(que, promesa)` que incrementa `_sinJuzgar` (nuevo) e imprime `⚠ SIN JUZGAR:`; el veredicto final pasa a tres: `CONTRATO ROTO` si `_fallos > 0` (y dice además cuántas quedaron sin juzgar), `CONTRATO SIN VEREDICTO COMPLETO: N promesa(s) sin juzgar` con código **99** si `_fallos == 0 && _sinJuzgar > 0`, `CONTRATO INTACTO` solo si las dos son 0; los **3** sitios de hoy (`:911`, `:925`, `:7912`) pasan a `NoPudeJuzgar`. `.github/workflows/contrato.yml` y `scripts/verificar.ps1`: una columna más, `Select-String 'SIN JUZGAR:'`, en el resumen |
| **¿Núcleo congelado?** | no |
| **Terminado** | un `TextBlock` de Consolas 10 medido en el arnés da alto 11,71 (±0,1), que es lo que midió `medir-panel.ps1`; forzando `U_REPO` vacío, la 164 imprime `SIN JUZGAR` y el script sale con **99** (no con 1 ni con 0) y `verificar.ps1` rotula «NO CORRIDO» (`:135`); con `U_REPO` puesto, INTACTO como antes |
| **Sitios con esta clase de error** | 3 sitios con «no pude» que dicen inocente (2: `:911`, `:925`) o culpable (1: `:7912`); 0 de medir XAML (grep `XamlReader\|\.Measure\(` vacío) |

### Fase 1 — la paleta, y el inspector que no se enciende encima

| | |
|---|---|
| **Promesa que pone verde** | 371 |
| **Qué toca** | `Ui/Jev/PaletaDeJev.cs`, `Ui/Jev/ExclusionConElInspector.cs` (nuevos); `Uia/UiInspector.cs:59-63` |
| **¿Núcleo congelado?** | no |
| **Terminado** | 371 verde; 242 (el notch blanco y negro) intacta; sabotaje `Elegida → 0xE6FFA020` verificado por diff (ROTO nombrando la 371) |
| **Sitios con esta clase de error** | choques de tono entre tokens de la propia paleta: 3 aceptados (los azules, 3,7–8,1°); choques con **otras** paletas, documentados en el código y no en `ChoquesAceptados` porque el overlay y el inspector no coinciden: 3 (`Ausente`≈`Atencion` 3,0°, `Candidata`≈shell mapeado 25,4°, `Elegida`≈`Fallo` 14,9°) |

### Fase 2 — el modelo del panel

| | |
|---|---|
| **Promesa que pone verde** | 372, 373, 374 |
| **Qué toca** | `Ui/Jev/CicloDeJev.cs`, `EstadoDeLaDecision.cs`, `TextosDeJev.cs`, `CosteDeJev.cs` (nuevos); `Contrato.cs`: **4 comprobaciones más** dentro de la 373 (1) y la 374 (3), escritas antes que el código que juzgan (Hallazgos, fase 2) |
| **¿Núcleo congelado?** | no |
| **Terminado** | 372–374 verdes; 3 sabotajes por diff; ninguna cadena concluye; con `Pulsada = "3"` y elegida `6)` el ticker dice que se pulsó la 2.ª |
| **Sitios con esta clase de error** | textos que hoy concluyen por el modelo: 1 (`ElTramo.cs:157-158`, «el objetivo ya está cumplido», que A reescribe en la 349; la vista no lo repite); resaltados que ignoran la segunda mejor: 0 hoy (no hay vista), 0 después; **medido al cerrarla:** derivar el número de un id, 2 sitios en el cliente (`SurfaceMapTools.cs:318` y `CandidataDeJev.NumeroDelId`), los dos con `Substring(0, IndexOf(')'))`; comparaciones de identidad en el modelo, 6, todas `Ordinal` y por el mismo camino (3 por número del id, 3 por id tal cual) más 1 por etiqueta a propósito (numerar solo si dos de las cinco la comparten); ramas del ticker que afirmaban una causa que no podían distinguir, 2 (sin distribución y elegida no ofrecida decían «No estoy seguro»), cerradas con su comprobación vista roja primero |

### Fase 3 — el modelo del overlay

| | |
|---|---|
| **Promesa que pone verde** | 375 (parte (a); la (b) se lee en la fase 8) |
| **Qué toca** | `Ui/Jev/CajasDelOverlay.cs` (nuevo) |
| **¿Núcleo congelado?** | no |
| **Terminado** | 375 (a) verde; 285 intacta (la lista es la misma); con `pulsadaId == null` ninguna rosa |
| **Sitios con esta clase de error** | resaltados por subcadena en el repo: 1 (`SurfaceMapTools.cs:299`, `Porque.Contains("cumplido")`, que A quita); en la vista, 0; **medido al cerrarla:** `.Contains(` bajo `Ui/Jev/`, 0; sitios que construyen una caja pintada, 2 (`De` y `ConPulsada`), los dos por `EsPintable`; sitios de la vista que comparan la pulsada, 4, todos `Ordinal` y ninguno por etiqueta: 3 en `EstadoDeLaDecision` por el número del id (`:120` contra la elegida, `:121` `PorNumero`, `:167` la barra) y 1 en `CajasDelOverlay.EsLaPulsada`, que compara por el camino de quien produce cada forma —número del id si llega el número, id entero si llega el id—; filtros de caja vacía de UIA en `UiaSurface.cs`, 8 (`IsEmpty`), y `Pulso` ya descarta < 1 de lado (`:148`) |
| **Estado al cerrarla** | 375 (a) verde con sus 4 comprobaciones añadidas; la promesa sigue ✘ **solo** por la (b): «falta el fuente windows-client/src/Ui/Jev/OverlayDeJev.cs», que nace en la fase 8. «CONTRATO ROTO: 11», con 375–385 en ✘ y las marcas de las otras 302 idénticas a las de la fase 2 |

### Fase 4 — la geometría pura

| | |
|---|---|
| **Promesa que pone verde** | 376 (parte pura), 377, 380 (parte (a)) |
| **Qué toca** | `Ui/Jev/MedidaDelPanelDeJev.cs`, `DondeVaElPanel.cs`, `ReglaDeDpi.cs` (nuevos); `Ui/Pantallas.cs` (inversa por monitor) |
| **¿Núcleo congelado?** | no |
| **Terminado** | las tres verdes; 249/251/260 (medida y sitio del notch) intactas; `DondeVaElPanel` recibe el notch de `ReglaDeLaBandeja.ArribaAlCentro` como obstáculo; `AltoDe(0) == 64` por el mínimo y `SumaDePartes(0) == 57.6` |
| **Sitios con esta clase de error** | conversiones DPI a mano fuera de `Pantallas`: 7 (contadas arriba); esta fase añade 0 y quita 0 (no son suyas); altos que salen del contenido: 0 (`MedidaDelNotch.AltoDe` ya es fijo); **medido al cerrarla:** `TransformFromDevice\|DpiScaleX` en código, 7 (las mismas, con las líneas corridas: `FaceWindow.Escritorio.cs:112`, `FaceWindow.xaml.cs:1906, 2212, 4617, 4641`, `InspectorOverlay.cs:100`, `PanelDeAcciones.cs:311`) y **0** bajo `Ui/Jev/`; sitios que reciben una escala, 2 (`DondeVaElPanel.Calcular` y el constructor de `ConversorDeMonitor`), los dos con la misma guarda `!double.IsFinite(escala) \|\| escala <= 0` y el mismo motivo en el mensaje; el tope de barras, 1 constante (`EstadoDeLaDecision.BarrasComoMucho`) leída en 2 sitios (el `Take` del modelo y `MedidaDelPanelDeJev.SumaDePartes`), no un 5 repetido |
| **Estado al cerrarla** | lo que dejaron sin commitear los dos intentos colgados se **revisó y se terminó**, no se descartó: `MedidaDelPanelDeJev`, `DondeVaElPanel`, `ReglaDeDpi` y `Pantallas.DelMonitor` cumplían lo que la tabla ya juzgaba; faltaba lo que pedían las **6 comprobaciones** que esos intentos escribieron en el contrato antes que su código (la guarda de barras, la de escala en dos sitios, `JuntoAlAncla` y la esquina que no tapa el ancla; Hallazgos, fase 4), vistas **rojas** contra ese código a medias (M: «CONTRATO ROTO: 16», con 6 `✘` en 376/377/380: cinco de las nuevas y la del cuadrado, que hizo saltar el caso nuevo; la sexta pasa sola sin la propiedad y la sostiene su pareja). Con el código: 377 ✔ entera; la 376 sigue ✘ **solo** por su `PENDIENTE` del XAML (fase 8) y la 380 **solo** por su (b), «OverlayDeJev.cs sobreescribe OnDpiChanged…» (fase 8), con todas sus comprobaciones puras en verde. «CONTRATO ROTO: 10» (antes 11: la 377 salió), 303 ✔, y las marcas de las otras 302 promesas idénticas a las de antes (M, `diff` de las líneas ✔/✘). Tres sabotajes por diff, uno por promesa: `AltoMinimo` 64 → 60 da 12 (dos `✘` de la 376: «el mínimo declarado es 64…: 60» y «…el mínimo, 64, no la suma: 60,00»); devolver la primera esquina sin probarla (`if (true \|\| …`) da 22 (doce `✘` de la 377); `AUnidad` sin restar el origen da 12 (dos `✘` de la 380: «(2000, 300) físicos en ese monitor son (53,33, 200)…: 1333,33;200» y «ida y vuelta dan la identidad en 20 puntos»). Los tres restaurados con `cmp` idéntico y el contrato otra vez en 10 |

### Fase 5 — el vuelo

| | |
|---|---|
| **Promesa que pone verde** | 381 (parte (a)) |
| **Qué toca** | `Ui/Jev/PlanDeVuelo.cs` (nuevo); `Contrato.cs`: **4 comprobaciones más** dentro de la 381, escritas antes que el código que juzgan (Hallazgos, fase 5) |
| **¿Núcleo congelado?** | no |
| **Terminado** | 381 (a) verde; 240 intacta (la curva de la carita no cambia) |
| **Sitios con esta clase de error** | esperas fijas por decoración: 0 en `main` desde #83 (anatomía §2); la flecha no reintroduce ninguna; **medido al cerrarla:** `Task.Delay\|Thread.Sleep\|.Wait(` bajo `Ui/Jev/`, 0 (el vuelo es una función de p: quien lo anima pone el reloj, fase 8); curvas de vuelo en el cliente, 2 y separadas —`Ui/Vuelo.cs` (la carita, 240) y `PlanDeVuelo` (la flecha)—, y esta fase no toca la primera; sitios que reciben una escala, **3** (`DondeVaElPanel.Calcular`, el constructor de `ConversorDeMonitor` y `PlanDeVuelo.Calcular`), los tres con la misma guarda `!double.IsFinite(escala) \|\| escala <= 0`; conversiones DPI a mano bajo `Ui/Jev/`, 0 (fuera, las 7 de siempre) |
| **Estado al cerrarla** | las **4 comprobaciones nuevas** de la 381 se vieron **rojas** contra una primera versión que portaba la curva del plano tal cual, sin topes ni guardas —las 8 que ya había salieron verdes con ella— (M: «CONTRATO ROTO: 14», cuatro `✘` nuevos: «fallan 96 de 120» vuelos cortos, `p=1,5` fuera de la curva en (−1004, −6272), la escala 0/NaN/−1 y el punto NaN sin lanzar), y verdes con el código final. La 381 sigue ✘ **solo** por su (b), «falta el fuente windows-client/src/Ui/Jev/VistaDeJev.cs» (fase 8), igual que la 375 y la 380. «CONTRATO ROTO: 10», 303 ✔, 0 SIN JUZGAR, y las marcas ✔/✘ de las 313 **idénticas** a las de la fase 4 (M, `diff`): 240, 249, 251 y 260 siguen ✔. Sabotaje por diff: `Math.Clamp(distancia / (DipPorSegundo * escala), DuracionMinima, DuracionMaxima)` → `distancia / (DipPorSegundo * escala)` (una línea, 9.018 → 8.974 bytes, archivo LF) da **12**, con dos `✘` nuevos y los dos en la 381 («260 físicos: … acotado al mínimo 1,05: 0,500» y «1.500 físicos: … acotado al máximo 2,2: 2,885»); restaurado con `cmp` idéntico y el contrato otra vez en 10 |

### Fase 6 — el conector, la máquina y las reglas de una línea

| | |
|---|---|
| **Promesa que pone verde** | 379 (a), 382 (a), 383 (a), 384 (a), 385 (a) |
| **Qué toca** | `Ui/Jev/ObservadorDelDecisor.cs`, `IDespachador.cs`, `ConectorDeLaVista.cs`, `MaquinaDeLaVista.cs`, `EstilosDeVentana.cs`, `ConfiguracionDeLaVista.cs`, `ReglaDeQuienVuela.cs` (nuevos); `Contrato.cs`: **22 comprobaciones más** dentro de la 379 (5), la 382 (11), la 383 (3), la 384 (1) y la 385 (2), escritas antes que el código que juzgan, y la (b) de la 385 pasa de `ShowTalk(` a `AbrirChat(true)` porque `main` cambió el atajo en #113 (Hallazgos, fase 6) |
| **¿Núcleo congelado?** | no |
| **Terminado** | las cinco verdes en su parte (a); 290 intacta; el catch del pintor cuenta tipo y mensaje (patrón nº3); `Envolver(Envolver(f)).Interno == f`; el coalescing se juzga con un despachador que acumula, sin reloj |
| **Sitios con esta clase de error** | `Dispatcher.Invoke` síncronos en la costura del tramo: 0 hoy (`FaceWindow.xaml.cs:464` ya usa `BeginInvoke`; el `Invoke` de `:414` es del localizador, no del tramo); la vista no añade ninguno. Pruebas del contrato que dependen del reloj: la 341 de la voz arrastra un `Wait(3 s)`; esta rama añade **0**; **medido al cerrarla:** la costura del tramo en `FaceWindow.xaml.cs` es `:463-465` —`AlEmpezarTramo` y `AlTerminarTramo` directos, `Progreso` por `BeginInvoke`—, con **0** `Dispatcher.Invoke(` de los **17** del archivo; bajo `Ui/Jev/`, **0** `Dispatcher.Invoke(` y **0** `BeginInvoke` (el único llega con `DespachadorDeWpf`, fase 8); `Sleep\|Task.Delay\|.Wait(` en las líneas añadidas al contrato, **0**; catches nuevos, **3** —encolar y pintar en `ConectorDeLaVista`, publicar en el envoltorio—, los tres con la cadena entera y ninguno mudo; traducciones de `DecisionDeUnPaso` a `DecisionDeJev`, **1** (`CicloDe`), la misma para el puente y para el evento de C; sitios que sacan algo de un id con `IndexOf(')')`, **3** (`SurfaceMapTools.cs:318` y `CandidataDeJev.NumeroDelId` el número; `ObservadorDelDecisor.EtiquetaYTipo` la etiqueta y el tipo), los tres por la primera «)»; lecturas de `U_JEV_OVERLAY`, **1**; sitios que reciben una escala, **3**, los mismos de la fase 5 (la máquina se la pasa a `DondeVaElPanel` y no copia una cuarta guarda); lo visible de la máquina que depende de `Encendida`, **5 de 5** (`OverlayVisible`, `PanelVisible`, `FlechaVisible`, y `CajasVisibles` y `PanelEnCorrida` a través de las dos primeras), más `EnTramo` |
| **Estado al cerrarla** | las **22 comprobaciones nuevas** se vieron contra una versión ingenua —la decisión copiada campo a campo, `U_JEV_OVERLAY` leído con `== "si"`, una máquina que no mira si Jev está encendido— y salieron **17 rojas** (M: «CONTRATO ROTO: 28»); las 5 que pasaron ya las cumplía esa versión y quedan de guardia (Hallazgos, fase 6). Con el código final: la **385 ✔ entera** —su (b) es una ausencia que ya es cierta, con la comprobación puesta al día tras #113—, y la 379, la 382, la 383 y la 384 siguen ✘ **solo** por sus (b): «falta el fuente …OverlayDeJev.cs» y «…VistaDeJev.cs» (379, fase 8), «hay 0» `BeginInvoke` en `DespachadorDeWpf.cs` (382, fase 8), «falta el fuente …FaceWindow.Jev.cs» (383, fase 9) y «hay 0» `LaCaritaViaja(` en `FaceWindow.xaml.cs` (384, fase 9), con todas sus comprobaciones (a) en verde. «CONTRATO ROTO: 10», **304 ✔** (antes 303), 0 SIN JUZGAR, y las marcas ✔/✘ de las 313 idénticas a las de la fase 5 salvo la 385, ✘ → ✔ (M, `diff`); 163, 240, 242, 249, 251, 260, 285 y **290** siguen ✔. Cinco sabotajes por diff, uno por promesa, todos de una línea sobre archivos LF (M: 0 CRLF, líneas iguales antes y después), cada uno con su contrato entero y restaurado con `cmp` idéntico (`fase6\sabotea.py`, `sabotajes.log`): quitar `NoActivable` de `ExtendidosDelOverlay` da **11** con «el overlay es transparente, en capas, de herramienta y no activable: 0x000800A0» en la 379; `Encolar` → `Ahora` en `Publicar` da **12** con «publicar encoló (0)…» y «…el pintor recibe UNO, el último (0 recibido(s))» en la 382 —el `try` que protege el encolado se traga el lanzamiento del doble, y aun así el sabotaje se ve—; quitar `_flechaVuela = false` de `Suelta` da **11** con «y esconde la flecha» en la 383; `LaCaritaViaja` → `true` da **11** con «en un tramo con Jev la carita ni viaja al clic…» en la 384; y quitar `DejaPasarElRaton` de `ExtendidosDelPanel` da **11** con «la máscara del panel lleva WS_EX_TRANSPARENT y WS_EX_NOACTIVATE: 0x08080080» y la 385 ✔ → ✘. En cada sabotaje las marcas de las demás promesas no cambiaron (M, `diff`), y la corrida restaurada es idéntica a la verde en marcas y en motivos |

### Fase 7 — un solo vigilante

| | |
|---|---|
| **Promesa que pone verde** | 378 |
| **Qué toca** | `Ui/Jev/OrdenEnZ.cs` (nuevo: `Capa`, `OrdenEnZ`, `VigilanteEnOrden`); `Ui/SiempreDelante.cs` (`EntraAlGrupo`; el `Vigilar` por ventana se **borra** y no hay `VigilarEnOrden()` aparte: el reloj nace con la primera que entra); `Ui/Muelle.cs:177` y `Ui/PanelDeAcciones.cs:272` (la llamada, `SiempreDelante.EntraAlGrupo(this, Jev.Capa.Carita/Notch)`, más su comentario; sin `using` nuevo); `Contrato.cs`: **10 comprobaciones más** dentro de la 378, escritas antes que el código que juzgan (Hallazgos, fase 7) |
| **¿Núcleo congelado?** | no |
| **Terminado** | 378 verde en (a) y (b): grep `\.Vigilar\(\)` da 0 fuera de `SiempreDelante.cs` **y** `Muelle.cs` y `PanelDeAcciones.cs` contienen `EntraAlGrupo(this` |
| **Sitios con esta clase de error** | 2 (`Muelle.cs:175`, `PanelDeAcciones.cs:247`); **medido al cerrarla:** `.Vigilar()` en todo `windows-client/src`, **2 → 0** (los constructores de `Muelle` y `PanelDeAcciones`; `LiveAudio.cs` tiene un `Vigilar()` propio, sin punto, que no es este); relojes que suben capas, uno por ventana → **1** para el grupo (`new DispatcherTimer` en `SiempreDelante.cs`: 1); catch mudo en `SiempreDelante.cs`, **1 → 0** —el nuevo, en `VigilanteEnOrden`, dice paso, tipo, mensaje y capa—; el `bool` de `SetWindowPos` tirado en `SiempreDelante.cs`, **1 → 0** (ahora lanza `Win32Exception` con su código y el vigilante lo dice); ventanas `Topmost` del cliente, **12**: 2 en el grupo (muelle y notch) y 10 fuera que no vigilaban antes y siguen igual (Hallazgos, fase 7); `FaceWindow.xaml.cs`, 0 líneas tocadas |
| **Estado al cerrarla** | las **10 comprobaciones nuevas** se vieron **rojas** antes del código final, en dos tandas (M): contra una versión ingenua —una lista a la que se añade, `AlMostrar` = `Tick`, sin catch, y el grupo con su reloj **al lado** del `Vigilar` de siempre— salieron 8 («CONTRATO ROTO: 18», nueve `✘` en la 378: la misma ventana subida dos veces, falta `Sale`, una que no es del grupo reordena, el tick lanza dos veces, el notch y la flecha sin subir, cero líneas de log de las dos, y «crea 2» `DispatcherTimer`), **y esa ingenua pasaba las 7 comprobaciones que ya había**; las 2 que allí no llegaron a mirar nada —«mostrarla después de salir», que sin `Sale` no llegó a correr, y el barrido de `.Vigilar()`, que la ingenua ya cumplía— se vieron rojas con una segunda variante (`Sale` que no saca, `AlMostrar` sin mirar quién, y un `TercerVigilante.cs` de una línea con `.Vigilar()` en un comentario): «CONTRATO ROTO: 13», cuatro `✘` en la 378. Con el código final: la **378 ✔ entera**, (a) y (b). «CONTRATO ROTO: 9» (antes 10), **305 ✔** (antes 304), 0 SIN JUZGAR, y las marcas ✔/✘ de las 313 idénticas a las de la fase 6 salvo la 378, ✘ → ✔ (M, `diff`); 163, 240, 242, 249, 251, 260, 285 y 290 siguen ✔. Lo que queda ✘ es lo de las fases 8 y 9: 375 (b), 376 XAML, 379 (b), 380 (b), 381 (b), 382 (b), 383 (b) y 384 (b). Sabotaje por diff: invertir `OrdenEnZ.Capas` (una línea, `OrdenEnZ.cs:38`, archivo LF, 7.143 bytes antes y después) da **14**, con cinco `✘` y todos en la 378 —la lista, el tick, `AlMostrar`, la flecha arriba y el notch y la flecha del tick con fallo, que salen al revés, [5, 3, 5, 3]— y ninguna otra marca cambiada; restaurado con `cmp` idéntico, y el contrato otra vez en 9 con marcas y motivos idénticos a la corrida verde (M) |

### Fase 8 — las tres ventanas

| | |
|---|---|
| **Promesa que pone verde** | 376 (parte XAML); 375, 379, 380, 381, 382 (partes (b): por fuente) |
| **Qué toca** | `Ui/Jev/OverlayDeJev.cs`, `PanelDeJev.cs` (+ su XAML como recurso), `FlechaDeJev.cs`, `DespachadorDeWpf.cs`, `VistaDeJev.cs` (nuevos) |
| **¿Núcleo congelado?** | no |
| **Terminado** | compila en Release; el XAML real mide 64/198,6 en el arnés; contrato intacto; las ventanas solo llaman al modelo (grep: ni `OrderBy` ni `Contains(` sobre etiquetas dentro de las tres ventanas); exactamente 1 `BeginInvoke` bajo `Ui/Jev/` |
| **Sitios con esta clase de error** | ventanas de Ü que cubren solo la primaria: 2 (`InspectorOverlay.cs:86-89`, `AuraDeAprendizaje.cs:122-124`); `OverlayDeJev` no repite el patrón |
| **Estado al cerrarla** | en tres pasos (8a overlay, 8b panel, 8c flecha, vista y despachador; §Revisiones): **375, 376, 379, 380, 381 y 382 ✔ enteras**, y 377, 378 y 385 siguen ✔. «CONTRATO ROTO: 2» —solo la 383 y la 384, que son de la fase 9—, 311 ✔, 0 SIN JUZGAR (M). Compila en Release; exactamente 1 `BeginInvoke` bajo `Ui/Jev/`, en `DespachadorDeWpf.cs`; ni `OrderBy` ni `Contains(` en las tres ventanas (M, grep). Las ventanas que cubren solo la primaria resultaron 3, no 2 (hallazgo del 8a) |

### Fase 9 — el cableado mínimo, y el nivel 4

| | |
|---|---|
| **Promesa que pone verde** | 383 (b), 384 (b), 385 (b) |
| **Qué toca** | `Ui/FaceWindow.Jev.cs` (nuevo, parcial); `Ui/FaceWindow.xaml.cs` (**≤ 7 líneas**, contadas en el commit; `InvocarPorAtajo` intacto); `Contrato.cs`: **10 comprobaciones más** dentro de la 381 (1), la 382 (1), la 383 (5) y la 384 (3), escritas antes que el código que juzgan, y el ayudante `GanchoLlevaA` (Hallazgos, fase 9) |
| **¿Núcleo congelado?** | no (la UI no está congelada, pero es zona de choque alta: aviso en `#miracle-updates` al abrir) |
| **Terminado** | contrato intacto (15/15, 0 sin juzgar); sabotajes por diff de las 15; nivel 4 **con D sola** (tabla de abajo, ≥ 2 pantallas con nombre y el log pegado); el nivel 4 de las cajas verdes queda expresamente pendiente de C |
| **Sitios con esta clase de error** | ganchos que tocan el archivo grande cuando cabían en un parcial: 0 después de esta fase (todo lo de Jev vive en el parcial salvo las ≤ 7 líneas); **medido al cerrarla** (grep, M): líneas de Jev en `FaceWindow.xaml.cs`, **4 añadidas y 0 editadas**, y las 4 no caben en el parcial —nacer necesita el `mcp` local del arranque, sincronizar va dentro de `PintarBotonJev` y las dos de la regla dentro de sus métodos—; sitios donde la carita viaja al clic o sigue al cursor sintético, **2 de 2** con la regla (`IrJuntoA` con `alClic`, que es la única llamada a `ViajarAlClic`, y `OnAutomationCursorMoved`, el único suscriptor de `UiaSurface.CursorMoved`); asignaciones de los tres avisos del tramo, 3 en `FaceWindow.xaml.cs` (`:463-465`) y 3 en el parcial que las encadenan, ninguna después; suscripciones: `UiaSurface.Pulso` 2 (la carita y la vista), `Senalador.Suelta` 3 (dos de la carita y la vista), `Freno.SePulso` 2 (la carita y la vista); `.Decisor =` 4 (encender y apagar en `InterruptorDelDecisor`, envolver y desenvolver en el parcial); catches nuevos, **2** (cerrar Ü y sincronizar), los dos con la cadena entera y ninguno mudo |
| **Estado al cerrarla** | ver Hallazgos, fase 9. **El nivel 4 no se hizo** —en esta fase no se ejecuta `U.exe`—: queda escrito en §Nivel 4 pendiente, con las dos pantallas y las líneas de log que existen de verdad |

## Lo que NO entra

- **El campo de texto del panel y la receta del foco** (activar, comprobar `IsActive` del panel, devolver el primer
  plano, `WS_EX_TRANSPARENT` al pulsar Enter). En Ü el objetivo llega por la voz (`map_tramo`); escribirlo en el panel
  obligaría a activar una ventana nuestra y a arrancar un tramo desde la UI, que pasa por `Mcp/`. Es otra spec, **y
  con ella iría el atajo**: aquí el panel nace al encender Jev, muestra, y nunca toma el ratón ni el foco (385); el
  atajo de invocar a Ü sigue siendo la vía de teclado para escribirle (§Revisiones, 3). Hallazgo de paso, para quien
  la escriba: `DevolverElFoco` (`FaceWindow.xaml.cs:1618`) tira el `bool` de `SetForegroundWindow` dentro de un
  `catch { }` mudo (patrón nº3).
- **El burbuja verde en reposo** (anillo que respira alrededor del ratón real, cono, rayo, etiqueta del objetivo).
  Es decoración fuera de la corrida, y en corrida el plano ya decide apagarlo. Si se quiere, es una fase sobre
  `OverlayDeJev` con su promesa (radio `clamp(min(dSel+20, dSegundo−8), 28, 260)`, medible).
- **Las causas de «no veo nada accionable»** (elevada, SAP sin scripting, 0 en UIA, sin nombre, sin respuesta): son
  de C (la observación) y del núcleo. La vista enseña la causa que le llega, tal cual (374).
- **«Validado» mecánico** («✓ se abrió un menú»): sale de la huella de B (353, «cambió dentro»). Cuando `Paso` lo
  traiga, el ticker lo enseña por la misma línea de progreso, sin cambiar el modelo.
- **El juez del clic sobre una ventana de Ü** (350, A) y **la política de lo que viaja** (346/347, A). D no manda
  nada a la red: lo que pinta ya está en la pantalla de la persona.
- **El modo sombra** (335 del dueño): el panel lo enseñaría con «· sombra» en `Ausente`; es otra rama.
- **La fuente del medidor `ausente`** (revisión del 2026-09-23, hallazgo 7). El panel pinta el medidor y lo pinta
  honesto —sin dato, «—» y relleno 0 (373)—, pero **hoy nada lo alimenta**: `DecisionDeUnPaso` no trae ese campo,
  `PeticionASystemOne` solo pregunta puerta, cumplido y peligro, `ObservadorDelDecisor.CicloDe` pone `Ausente = null`
  siempre (382 lo exige) y nada en `Decision/` lo nombra (M: `Ausente` o `absent`, 0 apariciones; sin distinguir
mayúsculas, 2, y las dos son comentarios de «vacío no es ausente» y «clave ausente»), ni en las ramas hermanas
(M, `Ausente` o `absent` en `Decision/`, `Navigation/` y `Mcp/` de las ramas locales `jero/jev-la-distribucion-se-valida-entera`,
`jero/jev-la-espera-mira-lo-que-se-ve`, `jero/jev-una-lectura-por-ciclo` y `jero/jev-todo-junto`: 0 en las cuatro). Así que
  con Jev real el medidor enseña **siempre** «—», y el estado del plano «`absent` alto → No lo veo en esta pantalla
  (ausente {a}). Me detengo.» es **inalcanzable** en esta rama. Se decide dejar el medidor y no quitarlo del XAML: la
  376 mide el panel con los dos medidores, que es el plano, y quitarlo sería rehacer sus medidas para volver a
  ponerlas cuando llegue el dato. **Dueño: A** (el decisor, spec 046, PR #118) o una spec nueva, con tres piezas y
  su promesa: la pregunta en `PeticionASystemOne`, el campo `DecisionDeUnPaso.Ausente` y su traducción en
  `ObservadorDelDecisor.CicloDe` —la 382 tendría que cambiar su «`Ausente` es `null` siempre», y eso se habla con
  el dueño—. Hasta entonces, en el nivel 4 el medidor `ausente` con «—» **no es un fallo** y no se da por visto con
  dato (fila 3).
- **Las 7 conversiones DPI a mano** de hoy: no son de esta rama; quedan contadas como deuda.
- **Medir la CPU de la ventana en capas animada**: se mide en el nivel 4 y, si sale mal, la alternativa (repintar el
  burbuja a 30 fps en vez de la capa de pulso) es una fase sobre `OverlayDeJev`, no un cambio de promesa.
- **Que las cajas queden encima del panel** como en el vídeo: decidido al revés y por qué (§Diseño).
- **Un interruptor del overlay en el botón** («Jev · on+cajas»): el encendido va por `U_JEV_OVERLAY` leído al
  encender Jev (379). Si el dueño quiere pulsarlo, son 2 líneas más en `OnToggleJev` y otra promesa.

## Nivel 4 pendiente

Se hace en el PC real, con Jev **apagado o en `U_DECISOR=simulado`** fuera de SAP hasta que A entre (mientras no
entre, `U_DECISOR=jev` sobre `sapgui://` manda filas con nombre y documento). Con simulado no hay barras (373): las
barras se ven solo con un transporte falso que devuelva una distribución, o con Jev sobre Explorador/Chrome.

**D depende de C y de A para dos de sus entregables, y se dice:** las cajas verdes de las candidatas necesitan la
365 y la 368 de C; el «$» con número necesita la 350 de A. Las tres están PENDIENTES en ramas que hoy solo tienen su
spec (M). Por eso la tabla se parte en lo que **se ve con D sola** —y es lo que su PR trae fotografiado— y lo que
**se repite cuando C esté en `main`**, antes de que D abra su PR (la arquitectura ya ordena D al final): D rebasa,
cambia el puente por `AlDecidir`, y **vuelve a correr las filas marcadas «con C»** con el log pegado. Si 365/368
cambian de forma al implementarse, lo que se adapta es el parcial, no el modelo, y se cuenta en el commit.

| Qué | Cómo | Dónde | ¿Con D sola? |
|---|---|---|---|
| El panel con barras, medidores y coste «—» | un tramo con transporte falso (`U_DECISOR=jev` + `TYPESAFE_API_KEY` de mentira y un `Transporte` inyectado que contesta una distribución fija: **no se llama a TypeSafe**) sobre Explorador «Descargas» y Chrome; foto del panel a 3,04 px/DIP junto a `recortes/revision/full_16.png` | 2 pantallas con nombre | **sí** |
| La rosa y la flecha sobre lo pulsado | mismo tramo: al pulsar, `UiaSurface.Pulso` enciende la rosa y la flecha vuela; con la segunda mejor forzada (la primera «no está»), el ticker dice «la 1.ª no estaba» y la rosa está sobre la que la mano pulsó | log (`vista: pulsada (n) …`) + foto | **sí** |
| Las cajas verdes de las candidatas y «N sin caja» | el mismo tramo, con las candidatas con caja de C; hasta entonces el overlay dice «· N detectados (N sin caja)» y no pinta ninguna verde | foto + log | **no: con C** |
| El «$» con número | igual, con `input_tokens` en la decisión (350) | foto | **no: con A** |
| El orden en Z | línea `orden Z: flecha 0 · panel 1 · notch 2 · carita 3 · overlays 4…` por `EnumWindows` a los 10 s, 60 s y tras esconder y enseñar el overlay | log | sí |
| El overlay fuera de la captura | una foto con `Screenshotter` con el overlay encendido (`U_JEV_OVERLAY=si`) y la rosa puesta: sin rosa en el PNG | log + PNG | sí |
| El overlay caduca al cambiar de app | con la rosa puesta, Alt+Tab a otra ventana: la rosa desaparece en el acto (línea `overlay: caducó (cambió la ventana de delante)`) | log | sí |
| El overlay apagado por defecto | sin `U_JEV_OVERLAY`, encender Jev: línea `overlay: apagado (sin U_JEV_OVERLAY)` y ninguna ventana de overlay en `EnumWindows` | log | sí |
| La carita quieta en tramo | 0 líneas `viaje al clic` (`FaceWindow.xaml.cs:1801`) durante el tramo; ≥ 1 en un `map_take` fuera de tramo | log | sí |
| El atajo sigue abriendo el globo | con Jev encendido, Ctrl+Alt+U: el globo con el foco en el campo, como hoy; ninguna ventana de Jev toma el foco (`GetForegroundWindow`) | log (`atajo:`) | sí |
| Escape y apagar | Escape a mitad de tramo: overlay vacío y flecha escondida en el acto; botón «Jev · off»: las tres ventanas cerradas | log + a ojo | sí |
| CPU de la ventana en capas | 60 s con el pulso animado y el ratón quieto frente a 60 s sin overlay, contador de CPU del proceso | tabla en el PR | sí |
| **Dos escalas de DPI** | **no se puede aquí: un solo monitor.** Se dice en el PR y queda para una máquina con dos | — | no: máquina |
| El vuelo en la costura entre monitores | ídem | — | no: máquina |

### Lo que tiene que mirar el dueño, y en qué dos pantallas (escrito en la fase 9, sin correr: aquí no se ejecuta `U.exe`)

La fase 9 deja todo cableado y el contrato intacto, pero **ninguna de estas filas se ha visto**: el contrato no toca la
pantalla. Lo que sigue es la corrida a mano, con las líneas de log que **existen de verdad** en el código de la rama
(M, grep del 2026-09-23; el formato es `[hh:mm:ss] [id] etiqueta: mensaje`). Donde la tabla de arriba nombraba una
línea que no existe, se dice aquí y se da la forma de medirlo.

**Las dos pantallas: el Explorador de archivos en «Descargas» y Chrome** (cualquier página con enlaces y botones).
Las dos son UIA —dentro de SAP, UIA no ve nada y hasta que A entre Jev manda allí filas con nombre—, y son distintas a
propósito: una ventana clásica de Win32 y un navegador (aprendizaje nº9).

Preparación, una vez:

1. Compilar la rama en Release y arrancar **ese** `U.exe` (`windows-client\bin\x64\Release\net8.0-windows10.0.19041.0\`),
   no el instalado. Sin `U_JEV_OVERLAY` la primera vez.
2. Con qué decide Jev, y es decisión del dueño porque cuesta: **(a)** el botón «Jev · on» con la clave de TypeSafe
   —Jev de verdad, el único camino con barras—, o **(b)** `U_DECISOR=simulado` al arrancar —sin barras (373), gratis—.
   El «transporte falso» de la primera fila **no existe como opción del binario**: `ClienteTypeSafe.TransporteSegun`
   solo construye el cliente real (L); para verlo con barras sin TypeSafe haría falta una fase que lo añada con su
   promesa.

Qué mirar, en orden, en cada una de las dos pantallas:

| # | Qué se hace | Qué tiene que decir el log (o cómo se mide) | Si no |
|---|---|---|---|
| 1 | Arrancar sin `U_JEV_OVERLAY` | `jev-vista: vista de Jev creada · overlay: apagado (sin U_JEV_OVERLAY)` | la vista no nació: falta la línea `NacerLaVistaDeJev` o lanzó antes |
| 2 | Encender Jev (botón o `U_DECISOR`) | `jev-vista: Jev encendido · overlay: apagado (…) · panel en x,y,w,h · 0 overlay(s) · flecha escondida…`, y el panel se ve junto a la carita | `jev-vista: ✘ abrir las ventanas de Jev lanzó …` dice el paso; `el panel nace sin sitio calculado` si Windows no dio el monitor |
| 3 | Un tramo por voz sobre la pantalla («abre la carpeta X», «pulsa el enlace Y») | el panel enseña el objetivo y «Mirando la pantalla»; con Jev real, cabecera, medidores y cinco barras; coste «—»; el medidor `ausente` enseña **siempre** «—» (no tiene fuente, §Lo que NO entra) y **no cuenta como visto con dato**; al llegar las cinco barras el panel **se recoloca** con su alto (385) y no tapa la carita ni se sale del área de trabajo | sin decisiones en el panel: `Jev encendido con el Decisor del mapa en null`; si el panel crece y tapa la carita, el `CONTRASTE` del panel dice con qué alto se calculó el sitio |
| 4 | La mano pulsa | `jev-flecha: vuela de … a … (lo pulsado en …) en N s` y después `posada en … tras N fotograma(s)`; si no vuela, `jev-vista: la flecha no vuela a …: <lo que contestó Windows>` | la línea dice cuál de los dos handles no casó |
| 5 | Durante el tramo con Jev | **0** líneas `ui-anim: viaje al clic:` entre `tramo:` y el fin; fuera de tramo (un `map_take` o Jev apagado) y con la carita plegada, **≥ 1** —plegada, porque `IrJuntoA` no viaja con el panel abierto— y **0** líneas `jev-flecha: vuela` (revisión del 2026-09-23: fuera de tramo vuela solo la carita) | si hay viajes en tramo, la regla no recibe `EnTramo`; si vuela la flecha fuera de tramo, la vista no mira `Maquina.EnTramo` (384) |
| 6 | Escape a mitad de tramo | el overlay vacío y la flecha escondida **en el acto**; el panel sigue, sin corrida, y **se queda así**: después solo enseña el motivo («paraste tú con Escape; no sigo.»), sin objetivo ni barras (383, revisión del 2026-09-23) | a ojo: no hay línea propia de `Suelta`; si vuelven las barras tras Escape, la vista no pinta por `Maquina.QueSePinta` |
| 7 | «Jev · off» | `jev-vista: Jev apagado: cerrados el panel, la flecha y N overlay(s)` y las tres ventanas desaparecen | — |
| 8 | Repetir 1–7 con `U_JEV_OVERLAY=si` | la 1 dice `overlay: encendido (U_JEV_OVERLAY=si)`; la 2, `jev-overlay: N overlay(s) para N monitor(es): […]`; al pulsar, la rosa sobre lo pulsado | — |
| 9 | Con la rosa puesta, Alt+Tab a otra app | `jev-overlay: cajas caducadas en el overlay de …: cambió la ventana de delante (hwnd 0x…); había N pintada(s)` | la tabla de arriba decía `overlay: caducó (…)`: **esa línea no existe**; es esta |
| 10 | Con la rosa puesta, una captura (`Screenshotter`, o Win+Shift+S) | la rosa **no** sale en el PNG, **ni el panel** (385, revisión del 2026-09-23: el panel también se excluye de la captura) | `jev-overlay: no se pudo excluir de la captura …`; para el panel, `jev-panel: no se pudo excluir de la captura el panel …` |
| 11 | Orden en Z, a los 10 s, a los 60 s y tras esconder y enseñar el panel | **no hay línea `orden Z:` en U** (la tabla de arriba la nombraba): se mide desde fuera, con `EnumWindows` sobre las ventanas del proceso, y tiene que salir flecha, panel, notch, muelle y overlays, de arriba abajo | `SiempreDelante`: línea con el paso, la capa y el handle que no subió |
| 12 | Con Jev encendido, el atajo de invocar a Ü | el chat del notch con el foco en su campo, como hoy; **no hay línea `atajo:` de éxito** (solo la de fallo, `Activate() no trajo la ventana al frente`): se mide con `GetForegroundWindow` desde fuera y no puede ser ninguna ventana de Jev | — |
| 13 | Cerrar Ü con Jev encendido | el proceso termina; no queda ningún `U.exe` de la carpeta de la rama | `jev-vista: ✘ al cerrarse Ü, cerrar las ventanas de Jev lanzó …` |

**Lo que NO se va a ver con D sola, y no es un fallo de la corrida:**

- ~~**«la 1.ª no estaba: pulsé la 2.ª …»**~~ **Sí se verá desde la revisión del 2026-09-23**: el puente lee el
  número de la línea `paso k: «x» (n) conf … · cambió` con `CicloDeJev.ConLaLinea` (372), y la resaltada pasa a la
  pulsada. Lo que hace falta para verlo es que la mano pulse la segunda mejor, y eso no se provoca a voluntad: si no
  pasa en la corrida, se dice «no ocurrió», no «funciona».
- **El medidor `ausente` con dato**: sin fuente (§Lo que NO entra); enseña «—».
- **El primer objetivo sale con «tramo: » delante**: `ElTramo` avisa con `tramo: {objetivo}` (`ElTramo.cs:132`, L) y el
  decisor recibe el objetivo pelado (`:146`, L), así que el panel dice «tramo: abre X» hasta la primera decisión y
  «abre X» después. Quitar el prefijo es una línea con su comprobación, no del cableado.
- Las cajas verdes (con C) y el «$» con número (con A), como dice la tabla.
- Dos escalas de DPI y la costura entre monitores: esta máquina tiene uno.

Lo que se pega en el PR: las líneas de arriba con su hora, por pantalla, y las dos fotos del panel (Explorador y
Chrome).

## Revisiones

**2026-09-22.** Nueve refutaciones a la versión `98019ed`, comprobadas una a una contra el código de `bb3d0dc` y los
guiones de la revisión antes de aplicarlas. **Ninguna resultó falsa**; dos traen números que se recalcularon y se
anotan. Lo que cambió en cada promesa está aplicado arriba; aquí queda por qué.

| # | Refutación (resumen) | Veredicto | Comprobación (M) | Qué cambió |
|---|---|---|---|---|
| 1 | 371: la regla de 30° sobre los 16 ARGB es falsa hoy y después; grises a 150° a 8° de `Cumplido`; azules a 4–8°; dos tokens sin tono; el sabotaje no se puede verificar | **comprobada** (bloqueaba) | HSL sobre la tabla: 24 pares con tono definido a < 30°, **3** tokens sin tono (la refutación decía 27 pares indefinidos: son 42 pares con al menos un acromático; el fondo es el mismo); con saturación ≥ 0,25 en los dos quedan **3** pares, los azules. `Elegida → 0xE6FFA020` cae a **4,5°** de `Ausente` (la refutación decía 12°; la conclusión no cambia). El sabotaje viejo (`Candidata → 0x943FBF6F`, S 0,50, 15,6° de `Cumplido`) también rompe bajo la regla nueva | la regla se aplica a pares de tokens **cromáticos** (`EsCromatico` = S ≥ 0,25); neutros y acromáticos solo por valor; `ChoquesAceptados` son los tres azules, enumerados; sabotaje nuevo; los tres choques con otras paletas quedan documentados aparte con sus grados |
| 2 | 376: las partes no suman 64 sin resultados (57,6 / 60); el 64 sale de un `MinHeight` que la promesa no nombra | **comprobada** | `medir-panel.ps1:26` lleva `MinHeight="64"`; `medir-panel.txt:2,6` miden 64,0 con relleno 13/10/13/10, no con 7,6; suma con 7,6 = 57,6 | `AltoMinimo = 64` declarado (calca los 63,7 del vídeo), `AltoDe = max(mínimo, SumaDePartes)`, el `MinHeight` citado como parte, sabotaje del caso 0 barras |
| 3 | 385: el atajo existe para escribirle a Ü; con Jev encendido pasaría a abrir un panel de solo lectura y se pierde la vía de teclado | **comprobada** | `FaceWindow.xaml.cs:1587-1600`: `_prevForeground`, `Desplegar("atajo: escribirle a Ü")`, `ShowTalk(…, focusInput: true)` | `InvocarPorAtajo` no se toca; el panel nace con `Sincronizar(on)`; `QueAbreElAtajo` desaparece; la 385 lo juzga por fuente (el cuerpo del atajo no sabe de Jev); el cableado baja de ≤ 9 a ≤ 7 líneas |
| 4 | 379: no hay camino por el que una persona encienda el overlay; el contrato queda verde con un método puro que nadie llama | **comprobada** | grep en la 049 de `98019ed`: `EncenderOverlay\|OverlayEncendido\|U_JEV` solo en la fila de juicio de la 379 | `ConfiguracionDeLaVista.Leer(entorno)` pura con `U_JEV_OVERLAY=si`, `Porque` que nombra la variable, `Estado` de la máquina que lo dice; `VistaDeJev.cs` la llama (por fuente); fila de nivel 4 «apagado por defecto» |
| 5 | 375/378/379/380/381/383/384/385 prometen conducta de ventanas y el contrato solo mira constantes o ausencias de texto; sabotear el gancho deja INTACTO; la 378 desprotege muelle y notch | **comprobada** | cada sabotaje listado se repasó contra la fila de juicio de `98019ed`: ninguno la ponía roja; `UiInspector.Toggle` (`:59-63`) no consulta nada | cada enunciado dice qué juzga el contrato y qué se lee por fuente (clase (b) con `U_REPO`: `SetWinEventHook`, `ExtendidosDelOverlay`, `SetWindowDisplayAffinity`, `OnDpiChanged` + `RectTrasCambio(`, `GetForegroundWindow` y no `appDelante: true`, `Senalador.Suelta +=`/`Freno.SePulso +=`, 2 `LaCaritaViaja(`, `InvocarPorAtajo` sin Jev, `ExclusionConElInspector.` en el inspector); la 378 exige `EntraAlGrupo(this` en muelle y notch, no solo la ausencia de `.Vigilar()`; lo que ni así se alcanza está en el nivel 4 con nombre |
| 6 | 382: con el despachador inyectado, el `BeginInvoke` real queda fuera del juicio; «< 50 ms» y «recibe 1, el último» dependen del reloj del runner | **comprobada** | el único `BeginInvoke` de la costura está en `FaceWindow.xaml.cs:464`; `contrato.yml:25` corre en `windows-latest` sin garantías de latencia | `IDespachador` con `Encolar`/`Ahora`; el contrato usa un doble que lanza en `Ahora`, otro que acumula y `Vaciar()` cuenta 1, otro que nunca ejecuta y `Publicar` retorna: **sin reloj**; `DespachadorDeWpf` es la única línea con `BeginInvoke` bajo `Ui/Jev/`, contada por fuente |
| 7 | 376 (XAML): «NO PUDE JUZGARLA» es hoy un `return` sin contar: la promesa sale ✔ y CI solo cuenta `PENDIENTE:` | **comprobada, con un matiz** | son **3** sitios y no 2, con **dos conductas**: la 340 (`:911`) y la 341 (`:925`) dicen inocente (✔); la 164 (`:7912`) hace `_fallos++` y dice culpable (✘). El script ya sale con 99 si falta `CONTRATO (INTACTO\|ROTO)` (`contrato-del-grafo.ps1:119`) y `verificar.ps1:135` y `contrato.yml:81` rotulan 99 como «no se pudo juzgar» | fase 0: `NoPudeJuzgar` + `_sinJuzgar` + tercer veredicto `CONTRATO SIN VEREDICTO COMPLETO` con código 99 cuando no hay rojas pero sí sin juzgar; los 3 sitios pasan por él; `SIN JUZGAR:` en los resúmenes de CI y de `verificar.ps1` |
| 8 | 372/375: la costura solo ve `d.Puerta`; la mano pulsa la segunda mejor sin volver al decisor; el panel resaltaría la que no se pulsó; C no lo arregla; el interruptor reasigna `Decisor` y `Sincronizar` repetido envolvería dos veces | **comprobada** | `SurfaceMapTools.cs:304-309` (segunda ≥ 0,25, sin otra llamada), `:318` (número del id); `InterruptorDelDecisor.cs:66, 81`; `UiaSurface.cs:139` (`Pulso` con la caja real); `ElTramo.cs:204` (la línea trae `(n)`); 048:158 (368 lleva «la decisión tal como la devolvió el decisor») | el ciclo lleva `Pulsada` (número, de la línea de progreso) y `CajaPulsada` (de la mano); la rosa, el `SemiBold` y la flecha siguen a la pulsada; la elegida solo se marca mientras no hay pulsada; ticker «la 1.ª no estaba: pulsé la 2.ª»; `Envolver` idempotente juzgado; `Sincronizar` cuelga de `PintarBotonJev`, que corre después de `Encender`/`Apagar`; a C se le pide la pulsada en la 368 |
| 9 | Nivel 4: con D sola no se ve ni una caja ni un dólar; la tabla prometía fotos «con datos reales» sobre un overlay vacío | **comprobada** | `git diff --name-only origin/main...HEAD` en los tres worktrees hermanos: solo `docs/specs` | la dependencia se declara; la tabla del nivel 4 dice qué se ve con D sola (panel con barras por transporte falso, **rosa y flecha sobre lo pulsado** gracias a `UiaSurface.Pulso`, orden en Z, captura, caducidad, carita, atajo, Escape) y qué se repite «con C» / «con A» antes de abrir el PR |

**2026-09-24, al juntar A, B, C y D en la rama de pruebas `jero/jev-todo-junto`** (merge `3c56ec9` sobre `ec72d96`).
El merge dejó el contrato INTACTO (339 ✔) y cuatro choques de intención que git no marca y ningún juez veía. Cambian dos
enunciados (374 ampliada, 382 reescrita) y los jueces de 372, 374 y 382; **ningún número nuevo, ninguno reciclado**.
Es un cambio de lo que se promete y **lo tiene que mirar el dueño de la 049 antes de que D llegue a `main`**.

| # | Choque | Comprobación (M) | Qué cambió |
|---|---|---|---|
| 1 | El puente envuelve el `Decisor` y ve la decisión **antes** del veto de A (390, `SurfaceMapTools.DecidirYPulsar`): con «Guardar» a 0,99 publicaría `Actuar=true` y el panel diría «Pulsando «Guardar» (4)» con la barra resaltada «por elegida». La 046 (§El veto determinista) le deja escrito a D que pinte «vetada» por `Veto`, y nada de D leía `Veto` | `grep -i "veto\|vetad" windows-client/src/Ui/Jev/` → **0** antes de este cambio; el `Decisor` del mapa tiene **1** llamador (`SurfaceMapTools.cs:816`), dentro de `UnPasoDecidido`, así que el evento de C cubre todas las decisiones que el envoltorio veía | la vista oye `AlDecidir` (`ObservadorDelDecisor.Oir`, idempotente por mapa) y deja de envolver; `DecisionDeJev.Veto`; el ticker dice «Vetada: {veto}» (punto 0,42) antes que cualquier compuerta de Jev; la 374 y la 382 lo juzgan |
| 2 | La regex de la línea de progreso terminaba en `· (no )?cambió$` y B (353) escribe «cambió de sitio», «cambió dentro» y «cambió delante» (`ElTramo.QueCambioEnPalabras`): toda pulsación que cambió algo dejaba la pulsada en `null` y la resaltada volvía a ser la elegida. El juez de la 372 no lo veía: sus líneas eran las de antes de B | rojo medido con el código del merge: la 372 cae por «pulsada «»» con las tres palabras nuevas, y el ticker enseña la línea en vez de «la 1.ª no estaba» | la pulsada llega con el paso del evento (`Paso.Numero`, solo si `Actuo` y `Termino`), que es lo que esta spec le pidió a C; `ConLaLinea` pinta la línea sobre el ciclo decidido sin tocar la pulsada; `PulsadaDeLaLinea` y su regex se borran (aprendizaje nº16: una sola fuente) |
| 3 | El evento sale cuando el paso **terminó**, espera incluida (368: `UnPasoDecidido` publica sobre el paso devuelto): si la mano cambió de sitio, las cajas leídas de sus candidatas son de la pantalla que se fue | lectura de `SurfaceMapTools.UnPasoDecidido` y `DecidirYPulsar` (L); la huella dice qué cambió en `Paso.QueCambio` | la caja de una candidata solo viaja al ciclo si `Paso.QueCambio == Nada`; si no, va sin caja —se ofrece, se cuenta y no se pinta, 375—. La 382 lo juzga con una mano que cambia de sitio y otra que no |
| 4 | Con el puente, el ciclo decidido salía al decidir y la línea del paso cientos de ms después; con el evento salen **pegados** (el tramo escribe «paso k: …» en cuanto `UnPasoDecidido` vuelve), y el conector pinta solo el último ciclo encolado (382). La vista solo repintaba el overlay con un ciclo `Decidido`: las cajas de un paso casi nunca se habrían pintado | lectura de `ConectorDeLaVista.Publicar`/`Vaciar` y `VistaDeJev.Pintar` (L), y el orden evento → línea en `ElTramo.Bucle` (`_manos.Paso` y después `Cuenta`) | `CajasDelOverlay.DelCiclo(ciclo)`, pura: el decidido y la línea que lo lleva pintan las mismas cajas; un ciclo sin paso («Mirando», una línea sin decisión ni candidatas) no toca el overlay. `VistaDeJev.Pintar` la usa. **Lo mismo le pasará al coste** cuando haya tokens: hoy suma al pintar un `Decidido` (373), y ese ciclo puede no pintarse nunca; es lo que la fase 6 ya dejó escrito («hay que acumular en `Publicar`») y se hace con el «$» |

**Medido (M), en la rama de pruebas:** rojo antes del código (`0d9fd6e`): `CONTRATO ROTO: 17 promesa(s) incumplida(s).
El cambio no puede entrar así.` — 336 ✔, rojas exactamente 372, 374 y 382. Verde con el código: `CONTRATO INTACTO: el
grafo se comporta como el día que se congeló.` — 339 ✔. Cinco sabotajes de una línea, en cadena (cada corrida enseña
la saboteada roja y la anterior otra vez verde), verificados por diff contra copia y con el `.cs` anterior al `U.dll`
juzgado: sin la línea del veto en el ticker → rojas 374 y 382 («No estoy seguro (0.99)»); cajas siempre, aunque el
paso cambie → roja solo la 382; oír dos veces vuelve a suscribir → roja solo la 382 (dos ciclos por paso); la línea
borra la pulsada —la forma del choque con B— → rojas 372 y 382; la línea no pinta cajas → roja solo la 382.

**Lo que cuesta, y se dice:** el panel y las cajas se pintan al terminar el paso, no al decidir; la flecha sigue
volando al pulsar por `UiaSurface.Pulso`. Una rosa «sola» de la mano (candidata sin caja leída) se va en cuanto llega
el evento de ese paso, porque el ciclo decidido repinta el overlay desde sus candidatas. **Sin medir en el PC real**
(aquí no se ejecuta `U.exe`): las filas «con C» del nivel 4 siguen pendientes. **Y queda fuera, a propósito:** el «$»
con número. A ya trae `InputTokens` en la decisión (387), pero ponerlo en `TokensFacturados` es otra promesa (373) y
no entra en un merge.

## Hallazgos

- **2026-09-22.** `origin/main` (`f811796`, `5574148`) añadió al contrato del grafo las promesas **341, 342 y 343**
  (voz y log de Pipe). La arquitectura reservaba 341–350 para la rama A: **A tiene que desplazar sus números** (su
  spec 046 en el worktree ya usa 346–350 para lo que la arquitectura llamaba 345–349, y ahora chocan también 342 y
  343). 371–385 siguen libres en `Contrato.cs` y en las **46** refs `origin/*` (`Prueba("37[1-9]|38[0-9]\.` y
  `docs/specs/049-*`: 0 resultados), y no se reciclan.
- **2026-09-22.** Las ramas `origin/jose/el-notch-no-respira` y `origin/jose/notch-y-barra` muestran contra `main`
  exactamente los archivos que `main` ya tiene (`MedidaDelNotch.cs`, `SiempreDelante.cs`…): entraron por squash y no
  hay trabajo abierto allí que choque con `Muelle.cs:175` ni `PanelDeAcciones.cs:247`.
- **2026-09-22.** Conversiones DPI a mano fuera de `Pantallas`: **7**, no 6 (la arquitectura no contaba
  `FaceWindow.Escritorio.cs:111`).
- **2026-09-22.** `Freno.Tarea` no se limpia en `Termine()` (`Actions/Freno.cs:93`): no sirve como «hay tramo en
  marcha». La vista lleva su propio `EnTramo` alimentado por `AlEmpezarTramo`/`AlTerminarTramo`.
- **2026-09-22.** Nadie lee `usage.input_tokens` en `main`: hasta A/C el `$` del panel dirá «—» sobre datos reales.
  No es un fallo de la vista: es la regla de la 373 funcionando.
- **2026-09-22.** **Para C:** la 368 lleva «la decisión tal como la devolvió el decisor» (048:158), y eso no dice qué
  se pulsó (288: puede ser la segunda mejor). D lo saca hoy de `UiaSurface.Pulso` y de la línea de progreso; si la 368
  llevara `Pulsada` (número del id) y su caja, el puente por `Progreso` sobraría. Se le pide al abrir el PR de C.
- **2026-09-22.** El arnés tiene **dos conductas** para «no pude juzgar» (`:911`/`:925` inocente, `:7912` culpable), y
  ninguna es «no sé». La fase 0 de esta rama unifica las tres; hasta entonces, la 340 y la 341 pueden estar ✔ sin
  haber mirado nada si falta `U_REPO`.
- **2026-09-22 (fase 1).** `TextoTerciario` (`0xFF6B736F`) sobre `FondoDelPanel` da **3,86:1** con la fórmula de
  WCAG (M): por debajo del 4,5:1 de AA para texto normal, y el plano lo usa en metadatos de 9–10 DIP. Primario 16,1:1,
  secundario 9,0:1, placeholder 6,2:1 (M). La 371 fija el ARGB del plano, así que cambiarlo es cambiar la promesa:
  decisión del dueño, dicha en `PaletaDeJev.cs` para que no se descubra en el PC real.
- **2026-09-22 (fase 1).** El veredicto «CONTRATO ROTO: N promesa(s) incumplida(s)» cuenta **comprobaciones**
  fallidas, no promesas: el sabotaje de la 371 rompe dos `Debe` y el número pasó de 14 a **16** con una sola promesa
  más en rojo (M, `Prueba` suma `_fallos` por cada `Debe`). El recuento por promesa (✔/✘) sí es exacto. No es de esta
  rama; queda anotado porque un número que dice «promesas» y cuenta otra cosa es el aprendizaje nº2.
- **2026-09-22 (fase 1).** Con el overlay de Jev encendido, `UiInspector.Start` no arranca y lo dice en el log
  (`inspector · no se enciende: el overlay de Jev está encendido…`), pero la línea de estado de `OnToggleInspector`
  (`FaceWindow.xaml.cs:4562`) dirá «Inspector apagado» sin el porqué. Hoy es inalcanzable (el overlay nace en la fase
  8); la fase 9 decide si gasta una de sus ≤ 7 líneas en decirlo o si basta el log.
- **2026-09-22 (fase 2).** **El contrato de la 373 y la 374 tenía cuatro ramas sin juez**, y se le añadieron cuatro
  comprobaciones antes que el código (el enunciado no cambia: son su forma ejecutable). (1) Una línea de progreso lleva
  los tokens de su decisión y **no vuelve a facturar**: sin esto cada «paso k: … · cambió» duplicaba el gasto. (2) Una
  decisión que no actúa **sin distribución** enseña su `Porque` tal cual: «No estoy seguro (0.00)» cuando TypeSafe ni
  contestó es el aprendizaje nº2. (3) Una que no actúa **con distribución pero cuya primera no se ofreció**, también: se
  vio roja antes de su código («dijo No estoy seguro (0.70)», M). Y por eso las compuertas del ticker siguen el orden de
  `ElDecisor.ConJev` —ofrecida, cumplido, peligro, umbral—, no otro. (4) Actuar dice `«Pulsando «Buscar» (6)»`: el plano
  daba `«Pulsando «{etiqueta}»»`, y se le añade el número porque dos «Buscar» existen y la línea de progreso y la de la
  segunda mejor ya lo llevan. Las cadenas del plano «esperan al dueño»; esta también.
- **2026-09-22 (fase 2). Para la fase 6 (el observador):** `DecisionDeJev.Puerta` es la **elegida también cuando no se
  actúa** —el panel la necesita para «"Grabar" no se deshace»—, y `DecisionDeUnPaso.Puerta` viene **vacía** cuando no se
  actúa (`ElDecisor.cs`, `DecisionDeUnPaso.No`). Quien traduzca una en otra la saca de `Alternativas`; si no, el peligro
  dirá `«» no se deshace`. Y `Cumplido`/`Ausente` son `double?` en la vista y `double` (0 = no se preguntó) en el decisor.
- **2026-09-22 (fase 2). Para la fase 8/9 (el puente):** un ciclo de línea se construye como **el decidido con la línea
  puesta** (`ciclo with { Fase = Linea, Linea = …, Pulsada = … }`): solo así conserva su distribución y no vuelve a
  facturar. Y el motivo de parada se enseña **tal cual**, así que cuando el tramo pare por cumplido el ticker dirá el
  texto de `ElTramo.cs:157` —«el objetivo ya está cumplido: …»—, que concluye: **1 sitio**, que A reescribe en la 349.
  La vista no lo escribe en ninguna de sus cadenas, pero lo enseña hasta entonces.
- **2026-09-22 (fase 2). Sin juez, y dicho:** (a) el orden completo del ticker —parada, segunda mejor, línea, fase,
  decisión— solo se juzga por pares (segunda mejor antes que línea; parada sin resaltada); (b) el «(N sin caja)» del
  puente (§El puente provisional, 3) **no está en el modelo del panel**: ninguna promesa lo juzga, la 372 solo mira
  «· 7 detectados» con todas las cajas. El recuento vive en `CajasDelOverlay.SinCaja` (375, fase 3); si el panel lo
  enseña, que sea con su comprobación.
- **2026-09-22.** `UiaSurface.Pulso` (`windows-graph/.../UiaSurface.cs:139`) ya avisa con la caja real de lo que la
  mano pulsa, en físicos, y la carita lo escucha (`FaceWindow.xaml.cs:1318`). Es lo que hace posible una rosa honesta
  sin esperar a C: lo que se resalta es lo que se pulsó, con la geometría de quien lo pulsó.
- **2026-09-22 (fase 3). La tabla de juicio de la 375 dejaba pasar cuatro ramas**, y se le añadieron cuatro bloques
  (ocho `Debe`) antes que el código. Se vieron **rojos los ocho** contra una primera versión ingenua (casar solo ids
  enteros, «el centro de la mano cae dentro», no limpiar la rosa de antes, filtrar solo `Caja == null`) y verdes con la
  final (M, `vista-f3\contrato-ingenua.txt` frente a `contrato-fase3.txt`). (1) **La pulsada llega con dos formas**:
  el ciclo lleva el **número** (`CicloDeJev.Pulsada`, de la línea de progreso) y la fila de juicio pasaba el **id
  entero**; un modelo que solo casara ids enteros habría pasado la 375 y no habría pintado nunca una rosa en la fase 8
  (aprendizaje nº16). (2) **Contención no es alineación** (patrón nº7): la mano calza con una candidata por
  intersección sobre unión ≥ 0,6, el umbral con que TipTour reencuentra su elegido según el plano (§La máquina de
  TipTour); **ese 0,6 no está medido en Ü (D)**, y cuánto calzan de verdad la caja de C y la de la mano del mismo
  elemento es una fila más para el nivel 4 «con C». (3) **Una sola rosa siempre**: la caja sola de la mano vive
  aparte de las de las candidatas, así que una segunda pulsación no deja la primera pintada. (4) **Vacío no es
  ausente** (patrón nº9): `Rect.Empty`, sin ancho y ≤ 2 de lado (el paso 1 de «una caja» del plano) cuentan en
  `SinCaja`; `UiaSurface.Pulso` ya descarta < 1 (`:148`), así que por la mano solo entra el tramo de 1 a 2.
- **2026-09-22 (fase 3). La 375 no puede quedar ✔ antes de la fase 8**: su (b) lee `OverlayDeJev.cs` con `U_REPO`
  y, si falta, `FuenteDelRepo` dice «falta el fuente …, que es parte de lo prometido» (un `Debe`, no un
  `Pendiente`: el resumen de CI, que cuenta `PENDIENTE:`, no la cuenta como pendiente). Por eso el recuento se queda
  en 11 al cerrar esta fase aunque la (a) esté entera: la 375 cambia su `PENDIENTE` por esa línea. Lo mismo les
  pasará a la 380 (fase 4) y la 381 (fase 5). No se toca el arnés: el mensaje es cierto.
- **2026-09-22 (fase 3). Para la fase 8 (la ventana):** el punto de una caja depende de dos datos del modelo —se
  pinta si `EsLeida` y no es `Rosa`— y hoy lo decidiría la ventana; si se quiere juzgado, que sea una propiedad del
  modelo con su comprobación. La cuenta `SinCaja` está en el modelo del overlay y **no** en el del panel: el «(N sin
  caja)» del puente sigue sin juez (hallazgo de la fase 2). Y la caja sola de la mano va **sin etiqueta**
  (`UiaSurface.Pulso` no la trae): la ventana no debe inventarle una.
- **2026-09-23 (fase 4). La tabla de juicio de la 376, la 377 y la 380 dejaba pasar tres ramas**, y los dos intentos
  que se colgaron les añadieron **seis** comprobaciones antes que el código, y contra el código a medias que dejaron
  el contrato dio **seis `✘`** (M: «CONTRATO ROTO: 16»): cinco de las seis nuevas, más la del cuadrado de 44, que el
  caso nuevo del ancla tapada hizo saltar. La sexta, `!lejos.Junto`, pasa sola mientras la propiedad no exista —un
  `is true` sobre nada da falso— y la sostiene su pareja `a.Junto`, que sí se vio roja. Verdes las seis con el código
  final. (1) **Cinco
  barras como mucho**: `AltoDe(6)` no lanzaba (M) y daba la suma, 218,6 (D), un alto para un panel que no existe; ahora lanza, y el
  tope es `EstadoDeLaDecision.BarrasComoMucho`, no otro 5. (2) **«La esquina opuesta» y «sin tapar el cuadrado»
  chocan** cuando el ancla vive en esa misma esquina (ancla en (100, 100), lo pulsado en el resto): el código a medias
  ponía el panel en (12, 12), encima del ancla. El enunciado dice las dos cosas y la del cuadrado vale siempre, así
  que las esquinas del área se ordenan de más lejos a más cerca de lo pulsado y gana la primera que no tape el ancla.
  **No es una regla nueva**: las cuatro esquinas son simétricas respecto del centro y la suma de cuadrados se separa
  por ejes, así que la más lejana es la opuesta (D, por álgebra; los dos casos del contrato lo confirman, M). (3)
  **`JuntoAlAncla`**: «ArribaIzquierda» sola no distingue la esquina junto al ancla de la del área de trabajo, y la
  línea de log de la fase 8 tiene que poder decirlo (aprendizaje nº2). (4) **Una escala que no es escala**: con 0 —un
  `GetDpiForMonitor` que falla— los 56/32/44/12 valen 0 y el panel cae encima del ancla diciendo que cabe; y el
  conversor de `Pantallas` da infinitos a la ida y NaN a la vuelta. Los dos lanzan `ArgumentOutOfRangeException` con el
  valor que llegó (patrón nº9).
- **2026-09-23 (fase 4). Sin juez, y dicho:** (a) si el área de trabajo no da para el panel y el cuadrado a la vez —las
  cuatro esquinas tapan el ancla, con 340×199 a escala 1 eso pide un `rcWork` de menos de ~750×470 físicos (D)—, se
  queda la opuesta aunque tape: ninguna comprobación lo mira. (b) `ReglaDeDpi.RectTrasCambio` devuelve el calculado
  aunque esté vacío: **para la fase 8**, el overlay no debe llamarla antes de tener su `rcMonitor`, o aplicaría
  `Rect.Empty`. (c) La esquina a la que se va cuando ninguna cabe **no mira los obstáculos** (el notch vive arriba al
  centro y las esquinas del área no lo alcanzan con un panel de 340, D).
- **2026-09-23 (fase 4). De método, y para quien sabotee después:** en Git Bash, `sed -i` sobre un archivo **CRLF**
  le quita **todos** los `\r` (M: `Pantallas.cs` pasó de 5.232 a 5.111 bytes y `diff` marcó las 95 líneas); con
  `sed -b -i` cambia solo la línea (5.232 → 5.206, los 26 caracteres del sabotaje, y `diff` enseña una línea con su
  `^M`). Y `grep -c $'\r$'` de Git Bash **no sirve para contar CRLF**: dio 0 y 99 sobre el mismo archivo en dos
  llamadas; los bytes, contados con `[IO.File]::ReadAllBytes`, dicen que el árbol de trabajo mezcla finales (LF en
  `Contrato.cs` y en la mayoría de `Ui/Jev/`, CRLF en `Pantallas.cs`, `PaletaDeJev.cs` y `ExclusionConElInspector.cs`).
  Es el tropiezo del 2026-08-21 (`CLAUDE.md` §EL CICLO) por el otro lado: allí el patrón no casaba y el sabotaje no se
  aplicó; aquí se aplicaba de más. Los tres de esta fase se comprobaron con `diff` de una línea y se restauraron con
  `cmp` idéntico.
- **2026-09-23 (fase 5). «No se devuelve» se miraba en un solo vuelo, y la curva del plano solo lo cumple en los
  largos.** La tabla de juicio lo comprobaba en el de 780, y ahí la curva del plano (`OverlayWindow.swift:938-994`)
  pasa. En un vuelo corto no: sus mínimos —control 70·s, arco 18·s— ponen el control **más allá** del punto de llegada
  y el camino se pasa y vuelve. Medido en Python sobre la curva tal cual: **2.040 de 3.360** vuelos (28 distancias de
  0 a 5.000 DIP, 24 direcciones, 5 escalas) se devuelven, todos con 58·s de camino o menos; en C#, la primera versión
  sin topes dio «fallan 96 de 120» en la comprobación nueva, el mismo 96 que predijo el barrido (M). Lo pulsado a 60
  DIP de la flecha es lo corriente —el botón de al lado—, así que no es un caso raro. **Arreglo**: el control y el
  arco se acotan a la mitad del camino; con los dos, 0 de 15.408 vuelos se devuelven (M), y con 140·s de camino o
  más los topes no tocan nada (D, por los mínimos: 70·s ≤ la mitad desde ahí). Se añadieron cuatro comprobaciones a
  la 381, antes que el código final y vistas rojas contra la versión ingenua: los 120 vuelos cortos (incluida la
  flecha ya encima de lo pulsado, que no tiene dirección de llegada), `PosicionEn` fuera de [0, 1] —el último
  fotograma llega tarde, y una Bézier evaluada en p=1,5 deja la flecha en (−1004, −6272)—, la escala 0/NaN/−1 y un
  punto NaN o infinito. Las dos últimas son la misma guarda de la fase 4: ya son **3** los sitios que reciben una
  escala con ella, cada uno con su copia de la guarda y su mensaje; si aparece un cuarto, que se unifique.
- **2026-09-23 (fase 5). Dos desviaciones del plano, dichas en `PlanDeVuelo.cs` para que no se descubran en el PC.**
  (1) **La duración cuenta hasta lo pulsado**, no hasta donde se posa: es lo que la 381 enuncia y lo que el contrato
  juzga (780 → 1,5 s); TipTour cuenta hasta la pose de aterrizaje, y la diferencia es 42·s entre 520·s, **0,08 s**
  como mucho y solo entre los topes (D). (2) **Se posa en la recta de llegada**, 42·s antes del centro, y no en la
  mejor de las ocho poses del plano: la pose se puntúa contra el `rcWork` con un margen de 34·s y la firma que la 381
  pide no lo trae. Con la flecha a más de 42·s, el punto de llegada cae en el segmento que la une con lo pulsado, y
  no puede salir del área que los contenga a los dos (D). Sin dirección de llegada —la flecha ya encima— llega desde
  abajo a la derecha, donde descansa (ratón + (35, 25)).
- **2026-09-23 (fase 5). Sin juez, y dicho; para la fase 8:** (a) con la flecha a **menos de 42·s** de lo pulsado,
  el punto de llegada queda detrás de ella y puede caer fuera del `rcWork` (el margen de 34·s del plano no se
  aplica): ninguna comprobación lo mira. (b) Lo que el plano hace **encima** de la curva —el rumbo con +137°, el giro
  suavizado 0,11 por fotograma, la escala `1 + 0,045·sin(pπ)`, el asentamiento de 0,22 s, la píldora— no está en el
  modelo: es de `FlechaDeJev`, y si se quiere juzgado, que sea una propiedad del plan con su comprobación. (c) El
  plan no tiene reloj: quien lo anime calcula `p = transcurrido / Duracion` en cada `CompositionTarget.Rendering`, y
  `PosicionEn` ya se queda en los extremos si p se pasa. (d) El vuelo **en la costura entre monitores** —el camino
  en físicos no salta, la escala de llegada manda— sigue razonado y sin probar: un monitor aquí.
- **2026-09-23 (fase 6). La tabla de juicio de la 379, la 382, la 383, la 384 y la 385 dejaba pasar ramas que el
  código tenía que decidir, y se le añadieron 22 comprobaciones antes que el código.** Se vieron contra una primera
  versión ingenua —copiar la decisión campo a campo, leer `U_JEV_OVERLAY` con `== "si"`, una máquina sin mirar si Jev
  está encendido— y salieron **17 rojas** (M: «CONTRATO ROTO: 28», `fase6\contrato-ingenua.txt`); las otras 5 ya las
  cumplía esa versión y quedan de guardia: copiar `Cumplido` y `Peligro` con distribución, la forma del ciclo
  (`Decidido`, sin pulsada ni tokens), ninguna caja sin cajas, el panel que se aparta con la app detrás y ningún sitio
  sin pantalla. Lo que juzgan: (1) **la traducción** de `DecisionDeUnPaso` a `DecisionDeJev` —el hallazgo de la fase
  2 «para la fase 6»—: la `Puerta` de una que no actúa es la primera de la distribución (M: la ingenua dio «»), sin
  distribución `Cumplido` es `null` y no 0 (M: dio 0), `Ausente` es `null` siempre; (2) **etiqueta y tipo** salen del
  id por el camino que lo formó, con la etiqueta que lleva sus propios paréntesis; (3) **las cajas del evento de C**
  en paralelo a las ofrecidas, y un número distinto de cajas y de ids lanza en vez de emparejar a ojo; (4) un
  **oyente que lanza** no toca la decisión y queda en el log; (5) `U_JEV_OVERLAY` distinto de «si» dice **lo que se
  leyó** —«no», «1», vacía— en vez de «sin U_JEV_OVERLAY» (patrón nº2); (6) con **Jev apagado** nada de Jev se
  enciende aunque el tramo corra y la mano pulse, porque la mano también pulsa para Luna y `UiaSurface.Pulso` avisa
  igual; (7) `EnTramo` es **«un tramo con Jev»**, que es lo que `FaceWindow` le pasará a la regla de la 384; (8) el
  panel **nace ya en su sitio** y sin pantalla no se inventa uno.
- **2026-09-23 (fase 6). El evento de C se consume por `CicloDe`, sin depender de que C esté en `main`.** El PR #117
  (`1cf5d0d`) publica `SurfaceMapTools.AlDecidir(PasoDecidido)` con `Objetivo`, `Ofrecidas`, `Decision`,
  `Ms.Decidir` y `Candidatas` (con `Caja` leída o `Rect.Empty`). `ObservadorDelDecisor.CicloDe` recibe exactamente
  esos campos, en tipos que `main` ya tiene, y es la misma traducción que usa el envoltorio de hoy: cuando C entre, el
  parcial se suscribe con una línea, `CicloDe(e.Objetivo, e.Ofrecidas, e.Decision, e.Ms.Decidir, cajas)`, y las
  cajas verdes aparecen sin tocar el modelo. Dos cosas que el evento trae y el puente no: la caja de cada candidata y
  el número de paso. `PasoDecidido.Decision` puede ser nula (no hubo decisión); `CicloDe` no la acepta —el porqué ya
  llega por la línea de progreso— y el suscriptor la salta.
- **2026-09-23 (fase 6). La comprobación (b) de la 385 buscaba algo que `main` ya había quitado.** Al pasar de
  PENDIENTE a juzgada, «y sigue abriendo el globo (ShowTalk)» salió **roja sin que esta rama tocara el atajo** (M,
  `contrato-ingenua.txt`): `main` cambió en #113 (`043addc`, fusionado en `dc8d3c5`) el cuerpo de `InvocarPorAtajo`
  de `ShowTalk(…, focusInput: true)` a `_acciones?.AbrirChat(true)`, el chat del notch con el foco en el campo. Lo
  prometido no cambia —el atajo abre dónde escribirle a Ü, con el foco, y no sabe de Jev—, así que la comprobación
  pasa a exigir `AbrirChat(true)`; el enunciado se queda como está. Con eso la **385 queda ✔ entera** en esta fase: su
  (b) es una ausencia que ya es cierta, y la fase 9 tiene que mantenerla.
- **2026-09-23 (fase 6). Un comentario contó como código.** La 382 (b) cuenta las apariciones de `BeginInvoke` bajo
  `Ui/Jev/` como texto, y la primera versión de `IDespachador.cs` lo nombraba en un comentario: «hay 1 en
  [IDespachador.cs]» (M). Se reescribió el comentario y queda dicho allí mismo. **Para la fase 8:** ningún comentario
  de `Ui/Jev/` puede nombrar ese método ni `Dispatcher.Invoke(`; el único sitio es `DespachadorDeWpf.cs`.
- **2026-09-23 (fase 6). Sin juez, y dicho; para la fase 8:** (a) **el número de paso**: el envoltorio no lo sabe
  —la costura del decisor no lo trae— y el ciclo sale con `Paso = 0`, que la cabecera pintaría «paso 0»; lo pone quien
  conoce el tramo (el pintor, contando desde `AlEmpezarTramo`, o `PasoDecidido.Paso` cuando entre C). (b) `Cumplido`
  **con distribución se da por preguntado**: un transporte viejo sin la noul deja 0 (`ElDecisor.cs:183`) y el medidor
  dirá 0.00. (c) **La exclusión con el inspector** no la consulta la máquina: `OverlayVisible` es «la vista quiere el
  overlay», y es la ventana la que pregunta a `ExclusionConElInspector` al mostrarse y lo dice si no puede. (d)
  `FlechaVisible` es «vuela o señala a lo pulsado»; el modo «siguiendo» del plano no está en la máquina, y
  `AlTerminarTramo` no esconde la flecha: la esconde quien anima el vuelo al acabarlo, o Escape/soltar/apagar. (e)
  Cambiar `RcWork`, `Escala`, `TamanoDelPanel` o `Ancla` **no recoloca** el panel: se recoloca al encender y al
  conocer lo pulsado, así que la vista pone la pantalla antes de `Encender()`. (f) Una **excepción del decisor** sale
  tal cual y el observador no publica ciclo: el panel se entera por la línea de progreso del tramo, que ya la cuenta.
- **2026-09-23 (fase 7). La tabla de juicio de la 378 daba por buena una versión que no cumplía lo que dice.** Una
  ingenua —una lista a la que se añade sin mirar, `AlMostrar` que reordena siempre, ningún catch, y el grupo con su
  reloj **al lado** del `Vigilar` de siempre— pasaba las 7 comprobaciones que había (M, `fase7\contrato-ingenua2.txt`):
  el orden, el tick, `AlMostrar`, la escondida, la flecha arriba y la (b) entera. Con ella en la app, «un solo reloj»
  habría sido falso —dos relojes, y cualquiera podía volver a llamar al de cada ventana— y la comprobación no lo habría
  visto: la (b) solo pedía que `SiempreDelante.cs` **nombrara** `VigilanteEnOrden`. Se le añadieron **10**
  comprobaciones antes que el código, todas vistas rojas antes del código final (8 contra esa ingenua, «CONTRATO ROTO:
  18»; las 2 que allí no llegaron a mirar nada —«mostrarla después de salir» y el barrido de `.Vigilar()`—, con una
  segunda variante, «CONTRATO ROTO: 13», que además vio rojo el comportamiento de `Sale` y no solo su ausencia).
  Juzgan: (1) **la misma ventana dos veces sube una** —`EntraAlGrupo` puede repetirse, como el `Envolver` de la
  382—; (2) **`Sale` saca la que
  se cierra**, con una «visible» que dice que sí a propósito: las tres de Jev nacen y se cierran con cada vuelta del
  interruptor (383), y sin salir quedarían tres entradas más sujetando ventanas cerradas cada vez; (3) **`AlMostrar`
  por handle**, y solo si es del grupo: el enunciado dice «cuando una **del grupo** se muestra», y con la capa sola
  «una de las overlays» no dice cuál —la fila de juicio aceptaba las dos formas; desde esta fase, solo el handle—; (4)
  **lo que falla en una no para a las demás, y se dice una vez**: el `Vigilar` de antes tenía un catch mudo (patrón
  nº3) y además tiraba el `bool` de `SetWindowPos`, así que una capa que no subía no dejaba ni una línea; ahora
  `SiempreDelante.Subir` lanza `Win32Exception` con el código de Windows y el vigilante lo lleva al log con el paso
  —preguntar si se ve, o subirla—, la capa y el handle, **una vez** mientras falle igual (cada 3 s serían 1.200 líneas
  por hora); (5) **un solo reloj se cuenta**: 1 `new DispatcherTimer` en `SiempreDelante.cs`; (6) **ninguna, no solo
  esas dos** (patrón nº5): el barrido de `.Vigilar()` es sobre todo `windows-client/src`, y se vio rojo con un archivo
  de una línea que no era ni el muelle ni el notch.
- **2026-09-23 (fase 7). Sin juez, y dicho; para la fase 8, la 9 y el nivel 4:** (a) **el cableado WPF** —registrarse
  en `SourceInitialized`, `IsVisibleChanged` → `AlMostrar`, `Closed` → `Sale`, `SetWindowPos` → `Win32Exception`— no
  lo mira el contrato: por fuente solo se exige que `SiempreDelante.cs` se apoye en `VigilanteEnOrden` y tenga un
  reloj. Lo dice la fila «El orden en Z» del nivel 4 (`EnumWindows` a los 10 s, a los 60 s y tras esconder y enseñar).
  (b) **`Capa.Carita` es el muelle**, que es el anfitrión de la carita (272); la **carita suelta** —`FaceWindow`,
  `Topmost="True"` en su XAML y el `Topmost = false; Topmost = true` de `PintarAura` (`FaceWindow.xaml.cs:3018`)— **no
  está en el grupo**, y tampoco lo estaba antes: nunca se vigiló. En la línea «orden Z: … carita 3 …» del nivel 4,
  «carita» es el muelle. (c) De las **12** ventanas `Topmost` del cliente, **10** quedan fuera del grupo —`FaceWindow`,
  su `_panelSuelto` (`:3671`), `AuraDeAprendizaje`, `Aviso`, `CarruselDeApps`, `HighlightOverlay`, `InspectorOverlay`,
  `LocatorBadge`, `OnboardingWindow` y `TarjetasDeRecuerdo`—; ninguna vigilaba antes y esta fase no las toca. Las que
  salgan después del último tick quedan encima del grupo hasta el siguiente, como hasta ahora. (d) Hasta la fase 8 el
  grupo tiene **2** ventanas, y lo único que cambia en la app es el orden entre ellas: el notch queda siempre encima
  del muelle, donde antes ganaba el último reloj que tocara (D: casi nunca se pisan, uno arriba al centro y el otro
  contra el borde derecho). (e) El reloj se crea en el hilo de la primera `EntraAlGrupo`, que es el de la interfaz
  (los constructores del muelle y del notch); una ventana de otro hilo haría lanzar su «visible», y eso ya se dice en
  el log y no para a las demás (comprobación 4).
- **2026-09-23 (fase 8, paso 8a: solo la ventana del overlay).** `Ui/Jev/OverlayDeJev.cs` nace y pone en verde las
  partes (b) que leen solo su fuente: **375 ✔ y 380 ✔**; la **379 sigue ✘ únicamente** por «falta el fuente
  `VistaDeJev.cs`», que no es de este paso (M: su línea de `OverlayDeJev.cs` desaparece). «CONTRATO ROTO: 6» (antes
  9) y **las marcas ✔/✘ de las 313 idénticas a las de la fase 7 salvo 375 y 380** (M, `diff`). De método: el «N
  promesa(s) incumplida(s)» cuenta **comprobaciones** `✘` y pendientes, no promesas (M: base 9 = ocho líneas `✘` —la
  379 llevaba dos— más el `PENDIENTE` de la 376). **Hallazgos:** (1) **las ventanas que cubren solo la primaria son 3,
  no 2** (M, `grep PrimaryScreenWidth`): `AuraDeAprendizaje.cs:123-124`, `HighlightOverlay.cs:52-53` e
  `InspectorOverlay.cs:88-89`; `LaBarraDeTareas.cs:35` usa el rect de la primaria para un cálculo, no para una
  ventana. Ninguna se toca aquí. (2) **Las (b) por subcadena se satisfacen con un comentario**: con el nombre de la
  máscara en un `cref` y en la comprobación posterior, quitar la línea que la aplica habría dejado la 379 igual (D,
  contando apariciones: 4). Se reescribió para que cada nombre juzgado aparezca **una vez, en la línea que actúa**
  (`EstilosDeVentana.ExtendidosDelOverlay`, `.Caducar()`, `ReglaDeDpi.RectTrasCambio(`: 1 cada uno, M); `SetWinEventHook`
  y `SetWindowDisplayAffinity` siguen apareciendo también en su `DllImport`, así que quitar solo la llamada no se ve.
  **Sin juez, y dicho:** el `WM_WINDOWPOSCHANGING` que reescribe posición y tamaño con el rect aplicado (WPF no pasa el
  sugerido a `OnDpiChanged` ni dice si lo aplica antes o después: D, sin dos monitores de DPI distinto); la animación
  del pulso, que solo corre con cajas; el punto de «leída» y la etiqueta, que decide la ventana (hallazgo de la fase
  3); y `WINEVENT_SKIPOWNPROCESS`: volver de Ü a la misma app de trabajo sí caduca las cajas (D). Todo eso es del
  nivel 4. **Sabotaje por diff, uno por promesa** (archivo LF, 26.563 bytes; `sed -b`; cada uno restaurado con `cmp`
  idéntico): (375) `Pintar(Cajas.Caducar())` → `Pintar(Cajas)`, 26.553 bytes → «ROTO: 7», solo la 375 pasa a ✘ con
  «engancha EVENT_SYSTEM_FOREGROUND … y desde ahí llama .Caducar()»; (379) la máscara `EstilosDeVentana.ExtendidosDelOverlay`
  → `0x00080000` escrita a mano, 26.536 bytes → «ROTO: 7» y vuelve la línea «OverlayDeJev.cs aplica
  EstilosDeVentana.ExtendidosDelOverlay…» bajo la 379, con las 313 marcas iguales porque la 379 ya estaba ✘ por la
  vista; (380) `Aplicar(ReglaDeDpi.RectTrasCambio(_rcMonitor, ahora), …)` → `Aplicar(ahora, …)`, 26.524 bytes →
  «ROTO: 7», solo la 380 pasa a ✘ con «sobreescribe OnDpiChanged y desde ahí llama ReglaDeDpi.RectTrasCambio(».
  Restaurado todo, «ROTO: 6» con marcas y motivos idénticos a la corrida verde (M).
- **2026-09-23 (fase 8, paso 8b: solo la ventana del panel).** `Ui/Jev/PanelDeJev.cs` nace y pone en verde la
  **376 entera**: su `Xaml` medido en el arnés da 64 sin resultados, 198,6 con cinco barras, «Resultados» a 314,0 y
  340 de ancho, sin ⚠ («arnés · WPF mide sin pantalla: Consolas 10 → 11,71», M). **Cinco comprobaciones nuevas,
  escritas antes que su código:** (376) la ventana carga su contenido con `XamlReader.Parse(Xaml)`, una vez —si armara
  otro árbol, medir `Xaml` juzgaría un señuelo—; (377) `DondeVaElPanel.NotchEnFisicos(libre, primario)` es el rect de
  `ReglaDeLaBandeja.ArribaAlCentro` llevado a físicos por `Pantallas` —a 1,25, (747,5, 10, 425, 77,5)—, el panel pedido
  junto a él a 1,25 no lo cruza, y `PanelDeJev.cs` lo llama para sus obstáculos; (385) `PanelDeJev.cs` nombra
  `EstilosDeVentana.ExtendidosDelPanel` una vez, en la línea que la aplica, y nace con `ShowActivated = false` y
  `Focusable = false`. **Rojo antes del código** (M): «CONTRATO ROTO: 9» —376 con «falta el fuente PanelDeJev.cs» y su
  `PENDIENTE`, 377 con el `PENDIENTE` de `NotchEnFisicos`, 385 con «falta el fuente»— y las otras 310 marcas idénticas
  a las del 8a. **Verde:** «CONTRATO ROTO: 5» (antes 6), 376 ✘ → ✔, 377 y 385 ✔, 0 SIN JUZGAR, y las demás marcas
  idénticas a las del 8a (M, `diff`). Quedan ✘ 379 y 381 (falta `VistaDeJev.cs`), 382 (`DespachadorDeWpf.cs`), 383
  (`FaceWindow.Jev.cs`) y 384 (las dos llamadas en `FaceWindow.xaml.cs`). **Sitios** (patrón nº5, M por grep):
  `ArribaAlCentro(` fuera de su regla, 2 —el notch (`PanelDeAcciones.cs:849`) y `NotchEnFisicos`—, por el mismo camino;
  `XamlReader.Parse` en el cliente, 2 (`Estudio.cs:515`, un estilo, y el panel). **Sabotaje por diff** (archivos LF;
  `sed -b`; cada uno restaurado con `cmp` idéntico): (376) el relleno inferior `PaddingAbajo` → `PaddingArriba` (7,6 →
  10), 25.118 → 25.119 bytes → «ROTO: 6», solo la 376 con «y con cinco barras 198,6: 201,0»; (377) `NotchEnFisicos`
  sin la escala en el tamaño, 9.141 → 9.105 bytes → «ROTO: 6», solo la 377 con «747,5;10;340;62, esperado
  747,5;10;425;77,5»; (377, segunda, porque la llamada nunca se había visto roja) la ventana calcula el notch por su
  cuenta y en DIP (`ReglaDeLaBandeja.ArribaAlCentro(…)` en vez de `NotchEnFisicos`), 25.134 bytes → «ROTO: 6», solo la
  377 con «PanelDeJev.cs llama DondeVaElPanel.NotchEnFisicos(»; (385) la máscara escrita a mano `0x08080080` —sin
  `WS_EX_TRANSPARENT`—, 25.093 bytes → «ROTO: 6», solo la 385 con «la nombra 0 vez/veces». Restaurado todo, «ROTO: 5»
  con marcas y motivos idénticos a la corrida verde (M). **Vistas rojas solo por ausencia, y dicho:** la de
  `XamlReader.Parse(Xaml)` (376) y la de `ShowActivated`/`Focusable` (385) solo se vieron rojas con el archivo ausente,
  no con uno equivocado. **Sin juez, y dicho; para la 8c y el nivel 4:** (a) **nadie crea el panel todavía**: lo hará
  `VistaDeJev`, que es quien pone `MaquinaDeLaVista.Obstaculos = PanelDeJev.Obstaculos()`, `TamanoDelPanel` y llama
  `Colocar(RectDelPanel)`; (b) el panel **no anima**: los 0,16 s *easeInOut* de textos, barras y alto no están; (c)
  `SizeToContent` crece hacia abajo desde su esquina de arriba, así que en una esquina de arriba pasar de 64 a 198,6 lo
  acerca al ancla: la ventana no se recoloca sola —el sitio es de quien lo calcula— y lo dice con «CONTRASTE panel …:
  el sitio se pidió para W×H y la ventana mide w×h físicos» (tolerancia 1,5 px por el redondeo de `Nitida()`); (d) el
  obstáculo es el notch **cerrado** (340×62): con el chat abierto el notch mide 420×360 (`PanelDeAcciones.cs:397-398`,
  L) y el panel podría cruzarlo; (e) el área libre sale de `LaBarraDeTareas.Mirar()` —`SystemParameters.WorkArea`, DIP
  del primario—, la misma que usa el notch; si la escala del primario cambia después de iniciar sesión, las dos podrían
  no casar (D); (f) `WM_WINDOWPOSCHANGING` reescribe solo la posición, el tamaño es del contenido; (g) una pista con
  valor 0 no se rellena, donde el plano dice `max(2, …)` siempre: el modelo da relleno 0 para un medidor «—», y dos
  DIP ahí enseñarían un dato que no vino (desviación declarada); (h) el divisor `#80373B39` no es un token de la 371
  sino `BordeDelPanel` a la mitad de alfa; (i) el objetivo va en un `TextBlock`: sin campo de texto (§Lo que NO entra).
- **2026-09-23 (fase 8, paso 8c: la ventana de la flecha, y se cierra la fase 8).** `Ui/Jev/FlechaDeJev.cs`,
  `VistaDeJev.cs` y `DespachadorDeWpf.cs` nacen y ponen en verde las (b) que faltaban: **379 ✔, 381 ✔ y 382 ✔**, así
  que la fase 8 queda entera en verde. «CONTRATO ROTO: 2» (antes 5) —solo la 383 y la 384, de la fase 9—, 311 ✔, 0
  SIN JUZGAR, y las marcas de las 313 idénticas a las del 8b salvo 379, 381 y 382, ✘ → ✔ (M, `diff`). **Cinco
  comprobaciones nuevas, escritas antes que su código**, porque una (b) por subcadena se satisface con un comentario o
  con un `DllImport` (hallazgo del 8a), y esta vez se vio: (378) las tres ventanas de Jev entran al grupo, cada una con
  su capa, en una línea de código —el enunciado nombra la flecha, y lo de antes juzgaba al vigilante, no a quién se
  apunta—; (381) `VistaDeJev.cs` **llama** a `GetForegroundWindow()` en una línea que no es su `extern` y calcula el
  vuelo con `PlanDeVuelo.Calcular(`, y `FlechaDeJev.cs` mueve la flecha con `.PosicionEn(` y cuenta con `.Duracion`;
  (382) el único `BeginInvoke` bajo `Ui/Jev` es una llamada. Para eso nace en el contrato `LineasDeCodigo`, que
  descarta lo que va detrás de `//` y las líneas `extern`. **Rojo antes del código** (M): «CONTRATO ROTO: 8» —378 por
  «falta el fuente FlechaDeJev.cs», 379 y 381 por `VistaDeJev.cs`, 381 también por la flecha, 382 con «hay 0 en []» y
  «hay 0», 383 y 384 como estaban—, y de las 313 marcas solo cambió la 378, ✔ → ✘. **De método, medido:** la primera
  corrida roja salió **contaminada**: se escribió `DespachadorDeWpf.cs` mientras el contrato juzgaba, la 382 lo leyó y
  salió ✔. Se apartó el archivo y se repitió. Un fuente escrito durante una corrida entra en sus (b) aunque el binario
  ya esté compilado: mientras corre el contrato no se escribe en el árbol. **Sabotajes por diff** (archivos LF, `sed
  -b`, cada uno restaurado con `cmp` idéntico; cada uno «ROTO: 3» y solo su promesa pasa a ✘): (378) la flecha entra
  como `Capa.PanelDeJev`, 19.647 → 19.651 bytes, «entra al grupo como Capa.Flecha…: hay 0»; (379) la vista arma la
  configuración por reflexión con «si» fijo, sin leer el entorno, 18.334 → 18.411, «lee la configuración con
  ConfiguracionDeLaVista.Leer(»; (381) `deDelante` sale de `WindowFromPoint` en vez de `GetForegroundWindow()`, 18.334
  → 18.404, «LLAMA a GetForegroundWindow()…: hay 0», **y la comprobación de antes siguió verde** porque el nombre seguía
  en el `DllImport` y en un comentario, que es justo el caso que la nueva existe para ver; (381, segunda, porque la de la
  ventana solo se había visto roja por ausencia) la flecha vuela en línea recta con su propia interpolación, 19.647 →
  19.689, «mueve la flecha con .PosicionEn(…: 0 y 3»; (382) el adaptador ejecuta en el acto y nombra el método solo en
  un comentario, 3.502 → 3.525, «esa aparición es una llamada…: hay 0», **y la cuenta de texto de antes siguió en 1 y
  verde**. Restaurado todo, «ROTO: 2» con marcas y motivos idénticos a la corrida verde (M). **Sitios** (patrón nº5,
  grep, M): ventanas que entran al grupo, 5 en todo el cliente (muelle, notch, overlay, panel y flecha), y ningún
  `.Vigilar()`; `BeginInvoke` bajo `Ui/Jev`, 1; `CompositionTarget.Rendering +=` en el cliente, 4 (la flecha, `Vuelo.cs`
  ×2 y `LanzarConScroll.cs`); llamadas a `GetForegroundWindow()` en el cliente, 22, y la de la vista es la única que
  decide si vuela la flecha. **Sin juez, y dicho; para la fase 9 y el nivel 4:** (a) **nadie crea la vista todavía**:
  `new VistaDeJev(Dispatcher, ancla)`, `Sincronizar` desde `PintarBotonJev`, `Publicar` como `alDecidir` de
  `ObservadorDelDecisor.Envolver`, `AlPulsar` desde `UiaSurface.Pulso`, `Suelta` desde Escape y el señalador, y
  `EnTramo` para la regla de la 384 son del parcial. (b) **La pulsada por número de la línea de progreso** («paso k:
  «x» (n)») no se lee todavía: `Progreso` pone la línea sobre el último decidido sin `Pulsada`, así que el panel resalta
  la elegida hasta que el puente la lea, con su comprobación. (c) **La ventana de trabajo es la que está en el centro de
  lo pulsado** (`WindowFromPoint` subida a su raíz) y está delante si coincide con la raíz de `GetForegroundWindow()`.
  Que `WindowFromPoint` salte nuestras ventanas transparentes al ratón es D; la carita, que no lo es, encima de lo
  pulsado daría «detrás» (D; con la 384 cableada, en un tramo con Jev no viaja al clic). (d) **`FlechaVisible` no baja
  cuando la flecha se esconde sola** tras señalar 3 s: la máquina no tiene con qué (`Suelta` también vacía cajas y
  corrida). La ventana se esconde y la máquina la sigue contando hasta Escape, soltar o apagar. (e) **El coste se
  acumula en el pintor**: con el coalescing, un ciclo decidido que no llega a pintarse no suma sus tokens. Hoy no se ve
  —el puente no trae tokens y el coste dice «—»—; cuando A y C los traigan, hay que acumular en `Publicar`. (f)
  **Modos**: volando y señalando. «Siguiendo», el asentamiento de 0,22 s, el spinner y la escala de entrada de la
  píldora no están; el radio de los `BlurEffect` (16 y 6), sin medir. (g) `.Duracion` aparece también en dos líneas de
  log de la flecha, así que esa mitad de su comprobación se cumple sin contar el tiempo con ella; la mitad fuerte es
  `.PosicionEn(`, que está en una sola línea. (h) El vuelo en la costura entre monitores de escala distinta, sin probar:
  aquí hay un solo monitor. (i) `DespachadorDeWpf.Encolar` lanza si WPF devuelve la operación abortada, para que el
  conector baje su marca y lo diga; sin juez, porque cerrar el `Dispatcher` en el arnés lo rompería.
- **2026-09-23 (fase 9: el cableado mínimo; el nivel 4, escrito y sin correr).** `Ui/FaceWindow.Jev.cs` nace y
  `FaceWindow.xaml.cs` gana **4 líneas, 0 editadas** (M, `git diff --numstat`), y la 383 y la 384 pasan a ✔:
  «CONTRATO INTACTO», 313 ✔, 0 SIN JUZGAR, y de las 313 marcas solo cambian la 383 y la 384, ✘ → ✔, respecto a la
  final del 8c (M, `diff`). **Diez comprobaciones nuevas, escritas antes que su código**, con un ayudante nuevo,
  `GanchoLlevaA`, que busca el gancho en líneas de código y exige lo que hace en los 240 caracteres de código que le
  siguen: (381) el parcial engancha `UiaSurface.Pulso +=` a `.AlPulsar(`; (382) envuelve el `Decisor` con
  `ObservadorDelDecisor.Envolver(` hacia `Publicar`; (383) `Senalador.Suelta +=` y `Freno.SePulso +=` son código que
  lleva a `.Suelta()`, el parcial llama `.Sincronizar(`, `Closed +=` lleva a `.Sincronizar(false)` y el cuerpo de
  `PintarBotonJev` sincroniza; (384) las dos líneas de la regla le pasan `EnTramo` y el parcial le pasa el tramo a la
  vista con `.AlEmpezarTramo(` y `.AlTerminarTramo(`. **Rojo antes del código, con el archivo presente** (M): contra una
  versión ingenua —el parcial con solo el campo y los dos ganchos en un comentario, y las dos líneas de la regla con
  `LaCaritaViaja(false)`— «CONTRATO ROTO: 10», las diez `✘`, y de las 313 marcas solo cambiaron la 381 y la 382, ✔ →
  ✘. **Y esa ingenua pasaba las dos (b) que ya había** (M: ni un `✘` de «se suscribe a …» ni de «en exactamente 2
  líneas»): la de la 383 busca el gancho por subcadena y se satisfizo con el comentario —el hallazgo del 8a, otra
  vez—, y la cuenta de la 384 se cumple con una constante. **Sabotajes por diff**, uno por promesa, cada uno de una
  línea y restaurado con `cmp` idéntico (el parcial es LF, 8.531 bytes; `FaceWindow.xaml.cs` es CRLF, 328.527 bytes,
  con `sed -b`); cada uno da «ROTO: 1» con solo su promesa en ✘ (M, `diff` de marcas): (381) la suscripción a `Pulso`
  con el cuerpo vacío, 8.501 bytes, «engancha UiaSurface.Pulso += en código que lleva a .AlPulsar(»; (382)
  `mapa.Decisor = decisor` en vez de envolverlo, 8.484, «envuelve el Decisor con ObservadorDelDecisor.Envolver(…»;
  (383) la suscripción a `Freno.SePulso` comentada, 8.534, «y Freno.SePulso += es código que lleva a .Suelta(), no un
  comentario», **y la comprobación vieja siguió verde**; (384) borrar la línea de la regla en `IrJuntoA`, 328.391, «…en
  exactamente 2 líneas…: hay 1»; (385) `_vistaDeJev?.Suelta();` dentro de `InvocarPorAtajo`, 328.550, «y su cuerpo no
  nombra a Jev ni a _vistaDeJev» —la (b) de la 385 no se había visto roja nunca con el archivo presente (fase 6: «una
  ausencia que ya es cierta»)—. Restaurado todo, la corrida final da marcas y motivos idénticos a la verde (M).
  **Desviación del diseño, y dicha** (§El cableado): 4 líneas y no 7, porque los tres avisos del tramo se encadenan
  desde el parcial. **Sin juez, y dicho; para el nivel 4:** (a) el encadenado depende del orden: quien reasigne un
  aviso después de `NacerLaVistaDeJev` deja a la vista sin él, y ninguna comprobación lo vería (hoy hay 3 asignaciones
  y todas van antes, M); (b) la pulsada por número de la línea de progreso sigue sin leerse (hallazgo (b) del 8c), así
  que el «la 1.ª no estaba» no se va a ver con D sola; (c) el primer ciclo lleva el objetivo con «tramo: » delante
  (`ElTramo.cs:132`, L) y los decididos sin él (`:146`, L); (d) `AnclaDeLaCarita` pregunta a `GetWindowRect` por la
  carita o, si está guardada, por el muelle, y lanza si Windows no contesta; `SincronizarLaVistaDeJev` lo recoge y lo
  dice entero, la carita sigue y la vista queda apagada; (e) cerrar Ü cierra las tres (`Closed`), porque fuera del
  arranque la app sale con la última ventana (`App.xaml.cs:111`, L): que el proceso termine de verdad es la fila 13
  del nivel 4; (f) cuatro filas de la tabla del nivel 4 nombraban cosas que no existen —el transporte falso, la línea
  `overlay: caducó`, la línea `orden Z:` y una línea `atajo:` de éxito— y se corrigen en §Lo que tiene que mirar el
  dueño; (g) **el nivel 4 no se hizo**: en esta fase no se ejecuta `U.exe`.

- **2026-09-24 (la revisión del 2026-09-23: diez hallazgos, ocho distintos, y los ocho ciertos).** El 1 y el 8 son
  el mismo (la exclusión con el inspector), y el 4 y el 9 también (la flecha fuera de tramo). Cada uno se comprobó
  contra `HEAD` (`0e49e5e`) antes de arreglarlo, y ninguno resultó falso (M):
  1. **371, la exclusión solo existía en el contrato**: `OverlayActivo =` tenía **0** asignaciones en
     `windows-client/src` y `VistaDeJev` abría los overlays sin preguntar; la (b) se satisfacía con que el inspector
     *nombrara* la clase (aprendizaje nº18). Ahora la vista pregunta `PuedeEncender(Cual.Overlay)` antes de crear
     ninguna ventana, la máquina sabe que el overlay está **impedido** (`ImpedirElOverlay`, y su `Estado` lo dice con
     su porqué: «encendido» con el inspector delante sería el estado que miente) y la bandera se pone al enseñar
     ≥ 1 overlay y se quita al apagar. Sitios (nº5): las **2** superficies que comparten la paleta —el inspector y el
     overlay de Jev— preguntan y ponen su bandera, en **4** asignaciones y **2** preguntas; y hay **1** sitio que crea
     overlays de Jev (`OverlayDeJev.ParaCadaMonitor()`, `VistaDeJev.cs`), el que pregunta.
  2. **385, el sitio del panel con el alto de 0 barras**: `TamanoDelPanel` tenía **1** asignación, con `AltoDe(0)`.
     Ahora `MaquinaDeLaVista.AlPintarBarras(n)` pone el tamaño de lo que se pinta y recoloca con lo pulsado que ya se
     conocía, y la vista la llama cada vez que cambia el número de barras, mide (`UpdateLayout`) y mueve la ventana.
  3. **372, la pulsada de la línea de progreso**: la vista publicaba la línea sin `Pulsada`. Ahora
     `CicloDeJev.ConLaLinea` saca el número que va entre paréntesis justo antes de « conf» (`ElTramo.cs:204`), y
     **solo** si la línea dice «cambió» o «no cambió»: con «no pudo» la mano no terminó, y marcarla haría decir al
     ticker «pulsé la 2.ª» de algo que no se pulsó. Decisión de esta revisión, más estrecha que el hallazgo, y juzgada.
  4. **384, dos cuerpos por clic**: la vista y la máquina miraban `Encendida` y la carita `EnTramo`. Sitios: **2**
     suscriptores de `UiaSurface.Pulso` (`FaceWindow.xaml.cs:1326`, la carita, y `FaceWindow.Jev.cs:76`, la vista), y
     ahora los dos deciden por `EnTramo`; dentro de la vista, **2** guardas (`VistaDeJev.Pulsada` y
     `MaquinaDeLaVista.AlConocerPulsada`), las dos cambiadas. De las dos salidas que daba el hallazgo se toma «la
     flecha exige `EnTramo`» y no «la carita recibe `Encendida`», porque la 384 promete que fuera de tramo la carita
     **sigue** viajando.
  5. **383, las barras volvían tras Escape**: `Suelta` no olvidaba la última decisión y `Pintar` solo miraba
     `Encendida`. Ahora `Suelta` olvida la decisión y el objetivo **en el hilo que avisa** (la línea de parada llega
     antes de que la interfaz vacíe su cola) y la vista pinta lo que dice `MaquinaDeLaVista.QueSePinta`: en corrida,
     el ciclo; sin corrida, solo el motivo. **Consecuencia, y dicha**: una decisión fuera de un tramo —un
     `map_decidir` suelto antes del primer tramo— tampoco se pinta: no hay corrida. Lo que no se pinta se factura
     igual (373).
  6. **Una etiqueta en el log**: `FlechaDeJev.cs:299` escribía la de la candidata pulsada. Sitios: **1** de las
     líneas de log de `Ui/Jev/` y `FaceWindow.Jev.cs` (M, grep de sus interpolaciones: las demás llevan cuentas,
     rects, handles, tiempos y tipo y mensaje de excepción). Ahora «con píldora (N carácter(es))». **Sin juez, y
     dicho**: la regla es la de §Con qué se juzga y no es una promesa numerada; ninguna comprobación la vigila.
  7. **El medidor `ausente` sin fuente**: se decide la salida (a) del hallazgo, en §Lo que NO entra, con dueño.
  10. **377, el panel encima del menú de la carita**: el ancla era el centro de la ventana de la carita. Ahora la vista
     da la carita **entera** (`FaceWindow.Jev.cs`, `DondeEstaLaCarita`, `GetWindowRect` sin reducir) y
     `DondeVaElPanel.JuntoALaCarita` cuenta los 56/32 desde su borde y no tapa ni ella ni los 22 de alrededor;
     `Calcular(punto)` es la carita de 0×0 y da lo mismo que antes. La carita se vuelve a leer antes de cada
     recolocación: abrir el menú le cambia el tamaño. **El cableado, contado** (M, `git diff --numstat`):
     `FaceWindow.xaml.cs`, **0** líneas; el parcial `FaceWindow.Jev.cs`, +8 −6, de las que **3** son de código (el
     nombre que se le pasa a la vista, la firma y el `return` del rect) y el resto su comentario.
  9 (= 4) y 8 (= 1), sin más que añadir; del 9, además, **3 de 3** ventanas de Jev se excluyen ya de la captura
  (overlay, flecha y panel; antes 2 de 3): es el hallazgo del panel en las fotos, que venía en el mismo lote.
  **Rojo antes que el código** (M, `out\contrato-rojo.txt`, sesión anterior, con las comprobaciones nuevas y la
  producción de `HEAD`): «CONTRATO ROTO: 14 promesa(s) incumplida(s).», con 371, 372, 377, 383, 384 y 385 en ✘ y las
  capacidades nuevas en `PENDIENTE`. **Y re-medido en esta sesión con el `Contrato.cs` final** —los siete archivos
  de producción puestos a su versión de `HEAD`, restaurados después y comparados byte a byte: idénticos—: «CONTRATO
  ROTO: 14 promesa(s) incumplida(s).», 5 `PENDIENTE` y 9 comprobaciones en ✘, y el `diff` de las 313 marcas contra
  el verde cambia exactamente esas seis, ✔ → ✘ (M, `out\contrato-rev-rojo.txt`).
  **Verde**: «CONTRATO INTACTO», 313 ✔ y 0 ✘, con las marcas idénticas a las de la fase 9 (M, `diff` de las 313).
  **Sabotajes por diff**, uno por promesa, de una línea, con un script que exige que el patrón aparezca exactamente
  una vez, enseña el `diff`, corre el contrato y restaura comparando byte a byte (los seis archivos son LF sin BOM):
  (371) `VistaDeJev.cs:217`, `OverlayActivo = true` comentado: «CONTRATO ROTO: 1», «✘ y pone OverlayActivo = true
  cuando los enseña»; (372) `CicloDeJev.cs:166`, `Pulsada = desde.Pulsada` —la vista de antes—: «ROTO: 5», las cinco
  de `ConLaLinea` y la 372 sola en ✘; (377) `MaquinaDeLaVista.cs:252`, `Calcular(Ancla, …)` —el centro de antes—:
  «ROTO: 1», «✘ con la carita de 292×400 puesta, la máquina pone el panel fuera de ella: 1250;732;340;199», encima
  de ella; (383) `MaquinaDeLaVista.cs:190`, `if (Encendida) return ciclo;`: «ROTO: 2», «✘ pero sin objetivo ni barras
  … resultados PINTADOS» y «✘ y una decisión que llega después de Escape no vuelve a pintar la corrida»; (384)
  `MaquinaDeLaVista.cs:243`, `if (!Encendida) return;`: «ROTO: 1», «✘ con Jev encendido y sin tramo … flecha True,
  carita True»; (385) `MaquinaDeLaVista.cs:225`, el tamaño con `AltoDe(0)` —el de antes—: «ROTO: 4», «✘ con cinco
  barras, el tamaño con que se calcula es 340×198,6: 340;64», el sitio y el recolocado con 64 (656;854;340;64), y
  «a escala 1,5 … 510;96». En los seis, el `diff` de las 313 marcas contra el verde cambia **solo** la promesa
  saboteada, ✔ → ✘, y la restauración salió **idéntica**. El número de «ROTO: N» cuenta comprobaciones, no
  promesas: en la 372 son 5 comprobaciones de una sola promesa.
  **De método, y para quien compare marcas después**: `grep -o "^[✔✘] [0-9]*\."` en Git Bash da **0 líneas** —el
  corchete no casa caracteres de varios bytes— y `diff` de dos archivos vacíos dice que son iguales: la primera
  comparación de esta revisión salió «iguales» sin comparar nada. Se compara con `grep -oE "^(✔|✘) [0-9]+\."` y se
  cuentan las líneas (313) **antes** de fiarse del `diff`.
  **Sin juez, y dicho; para el nivel 4:** (a) si Luna llama otra herramienta mientras corre un tramo y había algo
  señalado, `SurfaceMapTools.Call` suelta lo señalado (`:2079`) y el panel queda sin corrida hasta el siguiente
  tramo, aunque el tramo siga: es lo que dice la 383, y ahora se ve más porque las decisiones de después ya no se
  pintan; (b) si una decisión está en la cola de la interfaz cuando llega Escape, el orden de la cola puede dejar el
  panel en reposo **sin** el motivo; (c) la carita se vuelve a leer al encender, al cambiar el número de barras y al
  conocer lo pulsado: abrir o cerrar su menú sin nada más no mueve el panel; (d) hallazgo de paso, fuera de esta rama
  (`Navigation/` no se toca): `ElTramo.cs:209` avisa del progreso dentro de un `catch { }` mudo (patrón nº3); (e) el
  nivel 4 **no se hizo**: en esta revisión no se ejecuta `U.exe`.

## Cierre

- [x] Todas las promesas verdes (`.\scripts\contrato-del-grafo.ps1` → CONTRATO INTACTO, 0 SIN JUZGAR) — fase 9, 313 ✔ (M); y tras la revisión del 2026-09-23, otra vez 313 ✔ y 0 ✘, con las marcas idénticas a las de la fase 9, y la voz «VOZ ÍNTEGRA» (M, 2026-09-24)
- [ ] Un sabotaje por promesa verificado por diff (copia, rompe, diff, compila sin silenciar, ROTO nombrando esa promesa, restaura, diff idéntico, INTACTO)
- [ ] `.\scripts\verificar.ps1` pasa, con evidencia en `out\evidencia.md`
- [ ] Nivel 4 **con D sola** en ≥ 2 pantallas, con nombre: …
- [ ] Nivel 4 **con C** (cajas verdes) y **con A** (el «$»): repetido sobre `main` con C y A dentro, antes de abrir el PR
- [ ] Dos escalas de DPI: **sin probar (un monitor)**, dicho en el PR
- [ ] Estado de este documento: **implementado** (AAAA-MM-DD)
