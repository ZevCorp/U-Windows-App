# TERRENO — génesis

**Este archivo es el prompt fundacional de un repositorio nuevo.** Ponlo como `CLAUDE.md` en la
raíz del repo y todo agente que trabaje aquí lo lee primero. Fecha: 2026-08-25. Autor de la visión:
José David. Redactado desde el prototipo `windows-app` (rama `jose/batch-resolver-estable`), donde
la tesis ya se probó con números.

---

## 0. Quién eres tú, el agente que construye esto

Construyes como Karpathy: **desde cero, entendiendo cada línea, el mínimo código que funciona.**
Nada de frameworks por costumbre, nada de capas "por si acaso", nada de copiar módulos enteros de
otro repo "porque ya funcionaban". Si un archivo tuyo pasa de ~300 líneas, sospecha. Si no puedes
explicar en una frase por qué existe una pieza, no existe todavía.

Estás creando algo que no existe: **el terreno aprendido que le permite a un agente de IA ejecutar
interfaces nativas en batch, a profundidad, sin mirar la pantalla entre paso y paso.** Nadie ha
hecho esto para Windows nativo. El DOM le dio esto a la web gratis; tú lo vas a fabricar para todo
lo demás.

**El lema del proyecto: nosotros creamos el terreno, el Agent SDK lo navega.**

---

## 1. La tesis (probada, no supuesta)

Computer use clásico es mirar→actuar→mirar: un viaje al modelo por cada clic, segundos por viaje.
El batch del Agent SDK (`computer_batch`, `browser_batch`) elimina los viajes **cuando el modelo
puede predecir la secuencia**. En Chrome puede porque el DOM le da el mapa de lo que hay y de lo
que habrá. En Windows nativo no hay DOM — y por eso el batch nativo hoy solo sirve para pantallas
que no cambian (una calculadora).

La esencia destilada del batch, que es la ley física de este proyecto:

> **velocidad = predecir N pasos + una compuerta barata y determinista antes de cada paso +
> parar honesto devolviendo el control cuando la realidad diverge.**

Nuestra respuesta: un **grafo-terreno** donde

- **nodo = ubicación** (una pantalla identificable: `sapgui://SID/TCODE/DYNPRO`, `uia://app/ventana`,
  `web://dominio/ruta`),
- cada ubicación guarda sus **elementos** con bandera **Vivo** (¿está en pantalla AHORA?) — ver un
  elemento vivo YA es una arista de alcanzabilidad,
- **arista de navegación = cruce ejecutado**: «pulsé este selector desde aquí y acabé allí». Las
  aristas **se ganan ejecutando**, nunca se deducen mirando ni se atribuyen a clics humanos
  (atribuir clics ajenos fue la fuente de aristas falsas del sistema viejo).

Sobre ese terreno corre **`map_batch`**: el modelo pide una lista de pasos; el código (no el
modelo) verifica antes de CADA paso que el elemento pedido está VIVO; si lo está, lo pulsa por
identidad y aprende la arista; si no, **para y devuelve un parcial honesto**: «hice N de M, paré en
el paso K porque X, estás en Y, y aquí veo vivo: A, B, C». El modelo replanifica con esa respuesta
sin gastar otra llamada de reconocimiento.

### Los números del prototipo (la prueba de que esto vale)

Tarea: «Ve al artículo de Ajedrez de Wikipedia, entra al portal asociado, vuelve a la portada por
el logo», conducida por el Agent SDK contra nuestro MCP:

| iteración | viajes al modelo | tiempo | resultado |
|---|---|---|---|
| terreno crudo, sin estabilidad | 28 | 7328 s | **FRACASO** (max turns) |
| + escalera de resolución, filtro de ruido, timeouts | 26 | 71.8 s | éxito |
| + web direccionable (ir directo por la URL) | **8** | **19.1 s** | éxito |

**LA MÉTRICA del proyecto es esa columna: viajes al modelo por tarea.** Cada mejora del terreno
debe bajarla. El techo teórico es 2–3: un viaje para planificar, uno o dos `map_batch`.

---

## 2. Lo nuevo que este repo añade: PROFUNDIDAD

El prototipo prueba pasos sobre lo vivo *ahora*. Este repo va más allá: **predecir estados
futuros**. Si el terreno sabe que desde la pantalla A, pulsar «Continuar» lleva a B, y sabe qué
elementos viven en B, entonces el modelo puede planear un batch que **atraviesa pantallas que aún
no están en pantalla**. La compuerta sigue verificando en tiempo de ejecución — la predicción
propone, el terreno vivo dispone.

Eso exige guardar, por cada arista cruzada, no solo el destino sino **qué se observó al llegar**.
Y exige una herramienta de consulta del terreno («¿qué habrá vivo después de pulsar X?») para que
el modelo planifique batches profundos.

El ciclo de trabajo será: **José David prueba a mano, tú analizas qué le faltó al terreno para
llegar más hondo, y amplías.** Cada ronda debe ampliar la profundidad alcanzable en batch.

### El banco de pruebas: SAP Logon

Las pruebas se hacen sobre SAP GUI, pero **la tecnología es agnóstica de interfaz** — lo que
funcione aquí sirve para cualquier app. SAP es el banco ideal porque:

- Sus pantallas tienen identidad natural: sistema/transacción/programa/dynpro
  (`sapgui://QAS/NWP1/SAPLN_WP_FRAMEWORK/0100`).
- Tiene su propio "DOM": la **Scripting API** (COM). UIA solo ve un Pane vacío dentro de SAP;
  el scripting ve todo, con Ids estables (`wnd[0]/usr/txtFIELD`, `wnd[0]/tbar[0]/okcd`).
  Verificado en esta máquina: `SapROTWr.SapROTWrapper` → `GetScriptingEngine` responde
  («MOTOR OK»). Gotcha conocido: sondear COM de SAP desde consola se hace con `cscript`/VBS,
  no con PowerShell (TYPE_E_CANTLOADLIBRARY).
- Sus flujos son deterministas y profundos (login → transacción → llenar campos → guardar):
  perfectos para medir profundidad de batch.

---

## 3. Arquitectura del repo nuevo

```
terreno/
  nucleo/        # EL GRAFO. C# puro: cero I/O, cero UI, cero reloj. Estoy/Observar/Cruzar/DesdeAqui.
  sentidos/      # LEER pantallas. Un adaptador por superficie: sap/ (Scripting COM), uia/ (después).
  manos/         # ACTUAR. Ejecutar por identidad (Id de SAP, selector UIA). Nunca coordenadas.
  recorredor/    # map_batch: la compuerta de vida + la escalera de resolución + el parcial honesto.
  puerta/        # MCP: protocolo JSON-RPC PURO en una clase, el cable HTTP en otra. 127.0.0.1.
  piloto/        # Agent SDK (TypeScript): el navegante. Mide LA MÉTRICA en cada corrida.
  visor/         # HTML estático que pinta el terreno leyendo un endpoint del daemon.
  contrato/      # LAS PROMESAS. Ejecutable de consola: compila, corre, veredicto. Sin framework.
  docs/          # este archivo + decisiones fechadas.
  PREGUNTAS.md   # el canal con el sistema viejo (ver §6).
```

### Tech stack (decidido, no abierto)

- **Núcleo, sentidos, manos, recorredor, puerta: C# / .NET 8 (LTS), console daemon.** Sin WPF, sin
  UI de escritorio. Razones: el COM de SAP y UIA son de primera clase en .NET; el código valioso
  del prototipo ya es C#; un daemon de consola arranca en CI y en cualquier máquina.
- **Piloto: TypeScript + `@anthropic-ai/claude-agent-sdk`.** Probado en el prototipo:
  `query(prompt, {mcpServers: {u: {type:"http", url:"http://127.0.0.1:8790/mcp"}},
  allowedTools:["mcp__u__*"]})`. Auth headless: `CLAUDE_CODE_OAUTH_TOKEN`.
- **Persistencia: UN archivo JSON del terreno** (`terreno.json`), escrito por el daemon. Nada de
  Neo4j ni bases de datos en v1 — el grafo cabe en memoria y un JSON se inspecciona con los ojos.
- **Contrato: ejecutable propio estilo `Prueba("1. ...", ...)` / `Debe(cond, "...")`** — cero
  dependencias de test, el contrato ES la especificación y se lee como tal.
- **Visor: HTML+JS estático, un solo archivo**, que lee `GET /terreno` del daemon. (El visor del
  prototipo — `nucleo/visor/index.html` — es sólido y gustó: extráelo y cámbiale la fuente de
  datos de Neo4j al endpoint del daemon.)

---

## 4. Las reglas del código (no negociables — todas costaron sangre en el prototipo)

1. **Promesas antes que código.** Toda conducta nueva nace como promesa en el contrato, se ve
   ROJA por el motivo escrito, se implementa, se ve verde, y luego se **sabotea** la implementación
   para verificar que la promesa muerde — y se verifica que el sabotaje SE APLICÓ (un sed que no
   casa por CRLF deja un verde que no prueba nada; `grep -c` después de cada sabotaje).
2. **Identidad, nunca coordenadas.** Se pulsa por Id de SAP o selector UIA. Las coordenadas fallan
   en silencio y reportan éxito.
3. **Comprobar por consecuencia.** Pedir no es llegar: después de cada acción se relee el mundo
   (¿cambió la ubicación? ¿apareció lo esperado?) antes de decir «hecho». Un plazo fijo se
   equivoca en las dos direcciones; se sondea y se sale en cuanto llega.
4. **Puro y cable, separados.** Lo que puede equivocarse en silencio (protocolo, decisiones de
   navegación, el grafo) vive en clases puras que el contrato juzga sin abrir una ventana. El
   HTTP, el COM y el UIA viven aparte y son delgados.
5. **Una pregunta se responde en UN sitio.** Dos copias se desincronizan en silencio, y la que se
   queda atrás es siempre la que menos se mira.
6. **La respuesta nunca miente y dice QUÉ HACER.** «Hice 3 de 5, paré porque X no está vivo, estás
   en Y, vivo aquí: A, B, C» — no «error». Distinguir «no lo conozco» de «lo conozco pero ahora no
   lo veo» de «sé llegar pero la puerta está tapada».
7. **Los porqués se escriben en el código, fechados.** Cada decisión no obvia lleva su historia en
   un remark: qué pasó, cuándo, qué se vio. El código es la memoria del proyecto.
8. **Respuestas de herramienta cortas.** El SDK manda a archivo los resultados >25k tokens y el
   modelo pierde el hilo. El parcial honesto lista máximo ~15 puertas vivas, las mejores primero.
9. **Una herramienta colgada no cuelga la puerta.** Timeout por herramienta en el protocolo,
   despacho paralelo en el cable. (Un `map_what_i_see` colgado 1014 s mató una corrida entera.)
10. **`main` = solo código real y estable.** Rama por feature con su prueba. Un portero (hook
    pre-push) corre el contrato completo y no deja pasar rojo. El contrato entero debe correr en
    segundos para que se corra siempre.

### El workflow diario

```
rama nueva → promesa ROJA (motivo verificado) → implementar → verde →
sabotear + verificar que aplicó → rojo → restaurar → verde →
prueba REAL contra SAP (tú mismo, por MCP o el piloto — nunca delegar la verificación) →
medir LA MÉTRICA si tocó el camino del batch → commit → portero → merge
```

Cierra cada sesión de trabajo nombrando **la siguiente prueba clave** hacia el objetivo: grabar y
ejecutar en batch 0→100, primero single-app, luego cross-app.

---

## 5. El mapa de fases

**F0 — esqueleto.** Repo, solución, contrato corriendo (aunque con 1 promesa), portero instalado,
daemon que arranca y contesta `ping` por MCP. *Prueba: el inspector MCP oficial lista herramientas.*

**F1 — sentidos SAP.** Leer la sesión viva por Scripting COM: acuñar la ubicación
(`sapgui://SID/TCODE/PROGRAMA/DYNPRO`) y los elementos interactivos con su Id estable. Enlace
tardío (reflexión, sin referencia a sapfewse.ocx) para compilar sin SAP. *Prueba: con SAP Logon
abierto, `map_where_am_i` dice la ubicación real y `map_what_i_see` lista los campos de verdad.*

**F2 — núcleo + recorredor.** El grafo (Estoy/Observar/Cruzar/DesdeAqui) y `map_batch` con su
compuerta de vida, la escalera de resolución (selector exacto → etiqueta viva exacta → difusa
entre puertas decentes → destino de arista), y el parcial honesto. *Prueba: un batch de 3 pasos en
SAP fabrica 3 aristas; un paso no vivo para el batch y lo cuenta.*

**F3 — puerta + piloto.** MCP JSON-RPC completo, piloto Agent SDK con la ruta predilecta
(`map_batch` primero), LA MÉTRICA impresa en cada corrida. *Prueba: una tarea SAP real 0→100
(login → transacción → campos → guardar) contada en viajes al modelo.*

**F4 — PROFUNDIDAD (la novedad).** Por cada arista, guardar lo observado al llegar. Herramienta de
consulta del terreno futuro. Batches que atraviesan pantallas aún no visibles. Rondas: José David
prueba, tú analizas qué faltó, amplías. *Prueba: cada ronda alcanza más hondo en un solo batch que
la anterior, y la métrica baja.*

Decisiones que se resuelven en su fase, no antes: forma exacta de `pasos` (¿`exit`+`text` bastan?),
cómo nombrar dynpros repetidos, cuándo un campo de texto es puerta y cuándo es contenido.

---

## 6. El protocolo de preguntas al sistema viejo

El prototipo (`windows-app`) tiene piezas valiosas y toneladas de historia que NO quieres heredar.
**No extraigas nada sin preguntar.** Hay un agente (Claude) con el prototipo entero en contexto;
José David le lleva tus preguntas y te trae respuestas con el código mínimo completo.

Mecánica: escribe tus preguntas en `PREGUNTAS.md`, numeradas, una pregunta concreta por ítem
(«¿cómo acuña el prototipo el selector UIA y qué casos raros cubre?», no «pásame el mapeador»).
Cuando llegue la respuesta, márcala resuelta con la fecha y la decisión tomada.

Inventario para orientarte — **vale la pena preguntar por**:

- `nucleo/Grafo/Grafo.cs` (~420 líneas, puro) — el grafo entero: la semántica de
  Estoy/Observar/Cruzar/DesdeAqui/ComoLlego y sus porqués. Es la mejor pieza del prototipo.
- `windows-client/src/Navigation/RecorrerSegunElNucleo.cs` — el batch: compuerta, escalera de
  resolución, filtro «ParecePuerta» (la web observa fragmentos de texto como elementos), homónimos,
  parcial honesto.
- `windows-client/src/Mcp/ProtocoloMcp.cs` + `ServidorMcp.cs` — JSON-RPC puro + cable HTTP, con
  timeout por herramienta y despacho paralelo. Casi listos para llevar tal cual.
- `mapeador/Pulso/ComoMePongoDelante.cs` — las decisiones puras de «ponerse delante» (25 promesas):
  web direccionable, «pedir el sitio» vs «pedir una página», esquema recordado.
- `windows-graph/src/Surfaces/SapGuiSurface.cs` (2954 líneas — **NO copiar entera**): pedir solo la
  lectura de sesión, la ejecución por Id, el OkCode y el patrón de enlace tardío COM.
- `piloto/piloto.mjs` — el sidecar del SDK con LA MÉTRICA.
- `nucleo/visor/index.html` — el visor que gustó; adaptarle la fuente de datos.

**Basura vieja — no preguntar, no extraer**: `SurfaceMap.cs` (2000+ líneas, el mapa congelado con
niveles/bronce-plata-oro), `MapaVivo.cs`, la atribución de clics humanos (ClickWatcher — fuente de
aristas falsas), todo lo WPF, la sonda 8791, workflows/teach/clinical.

---

## 7. Contexto de arranque que ya está verificado en esta máquina

- SAP Logon 800 instalado (`C:\Program Files\SAP\FrontEnd\SAPgui\saplogon.exe`) y el motor de
  scripting responde con el GUI abierto.
- `claude.exe` en `C:\Users\felip\.local\bin\`; `CLAUDE_CODE_OAUTH_TOKEN` instalado como variable
  de usuario (headless funciona).
- Puerto 8790 lo usa el MCP del prototipo — si conviven, el daemon nuevo usa otro (p. ej. 8890).
- PowerShell corrompe tildes al POSTear JSON como string (`artículo`→`art?culo`): mandar bytes
  UTF-8 o usar curl. Y las consolas Windows son cp1252: `PYTHONIOENCODING=utf-8` al inspeccionar.

El primer commit de este repo es este archivo. El segundo es F0. Empieza.
