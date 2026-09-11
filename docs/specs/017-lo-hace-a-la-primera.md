# Plan de implementación: lo que uno le pida, lo hace — y a la primera

Estado: **implementado; nivel 4 pendiente** (2026-09-11: 202–206 verdes y saboteadas; la prueba en el PC real no pudo correr de noche) · Nace de una nota de voz del 2026-09-10 ([transcripción y rúbrica](fuentes/2026-09-10-lo-hace-a-la-primera.md)) y del diagnóstico de esa noche · Rama: `jero/lo-hace-a-la-primera`

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

### Con qué se juzga cada una

Todas por reflexión, sin pantalla y sin modelo; nacen en rojo con `Pendiente(…, "017")`.

| # | Se juzga con | Sabotaje que la tiene que poner roja |
|---|---|---|
| 202 | `BatchCon` con mano falsa: con una ruta `a·s:1→b` la cuenta dice «ahora estás en «…b»»; sin ruta dice «no cambió»; y ya no aparece el «quedaste en» que tapaba el hecho (el prefijo «hice los» se queda: la demo lo lee) | reponer el cierre de `RecorrerSegunElNucleo.cs:183-185` |
| 203 | un grafo con dos «Descargas» vivas (TreeItem → b, TabItem → c): sin cuál, nada tocado y la cuenta trae «1)», «TreeItem», «2)», «TabItem»; con cuál=2 queda en c con un solo toque; y el catálogo declara `which` en `map_take` | ignorar `Cual` y tomar el primero; quitar el tipo del mensaje |
| 204 | una clase pura con la secuencia: dos `take` fallidos a «Descargas» —uno por nombre, otro por selector— y el tercero se rechaza con «dos»; otro destino pasa; un turno nuevo reinicia; tres logrados pasan; mirar nunca cuenta | `Admite` → siempre sí; comparar el texto crudo sin aplanar (aprendizaje nº16) |
| 205 | una clase pura con reloj inyectado: el resumen trae `llamadas=`, `distintas=`, `intentos_max=` y los ms | contar solo lo que devolvió resultado (el patrón nº10: el denominador es lo pedido) |
| 206 | el catálogo y las instrucciones como texto: sin `at`/`action` en `map_take`, sin `at` en `map_type`, `map_unblock` conserva `at`; sin «pide map_where_am_i primero» ni «más de dos veces»; con `which` y `map_look` juntos | reponer `at` en `map_take`; reponer la línea del `at` |

**Límites dichos, no escondidos.** La 203 no cubre dos homónimos **del mismo tipo**: se funden en un
selector antes de llegar al ejecutor (`Grafo.cs:136`). La 204 juzga la regla, no su cableado —un
guardia que se cree puesto (aprendizaje nº18)—; el cableado lo demuestra el nivel 4 con la línea del
rechazo. La 206 juzga el texto, no la conducta del modelo; la conducta la juzga el nivel 4.

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
- **Intentos:** acciones hacia el mismo destino dentro de la petición. Lectura estricta, la del audio:
  el «máximo dos intentos» es de **la petición**, no de cada acción suelta.
- **Tiempo por acción:** desde la línea de la llamada hasta su resultado. **≤ 2,0 s.**
- **Tiempo por petición:** desde que se entrega la orden hasta el resultado que deja el estado final.
  Se reporta siempre, diciendo que incluye la latencia del modelo.
- **Aprobado:** la tarea hecha, ≤2 intentos y cada acción ≤2,0 s. Si no, k/N con **el plan como
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
| R5, R6, R7 | T2, T1, T3/T5 | ninguna lógica con «Instagram» ni «Documentos» cableada: el diff se revisa con `grep` |
| **R8, R9, R10** | **no en esta rama** | ver «Lo que NO entra» |
| R11 | 202, en parte | cada acción dice si tuvo efecto; la tarea larga no se corre |
| R12, R15 | 204, 205; nivel 4 | |
| R13 | 202 (saber que no era por ahí); T5 | la voz no tiene un «atrás» genérico |
| R16 | nivel 4 | ninguna promesa mide tiempo |
| R17 | nivel 4, cruzado con el log | |
| R18 | no | pista secundaria, diagnosticada aparte |
| R19 | no se toca `voz/Realtime` ni la interrupción | |

## Lo que NO entra

- **Pasar las tareas largas al Agent SDK (R8–R10).** Es lo que el audio pone como el paso siguiente
  —*«ese creo que es el primer paso que tenemos que lograr, que lo que uno le pida lo haga»*— y no se
  construye esta noche por cuatro razones medidas: no existe ningún enrutado corto/largo; qué cuenta
  como «largo» es una decisión de producto; la credencial de esta máquina no está medida (la memoria del
  proyecto registra que la de equipo estaba bloqueada); y cada corrida del piloto cuesta entre 1,37 y
  4,06 USD en Opus (spec 013). Lo que ya existe para ello: `piloto/piloto.mjs`, que aquí no arranca (le
  faltan `node_modules` y apunta a otra máquina). Construir un enrutado que no se puede probar sería
  justo lo que este repo llama «parece que funcionó».
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
 
## Cierre

- [ ] Fase 0, la base con el binario de `main`: **no se pudo medir de noche** (salvapantallas OLED); el kit la corre junto a la rama
- [x] Promesas 202–206 verdes; las 170 anteriores intactas (175 juzgadas, 0 rotas)
- [x] Cada una rota a propósito, comprobada por diff, restaurada y recompilada después
- [ ] Nivel 4, base y rama en las mismas tareas: **pendiente, con alguien delante** (`scripts/nivel4-voz/correr.ps1`)
- [ ] El crítico de fidelidad al audio, con su veredicto pegado
- [ ] Estado: **implementado** (AAAA-MM-DD)
