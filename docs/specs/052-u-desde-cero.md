# Plan de implementación: un ciclo de Ü cabe en medio segundo

Estado: **en curso (noche del 2026-09-24)** · Nace de los tres audios del dueño del 2026-09-24 · Rama: `jose/u-desde-cero`

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

Promesas de `u/Contrato/Contrato.cs`. Se numeran desde la 430 (las de `main` llegan a la 423).

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
| 6 | la voz y la burbuja | nivel 4, a mano |

## Lo que queda fuera

- SAP (Scripting COM). El ciclo nuevo es UIA; SAP entra después con su propia pieza de accionables.
- El grafo/Neo4j y todo lo aprendido: la v1 no recuerda, decide cada vez. Es lo que la hace ligera.
- `main` no se toca: `u/` vive al lado y no cambia ningún comportamiento de U.exe.

## Bitácora

- **22:42** — medición de `main` hecha (tabla de arriba). Pulsar 3–4,6 s; la espera se come 1,8 s.
- **23:05** — promesas 430-441 escritas y ROJAS (12 pendientes). Commit `232ea21`.
- **23:10** — núcleo `u/Nucleo` en verde 12/12. Sabotaje por diff en 7 promesas, las 7 rojas (el primer
  sabotaje de la 441 quitaba solo uno de los dos frenos y siguió verde: se rehízo).
- **23:12** — **banco 1 sobre el PC real** (`u/Banco`, mismas 5 tareas que main): mediana con clic
  **373 ms**, máx 485; 1 de 14 vueltas fuera de presupuesto. Pero solo 1 de 5 tareas cumplida: Jev no
  sabía lo ya hecho (abría y cerraba «Archivo»), la huella no veía el texto de la Calculadora, y el
  Explorador leía la barra de tareas (mismo proceso, 60 accionables, 319 ms).
- **23:20** — promesas 442-444 rojas → verdes; sabotaje por diff de las tres, rojas (la primera de la 442 no
  compilaba y no juzgó: se rehízo con una que compila).
- **23:22** — **banco 2 sobre el PC real**: **4 de 5 tareas CUMPLIDAS** (Bloc de notas, Explorador →
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
