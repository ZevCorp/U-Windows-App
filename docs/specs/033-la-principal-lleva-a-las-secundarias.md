# Plan de implementación: la principal lleva a las secundarias a su escritorio, y habla con ellas

Estado: **propuesto** · Nace del diagnóstico del 2026-09-17 · Rama: `jose/la-principal-lleva-a-las-secundarias` (desde `main`, después de la 032)

> El dueño: «en la misma interfaz incrustamos varias caritas: tres a cada lado y la principal al
> centro. La principal es la orquestadora con la que hablas. La ventana de la aplicación se
> desplaza hasta otro escritorio, la principal va al lado de una secundaria, la coge, la arrastra
> y la suelta en ese escritorio, y vuelve a entrar en la aplicación. Luego la aplicación navega
> hasta otro escritorio y deja a otra. La principal habla con las otras y me da el contexto de lo
> que pasa en los otros escritorios; si un agente necesita ayuda, se lo dice a la principal y la
> principal me habla a mí.»

Tercera pieza del camino de la [031](031-la-consulta-lleva-la-carita-a-otro-escritorio.md). Se
apoya en todo lo anterior y no repite nada: el anfitrión y la silla (272), el viaje (274), el
asistente como objeto con su ruta MCP (278), y la cerca (275–277).

## Diagnóstico: qué se midió

Del código en `origin/main` (`a2f972a`), 2026-09-17.

| Qué | Medida | Fuente |
|---|---|---|
| Cómo habla hoy un cerebro con la persona **a mitad de tarea** | por dos herramientas MCP: `voz_decir` habla por la voz prestada, y `voz_preguntar` **bloquea hasta 60 s** esperando que la persona conteste por voz; corren en el hilo del servidor MCP | `SurfaceMapTools.cs:1822`, `:1881-1882`; `FaceWindow.xaml.cs:1091-1094`, `:3251`, `:3070-3075`, `:5580`; `Piloto/VozPrestada.cs:23` |
| Qué le llega a la app del cerebro además | texto por stdout, una línea JSON por evento, recortado a 160 caracteres para el estado; y el texto final de la corrida | `ElPiloto.cs:86-102`; `FaceWindow.xaml.cs:3107-3112`, `:3136-3141`, `:5461-5463` |
| Cómo recibe un cerebro su tarea | `mensaje.json` en su carpeta: bloques de texto, caja de herramientas, prohibidas, modelo y URL de MCP; para un encargo, la nota marcada más el catálogo de skills comprobadas | `ElPiloto.cs:31`, `:49-54`; `Piloto/MensajeDelEncargo.cs:26`, `:50`; `CajasDelPiloto.cs:72-77` |
| Cuántos cerebros pueden correr a la vez | los que se lancen: `ElPiloto` no guarda estado; lo que hoy lo impide son dos banderas de la carita que ni siquiera se coordinan entre sí | `ElPiloto.cs:26`; `FaceWindow.xaml.cs:2844`, `:5411`, `:71` |
| Qué SDK es el cerebro | Node, `@anthropic-ai/claude-agent-sdk`, `query` con reanudación por `session_id` y hasta 120 turnos | `agente-piloto/package.json:7-8`, `piloto.mjs:41`, `:160-172` |
| Cómo se mueve la carita hoy de un punto a otro | `MoverConMuelle` (260–720 ms según la distancia) y `ViajarAlClic`; el arrastre a mano es `DragMove` | `FaceWindow.xaml.cs:1665-1697`, `:2002-2019` |
| Qué hay en el centro de la consulta | la fila 2, un `ScrollViewer` con la nota, la lista o los aprendizajes; el hueco de la silla y el botón los pone la 031 | `ConsultaWindow.cs:195-200`, `:405-415`; spec 031 fases 3 y 4 |

## Por qué esto va dirigido por especificación

Porque «la principal me cuenta lo que pasa en los otros escritorios» es lo más fácil de fingir
que hay: un modelo con un resumen plausible y sin dato. La promesa obliga a que lo que cuenta
salga del estado real de cada asistente —dónde está, qué hace, su último paso con hora—, y a que
«necesito ayuda» tenga un camino que llega y una respuesta que vuelve, no un bloqueo de 60 s en un
escritorio que nadie mira.

**La regla del flujo, y no tiene excepciones:** ninguna línea de producción entra antes que la
promesa que la juzga. Cada fase empieza con el contrato ROTO y termina con el contrato INTACTO.

## El diseño

**Siete sillas.** En el centro de operaciones hay una silla central, la de la principal (la de la
031), y seis laterales, tres a cada lado, más pequeñas. Una silla lateral vacía es un hueco tenue;
pulsarlo crea una secundaria, con nombre («Asistente 2»…, editable), sentada ahí. Cada secundaria
sentada lleva debajo su botón «Llevar a…», el mismo de la 273, con los escritorios por nombre y
«Uno nuevo». Una secundaria fuera del centro tiene en su silla su nombre y una línea de estado.

**El viaje de una secundaria, en cinco movimientos** (`ReglaDelViaje.Coreografia`, pura: devuelve
los movimientos; `Coreografo` los ejecuta con las animaciones que ya existen):

1. El centro de operaciones viaja al destino con la 274: fijado si se puede, y llega cuando el
   sistema lo dice.
2. La principal se levanta de su silla y va a la silla de la secundaria (`MoverConMuelle`).
3. La toma —las dos caritas se mueven juntas— y la lleva fuera de la ventana, hasta el sitio
   donde la secundaria va a vivir en ese escritorio (su borde, con `ReglaDelMuelle.SitioAlSacar`).
4. La suelta: la secundaria se posa con su animación, y desde ese instante su escritorio es ese
   (270) y su cerca está puesta (275–277).
5. La principal vuelve a su silla, y el centro de operaciones **vuelve a casa**: al escritorio de
   donde salió, con la 274 al revés. Si hay más secundarias en cola, encadena antes de volver.

Nadie arrastra con el ratón: es una coreografía que se mira. Se puede parar con Escape, y parar
deja a la secundaria donde estaba en ese movimiento y lo dice.

**La secundaria trabaja allí.** Nace sin cerebro. Lo recibe con su primer encargo, que le da la
principal: se arranca su piloto con su ruta MCP (278) y con el encargo como mensaje. Su carita
enseña el estado (el aro mientras trabaja, como el notch) y su último paso en una línea.

**La principal habla con las secundarias.** Tres herramientas nuevas en la caja de la principal,
y solo en la suya: `asistentes_estado` (cada uno: nombre, escritorio, qué hace, último paso con
hora, y si espera algo), `asistente_encargar(nombre, tarea)` (crea la secundaria si no existe,
la sienta, y si ya está en un escritorio le arranca o le cambia el encargo) y
`asistente_responder(nombre, respuesta)`. Cada asistente tiene un **buzón**: una lista con hora de
lo que dijo y lo que pide. `asistentes_estado` es lo que la principal lee cuando la persona le
pregunta «¿qué pasa en SEO?», y la promesa exige que lo que cuenta salga de ahí.

**Pedir ayuda.** `voz_preguntar` desde una secundaria ya no bloquea 60 s a ciegas: deja la
pregunta en su buzón con estado «espera respuesta», la principal la recibe como un mensaje suyo
(«el asistente de SEO pregunta: …»), se lo dice a la persona por la voz, y la respuesta vuelve
por `asistente_responder`. La secundaria espera con plazo (los 120 s de una herramienta, ya
existentes); si nadie contesta, lo sabe y sigue o para, y lo dice. `voz_decir` desde una
secundaria va al buzón y a su línea de estado, no a la voz.

**Una sola aplicación.** Un instalador, un icono, una `U.exe`. Cerrar el centro de operaciones
cierra a las secundarias, con aviso si alguna está a medias. Las secundarias no aparecen en la
barra de tareas; el centro sí, como la consulta hoy.

## La especificación

Numeración a continuación de la 032; se confirma en `/promesas`.

| # | Promesa | Fase |
|---|---|---|
| 280 | el centro de operaciones tiene siete sillas: la central de la principal y seis laterales, tres a cada lado; una lateral vacía se ve como hueco y pulsarla crea una secundaria con nombre, sentada ahí; una sentada lleva debajo su botón de llevar, y una que está fuera enseña en su silla su nombre y su estado | 1 |
| 281 | llevar una secundaria es una coreografía de cinco movimientos en este orden: el centro viaja, la principal se levanta y va a su silla, la toma y la lleva fuera hasta su sitio, la suelta y la secundaria se posa, y la principal vuelve a su silla y el centro vuelve a casa; con varias en cola, las encadena antes de volver; Escape la para y deja a cada una donde estaba, y lo dice | 2 |
| 282 | soltada en un escritorio, la secundaria trabaja allí: su escritorio es ese, su cerebro arranca con su primer encargo y su ruta propia, y su carita enseña qué hace y su último paso | 2 |
| 283 | la principal tiene tres herramientas que las secundarias no tienen —estado de los asistentes, encargar, responder—, y cada asistente tiene un buzón con hora de lo que dijo y lo que pide | 3 |
| 284 | lo que la principal cuenta de otro escritorio sale del estado real de ese asistente —nombre, escritorio, qué hace, último paso con hora, si espera algo—, y sin ese dato dice que no lo tiene en vez de inventarlo | 3 |
| 285 | cuando una secundaria pregunta, no bloquea a ciegas: la pregunta va a su buzón, la principal la recibe como mensaje suyo y la dice a la persona, la respuesta vuelve por la principal, y sin respuesta en el plazo la secundaria lo sabe y lo dice | 4 |
| 286 | el usuario ve una sola aplicación: una instalación, un icono, un proceso; las secundarias no salen en la barra de tareas; cerrar el centro cierra a todas, con aviso si alguna está a medias | 5 |

La que cierra el asunto es la **284**: es la diferencia entre un orquestador y un narrador.

### Con qué se juzga cada una

- **280** — `ReglaDeLasSillas` (pura): siete sitios con su geometría relativa a la caja del
  centro; qué se ve en cada silla según su estado (vacía / sentada / fuera); 272 intacta.
- **281** — `ReglaDelViaje.Coreografia(secundaria, destino, cola)` devuelve los movimientos en
  ese orden, la vuelta a casa al final, y `Parar(movimiento)` el estado que deja.
- **282** — `Asistente` falso: tras `Soltar(escritorio)` su escritorio es ese y `Encargar` arranca
  un cerebro falso con su URL; su estado enseña el último paso.
- **283** — las cajas de herramientas (`CajasDelPiloto`) de principal y secundaria: las tres
  solo en la principal; el buzón guarda con hora y en orden.
- **284** — `ContarElEstado(asistentes)` con datos falsos devuelve exactamente esos datos, y con
  un asistente sin dato devuelve «no lo tengo»; el texto no contiene nada que no esté en la
  entrada.
- **285** — máquina de estados de una pregunta: buzón → principal → persona → respuesta → buzón,
  y el plazo vencido deja el estado «sin respuesta» y no otro.
- **286** — las ventanas de las secundarias tienen `ShowInTaskbar=false`; `Cerrar(centro)` con
  una secundaria a medias devuelve «avisar» y no cierra sola.

Sobre la máquina: dos secundarias llevadas a «SEO» y a «Anuncios» encadenadas, con el centro
volviendo a casa; un encargo a la de «SEO» que abre la Tienda y cierra una ventana por patrón
mientras la persona está en casa; «¿qué pasa en SEO?» contestado con su último paso y su hora; y
una pregunta desde «SEO» que llega por la voz de la principal y vuelve. Todo con horas en el log.

### Límites dichos, no escondidos

- **La secundaria oye a la persona solo cuando la tiene delante.** En su escritorio, un clic o el
  doble Ctrl hablan con ella (279); desde cualquier otro, pide y dice por la principal, y no mira
  la pantalla (277).
- **Las secundarias no se hablan entre sí**: todo pasa por la principal. Es lo que hace que la
  persona tenga un solo interlocutor.
- **La coreografía se mira, no se arrastra**: la carita no responde al ratón durante el viaje.
- **Sin persistencia**: al reiniciar, las secundarias no existen; los encargos a medias se
  pierden con aviso (286).

## Las fases

### Fase 1 — las siete sillas (280)

| | |
|---|---|
| **Qué toca** | `ConsultaWindow.Anfitrion.cs` (de la 031: seis huecos más y su regla), `windows-client/src/Ui/ReglaDeLasSillas.cs` (nuevo, puro), `CaritaSecundaria` (de la 032) sentada |
| **Terminado** | 280 verde; 272 y 273 intactas |

### Fase 2 — el viaje de una secundaria (281, 282)

| | |
|---|---|
| **Qué toca** | `ReglaDelViaje.cs` (`Coreografia`, `Parar`), `windows-client/src/Ui/Coreografo.cs` (nuevo: los movimientos con `MoverConMuelle` y el viaje de la 274, ida y vuelta), `Asistente.cs` (`Soltar`, `Encargar`), `ElPiloto.cs` (arranque con nombre) |
| **¿Núcleo congelado?** | no; zona de choque alta (`FaceWindow.xaml.cs`): rama corta y aviso |
| **Terminado** | 281 y 282 verdes; 274 intacta |

### Fase 3 — la principal habla con las secundarias (283, 284)

| | |
|---|---|
| **Qué toca** | `CajasDelPiloto.cs` (tres herramientas solo en la principal), `SurfaceMapTools` de la principal (despacho), `windows-client/src/Navigation/Buzon.cs` (nuevo), `ContarElEstado.cs` (nuevo, puro), las instrucciones de la principal (`ConversacionEnVivo.cs:1295`, el catálogo de herramientas `:914`) |
| **Terminado** | 283 y 284 verdes |

### Fase 4 — pedir ayuda (285)

| | |
|---|---|
| **Qué toca** | `SurfaceMapTools.cs:1881-1882` (`voz_preguntar`/`voz_decir` de una secundaria van al buzón), `FaceWindow.xaml.cs:3251` (`PreguntarYEsperar` con destino), `Buzon.cs` (estados de una pregunta) |
| **Terminado** | 285 verde |

### Fase 5 — una sola aplicación (286)

| | |
|---|---|
| **Qué toca** | `CaritaSecundaria` (`ShowInTaskbar=false`), el cierre del centro (`ConsultaWindow.cs:488`, `App.xaml.cs:110-128`), `ReglaDelCierre.cs` (nuevo, puro) |
| **Terminado** | 286 verde |

## Lo que NO entra

- Que las secundarias se hablen entre sí. Voz propia sí tienen, pero solo cuando las miras (279).
- Persistir secundarias, escritorios y encargos entre reinicios.
- Elegir modelo o caja de herramientas por secundaria: todas nacen con la caja del encargo.

## Hallazgos

<!-- Se rellena DURANTE la implementación. -->

## Cierre

- [ ] Todas las promesas verdes (`.\scripts\contrato-del-grafo.ps1` → CONTRATO INTACTO)
- [ ] `.\scripts\verificar.ps1` pasa, con evidencia en `out\evidencia.md`
- [ ] Probado con dos secundarias en dos escritorios con nombre: …
- [ ] Estado de este documento: **implementado** (AAAA-MM-DD)
