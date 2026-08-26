# Terreno profundo — batch a profundidad sobre el código que ya tenemos

**Plan sobre `windows-app`, no repo nuevo.** Rama base: `jose/batch-resolver-estable`.
Fecha: 2026-08-25. Sustituye el enfoque de `GENESIS-terreno.md` (repo desde cero): los cimientos
ya están construidos y probados aquí; ese archivo queda como doctrina de referencia (reglas del
código, la tesis, los números).

**El lema sigue: nosotros creamos el terreno, el Agent SDK lo navega.**
**LA MÉTRICA sigue: viajes al modelo por tarea.** Baseline probado en Wikipedia: 28 viajes/fracaso
→ 8 viajes/19 s/éxito tras la tanda de estabilidad. Banco de pruebas nuevo: **SAP Logon**.

---

## 0. Lo que YA está construido y probado (no se toca, se usa)

| pieza | dónde | estado |
|---|---|---|
| El grafo puro (nodos=ubicaciones, Vivo, aristas por cruce) | `nucleo/Grafo/Grafo.cs` | probado, 25+ promesas |
| `map_batch` con compuerta de vida + escalera de resolución + parcial honesto | `RecorrerSegunElNucleo.cs` | promesas 56–65 |
| Servidor MCP real (JSON-RPC, timeout por herramienta, despacho paralelo) | `ProtocoloMcp.cs` + `ServidorMcp.cs` (8790) | promesas 60–62, 67 |
| Piloto Agent SDK con LA MÉTRICA | `piloto/piloto.mjs` | corriendo |
| Web direccionable (página ≠ sitio, ir por la URL) | `ComoMePongoDelante` + `AppAligner` | promesas 24–25, 66 |
| Lectura SAP por Scripting COM (enlace tardío) | `windows-graph/src/Surfaces/SapGuiSurface.cs` | escrito, verificado el motor |
| El visor | `nucleo/visor/index.html` | **ya pinta el núcleo nuevo** vía Neo4j |
| Alimentación del grafo | `MapaVivo` → `Grafo.Observar` | **solo UIA — ciego dentro de SAP** |

El descubrimiento clave de la profundidad: **el grafo YA guarda lo necesario para predecir**.
`_vistos` recuerda los elementos de cada ubicación (estés o no estés allí) y `_destinos` cada
cruce. «Predecir estados futuros» no pide un modelo de datos nuevo: pide (a) que SAP entre al
grafo, (b) una herramienta de consulta del terreno por delante, y (c) el visor enseñándolo.

---

## T1 — Sentidos y manos SAP: que SAP entre al núcleo

**El hueco:** `MapaVivo` lee la pantalla con `UiaReader` siempre. Dentro de una sesión SAP, UIA ve
un Pane vacío → el grafo no aprende nada de SAP. Y `PulsarSegunElNucleo` pulsa por UIA → tampoco
sabría pulsar un campo SAP.

**El cable (adaptador por superficie, como manda el núcleo congelado):**

1. **Sentido**: en el lector que `FaceWindow` le da a `MapaVivo`, si la ubicación de delante es
   `sapgui://…`, leer los elementos por `SapGuiSurface` (scripting): `Selector` = Id de scripting
   (`wnd[0]/usr/txtFIELD`, `wnd[0]/tbar[0]/okcd`), `Etiqueta` = texto/tooltip, `Tipo` = GuiButton,
   GuiTextField… Si no, UIA como hoy. Una función de lectura por superficie, misma salida.
2. **Mano**: en `PulsarSegunElNucleo`, si el selector empieza por `wnd[` (o la ubicación es
   `sapgui://`), ejecutar por scripting (press/focus+enter según tipo) y verificar por
   consecuencia con la identidad de sesión (transacción/dynpro cambian → nos movió).
3. **Escribir**: `map_type` sobre campos SAP = poner `.Text` por scripting, no teclear a ciegas.

**Promesas (contrato del cliente, rojo primero, sabotaje verificado):**
- a) *SAP se observa*: con una sesión falsa que entrega elementos de scripting, `Observar` recibe
  los campos con su Id como selector.
- b) *SAP se pulsa por identidad*: un paso de batch cuyo selector es un Id de scripting va por la
  mano SAP, nunca por UIA ni coordenadas.
- c) *la ubicación SAP es la sesión*: `sapgui://SID/TCODE/PROGRAMA/DYNPRO` — cambiar de dynpro es
  cambiar de nodo.

**Prueba real:** SAP Logon abierto → entrar a una sesión → `map_what_i_see` lista los campos DE
VERDAD (no 12 elementos UIA del marco) → un `map_batch` de 2 pasos (okcd + Enter) cruza a una
transacción y fabrica la arista. Gotcha vigente: sondear COM con `cscript`/VBS, no PowerShell.

---

## T2 — El visor: pestaña «Terreno» (verte lo que construimos, en el archivo que ya te gusta)

**Veredicto sobre tu duda:** el visor NO está sobre el concepto viejo. Lee Neo4j en
`127.0.0.1:7474`, y eso es la proyección del núcleo nuevo (`ProyectorNeo4j`), que corre en cada
observación. «Aquí» y «Mapa completo» ya son nodos-ubicación con vivo/recordado. Lo que le falta
es enseñar LO NUEVO: los batches y la profundidad.

**El plan: modificar el mismo `index.html`, una pestaña nueva «Terreno»**, con el mismo lenguaje
visual (mismo lienzo, misma paleta, claro/oscuro):

1. **El último batch, dibujado**: el camino pedido sobre el subgrafo — pasos hechos en verde, el
   paso donde paró en rojo con su motivo, y las puertas vivas que la respuesta parcial ofreció.
2. **Profundidad desde aquí**: árbol de 2–3 niveles desde el nodo actual — lo que el terreno
   predice que habrá tras cada cruce (vivo ahora = trazo lleno; solo recordado = punteado, la
   misma distinción que el núcleo ya hace).
3. **LA MÉTRICA**: las últimas corridas del piloto (viajes, llamadas, resultado), para ver la
   curva bajar.

**Datos — dos endpoints nuevos en el 8792** (`ServidorDelNucleo`, junto a `/nucleo` y `/ir`):
- `GET /terreno?desde=<ubicacion>&niveles=2` → el árbol de predicción desde el grafo
  (`DesdeAqui` encadenado por `Destino`), corto y con vivo/recordado por elemento.
- `GET /batches` → anillo de las últimas N corridas de `RecorrerSegunElNucleo` (pasos, dónde
  paró, motivo) + las métricas que el piloto reporte (`POST /metrica` al terminar cada corrida).

El visor sigue sin leer al pintor: lee el grafo y el recorredor, las mismas fuentes que todo.

**Promesas:** d) `/terreno` responde el árbol con la distinción vivo/recordado; e) cada batch deja
su rastro consultable en `/batches` (el anillo no crece sin límite).

---

## T3 — Terreno por delante para el MODELO: `map_ahead`

La misma consulta de T2, como herramienta MCP para que el piloto planifique batches profundos:

```
map_ahead(exit?: puerta, niveles?: 1-3)
→ «Tras «NWP1» quedarás en sapgui://QAS/NWP1/SAPL…/0100. Allí recuerdo vivo la última vez:
   «Paciente» (campo), «Buscar» (botón)… Tras «Buscar» quedarás en …»
```

- Respuesta corta (≤15 puertas por nivel, las cruzadas primero) — regla 8 del génesis.
- La compuerta NO se relaja: `map_ahead` propone con memoria, `map_batch` verifica con vida.
  Predicción ≠ promesa; el terreno vivo dispone.
- El prompt del piloto aprende la ruta predilecta nueva: `map_ahead` para planear → `map_batch`
  hondo → replanificar solo donde divergió.

**Promesas:** f) `map_ahead` encadena por aristas cruzadas y distingue vivo/recordado; g) lo no
cruzado se anuncia como «destino por descubrir», nunca como destino inventado.

---

## T4 — Las rondas de profundidad (tú pruebas, yo analizo, ampliamos)

El ciclo que pediste, ya con todo conectado:

1. Tarea SAP real 0→100 por el piloto (arranque: login → transacción → campos → guardar).
2. Tú miras el visor («Terreno») mientras corre; yo mido LA MÉTRICA y leo dónde paró cada batch.
3. Analizo qué le faltó al terreno para llegar más hondo (¿un dynpro sin nombrar bien?, ¿un campo
   que es puerta y está clasificado como contenido?, ¿una espera?) — se arregla con promesa.
4. Ronda siguiente: el batch debe llegar más hondo y la métrica bajar. Se apunta la curva en el
   plan (aquí) con fecha.

**Curva a llenar:**

| fecha | tarea | viajes | profundidad máxima de un batch | qué faltó |
|---|---|---|---|---|
| — | — | — | — | — |

---

## Orden y dependencias

```
T1 (SAP al núcleo)  ──→  T4 (rondas)
T2 (visor Terreno)  ──→  T4        T2 y T3 comparten la consulta de terreno: se escribe UNA vez
T3 (map_ahead)      ──→  T4        (núcleo o un módulo puro), y la sirven el 8792 y el MCP.
```

T1 es la puerta de todo lo SAP. T2 y T3 pueden ir en paralelo a T1 (la consulta de terreno se
prueba con el grafo de Wikipedia que ya existe). T4 no empieza hasta que T1 y T3 estén verdes.

Flujo por fase: el de siempre — spec si toca el núcleo, promesa ROJA por el motivo escrito,
implementar, verde, sabotaje verificado, prueba real yo mismo, métrica si toca el batch, portero.
