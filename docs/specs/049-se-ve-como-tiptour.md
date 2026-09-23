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
| **371** | la paleta de Jev es la del plano y se juzga sin pincel: cada token guarda su ARGB exacto, su significado y si es cromático —saturación HSL de 0,25 o más—, dos tokens cromáticos con distinto significado distan al menos 30° de tono salvo los tres pares aceptados por escrito —los azules entre sí—, los neutros y los acromáticos se juzgan solo por su valor, no hay token para lo que no existe —el cian del OCR—, y el overlay de Jev y el inspector no se encienden a la vez: la regla es pura y el inspector la consulta, que se lee en su fuente | 1 |
| **372** | el panel de Jev sale de un modelo puro: dado un ciclo —objetivo, paso, candidatas, decisión, lo pulsado y tiempos— produce exactamente lo que se pinta: la cabecera «paso k» «· N detectados» «{ms}ms», los medidores cumplido y ausente, y cinco barras como mucho ordenadas de mayor a menor probabilidad con su valor a dos decimales y la etiqueta sola —numerada solo si dos de las cinco la comparten—; la resaltada es la pulsada por su id —la elegida solo mientras no se conozca la pulsada—, nunca una por parecido de texto; y si la mano pulsó otra que la elegida, el ticker lo dice | 2 |
| **373** | sin distribución no hay gráfico: con Luna, con la regla local o con una respuesta sin probabilidades no se pintan cabecera, medidores ni barras; el coste es el acumulado de la tarea a partir de los tokens facturados que devuelva el cliente, con cinco decimales, y sin tokens se enseña «—» y no crece; el ms de la cabecera es el de decidir; y un medidor sin dato enseña «—», nunca 0 | 2 |
| **374** | los textos del panel describen el paso y no concluyen: cada estado tiene su cadena exacta, el motivo con que el tramo para se enseña tal cual llega —sin reescribirlo—, «Jev cree que ya está» nunca se convierte en «listo» ni en «terminado», pulsar la segunda mejor se dice como lo que es, y el punto del ticker va a 0,82 mientras el tramo sigue y a 0,42 cuando para | 2 |
| **375** | el overlay pinta exactamente la lista que se ofreció al decisor: una caja por candidata con caja leída y ninguna más, en su orden, con su etiqueta sola; una candidata sin caja se ofrece pero no se pinta y se cuenta; la rosa es la de lo pulsado —por su id, o por la caja que la mano dice haber pulsado— y nunca la elegida antes de pulsar ni una por etiqueta —dos «Buscar» dan una sola rosa—; y las cajas caducan al cambiar la ventana de delante: la caducidad es pura y el gancho que la dispara se lee en el fuente del overlay | 3 |
| **376** | el panel de Jev mide lo que dice el plano y su alto sale de sus partes y de un mínimo declarado, no del contenido: 340 de ancho, 64 sin resultados —el mínimo, que calca los 63,7 del vídeo; las partes suman 57,6— y 198,6 con cinco barras; el XAML medido sin pantalla da lo mismo y la zona de resultados recibe sus 314 enteros porque el borde no ocupa sitio; y si WPF no mide en el arnés, la promesa queda SIN JUZGAR, ni verde ni roja | 4 (pura) · 8 (XAML) |
| **377** | el panel se pone donde no estorba, y se calcula sin pantalla: junto al ancla a (56, 32) por la escala, probando las cuatro esquinas en orden, sin tapar el cuadrado de 44 alrededor del ancla, a 12 del borde del área de trabajo, sin cruzar el notch ni ningún obstáculo ni la caja de lo pulsado si alguna esquina cabe, y si ninguna cabe se va a la esquina opuesta; nunca se sale del área de trabajo | 4 |
| **378** | un solo vigilante sube las capas de Ü en un orden fijo —overlays de cada monitor, carita, notch, panel de Jev, flecha— con un solo reloj, reordena en el acto cuando una del grupo se muestra, y ninguna vigila por su cuenta: el muelle y el notch entran al grupo y dejan de hacerlo, que se lee en sus fuentes | 7 |
| **379** | el overlay de Jev es una ventana por monitor en píxeles físicos, transparente, click-through, no activable, de herramienta y excluida de la captura —las máscaras son puras y el overlay las aplica, que se lee en su fuente—, y por defecto está apagado: lo enciende U_JEV_OVERLAY=si al encender Jev, la configuración es pura y dice por qué quedó como quedó, y el estado de la vista lo nombra | 6 |
| **380** | todo lo de Jev se dibuja por Pantallas: la inversa —de físicos a la unidad de un monitor concreto, restando su origen— existe y con la ida da la identidad, un monitor secundario con otra escala convierte bien, no hay ninguna conversión a mano nueva bajo Ui/Jev, y al cambiar el DPI el overlay vuelve a aplicar el rect calculado y no el sugerido: la regla es pura y su llamada desde OnDpiChanged se lee en el fuente | 4 |
| **381** | la flecha vuela a lo pulsado con la duración del plano y no cuando la app no está delante: la duración es la distancia entre 520 por la escala, acotada entre 1,05 y 2,2 s; aterriza a 42 por la escala del centro de lo pulsado; el camino sale y llega parado y no se devuelve; con la ventana de trabajo detrás de otra no hay vuelo; y quién está delante se le pregunta a Windows, no se da por hecho, que se lee en el fuente de la vista | 5 |
| **382** | nada de la vista bloquea el ciclo: la decisión pasa por el observador intacta —el mismo objeto, y una excepción del decisor sale tal cual—, envolver dos veces envuelve una, publicar un ciclo solo encola y nunca ejecuta en el acto —con un despachador que lanza si se le pide ejecutar ya, publicar no lanza—, con la cola sin vaciar se pinta solo el último ciclo, una excepción al pintar no sale al ciclo y queda en el log con su tipo y su mensaje, y el único BeginInvoke de la vista vive en su adaptador a WPF | 6 |
| **383** | apagar Jev cierra las tres ventanas —overlay, panel y flecha— y Escape o soltar lo señalado vacían el overlay, esconden la flecha y dejan el panel sin corrida: la máquina es pura y sus dos ganchos se leen en el parcial; el interruptor del decisor sigue apagando el catálogo byte a byte | 6 |
| **384** | una sola cosa vuela por clic: mientras corre un tramo con Jev la carita ni viaja al clic ni sigue al cursor sintético, fuera de tramo sigue haciéndolo, y la carita no cambia de tamaño: la regla es pura y sus dos llamadas en FaceWindow se cuentan en el fuente | 6 (regla) · 9 (cableado) |
| **385** | el panel de Jev nunca toma el ratón ni el foco —click-through y no activable siempre—, nace al encender Jev sin tocar el atajo de invocar a Ü —InvocarPorAtajo sigue abriendo el globo y no sabe de Jev, que se lee en su fuente—, y se aparta de lo pulsado en cuanto lo conoce | 6 (regla) · 9 (cableado) |

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
| 371 | (a) `PaletaDeJev.Todos` trae cada token con `Nombre`, `Argb`, `Significado` y `EsCromatico` (= `SaturacionHsl(Argb) ≥ 0.25`); se comprueba el valor exacto de los del plano (`FondoDelPanel = 0xFF101211`, `BordeDelPanel = 0xB8373B39`, `TextoPrimario = 0xFFECEEED`, `TextoSecundario = 0xFFADB5B2`, `TextoTerciario = 0xFF6B736F`, `Placeholder = 0xFF939594`, `BarraElegida = 0xFF60A5FA`, `BarraNoElegida = 0x8C2563EB`, `Pista = 0x0DFFFFFF`, `Cumplido = 0xE634D399`, `Ausente = 0xE6FFB224`, `AzulDelCursor = 0xFF4F8EF7`, `Candidata = 0x9438FF2E`, `Elegida = 0xE6FF476B`, `FondoDeEtiqueta = 0x7A000000`, `FondoDeEtiquetaElegida = 0xB8000000`); los cromáticos son exactamente 7 (`BarraElegida`, `BarraNoElegida`, `Cumplido`, `Ausente`, `AzulDelCursor`, `Candidata`, `Elegida`); para cada par de cromáticos con `Significado` distinto, `DistanciaDeTono(a, b) ≥ 30` **o** el par está en `ChoquesAceptados` con su porqué, y `ChoquesAceptados` son exactamente los tres azules entre sí («los tres son Jev trabajando; se distinguen por alfa y por sitio: dos viven dentro del panel y uno fuera»); no existe ningún token cuyo nombre contenga «Ocr» ni «Cian»; `ExclusionConElInspector.PuedeEncender(Cual.Overlay)` es falso con el inspector marcado activo y viceversa. (b) `Uia/UiInspector.cs` contiene `ExclusionConElInspector.` | cambiar `Elegida` a `0xE6FFA020`: tono 34,4°, a **4,5°** de `Ausente` (M), par cromático no aceptado → 371 roja. (También rompe `Candidata → 0x943FBF6F`: S 0,50, a 15,6° de `Cumplido`.) |
| 372 | (a) un ciclo de mentira con 7 candidatas (dos con la etiqueta «Buscar», ids `3) Buscar (Button)` y `6) Buscar (Button)`), decisión `Actuar` sobre `6) Buscar (Button)` con `Alternativas` de 7 probabilidades y `MsDecidir = 399`: `EstadoDeLaDecision.De(ciclo, coste)` devuelve `Cabecera.Paso == "paso 2"`, `Detectados == "· 7 detectados"`, `Ms == "399ms"`; `Barras.Count == 5`, ordenadas de mayor a menor, `Valor` con `"0.00"` invariante; las dos «Buscar» salen como `"3) Buscar"` y `"6) Buscar"` y las demás sin número; con `Pulsada == null` exactamente **una** `Resaltada`, la de id `6) Buscar (Button)`, marcada `PorElegida`; con `Pulsada = "6"` (el número que trae la línea de progreso) la resaltada es la misma y `PorPulsada`; con `Pulsada = "3"` la resaltada pasa a `3) Buscar (Button)` —comparación por el **número del id**, `Ordinal`, por el mismo camino que `SurfaceMapTools.cs:318`— y el ticker es `"la 1.ª no estaba: pulsé la 2.ª «Buscar» (3)"`; y con 1.450 ms la cabecera dice `"1,450ms"` | quitar el `OrderByDescending` de las barras, o resaltar por `Etiqueta ==` en vez de por el número del id |
| 373 | (a) tres ciclos: con `Alternativas` vacías (Luna) y con la regla local → `Resultados == null` (ni cabecera, ni medidores, ni barras); dos ciclos con `TokensFacturados = 875` → `Coste == "$0.00007"` (875·2·0,042/10⁶ redondeado a 5 decimales) y `Acumulado` crece; un ciclo con `TokensFacturados = null` → `Coste == "—"` y `Acumulado` **no** cambia; `Ausente = null` → el medidor enseña `"—"` y su relleno es 0 | devolver `"$0.00000"` cuando no hay tokens |
| 374 | (a) `TextosDeJev` expone las cadenas y `EstadoDeLaDecision` las usa: estado «mirando» → `"Mirando la pantalla"` con punto 0,82; «eligiendo» con N=150 → `"Paso 1 — 150 elementos"`; decisión con `Cumplido ≥ 0,70` → `"Jev cree que ya está (0.94). No acciono más."` con punto 0,42; `Peligro ≥ 0,50` → `"«Grabar» no se deshace (peligro 0.80). Me detengo."`; confianza bajo umbral → `"No estoy seguro (0.31). Me detengo."`; pulsada ≠ elegida → `"la 1.ª no estaba: pulsé la 2.ª «Detalles» (2)"`; una línea de progreso del tramo `"paso 3: «Detalles» (2) conf 0.91 · no cambió"` → ticker **idéntico** y punto 0,82; una línea `"tramo: 3 paso(s) · se agotó el tope de 4 paso(s)."` → ticker `"se agotó el tope de 4 paso(s)."` y punto 0,42; y ninguna cadena de `TextosDeJev` contiene «listo», «terminado», «hecho» ni «éxito» | escribir «Listo: ya está» en el caso cumplido |
| 375 | (a) `CajasDelOverlay.De(candidatas, pulsadaId: null)` con 5 candidatas de las que 2 vienen sin caja (`Caja == null`): devuelve **3** cajas en el orden de la lista, `SinCaja == 2`, cada una con `Etiqueta` sola (sin «2) » ni tipo) y **ninguna** rosa aunque el ciclo traiga elegida; con dos «Buscar» y `pulsadaId = "6) Buscar (Button)"` exactamente **una** `Rosa`; `ConPulsada(cajaDeLaMano)` sin candidatas (el puente) → una sola caja, rosa, con la geometría que trajo la mano y `EsLeida = true`; `Caducar()` deja la lista vacía; y una candidata con caja `EsLeida = false` (estimada) se pinta **sin** punto. (b) `Ui/Jev/OverlayDeJev.cs` contiene `SetWinEventHook`, `EVENT_SYSTEM_FOREGROUND` y `.Caducar()` | resaltar por `Etiqueta.Contains(...)`, o pintar las de `Caja == null` con un rect vacío, o poner la rosa en la elegida con `pulsadaId == null` |
| 376 | (a) `MedidaDelPanelDeJev.Ancho == 340`, `AltoMinimo == 64`, `SumaDePartes(0) == 57.6` (±0,05: `PaddingArriba 10` + 18 + 8 + 14 + `PaddingAbajo 7.6`), `AltoDe(0) == 64` (= `max(AltoMinimo, SumaDePartes(0))`), `AltoDe(5) == 198.6` (±0,05: partes 18/14/11/11/15×5 y huecos 8/8/(1+5)/5/5/5) y `AltoDe(3) < AltoDe(5)` con paso 20 por barra; `AnchoDeResultados == 314`. **Parte XAML** (fase 8): en un hilo STA del arnés, `XamlReader.Parse` del recurso del panel + `Measure/Arrange` da `64` sin resultados —por el `MinHeight="64"` del `Border`, citado como parte— y `198.6` con cinco barras (±0,5), y el `StackPanel` `Resultados` recibe `314.0`; si WPF no arranca en el runner, la prueba llama `NoPudeJuzgar` (fase 0): ni ✔ ni ✘, y el veredicto final lo dice | pura: `AltoMinimo` 64 → 60 (da `AltoDe(0) == 60`); XAML: quitar el `MinHeight` del `Border` (mide 57,6 sin resultados), cambiar `PaddingAbajo` a 10 (da 201 con cinco barras), o poner el `BorderThickness` en el `Border` con hijos (recorta a 312,4) |
| 377 | (a) `DondeVaElPanel.Calcular(ancla, tamaño, rcWork, escala, obstáculos, objetivo)` con `rcWork = (0,0,1920,1040)`, escala 1, panel 340×199 y ancla (600, 400): esquina superior izquierda en (656, 432) y `Esquina == AbajoDerecha`; con el ancla en (1800, 400) no cabe a la derecha → `AbajoIzquierda` y `Right ≤ 1908`; con el notch en `ReglaDeLaBandeja.ArribaAlCentro` como obstáculo y el ancla arriba al centro, el resultado no lo cruza; con `objetivo = (700, 450, 200, 40)` (inflado 8) el resultado no lo cruza si alguna esquina cabe; con un objetivo tan grande que ninguna cabe → la esquina de `rcWork` opuesta al objetivo; en todos los casos `rcWork.Contains(resultado)` y el cuadrado de 44 alrededor del ancla queda libre; con escala 1,5 los 56/32/44/12 salen multiplicados | devolver siempre la primera esquina sin probar las demás |
| 378 | (a) `OrdenEnZ.Capas` es exactamente `[Overlays, Carita, Notch, PanelDeJev, Flecha]` de abajo arriba; `VigilanteEnOrden` construido con handles de mentira y un `subir(handle)` que anota: `Tick()` llama `subir` en ese orden y una vez por ventana visible; `AlMostrar(PanelDeJev)` provoca un reordenado completo en el acto; una ventana escondida no se sube. (b) `Ui/Muelle.cs` y `Ui/PanelDeAcciones.cs` contienen `SiempreDelante.EntraAlGrupo(this` y **no** contienen `.Vigilar()`; `Ui/SiempreDelante.cs` contiene `VigilanteEnOrden` | invertir el orden de la lista; o quitar `EntraAlGrupo` de `Muelle.cs` (la (b) lo ve aunque `.Vigilar()` tampoco esté) |
| 379 | (a) `EstilosDeVentana.ExtendidosDelOverlay == 0x080800A0` (= `WS_EX_TRANSPARENT 0x20 | LAYERED 0x80000 | TOOLWINDOW 0x80 | NOACTIVATE 0x08000000`), `Afinidad == 0x11`; `EstilosDeVentana.UnaPorMonitor([(0,0,1920,1080), (1920,0,2560,1440)])` devuelve dos rects físicos iguales a los `rcMonitor`; `ConfiguracionDeLaVista.Leer(n => n == "U_JEV_OVERLAY" ? "si" : null).OverlayEncendido == true` y `Leer(_ => null).OverlayEncendido == false` con `Porque` que nombra la variable («overlay apagado: sin U_JEV_OVERLAY»); `new MaquinaDeLaVista(Leer(_ => null)).OverlayEncendido == false` y `Estado` dice «overlay: apagado (sin U_JEV_OVERLAY)»; con la configuración encendida, `Encender()` deja `OverlayEncendido == true` y `Estado` dice «overlay: encendido (U_JEV_OVERLAY=si)». (b) `Ui/Jev/OverlayDeJev.cs` contiene `EstilosDeVentana.ExtendidosDelOverlay` y `SetWindowDisplayAffinity`; `Ui/Jev/VistaDeJev.cs` contiene `ConfiguracionDeLaVista.Leer(` | quitar `NOACTIVATE` de la máscara; o que `Leer(_ => null)` devuelva encendido |
| 380 | (a) `Pantallas.DelMonitor(rcMonitorFisico, escala)` da un conversor: `AUnidad((2000, 300))` con monitor en (1920, 0) y escala 1,5 → (53.33, 200); `AFisico(AUnidad(p)) == p` (±0,01) para 20 puntos; `ReglaDeDpi.RectTrasCambio(calculado, sugerido) == calculado`. (b) el fuente bajo `windows-client/src/Ui/Jev/` no contiene `TransformFromDevice` ni `DpiScaleX`, y `OverlayDeJev.cs` contiene `OnDpiChanged` y `ReglaDeDpi.RectTrasCambio(` | no restar el origen del monitor en `AUnidad`; o quitar la llamada a `RectTrasCambio` de `OnDpiChanged` (la (b) la echa en falta) |
| 381 | (a) `PlanDeVuelo.Calcular(inicio, centroPulsado, escala, appDelante)`: con `appDelante = false` → `null`; con distancia 260 y escala 1 → `Duracion == 1.05 s` (el mínimo); con 1.500 → `2.2 s` (el máximo); con 780 → `1.5 s`; `Fin` dista `42·escala` del centro; `PosicionEn(0) == inicio`, `PosicionEn(1) == Fin`; la distancia al fin es **no creciente** en 100 muestras de p (no se devuelve); y la velocidad en p=0 y p=1 es 0 (`|PosicionEn(0.01) − PosicionEn(0)| < |PosicionEn(0.5) − PosicionEn(0.49)|`). (b) `Ui/Jev/VistaDeJev.cs` contiene `GetForegroundWindow` y **no** contiene `appDelante: true` | cambiar `Math.Clamp(d / 520, 1.05, 2.2)` por `d / 520` a secas; o pasar `appDelante: true` |
| 382 | (a) `ObservadorDelDecisor.Envolver(interno, alDecidir)`: llamar al envuelto devuelve **el mismo objeto** `DecisionDeUnPaso` que devolvió el interno, `alDecidir` recibió un ciclo con `MsDecidir ≥ 0` y las mismas ids, un interno que lanza `InvalidOperationException` la deja pasar tal cual (mismo tipo, mismo mensaje), y `Envolver(Envolver(f))` tiene por `Interno` a `f` (no a un envoltorio: `EstaEnvuelto` distingue). `ConectorDeLaVista(IDespachador)` con dos operaciones, `Encolar(Action)` y `Ahora(Action)`: con un doble cuyo `Ahora` lanza, `Publicar(ciclo)` no lanza; con un doble que acumula sin ejecutar, 10 `Publicar` seguidos y luego `Vaciar()` → el pintor recibe **1** ciclo, el último; con un doble que no ejecuta nunca, `Publicar` retorna (sin medir ms); un pintor que lanza deja en el log una línea con `InvalidOperationException` y el mensaje, y `Publicar` no lanza. (b) bajo `windows-client/src/Ui/Jev/` hay exactamente **1** aparición de `BeginInvoke` (en `DespachadorDeWpf.cs`) y **0** de `Dispatcher.Invoke(` | llamar `Ahora` en vez de `Encolar` en el conector (el doble lanza), pintar la cola entera, o envolver sin mirar si ya está envuelto |
| 383 | (a) `MaquinaDeLaVista` con las tres visibles: `Apagar()` → `OverlayVisible`, `PanelVisible` y `FlechaVisible` en `false`; con corrida en marcha, `Suelta()` → overlay vacío (`CajasVisibles == 0`), `FlechaVisible == false`, `PanelEnCorrida == false` y el panel sigue visible; la promesa 290 sigue verde tal cual (apagar el interruptor no cambia por esta rama). (b) `Ui/FaceWindow.Jev.cs` contiene `Senalador.Suelta +=` y `Freno.SePulso +=` (Escape siempre, haya tramo o no: `Freno.cs:82`) y las dos llevan a `.Suelta()` | que `Apagar()` deje `PanelVisible = true`; o quitar la suscripción a `Freno.SePulso` |
| 384 | (a) `ReglaDeQuienVuela.LaCaritaViaja(enTramoConJev: true) == false` y `(false) == true`; la promesa 240 sigue verde (su fixture no está en tramo) y la 163 también (la carita no cambia de tamaño: esta rama no toca su ventana). (b) `Ui/FaceWindow.xaml.cs` contiene exactamente **2** líneas con `ReglaDeQuienVuela.LaCaritaViaja(` | devolver siempre `true`; o borrar uno de los dos `if` |
| 385 | (a) `EstilosDeVentana.ExtendidosDelPanel` incluye `WS_EX_TRANSPARENT` y `WS_EX_NOACTIVATE` sin condición de estado; `MaquinaDeLaVista.Encender()` deja `PanelVisible == true` (nace con Jev); `MaquinaDeLaVista.AlConocerPulsada(caja)` recoloca el panel llamando a `DondeVaElPanel` con esa caja como `objetivo` (se comprueba que el rect nuevo no la cruza). (b) en `Ui/FaceWindow.xaml.cs`, el cuerpo de `InvocarPorAtajo` (del `private void InvocarPorAtajo()` al siguiente `private void`) **no** contiene «Jev» ni `_vistaDeJev` | que el panel en reposo pierda `WS_EX_TRANSPARENT`; o meter un `if` de Jev en `InvocarPorAtajo` |

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
| `OrdenEnZ` + `VigilanteEnOrden` | la lista de capas y el vigilante de grupo con `subir` inyectable | sí | 378 |
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
| `Ui/SiempreDelante.cs` | añade `EntraAlGrupo(Window, Capa)` y `VigilarEnOrden()` sobre `VigilanteEnOrden` | no | 378 |
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
UiaSurface.Pulso(x, y, w, h)         → ciclo «pulsado» con CajaPulsada (geometría real de la mano)
                                       → overlay: la rosa sobre esa caja (por id si la caja calza con una candidata; sola si no)
                                       → PlanDeVuelo.Calcular(…, appDelante: GetForegroundWindow() es la de trabajo) → flecha vuela MIENTRAS la mano ya pulsó
                                       → MaquinaDeLaVista.AlConocerPulsada → el panel se aparta si la tapa
Progreso("paso k: «x» (n) conf … · cambió")  → ciclo con Pulsada = n → panel: la resaltada es la n; si n ≠ elegida, ticker
                                               «la 1.ª no estaba: pulsé la 2.ª «x» (n)»; si no, la línea tal cual; punto 0,82
Progreso("tramo: N paso(s) · <motivo>")      → ticker: el motivo tal cual, punto 0,42, ninguna barra resaltada
AlTerminarTramo()                    → flecha vuelve/se esconde; overlay se queda hasta que cambie la ventana de delante
Escape (Freno.SePulso) / Senalador.Suelta → overlay vacío, flecha escondida, panel sin corrida (383)
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

`SiempreDelante.EntraAlGrupo(window, capa)` + `VigilarEnOrden()`: un reloj de 3 s que en cada tick sube en el orden
de `OrdenEnZ.Capas` con `SetWindowPos(HWND_TOPMOST, SWP_NOMOVE|NOSIZE|NOACTIVATE)`, y reordena en el acto tras cada
`Show()` del grupo. `Muelle.cs:175` y `PanelDeAcciones.cs:247` cambian `this.Vigilar()` por `EntraAlGrupo` (**2
sitios**, patrón nº5; la (b) de la 378 exige la llamada nueva, no solo la ausencia de la vieja). El grupo se arma al
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
| **Qué toca** | `Ui/Jev/CicloDeJev.cs`, `EstadoDeLaDecision.cs`, `TextosDeJev.cs`, `CosteDeJev.cs` (nuevos) |
| **¿Núcleo congelado?** | no |
| **Terminado** | 372–374 verdes; 3 sabotajes por diff; ninguna cadena concluye; con `Pulsada = "3"` y elegida `6)` el ticker dice que se pulsó la 2.ª |
| **Sitios con esta clase de error** | textos que hoy concluyen por el modelo: 1 (`ElTramo.cs:157-158`, «el objetivo ya está cumplido», que A reescribe en la 349; la vista no lo repite); resaltados que ignoran la segunda mejor: 0 hoy (no hay vista), 0 después |

### Fase 3 — el modelo del overlay

| | |
|---|---|
| **Promesa que pone verde** | 375 (parte (a); la (b) se lee en la fase 8) |
| **Qué toca** | `Ui/Jev/CajasDelOverlay.cs` (nuevo) |
| **¿Núcleo congelado?** | no |
| **Terminado** | 375 (a) verde; 285 intacta (la lista es la misma); con `pulsadaId == null` ninguna rosa |
| **Sitios con esta clase de error** | resaltados por subcadena en el repo: 1 (`SurfaceMapTools.cs:299`, `Porque.Contains("cumplido")`, que A quita); en la vista, 0 |

### Fase 4 — la geometría pura

| | |
|---|---|
| **Promesa que pone verde** | 376 (parte pura), 377, 380 (parte (a)) |
| **Qué toca** | `Ui/Jev/MedidaDelPanelDeJev.cs`, `DondeVaElPanel.cs`, `ReglaDeDpi.cs` (nuevos); `Ui/Pantallas.cs` (inversa por monitor) |
| **¿Núcleo congelado?** | no |
| **Terminado** | las tres verdes; 249/251/260 (medida y sitio del notch) intactas; `DondeVaElPanel` recibe el notch de `ReglaDeLaBandeja.ArribaAlCentro` como obstáculo; `AltoDe(0) == 64` por el mínimo y `SumaDePartes(0) == 57.6` |
| **Sitios con esta clase de error** | conversiones DPI a mano fuera de `Pantallas`: 7 (contadas arriba); esta fase añade 0 y quita 0 (no son suyas); altos que salen del contenido: 0 (`MedidaDelNotch.AltoDe` ya es fijo) |

### Fase 5 — el vuelo

| | |
|---|---|
| **Promesa que pone verde** | 381 (parte (a)) |
| **Qué toca** | `Ui/Jev/PlanDeVuelo.cs` (nuevo) |
| **¿Núcleo congelado?** | no |
| **Terminado** | 381 (a) verde; 240 intacta (la curva de la carita no cambia) |
| **Sitios con esta clase de error** | esperas fijas por decoración: 0 en `main` desde #83 (anatomía §2); la flecha no reintroduce ninguna |

### Fase 6 — el conector, la máquina y las reglas de una línea

| | |
|---|---|
| **Promesa que pone verde** | 379 (a), 382 (a), 383 (a), 384 (a), 385 (a) |
| **Qué toca** | `Ui/Jev/ObservadorDelDecisor.cs`, `IDespachador.cs`, `ConectorDeLaVista.cs`, `MaquinaDeLaVista.cs`, `EstilosDeVentana.cs`, `ConfiguracionDeLaVista.cs`, `ReglaDeQuienVuela.cs` (nuevos) |
| **¿Núcleo congelado?** | no |
| **Terminado** | las cinco verdes en su parte (a); 290 intacta; el catch del pintor cuenta tipo y mensaje (patrón nº3); `Envolver(Envolver(f)).Interno == f`; el coalescing se juzga con un despachador que acumula, sin reloj |
| **Sitios con esta clase de error** | `Dispatcher.Invoke` síncronos en la costura del tramo: 0 hoy (`FaceWindow.xaml.cs:464` ya usa `BeginInvoke`; el `Invoke` de `:414` es del localizador, no del tramo); la vista no añade ninguno. Pruebas del contrato que dependen del reloj: la 341 de la voz arrastra un `Wait(3 s)`; esta rama añade **0** |

### Fase 7 — un solo vigilante

| | |
|---|---|
| **Promesa que pone verde** | 378 |
| **Qué toca** | `Ui/Jev/OrdenEnZ.cs` (nuevo); `Ui/SiempreDelante.cs` (`EntraAlGrupo`, `VigilarEnOrden`); `Ui/Muelle.cs:175`; `Ui/PanelDeAcciones.cs:247` |
| **¿Núcleo congelado?** | no |
| **Terminado** | 378 verde en (a) y (b): grep `\.Vigilar\(\)` da 0 fuera de `SiempreDelante.cs` **y** `Muelle.cs` y `PanelDeAcciones.cs` contienen `EntraAlGrupo(this` |
| **Sitios con esta clase de error** | 2 (`Muelle.cs:175`, `PanelDeAcciones.cs:247`) |

### Fase 8 — las tres ventanas

| | |
|---|---|
| **Promesa que pone verde** | 376 (parte XAML); 375, 379, 380, 381, 382 (partes (b): por fuente) |
| **Qué toca** | `Ui/Jev/OverlayDeJev.cs`, `PanelDeJev.cs` (+ su XAML como recurso), `FlechaDeJev.cs`, `DespachadorDeWpf.cs`, `VistaDeJev.cs` (nuevos) |
| **¿Núcleo congelado?** | no |
| **Terminado** | compila en Release; el XAML real mide 64/198,6 en el arnés; contrato intacto; las ventanas solo llaman al modelo (grep: ni `OrderBy` ni `Contains(` sobre etiquetas dentro de las tres ventanas); exactamente 1 `BeginInvoke` bajo `Ui/Jev/` |
| **Sitios con esta clase de error** | ventanas de Ü que cubren solo la primaria: 2 (`InspectorOverlay.cs:86-89`, `AuraDeAprendizaje.cs:122-124`); `OverlayDeJev` no repite el patrón |

### Fase 9 — el cableado mínimo, y el nivel 4

| | |
|---|---|
| **Promesa que pone verde** | 383 (b), 384 (b), 385 (b) |
| **Qué toca** | `Ui/FaceWindow.Jev.cs` (nuevo, parcial); `Ui/FaceWindow.xaml.cs` (**≤ 7 líneas**, contadas en el commit; `InvocarPorAtajo` intacto) |
| **¿Núcleo congelado?** | no (la UI no está congelada, pero es zona de choque alta: aviso en `#miracle-updates` al abrir) |
| **Terminado** | contrato intacto (15/15, 0 sin juzgar); sabotajes por diff de las 15; nivel 4 **con D sola** (tabla de abajo, ≥ 2 pantallas con nombre y el log pegado); el nivel 4 de las cajas verdes queda expresamente pendiente de C |
| **Sitios con esta clase de error** | ganchos que tocan el archivo grande cuando cabían en un parcial: 0 después de esta fase (todo lo de Jev vive en el parcial salvo las ≤ 7 líneas) |

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
- **2026-09-22.** `UiaSurface.Pulso` (`windows-graph/.../UiaSurface.cs:139`) ya avisa con la caja real de lo que la
  mano pulsa, en físicos, y la carita lo escucha (`FaceWindow.xaml.cs:1318`). Es lo que hace posible una rosa honesta
  sin esperar a C: lo que se resalta es lo que se pulsó, con la geometría de quien lo pulsó.

## Cierre

- [ ] Todas las promesas verdes (`.\scripts\contrato-del-grafo.ps1` → CONTRATO INTACTO, 0 SIN JUZGAR)
- [ ] Un sabotaje por promesa verificado por diff (copia, rompe, diff, compila sin silenciar, ROTO nombrando esa promesa, restaura, diff idéntico, INTACTO)
- [ ] `.\scripts\verificar.ps1` pasa, con evidencia en `out\evidencia.md`
- [ ] Nivel 4 **con D sola** en ≥ 2 pantallas, con nombre: …
- [ ] Nivel 4 **con C** (cajas verdes) y **con A** (el «$»): repetido sobre `main` con C y A dentro, antes de abrir el PR
- [ ] Dos escalas de DPI: **sin probar (un monitor)**, dicho en el PR
- [ ] Estado de este documento: **implementado** (AAAA-MM-DD)
