# Plan de implementación: lo que uno le pida, lo hace — y a la primera

Estado: **implementado; nivel 4 pendiente** (2026-09-11: 202–207 verdes y saboteadas, con los arreglos del crítico de la rama; la prueba en el PC real no pudo correr de noche) · **Las tareas largas (R8–R11) no están en esta rama** · Nace de una nota de voz del 2026-09-10 ([transcripción y rúbrica](fuentes/2026-09-10-lo-hace-a-la-primera.md)) y del diagnóstico de esa noche · Rama: `jero/lo-hace-a-la-primera`

> **La petición, en la voz del audio:** *«que lo que uno le pida lo haga, y lo haga a la primera,
> máximo dos intentos, máximo dos segundos, y no se sienta que falló, sino: lo hizo.»*

## Diagnóstico: qué se midió

Lo midieron el 2026-09-10 sobre `main` (`ba249fe`) seis agentes en paralelo: cinco frentes de solo
lectura —cómo elige herramienta el cerebro, qué pasa en una tarea de un clic, los logs, las tareas
largas, cómo se juzga— y una **rúbrica del audio escrita por un agente que no leyó código**. Cada uno
con su verificador adversarial.

**El cerebro es la voz.** El audio dice *«estoy usando la funcionalidad de voz de la rama main»*:
GPT Realtime (`gpt-realtime-2.1-mini`), con prompt y catálogo en `ConversacionEnVivo.cs`. El texto de
«Escríbele…» le llega solo con la voz abierta (`FaceWindow.xaml.cs:2300`); con ella cerrada va a
Graph, que desde esta máquina no responde.

**Lo medido es de agosto, y se dice así.** Con el código de hoy no hay en esta máquina ni una llamada
de herramienta registrada (`u-20260901…0908`: 0 `llamada recibida`), y la única sesión de voz de
septiembre (09-07) no ejecutó nada. Lo de abajo sale de agosto —otro motor, Gemini Live, y
herramientas que `e3c3ad8` retiró el 30—: vale como antecedente de la **clase** de fallo. La base con
el código de hoy es la fase 0.

| Qué | Medida | Fuente |
|---|---|---|
| Peticiones de voz reconstruidas | 23 | logs de agosto, `peticiones.py` |
| Llamadas por petición | mediana 2 · p90 4 · máx. 7 | ídem |
| Tiempo por petición | mediana 17 s · p90 46 s | ídem |
| Resueltas en ≤2 intentos **y** ≤2 s | **5 de 23** — las cinco, `file_open` sobre disco | ídem |
| Duración de `map_take` | mediana 7 s · p90 12 s | `duraciones.py` |

**Los dos botones parecidos, medido** (`u-20260809.log`, 09:37:31–09:38:10). `map_take exit=Descargas`
casó con tres salidas vivas —TreeItem, TabItem, SplitButton— y la respuesta fue *«hay 3 puertas
vivas… Dime el selector y sigo»*: sin número y sin destino. La voz pidió el mismo selector **tres
veces idénticas**, 6–7 s cada una, y ninguna llegó. Con un matiz: ese selector se lo había ofrecido
el propio sistema, y quien no lo resolvió fue el ejecutor. Y dos de las tres las retiró el modelo y
se ejecutaron igual.

### La causa, en orden de impacto

1. **La ambigüedad se detecta y se devuelve sin con qué resolverla.** `RecorrerSegunElNucleo.cs:143-147`
   pide «el selector»; `map_take` no tiene `which` (`ConversacionEnVivo.cs:753-765`) aunque `map_show`
   sí (`SurfaceMapTools.cs:1774`); y ante homónimos el prompt manda **preguntar al usuario**
   (`ConversacionEnVivo.cs:807-811`), no mirar.
2. **El pantallazo no cierra el lazo.** `map_look` solo adjunta una foto: nada convierte lo visto en
   un pulsar distinto. Y `map_show` devuelve a `map_take exit=«Label»` por etiqueta
   (`SurfaceMapTools.cs:347-349`), que recrea la ambigüedad. Por eso mirar queda de último: no desempata.
3. **Cada intento fallido cuesta 3–7 s** medidos, y hasta ~10,6 s por construcción: `EsperarloVivo`
   ≤4080 ms, `Resolve` 5×200 ms, `EsperarACambiar` ≤1800 ms y el ensayo de doble clic otros ≤1800 ms.
   Un segundo intento ya pasa de 2 s.
4. **No hay tope en el código.** `ConversacionEnVivo.cs:1594-1671` ejecuta todo lo que se pide, y la
   regla escrita tolera tres llamadas («más de dos veces», `:731`).
5. **Un éxito no dice si la pantalla cambió.** `Recorre` cierra con «hice los N paso(s): quedaste en…»
   (`RecorrerSegunElNucleo.cs:183-185`) y tira el «pulsé X y la pantalla no cambió» de `Pulsa`
   (`PulsarSegunElNucleo.cs:101-103`). Sin ese dato no hay «me di cuenta de que por aquí no era».
6. **Argumentos muertos que el prompt exige.** `at` y `action` de `map_take`, y `at` de `map_type`, no
   se usan en su cuerpo (`SurfaceMapTools.cs:2247, 2267`), y el prompt manda pedir `map_where_am_i` para
   rellenar `at` (`ConversacionEnVivo.cs:673-676`): un viaje de más, y la ilusión de controlar el gesto.

## Por qué va dirigido por especificación

Porque «lo hace a la primera» es lo más fácil de aparentar: basta callar el fallo, contar solo lo
ejecutado, o medir desde donde conviene. La rúbrica verificada lo advierte con todas las letras: *«que
no se sienta que falló»* se puede «cumplir» escondiendo la narración de los fallos. Las promesas
juzgan **la causa, no el reloj**: ninguna mide milisegundos (`docs/velocidad.md`), porque un umbral de
tiempo en el contrato es inestable y se gana subiendo el umbral. El tiempo lo juzga el nivel 4, con
la línea que deja la promesa 205.

## La especificación

**Numeración.** El máximo de `main` es la 192. Dos ramas abiertas de Jose ya usan números por encima
(`jose/la-nota-se-elige-y-se-corrige` llega a la 193 y a la spec 015; `jose/el-boton-del-collar-graba`
trae su propia spec 014) y tendrán que renumerar sobre `main`: nueve promesas y dos specs. Para no
pisarlas, esta spec salta a la **017** y sus promesas empiezan en la **202**. Los números no se
reciclan; los huecos están permitidos.

| # | Promesa | Fase |
|---|---|---|
| 202 | pulsar dice lo que pasó: una tanda que termina bien distingue «ahora estás en Y» de «la pantalla no cambió», y «hice los N paso(s)» ya no tapa el último hecho | 2 |
| 203 | con varias puertas vivas para un mismo nombre no se pide un selector a ciegas: se numeran 1..N en orden estable con su tipo y, si se sabe, a dónde llevan; y un paso que trae cuál pulsa esa y solo esa | 3 |
| 204 | dos intentos y no tres: en un mismo turno del usuario, la tercera acción hacia un destino que ya falló dos veces no se ejecuta, y se dice qué salió en cada una; el destino es el mismo se pida por nombre o por selector | 4 |
| 205 | cada turno del usuario deja una línea voz-turno con su medida: llamadas, herramientas distintas, el máximo de intentos a un mismo destino y los milisegundos hasta la primera acción y la última; lo rechazado y lo retirado también cuentan | 1 |
| 206 | el catálogo le pide al cerebro lo que las manos usan: map_take y map_type no ofrecen argumentos que su cuerpo ignora, map_take trae which, y las instrucciones mandan mirar y elegir con which antes que preguntar; la regla escrita de intentos es la del código: dos | 5 |
| 207 | la mano dice, sin prosa, si fue un intento y si lo logró: pulsar y que cambie la pantalla es logro, pulsar y que no cambie no lo es, pedir algo que no está es un intento fallido, y la lista de homónimos no es un intento | 4 |

### Con qué se juzga cada una

Todas por reflexión, sin pantalla y sin modelo; nacen en rojo con `Pendiente(…, "017")`.

| # | Se juzga con | Sabotaje que la tiene que poner roja |
|---|---|---|
| 202 | `BatchCon` con mano falsa: con una ruta `a·s:1→b` la cuenta dice «ahora estás en «…b»»; sin ruta dice «no cambió»; y ya no aparece el «quedaste en» que tapaba el hecho (el prefijo «hice los» se queda: la demo lo lee) | reponer el cierre de `RecorrerSegunElNucleo.cs:183-185` |
| 203 | un grafo con dos «Descargas» vivas (TreeItem → b, TabItem → c): sin cuál, nada tocado y la cuenta trae «1)», «TreeItem», «2)», «TabItem»; con cuál=2 queda en c con un solo toque; el catálogo declara `which` en `map_take`; y los candidatos viajan también como datos (`Resultado.Candidatos`), en el orden de su número | ignorar `Cual` y tomar el primero; quitar el tipo del mensaje; no llevar los candidatos como datos; llevarlos en otro orden |
| 204 | una clase pura con la secuencia: dos `take` fallidos a «Descargas» —uno por nombre, otro por selector— y el tercero se rechaza con «dos»; otro destino pasa; un turno nuevo reinicia; tres logrados pasan; mirar nunca cuenta. Y, por `DestinoDe` con `which`: dos fallos al candidato 1 frenan el 1, **no el 2** —si esa salida dio lista; sin lista, `which` no abre clave y el rechazo no lo sugiere—. Y `Despues`, el sitio único de lo que pasa tras cada herramienta: una excepción es fallo, la lista no (y se recuerda), lo que no trae mano no se adivina. Tras una lista, el selector de un candidato ES ese candidato, y `which=02` es el 2; con cualquier `which`, y con los selectores REALES de la tanda de la 203 (s:1, s:2, como los de SAP) cruzada con el tope | comparar el texto crudo sin aplanar (aprendizaje nº16); quitar el candidato de la clave; abrir clave con `which` sin lista; que la excepción no cuente; olvidar la lista; sugerir `which` sin lista; que el selector de un candidato vuelva a ser otra clave; comparar `which` como texto; buscar el selector solo en la lista de su nombre; no quitar `which` antes de buscarlo |
| 205 | una clase pura con reloj inyectado: el resumen trae `llamadas=`, `distintas=`, `intentos_max=` y los ms; rechazadas y retiradas aparte; un `Resultado` sin `Llamada` en el turno no cuenta ni da un tiempo negativo; `desde_peticion=` se mide desde `Peticion()` | contar solo lo que devolvió resultado (el patrón nº10: el denominador es lo pedido); dejar que un resultado sin llamada cuente |
| 206 | el catálogo y las instrucciones como texto: sin `at`/`action` en `map_take`, sin `at` en `map_type`, `map_unblock` conserva `at`; sin «pide map_where_am_i primero» ni «más de dos veces»; con `which` y `map_look` juntos; el `which` de `map_show` no manda al de `map_take`; `map_take` avisa de que «Guardar» no cambia de pantalla y está bien | reponer `at` en `map_take`; que el `which` de `map_show` vuelva a mandar a `map_take` |
| 207 | `new SurfaceMapTools(() => null)` con un `RecorrerPorElNucleo` falso que devuelve cuatro resultados —la pantalla cambia, no cambia, no lo conoce, lista de homónimos—; `UltimaMano` leída por reflexión; y la mano lleva los candidatos de la lista | que «logrado» vuelva a ser «terminó», sin mirar si cambió; que la lista vuelva a contar como intento; que la mano deje de llevar los candidatos |

**Límites dichos, no escondidos.** La 203 no cubre dos homónimos **del mismo tipo**: se funden en un
selector antes de llegar al ejecutor (`Grafo.cs:136`). La 204 juzga la regla, no su cableado —un
guardia que se cree puesto (aprendizaje nº18)—; el cableado lo demuestra el nivel 4 con la línea del
rechazo. **La 205 tampoco juzga su cableado**: que `ConversacionEnVivo` llame a `Llamada`, `Resultado`,
`Peticion` y `Cerrar` donde toca solo lo dice la línea `voz-turno` de una corrida real. La 207 cierra
el hueco que el crítico encontró entre las dos: el dato del que ambas dependen (`UltimaMano`) ya se
juzga, y sabotearlo ya no deja el contrato intacto. Y qué cuenta como intento fallido ya no se decide en
`ConversacionEnVivo` sino en `TopeDeIntentos.Despues`, que la 204 juzga: sin juez queda solo que se llame. La 206 juzga el texto, no la conducta del modelo;
la conducta la juzga el nivel 4.

Y los límites de forma que desde fuera no se ven:

- **Solo `uia:name=` se aplana a su nombre.** Un selector por `aid=` o un id de SAP (`wnd[0]/…`)
  cuenta como un destino distinto de su etiqueta, y el tope lo vería como otro sitio.
- **Hay dos numeraciones de homónimos.** `map_take` numera por selector, que es un orden estable para
  elegir; `map_show`, por posición en la pantalla, que es el orden para señalar de arriba abajo. Ya no
  se cruzan en el texto —el `which` de `map_show` avisa de que no es el de `map_take`—, pero siguen
  siendo dos órdenes.
- **`which` solo separa candidatos si hubo lista.** El 1 y el 2 de una lista numerada son destinos
  distintos, que es lo que pide el audio («pruebo este otro botón»). Sin lista, `which` no abre una clave
  nueva: el ejecutor lo ignora y es el mismo botón (crítico final; la 204, ampliada). Y tras una lista,
  pedir un candidato por su selector es pedir ese candidato, y `which=02` es el 2 (tercera pasada). Con
  cualquier `which` y con cualquier selector de cualquier lista del turno —también los de SAP—: el tope
  aplica las reglas del ejecutor, que pulsa el selector exacto e ignora `which` (cuarta pasada).
- **Tres cuentas de «intento», con nombres parecidos.** `intentos_max` de la línea `voz-turno` cuenta
  llamadas al mismo destino (tres «Siguiente» logrados dan 3); el tope cuenta solo fallos; el juez del
  nivel 4, los intentos sin la lista. Miden cosas distintas: al leer un número, decir de cuál es.
- **El eco vacía el tope.** Con altavoz, la voz de Ü también dispara `speech_started`, que abre un turno
  nuevo y pone el tope a cero a mitad de una petición. La línea `voz-turno: turno nuevo (por
  speech_started)` lo delata en el nivel 4; el arreglo de fondo va con R18.
- **Una etiqueta difusa sigue siendo otra clave.** El ejecutor casa por aproximación («Descarga» con
  «Descargas»); el tope compara el nombre aplanado. Cuatro pasadas del crítico encontraron cuatro formas
  de la misma diferencia —el tope juzga lo PEDIDO y el ejecutor decide por sus reglas—, y cada una se
  cerró copiando una regla más. El arreglo de fondo es el contrario: que el ejecutor consulte el tope con
  lo que VA a pulsar. Es otra spec, y la primera que habría que abrir si el nivel 4 enseña insistencias.
- **El cableado sigue sin juez, y se midió.** Romper la línea de `ConversacionEnVivo` que le pasa los
  candidatos al tope deja el contrato INTACTO (sabotaje «cableado» de la cuarta ronda). Lo mismo borrar
  la llamada a `Despues` o a `NuevoTurno`. Lo que se juzga es lo que esas llamadas deciden, no que se
  hagan: eso lo dicen las líneas del log en el nivel 4.

## Las fases

Una spec, una rama, una fase por commit. El portero exige INTACTO para empujar: el rojo de cada fase
se comprueba en local y se anota en su commit; se empuja en verde.

| Fase | Promesa | Qué toca | Terminado cuando |
|---|---|---|---|
| **0** | — (medir) | nada | la tabla de base del nivel 4 con el binario de `main`, con su log |
| **1** | 205 | nuevo `Voice/CuentaDelTurno.cs`; su cableado en `ConversacionEnVivo.cs` | 205 verde y la línea `voz-turno:` aparece en una corrida real |
| **2** | 202 | `Navigation/RecorrerSegunElNucleo.cs` (el cierre) | 202 verde; siguen verdes las del ejecutor (56–59, 63–65, 76, 80, 103, 122) |
| **3** | 203 | `RecorrerSegunElNucleo.cs` (`Paso.Cual`, homónimos); `Mcp/SurfaceMapTools.cs` (`Take`, `which`); el catálogo | 203 verde; se cuentan con `grep` los sitios que resuelven homónimos y el número va en el commit |
| **4** | 204 | nuevo `Voice/TopeDeIntentos.cs`; su cableado antes de `_mapa.Call` | 204 verde y el rechazo aparece en el nivel 4 |
| **5** | 206 | el catálogo y las instrucciones de `ConversacionEnVivo.cs`; las firmas de `Take`/`Type` | 206 verde; siguen verdes 138, 141, 161 y 192, que leen el mismo texto |

## El nivel 4

Dos baterías, las dos corridas en la fase 0 con el binario de `main` y al final con el de la rama,
en el mismo equipo, la misma pantalla y el mismo punto de partida:

- **Por la puerta MCP (8790), sin voz.** Mide el ejecutor solo: cuánto tarda cada `map_take`, qué
  contesta ante homónimos, si dice que la pantalla no cambió. Es determinista y silenciosa.
- **Por la voz**, escribiendo en «Escríbele…» con la voz abierta. Es la única que juzga lo que el
  audio describe: qué herramienta **elige** el cerebro, cuántas veces insiste, si mira antes de
  preguntar. Tres repeticiones de cada tarea: el fallo del audio es intermitente.

| T | Lo que se le pide | Requisito | Estado final exigido |
|---|---|---|---|
| T1 | «abre el explorador de archivos y ve a Documentos» | R6 | el explorador delante, en Documentos |
| T2 | «entra a Instagram y ve los mensajes» (Chrome) | R5 | la bandeja de mensajes a la vista; si no hay sesión iniciada, «no medible» |
| T3 | «abre Configuración y ve a Bluetooth» | R7 | la página de Bluetooth |
| T4 | «en el explorador, pulsa Descargas en el panel de la izquierda» | R3 | Descargas, desde el panel |
| T5 | «en Configuración entra en Sistema, vuelve atrás y entra en Bluetooth» | R13 | Bluetooth, habiendo pasado por Sistema |

**Los umbrales, fijados antes de medir** (la rúbrica exige fijar el punto de partida):
- **Intentos:** acciones hacia el mismo destino dentro de la petición, **y a lo sumo un intento
  fallido en toda la petición** (lo frenado cuenta): el segundo tiene que ser el bueno. Lectura
  estricta, la del audio: el «máximo dos intentos» es de **la petición**, no de cada acción suelta.
- **Tiempo por acción:** desde la línea de la llamada hasta su resultado. **≤ 2,0 s.**
- **Tiempo por petición:** desde que se entrega la orden hasta el resultado que deja el estado final.
  Se reporta siempre, diciendo que incluye la latencia del modelo.
- **Aprobado:** la tarea hecha, ≤2 intentos al mismo destino, ≤1 intento fallido en toda la petición
  y cada acción ≤2,0 s. Si no, k/N con **el plan como
  denominador**, al lado de la base de la fase 0.
- Lo que Ü **dice** se cruza con el log: callar un fallo no cuenta como no fallar.

## Cobertura de la rúbrica

La rúbrica (19 requisitos, R1–R17 foco, R18–R19 secundarios) está en la fuente. Lo que cubre esta spec:

| R | Lo cubre | Nota |
|---|---|---|
| R1, R14 | todas las fases; el nivel 4 | |
| R2 | 203, 206; «herramientas distintas» en la línea de 205 | lo que el modelo elige de verdad solo lo dice el nivel 4 |
| R3 | 203, 206; T4 | no cubre homónimos del mismo tipo |
| R4 | 204 de forma indirecta; nivel 4 | en el audio era un ejemplo, no una regla de modalidad |
| R5 | **sin medir** | T2 salió del kit: abrir mensajes privados manda la foto de la pantalla a OpenAI, y eso se decide con el usuario delante |
| R6, R7 | T1, T3/T5 | ninguna lógica con «Instagram» ni «Documentos» cableada: el diff se revisa con `grep` |
| **R8, R9, R10** | **no en esta rama** | ver «Lo que NO entra» |
| R11 | 202, en parte | cada acción dice si tuvo efecto; **la tarea larga no está en esta rama** |
| R12, R15 | 204, 205; nivel 4 | |
| R13 | 202 (saber que no era por ahí); T5 | la voz no tiene un «atrás» genérico |
| R16 | **fuera, declarado** | ninguna promesa mide tiempo, y esta rama no toca las esperas (patrón nº14). Hay un suelo por construcción: un pulsar que no cambia la pantalla espera `EsperarACambiar` hasta 1,8 s antes de decirlo, así que un intento que no navega no baja de ~1,8 s, y dos no caben en 2 s. Bajarlo es otra spec, medida antes |
| R17 | nivel 4, cruzado con el log | |
| R18 | no | pista secundaria, diagnosticada aparte |
| R19 | **tocado, sin medir** | no se toca `voz/Realtime`, pero sí el manejador de `speech_started` en `ConversacionEnVivo`: callar va primero y después se cierra la medida del turno. Que interrumpir siga funcionando como en `main` solo lo dice el nivel 4 |

## Lo que NO entra

- **Las tareas largas por el Agent SDK (R8–R11) no están en esta rama**, y es lo primero que hay que
  decir de ella. El audio las pide —«crea una campaña en Meta» va al Agent SDK, que planea y ejecuta por
  tandas— y aquí no hay nada de eso. Se eligió lo que el propio audio pone primero —*«ese creo que es
  el primer paso que tenemos que lograr, que lo que uno le pida lo haga»*— porque lo corto se puede
  juzgar esta noche y lo largo no: `piloto/piloto.mjs` no arranca en esta máquina (le faltan
  `node_modules` y apunta a otra), y el SDK funciona por Console pagando por uso —0,55 USD una llamada
  mínima, entre 1,37 y 4,06 USD una corrida del piloto en Opus (spec 013)—. Un enrutado que no se puede
  correr de punta a punta sería justo lo que este repo llama «parece que funcionó». Es la spec
  siguiente, no un «quizá».
- **La voz que se corta sola (R18).** Diagnosticada aparte la misma noche: no hay causa ganadora sin
  los logs del equipo de quien grabó la nota. Si se abre, es otra spec y otra rama.
- **Homónimos del mismo tipo**, **el ejecutor que no resuelve un selector que él mismo ofreció**, y
  **cancelar una herramienta que ya está corriendo**: medidos, fuera de esta noche.
- **Tocar las esperas** (`EsperaMaximaMs`) ni el ensayo de doble clic: patrón nº14.

## Hallazgos

- **2026-09-10 (rojo, por las razones escritas).** 175 juzgadas, 5 rotas, exactamente 202–206, y las
  170 anteriores intactas. Los motivos del rojo son los escritos, no un fallo del arnés: la 202 cae con
  «hice los 1 paso(s)», la 203 con «Dime el selector» y `Paso.Cual` pendiente, la 204 y la 205 con sus
  clases pendientes, y la 206 con los argumentos muertos y el «más de dos veces». Commit `5a81bcb`.
- **2026-09-11 (tres promesas viejas rotas por el camino, arregladas sin tocar sus pruebas).** Al poner
  las cinco en verde cayeron la 56, la 64 y la 103, y las tres tenían razón:
  - **56 y 64** exigen que, ante homónimos, se den **los selectores**. La lista numerada nueva daba
    etiqueta, tipo y destino, pero no el selector. Ahora cada entrada lo lleva, y el cerebro puede
    usarlo directamente.
  - **103** construye un `Paso` por reflexión con cuatro argumentos. Un quinto parámetro en el
    constructor la rompía con `MissingMethodException`, porque reflexión no rellena opcionales.
    `Cual` pasó a ser una propiedad `init`, fuera del constructor.
  - La lección: añadir un parámetro a un record que el contrato construye por reflexión es cambiar su
    firma pública, aunque tenga valor por defecto.
- **La clase de error de los homónimos vivía en 2 sitios**, y los 2 están corregidos:
  - el ejecutor, que contestaba «dime el selector»;
  - `map_show`, que tras resolver un homónimo señalándolo sugería `map_take exit=«Etiqueta»` y volvía
    a crear la ambigüedad. Con homónimos, ahora sugiere el selector.
- **Lo que no se sabe leer no se adivina.** `map_go_to`, `map_open_app` y `file_open` devuelven solo
  texto. El tope no los cuenta como fallo, y la medida del turno no los cuenta como acción que actuó.
  Solo `map_take` y `map_type` traen su resultado estructurado (`SurfaceMapTools.UltimaMano`). Límite
  dicho: un botón que hace su trabajo sin cambiar de pantalla, como «Guardar», cuenta como no
  logrado, así que el tercero idéntico se frena.

- **2026-09-11 (el sabotaje).** Cada promesa se rompió a propósito con un patrón de una sola línea. Se
  comprobó por diff de bytes que el cambio se aplicaba, se exigió el veredicto del juez, se restauró y
  se recompiló al final: **CONTRATO INTACTO**.

  | Promesa | Sabotaje | Quedó |
  |---|---|---|
  | 202 | el cierre vuelve a tapar el último hecho | roja |
  | 203 | se ignora `which` y se toma el primero | roja |
  | 204 | el destino se compara como texto crudo, sin aplanar | roja, **y también la 205** |
  | 205 | solo se cuenta lo que devolvió resultado | roja |
  | 206 | `map_take` vuelve a ofrecer `at` | roja |

  Que el sabotaje de la 204 tumbe también la 205 no es un efecto colateral: las dos preguntan «¿es el
  mismo sitio?» por el mismo `TopeDeIntentos.Destino`. Un solo criterio, como pide el aprendizaje nº16.

- **2026-09-10, 23:55 (el nivel 4 no pudo correr de noche, y por qué).** La base, con el binario de
  `main`, abortó. El conductor pulsó Ctrl+Alt+M y la voz no se abrió, sin un solo error en el log. Una
  sonda midió la causa: el escritorio de entrada era **«Screen-saver»**.
  - El salvapantallas **OLED Care** del fabricante se activa por inactividad aunque la pantalla esté en
    «apagar: nunca», y se traga toda la entrada inyectada: `keybd_event` falla en silencio y `SendKeys`
    devuelve «Acceso denegado».
  - **No se cerró a propósito**: protege el panel OLED de quemarse, y forzar la pantalla encendida toda
    la noche habría sido arriesgar el hardware para una prueba.
  - El conductor ahora comprueba el escritorio de entrada antes de empezar y antes de cada Enter. El
    kit queda en `scripts/nivel4-voz/`, en un comando.
- **La memoria del terreno vive en Neo4j** (`127.0.0.1:7474`), que no estaba corriendo: una instancia
  aislada arranca «sin nada recordado», y copiar `%LOCALAPPDATA%\U` no copia el mapa. Base y rama
  arrancan igual de vacías, así que la comparación es justa. Pero no es la experiencia de alguien con
  Neo4j vivo, y hay que decirlo al leer los números.
- **Un ajuste de alcance del nivel 4.** La batería por la puerta MCP (8790) no se escribió. La batería
  por voz deja en el log la duración de cada acción (`mapa-mcp: ← (N ms)`), que es justo lo que aquella
  iba a medir, y además juzga lo que la otra no ve: qué herramienta **elige** el cerebro.

- **2026-09-11 (una regresión que ningún contrato veía).** Buscando quién leía los textos que cambia esta rama
  apareció la demo de punta a punta: `FaceWindow.xaml.cs` decide en **tres sitios** si una tanda de `map_batch`
  terminó leyendo si su cuenta **empieza por «hice los»** —el aprendizaje nº2, vivo en otro archivo—. Con el primer
  cierre de la 202, una tanda de un paso que terminaba bien empezaba por «pulsé…», y la demo la habría dado por
  fallida sin que ninguna promesa se enterara. Se arregló sin tocar `FaceWindow`, que es zona de choque: el prefijo
  «hice los» se queda siempre y detrás va el último hecho. La 202 dejó de exigir que no apareciera «hice los» y
  exige que ya no aparezca el «quedaste en» que tapaba el hecho; su sabotaje, repetido, sigue en rojo. Para quien
  toque la demo: esos tres `StartsWith` deberían leer el `Termino` estructurado, no la prosa.
 
- **2026-09-11 (el crítico de la rama, primera pasada: 4/10).** Un agente que no escribió el código
  juzgó la rama contra el audio y la rúbrica. Lo que encontró y cómo quedó:
  - **El tope frenaba el «pruebo este otro botón».** Dos fallos al candidato 1 de una lista numerada
    frenaban también al 2 —con un rechazo que encima decía «elige otro candidato con which»—. El
    candidato elegido es ahora parte del destino (204, ampliada).
  - **La lista de homónimos contaba como intento fallido**, aunque no se pulsara nada. El resultado la
    marca (`Ambiguo`) y la mano dice si fue un intento (`Intento`): promesa **207**, nueva.
  - **Sabotear `UltimaMano` dejaba el contrato intacto**: 204 y 205 dependían de un dato que nadie
    juzgaba (aprendizaje nº18). La 207 lo juzga.
  - **`map_type` por UIA no dejaba mano**: fuera de SAP el tope no frenaba ninguna escritura. Ahora la
    deja; ninguna promesa lo juzga, porque escribir por UIA necesita pantalla.
  - **Una excepción contaba como logro** (`?? true`). Ahora es un intento que no se logró.
  - **La medida del turno podía dar tiempos negativos** (un resultado que cruzaba el cierre) y no se
    reiniciaba en un turno sin llamadas. Y medía desde la primera llamada del modelo, no desde que se
    pidió: ahora dice también `desde_peticion=` (205, ampliada).
  - **Con `speech_started` se contaba el turno antes de callarse.** Ahora callar va primero, y la línea
    dice por qué empezó el turno (voz o texto): con altavoz, el eco también dispara `speech_started`.
  - **El `which` de `map_show` mandaba a pulsar con el de `map_take`**, que numera en otro orden; y
    `map_show` sugería un selector aunque lo compartieran varios. Las dos cosas, arregladas (206).
  - **`map_take` decía «si no cambió, por ahí no era»**, y un Guardar que hace su trabajo no cambia de
    pantalla. La descripción lo distingue ahora (206).
  - **El juez del nivel 4 favorecía a la rama**: `analizar.py` solo contaba las llamadas que llegaban a
    ejecutarse, así que un tercer intento suspendía en `main` y aprobaba aquí. Ahora cuenta lo frenado
    (commit `8a98fa7`).
  - **Esta spec decía cosas que no eran ciertas**: que R16 lo cubría el nivel 4 (hay un suelo de
    ~1,8 s por intento que no navega), que R19 no se tocaba (se tocó `speech_started`), y dos razones
    de R8 que eran excusas. Corregidas arriba.

- **2026-09-11 (el sabotaje, segunda ronda).** Los mismos tres cuidados que en la primera; y las
  cuatro de la primera que no cambiaron de patrón se repitieron sobre el código nuevo.

  | Promesa | Sabotaje | Quedó |
  |---|---|---|
  | 204 | el destino se compara como texto crudo, sin aplanar | roja, **y también la 205** |
  | 204 | el candidato elegido deja de ser parte del destino | roja |
  | 205 | un resultado sin llamada en el turno vuelve a contar | roja |
  | 206 | el `which` de `map_show` vuelve a mandar al de `map_take` | roja |
  | 207 | «logrado» vuelve a ser «terminó», sin mirar si la pantalla cambió | roja |
  | 207 | la lista de homónimos vuelve a contar como intento | roja |
  | 202 | *(primera ronda, repetida)* el cierre vuelve a tapar el último hecho | roja |
  | 203 | *(primera ronda, repetida)* se ignora `which` y se toma el primero | roja |
  | 205 | *(primera ronda, repetida)* solo se cuenta lo que devolvió resultado | roja |
  | 206 | *(primera ronda, repetida)* `map_take` vuelve a ofrecer `at` | roja |
  
  Diez de diez en rojo, cada una comprobada por diff de bytes y restaurada idéntica; recompilado al final con todo restaurado: **CONTRATO INTACTO, 176 juzgadas**. El contrato de la voz, 32/32.
- **2026-09-11, 01:10 (el crítico final, segunda pasada: 5/10).** Sobre `8f4952b`. Dio por hechos los
  arreglos 2, 4, 5 y 6 de la primera pasada, el 1 con un agujero nuevo y el 3 a medias. Lo que encontró
  y cómo quedó:
  - **`which` esquivaba el tope, y el rechazo mandaba hacerlo.** El ejecutor solo lee `which` cuando hay
    homónimos; con un único «Descargas», which=1, 2, 3… pulsaban el mismo botón y cada número era una
    clave nueva, sin límite. Y la 204 lo certificaba en verde: un guardia que se cree puesto
    (aprendizaje nº18). Ahora `which` solo separa si esa salida dio lista en el turno, y el rechazo solo
    lo sugiere entonces, recordando la lista. La 204, ampliada y en rojo primero.
  - **El cableado del tope no tenía juez**: cambiar `Anota(…, !fallo, …)` por `true` dejaba el contrato
    intacto. La decisión pasó a `TopeDeIntentos.Despues`, que la 204 juzga.
  - **Dos salidas de `map_type` por UIA seguían sin mano** («no hay campo con el foco», «NO escribo»).
    Ahora la dejan, como intento fallido. Sin promesa: necesitan el foco de una pantalla real.
  - **El juez del nivel 4 aprobaba por destino**, más laxo que esta spec. Ahora exige a lo sumo un
    intento fallido en toda la petición, y enseña si miró antes de actuar.
  - **Lo que queda, y se dice**: el nivel 4, que bloquea el merge porque el ejecutor es también el de
    SAP; las tareas largas; los dos segundos; el tercer Copiar o Guardar al mismo destino, que se frena
    porque «logrado» es «cambió» (la mano de tres estados); y las tres cuentas de «intento».
  Su veredicto entero va en el PR.

- **2026-09-11 (el sabotaje, tercera ronda).** Los mismos tres cuidados.

  | Promesa | Sabotaje | Quedó |
  |---|---|---|
  | 204 | `which` sin lista vuelve a abrir una clave nueva | roja |
  | 204 | una excepción deja de contar como intento fallido | roja |
  | 204 | la lista de homónimos deja de recordarse | roja |
  | 204 | el rechazo sin lista vuelve a sugerir `which` | roja |
  | 204 | *(repetido)* el destino se compara como texto crudo, sin aplanar | roja, **y también la 205** |
  | 204 | *(repetido)* el candidato elegido deja de ser parte del destino | roja |
  | 207 | *(repetido)* «logrado» vuelve a ser «terminó» | roja |
  | 207 | *(repetido)* la lista vuelve a contar como intento | roja |
  | juez del nivel 4 | se quita «a lo sumo un fallido por petición» | la autoprueba falla (T3 pasa a aprobada) |
  
  Ocho de ocho en rojo sobre el código final, cada una comprobada por diff de bytes y restaurada idéntica;
  recompilado al final: **CONTRATO INTACTO, 176 juzgadas**. Y el juez del nivel 4 tiene ahora su propia
  autoprueba con logs sintéticos (10 de 10), que también se rompió a propósito y lo notó.
- **2026-09-11, 02:00 (el crítico, tercera pasada: 5/10).** Sobre `7692795`. Confirmó con una sonda sobre
  el `U.dll` compilado que `which` sin lista ya no esquiva el tope, y que el juez aprueba por petición. Y
  encontró:
  - **Tras una lista, el mismo botón admitía cuatro intentos.** El rechazo trae la lista con el selector
    de cada candidato; pedir ese selector tras dos fallos con `which=2` abría una clave nueva, y el
    ejecutor pulsaba el mismo botón. Contradecía el enunciado de la 204. Ahora los candidatos viajan como
    datos del ejecutor a la mano y al tope (`Resultado.Candidatos` → `Mano.Candidatos` → `Despues`), y
    el selector de un candidato es ese candidato. La 203, la 204 y la 207, ampliadas y en rojo primero.
  - **`which=02` o `+2` abrían otra clave**: ahora el número se lee como lo lee el ejecutor.
  - **El juez no reconocía las dos salidas nuevas de `map_type`**: ahora sí.
  - **La autoprueba del juez no estaba en el repo**, aunque el PR decía que existía. Ahora vive en
    `scripts/nivel4-voz/autoprueba.py`, con un caso por regla (10/10), y se rompió a propósito dos veces.
  - **Los límites no decían que el eco vacía el tope.** Ahora sí (arriba).
  Su veredicto entero va en el PR.

- **2026-09-11 (el sabotaje, cuarta ronda).** Los mismos tres cuidados, y un sabotaje del cableado que se
  sabía sin juez, corrido para medir el límite en vez de suponerlo.

  | Promesa | Sabotaje | Quedó |
  |---|---|---|
  | 203 | la lista deja de viajar como datos | roja |
  | 203 | los candidatos viajan en otro orden que su número | roja |
  | 204 | tras la lista, el selector de un candidato vuelve a ser otra clave | roja |
  | 204 | `which` vuelve a compararse como texto (02 no es 2) | roja |
  | 204 | *(repetido)* `which` sin lista vuelve a abrir una clave nueva | roja |
  | 204 | *(repetido)* una excepción deja de contar como intento fallido | roja |
  | 204 | *(repetido)* la lista de homónimos deja de recordarse | roja |
  | 204 | *(repetido)* el rechazo sin lista vuelve a sugerir `which` | roja |
  | 204 | *(repetido)* el destino se compara como texto crudo, sin aplanar | roja, **y también la 205** |
  | 207 | *(repetido)* «logrado» vuelve a ser «terminó» | roja |
  | 207 | *(repetido)* la lista vuelve a contar como intento | roja |
  | 207 | la mano deja de llevar los candidatos | roja |
  | — | **cableado**: la voz no le pasa los candidatos al tope | **INTACTO, como se esperaba**: el límite declarado, medido |
  | juez del nivel 4 | deja de reconocer «no hay campo con el foco» y «NO escribo» | la autoprueba falla (T1 r2 pasa a aprobada) |
  
  Doce de doce en rojo sobre el código final, cada una comprobada por diff de bytes y restaurada idéntica;
  recompilado al final: **CONTRATO INTACTO, 176 juzgadas**. En total, 19 sabotajes distintos del contrato
  en cuatro rondas, todos en rojo; y uno del cableado, que el contrato no ve y así se dice.
- **2026-09-11, 02:40 (el crítico, cuarta pasada: 5/10).** Sobre `0fb58b2`. Dio por hechos `which=02`, los
  dos fallos de `map_type` en el juez, la autoprueba en el repo y el eco declarado. Y con una sonda sobre
  el `U.dll` compilado, con el ejecutor real y el mismo cableado que la voz, encontró:
  - **Tras una lista, «selector + which=N» esquivaba el tope sin límite**: el ejecutor pulsa el selector
    exacto e ignora `which`, y el tope lo guardaba como `descargas#N`. Seis toques al mismo botón; y los
    fallos se anotaban en la clave de otro candidato, que quedaba frenado sin haberlo tocado.
  - **Con selectores de SAP, la lista no se encontraba nunca**: no se aplanan a la etiqueta. Cuatro toques.
  - **La 204 elegía el caso que pasa**: nunca cruzaba la tanda real de la 203 con el tope.
  Arreglado aplicando las reglas del ejecutor: el selector exacto de un candidato de cualquier lista del
  turno es ese candidato, con o sin `which`; y la 204 cruza ahora los candidatos reales de la tanda de la
  203 con el tope. En rojo primero, por las tres razones. Lo que queda de la misma clase —la etiqueta
  difusa— se declara arriba, con su arreglo de fondo.

- **2026-09-11 (el sabotaje, quinta ronda).**

  | Promesa | Sabotaje | Quedó |
  |---|---|---|
  | 204 | el selector se busca solo en la lista de su nombre (los de SAP no la encuentran) | roja |
  | 204 | `which` deja de quitarse antes de buscar el selector | roja |
  | 204 | el selector de un candidato deja de reconocerse | roja |
  | 204 | *(repetido)* `which` sin lista vuelve a abrir una clave nueva | roja |
  | 204 | *(repetido)* `which` vuelve a compararse como texto | roja |
  | 204 | *(repetido)* la lista de homónimos deja de recordarse | roja |
  | 204 | *(repetido)* el destino se compara como texto crudo, sin aplanar | roja, **y también la 205** |
  | 203 | *(repetido)* la lista deja de viajar como datos | roja, **y ahora también la 204** |
  
  Ocho de ocho en rojo, cada una comprobada por diff de bytes y restaurada idéntica; recompilado al final:
  **CONTRATO INTACTO, 176 juzgadas**. Que romper la 203 tumbe ahora también la 204 es lo que la cuarta
  pasada echaba en falta: el contrato cruza por fin la tanda real del ejecutor con el tope. En total, 21
  sabotajes distintos del contrato en cinco rondas, todos en rojo, y uno del cableado, medido INTACTO y
  declarado.

## Cierre

- [ ] Fase 0, la base con el binario de `main`: **no se pudo medir de noche** (salvapantallas OLED); el kit la corre junto a la rama
- [x] Promesas 202–207 verdes; las 170 anteriores intactas (176 juzgadas, 0 rotas)
- [x] Cada una rota a propósito, comprobada por diff, restaurada y recompilada después
- [ ] Nivel 4, base y rama en las mismas tareas: **pendiente, con alguien delante** (`scripts/nivel4-voz/correr.ps1`)
- [ ] El crítico de fidelidad al audio, con su veredicto pegado
- [ ] Estado: **implementado** (AAAA-MM-DD)
