# Lo leído tampoco sale por otro camino

Estado: **en curso** · Nace de la revisión de fugas del 2026-09-24 sobre la rama de pruebas
`jero/jev-todo-junto` (main `043addc` + A #118 + B #119 + C #117 + D #120 + la 051 #121) · Rama: la
misma, antes de que el dueño empiece a probar.

La 051 dejó escrito al cerrar (§*Lo que queda fuera*) que lo leído de la pantalla —clase L: filas,
títulos de ventana, etiquetas del dynpro— «pide su propia spec (propuesta: 052, «lo leído tampoco
sube»), que empieza por S5». Esta es esa spec, y no empieza solo por S5: la revisión encontró además
dos caminos por los que lo que la **política 393** le niega a Jev sale igual.

## Diagnóstico: qué se midió

Todo **(M, lectura)** sobre `e58190b`, contado con `grep` y leyendo cada sitio; nada de esto se midió
en el PC real (no hay SAP de pruebas en esta sesión) y se dice.

| # | Qué sale | Por dónde | Sitio |
|---|---|---|---|
| 1 | Las filas de SAP en crudo —nombre y documento de cada `GuiGridFila`, `GuiTreeFila`, `GuiTreeCarpeta`— justo cuando la 393 acaba de negarse a mandar texto de SAP a Jev | `map_decidir` en `sapgui://` sin `U_DECISOR_SAP_TEXTO=si` → `ConJev` devuelve `No(«…Decide Luna.»)` → `DecidirYPulsar` contesta «no se acciona: …» → `map_decidir` está en `ComoSeContesta.Actos`, así que el despacho pega `LoQueVeo()`, que escribe cada puerta como `«{etiqueta}» ({tipo})` sin pasar por `NombreParaContar` → la respuesta vuelve a GPT-Live como `function_call_output`. Y la línea «mapa-mcp ←» del log local guarda sus primeros 200 caracteres | `SurfaceMapTools.cs:613` (`LoQueVeo`), `:2794` (el pegado) |
| 2 | Lo mismo, dentro de la cuenta final del tramo | `map_tramo` en SAP sin habilitar: el primer paso sale vetado por la política y el tramo para; la cuenta lleva `_manos.Inventario()` = `LoQueVeo()` detrás, se guarda para `map_tramo_estado` y va entera a la voz por `AvisarALaVoz` (`FaceWindow.xaml.cs:475`) | `ElTramo.cs:234-238`, `SurfaceMapTools.cs:2385` |
| 3 | La ubicación entera de la pantalla en el evento `analyze`: en `uia://` el título de la ventana, en `web://` la ruta. Incluye SAP visto por UIA (`uia://saplogon.exe/<título>`) y cualquier documento o búsqueda cuyo nombre tecleó la persona | `TelemetryBus.Emit("analyze", …, surfaceUrl: loc?.Id ?? "")` en cada turno del consciente → `POST /agent/events`. La 051 lo congeló «sin cambio» en su censo S5 y lo dejó para esta spec | `AgentLoop.cs:239` |
| 4 | Texto de SAP hacia Jev **con la 393 puesta**: los nombres que UIA ve en la ventana de SAP y las etiquetas del dynpro, bajo una ubicación que no es SAP | `PuertasDeAhora` juzga el dónde **una vez** (`aqui = _where()?.Id`) y lee el contenido de otras dos fuentes que eligen su ventana por su cuenta: la lectura UIA usa `VentanaQueLeeria` (`AppAligner.VentanaDelUsuario`) en el momento de leer y etiqueta la observación con el `aqui` viejo; `CamposDeSap` vuelve a preguntar `DondeEstoy()`. Si SAP pasa al frente entre juzgar y leer, o si SAP está Busy y `SurfaceLocator.Compute` sostiene `Current` —la pantalla anterior, que no es SAP— con el hwnd de SAP (`ConVentana`), `Decisor(aqui, …)` recibe un origin permitido con puertas leídas de SAP y `ConJev` lo manda | `SurfaceMapTools.cs:332, :343`; `FaceWindow.xaml.cs:958`; `SurfaceLocator.cs:322` |

La 046 (fase 10, (a)) ya nombraba el sitio de `CamposDeSap` como abierto y «sin juez posible sin leer
UIA de verdad». Desde la 048 el lector sí se inyecta (`Lee`, `VentanaQueLeeria`), y el juez ya es
posible. El sitio de la lectura UIA (`:332`) no aparecía en ninguna parte.

## Por qué esto va dirigido por especificación

Los cuatro son la misma forma de fallo: **una política que se cree puesta** (aprendizaje nº18). La
393 decide **quién** decide, y su juez la mira en el único sitio donde se aplica —`ConJev`, antes del
transporte—; nadie miraba qué pasaba con el mismo texto un paso después (la respuesta a Luna, la
cuenta a la voz) ni un paso antes (de qué ventana salía lo que se le ofrecía). Una prueba escrita
después del código se escribiría sobre el sitio arreglado y dejaría fuera los de al lado.

**La regla del flujo, y no tiene excepciones:** ninguna línea de producción entra antes que la
promesa que la juzga.

## La especificación

| # | Promesa | Fase |
|---|---|---|
| **402** | lo que la política no deja viajar a Jev tampoco vuelve en crudo con su respuesta: cuando el decisor no decide porque la política no deja salir el texto de la pantalla —SAP sin U_DECISOR_SAP_TEXTO=si—, el inventario que vuelve pegado a map_decidir y el que lleva la cuenta del tramo —la que llega a la voz y la de map_tramo_estado— nombran cada fila por número y tipo, «fila N (GuiGridFila)», nunca por su texto, y tampoco la línea de su respuesta en el log; el control sigue volviendo con el inventario, y lo que no es una fila se lista igual | 1 |
| **403** | lo que se le ofrece al decisor es de la ventana y del dónde que se juzgaron: si la ventana que se leyó no es la de la ubicación juzgada, o es de SAP y la ubicación juzgada no lo es —SAP en tránsito sostiene la ubicación anterior—, map_decidir no decide: el transporte no se toca, no se pulsa nada y dice cuál de las dos no cuadró; con la misma ventana y el mismo mundo decide como siempre; y los campos del dynpro se leen para la ubicación ya juzgada, que se les pasa, sin volver a preguntar dónde se está | 2 |
| 395 (S5) | la fila S5 del censo de la 051 cambia de `surfaceUrl: loc?.Id ?? ""` a `surfaceUrl: PoliticaDeLoQueViaja.UbicacionQueViaja(loc?.Id ?? "")`: de la ubicación sube solo el origin, con el mismo recorte que ya se le aplica a Jev (393), en un solo sitio (aprendizaje nº16). El enunciado de la 395 no cambia; cambia su dato | 3 |

### Decisiones, y la que no es mía

1. **402 es el mínimo, no la salida técnica entera.** La revisión propuso dos: (a) que `LoQueVeo`
   nombre **siempre** cada fila por `NombreParaContar` y que `map_take` acepte «fila N»; (b) el
   mínimo, no pegar filas en crudo cuando la decisión trae el veto de la política. La (a) cambia lo
   que prometen la 183 («la fila del paciente deja de ser una puerta invisible»), la 263, la 286 y la
   390, y con ella Luna dejaría de poder elegir un paciente por su nombre en `map_what_i_see`: **eso
   lo decide el dueño**, y esta spec no lo decide por él. Entra la (b).
2. **La señal es un dato de la decisión, no la prosa del porqué** (aprendizaje nº2, spec 048).
   `DecisionDeUnPaso.LaPoliticaNoDejoViajar` la pone `ConJev` en la única rama que vuelve por la
   política; el paso del tramo la lleva (`ElTramo.Paso.LaPoliticaNoDejoViajar`), y quien pega el
   inventario —el despacho para `map_decidir`, las manos del tramo para su cuenta— la lee de ahí. Sin
   estado compartido entre llamadas: el tramo corre en su tarea y Luna puede llamar mientras tanto.
3. **Las filas se nombran por el mismo camino que para Jev**: `NombreParaContar($"{N}) {etiqueta} ({tipo})",
   etiqueta)`, con N la posición en la misma lista que numera `DecidirYPulsar` (285). «fila 2» en el
   relato y «fila 2» en el inventario son la misma puerta.
4. **403 tiene dos reglas, y el mensaje distingue cuál mordió** (patrón nº2): (i) la ventana leída no
   es la de la ubicación —se compara solo cuando las dos se conocen: un hwnd 0 es «no vino», no «otra»
   (patrón nº9)—; (ii) la ventana leída es de SAP y la ubicación no —el proceso se reconoce por el mismo
   criterio que la política (`Mundos.EsSesionDeSap`, que usa `SurfaceLocator.IsSap`)—. La (ii) es la
   que caza el Busy: ahí `ConVentana` pone el hwnd de SAP sobre la ubicación sostenida, así que la (i)
   sola no lo vería. **No se arregla `SurfaceLocator`**: el hwnd de una ubicación lo usa también la
   ventana de trabajo (233), y cambiarlo desde aquí sería cambiar otra promesa sin su juez.
5. **403 frena al decisor, no a `map_what_i_see`.** Lo que se lista a Luna con la etiqueta de otra
   ventana es una caja que miente (patrón nº8), pero no una fuga: queda anotado abajo.
6. **`CamposDeSap` recibe el dónde**: pasa de `Func<IReadOnlyList<DetectedField>>` a
   `Func<string, IReadOnlyList<DetectedField>>`. Sus tres llamadores ya tenían el dónde a mano.

### Con qué se juzga cada una

- **402**: mapa a mano con ubicación `sapgui://`, puertas inyectadas (un botón y las tres clases de
  fila) y el decisor de verdad (`ElDecisor.ElegirConModelo` con la política leída de un entorno vacío
  y un transporte que cuenta). Sin `InventarioParaLosActos`: se juzga el `LoQueVeo` de verdad.
- **403**: el lector inyectado de la 048 (`Lee`, `VentanaQueLeeria`), la ubicación con su `Hwnd`, y
  una costura nueva, `ProcesoDeLaVentana` (null = `AppAligner.ProcesoDe`). Y por fuente
  (`FuenteDelRepo`), que la lambda de `CamposDeSap` en `FaceWindow` no pregunta `DondeEstoy`.
- **395 (S5)**: el censo, como toda la 395. El sabotaje es volver a `loc?.Id ?? ""`.

## Las fases

| Fase | Promesa | Toca | Sitios con la clase de error | Sabotaje |
|---|---|---|---|---|
| 1 | 402 | `Decision/ElDecisor.cs` (la señal, en la factoría y en las 4 copias), `Navigation/ElTramo.cs` (el paso la lleva), `Mcp/SurfaceMapTools.cs` (`LoQueVeo(filasSinTexto)`, `Decidir`, el despacho, las manos del tramo) | inventarios que pueden salir tras el veto de la política: **2** (el pegado del despacho para `map_decidir` y la cuenta del tramo); `map_what_i_see` es el tercer lector de `LoQueVeo` y queda fuera por la decisión 1 | `LoQueVeo` ignora `filasSinTexto` |
| 2 | 403 | `Mcp/SurfaceMapTools.cs` (`PuertasDeAhora` devuelve por qué lo leído no es de aquí; `DecidirYPulsar` no decide), `Ui/FaceWindow.xaml.cs` (la lambda de `CamposDeSap`) | lecturas de contenido que eligen su ventana o su dónde por su cuenta: **2** (`VistaReciente`, `:332`, y `CamposDeSap`, `FaceWindow.xaml.cs:958`); llamadas a `CamposDeSap`: **3** (`:270`, `:1350`, `:1829`) | `PuertasDeAhora` no compara |
| 3 | 395 (S5) | `Agent/AgentLoop.cs` | `TelemetryBus.Emit` con una ubicación entera: **1** (S5); los otros nueve del censo no llevan ubicación | volver a `loc?.Id ?? ""` |

## Lo que NO entra, y se dice

- **`map_what_i_see` y el inventario pegado a cada acto (263) siguen mandando las filas de SAP a
  OpenAI**, con Jev encendido o apagado. La 393 decide quién decide; no deja el texto de SAP dentro
  del equipo. Es la decisión 1: la del dueño, con la 183, la 263, la 286 y la 390 delante.
- **`map_take «fila N»`**: parte de la salida técnica (a); sin ella, «fila 2» en el inventario tras el
  veto solo se puede elegir pidiendo `map_what_i_see`, que la nombra por su texto.
- **El objetivo en claro en la cuenta del tramo que va a la voz**: lo dictó la voz; volver a ella no
  saca nada del equipo que no hubiera salido ya. Al log va tapado (051).
- **`SurfaceLocator` sostiene la ubicación anterior con el hwnd de SAP** durante el Busy
  (`ConVentana` sobre `Current`). La 403 lo caza por sus consecuencias; arreglarlo en la fuente toca
  la 233 (decisión 4).
- **`map_what_i_see` con la etiqueta de otra ventana** (decisión 5).

## Hallazgos

<!-- Se rellena durante la implementación. -->

## Cierre

- [ ] 402 y 403 verdes, 395 verde con S5 nueva, y el resto intacto (`contrato-del-grafo.ps1`)
- [ ] La voz íntegra (`contrato-de-la-voz.ps1`)
- [ ] Sabotaje de cada una, visto rojo y restaurado
- [ ] Nivel 4 (a mano, por el dueño): SAP sin habilitar, `map_decidir` y `map_tramo` sobre NWP1 —la
      respuesta y el aviso a la voz nombran las filas por número—; y el Busy de SAP con otra app delante
      justo antes —el log dice «decisor: ✋ … lo leído no es de aquí» y no hay llamada a Jev—
