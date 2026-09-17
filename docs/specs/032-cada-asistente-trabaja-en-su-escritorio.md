# Plan de implementación: varios asistentes en un solo Ü, y cada uno trabaja en su escritorio

Estado: **propuesto** · Nace del diagnóstico del 2026-09-17 · Rama: `jose/cada-asistente-en-su-escritorio` (se abre desde `main` cuando la 031 haya entrado)

> Segunda pieza del camino de la [spec 031](031-la-consulta-lleva-la-carita-a-otro-escritorio.md).
> El dueño: «cada asistente en cada escritorio tiene su entorno aparte y hace exactamente lo que ya
> hace; es muy importante que quede trabajando y operando en ese escritorio. El usuario tiene que
> sentir que hay una sola aplicación instalada, incluso si son varios `.exe`: eso es tu decisión.»

## La decisión: un solo proceso, varios asistentes

**Todos los asistentes viven dentro de la misma `U.exe`.** Se consideró lo otro —una `U.exe` por
asistente, que es lo que hoy hace a mano `scripts/dev-paralelo.ps1`— y se descartó con el
inventario delante, no por gusto:

| Con varios procesos | Con un proceso |
|---|---|
| el puerto MCP 8790 es `const` y el fallo de bind se descarta: la segunda `U.exe` no abre servidor **y sigue siendo cliente de 127.0.0.1:8790** en 5 sitios — su cerebro conduce las manos de la primera (`ServidorMcp.cs:26`, `FaceWindow.xaml.cs:947,1134,3065,5049,5059,5429`) | un servidor, una ruta por asistente |
| `config.json` se serializa entero y se pisa (`Config.cs:157-167`); `sesion.dat` rota el refresh token y la segunda se queda con uno consumido (`SesionMiracle.cs:226-239`) | una sesión del médico, una configuración |
| siete ganchos de teclado y ratón globales por proceso, y `RegisterHotKey` que se agota a la tercera (`Freno.cs:142`, `AtajoPorGolpes.cs:76`, `GlobalHotkeys.cs:67-111`) | un juego de ganchos, con dueño |
| el actualizador de una reinicia con las DLL de la otra mapeadas (`Updater.cs:117-206`) | un actualizador |
| hablar entre asistentes sería un canal entre procesos | una llamada en memoria |
| `collar.json`, `skills\` y `recuerdos\` ignoran `U_DATA_DIR` (`CollarPermanente.cs:32`, `SkillEnsenada.cs:184`, `FotosDeLosRecuerdos.cs:28`, `AlbumDeMiradas.cs:77`): compartidos por accidente | compartidos porque son del mismo proceso |

Y lo que hace posible el proceso único ya está: **las manos se crean una por clic** y su ventana
objetivo es de instancia (`UiaSurface.cs:193-197`, `:951`; `FaceWindow.xaml.cs:540`), **el piloto
no guarda estado** —cada corrida es su propio `node` (`ElPiloto.cs:26`, `:70`)—, y **la ventana
de trabajo** es una clase de instancia cuyo propio comentario dice que es la base del escritorio
virtual (`VentanaDeTrabajo.cs:20-23`). Lo que falta es que haya **N** de cada cosa que hoy hay una.

## Diagnóstico: qué se midió

Lo de la API está en la 031 (cloaked = 2 para otro escritorio; mover ventana ajena =
`E_ACCESSDENIED`; `GetWindowDesktopId` contesta para cualquier ventana). Lo de abajo es el código
en `origin/main` (`a2f972a`), 2026-09-17.

### Lo que hay uno por proceso y tiene que ser uno por asistente

| Qué | Dónde | Por qué es de un asistente |
|---|---|---|
| La ventana de trabajo | `FaceWindow.xaml.cs:44` (`_trabajo`), lectores en `:5333`, `:5361`, `:5402` | es «dónde ejecuta» |
| El localizador y su SAP | `SurfaceLocator.cs:29`, `:69`; caché de 400 ms en `FaceWindow.xaml.cs:5331` | es «dónde estoy» |
| Las herramientas del mapa y sus **ranuras únicas** `Decir`, `Preguntar`, `Llegue`, `GuardarSkill`, `Plan`, `VentanaDeTrabajo` | `SurfaceMapTools.cs:31`, `:1561`, `:1639-1647`; puestas y anuladas por quien arranca y termina (`FaceWindow.xaml.cs:3074-3085`, `:3118`, `:5435`, `:5451`) | dos pilotos a la vez: el segundo pisa las del primero, y el `finally` del primero deja en `null` las del que sigue vivo |
| El servidor MCP: **sin sesión ni id de cliente**, un solo `_ejecutar` | `ProtocoloMcp.cs:20-22`, `:73-78`, `:128-132`; `ServidorMcp.cs:38` | no se sabe qué piloto llama |
| El «aquí» proyectado a Neo4j (`actual:true`, uno en toda la base) | `ProyectorNeo4j.cs:207-209`, latido en `MapaVivo.cs:225-226` | dos asistentes se lo arrancan cuatro veces por segundo |
| La sesión SAP se elige **por la ventana de delante** | `SapGuiSurface.cs:264-298`, `CualSesion.Elige(…, GetForegroundWindow())` en `:296` | dos asistentes en dos sesiones resuelven a la que la persona tiene delante |
| Las banderas de «estoy corriendo» y el `_cts` | `FaceWindow.xaml.cs:2844`, `:5411`, `:71` (reasignado en `:3101` y `:5437`) | un encargo y una comprobación se pisan el token |

### Lo que es `static` y tendría que llevar dueño

| Qué | Dónde | Con dos asistentes |
|---|---|---|
| El freno (`Freno`, clase estática entera; `UiaSurface.HayQueParar` puesto en su constructor) | `Freno.cs:27`, `:38`, `:142` | un Escape para a todos |
| `UiaSurface.CursorMoved`, `Pulso`, `LogGlobal` | `UiaSurface.cs:127`, `:139`, `:198`; cableados en `FaceWindow.xaml.cs:532`, `:1255-1257` | la carita B se planta sobre el clic de A; el log de B pisa el de A |
| `Senalador`, `TarjetasDeRecuerdo` | `Senalador.cs:17-38`, `TarjetasDeRecuerdo.cs:26-31`; usados sin dueño en `FaceWindow.xaml.cs:3223-3245` | la coreografía del paso se cruza |
| `PuenteASap.Enviar`, `PuenteDeAprendizajes.Mostrar` | `PuenteASap.cs:16`, `PuenteDeAprendizajes.cs:23`; puestos en `FaceWindow.xaml.cs:1220-1223` | ranura única |
| El motor COM de SAP cacheado y sus cachés de vista | `SapGuiSurface.cs:77`, `:208-209`, `:1278` | compartidos; solo es problema con dos sesiones SAP |
| Atajos, golpes de Ctrl, vigía de clics, grabación | `GlobalHotkeys.cs:67`, `AtajoPorGolpes.cs:76`, `ClickWatcher.cs:172`, `UiaSurface.cs:2043-2045` | ¿a cuál asistente le hablan? |
| La voz y el micrófono | `FaceWindow.xaml.cs:448` (`_vivo`), `LiveAudio.cs:84`, `:243-256` | una sola conversación viva, atada a la carita: tiene que poder cambiar de asistente |

### Lo que asume que su escritorio es el que se ve

| Qué | Hoy | Consecuencia con un asistente en un escritorio que no está a la vista |
|---|---|---|
| «Se ve» | `IsWindowVisible` y cloaked = 0, **sin leer el valor** (`UiaSurface.cs:282-287`) | su ventana de trabajo cuenta como cerrada: `VentanaExiste` (`:290`) → `VentanaDeTrabajo.Resolver` la suelta (`VentanaDeTrabajo.cs:49-59`) |
| La ventana de delante (230) y las abiertas (232) | orden Z desde el foco (`VentanaDeDelante.cs:30-46`); `EnumWindows` + «se ve» (`UiaSurface.cs:432-452`) | el foco es el de la persona **en otro escritorio**: aprende y describe lo que no es suyo, y no ve las apps del propio |
| Clic físico, doble, derecho, rueda, por posición; teclear; `key:` | `SetCursorPos` + `mouse_event` con `TraerAlFrente` antes (`UiaSurface.cs:1004-1018`, `:1212`, `:1556`, `:1800`, `:1900`, `:1939`); `SendInput`/`keybd_event` al foco (`:375-406`, `:1158-1180`, `:1426-1473`; `SapGuiSurface.cs:2409`; `SurfaceMapTools.cs:2547`) | traer al frente una ventana de otro escritorio **se lleva a la persona** (fase 0 de la 031 lo confirma); las teclas caen en la ventana de la persona |
| La mirada y computer use | `CopyFromScreen` del framebuffer (`CapturaDePantalla.cs:78-98`, `Screenshotter.cs:29-90`, `InputExecutor.cs:52-149`) | ve y actúa sobre el escritorio de la persona |
| Abrir una app | lanza y espera su ventana (`AbrirSegunElNucleo.cs`, `FaceWindow.xaml.cs:630-653`) | la ventana nace donde está la persona, y no se puede mover (medido) |
| Patrón, mensaje, `ValuePattern`, SAP por COM | sin foco ni ratón (`UiaSurface.cs:1784-1842`, `:1369-1397`; `SapGuiSurface.cs:2303-2348`) | **siguen funcionando** (salvo `GetClickablePoint` en cloaked, fase 0) |

## Por qué esto va dirigido por especificación

Porque cada uno de estos fallos es **silencioso** y se parece a otra cosa: «el grafo está mal» en
vez de «hay dos manos» (ya pasó, `dev-paralelo.ps1:83-89`); «la ventana ya no existe» en vez de
«está en otro escritorio»; «Ü improvisó» en vez de «el cerebro de la secundaria movió las manos de
la principal». Es el aprendizaje nº16 con otra cara: un sistema que aprende menos de lo que cree,
sin ningún error. Sin promesa, ninguna de estas cercas se puede saber puesta.

**La regla del flujo, y no tiene excepciones:** ninguna línea de producción entra antes que la
promesa que la juzga. Cada fase empieza con el contrato ROTO y termina con el contrato INTACTO.

## El diseño

**El asistente es un objeto.** `Navigation.Asistente`: un nombre, su escritorio (270), su ventana
de trabajo, su localizador con su mano, sus herramientas del mapa (`SurfaceMapTools` **propio**,
con sus ranuras de decir, preguntar, plan y skill), su cerebro (la corrida de piloto en curso, con
su propio `CancellationToken`), y su buzón (033). Hay una **principal** —la de siempre, la de la
`FaceWindow`, la que tiene la voz— y hasta seis **secundarias** con una carita ligera
(`CaritaSecundaria`: el mismo `FaceControl`, sin panel). `Asistentes` es la lista, con la
principal siempre en ella.

**Una ruta MCP por asistente.** El servidor sigue siendo uno, en 8790, pero la ruta lleva el
nombre: `http://127.0.0.1:8790/mcp/<asistente>/`. El piloto de cada asistente arranca con su URL
(`mensaje.json` ya lleva el campo `Mcp`), y el servidor despacha `tools/call` a las herramientas
**de ese** asistente. No hace falta sesión ni cabecera: la ruta es la identidad, y funciona con el
cliente HTTP del Agent SDK tal como está. La ruta de hoy sin nombre es la principal.

**Lo que hoy es estático lleva dueño.** `Pulso`, `CursorMoved`, `LogGlobal`, `Senalador`,
`TarjetasDeRecuerdo` y los puentes pasan a decir **de quién** son (el nombre del asistente viaja en
el evento), y cada carita atiende solo lo suyo. El freno deja de ser «para a todos»: Escape frena
al asistente **cuyo escritorio está a la vista**, que es al que la persona está mirando. El «aquí»
de Neo4j lleva el nombre del asistente. La sesión SAP se elige por **la ventana de trabajo del
asistente**, no por la de delante; sin ventana de trabajo, como hoy.

**La cerca.** Cada asistente mira y toca solo lo que hay en su escritorio, y cuando su escritorio
no está a la vista, lo que necesita la pantalla no se hace a ciegas: se dice. Un vigía
(`VigiaDelEscritorio`) sondea el registro cada 300 ms y avisa «el escritorio a la vista cambió»
—no hay API pública del evento; el registro sí cambia, y 300 ms es menos que el deslizamiento—.

- *Lo que se ve* (`ReglaDeLoQueSeVe`, pura): una ventana **de mi escritorio** encubierta por el
  sistema (cloaked = 2) porque el escritorio no está a la vista **existe**; cerrándose (cloaked = 1)
  sigue sin contar. La ventana de delante y las abiertas se eligen solo entre las de mi escritorio;
  si el mío no está a la vista, no hay foco de la persona y «dónde estoy» lo dice así.
- *Lo que se hace* (`Cerca.PuedeAhora`, pura): los gestos que van por el control —patrón, mensaje,
  `ValuePattern`, COM de SAP— siempre; los que necesitan la pantalla —clic físico, teclado del
  sistema, rueda, la mirada, computer use, abrir una app— solo con mi escritorio a la vista, y si
  no, la mano contesta «necesito que mi escritorio esté a la vista para X» y **nunca trae nada al
  frente**. `ComoSePulsa.Decidir` no cambia: la cerca va delante.
- *Lo que entra por el sistema*: los atajos, los golpes de Ctrl, el micrófono y la voz son **del
  asistente cuyo escritorio está a la vista** (lo aclaró el dueño el 2026-09-17: «cuando vaya al
  escritorio de cualquiera de los asistentes quiero poder hablar con él: si hago clic encima o
  doble Ctrl, hablo con ese»). Al cambiar el escritorio a la vista, la voz pasa de mano: la que se
  va se calla sin perder su conversación, y la que llega la toma. Un clic sobre una carita habla
  con esa carita, como hoy (promesa 147). El vigía de clics y la grabación aprenden para el mismo
  asistente.

## La especificación

Numeración a continuación de la 031; se confirma en `/promesas` contra `origin/main`.

| # | Promesa | Fase |
|---|---|---|
| 275 | una ventana de su escritorio no deja de existir porque el escritorio no esté a la vista: «se ve» distingue una ventana encubierta por el sistema —otro escritorio— de una que se está cerrando, y la ventana de trabajo solo se suelta cuando de verdad ya no está | 1 |
| 276 | la ventana de delante y las ventanas abiertas se eligen solo entre las de su escritorio: lo que la persona hace en otro no es su foco, no se aprende y no entra en «qué hay abierto»; y si su escritorio no está a la vista, no hay foco de la persona y «dónde estoy» lo dice así | 1 |
| 277 | lo que necesita el escritorio a la vista —el clic físico, el teclado del sistema, la rueda, la mirada, computer use y abrir una app— no se hace mientras su escritorio no esté a la vista: la mano dice por qué y no trae nada al frente, porque traer al frente una ventana de otro escritorio se lleva a la persona; lo que va por patrón, por mensaje o por la API de SAP sigue igual | 2 |
| 278 | cada asistente tiene lo suyo dentro del mismo proceso: su ventana de trabajo, su localizador, sus herramientas del mapa con sus ranuras, su cerebro con su propio cancelar, su ruta MCP y su «aquí» en el mapa; el cerebro de uno solo llega a las manos de ese uno, y el que termina no borra las ranuras del que sigue vivo | 3 |
| 279 | lo global tiene dueño: cada pulso, cursor, señal, tarjeta y línea de log dice de qué asistente es y cada carita atiende solo lo suyo; Escape, los atajos, los golpes de Ctrl, el micrófono y la voz son del asistente cuyo escritorio está a la vista, y al cambiar el escritorio la voz pasa de mano sin perder ninguna conversación; un clic sobre una carita habla con esa; y la sesión SAP de un asistente es la de su ventana de trabajo, no la de delante | 4 |

La que cierra el asunto es la **278**: sin ella dos cerebros son un cerebro con dos voces. La 277
es la que impide que un asistente en otro escritorio te cambie el escritorio y teclee en tu
ventana.

### Con qué se juzga cada una

Sin pantalla, por reflexión, con mapas a mano como la 230 y la 233:

- **275** — `ReglaDeLoQueSeVe.Existe(visible, cloaked, escritorioDeLaVentana, elMio)` con la tabla
  de casos (visible/no × cloaked 0/1/2 × mío/ajeno); `VentanaDeTrabajo.Resolver` con un «existe»
  que devuelve «encubierta pero mía» no suelta la ventana.
- **276** — `VentanaDeDelante.Elegir` con una cadena Z falsa de dos escritorios elige solo las del
  mío; con el mío fuera de la vista devuelve «ninguna» y el texto de «dónde estoy» lo dice.
- **277** — `Cerca.PuedeAhora(gesto, miEscritorioALaVista)` para cada gesto de `ComoSePulsa` y de
  `ComoSeEscribe`, más mirar y abrir; y el motivo de la mano para los que no.
- **278** — `Asistentes` con dos asistentes falsos: la ruta `/mcp/<nombre>/` despacha a las
  herramientas de ese nombre y solo a esas; terminar el cerebro de uno deja intactas las ranuras
  del otro; y cada uno tiene su `VentanaDeTrabajo` distinta.
- **279** — `ReglaDeQuienAtiende(escritorioALaVista, asistentes)` devuelve uno solo, y una máquina
  de estados con dos asistentes falsos cambiando el escritorio nunca deja la voz en dos ni en
  ninguno; los eventos con dueño llevan el nombre y una carita falsa ignora los ajenos;
  `CualSesion.Elige` recibe la ventana de trabajo y no el foco.

Sobre la máquina: la principal en «Anuncios» y una secundaria en «SEO» (llevada con la 033, o
puesta a mano con la sonda). Con la persona en «Anuncios», la de «SEO» hace un clic por patrón en
su ventana de trabajo y **no** un clic físico (el log dice por qué), un Escape frena solo a la de
«Anuncios», y `Ctrl+Alt+U` abre la voz de la de «Anuncios»; al deslizar a «SEO», el doble Ctrl habla con la de «SEO». Dos pantallas con nombre, como en la 020.

### Límites dichos, no escondidos

- **Un solo SAP por asistente, y una sola sesión SAP en la práctica**: dos asistentes operando la
  misma sesión SAP a la vez no se soportan; la cola de exportaciones sigue siendo una.
- **Una voz a la vez.** Hay un micrófono y lo tiene el asistente que tienes delante; los demás
  piden y dicen por la principal (033) mientras no los mires.
- **Abrir una app a ciegas no se hace.** Mover la ventana ajena al escritorio del asistente
  necesitaría la interfaz interna de Windows, y esa decisión se toma aparte si hace falta.
- **La memoria es una** (grafo, skills, recuerdos): se comparte a propósito, con el «aquí» por
  asistente. Que dos asistentes aprendan lo mismo dos veces es un caso que el grafo ya resuelve
  con `MERGE`.

## Las fases

### Fase 0 — dos preguntas que trae la fase 0 de la 031

Si `SetForegroundWindow` sobre otro escritorio se lleva a la persona, y si
`GetClickablePoint`/`PostMessage` llegan a una ventana cloaked = 2. Si lo segundo es que no, el
peldaño «mensaje» pasa a ser de la pantalla en la 277 y se dice en hallazgos.

### Fase 1 — lo que se ve es lo mío (275, 276)

| | |
|---|---|
| **Qué toca** | `windows-graph/src/Surfaces/ReglaDeLoQueSeVe.cs` (nuevo, puro), `UiaSurface.cs:282-290`, `:432-452`, `VentanaDeDelante.cs:30-46` (un predicado más), `SurfaceLocator.cs:188-192`, `SurfaceMapTools` («dónde estoy» sin foco de la persona) |
| **¿Núcleo congelado?** | `VentanaDeDelante` es de la 230: se **extiende**; la 230 queda intacta |
| **Terminado** | 275 y 276 verdes; 230, 232, 233 intactas |
| **Sitios con esta clase de error** | «se ve» sin leer el valor: 1; enumeraciones con solo `IsWindowVisible`: 4 (`AppAligner.cs:184`, `:237`, `PestanasAbiertas.cs:460`, `SurfaceMapTools.cs:437`) |

### Fase 2 — lo que necesita la vista, espera (277)

| | |
|---|---|
| **Qué toca** | `windows-graph/src/Surfaces/Cerca.cs` (nuevo, puro), `UiaSurface.cs` delante de cada gesto de pantalla (`:375`, `:1004`, `:1158`, `:1212`, `:1426`, `:1556`, `:1800`, `:1939`), `SapGuiSurface.cs:2409`, `SurfaceMapTools.cs:2547`, `CapturaDePantalla.cs:78`, `Screenshotter.cs:29-90`, `InputExecutor.cs:46`, `AbrirSegunElNucleo.cs`, y `VigiaDelEscritorio.cs` (nuevo) |
| **Terminado** | 277 verde; 234, 235, 237 intactas |
| **Sitios con esta clase de error** | 14, contados arriba; el commit trae el número final |

### Fase 3 — el asistente es un objeto, y hay N (278)

| | |
|---|---|
| **Qué toca** | `windows-client/src/Navigation/Asistente.cs` y `Asistentes.cs` (nuevos), `FaceWindow.xaml.cs:44`, `:430-432`, `:588-600`, `:3074-3118`, `:5435-5451` (la principal pasa a ser un `Asistente`; sus ranuras y su `_cts` viven en él), `ServidorMcp.cs:38` y `ProtocoloMcp.cs:128-132` (despacho por ruta), `ElPiloto.cs:31` (la URL con nombre), `MapaVivo.cs`/`ProyectorNeo4j.cs:203-212` («aquí» con nombre), `windows-client/src/Ui/CaritaSecundaria.cs` (nuevo: el `FaceControl` con su estado, sin panel) |
| **¿Núcleo congelado?** | no, pero es la zona de choque alta del repo (`FaceWindow.xaml.cs`): rama de un día, y avisar |
| **Terminado** | 278 verde; las promesas del piloto y de las lecciones (013, 015, 017) intactas |
| **Sitios con esta clase de error** | ranuras únicas: 6 (`SurfaceMapTools.cs:1561`, `:1639-1647`); banderas y `_cts`: 3 |

### Fase 4 — lo global tiene dueño (279)

| | |
|---|---|
| **Qué toca** | `Freno.cs` (quién frena, con el vigía), `UiaSurface.cs:127`, `:139`, `:198` (dueño en el evento), `Senalador.cs`, `TarjetasDeRecuerdo.cs`, `PuenteASap.cs`, `PuenteDeAprendizajes.cs`, `FaceWindow.xaml.cs:206-252`, `:532`, `:1255-1257`, `:3223-3245` (atender solo lo propio), `SapGuiSurface.cs:264-298` (`CualSesion` por ventana de trabajo), `GlobalHotkeys`/`AtajoPorGolpes`/`ClickWatcher` y `ConversacionEnVivo` (del asistente que está a la vista: la conversación cambia de carita y de herramientas sin cerrarse) |
| **Terminado** | 279 verde; 146 (un solo micrófono) y 72 (la sesión SAP) intactas |
| **Sitios con esta clase de error** | estáticos sin dueño: 9, contados en la tabla de arriba |

## Lo que NO entra

- **Cambiar cómo se pulsa, se escribe o se aprende.** La cerca decide *si ahora*, nunca *cómo*.
- **Mover ventanas ajenas** al escritorio del asistente.
- **Repartir SAP** entre asistentes.
- **Las sillas, el viaje de las secundarias y la conversación entre asistentes**: spec 033.
- **Persistir asistentes y escritorios** entre reinicios.

## Hallazgos

<!-- Se rellena DURANTE la implementación. -->

## Cierre

- [ ] Todas las promesas verdes (`.\scripts\contrato-del-grafo.ps1` → CONTRATO INTACTO)
- [ ] `.\scripts\verificar.ps1` pasa, con evidencia en `out\evidencia.md`
- [ ] Probado con dos asistentes en dos escritorios con nombre: …
- [ ] Estado de este documento: **implementado** (AAAA-MM-DD)
