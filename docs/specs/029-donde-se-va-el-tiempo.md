# Dónde se va el tiempo — la ejecución medida como un solo sistema

Estado: **fase 1 en main (#79); fase 2 en curso** · 2026-09-17 · Ramas: `jose/llegar-es-llegar`,
`jose/los-actos-cuentan-lo-que-dejan`

> El dueño: «quiero que cambiemos el enfoque de medición… cuáles son los pilares que, si los mejoramos,
> mejoran muchísimo… y qué es ruido en medio que se puede simplificar o eliminar, cuidando lo que ya
> funciona». Y sobre el grafo: «creo que el asistente puede estar buscando rutas pregrabadas que hoy
> no funcionan y sólo nos consumen tiempo… pero no lo sé, tú tienes los logs, no te sesgues».

Este documento es la medición, en el orden en que se hizo, con los números de tres días de corridas
reales del dueño (`C:\U-clic`, logs del 15, 16 y 17 de septiembre) y de su última prueba —Instagram
y Google Docs, 10:23–10:28 del 17— que corrió ya con las specs 025 a 028 dentro. Al final está lo que
recomiendo hacer, por orden de lo que compra cada cosa y lo que arriesga.

## 1. El corte que hace falta: actos, saberes y aprender

Las herramientas de Ü no son de la misma especie, y medirlas juntas es lo que hasta ahora despistaba:

- **Actos** —pulsar, escribir, abrir, desplazar, ir— son lo único que hace avanzar una tarea.
- **Saberes** —dónde estoy, qué hay, mirar, señalar— no avanzan nada: existen para que el siguiente
  acto sea el correcto.
- **Aprender** —`map_esto_es`— no avanza ni informa: guarda para la próxima vez.

| Día | Llamadas | Tiempo de herramienta | Actos | Saberes | Aprender |
|---|---|---|---|---|---|
| 15 | 289 | 945 s | 55% | 38% | 7% |
| 16 | 252 | 631 s | 53% | 38% | 9% |
| 17 | 82 | 369 s | 45% | 26% | 28% |
| **última prueba** | 30 | **32 s** | 59% | 26% | 15% |

## 2. El hallazgo que cambia el problema: ya no es nuestro código

En la última prueba —treinta llamadas para abrir dos chats de Instagram, resumir un vídeo, crear un
documento y escribirlo— **el tiempo de herramienta fue 32 segundos**. La mediana de una llamada, entre
0,4 y 0,7 s. Lo que había alrededor:

| | |
|---|---|
| Nuestras herramientas | 32 s |
| Huecos entre llamadas en los que **el usuario hablaba** (enseñando, pidiendo) | 95 s |
| Huecos en los que **sólo el modelo** pensaba | 70 s (mediana 3,0 s por vuelta) |

Con las specs 025–028 puestas, **nuestro código es alrededor del 20% del reloj**. Por Amdahl, aunque
las herramientas costaran cero, la tarea bajaría un quinto. El resto es el número de vueltas que el
modelo da y lo que tarda en cada una. **La palanca grande ya no es acelerar un clic: es que hagan
falta menos clics y menos preguntas.**

Esto no invalida lo hecho: el 15 la herramienta era el 44% del reloj y `map_go_to` costaba 16 s de
mediana en fallar. Se arregló lo que había que arreglar primero. Ahora toca otra capa.

## 3. La hipótesis del grafo, contestada con números

Pregunta del dueño: ¿la búsqueda de un camino pregrabado consume tiempo sin usarse?

**`map_go_to`, tres días, 79 llamadas, qué pasó de verdad:**

| Desenlace | n | Tiempo total | Mediana |
|---|---|---|---|
| «no hay ningún camino aprendido» | **21** | **315 s** | 11–16 s |
| directo («te puse delante») | 56 | 78 s | 0,5–1,3 s |
| camino del grafo, recorrido paso a paso | **1** | 2 s | 2 s |
| ya estaba | 2 | 0 s | — |

El camino aprendido se usó **una vez en tres días**. Así que sí: en la conversación por voz, la ruta
pregrabada casi nunca sirve. **Pero no es ella lo que cuesta.** Consultar el grafo es una búsqueda en
memoria, milisegundos. Lo que cuesta son los 21 «no hay camino» a 15 s cada uno, y se miró uno por uno
qué había pasado en ellos:

| Mecanismo | Casos | Ejemplo |
|---|---|---|
| **Llegó, pero la comparación es demasiado estricta** | ≥ 8 | pidió `web://www.google.com`, llegó a `web://google.com`; pidió `…/document/u/0/`, llegó a `…/document/u/0` (una barra final); pidió `…/search?q=…`, llegó a `…/search`; pidió `ycombinator.com`, llegó a `apply.ycombinator.com` |
| **El bucle vigilaba la ventana equivocada** | ≥ 6 | pidió `google.com` y se activó la pestaña «Google Sheets» por el título; pidió `chatgpt.com` estando delante otra ventana, y la llegada se vio sólo después de agotar el plazo |
| Sondas mías con un id inventado | 3 | no cuentan |

`EstarEnElSitioBasta` sólo admite «mismo host» cuando el destino es un sitio a secas; con cualquier
ruta exige igualdad exacta, y el navegador normaliza rutas, quita `www.`, redirige a subdominios y
pierde la query. **En todos esos casos el usuario ya estaba donde quería** y Ü declaró fracaso tras
agotar el presupuesto, y el modelo gastó otra vuelta en descubrir que sí había llegado.

**Conclusión:** no hay que quitarle al asistente de voz la ruta del grafo —cuesta nada y el día que
haya rutas aprendidas será lo más rápido que tenemos—. Hay que arreglar **cómo se decide que se
llegó**: una comparación tolerante (host sin `www.`, subdominio del mismo sitio, la ruta pedida como
prefijo de la ruta real, sin barra final ni query) y salir en cuanto cuadre. Eso convierte 15 s en
menos de uno en la mayoría de los 21 casos, sin tocar el grafo.

## 4. Los otros dos sitios donde nuestro código sigue tirando tiempo

**`map_open_app` espera 8 segundos a una ventana que no va a aparecer.** Medido: 103 s el 15, 60 s el
16, 55 s el 17 (mediana 10,7 s). Por dentro (`FaceWindow.xaml.cs:663`): tras abrir, si la ventana de
trabajo sigue siendo la misma, un bucle de **40 vueltas × 200 ms más una enumeración de ventanas**
espera a que aparezca una nueva; cuando la app ya estaba abierta y se trajo al frente —que es lo que
pasa con `instancia=existente`— esa ventana nueva no existe, y se agotan los 8 s para contestar «ya
estaba abierta». Es el **sexto bucle con presupuesto ficticio** (la spec 025 contó cinco), y además
con el disparador al revés: sólo tiene sentido esperar cuando se *lanzó* algo.

**Un acto no cuenta lo que dejó delante, y el modelo tiene que preguntarlo.** Tres días, 214 actos:
**el 50% van seguidos inmediatamente de un «¿y ahora qué hay?»** (`map_what_i_see` o
`map_where_am_i`): 106 vueltas del modelo que existen sólo porque `map_type`, `map_go_to` y
`map_open_app` contestan «escribí X» o «te puse delante» sin decir qué hay ahora. `map_take` ya dice
«ahora estás en Y» (promesa 202) pero tampoco lo que se ve. Cada vuelta evitada son 3 s de modelo,
que es la parte que no podemos acelerar de otra forma.

**Lo que no es palanca, medido:** `map_what_i_see` repetido en la misma pantalla en menos de 8 s pasó
6 veces en tres días (no vale la pena); `map_type` de 4,3 s eran 581 caracteres tecleados en un campo
mudo, proporcional y honesto; `map_esto_es` va detrás de que el usuario enseña algo, y los huecos de
14–17 s que lo preceden son el razonador componiendo el recuerdo, no nuestro código.

## 5. Lo que recomiendo, en orden

Cada corte va con su promesa, su sabotaje y su medición antes/después, como todo lo demás. Lo
ordeno por lo que compra dividido por lo que arriesga:

| # | Corte | Compra | Riesgo | Toca |
|---|---|---|---|---|
| 1 | **Llegar es llegar**: comparación tolerante de la llegada web, y salir en cuanto cuadre | los 21 «no hay camino» (315 s / 3 días) pasan de 11–16 s a menos de 1 s en la mayoría | bajo: una función pura de comparación, juzgable sin pantalla | `ComoMePongoDelante.EstarEnElSitioBasta`, `PasoDelNucleo` |
| 2 | **`map_open_app` no espera a lo que no lanzó**: el bucle de la ventana nueva sólo si hubo lanzamiento, y con reloj | 55–103 s/día; la mediana de 10,7 s baja al coste real de traer al frente | bajo: es el sexto bucle de la spec 025 | `FaceWindow.xaml.cs:640–663` |
| 3 | **Los actos cuentan lo que dejaron delante**: `map_type`, `map_go_to`, `map_open_app` devuelven la misma línea compacta que `map_what_i_see`, dentro del tope de 32 KB | hasta 106 vueltas del modelo / 3 días (~3 s cada una); en la última prueba, 11 de 30 llamadas | medio-bajo: añade información a respuestas que ya existen; no cambia qué se hace | los tres constructores de respuesta y la promesa 202 ampliada |
| 4 | **La pestaña por dominio, no por título**: `google.com` no activa «Google Sheets» | parte de los ≥ 6 casos de ventana equivocada; y evita cambiar la ventana de trabajo por error | bajo: es la regla de nombres en una dirección (`LoNombra`) que el batch ya usa | `PestanasAbiertas` |
| 5 | **El catálogo de apps no se rescanea en cada apertura** | la parte fija de `map_open_app` que queda tras el corte 2 | bajo: una memoria con caducidad | `AppsDelSistema.Todas` |

Lo que **no** recomiendo ahora, y por qué:

- **Quitar la ruta del grafo del asistente de voz.** Cuesta milisegundos y es el único camino que
  un día será instantáneo. Lo caro era el fracaso en reconocer la llegada, que es el corte 1.
- **El observador único** (abajo). Es la forma correcta, pero con nuestro código en el 20% del reloj
  compra menos de lo que parece y toca diez clases y el localizador. Queda documentado para cuando
  los cortes 1–5 estén dentro y se vuelva a medir.
- **Recortar esperas.** Las que quedan verifican por consecuencia; acortarlas es cambiar lo prometido.

## 6. Lo que se deja escrito para el futuro: el observador único

Leído entero, el sistema de «dónde estoy» tiene esta forma, y no es la que uno querría:

- **Tres observadores periódicos que no se alimentan entre sí.** `SurfaceLocator` sondea cada 800 ms
  con una compuerta barata (mismo hwnd y título ⇒ no recalcula), publica `Current` y un evento
  `Changed` que **sólo la carita escucha**. `MapaVivo` sondea cada 250–4000 ms por su cuenta llamando
  al localizador crudo. El álbum de miradas cuelga del segundo.
- **Unos diez sitios que preguntan por su cuenta**, la mayoría al crudo: `Pulsar`, `Recorrer`,
  `Abrir`, `Paso`, `Aqui`, `LoQueSenalas`, `MundoQueToca`, `ServidorDelNucleo`, `SurfaceMapTools`,
  `AgentLoop` y una quincena de llamadas sueltas en `FaceWindow`. Dos tienen su propia memoria de
  400 ms.
- **Cada pregunta al crudo recorre el árbol UIA entero** para encontrar la barra de direcciones
  (`FromHandle` + `FindFirst(Descendants, Edit)`): 129 ms de mediana, 827 de máximo, medido el 17.
- **Esperar es «preguntar y dormir»**: los seis bucles re-preguntan. El `Compas` hizo la espera
  honesta, no barata, y termina en el siguiente sondeo del que espera, no en el instante del cambio.
- **El SATURADO del log** (`VueltaUnica` descartando ticks) es la consecuencia: más lectores que
  capacidad de leer.

La forma simple: **un observador publica; esperar es suscribirse.** Un solo sitio sondea la pantalla
—con la compuerta barata que `SurfaceLocator` ya tiene— y publica ubicación, número de versión y
evento. Preguntar «dónde estoy» pasa a ser leer un campo; esperar a que cambie pasa a ser esperar a
que la versión suba o se acabe el presupuesto, sin lecturas extra y despertando en el instante en que
el observador lo nota; mientras alguien espera, el observador acelera a ~120 ms y en reposo se relaja
a 800. La única excepción, a propósito: la compuerta **antes de actuar** sigue mirando la ventana de
trabajo *ahora*, porque ahí se juega la corrección (spec 020). Desaparecen los bucles de sondeo, las
memorias cortas de ubicación, la mitad del trabajo de `MapaVivo` y el SATURADO de ubicación; se
conservan la regla de la ventana de delante, la identidad de SAP con su `Busy`, el vigía de cuelgues
y todas las promesas que juzgan qué se hace con la ubicación. Riesgos: la detección pasa a depender
de la cadencia del observador; SAP no admite la compuerta barata; el localizador vive hoy en un
temporizador de la interfaz y el observador necesita su propio hilo. Promesas cuando toque: «una
sola lectura de pantalla por tick, pregunte quien pregunte», «esperar no lee», «la detección no tarda
más que la cadencia acelerada».

## 6b. La especificación de la fase 1 (cortes 1 y 2)

| # | Promesa | Contrato |
|---|---|---|
| 261 | llegar es llegar: pedir una página web se da por cumplido cuando la ruta pedida es el comienzo de la ruta real —sin `www.`, sin barra final, sin la query que el navegador descarta, y en un subdominio del mismo sitio—; pedir una página y estar sólo en el sitio sigue sin bastar, y el camino a un sitio termina en el instante en que se llega, no al agotar el plazo | grafo, y el del mapeador amplía su prueba 24 |
| 262 | abrir no espera a lo que no lanzó: la espera a que aparezca una ventana nueva sólo ocurre cuando de verdad se lanzó algo, y se mide con el reloj y no contando vueltas; traer al frente una app que ya estaba abierta contesta en cuanto está delante | grafo |

Con qué se juzga, sin pantalla: la comparación de llegada con los ocho casos reales de los logs (259);
`PasoDelNucleo` con un `donde` que cae en la variante normalizada, cronometrado; y `AbrirSegunElNucleo`
declarando si lanzó, con la espera a la ventana nueva extraída a una función con reloj y un
enumerador lento (260). Sobre la máquina: `map_go_to` y `map_open_app` por MCP, antes y después.

## 6c. La especificación de la fase 2 (corte 3)

| # | Promesa | Contrato |
|---|---|---|
| 263 | un acto cuenta lo que dejó delante: pulsar, escribir, ir, abrir, desplazar y desbloquear devuelven, detrás de lo que pasó, el mismo inventario que daría `map_what_i_see` —la pantalla y lo accionable—, también cuando no pudieron; una llamada que ni llegó a actuar no lo añade, un saber no lo repite, y el catálogo le dice al modelo que después de un acto no vuelva a preguntar qué hay | grafo |

Por qué también cuando no pudieron: la promesa 38 ya dice que «si no se pudo, se dice QUÉ hay ahora»; el
inventario es exactamente eso. Y por qué en el despacho y no en cada herramienta: había seis sitios y
la clase de error —contestar sin contar qué quedó delante— se arregla una vez donde pasan todos.

Con qué se juzga, sin pantalla: la regla de qué lleva inventario y qué no (pura); `SurfaceMapTools.Call`
con el núcleo y el inventario falsos, viendo que ir y abrir lo pegan, que un saber no, y que una llamada
sin argumento no; y el catálogo y las instrucciones, leídos por reflexión. Sobre la máquina: `map_go_to`
y `map_type` por MCP trayendo lo que hay delante, y una tarea por voz contando cuántos «¿y ahora qué
hay?» siguen a un acto —antes, el 50%—.

## 8. Después del corte 3: la corrida del 17 de septiembre a las 13:17, medida

La misma tarea de siempre del dueño —investigar en Google, leer, dictar a Docs y al Bloc de notas—,
42 llamadas en ocho minutos, con los cortes 1, 2 y 3 dentro (1.3.3).

**El corte 3 valió.** Saberes que el modelo pide justo después de un acto, sin que el usuario hablara en
medio: **1 de 20 actos (5%)**, contra 2 de 7 (29%) en la corrida anterior y el 50% de los tres días. Y el
hueco del modelo tras un acto no creció por leer una respuesta más larga: 3,0 s de mediana.

**Dónde se va el reloj ahora** (unos 430 s de tarea):

| | | |
|---|---|---|
| Nuestras herramientas | 67 s | 15% |
| Huecos en los que sólo el modelo pensaba | 121 s | 28% |
| Huecos en los que **el usuario hablaba** (enseñando: 14 `map_esto_es`; dictando) | 241 s | 56% |

El 56% es la persona hablando. No es ruido y no se corta: es la tarea. Lo que queda por cortar es el
15% de herramienta y, dentro del 28% del modelo, las vueltas que sobran.

**Los 67 s de herramienta, uno a uno:**

| Qué | s | Qué era de verdad |
|---|---|---|
| `map_type` (5) | 28,1 | **15,6 s fueron 2.212 caracteres tecleados uno a uno en Google Docs** (7,1 ms/carácter): el campo es mudo y se teclea. En el Bloc de notas, 3.017 caracteres por `SetValue` costaron 2,5 s |
| `map_go_to` (9) | 20,5 | **13,2 s fueron un solo «no hay camino»**: la pestaña de Docs se activó en Chrome, pero la persona tenía el Bloc de notas delante y **la llegada se juzgó sobre la ventana de delante, no sobre la de trabajo**. `trabajo:` dice «la ventana de trabajo es ahora docs.google.com» un segundo después de haber declarado el fracaso |
| `map_esto_es` (14) | 7,8 | enseñanza del usuario; proporcional |
| `map_scroll` (4) | 4,3 | ~1 s cada uno: el precio del corte 3 (cada acto lee la pantalla) |
| el resto | 6 | — |

**Un error mío del corte 1, visto en esta corrida.** A las 13:23:29 se pidió `google.com`, la heurística de
pestañas activó «Google Sheets» por el título, y la regla de llegada nueva aceptó `docs.google.com` como
subdominio de `google.com`: **«te puse delante de google.com» estando en Sheets**. Una caja que miente. Lo
salvó el corte 3: el inventario decía «EN PANTALLA AHORA, en docs.google.com/spreadsheets…» y el modelo
volvió a pedir `www.google.com` diez segundos después. La tolerancia a subdominios se retira: `www.` sí,
otro subdominio no —`docs.`, `mail.`, `drive.` son sitios distintos—. El caso `apply.ycombinator.com`
(uno en tres días) vuelve a «no hay camino» y se acepta.

**Los cortes 4 y 5, revisados con datos:**

- **Corte 4 (la pestaña por dominio, no por título): sí aporta, y por más de lo que se pensó.** En esta
  corrida la heurística de título falló tres veces: «Instagram» tomada por `docs.google.com`, «Nueva
  pestaña» por `instagram.com` —dos o tres segundos cada una en activar, comprobar y volver— y «Google
  Sheets» por `google.com`, que además produjo la mentira de arriba. Pero el mecanismo caro detrás de
  los 13 s no es la pestaña: es **juzgar la llegada sobre la ventana equivocada**. El corte 4 se reformula:
  (a) `PasoDelNucleo` juzga la llegada sobre la **ventana de trabajo** (`DondeTrabajo`, la misma que usan
  pulsar y recorrer), no sobre la de delante —es la spec 020 aplicada a ir—; (b) la pestaña se elige por
  su URL y no por su título; (c) la regla de llegada pierde los subdominios.
- **Corte 5 (el catálogo de apps sin rescanear): no aporta, y se descarta.** Medido en tres días: las
  aperturas de apps ya abiertas cuestan 668 ms de mediana (era el bucle de 8 s, ya cortado), y las que
  lanzan de verdad cuestan lo que tarda la app en arrancar —VS Code 2,3 s, SAP Logon 2,5 s— con el
  catálogo contestando en el mismo segundo. No hay nada que cortar ahí.

**Una palanca nueva, con su número: pegar en vez de teclear.** En un campo mudo se teclea carácter a
carácter a 7 ms cada uno; 2.212 caracteres son 15,6 s, el 23% de toda la herramienta de esta corrida.
Pegar desde el portapapeles es un gesto. Con sus límites dichos: hay que guardar y devolver el
portapapeles de la persona, y hay campos que no aceptan pegar —ahí se teclea, como hoy—.

**El orden que queda, por lo que compra dividido por lo que arriesga:**

| # | Corte | Compra | Riesgo |
|---|---|---|---|
| 1' | Retirar los subdominios de la regla de llegada | deja de mentir «te puse delante» | bajo: quitar una línea y sumar un caso a la 261 |
| 4a | La llegada se juzga sobre la ventana de trabajo | los 13 s de esta corrida y los ≥ 6 casos de «ventana equivocada» de tres días | bajo-medio: cambia qué `donde` recibe el servidor del núcleo, y es el que ya usan pulsar y recorrer |
| 6 | Pegar en vez de teclear en campos mudos, para textos largos | 15,6 s de 67 en esta corrida | medio: portapapeles ajeno; se guarda y se devuelve |
| 4b | La pestaña por URL, no por título | 2–3 s por fallo, tres por corrida | bajo |
| ~~5~~ | ~~el catálogo de apps~~ | nada medible | — |

**Y un síntoma anotado para el observador único:** en una apertura de Chrome el localizador escribió la
misma línea —«sin barra de direcciones: mobiliario del navegador»— **sesenta veces en ocho segundos**:
siete preguntas por segundo de distintos sondeadores a la misma ventana. No cuesta tiempo al usuario
hoy; es la prueba de que hay más lectores que capacidad de leer.

## 7. Cómo se sigue midiendo

El reparto de este documento sale de dos guiones sobre el log —el de tiempos por herramienta y el
de actos/saberes con la anatomía de cada hueco (voz del usuario, voz de Ü, arranque de la
delegación)— y se repite después de cada corte. Lo que se compara no es «cuánto tarda la tarea» sino
tres números: **segundos de herramienta, vueltas del modelo y segundos en llamadas que acabaron en
nada.** Si un corte no mueve ninguno de los tres, no era un corte.
