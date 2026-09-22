# Se ve como TipTour: el overlay, el panel y la flecha de Jev, alimentados por un modelo que el contrato juzga

Estado: **en construcción** · Nace del encargo del dueño del 2026-09-22 («trae main, revisa y cambia la arquitectura al nuevo Jev») · Rama: `jero/jev-se-ve-como-tiptour` · Base: `main` `dde8c40` + `f811796` (fusionado el 22-09 al abrir la rama) · Promesas **371–385**

> Rama **D** de las cuatro de la nueva arquitectura de Jev (`scratchpad\jev\arquitectura-jev-en-u.md`, §7). Entra
> **después de C** (spec 048): consume su evento tipado `AlDecidir` (368) y la candidata con caja (363). Hasta que C
> entre, esta rama se alimenta por un puente provisional que se describe abajo y que deja el overlay **vacío y
> diciéndolo**, nunca pintando cajas inventadas.
>
> Todo lo visual sale de `scratchpad\jev\plano-para-wpf.md` (medido sobre el vídeo de TipTour y leído en su código
> MIT, `5258246`). Aquí no se discute otra vez: se cita el token, la medida o la duración, y se dice qué promesa la
> juzga. **Marcas:** (M) medido —log, fotograma, `grep`, `Measure`—; (D) deducido; sin marca, una meta.

## Diagnóstico: qué se midió

| Qué | Medida | Fuente |
|---|---|---|
| Lo que hoy se **ve** de una decisión de Jev en Ü | una línea de texto en el notch (`Progreso`) y un botón «Jev · on/off»; **0** cajas, **0** barras, **0** medidores | `Mcp/SurfaceMapTools.cs:1730`, `Ui/FaceWindow.xaml.cs:464-469, 2889-2896` (M, grep) |
| Lo que el vídeo de TipTour enseña por paso | cajas verdes sobre cada candidato, rosa para el elegido, panel de 340 pt con «step k · N detected · NNNms $acumulado», medidores `done`/`absent` y 5 barras; una flecha azul que vuela 1,05–2,2 s al elegido | `plano-para-wpf.md` §Tokens, §Medidas, §Duraciones (M sobre fotogramas) |
| Cuánto tarda decidir en Ü, que es el «ms» de la cabecera | p50 322–350 ms con 20/60/160 puertas; 1.066 ms en frío | `docs/anatomia-del-clic-2026-09-18.md` §3 (M) |
| Cuánto cuesta un paso, que es el «$» | `usage.input_tokens` × **$0,042 / M** tokens de entrada; 875 tokens con 20 puertas = $0,000037 por paso: con 4 decimales el contador parecería roto | `docs/computer-use-con-jev-2026-09-21.md:41`; anatomía §3 (M × precio publicado) |
| Quién lee hoy `usage.input_tokens` | **nadie**: `ClienteTypeSafe.Pregunta` devuelve la respuesta cruda y `ElDecisor` no la lee | `Decision/ClienteTypeSafe.cs:78`, `Decision/ElDecisor.cs:163-186` (M, lectura) |
| Ventanas de Ü que vigilan «siempre delante» **por su cuenta**, cada una con su reloj | **2**: `Muelle.cs:175` y `PanelDeAcciones.cs:247`; con overlay, panel y flecha serían 5 relojes compitiendo | grep `\.Vigilar\(\)` (M) |
| Conversiones de DPI escritas a mano fuera de `Pantallas` | **7**: `FaceWindow.Escritorio.cs:111`, `FaceWindow.xaml.cs:1885, 2211, 4682, 4706`, `InspectorOverlay.cs:100`, `PanelDeAcciones.cs:282`; todas válidas solo en el monitor primario | grep `TransformFromDevice\|DpiScaleX` (M; la arquitectura decía 6) |
| Pesos de fuente que WPF resuelve de verdad | `Segoe UI Variable Text` + `Medium` → **SemiBold**; `Consolas` + `Medium` → **Regular**; Cascadia Mono **no está** en esta máquina | `recortes/revision/medir-panel.txt` (M) |
| Alto natural de una línea en WPF | Consolas 10 → 11,71; Segoe 9 → 11,97: las filas de 11 **recortan** sin `BlockLineHeight` | mismo guion (M, `Measure`) |
| El XAML del panel medido sin pantalla | 340×**64** sin resultados y 340×**198,6** con cinco barras; la versión con `BorderThickness` en el mismo `Border` daba 202,6 y recortaba `Resultados` a 312,4 | `recortes/revision/medir-panel.ps1` (M) |
| El «elegido» rosa en el vídeo | **nunca aparece** (barrido t=62–134): lo que parece elegido es el objetivo del cursor burbuja, la caja más cercana al ratón real | `plano` §El estado «elegido» (M) |
| Cuántos cuerpos viajan hoy por clic | 1: la carita plegada sigue al cursor sintético y viaja a cada clic (promesa 240) | `FaceWindow.xaml.cs:1876-1892, 4657-4658` (M, lectura) |
| Monitores en esta máquina | **1**: dos escalas de DPI no se pueden probar aquí | (M) |

## Por qué esto va dirigido por especificación

Una vista se da por buena a sí misma más que ningún otro subsistema: se abre, «se ve bien», y nadie comprueba que
lo pintado sea lo ofrecido. Este repo ya pagó esa clase de fallo tres veces —la caja que miente (aprendizaje nº4), el
destello amarillo indistinguible del ámbar (`windows-client/CLAUDE.md` §Paleta) y el «29 de 30» con 19 pasos comidos
(nº10)—. Y aquí hay cuatro sitios concretos donde una vista escrita a ojo mentiría:

1. **Barras sin distribución.** Con Luna o con la regla local no hay probabilidades; pintar sus «confianzas» como
   barras sería un gráfico de nada.
2. **Un coste inventado.** Nadie lee hoy `input_tokens`: un `$` calculado con un número supuesto es una cifra falsa
   en la pantalla de un hospital.
3. **El elegido por parecido.** Dos «Buscar» existen en SAP; resaltar por subcadena ilumina el que no se va a pulsar.
4. **La vista en el camino del clic.** Un `Dispatcher.Invoke` en la costura devuelve al ciclo los segundos que B y C
   están quitando.

Por eso la vista sale de un **modelo puro** (`EstadoDeLaDecision`, `CajasDelOverlay`, `DondeVaElPanel`,
`PlanDeVuelo`, `OrdenEnZ`): dado un ciclo, produce exactamente lo que se pinta, y eso se juzga en un segundo sin
pantalla, sin SAP y sin TypeSafe. Las ventanas WPF solo dibujan lo que el modelo devuelve.

**La regla del flujo, y no tiene excepciones:** ninguna línea de producción entra antes que la promesa que la
juzga. Cada fase empieza con el contrato ROTO y termina con el contrato INTACTO.

## Lo que se ve, en una frase

Un **overlay** por monitor, transparente y *click-through*, que pinta una caja verde con su etiqueta sobre cada
candidata que se le ofreció al decisor y una rosa sobre la elegida; un **panel** de 340 DIP que enseña el objetivo,
el estado del paso, «paso k · N detectados · {ms}ms ${acumulado}», los medidores `cumplido`/`ausente` y cinco
barras; y una **flecha** azul que vuela al elegido mientras la mano ya pulsó. Todo alimentado por un modelo puro, por
`Dispatcher`, sin que el ciclo espere a nada de ello.

> **Nota de nombres.** El encargo llama «cursor burbuja» a lo que vuela. En el plano lo que vuela es **la flecha**
> (`OverlayWindow.swift`), y «el burbuja» es el anillo verde que respira alrededor del ratón **real** en reposo. Aquí
> se promete la flecha; el burbuja en reposo queda fuera (ver *Lo que NO entra*).

## La especificación

El enunciado es el que va **literalmente** en `tests/ContratoDelGrafo/Contrato.cs`, bajo `// ── Spec 049 ──`.

| # | Promesa | Fase que la pone verde |
|---|---|---|
| **371** | la paleta de Jev es la del plano y se juzga sin pincel: cada token guarda su ARGB exacto y su significado, dos tokens con distinto significado distan al menos 30° de tono salvo los choques aceptados por escrito, no hay token para lo que no existe —el cian del OCR—, y el overlay de Jev y el inspector no se encienden a la vez | 1 |
| **372** | el panel de Jev sale de un modelo puro: dado un ciclo —objetivo, paso, candidatas, decisión y tiempos— produce exactamente lo que se pinta: la cabecera «paso k» «· N detectados» «{ms}ms», los medidores cumplido y ausente, y cinco barras como mucho ordenadas de mayor a menor probabilidad con su valor a dos decimales y la etiqueta sola —numerada solo si dos de las cinco la comparten—, y la resaltada es la del id que se va a pulsar, nunca una por parecido de texto | 2 |
| **373** | sin distribución no hay gráfico: con Luna, con la regla local o con una respuesta sin probabilidades no se pintan cabecera, medidores ni barras; el coste es el acumulado de la tarea a partir de los tokens facturados que devuelva el cliente, con cinco decimales, y sin tokens se enseña «—» y no crece; el ms de la cabecera es el de decidir; y un medidor sin dato enseña «—», nunca 0 | 2 |
| **374** | los textos del panel describen el paso y no concluyen: cada estado tiene su cadena exacta, el motivo con que el tramo para se enseña tal cual llega —sin reescribirlo—, «Jev cree que ya está» nunca se convierte en «listo» ni en «terminado», y el punto del ticker va a 0,82 mientras el tramo sigue y a 0,42 cuando para | 2 |
| **375** | el overlay pinta exactamente la lista que se ofreció al decisor: una caja por candidata con caja leída y ninguna más, en su orden, con su etiqueta sola; una candidata sin caja se ofrece pero no se pinta y se cuenta; el elegido se resalta por su id y no por su etiqueta —dos «Buscar» dan una sola rosa—; y las cajas caducan en cuanto cambia la ventana de delante | 3 |
| **376** | el panel de Jev mide lo que dice el plano y su alto sale de sus partes, no del contenido: 340 de ancho, 64 sin resultados y 198,6 con cinco barras; el XAML medido sin pantalla da lo mismo, y la zona de resultados recibe sus 314 enteros porque el borde no ocupa sitio | 4 (pura) · 8 (XAML) |
| **377** | el panel se pone donde no estorba, y se calcula sin pantalla: junto al ancla a (56, 32) por la escala, probando las cuatro esquinas en orden, sin tapar el cuadrado de 44 alrededor del ancla, a 12 del borde del área de trabajo, sin cruzar el notch ni ningún obstáculo ni la caja del elegido si alguna esquina cabe, y si ninguna cabe se va a la esquina opuesta; nunca se sale del área de trabajo | 4 |
| **378** | un solo vigilante sube las capas de Ü en un orden fijo —overlays de cada monitor, carita, notch, panel de Jev, flecha— con un solo reloj, reordena en el acto cuando una del grupo se muestra, y ninguna vigila por su cuenta: el muelle y el notch dejan de hacerlo | 7 |
| **379** | el overlay de Jev es una ventana por monitor en píxeles físicos, transparente, click-through, no activable, de herramienta y excluida de la captura, y por defecto está apagado: se enciende y se apaga con un interruptor que dice su estado | 6 |
| **380** | todo lo de Jev se dibuja por Pantallas: la inversa —de físicos a la unidad de un monitor concreto, restando su origen— existe y con la ida da la identidad, un monitor secundario con otra escala convierte bien, no hay ninguna conversión a mano nueva bajo Ui/Jev, y al cambiar el DPI se vuelve a aplicar el rect calculado y no el sugerido | 4 |
| **381** | la flecha vuela al elegido con la duración del plano y no cuando la app no está delante: la duración es la distancia entre 520 por la escala, acotada entre 1,05 y 2,2 s; aterriza a 42 por la escala del centro del elegido; el camino sale y llega parado y no se devuelve; y con la ventana de trabajo detrás de otra no hay vuelo | 5 |
| **382** | nada de la vista bloquea el ciclo: la decisión pasa por el observador intacta —el mismo objeto, y una excepción del decisor sale tal cual—, publicar un ciclo vuelve antes de que se pinte, con el pintor atascado se pinta solo el último ciclo, y una excepción al pintar no sale al ciclo y queda en el log con su tipo y su mensaje | 6 |
| **383** | apagar Jev cierra las tres ventanas —overlay, panel y flecha— y Escape o soltar lo señalado vacían el overlay, esconden la flecha y dejan el panel sin corrida; el interruptor del decisor sigue apagando el catálogo byte a byte | 6 |
| **384** | una sola cosa vuela por clic: mientras corre un tramo con Jev la carita ni viaja al clic ni sigue al cursor sintético, fuera de tramo sigue haciéndolo, y la carita no cambia de tamaño | 6 (regla) · 9 (cableado) |
| **385** | el panel de Jev nunca toma el ratón ni el foco —click-through y no activable siempre—, se abre por el mismo atajo de invocar a Ü cuando Jev está encendido y con Jev apagado el atajo sigue abriendo el globo, y se aparta del elegido en cuanto lo conoce | 6 (regla) · 9 (cableado) |

**La que de verdad cierra el asunto es la 372**: mientras la vista no salga de un modelo que se pueda juzgar, todo lo
demás —tokens, medidas, orden en Z— es cosmético sobre algo que nadie sabe si dice la verdad. La segunda es la
**382**: si la vista se cuela en el camino del clic, deshace lo que B y C consiguen.

### Con qué se juzga cada una

Todo con **mapa a mano en la propia prueba** y datos de mentira: ni pantalla, ni SAP, ni TypeSafe. Las capacidades se
piden por nombre con `Capacidad("U.WindowsClient.Ui.Jev.…")` y, si no existen, `Pendiente(…, "NNN", "049")`. Los
ARGB van en `uint`, como `PaletaDelNotch`, para que el contrato no levante WPF. **Ninguna prueba ni ninguna línea de
log de esta rama lleva una etiqueta de candidata**: los fixtures usan etiquetas de mentira («Detalles», «Buscar»,
«Grabar») y el log de la vista escribe cuentas y tiempos, nunca texto de pantalla.

| # | Cómo se juzga (sin pantalla) | Sabotaje de una línea |
|---|---|---|
| 371 | `PaletaDeJev.Todos` trae cada token con `Nombre`, `Argb` y `Significado`; se comprueba el valor exacto de los del plano (`FondoDelPanel = 0xFF101211`, `BordeDelPanel = 0xB8373B39`, `TextoPrimario = 0xFFECEEED`, `TextoSecundario = 0xFFADB5B2`, `TextoTerciario = 0xFF6B736F`, `Placeholder = 0xFF939594`, `BarraElegida = 0xFF60A5FA`, `BarraNoElegida = 0x8C2563EB`, `Pista = 0x0DFFFFFF`, `Cumplido = 0xE634D399`, `Ausente = 0xE6FFB224`, `AzulDelCursor = 0xFF4F8EF7`, `Candidata = 0x9438FF2E`, `Elegida = 0xE6FF476B`, `FondoDeEtiqueta = 0x7A000000`, `FondoDeEtiquetaElegida = 0xB8000000`); para cada par con `Significado` distinto, `DistanciaDeTono(a, b) ≥ 30` **o** el par está en `ChoquesAceptados` con su porqué (el azul del cursor y el de las barras: «los dos son Jev»); no existe ningún token cuyo nombre contenga «Ocr» ni «Cian»; `ExclusionConElInspector.PuedeEncender(Cual.Overlay)` es falso con el inspector marcado activo y viceversa | cambiar `Candidata` a `0x943FBF6F` (el verde «shell mapeado» del inspector): la distancia de tono con `Cumplido` baja de 30 |
| 372 | un ciclo de mentira con 7 candidatas (dos con la etiqueta «Buscar», ids `3) Buscar (Button)` y `6) Buscar (Button)`), decisión `Actuar` sobre `6) Buscar (Button)` con `Alternativas` de 7 probabilidades y `MsDecidir = 399`: `EstadoDeLaDecision.De(ciclo, coste)` devuelve `Cabecera.Paso == "paso 2"`, `Detectados == "· 7 detectados"`, `Ms == "399ms"`; `Barras.Count == 5`, ordenadas de mayor a menor, `Valor` con `"0.00"` invariante; las dos «Buscar» salen como `"3) Buscar"` y `"6) Buscar"` y las demás sin número; exactamente **una** `Elegida`, la de id `6) Buscar (Button)`, y con 1.450 ms la cabecera dice `"1,450ms"` | quitar el `OrderByDescending` de las barras, o resaltar por `Etiqueta ==` en vez de por `Id ==` |
| 373 | tres ciclos: con `Alternativas` vacías (Luna) y con la regla local → `Resultados == null` (ni cabecera, ni medidores, ni barras); dos ciclos con `TokensFacturados = 875` → `Coste == "$0.00007"` (875·2·0,042/10⁶ redondeado a 5 decimales) y `Acumulado` crece; un ciclo con `TokensFacturados = null` → `Coste == "—"` y `Acumulado` **no** cambia; `Ausente = null` → el medidor enseña `"—"` y su relleno es 0 | devolver `"$0.00000"` cuando no hay tokens |
| 374 | `TextosDeJev` expone las cadenas y `EstadoDeLaDecision` las usa: estado «mirando» → `"Mirando la pantalla"` con punto 0,82; «eligiendo» con N=150 → `"Paso 1 — 150 elementos"`; decisión con `Cumplido ≥ 0,70` → `"Jev cree que ya está (0.94). No acciono más."` con punto 0,42; `Peligro ≥ 0,50` → `"«Grabar» no se deshace (peligro 0.80). Me detengo."`; confianza bajo umbral → `"No estoy seguro (0.31). Me detengo."`; una línea de progreso del tramo `"paso 3: «Detalles» (2) conf 0.91 · no cambió"` → ticker **idéntico** y punto 0,82; una línea `"tramo: 3 paso(s) · se agotó el tope de 4 paso(s)."` → ticker `"se agotó el tope de 4 paso(s)."` y punto 0,42; y ninguna cadena de `TextosDeJev` contiene «listo», «terminado», «hecho» ni «éxito» | escribir «Listo: ya está» en el caso cumplido |
| 375 | `CajasDelOverlay.De(candidatas, elegidaId)` con 5 candidatas de las que 2 vienen sin caja (`Caja == null`): devuelve **3** cajas en el orden de la lista, `SinCaja == 2`, cada una con `Etiqueta` sola (sin «2) » ni tipo); con dos «Buscar» y `elegidaId = "6) Buscar (Button)"` exactamente **una** `Elegida`; `Caducar()` deja la lista vacía; y una candidata con caja `EsLeida = false` (estimada) se pinta **sin** punto | resaltar por `Etiqueta.Contains(...)`, o pintar las de `Caja == null` con un rect vacío |
| 376 | `MedidaDelPanelDeJev.Ancho == 340`, `AltoDe(0) == 64`, `AltoDe(5) == 198.6` (±0,05) y `AltoDe(3) < AltoDe(5)` con paso 20 por barra; el alto se calcula de `PaddingArriba 10`, `PaddingAbajo 7.6`, filas 18/14/11/11/15 y huecos 8/8/(1+5)/5/5/5; `AnchoDeResultados == 314`. **Parte XAML** (fase 8): en un hilo STA del arnés, `XamlReader.Parse` del recurso del panel + `Measure/Arrange` da `64` sin resultados y `198.6` con cinco barras (±0,5), y el `StackPanel` `Resultados` recibe `314.0`; si WPF no arranca en el runner, la prueba dice **«NO PUDE JUZGARLA»** (aprendizaje nº17), no «roto» | cambiar `PaddingAbajo` a 10 (da 201), o poner el `BorderThickness` en el `Border` con hijos (recorta a 312,4) |
| 377 | `DondeVaElPanel.Calcular(ancla, tamaño, rcWork, escala, obstáculos, objetivo)` con `rcWork = (0,0,1920,1040)`, escala 1, panel 340×199 y ancla (600, 400): esquina superior izquierda en (656, 432) y `Esquina == AbajoDerecha`; con el ancla en (1800, 400) no cabe a la derecha → `AbajoIzquierda` y `Right ≤ 1908`; con el notch en `ReglaDeLaBandeja.ArribaAlCentro` como obstáculo y el ancla arriba al centro, el resultado no lo cruza; con `objetivo = (700, 450, 200, 40)` (inflado 8) el resultado no lo cruza si alguna esquina cabe; con un objetivo tan grande que ninguna cabe → la esquina de `rcWork` opuesta al objetivo; en todos los casos `rcWork.Contains(resultado)` y el cuadrado de 44 alrededor del ancla queda libre; con escala 1,5 los 56/32/44/12 salen multiplicados | devolver siempre la primera esquina sin probar las demás |
| 378 | `OrdenEnZ.Capas` es exactamente `[Overlays, Carita, Notch, PanelDeJev, Flecha]` de abajo arriba; `VigilanteEnOrden` construido con handles de mentira y un `subir(handle)` que anota: `Tick()` llama `subir` en ese orden y una vez por ventana visible; `AlMostrar(PanelDeJev)` provoca un reordenado completo en el acto; una ventana escondida no se sube; y el fuente de `Ui/Muelle.cs` y `Ui/PanelDeAcciones.cs` (leído con `U_REPO`, como hace la 341) no contiene `.Vigilar()` | invertir el orden de la lista, o dejar `this.Vigilar()` en `Muelle.cs` |
| 379 | `EstilosDeVentana.ExtendidosDelOverlay == 0x080800A0` (= `WS_EX_TRANSPARENT 0x20 | LAYERED 0x80000 | TOOLWINDOW 0x80 | NOACTIVATE 0x08000000`), `Afinidad == 0x11`; `EstilosDeVentana.UnaPorMonitor([(0,0,1920,1080), (1920,0,2560,1440)])` devuelve dos rects físicos iguales a los `rcMonitor`; `new MaquinaDeLaVista().OverlayEncendido == false`; `EncenderOverlay()` lo pone en `true` y `Estado` dice «overlay encendido»; `ApagarOverlay()` lo devuelve | quitar `NOACTIVATE` de la máscara, o arrancar con `OverlayEncendido = true` |
| 380 | `Pantallas.DelMonitor(rcMonitorFisico, escala)` da un conversor: `AUnidad((2000, 300))` con monitor en (1920, 0) y escala 1,5 → (53.33, 200); `AFisico(AUnidad(p)) == p` (±0,01) para 20 puntos; `ReglaDeDpi.RectTrasCambio(calculado, sugerido) == calculado`; y el fuente bajo `windows-client/src/Ui/Jev/` (leído con `U_REPO`) no contiene `TransformFromDevice` ni `DpiScaleX` | no restar el origen del monitor en `AUnidad` |
| 381 | `PlanDeVuelo.Calcular(inicio, centroElegido, escala, appDelante)`: con `appDelante = false` → `null`; con distancia 260 y escala 1 → `Duracion == 1.05 s` (el mínimo); con 1.500 → `2.2 s` (el máximo); con 780 → `1.5 s`; `Fin` dista `42·escala` del centro del elegido; `PosicionEn(0) == inicio`, `PosicionEn(1) == Fin`; la distancia al fin es **no creciente** en 100 muestras de p (no se devuelve); y la velocidad en p=0 y p=1 es 0 (`|PosicionEn(0.01) − PosicionEn(0)| < |PosicionEn(0.5) − PosicionEn(0.49)|`) | cambiar `Math.Clamp(d / 520, 1.05, 2.2)` por `d / 520` a secas |
| 382 | `ObservadorDelDecisor.Observar(interno, alDecidir)`: llamar al observado devuelve **el mismo objeto** `DecisionDeUnPaso` que devolvió el interno, `alDecidir` recibió un ciclo con `MsDecidir ≥ 0` y las mismas ids, y un interno que lanza `InvalidOperationException` la deja pasar tal cual (mismo tipo, mismo mensaje); `ConectorDeLaVista` con un despachador que ejecuta en otro hilo y un pintor que duerme 500 ms: `Publicar(ciclo)` vuelve en < 50 ms; 10 `Publicar` seguidos con el pintor bloqueado → al soltarlo el pintor recibe **1** ciclo, el último; un pintor que lanza deja en el log una línea con `InvalidOperationException` y el mensaje, y `Publicar` no lanza | sustituir `BeginInvoke` por `Invoke` en el conector, o pintar la cola entera |
| 383 | `MaquinaDeLaVista` con las tres visibles: `Apagar()` → `OverlayVisible`, `PanelVisible` y `FlechaVisible` en `false`; con corrida en marcha, `Suelta()` → overlay vacío (`CajasVisibles == 0`), `FlechaVisible == false`, `PanelEnCorrida == false` y el panel sigue visible; la promesa 290 sigue verde tal cual (apagar el interruptor no cambia por esta rama) | que `Apagar()` deje `PanelVisible = true` |
| 384 | `ReglaDeQuienVuela.LaCaritaViaja(enTramoConJev: true) == false` y `(false) == true`; la promesa 240 sigue verde (su fixture no está en tramo) y la 163 también (la carita no cambia de tamaño: esta rama no toca su ventana) | devolver siempre `true` |
| 385 | `EstilosDeVentana.ExtendidosDelPanel` incluye `WS_EX_TRANSPARENT` y `WS_EX_NOACTIVATE` sin condición de estado; `QueAbreElAtajo.Con(jevEncendido: true) == LoQueAbre.PanelDeJev` y `(false) == LoQueAbre.Globo`; `MaquinaDeLaVista.AlConocerElegido(cajaElegida)` recoloca el panel llamando a `DondeVaElPanel` con esa caja como `objetivo` (se comprueba que el rect nuevo no la cruza) | que el panel en reposo pierda `WS_EX_TRANSPARENT`, o que `Con(true)` devuelva `Globo` |

## Diseño

### Las piezas, y dónde viven

Todo lo nuevo va bajo **`windows-client/src/Ui/Jev/`** (espacio `U.WindowsClient.Ui.Jev`), salvo el parcial de
`FaceWindow` y los retoques a piezas que ya existen.

| Pieza | Qué es | ¿Pura? | Promesas |
|---|---|---|---|
| `PaletaDeJev` | los tokens del plano en `uint` ARGB, con significado, `DistanciaDeTono`, `ChoquesAceptados` | sí | 371 |
| `ExclusionConElInspector` | dos banderas y `PuedeEncender(cual)`: el overlay de Jev y `UiInspector` nunca a la vez | sí | 371 |
| `CicloDeJev` | el registro de entrada: `Objetivo`, `Paso`, `Candidatas(Id, Etiqueta, Tipo, Caja?, EsLeida)`, `Decision?(Actuar, Puerta, Confianza, Alternativas, Cumplido?, Ausente?, Peligro, Porque)`, `MsDecidir`, `TokensFacturados?`, `Fase`, `Linea` (progreso del tramo, tal cual) | datos | 372–375 |
| `EstadoDeLaDecision` | `De(ciclo, coste) → LoQuePinta(Objetivo, Ticker, Punto, Resultados?(Cabecera, Medidores, Barras))` | sí | 372, 373, 374 |
| `TextosDeJev` | las cadenas, una por estado, con formato invariante | sí | 374 |
| `CosteDeJev` | `PrecioPorMillon = 0.042`, `Acumular(tokens?)`, `Texto` («$0.00004» / «—») | sí | 373 |
| `CajasDelOverlay` | `De(candidatas, elegidaId) → cajas`, `SinCaja`, `Caducar()` | sí | 375 |
| `MedidaDelPanelDeJev` | constantes y `AltoDe(barras)` calculado de las partes | sí | 376 |
| `DondeVaElPanel` | `Calcular(ancla, tamaño, rcWork, escala, obstáculos, objetivo?) → (Rect, Esquina)` | sí | 377, 385 |
| `OrdenEnZ` + `VigilanteEnOrden` | la lista de capas y el vigilante de grupo con `subir` inyectable | sí | 378 |
| `EstilosDeVentana` | las máscaras `GWL_EXSTYLE`, la afinidad, `UnaPorMonitor` | sí | 379, 385 |
| `ReglaDeDpi` | `RectTrasCambio(calculado, sugerido)` | sí | 380 |
| `PlanDeVuelo` | la curva del vuelo en físicos, con `s_fin` | sí | 381 |
| `ObservadorDelDecisor` | decora el `Decisor` del mapa: cronometra, arma el ciclo, devuelve la decisión intacta | sí | 382 |
| `ConectorDeLaVista` | `Publicar(ciclo)` → `BeginInvoke` con *coalescing*; catch que cuenta | sí (despachador inyectable) | 382 |
| `MaquinaDeLaVista` | el estado: encendida, overlay encendido (por defecto no), en tramo, visibles, `Apagar`, `Suelta`, `AlConocerElegido` | sí | 379, 383, 385 |
| `ReglaDeQuienVuela`, `QueAbreElAtajo` | dos reglas de una línea, para que el `if` de `FaceWindow` no sea código suelto | sí | 384, 385 |
| `OverlayDeJev`, `PanelDeJev`, `FlechaDeJev` | las tres `Window` | no: solo dibujan lo que el modelo devuelve | 376 (XAML), 379, 380, 381 |
| `VistaDeJev` | el coordinador: crea las ventanas, sostiene conector y máquina, se sincroniza con el interruptor | no | — |
| `Ui/FaceWindow.Jev.cs` | parcial, como `FaceWindow.Escritorio.cs`: todo el cableado que cabe fuera del archivo grande | no | 9 |
| `Ui/Pantallas.cs` | añade la **inversa** por monitor (`DelMonitor`, `AUnidad`, `AFisico`) | sí | 380 |
| `Ui/SiempreDelante.cs` | añade `VigilarEnOrden(params Window[])` sobre `VigilanteEnOrden` | no | 378 |
| `Ui/Muelle.cs:175`, `Ui/PanelDeAcciones.cs:247` | dejan de llamar `this.Vigilar()`; entran al grupo | — | 378 |
| `Uia/UiInspector.cs:59-63` | `Toggle` pregunta a `ExclusionConElInspector` | — | 371 |

### El ciclo, visto desde la vista

```
AlEmpezarTramo("tramo: <objetivo>")  → panel: Objetivo, ticker «Mirando la pantalla», punto 0,82, Resultados como estaban
Decisor(pantalla, objetivo, ids)     → ObservadorDelDecisor: cronómetro → decisión intacta → ciclo «decidido»
                                       → ConectorDeLaVista.Publicar (BeginInvoke, coalescing)
                                       → EstadoDeLaDecision.De → panel: cabecera · medidores · 5 barras · elegida
                                       → CajasDelOverlay.De   → overlay: cajas, rosa por id (solo con caja leída)
                                       → PlanDeVuelo.Calcular → flecha: vuela (si la app está delante) MIENTRAS la mano ya pulsó
                                       → MaquinaDeLaVista.AlConocerElegido → el panel se aparta si tapa la caja
Progreso("paso k: «x» (n) conf … · cambió")  → ticker: la línea tal cual, punto 0,82
Progreso("tramo: N paso(s) · <motivo>")      → ticker: el motivo tal cual, punto 0,42, ninguna barra resaltada
AlTerminarTramo()                    → flecha vuelve/se esconde; overlay se queda hasta que cambie la ventana de delante
Escape / Senalador.Suelta            → overlay vacío, flecha escondida, panel sin corrida (383)
Apagar Jev (PintarBotonJev off)      → las tres ventanas cerradas (383)
```

Tres reglas del plano que este diseño respeta y hay que poder señalar en el código:

- **Lo que se pinta es exactamente lo que se ofreció** (285, aprendizaje nº16): las candidatas del ciclo son las ids
  que `UnPasoDecidido` numera (`SurfaceMapTools.cs:266-274`), y la elegida se compara por **`string.Equals(Ordinal)`
  con la id**, por el mismo camino que `ElDecisor.cs:203-205`.
- **El overlay caduca al cambiar la ventana de delante** (`SetWinEventHook(EVENT_SYSTEM_FOREGROUND)`), al revés que
  TipTour (t=73, cajas de la terminal sobre Music).
- **Las cajas quedan debajo del panel**, al revés que TipTour y a propósito: el panel es opaco y lo que tapa no se pulsa.
  Si el dueño quiere el calco, es mover un elemento en `OrdenEnZ.Capas`.

### El puente provisional hasta que C entre

`AlDecidir` (368) y la candidata con caja (363) son de C. Mientras no estén en `main`, `FaceWindow.Jev.cs` decora el
`Decisor` del mapa con `ObservadorDelDecisor` **desde fuera** (`Mcp/SurfaceMapTools.cs:1722` es una propiedad
pública; ni `Decision/` ni `Navigation/` ni `SurfaceMapTools` se tocan). Lo que ese puente ve: las ids numeradas,
la decisión y los ms de decidir. Lo que **no** ve: las cajas ni los tokens facturados. Consecuencias, dichas en el
panel y no escondidas:

- el overlay no pinta ninguna caja y el panel dice «· N detectados (N sin caja)»;
- el coste dice «—» hasta que la decisión traiga `input_tokens` (349 de A, 367 de C).

Cuando C entre, el parcial se suscribe a `AlDecidir` en lugar de decorar: **una línea** cambia, y las promesas
375 y 373 pasan a verse con datos reales en el nivel 4. Es un puente y no un atajo: el modelo puro es el mismo.

### Lo que pinta cada ventana, con sus números

**`OverlayDeJev`** — una por monitor, `rcMonitor` en físicos por `SetWindowPos`, `EstilosDeVentana.ExtendidosDelOverlay`,
`WDA_EXCLUDEFROMCAPTURE` con línea de log si falla (como `AuraDeAprendizaje.cs:161-162`). Cuatro capas
`DrawingVisual` (plano §Cómo se pinta): fijo, pulso (una sola `DoubleAnimation` 0→1 de 1,428 s `AutoReverse`
`SineEase EaseInOut`), etiqueta, cajas. Una caja: radio 6, trazo 0,9 a `Candidata`, halos +3,8 y +1,6, relleno 0,018,
corchetes `clamp(0,28·min(w,h), 8, 18)`; etiqueta si ≥ 42×12, Consolas 8,3 `Normal`, cápsula `FondoDeEtiqueta` de alto
14,5 con ancho `min(FormattedText + 14, max(58, anchoPantalla − x1 − 8))`; punto de 3,0 solo si la geometría es
**leída** (aprendizaje nº4). La elegida: `Elegida` trazo 2,0, relleno 0,08, etiqueta Consolas 10,5 `Bold` blanca sobre
`FondoDeEtiquetaElegida`, sin punto. Texto con `TextRenderingMode=Grayscale` y `TextFormattingMode=Display`.

**`PanelDeJev`** — 340 de ancho, `SizeToContent=Height`, fondo opaco `FondoDelPanel` (desviación declarada frente al
0,96 del vídeo: `Nitida()` exige opaco, `Estudio.cs:431`), trazo 0,8 en un `Border` superpuesto sin hijos, **sin
sombra**. Filas del XAML del plano: entrada 18 (glifo ⌘ en `Segoe UI Symbol` 11 sin peso, objetivo en Segoe UI
Variable Text 13), ticker 14 (punto 4×4 a 0,82/0,42, texto 11), divisor 1, cabecera 11 (Consolas 10 `Normal`,
`BlockLineHeight=11`), medidores 11 (título Segoe 9 `Normal`, pista 64×4 `Pista` con relleno `max(2, 64·v)`, valor
Consolas 10), cinco barras de 15 con paso 20 (etiqueta en 118, pista 150×5, relleno `max(2, 150·p)`, `SemiBold` +
`TextoPrimario` solo en la elegida). Padding 13/10/13/7,6. Cambios de texto, barras y alto con 0,16 s *easeInOut*.
Números con `InvariantCulture`; ms con `"N0"`; `$` con `"0.00000"`. Siempre `ExtendidosDelPanel` (click-through,
no activable): **no tiene campo de texto** (ver *Lo que NO entra*), así que nunca necesita el foco.

**`FlechaDeJev`** — ventana propia de 128 de alto y hasta 544 de ancho (píldora `MaxWidth=240` con elipsis), centrada
en la flecha y movida con `SetWindowPos` cada fotograma con `SWP_NOZORDER`… salvo que va arriba del todo y puede
pasar `HWND_TOPMOST`. Silueta Lucide `mouse-pointer-2` en caja de 24 escalada con `Geometry.Transform` (44/24), trazo
4,2 `AzulDelCursor`, relleno blanco, dos halos `BlurEffect` 16 y 6 (equivalencia con SwiftUI **sin medir**); píldora
`CornerRadius 6`, `Padding 8,4`, Segoe 11 `Normal` blanca. Modos: siguiendo (ratón + (35, 25)·s, solo en reposo con
Jev encendido), volando (`PlanDeVuelo`), señalando (3 s y vuelve; escala 1,035 cada 1,4 s). **El clic no espera al
vuelo**: `PlanDeVuelo` se lanza desde el pintor y la mano ya salió.

### Orden en Z y DPI

`SiempreDelante.VigilarEnOrden(overlays…, muelle, notch, panel, flecha)`: un reloj de 3 s que en cada tick sube en
ese orden con `SetWindowPos(HWND_TOPMOST, SWP_NOMOVE|NOSIZE|NOACTIVATE)`, y reordena en el acto tras cada `Show()`
del grupo. `Muelle.cs:175` y `PanelDeAcciones.cs:247` dejan de llamar `Vigilar()` (**2 sitios**, patrón nº5). El
grupo se arma al arrancar aunque Jev esté apagado —la carita y el notch siguen necesitando vigilancia— y las tres de
Jev entran cuando nacen.

Posiciones en **físicos** del escritorio virtual, longitudes de diseño en DIP × la escala del monitor que manda, todo
por `SetWindowPos`, márgenes contra `rcWork`; `OnDpiChanged` reaplica el rect calculado (`ReglaDeDpi`). Las 7
conversiones a mano de hoy **no se tocan** en esta rama (no son suyas) y quedan contadas como deuda; bajo `Ui/Jev/`
no nace ninguna (380).

### El cableado en `FaceWindow.xaml.cs`: contado

Es la zona de choque alta del repo; el parcial `FaceWindow.Jev.cs` se lleva todo lo que puede. Lo que queda en el
archivo grande, y va contado en el commit de la fase 9 (**≤ 9 líneas**):

| Dónde (líneas de `f811796`) | Qué | Líneas |
|---|---|---|
| junto a `:457` (nace el interruptor) | `_vistaDeJev = new VistaDeJev(mcp.Map, …)` | +1 |
| `:462` y `:463` (`AlEmpezarTramo`/`AlTerminarTramo`) | además de `Freno`, avisan a la vista (objetivo y fin de tramo) | 2 editadas |
| `:464-469` (`Progreso`) | la misma línea que va al notch va al ticker, tal cual | +1 |
| `:1883` (`OnAutomationCursorMoved`) y `:4667` (`IrJuntoA` con `alClic`) | `if (!ReglaDeQuienVuela.LaCaritaViaja(_vistaDeJev?.EnTramo == true)) return;` | +2 |
| `:1589` (`InvocarPorAtajo`) | `if (QueAbreElAtajo.Con(jevEncendido) == PanelDeJev) { _vistaDeJev.AbrirPanel(); return; }` | +1 |
| `:2895` (`PintarBotonJev`) | `_vistaDeJev?.Sincronizar(on)` (decora/desdecora el `Decisor`, abre/cierra las tres ventanas, arma el grupo en Z la primera vez) | +1 |

Cruces con las otras ramas en este archivo: B toca `768-798`, C `434, 508-565, 5570-5684`; ninguna de las líneas de
arriba está a menos de 10 de esas. D entra después de C y rebasa.

## Las fases

Una fase = un commit que pone verde sus promesas sin romper las anteriores. Antes de la fase 1 va la etapa
`/promesas`: las 15 en `Contrato.cs`, todas `PENDIENTE`, y el veredicto literal «CONTRATO ROTO: 15 promesa(s)
incumplida(s)» pegado en el commit `test(jev-vista): …`.

### Fase 0 — el arnés mide XAML sin pantalla

| | |
|---|---|
| **Promesa que pone verde** | ninguna: habilita la parte XAML de la 376 |
| **Qué toca** | `tests/ContratoDelGrafo/Contrato.cs` (ayudante `EnSta(Action)` + `MedirXaml(string)`) |
| **¿Núcleo congelado?** | no |
| **Terminado** | un `TextBlock` de Consolas 10 medido en el arnés da alto 11,71 (±0,1), que es lo que midió `medir-panel.ps1`; si WPF no arranca en el runner, el ayudante devuelve «NO PUDE JUZGARLA» y la prueba no cuenta como rota (aprendizaje nº17) |
| **Sitios con esta clase de error** | 0: hoy el contrato no mide ningún XAML (grep `XamlReader|\.Measure\(` vacío) |

### Fase 1 — la paleta, y el inspector que no se enciende encima

| | |
|---|---|
| **Promesa que pone verde** | 371 |
| **Qué toca** | `Ui/Jev/PaletaDeJev.cs`, `Ui/Jev/ExclusionConElInspector.cs` (nuevos); `Uia/UiInspector.cs:59-63` |
| **¿Núcleo congelado?** | no |
| **Terminado** | 371 verde; 242 (el notch blanco y negro) intacta; sabotaje del verde verificado por diff |
| **Sitios con esta clase de error** | choques de color escritos: 3 (`Ausente`≈`Atencion`, `Candidata`≈shell mapeado/`Vivo`, `Elegida`≈`Fallo`) + 1 aceptado (los dos azules) |

### Fase 2 — el modelo del panel

| | |
|---|---|
| **Promesa que pone verde** | 372, 373, 374 |
| **Qué toca** | `Ui/Jev/CicloDeJev.cs`, `EstadoDeLaDecision.cs`, `TextosDeJev.cs`, `CosteDeJev.cs` (nuevos) |
| **¿Núcleo congelado?** | no |
| **Terminado** | 372–374 verdes; 3 sabotajes por diff; ninguna cadena concluye |
| **Sitios con esta clase de error** | textos que hoy concluyen por el modelo: 1 (`ElTramo.cs:157-158`, «el objetivo ya está cumplido», que A reescribe en la 348; la vista no lo repite) |

### Fase 3 — el modelo del overlay

| | |
|---|---|
| **Promesa que pone verde** | 375 |
| **Qué toca** | `Ui/Jev/CajasDelOverlay.cs` (nuevo) |
| **¿Núcleo congelado?** | no |
| **Terminado** | 375 verde; 285 intacta (la lista es la misma) |
| **Sitios con esta clase de error** | resaltados por subcadena en el repo: 1 (`SurfaceMapTools.cs:299`, `Porque.Contains("cumplido")`, que A quita); en la vista, 0 |

### Fase 4 — la geometría pura

| | |
|---|---|
| **Promesa que pone verde** | 376 (parte pura), 377, 380 |
| **Qué toca** | `Ui/Jev/MedidaDelPanelDeJev.cs`, `DondeVaElPanel.cs`, `ReglaDeDpi.cs` (nuevos); `Ui/Pantallas.cs` (inversa por monitor) |
| **¿Núcleo congelado?** | no |
| **Terminado** | las tres verdes; 249/251/260 (medida y sitio del notch) intactas; `DondeVaElPanel` recibe el notch de `ReglaDeLaBandeja.ArribaAlCentro` como obstáculo |
| **Sitios con esta clase de error** | conversiones DPI a mano fuera de `Pantallas`: 7 (contadas arriba); esta fase añade 0 y quita 0 (no son suyas) |

### Fase 5 — el vuelo

| | |
|---|---|
| **Promesa que pone verde** | 381 |
| **Qué toca** | `Ui/Jev/PlanDeVuelo.cs` (nuevo) |
| **¿Núcleo congelado?** | no |
| **Terminado** | 381 verde; 240 intacta (la curva de la carita no cambia) |
| **Sitios con esta clase de error** | esperas fijas por decoración: 0 en `main` desde #83 (anatomía §2); la flecha no reintroduce ninguna |

### Fase 6 — el conector, la máquina y las reglas de una línea

| | |
|---|---|
| **Promesa que pone verde** | 379, 382, 383, 384 (regla), 385 (regla) |
| **Qué toca** | `Ui/Jev/ObservadorDelDecisor.cs`, `ConectorDeLaVista.cs`, `MaquinaDeLaVista.cs`, `EstilosDeVentana.cs`, `ReglaDeQuienVuela.cs`, `QueAbreElAtajo.cs` (nuevos) |
| **¿Núcleo congelado?** | no |
| **Terminado** | las cinco verdes; 290 intacta; el catch del pintor cuenta tipo y mensaje (patrón nº3) |
| **Sitios con esta clase de error** | `Dispatcher.Invoke` síncronos en la costura del tramo: 0 hoy (`FaceWindow.xaml.cs:464` ya usa `BeginInvoke`); la vista no añade ninguno |

### Fase 7 — un solo vigilante

| | |
|---|---|
| **Promesa que pone verde** | 378 |
| **Qué toca** | `Ui/Jev/OrdenEnZ.cs` (nuevo); `Ui/SiempreDelante.cs` (`VigilarEnOrden`); `Ui/Muelle.cs:175`; `Ui/PanelDeAcciones.cs:247` |
| **¿Núcleo congelado?** | no |
| **Terminado** | 378 verde; grep `\.Vigilar\(\)` da 0 fuera de `SiempreDelante.cs` |
| **Sitios con esta clase de error** | 2 (`Muelle.cs:175`, `PanelDeAcciones.cs:247`) |

### Fase 8 — las tres ventanas

| | |
|---|---|
| **Promesa que pone verde** | 376 (parte XAML) |
| **Qué toca** | `Ui/Jev/OverlayDeJev.cs`, `PanelDeJev.cs` (+ su XAML como recurso), `FlechaDeJev.cs`, `VistaDeJev.cs` (nuevos) |
| **¿Núcleo congelado?** | no |
| **Terminado** | compila en Release; el XAML real mide 64/198,6 en el arnés; contrato intacto; las ventanas solo llaman al modelo (grep: ni `OrderBy` ni `Contains(` sobre etiquetas dentro de las tres ventanas) |
| **Sitios con esta clase de error** | ventanas de Ü que cubren solo la primaria: 2 (`InspectorOverlay.cs:86-89`, `AuraDeAprendizaje.cs:122-124`); `OverlayDeJev` no repite el patrón |

### Fase 9 — el cableado mínimo, y el nivel 4

| | |
|---|---|
| **Promesa que pone verde** | 384 y 385 (cableado); 383 se ve en el PC |
| **Qué toca** | `Ui/FaceWindow.Jev.cs` (nuevo, parcial); `Ui/FaceWindow.xaml.cs` (**≤ 9 líneas**, contadas en el commit) |
| **¿Núcleo congelado?** | no (la UI no está congelada, pero es zona de choque alta: aviso en `#miracle-updates` al abrir) |
| **Terminado** | contrato intacto (15/15); sabotajes por diff de las 15; nivel 4 sobre ≥ 2 pantallas con el log pegado |
| **Sitios con esta clase de error** | ganchos que tocan el archivo grande cuando cabían en un parcial: 0 después de esta fase (todo lo de Jev vive en el parcial salvo las ≤ 9 líneas) |

## Lo que NO entra

- **El campo de texto del panel y la receta del foco** (activar, comprobar `IsActive` del panel, devolver el primer
  plano, `WS_EX_TRANSPARENT` al pulsar Enter). En Ü el objetivo llega por la voz (`map_tramo`); escribirlo en el panel
  obligaría a activar una ventana nuestra y a arrancar un tramo desde la UI, que pasa por `Mcp/`. Es otra spec: el
  panel de esta se abre por el atajo y **muestra**, sin tomar nunca el ratón ni el foco (385). Hallazgo de paso, para
  quien la escriba: `DevolverElFoco` (`FaceWindow.xaml.cs:1618`) tira el `bool` de `SetForegroundWindow` dentro de un
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

## Nivel 4 pendiente

Se hace en el PC real, con Jev **apagado o en `U_DECISOR=simulado`** fuera de SAP hasta que A entre (mientras no
entre, `U_DECISOR=jev` sobre `sapgui://` manda filas con nombre y documento). Con simulado no hay barras (373): las
barras se ven solo con un transporte falso que devuelva una distribución, o con Jev sobre Explorador/Chrome.

| Qué | Cómo | Dónde |
|---|---|---|
| El panel y el overlay con datos reales | un tramo con transporte falso (`U_DECISOR=jev` + `TYPESAFE_API_KEY` de mentira y un `Transporte` inyectado que contesta una distribución fija: **no se llama a TypeSafe**) sobre Explorador «Descargas» y Chrome; foto del panel a 3,04 px/DIP junto a `recortes/revision/full_16.png` | 2 pantallas con nombre |
| El orden en Z | línea `orden Z: flecha 0 · panel 1 · notch 2 · carita 3 · overlays 4…` por `EnumWindows` a los 10 s, 60 s y tras esconder y enseñar el overlay | log |
| El overlay fuera de la captura | una foto con `Screenshotter` con el overlay encendido: sin cajas en el PNG | log + PNG |
| La carita quieta en tramo | 0 líneas `viaje al clic` (`FaceWindow.xaml.cs:1801`) durante el tramo; ≥ 1 en un `map_take` fuera de tramo | log |
| Escape y apagar | Escape a mitad de tramo: overlay vacío y flecha escondida en el acto; botón «Jev · off»: las tres ventanas cerradas | log + a ojo |
| CPU de la ventana en capas | 60 s con el pulso animado y el ratón quieto frente a 60 s sin overlay, contador de CPU del proceso | tabla en el PR |
| **Dos escalas de DPI** | **no se puede aquí: un solo monitor.** Se dice en el PR y queda para una máquina con dos | — |
| El vuelo en la costura entre monitores | ídem | — |

## Hallazgos

- **2026-09-22.** `origin/main` (`f811796`, Felipe) añadió la promesa **341** al contrato del grafo («dos recordatorios
  vencidos despiertan una sola sesión de voz»). La arquitectura reservaba 341–350 para la rama A: **A tiene que
  desplazar sus números**; 371–385 siguen libres en `Contrato.cs` y en las 44 refs `origin/*`, y `049-*` no existe en
  ninguna. Los números no se reciclan.
- **2026-09-22.** Las ramas `origin/jose/el-notch-no-respira` y `origin/jose/notch-y-barra` muestran contra `main`
  exactamente los archivos que `main` ya tiene (`MedidaDelNotch.cs`, `SiempreDelante.cs`…): entraron por squash y no
  hay trabajo abierto allí que choque con `Muelle.cs:175` ni `PanelDeAcciones.cs:247`.
- **2026-09-22.** Conversiones DPI a mano fuera de `Pantallas`: **7**, no 6 (la arquitectura no contaba
  `FaceWindow.Escritorio.cs:111`).
- **2026-09-22.** `Freno.Tarea` no se limpia en `Termine()` (`Actions/Freno.cs:93`): no sirve como «hay tramo en
  marcha». La vista lleva su propio `EnTramo` alimentado por `AlEmpezarTramo`/`AlTerminarTramo`.
- **2026-09-22.** Nadie lee `usage.input_tokens` en `main`: hasta A/C el `$` del panel dirá «—» sobre datos reales.
  No es un fallo de la vista: es la regla de la 373 funcionando.

## Cierre

- [ ] Todas las promesas verdes (`.\scripts\contrato-del-grafo.ps1` → CONTRATO INTACTO)
- [ ] Un sabotaje por promesa verificado por diff (copia, rompe, diff, compila sin silenciar, ROTO nombrando esa promesa, restaura, diff idéntico, INTACTO)
- [ ] `.\scripts\verificar.ps1` pasa, con evidencia en `out\evidencia.md`
- [ ] Probado en ≥ 2 pantallas, con nombre: …
- [ ] Dos escalas de DPI: **sin probar (un monitor)**, dicho en el PR
- [ ] Estado de este documento: **implementado** (AAAA-MM-DD)
