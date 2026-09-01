# Plan de implementación: el gesto viaja con la arista, y el clic derecho existe

Estado: **implementado** (2026-08-31) · Nace del diagnóstico del 2026-08-26 · Rama: `jose/agente-sdk`

> **De dónde viene esto.** Es el port de una lección aprendida sobre la arquitectura anterior
> (rama `jose/agente-sdk-antes-de-main`, commits `b33ccef..228de69`), reescrita para el terreno.
> Allí se midió, se arregló y se probó a mano; al traer main (que reemplazó `SurfaceMap` por el
> núcleo nuevo) el código murió pero la lección quedó — y resulta que el terreno la necesita más.

## Diagnóstico: qué se midió

Sobre la arquitectura VIEJA (2026-08-26, log `u-20260826.log`, instancia `C:\U-dev2`):

| Qué | Medida |
|---|---|
| Un `take` sobre contenido eran TRES clics físicos | `click` → 150 ms → «un clic no movió nada, se prueba el doble» → dos clics más |
| El gesto ensayado NO se guardaba | se aprendía `elegida.Info.ActionType` — la conjetura, no lo que funcionó |
| Con el arreglo, la segunda visita iba directa | `«specs»: gesto ya aprendido (doubleclick), no se ensaya` — de 3 clics a 1 |

Sobre la arquitectura NUEVA (main `8798030`, leída el 2026-08-26):

| Qué | Medida | Fuente |
|---|---|---|
| La mano UIA manda **siempre** `ActionType: "click"` | literal en el lambda | `FaceWindow.xaml.cs:465` |
| **Nadie** manda `doubleclick`: el dispatch existe y no tiene llamadores | `RealDoubleClick` solo se alcanza desde un `ActionType` que ya nadie construye | `UiaSurface.cs:878` + grep |
| El comentario dice lo contrario del código | *«escalar al doble clic siguen siendo de UIA y no se tocan»* — falso: no hay escalada en ninguna capa | `FaceWindow.xaml.cs:571` |
| `PulsarSegunElNucleo` juzga por consecuencia pero no escala ni recuerda el gesto | «pulsé y la pantalla no cambió» y ahí muere | `PulsarSegunElNucleo.cs:66-69` |
| La arista del terreno guarda destino, **no gesto** | `_destinos[ubicacion\nselector] = destino` | `nucleo/Grafo/Grafo.cs:171` |
| Clic derecho: **no existe** en ningún mundo | 0 coincidencias de `rightclick` | grep |

**Consecuencia práctica en main hoy:** un batch que llegue a una carpeta del Explorador la
**selecciona y no la abre**. `PulsarSegunElNucleo` reporta honesto «no cambió», el batch para — y
no hay forma de que a la siguiente le vaya mejor, porque no se aprendió nada del gesto.

## Por qué esto va dirigido por especificación

Porque la forma fácil de «arreglarlo» es la que ya falló dos veces: deducir el gesto del tipo de
control (2026-08-03: en Configuración el segundo clic ANULA el primero), o ensayar siempre
(2026-08-26: tres clics por acción, y el doble cayendo encima de un clic que ya había funcionado
cuando la app tardaba más que la ventana de espera). Sin promesas escritas antes, cualquiera de
las dos vuelve — las dos «pasan» a simple vista.

Y porque encaja con la tesis del terreno, no contra ella: *«las aristas se ganan ejecutando»*
(GENESIS-terreno). El gesto es parte de lo que se gana al ejecutar. La métrica del proyecto es
viajes al modelo por tarea: un batch que abre carpetas a la primera en vez de parar en cada una
es exactamente esa métrica bajando.

## La especificación

Dos contratos, porque el hecho vive en dos capas:

**En `nucleo/Contrato` (el terreno), en continuación de sus 20:**

| # | Promesa |
|---|---|
| 21 | el gesto que abrió una puerta viaja con su arista |

**En `tests/ContratoDelGrafo` (el comportamiento al pulsar), en continuación de las 81:**

| # | Promesa |
|---|---|
| 82 | el gesto aprendido no se vuelve a ensayar: la segunda vez va directo |
| 83 | el doble solo se ensaya sobre contenido: un botón jamás recibe un segundo clic |

**La que cierra el asunto es la 82.** La 83 es su guardarraíl: sin ella, la forma fácil de cumplir
la 82 es ensayar el doble en todas partes — y un doble sobre «Guardar» es guardar dos veces.

El clic derecho **no lleva promesa de contrato** y es deliberado: `RealRightClick` vive en
`UiaSurface` y necesita un elemento real delante — es nivel 4 (corrida a mano), no nivel 2. Ya
quedó probado sobre el Explorador real antes del port: menú de 16 opciones, log
`→ clic DERECHO en (952,324)`.

### Con qué se juzga cada una

| # | Se juzga con |
|---|---|
| 21 | `Grafo` a mano: `Cruzar(..., gesto)` y leerlo de vuelta; sin cruzar, gesto vacío |
| 82 | El arnés del batch que ya existe (`BatchCon`): una mano falsa que solo navega con `doubleclick`; 1ª vez = 2 toques y arista con gesto; 2ª vez = 1 toque directo |
| 83 | Mismo arnés: un `Button` cuya ruta nunca dispara → exactamente 1 toque, y el parcial honesto |

## Las fases

| Fase | Promesa | Qué toca |
|---|---|---|
| 1 | 21 | `nucleo/Grafo/Grafo.cs` (+`GestoDe`), `nucleo/Contrato/Contrato.cs` |
| 2 | 82, 83 | `PulsarSegunElNucleo.cs`, `FaceWindow.xaml.cs` (la mano acepta gesto), `tests/ContratoDelGrafo` |
| 3 | — | `UiaSurface.cs`: `rightclick` (port directo, ya probado a mano) |
| 4 | — | compuerta: el juez dice «no sé» cuando no puede correr (port de los arreglos medidos el 2026-08-25/26) |

## Lo que NO entra

- **Deducir el gesto por tipo de control.** El tipo solo acota QUÉ es seguro ensayar (contenido sí,
  botón no); nunca decide el gesto. La diferencia es la promesa 83.
- **El gesto en `PasoDelNucleo` (el camino de «ir»).** Hoy re-cruza aristas con clic simple; con
  esta spec la arista ya sabe su gesto, pero cablearlo ahí es otro cambio y se anota como pendiente
  en vez de colarlo.
- **Señalar antes de tocar y arrastrar por identidad.** Estaban en la spec vieja (promesas 62/64 de
  la numeración muerta); siguen pendientes y valen, pero el terreno cambió lo suficiente como para
  que merezcan su propia spec sobre esta base.
- **`map_tap` (el clic a ciegas).** La spec 002 vieja entera. Sobre el terreno hay que repensar qué
  significa un rescate cuando la compuerta de vida ya existe — no se porta a ciegas.

## Hallazgos

- **2026-08-26 (port)** — Main cree que escala al doble y no escala: el comentario de
  `FaceWindow.xaml.cs:571` afirma una capacidad que ninguna capa tiene ya. Un comentario que
  documenta código borrado es un mapa de un territorio que no existe.
- **2026-08-26 (port)** — **`Take` descarta `accionPedida` en silencio** (`SurfaceMapTools.cs:1813`):
  recibe el parámetro y construye `Paso(salida)` sin usarlo. Las acciones que el propio catálogo de
  la voz documenta (`click|addselect|doubleclick`, línea 1514) hoy no hacen nada — quien pida
  `addselect` cree que sumó a la selección y no sumó. Por eso el `rightclick` portado se queda en el
  ejecutor (`UiaSurface`) y NO se cablea aquí de tapadillo: llevar el gesto pedido a través del
  batch es su propia fase, con su promesa (un parámetro que se ignora en silencio es exactamente la
  clase de fallo que este repo colecciona — patrón nº16 de espíritu).
- **2026-08-26 (fase 2)** — El primer rojo de la 82 fue **del arnés, no del código**: la segunda
  pulsación se hacía desde `specs` sin haber vuelto a `docs`, o sea sobre OTRA arista — que
  correctamente no sabía nada (promesa 4 del núcleo). El arnés ganó un `volver()` explícito y el
  comentario que explica por qué.
- **2026-08-31 (agente optimizador)** — Tres bugs reales el mismo día que nació el código, todos
  arreglados y con sabotaje que los caza:
  1. **Los scripts de contrato tenían un agujero probado**: con `EAP='Stop'` + `2>&1`, una línea de
     stderr de un juez QUE SÍ CORRIÓ lanzaba y mataba el script antes del `exit 99` — el «no sé» se
     convertía en abort crudo o en un recuento inventado. Escudo `EAP='Continue'` alrededor de las
     invocaciones, con el porqué medido en el comentario.
  2. **El `Cruzar` de 3 args borraba el gesto aprendido** («no sé el gesto» ≠ «fue clic simple»):
     cada clic humano atribuido sobre una arista aprendida la degradaba al ensayo eterno. Ahora
     conserva (`GestoDe` como default), y la promesa 21 ganó el `Debe` que lo caza.
  3. **El gesto no se proyectaba a Neo4j ni volvía**: moría en cada apagado y cada arista re-pagaba
     el ensayo una vez por sesión. Ahora viaja en `LLEVA_A.gesto`, vuelve por el `Cruzar` de 4 args,
     entra en la huella (aprenderlo dispara re-proyección) y la promesa 19 lo compara.
- **2026-08-31 (agente optimizador, hueco conocido SIN arreglar)** — El ListItem que de verdad no
  navega paga la factura completa en cada visita (2×1800 ms + 3 clics): no hay arista, así que no
  queda rastro de «ya se ensayó y no navega». Guardar conocimiento negativo es decisión de spec,
  no parche — queda aquí como pendiente explícito.
- **2026-08-31 (agente tester, con dientes)** — **El vigía pasivo acuñó una arista falsa con
  NUESTRO doble clic sintético**: `mapa-vivo: aprendido: «Fecha de modificación» lleva de docs a
  specs` (11:55:24 y 11:57:25). `RecorrerSegunElNucleo` existe justo porque «el que pulsó fuimos
  nosotros», pero el atribuidor de clics humanos corre en paralelo y le colgó el cruce a la
  cabecera de columna. Quien pida ruta docs→specs puede recibir «pulsa Fecha de modificación». Es
  EL pecado que el GENESIS del terreno nombra como la fuente de aristas falsas del sistema viejo.
  Arreglarlo pide suprimir la atribución mientras hay una pulsación sintética en vuelo — promesa
  propia, fase propia.
- **2026-08-31 (agente tester)** — Tras la primera visita, «specs» por etiqueta se vuelve ambiguo
  (la pestaña y las migas que la propia visita creó también se llaman así): el guardia de
  ambigüedad rehúsa honesto y hay que pasar el selector. El camino rápido del gesto aprendido queda
  detrás de ese guardia justo después de estrenarse. Anotado; el guardia hace lo correcto.
- **2026-08-31 (agente tester)** — La mano del gesto (`new UiaSurface` en FaceWindow) no engancha
  `Log`, así que «clic físico» del camino nuevo no deja línea: la evidencia por clic se reconstruye
  de `clic-sap` y duraciones. Cosmético pero incómodo para diagnosticar.

## La corrida a mano (nivel 4)

La hizo un agente tester independiente el **2026-08-31**, sobre U.exe PID 20392 (`C:\U-dev2\bin`),
por el servidor MCP real (`127.0.0.1:8790/mcp/`, JSON-RPC `tools/call` → `map_take`) con el
foreground y la llamada en el mismo proceso. **Dos pantallas, con nombre: Explorador en
`…\U-Windows-App\docs` y `…\docs\specs`** (ida-vuelta-ida). Log: `u-20260831.log`.

| Qué | Evidencia |
|---|---|
| 1ª toma de «specs»: ENSAYÓ | 11:55:19→11:55:24 · dos clics con 3 s entre ellos · **5.618 ms** · llegó a `…/specs` |
| Vuelta con «Atrás» (Button, un clic) | 11:56:54 · 1.541 ms |
| 2ª toma de «specs»: DIRECTA | 11:57:23 · UN evento de clic · **1.501 ms** · llegó a `…/specs` |
| El argumento que no depende de contar líneas | 1.501 ms < 1.800 ms (la ventana mínima que un ensayo tiene que agotar): un ensayo **no cabe físicamente** en esa duración |
| Promesa 83 (botón que no navega) | «Actualizar (F5)»: 2.858 ms ≈ un clic + UNA espera; un ensayo habría necesitado ~5,6 s |

**De 5.618 ms y 3 clics a 1.501 ms y 1 clic.** La métrica del GENESIS —costo por paso— bajando en
la pantalla real.

## Cierre

- [x] Promesa 21 verde en `nucleo/Contrato` (21/21 ÍNTEGRO; la 19 ahora compara también el gesto)
- [x] Promesas 82-83 verdes en `tests/ContratoDelGrafo` (63 promesas numeradas 21-83, INTACTO)
- [x] Rotas a propósito: 6 sabotajes de la fase inicial (S1 rehecho con diff tras pillarse a sí
      mismo sin aplicar) + 2 de los arreglos del optimizador (restauración sin gesto → 19; el
      3-args que borra → 21). Cada uno cazado por su promesa exacta.
- [x] Probado a mano en 2 pantallas por agente independiente: `explorer.exe/docs` y
      `explorer.exe/specs` (tabla arriba)
- [x] Verificado por agente independiente: 3 jueces re-corridos, spec↔contrato literal, rama limpia
- [ ] Pendientes con nombre: arista falsa del vigía pasivo (hallazgo con dientes), conocimiento
      negativo del ensayo fallido, `accionPedida` descartada en `Take`, gesto en `PasoDelNucleo`
- [x] Estado: **implementado** (2026-08-31)
