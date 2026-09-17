# Plan de implementación: la consulta lleva a la carita a otro escritorio

Estado: **propuesto** · Nace del diagnóstico del 2026-09-17 · Rama: `jose/un-asistente-por-escritorio`

> El dueño: «quiero poder tener múltiples asistentes de mi Ü en múltiples escritorios virtuales de
> Windows. Dejar un asistente en cada escritorio, cada uno con su entorno aparte, que hagan
> exactamente lo que ya hacen. Quiero poder incrustar la carita en el centro de la ventana de la
> consulta —que abra con las dimensiones del pantallazo, no las verticales de ahora— y sacarla; y
> un botón debajo que diga "llevar a otro escritorio": la aplicación se queda enfrente mío, el
> escritorio cambia por detrás, la carita hace la animación de soltarse en el otro escritorio. Y
> es muy importante que la carita quede trabajando y operando en ese escritorio.»

Y el diseño final, dicho el mismo día: «la ventana de la consulta se convierte en el centro de
operaciones de los asistentes: la carita principal al centro, con la que hablas, y hasta tres a
cada lado; la principal lleva a cada secundaria a su escritorio, la suelta allí y vuelve; después
habla con ellas, te cuenta qué pasa en los otros escritorios, y si una necesita ayuda te lo dice.
El usuario tiene que sentir que hay **una sola aplicación**.»

**La decisión de fondo, tomada aquí: un solo proceso.** Todos los asistentes viven dentro de la
misma `U.exe`. No es solo que el instalador sea uno —eso también lo sería con varios procesos—; es
que con un proceso desaparecen de golpe las colisiones que el inventario de la 032 midió (puertos,
configuración, sesión del médico, ganchos de teclado, actualizador, el «aquí» del mapa), y la
conversación entre la principal y las secundarias es una llamada en memoria en vez de un canal
entre procesos. Lo que cada asistente necesita propio —su escritorio, su ventana de trabajo, su
cerebro, su carita— cabe en un objeto. Está argumentado en la 032.

El camino son **cuatro specs y cuatro ramas** (regla de `ramas-y-commits.md`: una spec = una rama,
de medio día a tres días), y cada una deja algo que se usa:

| Spec | Deja | Promesas |
|---|---|---|
| **031, esta** | la consulta con su tamaño, la carita sentada dentro, el botón, y el viaje del centro de operaciones a otro escritorio. Con una sola carita, es la petición original tal cual | 270–274 |
| [032](032-cada-asistente-trabaja-en-su-escritorio.md) | varios asistentes en un solo proceso, cada uno con su escritorio y su ventana de trabajo, y la cerca que hace cierto «trabaja en el suyo» | 275–279 |
| [033](033-la-principal-lleva-a-las-secundarias.md) | las siete sillas, la principal llevando a cada secundaria a su escritorio, y la conversación entre ellas | 280–286 |
| [034](034-medico-o-estudiante.md) | el rol de la cuenta, la nota normal del estudiante, y la nota entregada a la principal | 290–292 |

## Diagnóstico: qué se midió

Todo del 2026-09-17, sobre esta máquina (Windows 11 26200, pantalla al 125 %), con una sonda de
solo lectura contra la API pública de escritorios virtuales (`IVirtualDesktopManager`, la misma
que trae `shell32` desde Windows 10) y el registro, más el código en `origin/main` (`a2f972a`).

| Qué | Medida | Fuente |
|---|---|---|
| Escritorios virtuales de esta máquina | **5**, tres con nombre puesto por el dueño («Anuncios», «Whatsapp», «SEO») y dos sin nombre; el actual y el orden salen del registro | `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops` → `VirtualDesktopIDs` (16 bytes por GUID, en el orden de la vista de tareas), `CurrentVirtualDesktop`, y `Desktops\{guid}\Name` |
| En qué escritorio está cada ventana | `GetWindowDesktopId(hwnd)` contesta para **cualquier** ventana, propia o ajena, con su GUID; las ventanas de otro escritorio salen **cloaked = 2** (`DWM_CLOAKED_SHELL`) y `IsWindowVisible` sigue diciendo que sí | sonda, 12:58 |
| Mover una ventana **propia** a otro escritorio | `MoveWindowToDesktop` → `S_OK` en **5 ms**; la ventana queda cloaked = 2, «en el actual» = no, y el escritorio a la vista **no cambia** | sonda, 13:04 |
| Mover una ventana **ajena** (`charmap.exe`, Win32 clásico) | `E_ACCESSDENIED` (`0x80070005`): la API pública solo mueve ventanas del propio proceso | sonda, 13:01 |
| La ventana del pantallazo | `ConsultaWindow`, sin XAML, **470×660** con `CenterScreen`, sin persistencia de tamaño; la tarjeta visible del pantallazo mide **1180×765 px al 125 % = 944×612 puntos**, o sea la ventana entera **988×656** contando el hueco de la sombra (22/18/22/26, promesa 154) | `windows-client/src/Ui/ConsultaWindow.cs:170-181`, `:478-479`; pantallazo del dueño |
| Qué carita se ve dentro de la consulta en el pantallazo | **ninguna**: `ConsultaWindow` no contiene un `FaceControl`; lo que se ve es `FaceWindow` flotando encima porque es *topmost* y la consulta no | `ConsultaWindow.cs` (cero usos de `FaceControl`); `FaceWindow.xaml:7` |
| Cómo se guarda hoy la carita en el muelle | por gesto: soltarla dentro de la caja del muelle (`ReglaDelMuelle.Guarda`, margen de agarre 24) esconde `FaceWindow` (`Hide()`) y enseña la **silla** `Face`, un segundo `FaceControl` que vive dentro de `BarPanel`; sacarla es tirar (13 px) y `DragMove()`; **no hay animación de soltarse** más que la del sitio al posarse (`MoverConMuelle`, 260–720 ms) | `FaceWindow.xaml.cs:1966-2066`, `:2305-2309`, `:1665-1678`; `FaceWindow.xaml:512` |
| Si hay un «anfitrión» de la carita reutilizable | no: `FaceWindow` habla con `_muelle` por nombre (`Caja`, `Guardando`, `Show/Hide`, `Cambio`), y la silla está cableada en el XAML del panel. Lo reutilizable ya es puro: `ReglaDelMuelle.Guarda/SitioAlSacar/SitioAlAparecer` reciben la `Rect` por parámetro | `FaceWindow.xaml.cs:1793`, `:1972-1979`, `:2033`; `ReglaDelMuelle.cs:56-99` |
| Ventanas de una instancia que tendrían que viajar juntas | la carita (`FaceWindow`), el muelle (`Muelle`), el notch (`PanelDeAcciones`), la consulta, y las que toquen (`LogWindow`, overlays). Todas propias → todas se pueden mover con la API pública | `Application.Current.Windows` |
| Cómo se cambia de escritorio | no hay API pública: es el atajo del sistema (`Win+Ctrl+←/→`, `Win+Ctrl+D` para uno nuevo) por `SendInput`, o la interfaz interna `IVirtualDesktopManagerInternal`, cuyos GUID cambiaron entre compilaciones de Windows 11 | conocimiento de la plataforma; **se mide en la fase 0** |
| Qué mira el asistente para saber si una ventana «se ve» | `IsWindowVisible` **y** cloaked = 0, sin leer el valor: una ventana de otro escritorio cuenta como cerrada. Es la base de la 032, no de esta | `windows-graph/src/Surfaces/UiaSurface.cs:282-290` |

## Por qué esto va dirigido por especificación

Porque el viaje se juzga a sí mismo: mover las ventanas devuelve `S_OK` en 5 ms y el escritorio
sigue siendo el mismo, así que un «llevé la carita a "SEO"» puede ser verdad para la API y mentira
para la persona, que sigue viendo el escritorio de antes con la carita desaparecida. Y el anclaje
en la consulta nace de copiar el del muelle: sin promesa, la copia deriva —dos sillas ocupadas,
una carita que reaparece lejos de la mano— y nadie lo ve hasta que lo ve el dueño.

**La regla del flujo, y no tiene excepciones:** ninguna línea de producción entra antes que la
promesa que la juzga. Cada fase empieza con el contrato ROTO y termina con el contrato INTACTO.

## El diseño

Cuatro piezas, cada una con un solo sitio.

**Su escritorio.** Cada asistente sabe en qué escritorio virtual vive: el de su carita, leído del
sistema al arrancar y al terminar cada viaje. En esta spec hay un solo asistente, y sus ventanas
—la carita, el muelle, el notch y la consulta— forman **el centro de operaciones**, que viaja
junto: si alguna se queda en otro escritorio (un `LogWindow` abierto antes del viaje), se trae.
Vive en `U.Graph.Surfaces.EscritorioVirtual`: el adaptador a la API pública y al registro, y la
regla pura `ReglaDelEscritorio` (qué ventanas están descolocadas, qué orden tienen los escritorios,
cuál es el siguiente). Es la misma pieza con la que, en la 033, cada secundaria sabrá cuál es el
suyo.

**El anfitrión.** Hoy la carita se guarda en un sitio, el muelle. Pasa a guardarse en cualquier
**anfitrión**: algo con una caja en pantalla, una señal de «la tengo», y un hueco donde sentarla
(`AnfitrionDeLaCarita`). El muelle es uno; la consulta es el otro. `FaceWindow` deja de hablar con
`_muelle` para esto y habla con la lista de anfitriones: al soltar, la regla pura
`ReglaDelAnfitrion.Elegir` decide en cuál cae (el primero en orden Z cuya caja contiene el punto,
con el mismo margen de agarre de hoy), y se aplican **las mismas** reglas 149, 150 y 160. La silla
es **una sola** (`Face`), que se muda viva de un hueco al otro, como ya se muda `RootPanel` al
muelle: no hay una tercera cara que sincronizar a mano. Sacarla es tirar, igual que hoy.

**La consulta.** Abre con las dimensiones del pantallazo. En el centro de la vista de nota, donde
en el pantallazo está la carita, hay un hueco vacío que no se ve; al sentarla ahí, debajo aparece
el botón «Llevar a otro escritorio». El botón despliega los escritorios **por su nombre** (los que
no tienen nombre se llaman «Escritorio N», por su posición) menos el actual, y «Uno nuevo». Sin
carita sentada, el botón no está. Ningún elemento nuevo enseña texto al pasar el ratón (promesa
164).

**El viaje.** Un plan de pasos fijo, decidido por una regla pura (`ReglaDelViaje.Plan`) y ejecutado
por `ViajeDeEscritorio`:

1. Mover **todas** las ventanas de la instancia al destino (API pública, 5 ms cada una). Si el
   destino es «uno nuevo», primero se pide al sistema que lo cree y se lee su GUID del registro.
2. Cambiar el escritorio a la vista con el atajo del sistema, tantas veces como distancia haya
   en el orden del registro.
3. Esperar a que el registro diga que el actual **es** el destino, con plazo. Llegar no es haber
   pulsado: es que el sistema lo diga (aprendizaje de la spec 025: el reloj manda).
4. Al llegar: su escritorio pasa a ser el destino, la carita se levanta de la silla y se posa en su
   borde con la animación de moverse que ya existe, y el log dice «llegué a "SEO" en N ms».
5. Si no se llega en el plazo: se dice, las ventanas vuelven al origen, y nada queda a medias.

Sobre «la aplicación quedándose enfrente mío mientras el escritorio cambia por detrás», que es
exactamente lo que pidió el dueño: Windows tiene esa noción —en la vista de tareas, «mostrar esta
ventana en todos los escritorios», lo que se llama **fijar** la ventana— pero no le da API pública;
la que existe es interna y sus identificadores han cambiado entre compilaciones de Windows 11.
**Decisión:** durante el viaje la consulta y la carita se fijan con esa interfaz, si en esta
compilación responde, y se sueltan al llegar: así de verdad se quedan delante mientras el
escritorio desliza detrás. Si la interfaz no responde, el viaje cae al plan de arriba —mover
primero, cambiar después—, en el que la consulta falta de la pantalla el instante del
deslizamiento, y el log dice cuál de los dos caminos se usó. La fase 0 mide si la interfaz interna
responde en la 26200 y cuánto dura ese instante. Cuál se usó no se adivina: se mide y se escribe.

## La especificación

Los números siguen a los de la 029 (263) dejando hueco para lo que está en vuelo (029 y 030 en otras
ramas): **se confirman en `/promesas` contra `origin/main`**, y si hay choque se corren, nunca se
reciclan.

| # | Promesa | Fase |
|---|---|---|
| 270 | cada asistente sabe cuál es su escritorio: el escritorio virtual donde vive su carita, leído del sistema al arrancar y al terminar cada viaje; y las ventanas de su centro de operaciones —carita, muelle, notch, consulta— viven en ese mismo escritorio, nunca repartidas: la que se queda en otro, se trae | 1 |
| 271 | la consulta abre con las dimensiones del pantallazo, 988×656 puntos con el hueco de la sombra —la tarjeta 944×612—, centrada; y se sigue estirando desde sus bordes como hoy | 2 |
| 272 | la carita se guarda en la consulta igual que en el muelle: soltarla dentro la sienta en el centro, la flotante desaparece, y hay UNA silla ocupada a la vez —muelle o consulta, nunca las dos—; el anfitrión lo decide la caja donde se soltó, con el mismo margen de agarre, y tirar de ella la saca bajo el cursor como hoy | 3 |
| 273 | debajo de la carita sentada en la consulta hay un botón para llevarla a otro escritorio, que ofrece los escritorios por su nombre —«Escritorio N» los que no lo tienen— menos el actual, y «Uno nuevo»; sin carita sentada el botón no está | 4 |
| 274 | llevar a otro escritorio deja al asistente trabajando allí: las ventanas del centro de operaciones acaban en el destino, el destino queda a la vista, su escritorio pasa a ser ese, y la carita se suelta de la consulta y se posa con su animación; durante el viaje la consulta se queda delante si el sistema deja fijarla, y si no, el log dice que faltó un instante; llegar lo dice el sistema, no el botón, y si no se llega en el plazo se dice, las ventanas vuelven y nada queda a medias | 5 |

La que cierra el asunto es la **274**: sin ella, lo demás es una carita bonita dentro de una
ventana. Y la 274 sin la 032 es un asistente que viaja y, al quedarse solo en otro escritorio,
sigue mirando lo que la persona hace en el suyo.

### Con qué se juzga cada una

Sin pantalla, por reflexión contra el ensamblado, como la 149 y la 230:

- **270** — `ReglaDelEscritorio.Descolocadas(ventanas con su escritorio, el mío)` devuelve las
  que hay que traer y ninguna más; `Orden(registro)` conserva el orden de la vista de tareas; y
  `Capacidad("U.Graph.Surfaces.EscritorioVirtual")` existe.
- **271** — el tamaño inicial de la consulta es un valor con nombre (`ConsultaWindow.TamanoInicial`)
  y vale 988×656; el mínimo no baja de lo de hoy.
- **272** — `ReglaDelAnfitrion.Elegir(cajas en orden Z, punto)` elige el primero que contiene el
  punto con el margen de agarre, y ninguno si no cae en ninguno; y el estado «quién tiene la
  carita» es un solo valor (`Silla.Ocupada`), no un bool por anfitrión. Las 149, 150 y 160 siguen
  intactas y ahora se ejercen con dos cajas.
- **273** — `ReglaDelViaje.Destinos(escritorios con nombre, actual)` devuelve los nombres en orden,
  «Escritorio N» para los vacíos, sin el actual, y «Uno nuevo» al final; y `HayBoton(sillaOcupada,
  anfitrión)` solo es verdad con la carita sentada en la consulta.
- **274** — `ReglaDelViaje.Plan(origen, destino, orden, nuevo)` devuelve los pasos en ese orden y
  con esa distancia; `Llegue(actualDelRegistro, destino)` es lo único que declara la llegada; y
  `SiNoLlega(plan)` devuelve el plan inverso. Lo demás va a la máquina.

Sobre la máquina, con horas en el log: abrir la consulta y verla con el tamaño del pantallazo;
soltar la carita dentro y ver la silla ocupada y el botón; llevarla a «SEO» y a «Uno nuevo»; y en
cada llegada, un clic por patrón sobre una ventana de ese escritorio, para que «trabajando allí»
no sea una frase. Dos pantallas con nombre, como mínimo: la Tienda de Microsoft y Paint, que son
las que ya sirvieron en la 020.

### Límites dichos, no escondidos

- **Ü no muda ventanas ajenas.** Está medido: `E_ACCESSDENIED`. Una app que se abre mientras el
  escritorio de la instancia no está a la vista aterriza donde está la persona; qué hacer con eso
  es de la 032, y la respuesta es no abrirla a ciegas.
- **El cambio de escritorio es el atajo del sistema.** La interfaz interna se usa para una sola
  cosa, fijar la ventana durante el viaje, y siempre con el camino público de respaldo detrás: si
  Windows cambia y la interfaz deja de responder, el viaje sigue llegando, solo que sin la ventana
  delante durante el deslizamiento.
- **El estado «sentada» no se persiste** entre arranques, como hoy. Y el escritorio de la instancia
  tampoco: al reiniciar, cada Ü nace donde la persona está. Queda en hallazgos.
- **Un solo monitor.** El área de trabajo que acota la carita sigue siendo la principal.

## Las fases

### Fase 0 — la sonda: lo que la API contesta y el código solo cree

| | |
|---|---|
| **Promesa que pone verde** | ninguna: deja la tabla de hallazgos que decide la 5 |
| **Qué toca** | nada de producción; `scripts/sonda-escritorios.ps1` (la de hoy, ampliada) |
| **Terminado** | medidos, con hora: cuánto tarda el atajo `Win+Ctrl+→` por `SendInput` en cambiar el registro; si la interfaz interna de fijar ventanas responde en la 26200 y qué se ve con ella y sin ella; si `Win+Ctrl+D` deja su GUID al final de la lista; si un `SetForegroundWindow` sobre una ventana de otro escritorio se lleva a la persona (para la 032); y si `GetClickablePoint` y `PostMessage` llegan a una ventana cloaked = 2 (para la 032) |

### Fase 1 — la instancia sabe dónde vive (270)

| | |
|---|---|
| **Promesa que pone verde** | 270 |
| **Qué toca** | `windows-graph/src/Surfaces/EscritorioVirtual.cs` (nuevo: `IVirtualDesktopManager` por `ComImport` con sus GUID públicos, lectura del registro), `ReglaDelEscritorio.cs` (nuevo, puro), `FaceWindow.xaml.cs` (leer su escritorio al mostrarse y tras el viaje) |
| **¿Núcleo congelado?** | no |
| **Terminado** | 270 verde; el log dice al arrancar «vivo en "Anuncios"» |

### Fase 2 — la consulta mide lo que se pidió (271)

| | |
|---|---|
| **Promesa que pone verde** | 271 |
| **Qué toca** | `ConsultaWindow.cs:170-181` (un valor con nombre en vez de dos números) |
| **Terminado** | 271 verde; la 154 y la 159 intactas (el hueco de la sombra y el borde de agarre no se mueven) |

### Fase 3 — la consulta es anfitrión (272)

| | |
|---|---|
| **Promesa que pone verde** | 272 |
| **Qué toca** | `windows-client/src/Ui/AnfitrionDeLaCarita.cs` (nuevo, la interfaz), `ReglaDelAnfitrion.cs` (nuevo, puro), `Muelle.cs` (lo implementa: ya tiene `Caja` y `Guardando`), `ConsultaWindow.Anfitrion.cs` (nuevo, parcial: el hueco en el centro de la nota), `FaceWindow.xaml.cs:1966-2066` (guardar/sacar por anfitrión; la silla se muda al hueco elegido), `FaceWindow.xaml:512` (la silla pasa a un `Decorator` con nombre) |
| **¿Núcleo congelado?** | no, pero es la zona de choque alta del repo: **ver hallazgo nº1** |
| **Terminado** | 272 verde, 149/150/160 intactas |
| **Sitios con esta clase de error** | los que hablan con `_muelle` para guardar/sacar: 6 (`:1972, :1975, :1979, :2033, :2037, :1954`), contados con grep |

### Fase 4 — el botón (273)

| | |
|---|---|
| **Promesa que pone verde** | 273 |
| **Qué toca** | `ConsultaWindow.Anfitrion.cs` (la fila del botón bajo el hueco), `ReglaDelViaje.cs` (nuevo, puro: `Destinos`, `HayBoton`) |
| **Terminado** | 273 verde; 164 intacta (nada enseña texto al pasar el ratón) |

### Fase 5 — el viaje (274)

| | |
|---|---|
| **Promesa que pone verde** | 274 |
| **Qué toca** | `ReglaDelViaje.cs` (`Plan`, `Llegue`, `SiNoLlega`), `windows-client/src/Ui/ViajeDeEscritorio.cs` (nuevo: fijar las ventanas si la interfaz interna responde, mover las ventanas, `SendInput` del atajo, esperar el registro con plazo, soltar la fijación, deshacer), `EscritorioVirtual.cs` (la fijación, con enlace tardío y un «no responde» que se dice), `FaceWindow.xaml.cs` (al llegar: levantar la silla y posarse con `MoverConMuelle`) |
| **Terminado** | 274 verde; probado a «SEO» y a «Uno nuevo», con el log pegado en el PR |

## Lo que NO entra

- **Varios asistentes y la cerca**: spec 032. Aquí hay una sola carita y es la principal.
- **Las secundarias, las siete sillas y la conversación entre asistentes**: spec 033.
- **Persistir el escritorio de cada asistente** y volver a él al reiniciar.
- **Mover ventanas ajenas** entre escritorios (medido: la API pública no deja).
- **Cambiar cómo se pulsa o cómo se aprende.** Lo pidió el dueño y además no hace falta aquí.

## Hallazgos

1. **Riesgo de choque en `ConsultaWindow.cs`** (2026-09-17): la rama `origin/jose/la-nota-se-elige-y-se-corrige`,
   sin mergear, le mete +858 líneas. La fase 3 y la 4 se hacen en un archivo parcial nuevo
   (`ConsultaWindow.Anfitrion.cs`) y tocan del original solo el tamaño y el hueco de la fila 2, para
   que el merge sea de líneas y no de intenciones. Antes de abrir la fase 3, mirar si esa rama ya
   entró.
2. **Dos sillas mienten** (medido ya en 2026-09-05 para el muelle): la razón de que la silla sea una
   sola y se mude, y no una copia en la consulta.

## Cierre

- [ ] Todas las promesas verdes (`.\scripts\contrato-del-grafo.ps1` → CONTRATO INTACTO)
- [ ] `.\scripts\verificar.ps1` pasa, con evidencia en `out\evidencia.md`
- [ ] Probado en ≥2 pantallas, con nombre: …
- [ ] Estado de este documento: **implementado** (AAAA-MM-DD)
