# Experimentos viejos — el índice de rescate

**Esta rama es la foto de `main` del 2026-08-30, justo antes de la gran limpieza** que dejó main
solo con el mecanismo del terreno (grafo de nodos vivos + batch con compuerta + enseñanza por
recorrido, probado sobre SAP ese mismo día). Nada de lo que main perdió se perdió de verdad:
está aquí, entero y compilable.

Decisión de José David: main cuenta UNA historia — la del terreno. Lo valioso de los intentos
anteriores vive aquí con nota de para qué servirá.

## ⭐ Lo que vale oro para el futuro

### `windows-client/src/Navigation/JerarquiaWeb.cs`
Lee los **landmarks que el HTML declara** (`<header>`, `<nav>`, `<main>`) por UIA: la página
cuenta su propia estructura, en UNA lectura, sin cruzar nada (medido sobre GitHub 2026-08-08:
la navegación del repo, las migas, y 74 puertas de contenido separadas solas). **Es la versión
web de lo que el terreno hizo con SAP** — usar el idioma nativo de la superficie. Cuando el
terreno vaya a web: los landmarks agrupan puertas del grafo. El código está cosido a
`SurfaceMap.FijarNivel`; se rescata la LECTURA de landmarks y el orden de autoridad (lo humano
manda, la página más que la estadística).

### `windows-client/src/Ui/ProfundidadClasica.cs` + su atajo Ctrl+Shift (en `GlobalHotkeys`)
El panel de niveles de la derecha con salto entre profundidades por teclado. José David lo
quiere reutilizable: cuando la pestaña «Terreno» del visor necesite navegación por teclado
entre niveles del árbol de predicción, este es el patrón (pintado por nivel + hotkey global).

### `windows-client/src/Navigation/SurfaceMap.cs` (2.063 líneas) — como LIBRO, no como código
El mapa por niveles. Su mecanismo está sustituido por el grafo, pero sus remarks fechados son
un catálogo de casos raros de UIA/Windows ya resueltos: las celdas «Nombre» de Windows 11 que
envenenaban la atribución, el cromo transversal, qué es puerta y qué contenido, las carpetas
como única fuente de profundidad, EsPuerta… Leerlo antes de llevar el terreno a una superficie
nueva ahorra semanas.

### `map_run` (en `SurfaceMapTools.cs`, método `Run`) — las anclas `at`
El predecesor del batch: secuencias `go_to|take|type|unblock` con anclas «espera estar en X».
El germen del paso `hasta` que el plan del batch dejó para F4.

### Bronce→Plata (`Bronce.cs`, `Plata.cs`, `ProfundidadClasica`) — los criterios
La clasificación semántica de pantallas (niveles, navegación transversal vs contenido, «la
misma entrada da la misma plata», «lo humano manda y queda como desacuerdo»). El mecanismo
muere; los CRITERIOS y el orden de autoridad sirven si el terreno algún día clasifica
pantallas con LLM (la capa 2 diferida de graphify).

### `windows-client/src/Ui/GraphExplorerWindow.cs` (3.726 líneas)
La UI rica del mapa viejo: pintado por niveles, corrección manual de niveles con sello humano,
Limpiar, inspección. Patrones WPF copiables cuando la pestaña «Terreno» quiera interacción.

### El acta de las promesas retiradas (1–20 del contrato del grafo)
Vigilaban el mapa por niveles y las capas bronce→plata. Sus LECCIONES eternas: dos puertas al
mismo sitio son dos aristas; el robo de foco no es transición; guardar/cargar no pierde nada
(heredada al núcleo antes de retirarla); lo que dijo una persona manda; el orden del paseo no
cambia la estructura. El texto completo de cada una está en `tests/ContratoDelGrafo/Contrato.cs`
de esta rama.

## 🗑️ Lo que murió sin nota (está aquí por la foto, pero nadie lo llora)

- El **crawler** / `map_learn_app` (exploración autónoma del mapa viejo) — a José David no le
  gustó nunca su comportamiento.
- El **arquitecto nocturno** (`scripts/noche-arquitecto.ps1` + `Agent/AgentLoop.cs`).
- La **sonda 8791** (shim de desarrollo pre-MCP) y el **escenario CI de explorer** (podrido:
  juzgaba niveles que ya no se fijaban).

## Cómo rescatar algo

```bash
git checkout experimentos-viejos -- <ruta/del/archivo>
```

y adaptarlo al mundo del grafo (las puertas de entrada hoy son `Nucleo.Grafo`, `MundoQueToca`
y el visor). No re-conectar nada a `SurfaceMap`: está muerto en main a propósito.
