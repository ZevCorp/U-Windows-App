# Batch sobre nodos vivos — Agent SDK conduciendo el grafo

**Plan, no implementación.** Rama `jose/batch-sobre-nodos-vivos`. Fecha: 2026-08-24.

El problema que esto ataca, dicho como se vio: se le pide algo al asistente y **busca formas de
hacerlo** — prueba varias vías y la que funciona al final es pulsar el elemento vivo que expone
nuestro grafo. Esa vía tiene que dejar de ser la última que prueba y pasar a ser **la ruta
estándar**: la primera, siempre. Y tiene que poder recorrerla en tandas, no paso a paso con un
viaje al modelo entre cada clic.

---

## 1. Cómo funciona el batch del Agent SDK (investigado, no supuesto)

### 1.1 Qué es el Agent SDK

`@anthropic-ai/claude-agent-sdk` (TS) / `claude-agent-sdk` (Python): **Claude Code empaquetado como
librería**. Un solo punto de entrada — `query(prompt, options)` — que trae el bucle de agente
completo: herramientas, gestión de contexto, hooks, subagentes, sesiones y permisos. Corre en
nuestro proceso, con nuestra API key. No confundir con el Tool Runner del SDK de la API (un helper
de bucle sin herramientas propias) ni con Managed Agents (Anthropic hospeda el bucle).

Lo que nos importa: **se le conectan herramientas externas por MCP** y se le autorizan sin
preguntas:

```ts
query({
  prompt: "entra al correo enviado a Jerónimo",
  options: {
    mcpServers: { u: { type: "http", url: "http://127.0.0.1:8790/mcp" } },
    allowedTools: ["mcp__u__*"],   // sin esto, ve las herramientas pero no puede llamarlas
    systemPrompt: "...la ruta predilecta es el mapa..."
  }
})
```

Las herramientas llegan al modelo como `mcp__u__map_batch`, etc. `allowedTools` con comodín es la
forma recomendada (no `bypassPermissions`, que apaga más de lo necesario).

### 1.2 El batch de computer use (`computer_batch`)

Contrato exacto, tomado de la herramienta real:

- **Una llamada = una lista de acciones** (`left_click`, `type`, `key`, `scroll`, `screenshot`,
  `zoom`, `wait`…). *«Each individual tool call requires a model→API round trip (seconds); batching
  a predictable sequence eliminates all but one.»*
- **Secuencial, y para en el primer error.**
- **La compuerta corre antes de CADA acción del batch** — si una acción abre una app no permitida,
  la compuerta de la siguiente salta y el batch se detiene ahí. El modelo recupera el control con
  lo que alcanzó a pasar.
- Los screenshots del batch vuelven intercalados con las salidas; las coordenadas que se escriben
  en el batch refieren SIEMPRE al screenshot de ANTES de la llamada.

### 1.3 El batch del DOM en Chrome (`browser_batch`) — por qué se siente tan rápido

- Cada ítem es `{name, input}` de **cualquier** herramienta del navegador (click, type, navigate,
  read_page…), secuencial, para en el primer error, **el permiso de cada herramienta corre por
  ítem**.
- La velocidad viene de DOS cosas juntas: actúa sobre **identidades del DOM** (refs de
  `read_page`, no píxeles) y el batch elimina los viajes al modelo. Identidad + batch.

### 1.4 La esencia, destilada

> **velocidad = predecir N pasos + una compuerta barata y determinista antes de cada paso +
> parar honesto devolviendo el control cuando la realidad diverge.**

El modelo solo paga un viaje cuando el mundo no era como lo predijo. Todo lo demás corre a la
velocidad de la máquina.

---

## 2. El isomorfismo: nuestro grafo ya es esa superficie

La correspondencia es uno a uno, y nuestra compuerta es **mejor** que la de ellos:

| computer_batch (SDK) | map_batch (nuestro) |
|---|---|
| lista de acciones por coordenada | lista de pasos por **selector** (identidad UIA — nunca clic por coordenadas, regla de la casa) |
| compuerta: ¿la app del frente está permitida? | compuerta: **¿el elemento que toca está VIVO aquí?** (`DesdeAqui(aqui)` con `Vivo=true`) |
| para en el primer error | para en el primer no-vivo, **con el estado fresco en la respuesta** |
| screenshot intercalado | «quedaste en X, y aquí veo vivo: A, B, C» — el modelo replanifica sin otra llamada de reconocimiento |
| coordenadas ancladas al screenshot pre-batch | selectores: inmunes a que algo se mueva de sitio |

La bandera `Vivo` del núcleo —que ya existe, ya se prueba (promesas 1, 6, 11) y ya distingue «lo
veo ahora» de «lo recuerdo»— es exactamente la compuerta por-paso que este patrón necesita. No hay
que inventarla: hay que **ponerla en la puerta**.

---

## 3. La regla simple: `map_batch`

Una herramienta nueva en `SurfaceMapTools`:

```
map_batch(pasos: [{exit, action?, text?, hasta?}])
```

Por cada paso, en orden:

1. **COMPUERTA** — ubicación actual por el localizador (la fuente única de «dónde estoy»); el
   elemento pedido se busca entre lo VIVO de aquí. No vivo → **STOP**. No se pulsa a ciegas jamás.
2. **PULSAR** — por `PulsarSegunElNucleo.Pulsa`, que ya verifica por consecuencia (promesas 44 y
   45: pulsar sin que cambie nada no es llegar; un clic que no se pudo dar no se cuenta).
3. **APRENDER** — `Cruzar` escribe la arista con certeza, porque el que pulsó fuimos NOSOTROS
   (ver §4). Ya lo hace `Pulsa`; gratis.
4. Siguiente paso.

**La respuesta nunca miente**: «hice 3 de 5, estás en «X»; paré porque «Guardar» no está vivo aquí;
lo que SÍ veo vivo: …». Progreso parcial contado como parcial, con lo necesario para replanificar
en la misma respuesta — igual que el screenshot intercalado del computer_batch.

**El freno manda**: Escape ya corta `UiaSurface.Execute` por `HayQueParar`. El batch lo hereda —
y se promete, no se supone.

Notas de diseño:
- `hasta` opcional por paso (destino esperado): si se da y la llegada no coincide, también para.
  Es el `at` de `map_run`, pero por consecuencia y no por fe.
- `map_run` ya existe (`go_to|take|type|unblock` con anclas `at`). **Decisión propuesta**: nace
  `map_batch` con la compuerta de vida como corazón; cuando esté probado, `map_run` se pliega
  sobre él. Dos ejecutores de secuencias conviviendo mucho tiempo = dos opiniones del mismo hecho.

---

## 4. Las aristas: adoptamos la simplificación

El principio rector propuesto, escrito como regla:

> **Mapear todo lo visible (vivo y no vivo) ya es la arista.** `Observar` escribe
> `Ubicacion —ALCANZA→ Elemento` con su bandera de vida — eso es alcanzabilidad, y alcanzabilidad
> basta para ACTUAR. Las aristas entre ubicaciones (`LLEVA_A`) no se deducen mirando: **se ganan
> ejecutando.**

Por qué esto disuelve el problema de los nodos incomunicados:

- Hoy `LLEVA_A` depende de **atribuir clics humanos** (ClickWatcher), y esa atribución falla a
  cada rato — el log de hoy mismo: «salto de X a Y SIN atribuir», «el clic guardado ya no explica
  el viaje». Adivinar quién causó una transición que no hicimos nosotros es intrínsecamente frágil.
- Bajo `map_batch` no hay nada que adivinar: **nosotros pulsamos**, así que sabemos con certeza
  qué selector, desde dónde, llevó a dónde. `Cruzar` lo escribe y la promesa 46 ya lo vigila
  («lo que se cruza queda aprendido, y manda el terreno»).
- Consecuencia: cada ejecución en batch **fabrica aristas**. El grafo se conecta usándose — el
  aprendiz que ejecuta lo aprendido, aplicado al terreno. Y `ComoLlego` empieza a poder planificar
  multi-salto con aristas reales, no adivinadas.
- La atribución del vigilante de clics queda degradada a *bonus de mejor esfuerzo* (enriquece
  cuando acierta, no carga nada estructural). No se borra en esta tanda; se le quita el peso.

Lo que NO cambia: las cuatro reglas del núcleo (vivo ≠ recordado; observar no dice dónde estás; un
paso, no una ruta; no se inventa nada). `map_batch` es N invocaciones de «un paso» con compuerta —
no una promesa sobre el futuro.

---

## 5. Arquitectura de integración

```
┌─────────────────────────┐   MCP streamable HTTP    ┌──────────────────────────────┐
│ sidecar Agent SDK (TS)  │ ───────────────────────→ │ U.exe                        │
│ query(prompt, options)  │   127.0.0.1:8790/mcp     │  ServidorMcp (NUEVO)         │
│ · ruta predilecta:      │                          │   └→ LocalMcp (mismas manos, │
│   mcp__u__map_batch     │                          │       mismos vetos, freno)   │
│ · fallback:             │                          │  Grafo (vivo/recordado)      │
│   computer use batch    │                          │  PulsarSegunElNucleo→Cruzar  │
└─────────────────────────┘                          └──────────────────────────────┘
```

Piezas, con lo verificado hoy:

1. **Servidor MCP real dentro de U** — hace falta. La sonda 8791 es un shim de desarrollo
   (`{"tool","args"}` a pelo, verificado: no hay `jsonrpc`, ni `tools/list`, ni `initialize` en
   `LocalMcp`). El Agent SDK habla MCP de verdad. El servidor nuevo (JSON-RPC sobre HTTP
   streamable, solo 127.0.0.1) despacha al **mismo** `LocalMcp` — nada de un segundo camino de
   acción: mismas manos, mismos vetos, mismo freno. Puerto nuevo (8790); la sonda sigue siendo
   sonda.
2. **Sidecar Agent SDK** — Node/TS (el backend del repo ya es Node). Un proceso pequeño:
   `query()` con `mcpServers` apuntando a U, `allowedTools: ["mcp__u__*"]`, y el prompt del
   sistema con la ruta predilecta: *primero el mapa (`map_batch` sobre lo vivo); computer use en
   batch solo cuando el mapa no alcanza; y lo que el mapa aprenda en el camino queda para la
   próxima*. Se prueba con prompts de texto — sin voz, sin UI — que es como se aísla «¿sirve el
   terreno?» de «¿acierta el modelo?».
3. **La voz, después.** Conectar el sidecar a la conversación hablada es fase posterior; no se
   toca `ConversacionEnVivo` en esta tanda.

Detalles que ya sabemos que muerden (para no re-aprenderlos):
- Resultado de herramienta > 25k tokens → el SDK lo manda a archivo. `map_batch` responde corto.
- Tool search viene activo por defecto en el SDK; con ~30 herramientas nuestras está bien así.
- `allowedTools` explícito, endpoint solo loopback, y el freno (Escape) corta también al sidecar
  porque corta a las manos, no al cerebro.

---

## 6. Fases, cada una con su promesa

**F1 — `map_batch`, sin SDK todavía** (el corazón; todo lo demás depende de esto)
- Promesas nuevas del contrato del cliente, con sabotaje:
  - a) *la compuerta muerde*: un paso cuyo elemento no está vivo NO se pulsa, y ahí se para.
  - b) *parcial honesto*: la respuesta dice N de M, dónde quedó, por qué paró y qué hay vivo.
  - c) *el batch fabrica aristas*: tras un batch de 3 pasos, las 3 `LLEVA_A` están en el grafo.
  - d) *Escape corta el batch* a mitad, y lo dice.
- Prueba real por la sonda: la secuencia del correo a Jerónimo, en UNA llamada.

**F2 — Servidor MCP real en U**
- `initialize` / `tools/list` / `tools/call` sobre el catálogo de `LocalMcp`.
- Promesa: un cliente MCP genérico (el inspector oficial) lista las herramientas y ejecuta
  `map_where_am_i` sin saber nada de U.

**F3 — Sidecar Agent SDK + medición**
- `query()` conectado, prompt de ruta predilecta.
- La prueba que decide si esto valió: **«entra al correo enviado a Jerónimo», 0→100, midiendo
  cuántos viajes al modelo costó.** Hoy: uno por acción más los intentos fallidos. Meta: el grueso
  en 1–2 llamadas de `map_batch`, y el modelo interviniendo solo donde el terreno diverge.

**F4 — después de que F3 pruebe la tesis**
- Plegar `map_run` sobre `map_batch`.
- Computer use en batch como fallback dentro del mismo sidecar.
- El puente con la voz.

Decisiones abiertas (para resolver en F1, no antes):
- Forma exacta de `pasos` (¿aceptar `type`/`unblock` desde el día uno o solo `take`?). Propuesta:
  solo `take` + `type` en F1 — es lo que la prueba real necesita.
- ¿Qué devuelve la compuerta cuando el elemento pedido está *recordado pero no vivo*? Propuesta:
  distinguirlo en la respuesta («lo conozco pero ahora no lo veo»), que es la promesa 15 del
  núcleo hablando por el batch.
