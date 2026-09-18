# Clics en tiempo real: el humano planifica por la voz, Jev ejecuta, la carita va al lado

> Escrito el 2026-09-18 **midiendo el log de la corrida del dueño con Jev de verdad**
> (`C:\U-decisor\local\U\logs\u-20260918.log`, 03:30-03:35), la spec 029 (dónde se va el tiempo,
> en `main`), la spec 030 (el clic no se rechaza, rama `jose/el-clic-no-se-rechaza`) y la
> documentación de TypeSafe. Cada número lleva su hora o su archivo.
>
> **La experiencia que se busca**, en palabras del dueño: «que la carita flotante pueda moverse al
> lado de cada botón que cliquea y clicar muchos muy rápidamente», cambiando el rumbo por la voz
> mientras lo hace. Nivel Astra.
>
> **La decisión de alcance, también del dueño (2026-09-18):** en la v1 **el planificador es el ser
> humano, en tiempo real**. Luna se queda exactamente como hoy —recibe objetivos largos, habla, llama
> herramientas—: «lo que tenemos hoy funciona de maravilla, es el centro». El sistema de dos
> frecuencias (Luna re-planificando cada cierto tiempo sobre lo que Jev va haciendo) es un punto
> extra de desarrollo y de fallo, y **no entra** hasta que se mida que hace falta.

---

## 0. El diagnóstico en una tabla

Un clic con Jev, medido en tu corrida sobre openai.com (03:33-03:34):

| | ms | Quién |
|---|---|---|
| **Jev decide** (80, 65 y 65 puertas) | **582 · 186 · 245** | TypeSafe |
| `map_decidir` de punta a punta | **7.819 · 6.402 · 6.506** | nosotros |

**Jev es el 3-8 % del clic. El arnés es el resto.** Y no es una sorpresa nueva: la spec 030 midió
que un `map_take` normal cuesta 4,4-7,8 s y que **el clic es lo más caro de todo el sistema**
(1.249 s en tres días, más que cualquier otra cosa). Anatomía de uno (03:33:32 → 03:33:38, 5,8 s):

```
03:33:32  → map_take «Página de inicio de OpenAI» decir=… recuerdo=…
03:33:33     map_esto_es (el recuerdo, en paralelo)                    0,75 s
03:33:34–36  tarjeta + PAUSA DE LECTURA + señalar (coreografía 014)     ~2 s
03:33:36     resolver + Invoke por patrón                              <1 s
03:33:37     cambió la pantalla · foto del álbum (136 KB) · inventario  ~1,5 s
03:33:38  ← (5.812 ms)
```

Y entre clic y clic hay otro reloj que no es nuestro: **Luna piensa 3,0 s de mediana por vuelta**
(spec 029 §2). Hoy cada clic es una vuelta de Luna. Así que un clic «de tarea» cuesta 8-11 s aunque
Jev tarde 0,2.

**Lo que el nivel 4 con Jev enseñó además** (03:33:18, 03:33:47, 03:34:28): en **2 de 3** decisiones
la puerta elegida chocó con **homónimos** («hay 2 puertas vivas para «Investigación»: 1) Hyperlink,
2) Button») y el paso se perdió entero — 6-8 s para nada, y otra vuelta de Luna con `which`. Jev
elige por etiqueta, y la etiqueta no es única.

**Y el observador está saturado**: `mapa-vivo: SATURADO (lectura de pantalla): 22 vuelta(s)
descartadas` en mitad del paso (03:33:16). Hay más lectores que capacidad de leer (spec 029 §6).

## 1. Lo que TypeSafe permite y lo que no

Leído de `docs.typesafe.ai` el 2026-09-17/18:

| | |
|---|---|
| Transporte | **HTTP, petición-respuesta. No hay WebSocket ni streaming** en la API ni en los SDK. |
| Varias preguntas por llamada | **Sí**: un `state` y N `questions`; el cookbook «Parallel questions» mide **mismas respuestas, 12,2× más barato y 10× más rápido** que N llamadas. |
| Cupo | 1.200 peticiones/min (20/s), 250.000 tokens/s; «se mueven sin aviso». |
| Latencia medida por nosotros | 186-582 ms por decisión, con 65-80 puertas en el `state`. |
| Lo que devuelve un `choice` | la elegida **y la probabilidad de cada opción**: la segunda mejor viene gratis. |

Conclusión: **el «WebSocket» no se le pide a TypeSafe; se construye dentro.** Un bucle local que
mantiene el estado, le hace a Jev **una llamada por paso con tres preguntas** (qué puerta, si el
objetivo ya se cumplió, si la acción es peligrosa), y no vuelve a Luna hasta que el tramo termina
o se atasca. Eso es lo que convierte 20 llamadas/s de cupo en clics.

## 2. La arquitectura de la v1, en tres roles

```
 TÚ (voz, tiempo real)  ──intención / «para» / «mejor esto»──▶  LUNA (GPT-Live, como hoy)
                                                                     │  map_tramo(objetivo, tope)
                                                                     ▼
                                                        EL TRAMO (C#, bucle local)
                                             por paso: inventario → Jev (1 llamada, 3 preguntas)
                                                       → validar → pulsar por la escalera de map_take
                                                       → esperar el cambio suscrito (no dormir)
                                                       → la carita viaja al lado, sin bloquear
                                             para: objetivo cumplido · tope · duda · peligro · FRENO · bucle
                                                                     │  cuenta: N pasos, dónde, por qué paró, inventario
                                                                     ▼
                                                                   LUNA te lo dice; tú decides el rumbo
```

- **Tú** eres el planificador. Ves la pantalla y la carita; por la voz das el objetivo, y en
  cualquier instante «para», «no, mejor…», «¿y si…?».
- **Luna no cambia de naturaleza.** Sigue recibiendo objetivos largos y llamando herramientas.
  Gana **una** herramienta: `map_tramo`, que hace muchos clics de una llamada. Y sus instrucciones
  le dicen: para avanzar en pantalla, pide un tramo con el objetivo; no cliquees tú de uno en uno.
- **El tramo** es `map_batch` con el cerebro de Jev: `RecorrerSegunElNucleo` ya recorre N pasos con
  compuerta por paso, freno y relato (`SurfaceMapTools.Batch`); la diferencia es que **los pasos no
  vienen listados por Luna: los elige Jev en cada pantalla**, y el tramo sabe parar solo.
- **El rumbo en tiempo real lo pide la voz, no la transcripción.** Corrección del dueño
  (2026-09-18): la transcripción puede oír un «para» de fondo, y Live1 entiende el contexto y sabe
  cuándo NO parar; la voz ya es tiempo real, que frene ella. Así que el alto es **una herramienta
  que la voz llama** —`map_alto`— y que **salta la cola**: no espera a que el tramo termine, pone
  el mismo `Freno` que hoy dispara Escape (promesas 21-28) y el tramo se detiene en el paso en
  curso. Lo que esto exige, y se mide el primer día (fase 1): que la voz pueda emitir una llamada
  mientras la del tramo sigue pendiente. Si el protocolo no lo permite, el tramo pasa a correr
  **desprendido**: `map_tramo` contesta al instante «en marcha» y devuelve el turno, el tramo
  avanza por detrás contando cada paso, y la voz siempre tiene el turno libre para `map_alto`, para
  hablar contigo y para pedir el siguiente tramo. Es mi opción preferida aunque el protocolo lo
  permita: una voz que nunca está bloqueada es la experiencia Astra.

**Lo que NO cambia:** `map_take`, `map_type`, `map_go_to`, el mapa, las skills, la enseñanza. Con
`U_DECISOR` ausente, nada de esto existe y la app es la de hoy.

**Y se enciende en vivo, no solo por variable.** Pedido del dueño: un botón en el panel de la
carita —junto a `Learn`/`Work`, `FaceWindow.xaml:523`— que conmuta TypeSafe sin reiniciar. Por
dentro es lo que ya hace `Learn`/`Work`: cambiar `ConDecisor` y el `Decisor` del mapa, y volver a
mandar catálogo e instrucciones a la sesión con `CambiarModoAsync`. La variable fija el estado
inicial; el botón manda después. Y el botón dice en qué estado está: un verde sin haberlo medido
es peor que no tener botón.

**Lo que se gana en el arnés sirve a los dos ejecutores.** Observación del dueño, y es correcta:
las fases 0 y 4 acortan el clic de `map_take` igual que el del tramo — sin tarjeta ni pausa, sin
leer la pantalla dos veces, esperando el cambio suscrito. Con TypeSafe apagado, Luna ejecutando
también corre más. Por eso los números se miden en los dos caminos.

## 3. Qué necesitamos, por fases

Cada fase es una spec con promesas en rojo antes del código, sabotaje comprobado y nivel 4 en dos
pantallas. Orden por lo que compra dividido por lo que arriesga.

### Fase 0 — Juntar lo que ya está hecho (medio día)

- **La spec 030 a `main`** (rama `jose/el-clic-no-se-rechaza`, promesas 264-267): la compuerta
  mira otra vez, la escalera acaba en el ratón, **sin tarjeta ni pausa fuera de una comprobación**
  (los ~2-3 s de coreografía por clic), y «0 de N» se pinta como fallo. Es la mitad del clic.
- **La spec 035 a `main`** (esta rama, 275-286): el decisor, `map_decidir`, el interruptor.
- Los cortes 1' y 4a de la spec 029 (llegada juzgada sobre la ventana de trabajo, sin subdominios).

### Fase 1 — Medir el techo y despejar la duda que lo cambia todo (1 día)

- **El guion de anatomía del clic** sobre el log (de `→` a `←`, por fases: leer, coreografía,
  pulsar, esperar, inventario), corrido antes y después de cada fase. Sin esto no se sabe qué compró
  cada corte.
- **La duda:** ¿puede Luna oír «para» mientras `map_tramo` lleva 10 s corriendo? Con GPT-Live una
  llamada a herramienta deja el turno abierto (promesa 211). Si el servidor acepta audio nuevo con
  la llamada pendiente, el rumbo por voz llega; si no, el freno por transcripción es la única vía
  y hay que saberlo el primer día. Se mide con una sonda, no se deduce.
- **Fijar el modelo** a `jev-1.13.0` (el alias se mueve y los umbrales se calibran contra una
  versión) y medir la latencia de Jev con 20, 60 y 160 puertas: el `state` domina el coste.

### Fase 2 — Puertas únicas para Jev, la segunda mejor gratis, y el botón (1-2 días)

Lo que perdió 2 de 3 pasos en tu corrida.

- `map_decidir` (y después el tramo) le ofrece a Jev **puertas numeradas y únicas** —`3) Investigación
  (Hyperlink)`— sobre las que `map_take` ya sabe pulsar sin preguntar `which`. Nunca una etiqueta
  a secas. Promesa: «Jev elige entre puertas que se pueden pulsar sin ambigüedad».
- **La segunda mejor sin otra llamada**: si la elegida no está viva al ir a pulsarla, se prueba la
  siguiente por `probabilities` si supera el umbral. Cero latencia extra.
- **Una llamada, tres preguntas** por paso: `choice` puerta · `noul` «¿el objetivo ya está
  cumplido en esta pantalla?» · `noul` «¿accionar esta puerta es irreversible o peligroso?». Las
  tres vuelven juntas en ~300 ms.
- Solo etiquetas y tipos en el `state`, nunca valores de campos (el terreno es un hospital).
- **El botón Live** en el panel: enciende y apaga TypeSafe en vivo, re-manda el catálogo a la voz
  con `CambiarModoAsync`, y muestra el estado real. Promesa: «apagar por el botón deja el catálogo
  byte a byte como sin decisor, y encender lo trae, sin reiniciar».

### Fase 3 — El tramo: muchos clics de una llamada (2-3 días)

`map_tramo(objetivo, tope_pasos, decir)`, sobre `RecorrerSegunElNucleo`:

- Por paso: inventario → Jev → validar (en la lista, confianza ≥ umbral, no vetada, no peligrosa
  sin permiso) → pulsar por **la misma escalera de `map_take`** → esperar el cambio → siguiente.
- **Para solo** cuando: Jev dice que el objetivo se cumplió (y la pantalla cambió como para
  creerlo), se agota el tope, la confianza no llega, la acción es peligrosa (Grabar, Enviar,
  Eliminar: los vetos de hoy), se pide el **freno**, o **se repite la misma puerta 3 veces** (el
  detector de bucle que el diagnóstico pide desde el 7 de septiembre).
- Devuelve la cuenta en el idioma de hoy: «hice 7 de hasta 15, estoy en X, paré porque Y», más el
  inventario (promesa 263), para que Luna te lo cuente y tú decidas.
- **La carita viaja al lado de cada puerta en paralelo** (promesa 240: ≤450 ms), sin que el paso
  la espere. Hoy el paso espera a la coreografía; en el tramo la coreografía acompaña.
- Cada decisión, con su confianza, al log `decisor:` — también las descartadas: el umbral se
  ajusta con esos datos.

Riesgo principal: un tramo largo sobre la pantalla equivocada. Por eso el tope, el detector de
bucle, el freno y el veto de lo irreversible son promesas, no opciones.

### Fase 4 — Esperar es suscribirse: el observador único (3-4 días, el más caro)

Es el «WebSocket» de verdad, y ya está diseñado en la spec 029 §6. Hoy tres observadores sondean
por su cuenta, diez sitios preguntan al crudo, esperar es «preguntar y dormir» con presupuesto de
1.800 ms (`EsperaMaximaMs`), y el `SATURADO` es la consecuencia.

- **Un solo sitio lee la pantalla y publica** ubicación + versión + evento. Preguntar es leer un
  campo; esperar a que cambie es esperar la versión siguiente, **despertando en el instante del
  cambio** y no al siguiente sondeo. Mientras alguien espera, acelera a ~120 ms; en reposo, 800.
- **El tramo no lee la pantalla dos veces por paso**: usa la última lectura publicada si es de
  esta versión, y espera al cambio suscrito tras pulsar. Los ~1 s antes y ~1 s después del clic se
  van, y el 1,8 s de «¿cambió?» pasa a durar lo que tarde en cambiar.
- Lo que se conserva, dicho: la regla de la ventana de delante, la identidad de SAP con su `Busy`,
  la compuerta **antes** de actuar mirando la ventana de trabajo ahora (spec 020).

Toca diez clases y el localizador; por eso va cuarta y no primera, y por eso se mide antes (fase 1)
cuánto queda por ganar cuando las fases 2 y 3 ya están.

### Fase 5 — El rumbo por la voz, en tiempo real (1-2 días)

- **`map_alto`, llamado por la voz y fuera de cola**: Live1 decide cuándo parar —entiende el
  contexto; la transcripción no—, la llamada no espera a que el tramo termine, pone el `Freno` y el
  tramo para en el paso en curso y devuelve dónde quedó. Nunca por transcripción ni por energía.
- **Cambiar el rumbo**: lo que dices después llega a Luna como turno nuevo; Luna para el tramo si
  hace falta y pide otro con el objetivo nuevo. Con el tramo desprendido (fase 3), la voz nunca
  está bloqueada para esto.
- **El notch** cuenta el paso que va (ya lo hace por `Accion`), para que veas el rumbo sin que Luna
  hable.

### Después, y solo si se mide — Luna como planificadora por tramos

El sistema de dos frecuencias (Luna partiendo la intención en secciones y corrigiendo cada N pasos)
se construye **sobre** `map_tramo` sin tocarlo: es el mismo tramo con un tope corto y una Luna que
pide el siguiente. La hipótesis se mide con la misma tarea, N corridas, tope grande contra tope
corto: pasos por segundo, clics equivocados, segundos hasta el objetivo, re-planes. Hasta entonces,
el planificador eres tú, que es más fiable que cualquiera de los dos modelos (diagnóstico §1.5: la
voz es la pieza menos fiable del sistema).

## 4. Lo que se espera ganar, y cómo se sabrá

| | Hoy (medido) | Estimado tras 2+3 | Estimado tras 4 |
|---|---|---|---|
| Un clic, de pedirlo a hecho | 4,4-7,8 s | ~2-2,5 s (sin coreografía, sin homónimos) | ~0,8-1,5 s |
| Vueltas de Luna por clic | 1 (3 s) | 1 por tramo | 1 por tramo |
| 10 clics hacia un objetivo | ~90-110 s | ~25-30 s | ~10-15 s |

Son estimaciones sacadas de restar fases medidas, **no promesas**: la fase 1 las convierte en
números antes/después, y **si un corte no mueve segundos de herramienta, vueltas del modelo o
segundos en llamadas que acabaron en nada, no era un corte** (spec 029 §7). Y como la varianza entre
corridas es material, cada número sale de N corridas, no de una.

## 5. Decisiones tomadas, para que no se vuelvan a discutir

| Decisión | Por qué |
|---|---|
| El humano planifica en la v1; Luna no cambia | Pedido del dueño; es lo que hoy funciona; menos piezas que fallen |
| Sin WebSocket a TypeSafe | Su API no lo tiene; el bucle continuo se construye dentro |
| Jev no planifica, no habla, no llama herramientas | Su API solo acepta preguntas cerradas; medido contra la documentación |
| Una llamada por paso, tres preguntas | Mismas respuestas que tres llamadas, 10× más rápido (cookbook de TypeSafe) |
| Puertas numeradas y únicas, nunca etiquetas | 2 de 3 pasos perdidos por homónimos en la corrida real |
| El alto lo pide la voz por herramienta, fuera de cola; nunca por transcripción ni por energía | Un «para» de fondo no debe frenar, y Live1 sí entiende el contexto (dueño, 2026-09-18); la energía es ciega en esta máquina (medido) |
| TypeSafe se enciende por variable Y por un botón en vivo | Pedido del dueño; el botón muestra el estado real |
| Lo irreversible sigue vetado dentro de un tramo | Es un hospital; el tramo hereda los vetos de `map_take`, no los relaja |
| `U_DECISOR` ausente = la app de hoy | Nada de esto se enciende solo |

## 6. Lo que no se sabe todavía

- Si la voz puede emitir `map_alto` mientras la llamada del tramo está pendiente (fase 1). Si no,
  el tramo corre desprendido y devuelve el turno al instante; mi preferencia es esa en los dos casos.
- Cuánto tarda Jev con 160 puertas: el `state` domina el coste y SAP llega a 39 campos + 21 botones.
- Si Jev elige *bien* sobre SAP. Tres decisiones en openai.com (0,97 · 0,90 · 0,79 de confianza,
  las tres correctas) son tres, no una medida. Hace falta el hospital delante.
