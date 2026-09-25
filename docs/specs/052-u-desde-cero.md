# Plan de implementación: un ciclo de Ü cabe en medio segundo

Estado: **implementado; falta el nivel 4 a mano (hablarle, con micrófono y altavoz)** · noche del 2026-09-24 · Nace de los tres audios del dueño del 2026-09-24 · Rama: `jose/u-desde-cero`

## Qué es Ü, sin nada técnico

Un asistente que flota en la pantalla y está quieto hasta que se le habla. Se le habla por voz, en tiempo
real, y hace lo que se le pida en el computador con el ratón y el teclado, como lo haría una persona. Desde
«haz clic ahí» hasta «organízame la declaración de renta ante la DIAN»: la diferencia es cuánto planea.

```
voz (OpenAI Realtime)  →  Luna (planea)  →  Jev (ejecuta sobre los accionables de la pantalla)
```

**El ciclo**: dónde estoy → qué accionables hay → Jev elige → clic con el ratón real → otra vez.
**Meta 200 ms por ciclo; techo de la v1, 500 ms. Ninguna fase puede tardar un segundo.**
Premisa: si una persona hace clic y ve lo que cambió en menos de medio segundo, el sistema también.

## Diagnóstico: qué se midió (desde cero, sin métricas viejas)

`main` en `334f144`, compilado en Release, corrido en esta máquina el 2026-09-24 22:42–22:43. Sonda:
[`sondas/DelCiclo`](../../sondas/DelCiclo/Programa.cs), que habla con el U.exe vivo por su MCP (8790) y
decide con el mismo `ElDecisor` + `ClienteTypeSafe` que la app. Cinco tareas (Bloc de notas, Configuración
×2, Explorador, Calculadora). Datos crudos: `%TEMP%\u-medicion\ciclo-main.jsonl`; log `u-20260924-instalada-p7548-*.log`.

| Fase del ciclo | main hoy | Lo que cuesta la primitiva en crudo | Veredicto |
|---|---|---|---|
| **Dónde estoy** (`map_where_am_i`) | 48 – 590 ms | **0,29 ms** (GetForegroundWindow + pid + título) | harness: ×200–2000. Se rehace. |
| **Qué veo** (`map_what_i_see`, UIA con caché) | 108 – 313 ms (4–88 puertas) | la misma lectura; ~26 ms sobre una ventana vacía | se conserva el concepto (una petición con caché), se hace más barata |
| **Decide Jev** (TypeSafe, en caliente) | **200 – 263 ms** típicas; 625 ms la primera (en frío) | — | está a la altura: se conserva; se calienta al arrancar |
| **Pulsar** (`map_take`) | **290 – 4.628 ms**; la mano 470–540 ms + esperar el cambio **1.814 ms** (12 sondeos de «dónde», se agota el techo de 1.800) | **0,46 ms** SetCursorPos + SendInput | el cuello de botella. Se tira y se rehace. |
| **Abrir app** (`map_open_app`) | 560 – **13.995 ms**, y dos de cinco veces **no trajo la app al frente** | — | fuera del ciclo, pero se rehace |
| **Ciclo total** | 419 ms (cuando nadie pulsa) – **5.176 ms** (cuando pulsa) | | |

Lo que la medición dice además, y no es latencia:

1. **La espera mira lo que no cambia.** «Esperar el cambio» sondea *dónde estoy*; en Configuración, pasar
   de Inicio a Sistema no cambia la ventana, así que agota los 1.800 ms y reporta «no cambió» aunque
   cambió. Ese es el «se demora al pulsar el clic» que se veía a ojo.
2. **La mano no es el ratón.** Escalera de gestos (patrón UIA → mensaje → ratón suavizado) con esperas
   fijas de 20–200 ms entre peldaños: 470–540 ms para lo que el ratón real hace en medio milisegundo.
3. **El foco se pierde.** `map_open_app` dejó delante `claude.exe` y el «dónde estoy» le creyó: Jev tuvo
   que decidir entre puertas de otra app, y con razón no se atrevió (confianzas 0,26–0,59).
4. **Homónimos.** «Sistema» (Button) y «Sistema» (ListItem): la mano se paró a preguntar cuál. Elegir por
   número de puerta, no por etiqueta, lo resuelve de raíz.

## La arquitectura nueva (`u/`)

Proyecto aparte, en blanco. De `main` se copia **el concepto** de lo que está bajo 400 ms (una lectura UIA
con caché; Jev con elección numerada) y **nada** de lo que está encima (la mano, la espera, el localizador
con temporizador, el MCP como camino del ciclo).

| Pieza | Qué hace | Presupuesto |
|---|---|---|
| `Ubicacion` | ventana de delante, su proceso y título, por Win32 directo | < 1 ms |
| `Accionables` | UIA por **COM crudo** (IUIAutomation), UNA `FindAllBuildCache` con la condición de «accionable» del lado del proveedor, en un hilo MTA dedicado | < 100 ms |
| `Jev` | TypeSafe con conexión viva (keep-alive) y calentada al arrancar; elige un **número** | ~200–260 ms (no es nuestro) |
| `Raton` | el ratón real: SetCursorPos + SendInput abajo/arriba, sin suavizado ni esperas | < 1 ms |
| `Asentado` | tras el clic no se espera a ciegas: se relee hasta que la huella de los accionables cambie o pasen 150 ms | ≤ 150 ms |
| `Ciclo` | encadena lo anterior, mide cada fase y **marca el ciclo que pase de 500 ms** | ≤ 500 ms |
| `Luna` | convierte lo que se dijo en un plan de objetivos cortos (Responses API, `gpt-5.6-luna`) | fuera del ciclo |
| `Voz` | OpenAI Realtime: oye, transcribe, habla | fuera del ciclo |
| `App` | la burbuja flotante, quieta hasta que se le habla | — |

## La especificación

Promesas de `u/Contrato/Contrato.cs`, 430-454. Se numeran desde la 430 (las de `main` llegan a la 423). Cada enunciado es el que está literal en el contrato.

| # | Promesa | Fase |
|---|---|---|
| 430 | «Dónde estoy» se contesta sin UIA: ventana, proceso y título salen de Win32, y la propia burbuja de Ü nunca es «dónde estoy». | 1 |
| 431 | Un accionable se identifica por su número en la lista de ESTE ciclo; dos accionables con la misma etiqueta son dos números distintos. | 1 |
| 432 | Lo que no se ve no se ofrece: fuera de pantalla, sin tamaño o deshabilitado no entra en la lista. | 1 |
| 433 | El clic cae en el centro del accionable, en coordenadas de pantalla, y es un clic del ratón real (abajo + arriba, botón izquierdo). | 2 |
| 434 | La respuesta de Jev se valida entera: un número que no se ofreció, una confianza bajo el umbral, «cumplido» alto o «peligro» alto = no se pulsa, y se dice cuál. | 3 |
| 435 | El cuerpo que se le manda a Jev lleva la pantalla, el objetivo y las puertas numeradas, y nunca la clave. | 3 |
| 436 | La espera tras el clic termina en cuanto cambia la huella de los accionables, y nunca pasa de su techo (150 ms). | 4 |
| 437 | Cada ciclo deja sus cinco tiempos (dónde, ver, decidir, pulsar, asentar) y su total, y se marca FUERA DE PRESUPUESTO si pasa de 500 ms. | 4 |
| 438 | Un plan de Luna se lee a objetivos ejecutables; un plan vacío o ilegible no ejecuta nada y lo dice. | 5 |
| 439 | Un paso del plan que no se ejecutó deja rastro (`Omitido`); el denominador del resultado es el plan. | 5 |
| 440 | El ciclo para al primer «cumplido», al tope de pasos, o cuando Jev repite la misma puerta tres veces sin cambio. | 5 |
| 441 | Escape detiene todo en el ciclo siguiente, sin pulsar nada más. | 5 |
| 442 | Jev sabe lo que ya se hizo: el estado lleva, en orden, lo que ya se pulsó para este objetivo. | 5b |
| 443 | La huella ve los textos: si lo único que cambia es lo que dice la pantalla, la huella cambia. | 5b |
| 444 | Solo cuentan como emergentes las ventanas que pertenecen a la de delante: la barra de tareas no es un menú del Explorador. | 5b |
| 445 | Un paso con prefijo es un gesto directo y no le pregunta a Jev: «abre:», «escribe:» y «tecla:». | 6 |
| 446 | El plan se ejecuta en orden y para en el primer paso que falla; los que quedan salen Omitidos, y cada objetivo sabe lo que hicieron los pasos anteriores. | 6 |
| 447 | Lo que se le devuelve a Luna cabe en 30.000 bytes: si no cabe, se recorta y se dice cuánto se mandó de cuánto. | 6 |
| 448 | La voz abre con session.start en gpt-live-1 y Luna como delegada con «hacer» y «mirar»; una llamada se atiende UNA vez aunque llegue tres, y su resultado vuelve con su call_id y pide turno. | 6 |
| 449 | Una pantalla sin accionables se relee hasta 1 s antes de rendirse: una app que acaba de abrir todavía no pintó. | 7 |
| 450 | El Escape que pulsa Ü no es el freno de la persona: solo frena un Escape que Ü no mandó. | 7 |
| 451 | Si Jev dice que pulsar la elegida cumple el objetivo y la pantalla cambia al pulsarla, el objetivo termina sin otra llamada; si no cambia, se vuelve a preguntar. | 7 |
| 452 | El micrófono se calla solo mientras Ü suena de verdad: el siseo que el servidor manda entre frases no lo calla. | 7 |
| 453 | Tras una tecla que navega (Enter) se espera a que la pantalla cambie, hasta 1,5 s; tras cualquier otra, hasta 150 ms. | 7 |
| 454 | La primera entrada a Luna lleva, además del pedido, lo que hay delante ahora: no gasta un turno en mirar. | 7 |

La que cierra el asunto es la **437**: sin ella el presupuesto es una opinión.

## Las fases

| Fase | Resultado | Promesas |
|---|---|---|
| 0 | medir `main` desde cero (hecho, arriba) | — |
| 1 | ubicación y accionables baratos | 430–432 |
| 2 | el ratón real | 433 |
| 3 | Jev | 434–435 |
| 4 | el ciclo medido | 436–437 |
| 5 | Luna planea; el ciclo obedece al plan | 438–441 |
| 5b | lo que enseñó el primer banco: historia, textos, emergentes propias | 442–444 |
| 6 | el ejecutor del plan, la voz (GPT-Live + Luna delegada) y la burbuja | 445–448 + nivel 4 a mano |
| 7 | lo que enseñaron las corridas reales: pantalla que aún pinta, Escape propio, un clic que termina, siseo, Enter, primera entrada | 449–454 |

## Lo que queda fuera

- SAP (Scripting COM). El ciclo nuevo es UIA; SAP entra después con su propia pieza de accionables.
- El grafo/Neo4j y todo lo aprendido: la v1 no recuerda, decide cada vez. Es lo que la hace ligera.
- `main` no se toca: `u/` vive al lado y no cambia ningún comportamiento de U.exe.

## Bitácora

Horas sacadas de los commits y de los archivos del banco, no de memoria.


- **22:42** — medición de `main` hecha (tabla de arriba). Pulsar 3–4,6 s; la espera se come 1,8 s.
- **22:47** — promesas 430-441 escritas y ROJAS (12 pendientes). Commit `232ea21`.
- **22:51** — núcleo `u/Nucleo` en verde 12/12. Sabotaje por diff en 7 promesas, las 7 rojas (el primer
  sabotaje de la 441 quitaba solo uno de los dos frenos y siguió verde: se rehízo).
- **22:52** — **banco 1 sobre el PC real** (`u/Banco`, mismas 5 tareas que main): mediana con clic
  **373 ms**, máx 485; 1 de 14 vueltas fuera de presupuesto. Pero solo 1 de 5 tareas cumplida: Jev no
  sabía lo ya hecho (abría y cerraba «Archivo»), la huella no veía el texto de la Calculadora, y el
  Explorador leía la barra de tareas (mismo proceso, 60 accionables, 319 ms).
- **22:56** — promesas 442-444 rojas → verdes; sabotaje por diff de las tres, rojas (la primera de la 442 no
  compilaba y no juzgó: se rehízo con una que compila).
- **22:55** — **banco 2 sobre el PC real**: **4 de 5 tareas CUMPLIDAS** (Bloc de notas, Explorador →
  Documentos, Calculadora 7×8, Sistema → Pantalla). Mediana con clic **432 ms**, máx 724; 4 de 14 fuera de
  presupuesto — dos por Jev (617 y 972 ms) y dos por un asentado que espera a una app que aún carga
  (406-462 ms). Bluetooth: llegó, pero Jev dudó (0,58) en vez de decir «cumplido».

### main contra u, mismas cinco tareas, mismo PC

| | main (`334f144`) | u (banco 2) |
|---|---|---|
| dónde estoy | 48-590 ms | 0,02 ms |
| ver | 108-313 ms | 31-67 ms (0 cuando la reusa del asentado) |
| decidir (Jev) | 200-625 ms | 177-972 ms (mediana 236) |
| pulsar | 290-4.628 ms | 1,5-61 ms (mediana 11) |
| asentar | incluido en pulsar: 1.814 ms | 28-462 ms (mediana 128) |
| **vuelta con clic** | **3.579-5.176 ms** | **mediana 432 ms** |
| tareas cumplidas | 0 de 5 (Sistema llegó, sin «cumplido») | **4 de 5** |
- **22:59** — promesas 445-448 (ejecutor del plan, recorte para Luna, protocolo de GPT-Live) rojas → verdes;
  sabotaje por diff de las cuatro, rojas. Contrato de u: 19/19.
- **23:01** — **punta a punta sin voz** (`U-nuevo.exe --hacer`): «abre el bloc de notas y escribe: hola, soy Ü».
  Luna planeó en 2.778 ms `abre: notepad → escribe: hola, soy Ü`; el ejecutor lo hizo en 412 ms; Luna resumió en
  2.217 ms. Total 5,7 s, de los que el ciclo son 0,4: **el tiempo ahora es de Luna, no del harness**.
- **23:02-23:05** — **la voz contra el servidor real** (`--voz-prueba`, sin micrófono ni altavoz): `session.started`
  en 310-742 ms; Luna (delegada) llamó a `mirar` y `hacer`, se recuperó sola de un paso fallido, y la compuerta de
  peligro paró «Cerrar Calculadora» (0,56). Sin audio de entrada la voz no produce eventos `session.output_*`: lo
  que Luna escribe llega como `response.output_text.done`, y es lo que se cuenta. **La voz con altavoz y
  micrófono queda para la corrida a mano (nivel 4).**
- **23:05** — umbral de Jev 0,45 (no 0,70 como main): con 36 botones Jev eligió «Nueve», el correcto, con 0,52.
- **23:07** — promesa 449 (pantalla vacía = todavía no pintó) roja → verde; sabotaje: roja.
- **23:09** — **batería de punta a punta** (`scripts/bateria-u.ps1`, 6 pedidos): 6 de 6 cumplidos. Pero un Escape
  que mandó Ü frenó a Ü (4 pasos omitidos) y el Bloc de notas recibió «Ãœ» porque el .ps1 no tenía BOM.
- **23:10** — promesa 450 (el Escape de Ü no es el freno de la persona) roja → verde; sabotaje: roja. Contrato 21/21.
- **23:12** — abrir una app ahora incluye esperar a que se pueda leer (la primera lectura en frío costaba 351-469 ms
  dentro del primer ciclo). **Batería: 6 de 6 cumplidos · vuelta con clic mediana 343 ms, máx 691 · 2 de 18 fuera de
  presupuesto**, las dos por asentar ~290 ms mientras Configuración y el Explorador pintan la página nueva.

### Dónde está hoy cada fase (batería de las 23:12)

| Fase | Mediana | Qué la domina |
|---|---|---|
| dónde estoy | 0,0-0,2 ms | nada: Win32 |
| ver | 0 ms (reusa el asentado) / 23-160 ms | UIA; en frío ya no cuenta (va en «abre:») |
| decidir | ~210 ms (180-350) | **Jev**: el suelo del ciclo, y no es nuestro |
| pulsar | ~15 ms | SetCursorPos + SendInput; el resto es el hilo |
| asentar | ~30 ms en la misma pantalla, ~290 ms al navegar | lo que tarda la app en pintar la página nueva |
| **vuelta con clic** | **343 ms** | |

**Lo que falta para la meta de 200 ms**: Jev solo ya se come ~210. Para bajar de ahí haría falta decidir varios clics
por llamada (un plan de clics para la misma pantalla) o no preguntar «¿cumplido?» en una vuelta aparte — hoy cada
objetivo gasta una llamada de Jev más solo para confirmar que terminó (~200 ms).
- **23:14** — promesa 451 (Jev dice en la misma llamada si ese clic termina el objetivo) → **mediana con clic 261 ms**.
- **23:16-23:21** — **la burbuja, con el ratón real y el micrófono real**: se pinta (captura), un clic abre la voz
  (`session.started`), y se cierra sola tras 90 s de silencio. Dos fallos encontrados así y arreglados:
  el `DragMove()` atrapaba el clic (la burbuja no despertaba) y Ctrl+Alt+Espacio lo ocupa otra app (error 1409,
  y el fallo era mudo: ahora cae a Ctrl+Mayús+Espacio y lo dice). Y uno grave: **881 deltas de siseo del servidor
  en 90 s, 0 con voz**; la compuerta del eco los tomaba por voz y habría dejado el micrófono mudo para siempre
  (promesa 452).
- **23:22** — Chrome: busca el clima en Medellín y lo lee (17 °C, nublado). Luna repetía la búsqueda porque miraba
  25 ms después del Enter (promesa 453): 17,2 s → 9,1 s.
- **23:25** — `reasoning.effort` de Luna: `none` 1,6-1,9 s, `low` 1,3-1,7 s por turno; `minimal` no existe para
  Luna. Se queda en `low`: su tiempo es red y generación, no razonamiento.
- **23:27** — promesa 454 (Luna recibe lo que hay delante con el pedido). Contrato de u **25/25**, cada una con su
  sabotaje verificado por diff. **Batería de 7 pedidos: 7 de 7 cumplidos**, 5-15 s de principio a fin (de los que
  el ciclo es < 3 s y el resto es Luna); vuelta con clic mediana 296 ms (261-343 entre corridas). Lo que pasa de
  500 ms: picos de Jev (600-709 ms) y asentados de 320-430 ms mientras la app pinta la página nueva.

## Cómo verificarlo por la mañana

```powershell
cd .claude\worktrees\u-desde-cero
.\scripts\contrato-u.ps1          # 25 promesas, sin pantalla ni red, ~30 s
.\scripts\bateria-u.ps1           # 7 pedidos reales de punta a punta; MUEVE EL RATÓN: no tocar el equipo
dotnet build u\App\App.csproj -c Release -o $env:TEMP\u-nuevo-bin
& $env:TEMP\u-nuevo-bin\U-nuevo.exe   # la burbuja, abajo a la derecha
```

**Nivel 4, lo único que no se pudo hacer de noche: hablarle.** Clic en la burbuja (o Ctrl+Mayús+Espacio), esperar
el azul, y decir por ejemplo «abre la calculadora y calcula 45 por 12». Se mira en
`%LOCALAPPDATA%\U-nuevo\logs\u-AAAAMMDD.log`: `🗣` lo que oyó, `🌙 Luna →` lo que planeó, `⏱` cada vuelta, `Ü:` lo
que contestó, y al cerrar `N delta(s) de salida, M con voz` (M > 0 = sonó). Lo que no se sabe hasta hacerlo: si la
voz habla (sin audio de entrada no dijo nada) y si el eco por turnos basta con altavoces.

También: `U-nuevo.exe --hacer "pedido"` (sin voz), `--voz-prueba "pedido"` (voz real sin audio), `--plan "p1" "p2"`.

## Lo que queda abierto

1. **Jev es el suelo**: ~210 ms de mediana, con picos de 600-1.800 ms. Para la meta de 200 ms habría que decidir
   varios clics de una misma pantalla en una sola llamada.
2. **El asentado al navegar** (320-430 ms) es relectura mientras la app pinta. Escuchar eventos de UIA en vez de
   releer podría recortarlo; no se midió.
3. **Luna** tarda 1,3-3 s por turno y es la mayor parte del tiempo de un pedido. Está fuera del ciclo, pero la
   persona lo nota.
4. **Chrome** abre a veces el selector de perfiles y Luna elige el de la persona: funciona, pero es un paso que
   nadie pidió.
5. **SAP** no está: el ciclo nuevo es UIA puro. SAP necesita su propia pieza de accionables (Scripting COM).
6. **La voz**: eco por turnos (no se le puede hablar encima), y la sesión se cierra a los 90 s de silencio.
