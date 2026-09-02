# Plan de implementación: el workflow recién guardado se reconoce al instante y arranca sin esperar

Estado: **implementado, nivel 4 a mano pendiente** (2026-09-02) · Nace del diagnóstico del 2026-09-02 · Rama: `jose/workflow-a-la-mano`

> **La petición, en una frase:** que el workflow que acabo de enseñar sea muy fácil de identificar
> apenas lo guardo, y que al darle play arranque lo más rápido posible.

## Diagnóstico: qué se midió

Contra el Graph vivo (`graph-eight-pied.vercel.app`), el 2026-09-02 a las 13:50, con la API key
de esta máquina y solo lecturas:

| Qué | Medida | Fuente |
|---|---|---|
| Cómo se llaman los workflows guardados | de 4, **3 se llaman «Workflow sin descripción»** y el cuarto «User workflow summary:» (la primera línea del resumen del LLM, que es un encabezado) | `GET /api/v1/workflows` |
| Qué muestra el selector | `description` primero, y con eso los tres son indistinguibles | `WorkflowSummary.FromJson` |
| De dónde sale el nombre en Graph | si se enseñó sin título, la **primera frase del summary del LLM**; si el finish falló (504, `summary=""`), se queda el relleno | `Graph/src/application/use-cases/WorkflowLearner.js:131-136` |
| Lo que SÍ se sabe de cada uno | `sourceOrigin` (`uia://claude.exe`, `sapgui://…`), `sourceTitle`, `createdAt` (entero Neo4j `{low,high}`), `totalSteps` | la misma respuesta |
| Orden de la lista | del más viejo al más nuevo (`ORDER BY w.id ASC`) | `Neo4jWorkflowRepository.js:174` |
| Qué pasa con el índice del carrusel al terminar de enseñar | no se mueve: el nuevo queda al final, sin seleccionar | `ReloadDirectWorkflowsAsync` conserva `_directIndex` |
| ¿Se puede renombrar en Graph? | no hay endpoint | grep de rutas en `Graph/src` |
| Pedir el plan al darle play | **342 y 484 ms** en caliente; la primera llamada al cerebro **2 624 ms** (arranque frío de Vercel) | `POST /workflows/:id/plan`, `GET /workflows` |
| Dónde cae eso | en el camino crítico del botón: `RunAsync` empieza por `GetPlanAsync` | `WorkflowPlayer.cs:157` |
| Corridas recientes con `⏱ TIEMPOS` en el log | ninguna desde el 2026-08-12: no hay medida de terreno del resto de la cadena | `%LOCALAPPDATA%\U\logs` |

## Por qué esto va dirigido por especificación

Porque las dos cosas se pueden «arreglar» sin arreglarlas: un nombre bonito que sigue saliendo del
LLM (y del 504), y un play que parece rápido porque se quitó una espera que hacía falta. Lo que se
promete se puede juzgar sin pantalla: **de qué se compone un nombre** cuando el cerebro no lo dio,
**que el nombre que pone el operador manda y sobrevive**, **que el nuevo queda elegido**, y **que
el play no vuelve a pedir lo que ya tiene**.

**La regla del flujo, y no tiene excepciones:** ninguna línea de producción entra antes que la
promesa que la juzga.

## La especificación

En continuación: la 107 la tiene la rama `jose/aura-de-aprendizaje` (spec 006), sin mergear.

| # | Promesa | Fase |
|---|---|---|
| 108 | un workflow se presenta por lo que se sabe de él —app, ventana, cuándo, cuántos pasos— y nunca por el relleno del cerebro: «Workflow sin descripción» y «User workflow summary:» no son nombres | 1 |
| 109 | el nombre que le pones manda y sobrevive al reinicio: se guarda en disco por id, y sin nombre puesto se vuelve al derivado | 2 |
| 110 | la lista va del más nuevo al más viejo, y el recién enseñado queda elegido; si no se sabe cuál es, se elige el más nuevo | 3 |
| 111 | darle play no vuelve a pedir el plan si ya está en la mano: se pide al elegir el workflow y la corrida lo usa; sin plan a la mano se pide una sola vez | 4 |

### Con qué se juzga cada una

Mapa a mano en la propia prueba, sobre piezas puras en `windows-client/src/Workflows/`:

- **108** `NombreDeWorkflow.Derivar(JsonElement)`: con el relleno o el encabezado del LLM devuelve
  «Claude · 2 sep 13:42 · 6 pasos»; con una descripción real la devuelve tal cual; lee `createdAt`
  tanto como entero Neo4j `{low,high}` como número.
- **109** `NombresDeWorkflows`: `Poner(id, nombre)` → otra instancia (otro arranque) lo lee de
  disco; poner vacío quita el nombre.
- **110** `SelectorDeWorkflows.Ordenar` (por `CreatedAt` descendente, empate por id) e
  `IndiceDe(lista, id)` (el nuevo, o 0 si no consta).
- **111** `WorkflowPlayer.PlanSource`: con un backend de mentira que cuenta las peticiones a
  `/plan`, con `PlanSource` puesto la corrida hace **0**; sin él, **1**. El plan de la prueba lleva
  una superficie que este cliente no maneja, así la corrida para ANTES de tocar la pantalla.

Nivel 4, a mano y con log: enseñar algo, ver que aparece elegido y con nombre legible, ponerle
nombre, reiniciar Ü y verlo; y `▶ play: plan precargado en N ms` en el log frente a `pedido`.

## Las fases

| Fase | Promesa | Qué toca |
|---|---|---|
| 1 | 108 | nuevo `Workflows/NombreDeWorkflow.cs` + `WorkflowSummary` sale de la ventana de la biblioteca a `Workflows/` con `CreatedAt`, `SourceOrigin` |
| 2 | 109 | nuevo `Workflows/NombresDeWorkflows.cs`; UI: cuadro «ponle nombre» tras enseñar y ✏ en el selector |
| 3 | 110 | nuevo `Workflows/SelectorDeWorkflows.cs`; `ReloadDirectWorkflowsAsync` elige el recién enseñado |
| 4 | 111 | `WorkflowPlayer.PlanSource` (windows-graph) + `Workflows/PlanesALaMano.cs`; precarga al elegir, y el log dice de dónde vino el plan |

## Lo que NO entra

- **Tocar Graph.** Es otro repo y el nombre del LLM seguirá viniendo roto a veces; por eso el
  nombre vive en el cliente, que es quien sabe app, hora y pasos sin pedírselo a nadie.
- **Acortar las esperas del player** (`SurfaceReadiness`: 120 ms de sondeo, techos de 4 y 8 s).
  Cortan en cuanto el elemento está listo; sin una corrida medida con `⏱ TIEMPOS` no hay evidencia
  de que estorben, y el patrón nº14 prohíbe optimizar la cadencia sin medir el coste.
- **Un atajo de teclado global para play.** Otra decisión; se anota.

## Hallazgos

- **2026-09-02 (rojo → verde)** — Las cuatro salieron rojas por la razón escrita («todavía no
  existe…») con las 106 anteriores intactas, y verdes a la primera con el código.
- **2026-09-02 (el `createdAt` de Neo4j)** — Llega como `{low, high}` (dos int32); el valor es
  `high·2³² + low` **sin signo** en `low`. Con `low` firmado, la mitad de las fechas salen mal, y
  en silencio. La promesa 108 lo cubre con las dos formas (entero Neo4j y número llano).
- **2026-09-02 (lo que el nombre NO incluye)** — El `sourceTitle` solo entra si difiere de la app
  («Claude · Claude» no dice más que «Claude») y recortado a 28 caracteres: el carrusel es una
  píldora de 34 px de alto y un título de ventana entero la convertía en tres líneas.
- **2026-09-02 (PlanSource solo sin variables)** — El plan precargado se pide sin variables; una
  corrida CON variables (la biblioteca las admite) sigue pidiendo a Graph, porque un plan con
  variables sustituidas no es el mismo plan. Está escrito en `CargarPlanAsync`.

- **2026-09-02 (nivel 4, arranque real)** — `U.exe` de Release recién compilado, con los 4
  workflows del Graph vivo: a los 14 s del arranque el log dice
  `plan de wf_1788279683135 a la mano en 872 ms (43 pasos)` — el más nuevo quedó elegido y su
  plan pedido sin que nadie pulsara play. Lo que falta verlo con ojos: el carrusel con los nombres
  derivados, el cuadro de nombre al terminar de enseñar, y un play con `plan precargado` en el log.

## Cierre

- [x] Promesas 108-111 verdes (CONTRATO INTACTO, 2026-09-02)
- [x] Rotas a propósito una por una, comprobadas por diff, y recompilado después
- [x] Nivel 4 parcial: arranque real, el nuevo elegido y precargado (log)
- [ ] Nivel 4 a mano: enseñar → ver el nombre → ponerle nombre → reiniciar → play, en ≥2 apps con nombre y log
- [ ] Estado: **implementado** (AAAA-MM-DD)
