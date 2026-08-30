# Terreno profundo — batch a profundidad sobre el código que ya tenemos

**Plan sobre `windows-app`, no repo nuevo.** Rama base: `jose/batch-resolver-estable`.
Fecha: 2026-08-25. Sustituye el enfoque de `GENESIS-terreno.md` (repo desde cero): los cimientos
ya están construidos y probados aquí; ese archivo queda como doctrina de referencia (reglas del
código, la tesis, los números).

**El lema sigue: nosotros creamos el terreno, el Agent SDK lo navega.**
**Regla fija (pedida por José David, 2026-08-26): el despacho entre mundos vive en UN solo sitio
(`MundoQueToca.cs`). El grafo, el batch, la compuerta y el MCP no saben en qué mundo miran, pulsan
o escriben. Cada superficie futura es un adaptador más ahí dentro — nunca un `if` regado por el
sistema.**
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

**RESULTADO (2026-08-26, sesión QAS/NWP1 viva) — T1 HECHO y probado:**

- Promesas 68 (sentido), 69 (mano), 70 (filas de árbol), 71 (lápiz), todas rojo→verde→sabotaje
  verificado. El despacho quedó en `MundoQueToca.cs`; `EscribirEnElFoco` en `SapGuiSurface`.
- El sentido solo trajo 12 puertas (todas de la toolbar): TODO el contenido de NWP1 son dos
  `GuiShell[Tree]`. Con las filas visibles como puertas (70): **12 → 35 puertas**, con el menú
  clínico real (Órdenes Clínicas, Admisiones, Censo Pacientes C.E., …).
- Cadena entera cerrada: homónimos con selectores exactos (`#node=vw00216`/`vw00324`) → cruce por
  identidad → pantalla nueva (`…/ssubVIEW_SCREEN:SAPLN_WP_INP_MOVEMENTS:0001`) → arista aprendida
  y verificada en Neo4j. La ubicación SAP distingue SUBPANTALLAS del mismo dynpro: grano fino.
- Escribir: `SystemFocus` no rastrea el campo de comandos (vive en la toolbar) → el lápiz escribe
  sobre LO ÚLTIMO PULSADO por Id y relee para comprobar. Batch [«comando» → «nwp1» → Enter]: 2 de
  3 a la primera (el 3º pidió desempate de «Continuar», legítimo).

**Deudas descubiertas, para sus fases:**
- CONTAMINACIÓN EN MEMORIA (2026-08-30, ronda 8): puertas de Claude («Menu», «Copiar»…) aparecen
  bajo NWP1 en el grafo EN RAM —nunca en Neo4j, que está limpio— por la carrera Estoy/Observar
  cuando el foco cambia en mala hora. Cura de hoy: reiniciar U restaura limpio. Causa raíz
  pendiente: emparejar cada observación con la ubicación leída EN EL MISMO instante.
- «Finalizar (Shift+F3)» CIERRA LA SESIÓN de SAP (salir del sistema) y su confirmación colgó un
  batch 120 s hasta desloguear al usuario (2026-08-30, mea culpa del agente). Los regresos van por
  «Atrás/Back», nunca por Finalizar; candidata a puerta marcada-peligrosa en el terreno.
- El árbol de SAP Easy Access da 0 filas visibles de 205 claves (geometría distinta) — mismo
  síntoma que tenía NWP1 antes del 70; pendiente de mirar su `ItemGeometry`.
- «Ponerse delante» de una sesión SAP falla («no pude ponerme delante de QAS») — falta el
  equivalente SAP de la promesa 25 (SAP direccionable: OpenConnection / okcd, no solo enfocar el
  Logon).
- «Atrás» desde una lista de NWP1 SALE de la transacción entera (a Easy Access): la compuerta paró
  honesta, y la vuelta necesita el comando. El terreno lo aprenderá como arista más.
- PELIGRO documentado: si aparece el diálogo de licencia («el usuario ya ha entrado al sistema»),
  JAMÁS elegir «finalizar entradas existentes» — mata sesiones ajenas con datos sin grabar. Se
  cancela y se avisa.
- **«DÓNDE ESTOY» LEE LA VENTANA EN PRIMER PLANO, y el pulsar SAP no la necesita** (2026-08-26,
  batch [«comando» → «NWP1» → «Continuar»]): el 3er paso pulsó el botón correcto por scripting —
  la sesión SÍ avanzó de SESSION_MANAGER a NWP1, confirmado leyendo la sesión directo por COM— pero
  el batch contestó «quedaste en uia://claude.exe/claude», porque `SurfaceLocator.Compute` solo
  pregunta a `SapGuiSurface.Identity()` cuando el PROCESO en primer plano empieza por «sap»
  (`IsSap(proc)`); con esta terminal delante, la sesión que SÍ se movió quedó invisible para
  «comprobar por consecuencia». Correcto para UIA (ahí no hay acción sin foco); FALSO para SAP
  scripting, que actúa sin necesitar la ventana al frente. Debe ser la primera promesa de la
  siguiente ronda: cuando hay sesión SAP viva Y la ubicación actual ya era `sapgui://`, su propia
  identidad manda sobre el proceso en primer plano — hasta que una persona visiblemente cambie a
  otra app real.

**Prueba real original (referencia):** SAP Logon abierto → entrar a una sesión → `map_what_i_see` lista los campos DE
VERDAD (no 12 elementos UIA del marco) → un `map_batch` de 2 pasos (okcd + Enter) cruza a una
transacción y fabrica la arista. Gotcha vigente: sondear COM con `cscript`/VBS, no PowerShell.

---

## T2 — El visor: pestaña «Terreno» (verte lo que construimos, en el archivo que ya te gusta)

**RESULTADO (2026-08-28) — T2 HECHO:**
- Promesas 75 (el árbol del visor: vivo/recordado/destino sin inventar) y 76 (rastro de batches en
  anillo con tope), rojo→verde→sabotaje verificado. `TerrenoParaElVisor` + `RastroDeBatches`.
- Rutas nuevas del 8792: `GET /terreno?niveles=1..3&desde=` (el árbol en JSON, camelCase) y
  `GET /batches` (las últimas corridas). El visor gana la pestaña «Terreno»: árbol sangrado con
  vivo en trazo lleno y recordado punteado, «por descubrir» dicho con palabras, y debajo los
  últimos batches con ✔/✘.
- VIVO SOLO DONDE ESTAMOS: la bandera del grafo es «estaba en la última observación de ESA
  pantalla»; el árbol la apaga fuera de la pantalla actual — pintarla sería prometer pantalla.
- MIRAR EL VISOR NO CAMBIA EL TERRENO: con el navegador delante, la pestaña recuerda la última app
  real y mira desde ella, rotulándolo «ÚLTIMA APP · …».
- Verificado contra el SAP real: el árbol pinta el menú clínico de la sesión (los pisos del
  hospital) con la arista aprendida «Atrás (F3) → SESSION_MANAGER·0100».


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

**RESULTADO (2026-08-28) — T3 HECHO y probado con el piloto:**
- Promesas 73 (se cuenta) y 74 (no inventa, no ahoga), rojo→verde→sabotaje verificado. La consulta
  vive en `TerrenoPorDelante.cs` (pura; la sirven el MCP hoy y el 8792 en T2). Los nombres cortos
  de SAP llevan la transacción («NWP1·0100») porque la cola sola era ambigua.
- El piloto la usó A LA PRIMERA para armar el batch profundo perfecto:
  [comando → nwp1 → Enter → fila del árbol] — 4 pasos, 2 pantallas, UNA llamada, éxito en 23 s.
- Hallazgo: el árbol de NWP1 es POR USUARIO — la predicción enseña memoria (que puede ser de otra
  cuenta) y la compuerta lo detecta en vivo («lo conozco pero AHORA no lo veo»). La predicción
  propone, el terreno dispone: funcionó exactamente así en la corrida 23✗ de la curva.

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
3. **El desempate por destino** (hallazgo del 2026-08-26): el menú clínico repite nombres —dos
   «Órdenes Clínicas», dos «Admisiones», dos «Censo Pacientes C.E.»— y hoy cada homónimo cuesta un
   viaje al modelo. Cuando el terreno recuerde a dónde lleva cada puerta, el desempate se hace por
   DESTINO (la escalera ya tiene ese peldaño: «pedir el destino vale como pedir la puerta») — sin
   preguntar, sin LLM, determinista. Los recuerdos enseñados (map_esto_es) quedan para cuando dos
   puertas homónimas van a destinos DISTINTOS y solo una persona sabe cuál es cuál.
4. Analizo qué le faltó al terreno para llegar más hondo (¿un dynpro sin nombrar bien?, ¿un campo
   que es puerta y está clasificado como contenido?, ¿una espera?) — se arregla con promesa.
5. Ronda siguiente: el batch debe llegar más hondo y la métrica bajar. Se apunta la curva en el
   plan (aquí) con fecha.

**Curva a llenar:**

| fecha | tarea | viajes | profundidad máxima de un batch | qué faltó |
|---|---|---|---|---|
| 2026-08-26 | NWP1: cruzar fila de árbol (manual por MCP) | n/a (sin piloto) | 2 pasos hechos de una llamada (comando+texto); 3º pidió desempate | escribir SAP (arreglado), desempate por destino, Easy Access sin filas |
| 2026-08-26 | SESSION_MANAGER→NWP1: [comando, escribir «NWP1», Continuar] (manual por MCP) | n/a (sin piloto) | 3 de 3 (homónimo de «Continuar» desempatado por selector exacto) — confirmado por scripting que el dynpro cambió | «dónde estoy» ciego a SAP fuera de foco: el batch reportó mal el destino aunque el cruce fue real |
| 2026-08-28 | NWP1+Cirugías, 2 ventanas SAP | 28 ✗ | — | identidad de la OTRA ventana → promesa 72 |
| 2026-08-28 | NWP1+Cirugías (árbol ajeno) | 23 ✗ | 3 de 4 | la fila era de OTRO usuario; parcial honesto ✓ |
| 2026-08-28 | NWP1+Favoritos, con map_ahead | **10 ✓** | **4 pasos / 2 pantallas** | — |
| 2026-08-30 | R1-R2 T4 (flujo triage de José, a cuatro ojos) | — | — | tres huecos arreglados: clic humano por selección (77), árbol sin columnas, rejilla ALV (78: 36→49 puertas), y la QUIMERA de transición (Busy no se acuña). El doble clic humano en NWP1 escribió su arista ✓ |
| 2026-08-30 | R3-R7: enseñar recorriendo (77b/c, 79, 26 + vista pegajosa, frescura del clic) | — | — | el clic humano YA escribe aristas: Easy→NWP1 y NWP1→vista:Triage nacieron de las manos de José |
| 2026-08-30 | **Piloto sobre terreno enseñado**: Triage→Easy→Triage | **12 ✓** | **3 pasos / 3 pantallas** | — |
| 2026-08-30 | R9: dos «Triage» (Adultos/Pediatría) — LA CARPETA ES PARTE DEL NOMBRE (81) + fila desplazada por identidad (80) | — | — | vista:Urgencias Adultos Triage; RutaDelNodo con cache |
| 2026-08-30 | **Flujo clínico completo por batch**: fila GIRALDO + botón Triage JUNTOS → pantalla del triage del paciente (SAPLY000) | — ✓ | **2 pasos de rejilla / 1 batch** | regla enseñada al piloto: fila+botón juntos (la selección caduca) |

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
