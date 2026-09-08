# Plan de implementación: el botón del collar — un toque pausa, dos terminan

Estado: **en curso** · Nace de la petición del 2026-09-07 · Rama: `jose/el-boton-del-collar-graba`

Dos peticiones del dueño el mismo día. La primera: *«que al oprimir el botón del collar se empiece
a grabar el botón grabar en vez de que la carita escuche»*. La segunda, horas después, **cambia la
primera**: *«con doble toque en el collar se finalice la grabación, y con un solo toque se pause»*,
más *«analiza tú mismo cómo debe funcionar la vibración del collar para entregar feedback»*.

Por eso la promesa 184 se **retira** en vez de corregirse: decía que pulsar grabando *para*, y
ahora pausa. Ver *Lo que se retira*.

## Diagnóstico: qué se midió

Todo el 2026-09-07. Lo del collar sale de **una sonda de solo lectura** escrita para esto
(aprendizaje nº13), no de deducir del código.

| Qué | Medida | Fuente |
|---|---|---|
| ¿El collar distingue un toque de dos? | **No.** Manda `0x01` al apretar y `0x05` al soltar, y nada más | comentario medido el 2026-08-13 en `FuenteOmi.cs:35-38`, confirmado hoy |
| **¿El collar puede vibrar?** | **NO. No publica ninguna característica háptica** | sonda: los **12** servicios de este CV1, enumerados |
| ¿Y la app oficial de Omi, puede? | **Tampoco**: su lista de UUID no tiene `haptic`, `vibration`, `motor` ni `LED` | `app/lib/services/devices/models.dart` de `BasedHardware/omi` |
| ¿Es un olvido de esa lista? | **No**: Omi tiene una recompensa abierta de 500 $ titulada *«Simulate Haptics with Speaker»* | issue #573 de `BasedHardware/omi` |
| ¿Qué SÍ tiene el collar? | **Altavoz** (`cab1ab95-2ea5-4f4d-bb56-874b72cfc984`), audio, botón, batería, almacenamiento, hora, y **3 servicios sin identificar** | sonda |
| Los 12 servicios, enteros | `1801` · `1800` · `180f` batería · `180a` info · **`8d53dc1d…` ?** · `23ba7924` botón · `cab1ab95` **altavoz** · `19b10000` audio · **`19b10010…` ?** · **`19b10020…` ?** · `19b10030` hora · `30295780` almacenamiento | sonda |
| ¿Existe «pausar» en la consulta? | **No.** Los estados son `SinEmpezar · Grabando · Transcrita · GenerandoNota · NotaLista · Fallida` | `Clinical/Consulta.cs:7-27` |
| **Qué pasa si se pausa parando y volviendo a arrancar** | `ArrancarAsync` empieza con `Dicho.Limpiar()`: **se borra todo lo dicho antes de la pausa** | `Transcripcion/DictadoEnVivo.cs:96` |
| ¿El socket del dictado aguanta una pausa larga? | **No hay latido**: ni `keepalive` ni `ping` en todo el módulo | `grep` sobre `Clinical/Transcripcion/` |
| Cómo se para hoy | `PararAsync` manda el `finalize`, recoge la cola y cierra | `DictadoEnVivo.cs:159-175` |
| `TerminarAsync` exige estar grabando | `if (Estado != Grabando) { Motivo = "no hay ninguna grabación en marcha"; return; }` | `Consulta.cs:184` |
| Sonidos que la app ya tiene | **dos**: `tick.wav` y `mic_chime.wav`, cargados de `assets/` | `FaceWindow.xaml.cs:2153` |
| La metáfora del sonido, ya establecida | *«dos notas que SUBEN, escuchar = abrirse»* | `FaceWindow.xaml.cs:2103` |

**El hallazgo que decide el diseño del feedback: el collar no puede vibrar, y no es un límite
nuestro.** Lo que el dueño pidió —vibración— no existe en este hardware por ningún camino: ni el
aparato la publica, ni la app oficial la conoce, y la propia Omi paga por *simular* háptica con el
altavoz. Fingirlo sería el peor resultado posible: un feedback que la interfaz promete y el cuerpo
no siente.

**El hallazgo que decide la forma de la pausa:** `ArrancarAsync` llama a `Dicho.Limpiar()`. Pausar
de la forma obvia —parar y volver a arrancar— **borra en silencio todo lo dicho antes de la
pausa**. El médico pausa, reanuda, termina, y la nota sale con la mitad de la consulta. Es
exactamente *«lo peor no es que falle: es que parezca que funcionó»*, y no se ve hasta leer la nota.

## Por qué esto va dirigido por especificación

Porque el fallo que acecha es **invisible**: una pausa que pierde lo dicho no da error, no cambia
la pantalla, y solo se descubre leyendo la nota final de una consulta real que ya no se puede
repetir. Una prueba escrita después mediría «se pausó y se reanudó», que es justo lo que el bug no
rompe.

**La regla del flujo, y no tiene excepciones:** ninguna línea de producción entra antes que la
promesa que la juzga.

## El diseño

### Los gestos, y por qué el toque actúa YA

El collar no distingue toques: los cuenta la app dentro de una ventana. Eso plantea la disyuntiva
que la promesa 147 ya resolvió una vez para la carita: **esperar la ventana para saber si hubo un
segundo toque hace que el primero se sienta roto.**

Aquí se resuelve al revés que en un menú, y se puede porque **pausar es un prefijo compatible de
terminar**: las dos cosas paran el audio.

- El **primer** toque actúa en el acto: pausa (o reanuda, o empieza).
- Si llega un **segundo** dentro de la ventana, termina.

Un doble toque, entonces, hace «pausa… y cierre». No se pierde nada: el audio se cortó en el primer
toque, que es donde el médico quiso cortarlo. Y cada toque tiene respuesta inmediata, que es lo que
hace que un botón que no se ve se sienta vivo.

**La ventana es de 400 ms y no de 250** como la de la carita: aquel número es para un ratón, y este
botón se pulsa con un dedo, sobre una pieza pequeña que cuelga, a veces por encima de la ropa.

**Un doble toque no puede terminar una grabación que acaba de empezar.** Sin esa guarda, dos toques
desde parado abrirían la consulta y la cerrarían 300 ms después, generando una nota de nada. La
regla: terminar exige que la grabación sea **más vieja que la ventana**.

### El feedback: lo que el hardware deja, dicho sin adornos

La vibración **no se puede hacer**. Lo que queda es el sonido, y hay dos canales posibles:

| Canal | Estado |
|---|---|
| **El PC** (`SoundPlayer`, ya existe) | Funciona hoy. El médico está en la sala con el equipo |
| **El altavoz del collar** (`cab1ab95…`) | Existe en el aparato, pero **su protocolo no está documentado** y escribirle bytes a ciegas no se hace. Queda como *deuda con nombre* |

Se implementa el del PC, y el lenguaje se diseña para que sirva igual el día que el collar suene.

**El principio: el sonido dice el ESTADO en el que quedas, no la acción que hiciste.** Quien no mira
la pantalla necesita saber una sola cosa —¿me está grabando?— y necesita que esa respuesta suene
siempre igual.

| Momento | Sonido | Por qué |
|---|---|---|
| **En vivo** (empieza **o reanuda**) | dos notas que **suben** | «estás en vivo» suena idéntico las dos veces: es el hecho que importa, y no puede depender de cómo llegaste a él. Reusa la metáfora que el repo ya fijó: subir = abrirse |
| **Pausa** | **un** toque corto y neutro | mismo número que un toque, menos energía: pausa es «menos que en vivo» |
| **Termina** | dos notas que **bajan** | el espejo exacto de empezar: bajar = cerrarse. Y es el único momento sin vuelta atrás |
| **No se pudo** | dos notas **graves y cortas** | un toque que no hace nada es indistinguible de uno que no llegó, y esa duda es lo peor que le puede pasar a un botón que no se ve |

El último no estaba en la petición y es el que más protege: sin él, el médico que pulsa y no oye
nada no sabe si pausó, si el collar se desconectó, o si el botón falló.

## La especificación

| # | Promesa | Fase |
|---|---|---|
| ~~184~~ | ~~el botón graba la consulta… y pulsarlo grabando para~~ — **RETIRADA**: el dueño cambió el gesto el mismo día. El número no se recicla | — |
| 185 | el collar no distingue un toque de dos, así que los cuenta la app: dos toques dentro de la ventana son **un** gesto de terminar, y separados son dos gestos distintos | 1 |
| 186 | un toque pausa lo que se está grabando y otro lo reanuda; dos toques terminan — pero nunca terminan una grabación que empezó en ese mismo golpeteo, y el botón sigue sin poder abrir la voz de la carita | 1 |
| 187 | **pausar no pierde lo dicho**: al reanudar se sigue el mismo verbatim, y lo de antes de la pausa sigue estando en la nota | 2 |
| 188 | los tres momentos suenan distinto, y «estás en vivo» suena **igual** al empezar que al reanudar; un toque que no puede hacer nada también suena | 3 |

**La que cierra el asunto es la 187.** Mientras pausar pueda borrar lo dicho, las otras tres son
cosméticas: el gesto sería perfecto y la consulta saldría partida sin que nadie se entere.

### Con qué se juzga cada una

Reglas puras que la interfaz consulta, juzgadas por reflexión sin collar y sin pantalla — el mismo
camino que `ReglaDelToque` (147) y `ReglaDelMuelle` (148):

- **185 y 186** — `U.WindowsClient.Ui.ReglaDelBotonDelCollar.AlPulsar(hayConsulta, grabando, pausada, msDesdeElToqueAnterior, msGrabando)`. Falsifica: terminar con los toques separados; terminar una grabación recién empezada; cualquier respuesta que nombre la voz.
- **187** — `DictadoEnVivo` gana `PausarAsync`/`ReanudarAsync`, y la promesa comprueba **sobre el verbatim** que lo dicho antes de la pausa sigue ahí después de reanudar. Falsifica: que `ReanudarAsync` llame al `Limpiar` que hace `ArrancarAsync`.
- **188** — `U.WindowsClient.Ui.ReglaDelSonidoDelCollar.Para(momento)`. Falsifica: que empezar y reanudar suenen distinto; que dos momentos compartan sonido; que «no se pudo» sea silencio.

**Lo que el contrato no puede juzgar** y va al nivel 4, con el log en el PR: que el collar de verdad
mande los dos toques dentro de 400 ms cuando una persona los da, y que los cuatro sonidos se
distingan al oírlos.

## Las fases

| Fase | Promesa | Qué toca | Terminado |
|---|---|---|---|
| **0** | ninguna — arnés | `tests/ContratoDelGrafo/Contrato.cs` | 185-188 **ROJAS**, 184 retirada, 1..183 intactas |
| **1** | 185, 186 | `src/Ui/ReglaDelBotonDelCollar.cs`, `src/Ui/FaceWindow.xaml.cs` | 185-186 verdes |
| **2** | 187 | `src/Clinical/Transcripcion/DictadoEnVivo.cs`, `src/Clinical/Consulta.cs`, `src/Ui/ConsultaWindow.cs` | 187 verde |
| **3** | 188 | `src/Ui/ReglaDelSonidoDelCollar.cs` (nuevo), `src/Ui/FaceWindow.xaml.cs` | 188 verde |
| **4** | ninguna — nivel 4 | — | probado con collar real, log en el PR |

## Lo que NO entra

| Fuera | Por qué |
|---|---|
| **Hacer vibrar el collar** | **No se puede.** Medido: ni el aparato publica háptica, ni la app oficial la conoce, y Omi paga por simularla con el altavoz. No es una decisión, es el hardware |
| **Sonar por el altavoz del collar** | El servicio existe (`cab1ab95…`) pero su protocolo no está documentado, y escribir bytes a ciegas en una característica desconocida no se hace. Es la deuda con nombre de abajo |
| Los 3 servicios sin identificar | `8d53dc1d…`, `19b10010…`, `19b10020…`. Se anotan; identificarlos es otra sonda |
| Latido en el socket del dictado | La pausa cierra el socket a propósito, así que no hace falta. Si algún día se pausa sin cerrar, hará falta |
| Pausar desde la pantalla | Esta spec es del botón. El botón «Grabar» sigue empezando y terminando como hoy |

## Deuda con nombre: el altavoz del collar

El feedback correcto para un aparato que se lleva puesto **está en el aparato**, no en el PC: el
médico puede estar de espaldas al equipo o fuera de la sala. Este collar tiene altavoz y hoy no lo
usamos.

Lo que falta para usarlo, y por qué no se hace aquí: el protocolo de `cab1ab96-…` no está en la
documentación de Omi ni en el `models.dart` que sí dio los UUID. Averiguarlo es leer el
`transport.c` del firmware y probar contra el aparato — una sonda propia, con su medición, no un
`Write` a ver qué pasa. Cuando exista, `ReglaDelSonidoDelCollar` ya dice **qué** suena en cada
momento; solo cambia **por dónde**.

## Hallazgos

- **2026-09-07** — El collar **no puede vibrar**, y la petición no se puede cumplir tal como se
  pidió. Confirmado por tres caminos independientes: la sonda sobre este CV1, la lista de UUID de la
  app oficial, y la recompensa abierta de Omi para *simular* háptica con el altavoz.
- **2026-09-07** — **`ArrancarAsync` limpia el verbatim.** Pausar de la forma obvia habría borrado
  lo dicho antes de la pausa, en silencio. Es la promesa 187 y es la razón de que esta spec exista.
- **2026-09-07** — El dictado **no tiene latido**, así que una pausa larga con el socket abierto lo
  habría matado igual. Cerrarlo al pausar no es solo más barato: es lo único que aguanta una pausa
  de minutos.
- **2026-09-07, sobre el método** — La sonda que lee valores y descriptores de cada característica
  **se cuelga** cuando otra instancia de Ü pelea por el collar (su bucle de reintento va cada 6 s).
  La versión que solo enumera servicios contesta en segundos. Para preguntarle al aparato, preguntar
  **lo mínimo**.

## Cierre

- [ ] 185-188 verdes y 184 retirada (`.\scripts\contrato-del-grafo.ps1` → CONTRATO INTACTO)
- [ ] Sabotaje comprobado en la 187: si reanudar limpia el verbatim, la promesa se pone roja
- [ ] Probado con el collar real: pausa, reanuda, termina, y una pausa larga (> 1 min)
- [ ] Los cuatro sonidos se distinguen a oído
- [ ] Estado: **implementado** (AAAA-MM-DD)
