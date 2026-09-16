# Plan de implementación: Ü trabaja en su ventana, y la persona sigue en la suya

Estado: **implementado** el 2026-09-14 (230 a 234 en verde, contrato intacto) · Nace del diagnóstico del 2026-09-14 · Rama: `jose/la-ventana-de-trabajo`

> El dueño, tras la prueba con la voz nueva: «me interesa que pueda hacer los clics de forma
> infalible con el clic que no es el mouse del usuario, llevar ese tipo de clic al mismo nivel de
> confiabilidad que los clics con el cursor, porque así el usuario puede seguir usando su computador
> mientras el asistente ejecuta sus tareas». Y sobre abrir apps: «el modelo debe ser capaz de elegir
> de forma autónoma si abrir una nueva instancia o usar la que ya existe, y ser muy consciente de
> ello». Y el horizonte: un escritorio virtual aparte para Ü, con sus apps, sin interrumpir el flujo
> de la persona.

## Diagnóstico: qué se midió

Todo del log de la app de desarrollo del 2026-09-14 (`C:\U-dev2\local\U\logs\u-20260914.log`,
16:49–16:55), con Paint y la Tienda de Microsoft, y del código que lo explica.

| Qué | Medida | Fuente |
|---|---|---|
| Dónde buscaba Ü el botón «Elipse» de Paint | en la ventana con el foco, que era la Vista de tareas: «no pude pulsar «Elipse». Estás en «uia://explorer.exe/vista-de-tareas»», dos veces, y después el tope de dos intentos | log 16:50:10, 16:50:22, 16:50:32; `UiaSurface.Resolve` busca solo en `GetForegroundWindow` y `SoloEnFoco` impide el barrido |
| Dónde buscaba «Cerrar» de Paint | igual: lo que estaba delante era la carita o Meet; el ratón no se movió (lo vio el dueño) y las marcas del rastro son del cursor de la persona sobre Chrome | log 16:52:31–16:54:23 |
| El único cierre que funcionó | «Cerrar Microsoft Store», 16:54:44: el paso en que la carita no señaló y la ventana objetivo sí estaba delante | log 16:54:43–44 |
| Qué dijo Ü después de cerrar la Tienda | «Estás en «uia://ApplicationFrameHost.exe/microsoft-store»» tres y seis segundos después, con la lista de elementos de OTRA ventana | log 16:54:46, 16:54:50; `SurfaceLocator.Ahora()` devuelve `Current` sin recalcular cuando delante está Ü, y `Probe()` no lo refresca |
| Cuántas reglas hay para «la ventana de delante» | dos: el localizador usa la ventana con foco; el lector de elementos baja por el orden Z hasta la primera visible que no sea de Ü (`RealForegroundWindow`) | `SurfaceLocator.Ahora`, `UiaSurface.RealForegroundWindow` |
| Por qué apareció un segundo Paint | «lanzada «Microsoft.Paint_8wekyb3d8bbwe!App» por el catálogo del sistema»: cuando el nombre coincide exacto con una app instalada, se lanza antes de preguntar si ya está delante | log 16:53:48; `AbrirSegunElNucleo.Abrir`, rama `instalada` |
| Qué dice la mano cuando falla | «no pude pulsar «X».» para tres causas distintas; el registro detallado de `UiaSurface` (`L(...)`) no está conectado a ningún log | `PulsarSegunElNucleo.Pulsa`, `UiaSurface.Log` sin suscriptor |
| Cómo se pulsa hoy sin ratón | solo como respaldo: primero el clic físico (`RealClick`), que trae la ventana al frente con un `SetForegroundWindow` a secas, el que Windows ignora desde otro proceso (lo documenta `TraerAlFrente` dos métodos más abajo) | `UiaSurface.RealClick` |

## Por qué va dirigido por especificación

Porque hoy hay una sola idea de «dónde estoy» y sirve para dos cosas incompatibles. La primera es
aprender de la persona: el vigía atribuye cada clic humano al cambio de pantalla que produjo, y
para eso la pantalla es la que la persona mira. La segunda es ejecutar: buscar el elemento, pulsarlo
y comprobar la consecuencia, y para eso la pantalla tiene que ser la que Ü opera, que deja de
coincidir con la primera en cuanto la persona sigue trabajando. Separar las dos es un cambio de
modelo, y un cambio de modelo sin promesa se cuela en silencio: nada compila peor, solo Ü deja de
saber dónde está cuando más importa.

**La regla del flujo, y no tiene excepciones:** ninguna línea de producción entra antes que la
promesa que la juzga. Cada fase empieza con el contrato ROTO y termina con el contrato INTACTO.

## El diseño

Dos nociones, con nombre, y un solo sitio para cada una.

- **El foco de la persona** es la ventana que la persona mira: la primera ventana visible y con
  título, bajando por el orden Z desde la que tiene el foco, que no sea de Ü. Es la regla que ya
  usaba el lector de elementos; pasa a usarla también el localizador, y así deja de existir la
  ubicación congelada cuando delante está la carita. Es lo que alimenta el grafo desde el lado
  humano y no cambia de significado.
- **La ventana de trabajo de Ü** es la ventana concreta que Ü está operando: la que abrió o trajo al
  frente, a la que fue, o en la que dio el último paso que cambió de pantalla. Todo lo que Ü ejecuta
  se hace respecto a ella. Si desaparece, Ü lo sabe y lo dice. Si no hay ninguna, Ü trabaja sobre el
  foco de la persona, que es exactamente lo de hoy.

Sobre la ventana de trabajo, el clic sin ratón: el elemento se busca en esa ventana, exista o no
otra encima, y se pulsa por el patrón de accesibilidad cuando el elemento lo admite, sin traer nada
al frente y sin tocar el ratón. El clic físico queda como excepción, para lo que no admite patrón o
donde el patrón no navega (los árboles del explorador, medido en agosto), y cuando hace falta se
hace bien: se trae la ventana al frente con el enganche que ya existe, se pulsa, y se devuelven a la
persona su ventana y su cursor.

Al abrir una app, Ü mira primero qué ventanas de esa app hay y se lo dice al modelo. El modelo
decide si usa una existente o pide una copia nueva, y la herramienta acepta las dos.

## La especificación

| # | Promesa | Fase |
|---|---|---|
| 230 | la ventana de delante se elige con UNA regla, la misma para el localizador y para el lector de elementos: bajando por el orden Z desde la que tiene el foco, la primera visible y con título que no sea de Ü; si delante está Ü, la ubicación se calcula ahora con esa regla y no se devuelve la última recordada | 1 |
| 231 | la mano dice por qué no pudo: el registro de la superficie llega al log, y «no pude pulsar» trae la causa —no encontré el elemento en esa ventana, no admite ningún patrón, el patrón falló—, en vez de una sola frase para las tres | 1 |
| 232 | abrir una app mira primero qué ventanas suyas existen y lo dice: con una o varias abiertas trae una al frente y las nombra, y solo lanza otra si se pide una instancia nueva; sin ninguna abierta, lanza como siempre | 1 |
| 233 | Ü tiene una ventana de trabajo distinta del foco de la persona: lo que ejecuta se resuelve respecto a ella; si no hay, es el foco de la persona; y si la que tenía ya no existe, lo dice y vuelve al foco de la persona en vez de describir una pantalla cerrada | 2 |
| 234 | pulsar en la ventana de trabajo va por el patrón de accesibilidad cuando el elemento lo admite, sin traer nada al frente y sin mover el ratón; el clic físico es la excepción para lo que no admite patrón o no navega con él, y cuando se usa se devuelven el foco y el cursor a la persona | 2 |

La que cierra el asunto es la **233**: mientras Ü ejecute sobre la ventana que mira la persona, todo
lo demás son mejoras de un modelo equivocado.

### Con qué se juzga cada una

Mapa a mano en la propia prueba, sin pantalla: la regla de la ventana de delante con una cadena de
ventanas falsa (230); una mano falsa que devuelve su motivo, y el registro global de la superficie
recibiendo líneas (231); un catálogo de ventanas abiertas falso y un lanzador que cuenta (232); una
ventana de trabajo con un «existe» falso y un foco falso, y la cuenta del ejecutor con el aviso
(233); la decisión pura de cómo se pulsa según los patrones que admite el elemento, y qué hay que
devolver (234).

Lo que el contrato no puede juzgar y se mide en la máquina: que con Paint detrás de Chrome y la
carita delante, «cierra Paint» lo cierre sin mover el ratón; que tras cerrar la Tienda Ü diga dónde
está de verdad; que «abre Paint» con un Paint abierto no abra otro y sí lo traiga; y que el cableado
del localizador y del ejecutor use lo nuevo. Va al log, con horas.

### Límites dichos, no escondidos

- SAP no cambia en esta spec: su identidad es la sesión que está delante (72) y sus manos son la API
  de scripting, que no depende del foco. La ventana de trabajo de Ü, cuando es SAP, es la ventana de
  esa sesión.
- Cuándo se fija la ventana de trabajo: al abrir o traer una app (232), al ir a una pantalla, y
  cuando un paso de Ü cambia de pantalla y esa pantalla nueva es la que el sistema activó. Si la
  persona mantiene el foco en otro sitio y la acción de Ü abre un diálogo nuevo sin activarlo, Ü no
  lo sigue todavía; queda para la fase del escritorio virtual.
- El clic físico sobre una ventana de otro escritorio virtual no funciona; por eso el patrón va
  primero. El escritorio virtual en sí no entra.
- Devolver el foco a la persona tras un clic físico usa el mismo enganche que traer al frente; si
  Windows lo niega, se dice en el log y no se finge.

## Las fases

### Fase 1 — diagnóstico, una regla y conciencia de instancias (230, 231, 232)

`U.Graph.Surfaces.VentanaDeDelante.Elegir` (pura) y su uso en `UiaSurface.RealForegroundWindow` y
en `SurfaceLocator.Ahora`/`Probe`. `UiaSurface.LogGlobal` conectado a `LogBus` como `mano`, y
`PulsarSegunElNucleo.ConMotivo` con una mano que devuelve por qué no pudo. `AbrirSegunElNucleo`
con `ventanasAbiertas`, `traerVentana` y el argumento `instancia`; `UiaSurface.VentanasAbiertas`;
`map_open_app` con `instancia` y su descripción para el modelo.

### Fase 2 — la ventana de trabajo y el clic sin ratón (233, 234)

`Navigation.VentanaDeTrabajo` y su uso como «dónde» de todo lo que ejecuta. `SurfaceLocation.Hwnd`.
`U.Graph.Surfaces.ComoSePulsa.Decidir` (pura) y `UiaSurface.Execute(step, ventanaObjetivo)`, que
resuelve solo en esa ventana y pulsa por patrón; el físico con `TraerAlFrente` y devolución del foco
y del cursor.

## Lo que se encontró al implementar

La primera corrida a mano dejó dos huecos que el diseño no había nombrado, y los dos son la misma
clase de error: una pieza que decide sobre lo que Ü ejecuta seguía mirando el foco de la persona.

- **Lo que está vivo lo alimentaba solo el foco de la persona.** La compuerta de la ejecución dice
  «lo conozco aquí pero AHORA no lo veo» si el elemento no está entre lo observado en esa pantalla,
  y lo observado lo pone el latido del mapa vivo, que lee la ventana que mira la persona. Con la
  Tienda como ventana de trabajo y la carita delante, «Cerrar» estaba a la vista y la compuerta lo
  negó («hice 0 de 1 y paré en el paso 1: «Cerrar» lo conozco aquí pero AHORA no lo veo», 19:14:14). Lo que se añadió: el lector de elementos acepta una ventana
  concreta (`UiaReader.Read(hwnd)`), el mapa vivo la observa aparte del latido con la misma criba
  (`MapaVivo.ObservarVentana`), y el ejecutor lo hace antes de pulsar, de recorrer y de situarse
  (`ObservarLaVentanaDeTrabajo` en la carita). No se observa si la ventana de trabajo es la que la
  persona mira, si es SAP, o si ya no existe.
- **La interrupción se juzgaba en la ventana con el foco del sistema.** Con la Tienda abierta por Ü
  y la persona en Chrome, «dónde estoy» contestó con la barra de información de Chrome, que es una
  interrupción de la persona y no de Ü (19:20:48). `Interrupcion.Leer`, `EsOpaca` y `Describir`
  aceptan ahora la ventana en la que juzgar, y las herramientas del mapa reciben la ventana de
  trabajo (`SurfaceMapTools.VentanaDeTrabajo`). Sin ventana de trabajo se juzga el foco de la
  persona, como siempre.

La clase de error vivía en seis sitios: la resolución del elemento (`UiaSurface.Resolve`), el
localizador con la carita delante (`SurfaceLocator.Ahora`/`Probe`), el clic físico con su
`SetForegroundWindow` a secas (`RealClick`), la apertura de apps sin mirar las abiertas
(`AbrirSegunElNucleo`), lo vivo del mapa (`MapaVivo`) y la interrupción (`Interrupcion`). Seis
corregidos.

### La corrida a mano (nivel 4), del log de `C:/U-clic`, 2026-09-14

| Hora | Qué | Resultado |
|---|---|---|
| 19:24:19 | `map_open_app` «Microsoft Store» con la Tienda ya abierta | «ya estaba abierta: 1 ventana(s)», la trae, no lanza otra; la ventana de trabajo pasa a ser la Tienda |
| 19:24:23 | `map_where_am_i` con la persona en Chrome | «Estás en microsoft-store#inicio. Veo 48 salida(s)»: la ubicación es la de trabajo y lo vivo viene de ella |
| 19:24:37 | `map_take` «Cerrar» con la persona en Claude | la mano: «✓ en la ventana de trabajo», «cómo se pulsa: Patron (invoke=True)», «Invoke por patrón: sin foco, sin ratón», ok=True; la Tienda se cerró y el cursor no se movió |
| 19:24:41 | `map_where_am_i` después | «Ojo: la ventana en la que trabajaba ya no existe. Estás en claude.exe/claude»: dice dónde está de verdad, una vez |
| 19:25:07 | `map_open_app` «paint» sin Paint | lanza; la respuesta vuelve antes de que exista la ventana, así que la de trabajo se fija en la segunda llamada |
| 19:25:22 | `map_open_app` «paint» con Paint abierto | «ya estaba abierta: 1 ventana(s)», la trae; sigue habiendo un solo mspaint |
| 19:25:26 | `map_take` «Cerrar» en Paint | pulsó, «la ventana en la que trabajaba ya no existe. Ahora estás en claude.exe/claude»; Paint cerrado |

Dos pantallas con nombre: la Tienda de Microsoft y Paint. En las dos, el clic sin ratón llegó al
elemento correcto y la persona no perdió el foco.

Lo que quedó a la vista y no se arregló aquí:

- Al cerrar la Tienda, el paso contó «Ahora estás en ApplicationFrameHost.exe/microsoft-store»: es
  el marco de la app en mitad de cerrarse, que estaba delante un instante. La pregunta siguiente lo
  corrige con el aviso. Seguir el foco tras un paso podría descartar una ventana que muere en el
  segundo siguiente; no se hizo para no adivinar.
- Abrir una app que tarda en pintar su ventana vuelve sin ventana de trabajo (Paint, 15 s hasta la
  ventana): la primera llamada dice «Paint está delante» con la ubicación de la persona. Esperar la
  ventana tras lanzar es una mejora aparte.
- En la primera corrida, cerrar Paint contó «pulsé Cerrar y la pantalla no cambió» porque la ventana
  tardó más de 1,8 s en desaparecer; a la siguiente pregunta llegó el aviso correcto.

## Lo que NO entra

- El escritorio virtual: esta spec deja la base, no lo monta.
- Seguir un diálogo nuevo que la acción de Ü abrió sin que el sistema lo activara.
- Cambiar cómo aprende el grafo del lado de la persona: el vigía, la atribución y `Observar` siguen
  con el foco de la persona.
