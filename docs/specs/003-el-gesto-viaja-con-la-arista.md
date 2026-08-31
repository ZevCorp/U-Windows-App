# Plan de implementación: el gesto viaja con la arista, y el clic derecho existe

Estado: **propuesto** · Nace del diagnóstico del 2026-08-26 · Rama: `jose/agente-sdk`

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

## Cierre

- [ ] Promesa 21 verde en `nucleo/Contrato` (y las 20 anteriores intactas)
- [ ] Promesas 82-83 verdes en `tests/ContratoDelGrafo` (y las 81 anteriores intactas)
- [ ] Rotas a propósito, una por una
- [ ] Probado a mano en ≥2 pantallas, con nombre: …
- [ ] Estado: **implementado** (AAAA-MM-DD)
