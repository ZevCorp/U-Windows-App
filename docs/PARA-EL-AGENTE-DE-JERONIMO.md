# Contexto para el agente de código de Jerónimo

**Este archivo es una ventana de contexto.** Léelo entero antes de tocar nada. Describe el enfoque
de la rama `jose/batch-resolver-estable` del repo `ZevCorp/U-Windows-App`, qué está probado, qué
no, y las reglas que hacen que este código se pueda tocar sin romperlo.

Tu humano (Jerónimo) trae su propia rama de investigación sobre el mismo objetivo —**ejecución por
batch de un agente de IA sobre interfaces nativas**— con avances propios. Tu trabajo es entender
esta rama a fondo, decidir con criterio propio qué combinar, y seguir. Puedes bifurcar esta rama o
traer piezas a la suya; ambas opciones son válidas y la decisión es de él.

Fecha del traspaso: 2026-08-26. Último commit de la rama: `4c4c977`.

---

## 1. La tesis, y la ley física del proyecto

Computer use clásico es mirar→actuar→mirar: **un viaje al modelo por cada clic**, segundos cada
uno. El batch (`computer_batch`, `browser_batch`) elimina esos viajes **cuando el modelo puede
predecir la secuencia**. En Chrome puede, porque el DOM le da el mapa de la página. En Windows
nativo no hay DOM, así que el batch nativo hoy solo sirve para pantallas que no cambian.

> **velocidad = predecir N pasos + una compuerta barata y determinista antes de cada paso +
> parar honesto devolviendo el control cuando la realidad diverge.**

Nuestra respuesta es fabricar el mapa: un **grafo-terreno** que se construye usándose.

- **Nodo = ubicación** (una pantalla identificable): `sapgui://QAS/NWP1/SAPLN_WP_FRAMEWORK/0100`,
  `uia://app.exe/ventana`, `web://dominio/ruta`. En SAP la identidad baja hasta la **subpantalla**
  (`…/0100/ssubVIEW_SCREEN:SAPLN1AU:0004`), lo que da un grano mucho más fino que el dynpro.
- **Cada ubicación guarda sus elementos con bandera `Vivo`**: ¿está en pantalla AHORA, o solo lo
  recuerdo? Nunca se mezclan. Ver algo vivo YA es alcanzabilidad, y con eso basta para actuar.
- **Arista de navegación = cruce ejecutado**: «pulsé este selector desde aquí y acabé allí». Se
  gana **ejecutando**, jamás deduciendo ni atribuyendo clics humanos (eso ya se intentó: producía
  aristas falsas, y el vigilante de clics quedó degradado a mejor-esfuerzo).

Encima corre **`map_batch`**: el modelo manda N pasos; **el código** verifica antes de CADA paso
que lo pedido está vivo, lo pulsa **por identidad** y aprende la arista; si algo diverge, para y
devuelve un **parcial honesto** («hice N de M, paré en el paso K porque X, estás en Y, vivo aquí:
A, B, C») con el que el modelo replanifica sin gastar una llamada de reconocimiento.

**LA MÉTRICA del proyecto: viajes al modelo por tarea.** Toda mejora debe bajarla.

---

## 2. Arquitectura y mapa de archivos

```
sidecar Agent SDK (TS)  ──MCP streamable HTTP──►  U.exe (WPF, .NET 8)
piloto/piloto.mjs            127.0.0.1:8790/mcp     ├─ ServidorMcp + ProtocoloMcp
query() + allowedTools                              ├─ SurfaceMapTools (catálogo map_*)
mide LA MÉTRICA                                     ├─ RecorrerSegunElNucleo (map_batch)
                                                    ├─ MundoQueToca (EL DESPACHO)
                                                    ├─ Nucleo.Grafo (el terreno)
                                                    └─ ProyectorNeo4j → visor
```

### Lo que vale oro (léelo, cópialo, extiéndelo)

| archivo | qué es |
|---|---|
| `nucleo/Grafo/Grafo.cs` | **El terreno. ~420 líneas, puro**: sin I/O, sin UI, sin reloj. `Estoy` / `Observar` / `Cruzar` / `DesdeAqui` / `ComoLlego`. La mejor pieza del repo; sus comentarios explican cada decisión con la fecha en que se pagó. |
| `windows-client/src/Navigation/MundoQueToca.cs` | **EL DESPACHO ENTRE MUNDOS.** `SentidoPorMundo` (leer), `ManoPorMundo` (pulsar), `EscribirPorMundo` (escribir), `SentidoSap` (traducir SAP→núcleo). Puro: recibe delegados y solo decide. |
| `windows-client/src/Navigation/RecorrerSegunElNucleo.cs` | **`map_batch`**: la compuerta de vida, la escalera de resolución, el filtro `ParecePuerta`, los homónimos y el parcial honesto. |
| `windows-client/src/Navigation/PulsarSegunElNucleo.cs` | Pulsar UNA cosa, comprobar por consecuencia, y que el núcleo aprenda. 88 líneas. |
| `windows-client/src/Mcp/ProtocoloMcp.cs` + `ServidorMcp.cs` | JSON-RPC 2.0 puro + el cable HTTP. Timeout por herramienta y despacho paralelo por petición. |
| `mapeador/Pulso/ComoMePongoDelante.cs` | Decisiones puras de «ponerse delante» (25 promesas propias, contrato aparte). Web direccionable, sitio≠página, esquema recordado. |
| `windows-graph/src/Surfaces/SapGuiSurface.cs` | Superficie SAP por Scripting COM con **enlace tardío** (reflexión, sin referencia a `sapfewse.ocx`, compila sin SAP). 3.000 líneas: **no la leas entera**, ve a `Identity()`, `ReadVisibleElements()`, `VisibleTreeRows()`, `Execute()`, `EscribirEnElFoco()`. |
| `windows-graph/src/Surfaces/CualSesion.cs` | Decisión pura: cuál de las sesiones SAP es «la nuestra» (la de la ventana de delante). |
| `windows-graph/src/Surfaces/SapSelector.cs` | El vocabulario de identidad de SAP: `sap:wnd[0]/…`, con fragmentos `#node=` (fila de árbol), `#tbbtn=` (botón de toolbar), `#row=` (fila de ALV, por pares columna=valor). |
| `tests/ContratoDelGrafo/Contrato.cs` | **Las 72 promesas.** Es la especificación ejecutable, y se lee como tal. |
| `nucleo/visor/index.html` | El visor del terreno (lee Neo4j). Un solo archivo, claro/oscuro, sólido. |
| `piloto/piloto.mjs` | El sidecar del Agent SDK que imprime LA MÉTRICA. |
| `docs/plan-terreno-profundo.md` | **El plan vivo**: fases T1–T4, resultados fechados y la curva de la métrica. |

### Lo que NO debes tocar ni imitar

- `windows-client/src/Navigation/SurfaceMap.cs` (2.000+ líneas): el mapa VIEJO, con niveles y
  bronce/plata/oro. Está congelado y lo estamos dejando atrás. Convive porque muchas herramientas
  `map_*` aún cuelgan de él, pero **el terreno nuevo es `Nucleo.Grafo`**.
- El `ClickWatcher` / atribución de clics humanos: fuente histórica de aristas falsas.
- La sonda del puerto 8791 (`McpDevProbe`): shim de desarrollo, no habla MCP de verdad.
- Todo lo de workflows/teach/clinical: otra época del producto.

---

## 3. Reglas no negociables (todas costaron sangre; están fechadas en el código)

1. **El despacho entre mundos vive en UN SOLO SITIO** (`MundoQueToca.cs`). El grafo, el batch, la
   compuerta y el MCP **no saben** en qué mundo miran, pulsan o escriben. Cada superficie nueva es
   un adaptador más ahí dentro — **nunca un `if` regado por el sistema**. Esta la pidió José David
   explícitamente y es la que hace que mañana entre Citrix, o Java, o lo que sea.
2. **Promesas antes que código.** Escribe la promesa, **verifica que está roja por el motivo que
   dice**, implementa, ponla verde, **saboteala** y comprueba que vuelve a rojo, restaura. Un
   sabotaje que no muerde significa que la promesa no vale.
   *Trampa medida*: `sed`/`python` con patrones `\n` fallan en silencio sobre archivos CRLF y
   dejan un verde que no prueba nada. **Siempre `grep -c` después de sabotear.**
3. **Identidad, nunca coordenadas.** Selector UIA o Id de scripting SAP. Las coordenadas fallan en
   silencio y reportan éxito.
4. **Comprobar por consecuencia.** Pedir no es lograr. Tras cada acción se relee el mundo (¿cambió
   la ubicación? ¿el campo dice lo que escribí?) antes de decir «hecho». Se sondea y se sale en
   cuanto llega; un plazo fijo se equivoca en las dos direcciones.
5. **Puro y cable, separados.** Lo que puede equivocarse en silencio (el protocolo, las decisiones
   de navegación, el grafo) vive en clases puras que el contrato juzga **sin abrir una ventana**.
   El HTTP, el COM y UIA viven aparte y son delgados.
6. **Una pregunta se responde en UN sitio.** Dos copias se desincronizan en silencio y la que se
   queda atrás es siempre la que menos se mira.
7. **La respuesta nunca miente y dice QUÉ HACER.** Distinguir «no lo conozco» de «lo conozco pero
   ahora no lo veo» de «sé llegar pero la puerta está tapada». Nunca «error».
8. **Respuestas de herramienta cortas** (≤15 puertas, las mejores primero). El SDK manda a archivo
   los resultados >25k tokens y el modelo pierde el hilo.
9. **Los porqués se escriben en el código, fechados.** Cada decisión no obvia lleva su historia:
   qué pasó, cuándo, qué se vio. El código es la memoria del proyecto.
10. **Verifícalo tú.** Nunca «pruébalo y me dices». Si hay forma de comprobarlo desde aquí, se
    comprueba desde aquí.

---

## 4. Cómo correr todo (comandos exactos, Windows)

**El contrato** (compila el núcleo, compila el contrato contra esos binarios, juzga):

```bash
cd C:\Users\felip\OneDrive\Documentos\Code\windows-app; .\scripts\contrato-del-grafo.ps1
```

El contrato del mapeador va aparte:

```bash
cd C:\Users\felip\OneDrive\Documentos\Code\windows-app; dotnet run --project mapeador\Contrato
```

**Compilar y relanzar la app** (hay que reiniciar `U.exe` o pruebas el binario viejo):

```bash
dotnet build windows-client\WindowsClient.csproj -c Release -o C:\U-versiones\v1\bin --nologo -v quiet -nodeReuse:false
```

Luego matar `U.exe` y arrancar `C:\U-versiones\v1\bin\U.exe`.

**Hablarle al MCP a mano** (la forma más rápida de probar algo). Puerto `8790`:

```bash
curl -s -X POST http://127.0.0.1:8790/mcp -H "Content-Type: application/json; charset=utf-8" --data-binary @cuerpo.json
```

Herramientas útiles: `map_where_am_i`, `map_what_i_see`, `map_batch`, `map_go_to`,
`map_routes_from`, `map_open_app`.

**Ver el terreno**: `http://127.0.0.1:8792/nucleo` (JSON de dónde estás y qué alcanzas), o el
visor `nucleo/visor/index.html` contra Neo4j en `127.0.0.1:7474` (`neo4j` / `grafo-local-2026`).

**Limpiar el terreno antes de una prueba** (regla de la casa: las pruebas del mapa empiezan de
cero, si no el grafo con historia esconde lo que quieres medir):

```bash
curl -s -X POST http://127.0.0.1:7474/db/neo4j/tx/commit -u neo4j:grafo-local-2026 -H "Content-Type: application/json" -d "{\"statements\":[{\"statement\":\"MATCH (n) DETACH DELETE n\"}]}"
```

…y reiniciar `U.exe` (la restauración de arranque no encontrará nada).

**El piloto** (necesita `CLAUDE_CODE_OAUTH_TOKEN` como variable de usuario):

```bash
cd C:\Users\felip\OneDrive\Documentos\Code\windows-app\piloto; node piloto.mjs "la tarea en lenguaje natural"
```

Imprime la traza de llamadas y, al final, **LA MÉTRICA**.

**Sondear SAP a mano**: con `cscript` y VBS, **no con PowerShell** (PowerShell da
`TYPE_E_CANTLOADLIBRARY` con el COM de SAP). Hay scripts de sondeo en el scratchpad de la sesión;
son cinco líneas: `SapROTWr.SapROTWrapper` → `GetROTEntry("SAPGUI")` → `GetScriptingEngine`.

---

## 5. Gotchas ya pagados (no los repitas)

- **Encoding, dos veces.** PowerShell corrompe tildes al mandar JSON como string
  (`artículo`→`art?culo`) y produjo un falso «no lo conozco» que casi reporto como bug del
  producto. Manda bytes UTF-8 o escribe el cuerpo a archivo y usa `--data-binary @archivo`. Y las
  consolas Windows son cp1252: `PYTHONIOENCODING=utf-8` al inspeccionar.
- **CRLF y los sabotajes** (regla 2). Ya mordió: un contrato verde que no probaba nada.
- **UIA no ve dentro de SAP.** Es un `Pane` opaco. Para SAP: scripting o nada.
- **Una herramienta colgada colgaba la puerta entera.** `map_what_i_see` se quedó 1014 segundos y
  todo lo que llegó detrás murió en cadena. Arreglado con timeout por herramienta
  (`ProtocoloMcp.TiempoMaximoDeHerramienta`) + `Task.Run` por petición en `ServidorMcp`.
- **PELIGRO REAL, no técnico:** si SAP muestra el diálogo de licencia diciendo que el usuario ya
  tiene sesiones abiertas, **JAMÁS elegir «finalizar las entradas existentes»** — mata sesiones de
  otras personas con datos sin grabar. Se cancela y se avisa.
- El servidor QAS (`10.158.82.204:3200`) **no responde sin la VPN del hospital**.

---

## 6. Estado exacto: qué está hecho y qué sigue

### Hecho y probado (promesas 63–72, todas verdes con sabotaje verificado)

| # | promesa | qué resolvió |
|---|---|---|
| 63 | pedir el DESTINO vale tanto como pedir la puerta | el modelo pide «Portal:Ajedrez» y el terreno resuelve qué puerta lleva ahí |
| 64 | dos puertas al mismo destino no se adivinan | se pregunta, no se elige al azar |
| 65 | la basura de la web ni reclama pasos ni cuenta como puerta | las webs exponen fragmentos de texto («,», «[1]») como elementos; se filtran |
| 66 | una web es DIRECCIONABLE | sin camino aprendido se va por la URL en vez de rendirse |
| 67 | una herramienta colgada no cuelga la puerta MCP | timeout por herramienta + despacho paralelo |
| 68 | cada mundo se OBSERVA por su propia puerta | SAP por scripting; el marco de SAP Logon sigue por UIA |
| 69 | cada mundo se PULSA por su propia mano | **el selector decide** (`SapSelector.Owns`), incluidos los fragmentos |
| 70 | en SAP el contenido navegable son las FILAS del árbol | 12 → 35 puertas; solo las **visibles** (Vivo), no las 1197 claves cargadas |
| 71 | cada mundo se ESCRIBE por su propio lápiz | en SAP el texto va al campo por identidad y se relee; teclear era mandar letras al aire |
| 72 | la sesión SAP es la de la ventana DELANTE | con dos ventanas SAP se estaba leyendo la equivocada |

**Medido en Wikipedia** (Agent SDK contra nuestro MCP, misma tarea): 28 viajes y fracaso →
26 viajes / 71,8 s → **8 viajes / 19,1 s**. Sin tocar modelo ni prompt: solo terreno.

**Medido en SAP** (QAS/NWP1, sesión real, disparado a mano por MCP): sentido y mano funcionando;
homónimos resueltos con selectores exactos; cruce real de una fila de árbol; **arista aprendida y
verificada en Neo4j**; batch `[comando → escribir «nwp1» → Continuar]` con los 3 pasos hechos.

### La siguiente promesa (73), ya diagnosticada y sin escribir

**«Dónde estoy» está ciego a SAP cuando SAP no está en primer plano.**
`SurfaceLocator.Compute` solo le pregunta a `SapGuiSurface.Identity()` si el proceso en primer
plano empieza por «sap» (`if (IsSap(proc))`). Eso es correcto para UIA —ahí no hay acción sin
foco— y **falso para SAP scripting**, que actúa sin necesitar la ventana al frente.

Consecuencia medida: un batch pulsó correctamente y **la sesión SÍ avanzó** de `SESSION_MANAGER` a
`NWP1` (confirmado leyendo la sesión directo por COM), pero el batch contestó «quedaste en
`uia://claude.exe/claude`» porque la terminal estaba delante. El cruce fue real y el terreno no se
enteró: se pierde la arista y se rompe «comprobar por consecuencia».

La regla propuesta: **cuando hay sesión SAP viva y la ubicación actual ya era `sapgui://`, su
propia identidad manda sobre el proceso en primer plano** — hasta que una persona cambie
visiblemente a otra app real.

### Deudas abiertas

- El árbol de **SAP Easy Access da 0 filas visibles de 205 claves** (geometría distinta a la de
  NWP1). Mismo síntoma que tenía NWP1 antes de la promesa 70; mirar su `ItemGeometry`.
- **«Ponerse delante» de una sesión SAP falla** («no pude ponerme delante de QAS»): falta el
  equivalente SAP de la promesa 25 — SAP es direccionable (`OpenConnection`, el campo de comandos
  con `/n<TCODE>`), no solo «enfocar el Logon».
- **«Atrás» desde una lista de NWP1 sale de la transacción entera** a Easy Access. La compuerta
  paró honesta; volver necesita el comando. El terreno lo aprenderá como una arista más.
- **Los homónimos cuestan un viaje al modelo.** El menú clínico repite nombres (dos «Órdenes
  Clínicas», dos «Admisiones»). Cuando el terreno recuerde a dónde lleva cada puerta, el desempate
  se hace **por destino, determinista y sin preguntar** — la escalera ya tiene ese peldaño
  (promesa 63). Los recuerdos enseñados a mano quedan para cuando dos homónimas van a destinos
  distintos y solo una persona sabe cuál es cuál.

### Lo que falta construir (el plan, en `docs/plan-terreno-profundo.md`)

- **T2 — visor**: pestaña «Terreno» en `nucleo/visor/index.html` con el último batch dibujado, el
  árbol de profundidad desde donde estás (vivo = trazo lleno, recordado = punteado) y la curva de
  LA MÉTRICA. Se alimenta de dos endpoints nuevos en el 8792.
- **T3 — `map_ahead`** (*la pieza que falta para la profundidad*): servirle al modelo **lo que
  habrá después** de cada paso, desde la memoria del grafo, para que planifique batches profundos
  en un solo viaje. La compuerta no se relaja: `map_ahead` propone con memoria, `map_batch`
  verifica con vida.
- **T4 — rondas**: tarea SAP real 0→100 por el piloto, medir, analizar qué le faltó al terreno,
  ampliar. Cada ronda debe llegar más hondo y bajar la métrica.

---

## 7. Cómo empezar

1. `git fetch origin && git checkout jose/batch-resolver-estable`
2. Corre el contrato y confirma que ves «CONTRATO INTACTO» con 72 promesas.
3. Lee, en este orden: `Grafo.cs` → `RecorrerSegunElNucleo.cs` → `MundoQueToca.cs` →
   `plan-terreno-profundo.md`.
4. Compara con lo que Jerónimo ya tiene en su rama y decidan qué combinar. Si algo de aquí les
   sirve, tráiganlo con sus promesas — el contrato es lo que hace que el trasplante sea seguro.
5. Si van a extender el terreno a otra superficie, el punto de entrada es `MundoQueToca.cs` y
   **solo** `MundoQueToca.cs`.

Cuando decidan algo que contradiga una de las reglas de la sección 3, que sea a propósito y
escrito: esas reglas son cicatrices, no preferencias.
