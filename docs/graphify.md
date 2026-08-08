# Graphify — el grafo de decisiones

> **Principio (2026-08-01, decidido por el usuario):** el grafo es el PLAN, no el registro.
> Lo que se aprendió resolviendo problemas de mapeo no puede vivir regado en comentarios y
> memorias de sesión: se registra AQUÍ, estructurado, para que lo consuman personas hoy y el
> agente de mapeo mañana.

## Las dos capas

**Capa 1 — decisiones de desarrollo (este archivo).** Reglas duramente ganadas sobre cómo se
mapea UI. Las escribimos y consumimos los editores del código (humano + asistente). Cada regla
nace de un fallo real, con fecha y síntoma, porque una regla sin su porqué se borra al primer
refactor.

**Capa 2 — decisiones por app (EMPEZADA el 2026-08-05).** El agente que la alimenta ya existe: el
«maestro» mira la pantalla y dicta la jerarquía de navegación de la app, y el usuario la corrige
señalando. Frontera de diseño, que se mantiene y ya está materializada en dos archivos:

- **REGLAS = conocimiento sobre la app** («el panel lateral es navegación permanente»).
  Compartibles entre usuarios; un usuario nuevo hereda el paquete de reglas de cada app.
  Hoy: `jerarquias-ensenadas.json`, que SOBREVIVE a borrar el grafo.
- **TERRENO = datos del usuario** (qué carpetas tiene, cómo se llaman). SIEMPRE privado.
  Hoy: `surface-map.json`, que se borra sin pena para hacer una prueba limpia.

Se comparte el conocimiento de la app, nunca el contenido del usuario.

Lo que falta de la capa 2 no es el almacén sino el ALCANCE: hoy se enseña la jerarquía de niveles;
mañana, el resto del criterio por app. Ver «La estructura por NIVELES» y «La enseñanza de apps»
más abajo, y «Almacenamiento» para por qué el transporte sigue siendo un archivo.

---

## Formato de una decisión

```
### <regla en una frase imperativa>
- Síntoma: qué se veía fallar
- Causa: el porqué real
- Fecha/evidencia: cuándo se midió
- Código: dónde vive la implementación
```

---

## Reglas universales de UIA (cualquier app)

> **Vigilancia permanente: ¿lo hace ya UIA?** Varias veces hemos construido a mano algo que la
> API ya resolvía mejor (el punto pulsable, el desplazamiento a la vista), y otras hemos dado
> por identidad algo que UIA ofrece pero NO lo es (el AutomationId posicional). Antes de
> escribir aritmética sobre coordenadas o heurísticas sobre nombres: mirar si existe el patrón
> de UIA, y si existe, preguntarse si dice lo que creemos que dice.

### Un AutomationId numérico es una POSICIÓN, no una identidad
- Síntoma: las subcarpetas de cualquier carpeta se filtraban como si fueran cromo global y
  nunca se exploraban; el recorrido se quedaba en profundidad 1.
- Causa: en la lista del explorador cada fila lleva su índice como AutomationId («0», «1»…),
  así que `uia:aid=1;ct=ListItem` significa «el segundo de lo que haya ahora»: apunta a otra
  cosa al reordenar y COLISIONA entre pantallas. Como se emitía antes que el nombre, era el
  selector principal de todo el contenido.
- Regla: un aid formado solo por dígitos se degrada a respaldo; el nombre pasa delante. Y toda
  heurística que agrupe por selector debe excluir el contenido leyendo el `ct=` del propio
  selector, no confiando en que se lo pasen aparte.
- Fecha: 2026-08-01. Código: `UiaSurface.SelectorsFor`, `SurfaceMap.EsCromoGlobal`.

### Los MENÚS no se alcanzan caminando el árbol: hay que buscarlos con FindAll
- Síntoma: el asistente pulsaba «Nuevo», el menú se abría con «Carpeta» dentro, y seguía sin ver
  ninguna opción. Podía abrir menús pero nunca elegir en ellos.
- Causa: un menú abierto cuelga de una frontera que el `ControlViewWalker` no cruza —vive además
  en su propia ventana emergente (`Microsoft.UI.Content.PopupWindowSiteBridge`)— pero
  `FindAll(TreeScope.Descendants, MenuItem)` sobre la ventana principal SÍ los encuentra.
- Alcance: general, no del explorador. Los menús de CUALQUIER app estaban invisibles.
- Fecha: 2026-08-02. Código: `UiaReader.CollectMenus`.

### Una acción no navega: su éxito es haber HECHO algo, y después hay que releer
- Un botón de ejecución («Nuevo», «Cortar») a menudo deja la superficie igual: exigirle un cambio
  de pantalla reportaría fallo a un menú que se abrió perfectamente. Y como suele destapar cosas
  nuevas en la MISMA superficie, después de ejecutarla hay que releer sin el guardia de «esto ya
  se conoce» — si no, el asistente actúa sobre la pantalla de antes.
- Fecha: 2026-08-02. Código: `SurfaceMapTools.Take`, `ObservarSinGuardia`.

### Un selector casa con VARIOS elementos: elige el que se puede usar
- Síntoma: `map_go_to` rompía el primer tramo de toda ruta — «pulsé Escritorio pero seguimos en
  documentos».
- Causa: un nombre no es único. En el panel del explorador hay varios «Escritorio» (el de
  OneDrive, el anclado) y algunos cuelgan de ramas PLEGADAS, así que existen en el árbol de UIA
  con `rect=Empty`. `FindFirst` devolvía uno de esos. Sin caja no hay dónde pulsar → respaldo a
  Invoke → true sin navegar.
- Regla: resolver con `FindAll` y quedarse con el primero visible y con geometría; si ninguno
  sirve, devolver el primero y dejar que falle honestamente.
- Fecha: 2026-08-02. Código: `UiaSurface.MejorCandidato`.

### En lo seleccionable, SELECT va antes que INVOKE
- Síntoma: un tramo de ruta se daba por bueno sin cambiar de pantalla.
- Causa: un TreeItem o un ListItem exponen `InvokePattern` por herencia, pero invocarlos
  devuelve true sin navegar: lo que mueve un árbol o una lista es la SELECCIÓN.
- Fecha: 2026-08-02. Código: `UiaSurface.Click`.

### El punto pulsable lo da UIA (GetClickablePoint), no nuestra aritmética
- Síntoma: clics perfectamente válidos se rechazaban («el centro cae fuera de la ventana») y
  caían a Invoke, que sobre una pestaña no navega.
- Causa: comparar el centro de la caja contra `GetWindowRect` del contenedor es una
  aproximación falsa. `GetClickablePoint` devuelve un punto realmente alcanzable —contando
  recorte, scroll y solapes— o lanza `NoClickablePointException` si no lo hay. La intención de
  la comprobación era correcta; la implementación, no.
- Fecha: 2026-08-01. Código: `UiaSurface.RealClick`.

### Un fallo aislado no cierra un nodo
- Síntoma: al fallar una puerta, el retroceso se pasaba al padre y se perdían las 20+ puertas
  restantes de ese nodo; toda la corrida quedaba en profundidad 1.
- Causa: volver es intrínsecamente inestable (el historial se pasa, una pantalla tarda), así
  que un intento fallido no es evidencia de que el nodo sea inalcanzable. Se cierra tras 3
  fallos seguidos, y se registra cuántas puertas quedaron pendientes.
- Fecha: 2026-08-01. Código: `GraphCrawler.Frente.FallosSeguidos`.

### Un log que miente cuesta una ronda entera
- El registro del «plan» imprimía los candidatos EN BRUTO, sin aplicar el filtro que sí se
  aplicaba al ejecutar. Parecía que cada pantalla replanificaba el panel entero. Todo log de
  intención debe reflejar lo que se va a hacer de verdad, no lo que se consideró.
- Fecha: 2026-08-01. Código: `GraphCrawler.AbrirNodoAsync`.

### Guarda selectores, nunca referencias de elemento
- Síntoma: el recorrido «solo entraba a la primera carpeta»; las demás fallaban en silencio.
- Causa: los `AutomationElement` capturados mueren cuando la lista se repinta (al navegar y
  volver). El selector se resuelve de nuevo contra la pantalla que hay AHORA.
- Fecha: 2026-08-01. Código: `GraphCrawler.LeerSalidasAsync` (calcula el selector con el
  elemento vivo y descarta la referencia).

### La ventana de nivel superior se obtiene con GA_ROOT, no con el primer ancestro con handle
- Síntoma: el badge se congelaba en `uia://explorer.exe/ventana` durante todo el mapeo; ningún
  destino se confirmaba; el grafo no crecía.
- Causa: el primer ancestro con handle de un elemento del panel es una ventana HIJA
  (SysTreeView32). Enfocarla hace que GetForegroundWindow devuelva la hija, cuyo título es el
  nombre del panel → identidad vetada. Enfocar y recortar son preguntas distintas: enfocar
  quiere la raíz, recortar quiere el contenedor con scroll.
- Fecha: 2026-08-01. Código: `UiaSurface.TopLevelWindow` / `ContenedorDe`.

### El `ct=` del selector se aplica al resolver, no es un adorno
- Síntoma: clics al centro de la lista; «Documentos» resolvía un Pane de 1578×571.
- Causa: `ConditionFor` filtraba solo por Name; ante un TreeItem y un Pane homónimos gana el
  primero que aparezca. El selector siempre llevó el tipo; nadie lo usaba.
- Fecha: 2026-07-31. Código: `UiaSelector.ConditionFor` + `ControlTypePorNombre`.

### Desplaza a la vista antes de pulsar, relee la caja después, y comprueba que el punto caiga dentro del contenedor
- Síntoma: elementos bajo el scroll «se pulsaban» con ok=True y no pasaba nada; dos elementos
  distintos con el centro en el mismo punto (y=901), fuera del panel.
- Causa: un elemento existe en el árbol aunque no esté visible; ScrollIntoView lo deja pegado
  al borde y el centro puede quedar fuera. Un clic fuera no falla: acierta en otra cosa.
  «Aceptado no es ejecutado.»
- Fecha: 2026-08-01. Código: `UiaSurface.TraerALaVista` + `PuntoDentroDe` (contra el
  contenedor, no la ventana).

### Un selector sin contenido no identifica nada: descártalo
- Síntoma: «Recientes», «Favoritos», «Compartido» consumían 5 intentos cada uno y fallaban.
- Causa: sin Name ni AutomationId el único selector posible sale vacío (`uia:path=;ct=Custom`).
- Fecha: 2026-08-01. Código: `GraphCrawler.EsSelectorUtil`.

### SW_RESTORE solo si IsIconic
- Síntoma: la app mapeada se encogía al iniciar el mapeo.
- Causa: SW_RESTORE sobre una ventana maximizada la devuelve a su tamaño anterior. Traer al
  frente no debe cambiar el tamaño de nadie.
- Fecha: 2026-08-01. Código: los tres caminos de enfoque (GraphCrawler, AppAligner,
  NavStrategies).

### La acción viaja con la arista
- Síntoma: rutas que «prometían» y al ejecutarse solo seleccionaban.
- Causa: en una lista solo el doble clic abre; una arista aprendida con doubleclick y
  ejecutada con click no cumple su promesa.
- Fecha: 2026-07-31. Código: `EdgeInfo.ActionType`, respetado por crawler, map_take, map_go_to.

### El destino se confirma cuando se estabiliza, y las lecturas vacías no rompen el candidato
- Síntoma: nodos fantasma de estados transitorios; después destinos reales nunca confirmados.
- Causa: las transiciones pasan por estados intermedios (el título parpadea por el nombre del
  panel). Aprender el primer cambio captura el tránsito; exigir dos lecturas buenas SEGUIDAS
  y borrar el candidato ante cualquier lectura mala descarta destinos reales.
- Fecha: 2026-07-31/08-01. Código: `GraphCrawler.EsperarCambioAsync`.

### Corta el sufijo « - App» del título
- Síntoma: dos nodos para la misma carpeta (`app-dev-buena` y `app-dev-buena-explorador-...`).
- Causa: Windows añade el sufijo DESPUÉS de pintar; el mismo sitio se lee de dos formas.
  Convención universal («doc - Word», «página - Chrome»).
- Fecha: 2026-08-01. Código: `SurfaceLocator.SinSufijoDeApp`.

### Los nombres de panel no identifican pantallas, vengan por donde vengan
- Síntoma: nodos fantasma `panel-de-navegación`, `vista-elementos`,
  `control-de-árbol-de-espacios-de-nombres`, con decenas de aristas apuntándoles.
- Causa: son una FAMILIA (título transitorio, nombre alternativo, ventana hija enfocada), no
  casos sueltos. Un nombre que vale para cualquier ventana no identifica ninguna.
- Fecha: 2026-07-31. Código: `SurfaceLocator.EsNombreDePanel`, aplicado a título Y alternativo.

### La red de seguridad valida el destino, no el reloj
- Síntoma: acabó mapeando Photos.exe con la guardia de primer plano marcando 0 escapes.
- Causa: comprobar el foreground justo tras el clic falla porque la otra app tarda en
  arrancar. Mirar a qué app pertenece el destino AL QUE SE LLEGÓ no depende de tiempos.
- Fecha: 2026-08-01. Código: `GraphCrawler.EsDelObjetivo` sobre `llegue`.

---

### Si el título no distingue las pantallas, la identidad la da el CONTENIDO
- Síntoma: al mapear Configuración, los clics funcionaban (ok=True, aterrizaban bien) pero
  «no llevó a ninguna pantalla identificable». Cero aristas aprendidas.
- Causa: muchas apps modernas viven en UNA ventana cuyo título nunca cambia —Configuración dice
  «Configuración» estés en Sistema, en Bluetooth o en Cuentas—, así que derivar la identidad del
  título funde todas sus pantallas en un solo nodo: la pantalla cambia de verdad y el sistema no
  ve ninguna transición. Con el explorador no se notaba porque allí el título ES la carpeta.
- Regla: se añade la SECCIÓN abierta —el elemento de navegación que la app tiene seleccionado, que
  es como ella misma le dice al usuario dónde está— como sub-ruta: `…/configuración#bluetooth`.
  Solo cuando aporta: si coincide con el título no dice nada nuevo y no se añade, para no romper
  las identidades que ya funcionaban.
- Alcance: general. Configuración, Spotify, Teams… casi toda app moderna es así.
- Fecha: 2026-08-03, mapeando Configuración como segunda app. Código: `SurfaceLocator.SeccionSeleccionada`.

### Un respaldo que actúa sobre OTRA aplicación no es un respaldo
- Síntoma: mapeando Configuración, el recorrido pulsó un botón del explorador de archivos que
  estaba detrás.
- Causa: Configuración no tiene «Atrás», así que el selector no resolvía en su ventana y el
  barrido de respaldo lo buscó por todo el escritorio… hasta encontrarlo en otra app. El log lo
  dijo («NO estaba en foco — hallado por barrido en 'U-PRUEBA-ORGANIZAR'») pero actuó igual.
- Regla: quien mapea una app SIEMPRE la trae al frente antes de actuar; si un selector no está en
  la ventana de delante, no está. `SoloEnFoco` también en el crawler, no solo en la capa MCP.
- Fecha: 2026-08-03.

### Se aprende una app POR SU NOMBRE, no «la que esté delante»
- Síntoma: el mapeo arrancaba sobre la app equivocada una y otra vez.
- Causa: «mapear esta app» dependía de quién tuviera el foco al pulsar el botón, y eso es frágil
  hasta el absurdo — cualquier ventana que se pusiera delante en ese instante, incluida la de
  quien lanzaba la prueba, decidía qué se mapeaba.
- Regla: `map_learn_app` recibe la app, la trae al frente ella sola (o la abre si no está viva) y
  la recorre. La intención la pone quien pide, no el azar del escritorio. Abrir una app es
  razonable cuando ALGUIEN LA PIDIÓ por su nombre; lo que no vale es abrir cosas por iniciativa
  propia al recuperarse de un fallo.
- Fecha: 2026-08-03. Código: `SurfaceMapTools.LearnApp`.

### Una llamada a otro proceso sin plazo puede congelarlo todo
- Síntoma: el mapeo se quedó tres minutos sin registrar una sola línea y ni el botón de detener
  respondía.
- Causa: las llamadas a COM y a UIA pueden no volver NUNCA. Sin plazo, el recorrido entero queda
  colgado dentro de una de ellas. Un cuelgue silencioso es peor que un fallo: el fallo se ve.
- Regla: plazo en todo lo que hable con otro proceso. Si se pasa, se sigue sin ese dato —2 s para
  preguntar la carpeta abierta— o se salta esa pantalla —12 s para leer su árbol—. El hilo colgado
  se ABANDONA: matarlo arriesgaría el estado del proceso.
- Fecha: 2026-08-03. Código: `GraphCrawler.ConPlazoAsync`.

### Una regla ganada en una app NO se exporta a otra sin verificarla
- Síntoma: Configuración quedó con 98 rutas correctas y aun así 6 de 7 navegaciones fallaban.
- Causa: la regla «al bajar a una carpeta, aprende la subida al padre» venía del explorador y se
  aplicaba en todas partes. Pero `aid=upButton` SOLO existe en el explorador, así que el grafo de
  Configuración se llenó de caminos imposibles y las rutas se planificaban por un botón que esa
  app no tiene. El mapa estaba bien; lo que sobraba eran aristas que nunca se podían recorrer.
- Regla: antes de aprender una arista basada en un elemento concreto, COMPROBAR que ese elemento
  existe en la app actual. Es exactamente para lo que sirve mapear una segunda app.
- Fecha: 2026-08-03. Código: `SurfaceMapTools.ExisteBotonSubir`, `GraphCrawler.HayBotonSubir`.

### Hay apps que IGNORAN el ratón sintético
- Síntoma: se pulsó una sección del menú de Configuración con clic simple y con doble, ambos con
  ok=True y el punto dentro del elemento; la página no se movió ni una vez.
- Causa: muchas apps WinUI no reaccionan a la entrada de ratón sintetizada. Sí reaccionan a
  `SelectionItemPattern.Select()`, que abrió la sección a la primera.
- Regla: en lo seleccionable que NO es contenido de lista, seleccionar por patrón antes de
  recurrir al ratón. El contenido queda fuera a propósito: ahí seleccionar no es abrir, y
  confundirlos rompería el explorador, donde el doble clic hace falta de verdad. Se distinguen
  por dónde viven: la navegación ocupa el tercio izquierdo de la ventana.
- Fecha: 2026-08-03. Código: `UiaSurface.RealClick`, `EsContenidoDeLista`.

### Hay diálogos OPACOS a UIA: no se pueden leer ni pulsar
- El de cambiar el nombre del equipo (clase `Shell_Dialog`) no expone ni un botón ni un texto: una
  búsqueda global de «Siguiente» devuelve cero. Es un límite honesto de esta tecnología.
- Se detectan por ausencia TOTAL de elementos accionables y se reportan como «bloqueado por algo
  que no puedo leer», que es mejor que fingir que es un lugar vacío.
- Fecha: 2026-08-03. Código: `Interrupcion.EsOpaca`.

### Un diálogo PREGUNTA; una app OFRECE IR a sitios
- Contar botones no discrimina: la ventana de Configuración pasó de 9 botones visibles a 6 según
  la sección y de golpe se detectaba como diálogo, parando el mapeo de la app entera.
- Si hay menú, lista o pestañas, no es una pregunta. Y nuestras propias ventanas nunca son un
  bloqueo: el panel del grafo tiene la forma de un diálogo y se detectaba a sí mismo.
- Fecha: 2026-08-03. Código: `Interrupcion.Leer`.

## Reglas de apps UWP / WinUI (Configuración y similares)

### El proceso es el ANFITRIÓN: se mira DENTRO para saber de quién es la ventana
- Configuración, Calculadora, Fotos y el Correo corren bajo `ApplicationFrameHost.exe`, un
  anfitrión común. El proceso que de verdad dibuja está DENTRO, en una ventana hija de clase
  `Windows.UI.Core.CoreWindow`.
- **Resuelto 2026-08-05.** Antes la identidad quedaba como `uia://ApplicationFrameHost.exe/…` para
  TODAS, y el sistema perdía el hilo entero: la pantalla se identificaba como de otra app, el
  guardia de ubicación se negaba a actuar («se esperaba calculadora.exe y estamos en
  ApplicationFrameHost.exe») y «tráela al frente» decía que no pudo con la ventana delante. Se vio
  entero al pedirle una suma a la Calculadora: no llegó ni a pulsar el primer botón.
- `AppAligner.ProcesoDe` responde ahora esa pregunta para todo el sistema —lector, localizador y
  alineación—, mirando dentro cuando el dueño es el anfitrión. La calculadora pasa a
  `uia://CalculatorApp.exe/calculadora`.
- Corolario general: **el nombre de la app no siempre es el dueño de su ventana.** En el navegador
  la app es el DOMINIO («canva.com», que no es ningún proceso) y el Panel de control vive dentro de
  `explorer.exe`. Para actuar se usa la ventana que está DELANTE, no la buscada por nombre.
- Fecha: 2026-08-03, resuelto 2026-08-05. Código: `AppAligner.ProcesoDe`, `VentanaDelUsuario`.

### No tienen «Atrás» con AutomationId estable
- La regla del explorador (`aid=backButton`) no aplica. Aquí el retroceso es la propia navegación
  lateral, que está siempre presente — otra razón por la que el cromo global es la vuelta buena y
  el historial no.

### `Select()` sustituye a un CLIC, nunca a un DOBLE clic
- Síntoma: se creaban las tres carpetas y no se entraba en ninguna de ellas, con `ok=True` en cada
  paso. En el log: «→ seleccionado por patrón (la app no responde al ratón sintético)» sobre una
  carpeta del explorador, dentro de un doble clic.
- Causa: la regla nació para Configuración —que ignora el ratón sintético pero obedece
  `SelectionItemPattern.Select()`— y se coló en el explorador. `Select()` convertía el doble clic
  en dos selecciones, y seleccionar no abre nada.
- Regla: seleccionar y abrir son intenciones distintas. Quien pide un doble clic pide ABRIR, y
  `Select()` no sabe decir eso: la sustitución solo vale para el clic simple.
- Fecha: 2026-08-03. Código: `UiaSurface.RealDoubleClick` (`permitirSelect: false`).
- **Es el tercer caso de una regla de una app rompiendo otra. El patrón ya es conocido: toda regla
  nacida en una app se prueba en la otra antes de darla por universal.**

### Navegación o contenido lo dice el CONTENEDOR, no dónde cae el elemento
- Síntoma: con la ventana en x=743, una carpeta en x=1039 se clasificaba como menú lateral.
- Causa: la guarda era «en el tercio izquierdo de la ventana = navegación». El contenido empieza
  justo después del panel, así que su PRIMERA COLUMNA aún cae dentro de ese tercio.
- Regla: se sube al contenedor. `Tree` = navegación siempre. `List`/`DataGrid` ancho (>50 % de la
  ventana) = contenido; estrecho = menú lateral. Nunca la posición del elemento suelto.
- Fecha: 2026-08-03. Código: `UiaSurface.EsContenidoDeLista`.

## Reglas del explorador de archivos de Windows 11 (`explorer.exe`)

### Un clic sobre lo YA seleccionado no selecciona: abre el renombrado
- Síntoma: una carpeta recién creada y nombrada queda seleccionada; el clic previo al doble clic
  abría su campo de edición y el doble clic caía dentro del campo en vez de entrar en la carpeta.
- Regla: si el elemento ya está en la selección, el paso de seleccionar sobra — se va directo a
  abrir. Es la regla de toda la vida del explorador (clic lento sobre lo seleccionado = renombrar).
- Fecha: 2026-08-03. Código: `SurfaceMapTools.Take`.

### El explorador NO refresca su árbol UIA ante cambios hechos fuera de él
- Se creó una carpeta desde disco con la ventana abierta en ese directorio y, un segundo después,
  UIA seguía sin verla: 0 coincidencias por nombre. Lo creado POR la interfaz sí aparece.
- Consecuencia para las pruebas: preparar terreno a espaldas de la app no reproduce el escenario
  real, y hace parecer roto lo que funciona.
- Fecha: 2026-08-03.

### El contenido vive en ventanas hijas con HWND propio
- La ventana principal (CabinetWClass) expone UN nodo UIA. La lista de archivos está bajo
  `DirectUIHWND`/`SHELLDLL_DefView`; la barra de herramientas (Atrás, Subir, direcciones,
  pestañas) bajo `Microsoft.UI.Content.DesktopChildSiteBridge` e `InputSiteWindowClass`.
  Hay que hacer FromHandle sobre cada hija. Medido: 2026-07-31.
- Código: `UiaReader.ChildContentClasses` / `CollectFromChildren`.

### Carpeta-o-archivo se le pregunta al DISCO
- `ItemType` de UIA llega VACÍO (30/30 en una carpeta de capturas) y las extensiones están
  ocultas («Captura de pantalla 1» es un .png que no lo parece). La ruta abierta se obtiene
  del propio explorador (Shell.Application → HWND → Folder.Self.Path) y `Directory.Exists`
  responde sin margen de error. Para saber QUÉ hay se lee el disco; la UI enseña CÓMO navegar.
- Fecha: 2026-08-01. Código: `GraphCrawler.CarpetaEnPrimerPlano`.

### Los archivos son puertas, no cruces
- Abrir un archivo no explora la app: la abandona (doble clic en una foto → Photos.exe).
  Se registran en el mapa (existen ahí) y no se cruzan.

### En la lista se entra con doble clic; en el panel, con clic simple
- La acción depende de DÓNDE vive el elemento, no de qué es.

### El retroceso es el botón `aid=backButton`, por identidad
- Alt+Izquierda se lo lleva quien tenga el foco y falló 4/4 veces. `backButton` /
  `forwardButton` / `upButton` tienen AutomationId estable e independiente del idioma.
  Resultado medido: regresos fallidos de 9 → 0.
- Fecha: 2026-07-31. Código: `GraphCrawler.VolverAsync`.

### El cromo persistente se detecta por REPETICIÓN y se usa como atajo
- Regla: si la misma puerta (mismo selector) aparece en 2+ pantallas distintas, es parte del
  MARCO de la app —panel lateral, barra, menú—, no de ninguna pantalla concreta. Dos
  consecuencias: (a) no se re-explora en cada nivel; (b) sirve de ATAJO para llegar a su
  destino desde CUALQUIER pantalla, sin recorrer el camino.
- **Actualización 2026-08-04:** el umbral bajó de 3 a 2 y dejó de exigir aristas CRUZADAS —basta
  con haberlas VISTO—. Recorriendo Configuración en orden, cada sección se alcanzaba solo desde la
  anterior, así que ninguna llegaba a dos orígenes y el nodo central no aparecía hasta la segunda
  vuelta. Estar en todas partes es una propiedad observable; a dónde lleva es otra pregunta.
  (`EsCromoGlobal` conserva el umbral de 3 sobre `Ubicuidad`, que es otro contador: ese mide
  apariciones totales, no orígenes distintos.)
- **Superado en parte desde 2026-08-06:** ver «Lo DECLARADO no espera al contador» más abajo. La
  repetición sigue siendo la vía para apps que nadie ha enseñado, pero ya no es la única ni manda
  sobre lo declarado.
- Síntoma que lo motivó: cada pantalla replanificaba el panel izquierdo entero y lo primero
  que pulsaba era «Inicio», devolviendo el recorrido al principio; el contenido de las
  carpetas solo se alcanzaba mucho más tarde.
- Por qué por repetición y no por una lista de nombres: así vale para cualquier app sin
  conocerla de antemano. El contenido queda fuera por definición (dos carpetas pueden tener
  cada una su «readme.txt»).
- Dónde rinde: recolocarse tras un regreso fallido («en vez de deshacer el camino, voy al
  panel y entro otra vez» — como lo haría una persona) y responder `map_go_to` sin haber
  recorrido nunca ese camino concreto.
- Fecha: 2026-08-01. Código: `SurfaceMap.Ubicuidad` / `EsCromoGlobal` / `AtajoHacia`;
  consumido por `GraphCrawler.VolverAsync` y `SurfaceMapTools.GoTo`.

### Una puerta con destino conocido NO se vuelve a pulsar
- Síntoma: «Saved Searches» se pulsaba en TODAS las pantallas —ida y vuelta, dos navegaciones
  desperdiciadas por nodo— y rompía el orden del recorrido desde el principio.
- Causa: el filtro de «navegación ya conocida» comparaba solo contra la RAÍZ, y «Saved
  Searches» estaba al fondo del panel: al arrancar no era visible, así que nunca entró en el
  conjunto de conocidas. Además el panel conserva su desplazamiento entre pantallas
  (ScrollIntoView lo deja donde estaba), así que el primer elemento visible deja de ser el
  primero de la columna. La regla correcta no es «lo que había en la raíz» sino «todo selector
  cuyo destino ya sé»: su arista se DEDUCE al registrar la puerta, sin pulsarla.
- **Ampliado 2026-08-06 (N en vez de N²):** «ya sé» pasó a preguntarle AL MAPA, no solo a lo que
  el recorredor hubiera cruzado en ESTA corrida. El mapa ya sabía a dónde llevaba un selector y el
  recorredor lo volvía a cruzar desde cada pantalla — se veía en el log, cada pantalla
  replanificando el panel entero. Desde que el grafo anota todas las puertas visibles al llegar,
  UNA visita revela el destino de cada selector conocido: una puerta se cruza una vez en toda la
  app.
- Fecha: 2026-08-01, ampliado 2026-08-06. Código: `GraphCrawler.EsNavegacionGlobalYaConocida`.

### El panel de navegación global se aprende una vez, desde la raíz
- Está en TODAS las pantallas; sin filtro, cada carpeta gana las mismas ~9 aristas (9 de 19
  rutas en la corrida medida). Más abajo se salta, y el destino de sus puertas se DEDUCE por
  selector (misma puerta = mismo destino) → malla, no estrella. La deducción vale para cromo,
  NUNCA para contenido (dos carpetas pueden tener cada una su «readme.txt»).
- Código: `GraphCrawler.EsNavegacionGlobalYaConocida`, `SurfaceMap.ObserveExits` /
  `ResolverPuertasIguales` / `EsContenido`.

### Fuera de alcance
- «Este equipo», unidades por letra, Red, Papelera, Windows, Archivos de programa: mapear el
  disco del sistema no enseña nada sobre CÓMO se navega la app. Código: `SafeToClick.FueraDeAlcance`.

---

## Reglas de arquitectura del mapeo

### Un clic dentro de una app NO lleva a otra app
- Síntoma: `uia://claude.exe/claude` era el nodo MÁS visitado del grafo (49 visitas) sin que nadie
  hubiera navegado nunca hasta allí. Había 130 aristas entre apps distintas.
- Causa: cuando otra ventana roba el foco justo después de una acción, lo que aparece delante se
  anotaba como destino. Y `DestinoConocidoDe` propagaba ese destino a TODA pantalla que compartiera
  el selector: un solo robo de foco envenenaba el grafo entero.
- Regla: una transición solo se aprende si origen y destino son de la misma app. La deducción de
  destino por selector también se limita a la misma app. El terreno ya escrito se cura al cargar.
- Fecha: 2026-08-03. Código: `SurfaceMap.LearnTraversal`, `SurfaceMap.DestinoConocidoDe`, `SurfaceMap.Load`.

### El terreno manda sobre el mapa: una arista equivocada se corrige, no se acata
- Síntoma: se entró en `docs6` perfectamente y se reportó fallo, porque el mapa esperaba llegar a
  `claude.exe`. La arista mala se quedaba mala para siempre y arrastraba toda tarea que pasara por ella.
- Causa: la llegada se juzgaba solo contra el destino APRENDIDO.
- Regla: si la puerta lleva a otro sitio de la MISMA app, la equivocada era la arista: se reaprende
  y la tarea continúa. Si lleva a otra app, no es una puerta — es un archivo que se abre, y se dice.
- Fecha: 2026-08-03. Código: `SurfaceMapTools.Take`.

### Escribir sin campo de texto no es escribir: es renombrar lo que haya seleccionado
- Síntoma: `logo-empresa.png` se convirtió en `Datos.png` y la respuesta fue «✓ escrito «Datos»».
- Causa: sin `target`, se escribía en el elemento con el foco fuera cual fuera. Si la edición en
  línea no llegaba a abrirse, el foco lo tenía el archivo seleccionado y el explorador lo
  interpretó como renombrar.
- Regla: sin `target`, solo se escribe si el foco acepta texto. Si no, se niega y se explica. Un
  daño silencioso reportado como éxito es lo peor que puede hacer esta capa.
- **Corregido 2026-08-05: el criterio no es el NOMBRE DEL TIPO, es el patrón.** Exigir `Edit` o
  `Document` erraba por los dos lados: de más ya no (las filas de listas y árboles se rechazan
  siempre, que es el caso del renombrado), pero de MENOS sí — el buscador de YouTube es un
  `ComboBox`, y también la barra de Chrome. Se rechazaban por no llamarse «Edit» siendo
  exactamente donde se escribe en la web. Un ComboBox de solo selección y uno de búsqueda no se
  distinguen por el tipo: los distingue tener `ValuePattern` y no ser de solo lectura.
- Y en un campo de texto, **pulsar es ENFOCAR**: un `Edit` no expone Invoke ni Toggle ni Select, así
  que se daba por imposible pulsar la barra de búsqueda. `SetFocus` sí — y se COMPRUEBA después,
  porque no protesta cuando el foco acaba en otra parte y lo que viene detrás de pulsar un campo es
  escribir: una vez acabó en la barra de direcciones de Chrome.
- Fecha: 2026-08-03, corregido 2026-08-05. Código: `UiaSurface.AceptaTexto`, `SurfaceMapTools.Type`.

### No estar TODAVÍA no es no estar
- Síntoma: se pulsaba «Nuevo» y «Carpeta» respondía «no se encontró»; el grupo entero se caía.
- Causa: lo que se abre tarda en aparecer, y se preguntaba antes de que el menú existiera.
- Regla: ante un «no se encontró», se espera a que la pantalla se estabilice y se mira UNA vez más.
  Espera por ESTADO, no por reloj. Si sigue sin estar, entonces no está.
- Fecha: 2026-08-03. Código: `SurfaceMapTools.Take`, `EsperarPantallaLista`.

### «Atrás» NO es una arista: es un gesto de historial
- Síntoma: `map_go_to` fallaba el último tramo de las vueltas — «pulsé Atrás pero seguimos en
  win-x64».
- Causa: el botón depende de CÓMO se llegó, no de dónde se está. «win-x64 --Atrás--> documentos»
  fue cierto en un recorrido y falso al llegar a win-x64 por otro camino. Una arista describe una
  propiedad del sitio; un gesto de historial no lo es.
- Regla: se usa para volver durante el recorrido, nunca se aprende. La vuelta que SÍ es mapa es
  la navegación global (panel lateral, barra de inicio), que está en todas las pantallas y
  siempre lleva al mismo lugar.
- Fecha: 2026-08-02. Código: `GraphCrawler.VolverAsync`.

### Una puerta se cruza UNA vez, desde donde primero se pueda
- Síntoma: el mapa nunca supo volver a la raíz desde ningún sitio.
- Causa: vetar todo el cromo visible en la raíz dejaba agujeros permanentes: «Documentos» no
  puede aprenderse ESTANDO en documentos —pulsarlo no cambia de pantalla— y quedaba vetado
  también en las demás. Hay que saltar solo lo que ya tiene destino conocido.
- Fecha: 2026-08-02. Código: `GraphCrawler.EsNavegacionGlobalYaConocida`.

### Se mira alrededor SIEMPRE: una pantalla no se conoce de una vez
- Síntoma original: el asistente navegaba a una carpeta llena y leía «0 salidas conocidas», porque
  el mapa solo se llenaba durante un recorrido automático. Preguntar «dónde estoy» pasó a registrar
  las salidas de la pantalla actual.
- **Corregido 2026-08-05:** había un guardia que se saltaba la relectura cuando la pantalla «ya se
  conocía» (seis salidas recorribles y tres acciones). Ahorraba una lectura del árbol y costaba dos
  cosas: lo que aparecía DESPUÉS no entraba nunca —una carpeta recién creada, un botón que sale al
  seleccionar algo: es el «a veces verde y a veces gris» que se notaba en los puntos—; y desde que
  el grafo se arma con lo VISIBLE, sin volver a mirar no hay forma de saber qué dejó de estar.
  Una pantalla cambia mientras se usa. Si el grafo es lo que se ve, hay que mirar.
- Precio: una lectura del árbol de UI por llamada. Es lo que cuesta que el grafo sea lo que se ve
  y no lo que se recordaba.
- Fecha: 2026-08-02, replanteado 2026-08-05. Código: `SurfaceMapTools.ObservarAqui`.

### Cruzar una puerta sin explorar se juzga por el CAMBIO, no por el destino
- Síntoma: `map_take` sobre una puerta reportaba fallo aunque llegara.
- Causa: comparaba la llegada contra el marcador `?selector`, que no es un sitio. Para una puerta
  sin destino conocido el éxito es que la pantalla cambie — y lo descubierto se aprende, que es
  justo para lo que existen las puertas.
- Fecha: 2026-08-02. Código: `SurfaceMapTools.Take`.

### Las dependencias y artefactos de compilación están fuera de alcance
- `node_modules`, `.git`, `.next`, `bin`, `obj`, `dist`… Una corrida se perdió 20 minutos dentro
  de `node_modules/.next/dev`. Mismo criterio que «Este equipo»: el mapa describe la app, no el
  disco. Código: `SafeToClick.FueraDeAlcance`.


### El grafo es el plan, no el registro
- El bucle consume una FRONTERA de puertas registradas en el mapa; no listas en memoria.
  Un regreso fallido cuesta solo las puertas de ese nodo. Parar no pierde el pendiente.
- Fecha: 2026-08-01. Código: `GraphCrawler.RecorrerAsync` / `Frente`.

### El grafo se arma con las puertas VISIBLES: el mapa es memoria, no permiso
- Antes una puerta solo era arista al CRUZARLA; verla no contaba. Ahora toda puerta visible entra
  desde que se ve, con el destino por descubrir, y se puede tomar en el momento 0 — al cruzarla se
  le rellena el destino. Una pantalla recién abierta da decenas de salidas en la primera mirada.
- El extremo sin resolver es único por puerta (`?selector`), no un nodo «desconocido» compartido:
  eso inventaría caminos falsos `A → desconocido → B`. Son aristas COLGANTES.
- `Route()` no planifica a través de ellas —no se sabe a dónde dan— pero `map_go_to` mira antes de
  negarse: si hay algo delante que se llama como el destino, lo toma. Tomar verifica y aprende, así
  que la ruta que faltaba queda hecha: **se improvisa una vez y a partir de ahí es ruta.**
- Consecuencia para `map_take`: si el mapa no conoce lo pedido, lo busca en la pantalla de ahora.
  Y no exige que la pantalla cambie — una barra de búsqueda o una casilla hacen su trabajo sin ir a
  ninguna parte. El síntoma que lo motivó: «lo veo pero como no lo conozco no puedo pulsarlo»,
  porque ver leía la pantalla y pulsar leía el mapa.
- Se actúa sobre el elemento YA LEÍDO, sin volver a resolverlo por nombre: el lector encontraba
  «Buscar en Notas» y el ejecutor no lo resolvía ni en cinco intentos.
- La arista recuerda CUÁNDO se vio por última vez. Dejar de verse no la borra —se reaprendería sin
  fin— pero tampoco se ofrece como si estuviera: prometer una salida que no está es mandar a pulsar
  el vacío.
- La PUERTA forma parte de la identidad de la arista (`de → a → selector`): al panel de «Imágenes»
  se llega desde el árbol y desde los anclados, y con la clave antigua la segunda pisaba a la
  primera. Dos caminos son dos caminos.
- Fecha: 2026-08-05, decidido con el usuario. Código: `SurfaceMap.ObserveExits`, `Clave`,
  `SigueALaVista`, `SurfaceMapTools.Take` / `GoTo`, `UiaSurface.EjecutarSobre`.

### El observador no se observa: se mira la ventana DEL USUARIO
- Para hablarle a Ü hay que pulsar la carita, y eso pone NUESTRA ventana delante. Justo entonces
  se preguntaba «¿ves la barra de búsqueda?» y el sistema se negaba —«lo que está delante es mi
  propia interfaz»—, que además el modelo contaba en voz como «Claude se ha puesto por medio»,
  culpando a una app que ni estaba.
- Negarse era la respuesta equivocada a un guardia correcto: la ventana del usuario no ha
  desaparecido. `VentanaDelUsuario()` responde en un solo sitio: (1) la de delante si no es
  nuestra —y de paso se recuerda—; (2) si delante estamos nosotros, la ÚLTIMA que la persona
  activó; (3) y solo sin recuerdo, la primera por debajo en el orden Z.
- El paso 2 es el que importa: el orden Z de un instante no sirve, porque debajo de la nuestra
  puede haber cualquier cosa —en la prueba había Claude— y responder por ella es tan falso como
  negarse. Se refresca en cada sondeo del localizador.
- Fecha: 2026-08-05, observado por el usuario. Código: `AppAligner.VentanaDelUsuario`, `UiaReader.Read`.

### El grafo es de navegación Y de ejecución: se registra todo, se cruza solo navegación
- Cada puerta se clasifica en `navegacion` (lleva a otra pantalla) o `accion` (hace algo aquí:
  Nuevo, Cortar, Pegar, Eliminar). **Las dos se registran** — sin las de acción el asistente
  llega a cualquier sitio y no puede hacer nada al llegar, que es media razón de tener el grafo.
- Durante el MAPEO solo se cruzan las de navegación segura: pulsar «Eliminar» para ver a dónde
  lleva no es explorar, es romper. Durante la EJECUCIÓN deliberada se toman las de acción.
- `map_routes_from` las presenta por separado: «¿a dónde puedo ir?» y «¿qué puedo hacer aquí?»
  son preguntas distintas, y mezclarlas obliga al modelo a adivinar cuál es cuál.
- v1 determinista por tipo de control. El refinamiento fino —«Guardar como…» abre un diálogo,
  ¿navega o ejecuta?— es CRITERIO, no sintaxis: ahí entra el LLM de capa 2, leyendo las puertas
  ya registradas. La clasificación es un dato del mapa, no del clasificador: cambiar de criterio
  no obliga a volver a mapear.
- Fecha: 2026-08-02. Código: `SafeToClick.Clasificar`, `EdgeInfo.Kind`.

### Seleccionar y abrir son cosas distintas sobre el mismo elemento
- Para cortar un archivo hay que SELECCIONARLO (un clic); la acción aprendida para un archivo es
  el doble clic, que lo abre en otra aplicación. Sin poder forzar la forma de pulsar, organizar
  archivos por la interfaz era imposible: cualquier intento de tocar uno lo abría.
- `map_take` acepta `action: click|doubleclick` para forzarla, y una selección deliberada no
  espera cambio de pantalla —exigirlo reportaría fallo a un clic que hizo justo lo pedido—.
- Fecha: 2026-08-02. Código: `SurfaceMapTools.Take`.

### El SELECTOR desempata cuando dos elementos se llaman igual
- En el explorador hay dos «Detalles» (el modo de vista y el panel lateral) y dos «Nueva carpeta»
  (el archivo y su campo de renombrado). Responder «coincide con 2, elige por nombre exacto»
  dejaba al asistente sin salida: el nombre exacto era el mismo. Se acepta el selector.
- Fecha: 2026-08-02. Código: `SurfaceMapTools.Take`.

### El foco se recupera antes de actuar, y los paneles del shell se descartan con Escape
- El centro de notificaciones, el buscador o el menú inicio se ponen delante solos y la tarea
  muere ahí. `SetForegroundWindow` NO los aparta —Windows lo bloquea mientras uno de ellos tiene
  el foco—: hay que descartarlos con Escape, como haría una persona, y después recuperar la app.
- La vuelta se confirma con LAS DOS fuentes: el sistema (quién está delante) y el localizador
  (que sondea cada 800 ms). Conformarse con la primera dejaba una ventana donde el foco ya era
  correcto pero la superficie seguía siendo la de antes, y la ruta se calculaba desde el sitio
  equivocado. Recuperar el foco no es haberlo notado.
- No se LANZA nada: si la app no está viva, se dice. Abrir aplicaciones por iniciativa propia no
  es recuperarse.
- Fecha: 2026-08-02. Código: `SurfaceMapTools.AsegurarFoco`.

### Windows oculta las extensiones conocidas: el nombre del mapa es el que se VE
- «factura-enero.pdf» aparece en el mapa como «factura-enero». Buscar por el nombre real del
  archivo no encuentra nada. Lo que el mapa guarda es lo que la interfaz muestra.

### Un sistema que desbloquea, no un parche por cada bloqueo
- Estábamos arreglando los colapsos de uno en uno —un diálogo, otro diálogo, otro más— en vez de
  tener un mecanismo que los resuelva. Los bloqueos comparten forma: algo se cruza, hay que
  responderle, y después hay que volver a donde íbamos.
- `map_unblock` hace las tres cosas: sale del atasco, REANUDA por el mapa (salir no sirve de nada
  si la tarea no puede continuar) y deja constancia del incidente.
- Lo automático resuelve SOLO lo que no tiene decisión: el aviso informativo, el de una única
  salida, donde no se elige nada sino que se acusa recibo. En cuanto hay dos opciones hay una
  DECISIÓN y sube al consciente.
- **«No comprometer» no es lo mismo que acertar** (corregido por el usuario, 2026-08-03). La
  primera versión prefería siempre Cancelar/No/Cerrar; parecía prudente y era falso: si la tarea
  quería continuar de verdad, cancelar por regla la rompe igual, solo que en silencio y con aire
  de cautela. Continuar o no depende de lo que se estuviera intentando, y eso solo lo sabe quien
  tiene la intención. Se prefiere preguntar a acertar por casualidad.
- Al escalar se incluye QUÉ se estaba intentando: sin eso, quien decide no sabe para qué apareció
  el diálogo, que es justamente el dato que necesita.
- Se verifica que el diálogo se FUE. Un desbloqueo que no desbloquea es peor que no intentarlo.
- Verificado el 2026-08-03 provocando el aviso de extensión: detectó, eligió «No», el archivo
  conservó su extensión y la ejecución quedó lista para continuar.
- El incidente se registra pero NO dispara mejoras automáticas: si un diálogo se repite, eso es
  material para una regla, y esa decisión es de quien desarrolla.

### Un veto responde a UNA pregunta: no lo reutilices para otra
- `SafeToClick.Auto` responde «¿puede el explorador autónomo pulsar esto MIENTRAS MAPEA?», y por
  eso rechaza todos los botones: al mapear, un botón nunca es navegación. Usarlo como veto al
  responder un diálogo bloqueaba incluso pulsar «No», que era justo lo que salvaba el archivo.
- Responder a un diálogo es legítimo —para eso está—; lo que no lo es es responder «Eliminar».
  El veto correcto mira el VERBO (`EsDestructivo`), no el tipo de control.
- Fecha: 2026-08-03.

### Un DIÁLOGO no es un lugar: es una interrupción
- Síntoma: ante un aviso de Windows el sistema respondía «estás en
  uia://explorer.exe/ubicación-no-disponible, 15 salidas», como si fuera un nodo más del grafo.
- Causa: error de modelado. Un diálogo no es un sitio al que se llega: es algo que se cruza en el
  camino, dice POR QUÉ, y ofrece opciones entre las que hay que elegir. Es la misma corrección
  que ya hicimos con los menús («no es otro sitio, es una capa»).
- Consecuencia: quien tuviera que resolverlo —persona o modelo— recibía ruido en vez del dato
  útil. Ahora se reporta como interrupción, con su texto y sus opciones, y se dice que no hay
  rutas desde ahí: primero hay que responder.
- Se reconoce por su FORMA (pocos botones de respuesta + texto que explica), no por el título,
  que cambia con el idioma y la versión. El explorador normal, con 18 botones, no se confunde.
- Solo DESCRIBE, no decide: elegir entre «Aceptar», «Omitir» o «Sí» es criterio —el aviso de
  cambiar la extensión de un archivo tiene un «Sí» que lo corrompe— y esa decisión pertenece a la
  capa consciente, con el veto de SafeToClick encima.
- Fecha: 2026-08-03. Código: `SurfaceMapTools.DescribirInterrupcion`.

### La señal de que algo apareció es que haya MÁS que antes, no que haya alguno
- Síntoma: un grupo de pasos fallaba intermitentemente al elegir una opción de menú.
- Causa: se daba por abierto el menú al encontrar cualquier `MenuItem` en el árbol, y pueden
  quedar restos del menú anterior. Se continuaba sin que el menú estuviera abierto de verdad.
- Regla: contar ANTES de pulsar y esperar a que el número aumente. Es el mismo principio que ya
  resolvió la identidad de pantalla y la confirmación de llegada: comparar contra el estado
  previo, no contra cero.
- Fecha: 2026-08-03. Código: `SurfaceMapTools.CuantosMenus` / `EsperarMenu`.

### El banco de pruebas también tiene estado: no le tires el suelo a la app
- Varias corridas «fallaron» por culpa del guion de pruebas, no del sistema: borrar las carpetas
  mientras el explorador estaba dentro lo dejaba apuntando a una ubicación inexistente, con un
  diálogo de error bloqueando todo lo demás. El reinicio debe SACAR a la app de la zona antes de
  tocar el disco.
- Es la versión de laboratorio de la misma regla de siempre: verificar el estado antes de actuar.
- Fecha: 2026-08-03.

### Fallar RÁPIDO es parte de ser honesto
- Síntoma: una sola llamada tardó 181 s en fallar. Un fallo que tarda tres minutos parece un
  cuelgue, no un fallo.
- Causa: si un selector no resolvía en la ventana en foco, el ejecutor barría el árbol completo de
  TODAS las ventanas abiertas buscándolo. Con un navegador con muchas pestañas eso son minutos.
- Regla: cuando quien llama ya verificó la ubicación (`at`), el barrido es puro daño — si el
  elemento no está en la ventana de delante, no está. `UiaSurface.SoloEnFoco`.
- Fecha: 2026-08-03.

### Pregúntale a UIA lo que necesitas, no le pidas la pantalla entera
- `SeleccionActual` recorría todo el lector —ventanas hijas, menús, cientos de elementos, un viaje
  entre procesos por cada uno— para saber qué había marcado: ~3 s por llamada, en cada acción. Una
  condición compuesta (`ListItem AND IsSelected`) lo resuelve de una vez.
- Igual tras abrir un menú: interesan los `MenuItem` que acaban de aparecer, no las 300 puertas que
  la pantalla ya tenía. De ~7 s a una fracción.
- Medido: `map_where_am_i` 3.045 → 683 ms; abrir un submenú 150.030 → 2.634 ms; la tarea completa
  de organizar 222 → 36 s. Fecha: 2026-08-03.

### Una verificación que no tolera el asentamiento bloquea el paso siguiente
- El ancla de ubicación rechazaba en 11 ms porque, justo tras crear una carpeta, la superficie pasa
  un instante por la ventana emergente del menú. Esperar ~1 s a que se asiente mantiene intacta la
  garantía —si sigue sin coincidir, no se actúa— y deja de romper cadenas correctas.
- Fecha: 2026-08-03. Código: `SurfaceMapTools.ComprobarUbicacion`.

### No leas el árbol entero para responder algo que el sistema contesta al momento
- `LeerSalidasAsync` hacía un `Read()` completo del árbol UIA solo para saber quién estaba
  delante, y luego otro para usarlo. Recorrer una pantalla llena cuesta cientos de milisegundos:
  era medio segundo por nodo tirado en una pregunta que `GetForegroundWindow` contesta al momento.
- Fecha: 2026-08-02.

### Para verificar rápido hay que PREGUNTAR, no esperar al siguiente latido
- Síntoma: navegar por el grafo era correcto pero lento — una ruta de varios saltos se iba en
  segundos.
- Causa: la verificación de llegada consultaba `SurfaceLocator.Current`, un valor cacheado que se
  refresca cada 800 ms. Ese retraso era el suelo de latencia de CADA salto: se esperaba a que el
  reloj confirmara algo que ya había ocurrido. Es la misma lección que con el proceso en primer
  plano, en otro sitio.
- Con lectura inmediata: ~700 ms por salto, incluida la pulsación real y su verificación.
- Fecha: 2026-08-02. Código: `SurfaceLocator.Ahora()`.

### LA UBICACIÓN ES EL ANCLA: quien actúa declara dónde cree estar, y si no coincide no se actúa
- Síntoma: se pegaban archivos en su propia carpeta de origen, se creaban carpetas anidadas, y la
  tarea seguía «funcionando» varios pasos después del error real.
- Causa: un paso que falla deja el recorrido en otra pantalla, y las acciones siguientes se
  ejecutan igual de bien… sobre el sitio equivocado. La garantía estaba en quien LLAMA (que puede
  olvidarla) en vez de en el sistema.
- Regla: `map_take` y `map_type` aceptan `at` con la superficie esperada. Si la real no coincide,
  se rechaza la acción y se dice dónde estamos. El fallo aparece donde se produce.
- Fecha: 2026-08-02. Código: `SurfaceMapTools.ComprobarUbicacion`.

### Un selector cuyo destino depende de la pantalla NUNCA se generaliza
- Síntoma: el recorrido subía en escalera —de la carpeta de pruebas a Documentos, a felip, a
  Usuarios, a «Disco local (C:)»— creyendo obedecer al mapa.
- Causa: `aid=upButton` es el MISMO selector en todas las pantallas pero lleva a un sitio distinto
  en cada una (al padre de donde estés). La deducción «mismo botón → mismo destino» es cierta para
  el cromo global y falsa para estos. Aplicarla convirtió el mapa en una escalera.
- Regla: `upButton`/`backButton`/`forwardButton` se aprenden por pareja concreta y se excluyen de
  la deducción por selector y de la detección de cromo ubicuo.
- Fecha: 2026-08-02. Código: `SurfaceMap.EsRelativo`.

### «Atrás» es estado EFÍMERO de la sesión, no dato del mapa (idea del usuario)
- No se puede guardar como arista (depende de cómo se llegó) pero SÍ se sabe a dónde lleva ahora:
  al sitio del que se vino. Un historial vivo permite usarlo cuando el destino coincide y
  descartarlo cuando no, en vez de elegir entre aprenderlo mal o no tenerlo.
- Al volver al sitio anterior se DESAPILA en vez de apilar; si no, las idas y vueltas harían que
  «Atrás» prometiera un bucle.
- Fecha: 2026-08-02. Código: `SurfaceMapTools._historial`, `DestinoDeAtras`.

### Un doble clic es DOS pulsaciones seguidas, no dos clics completos
- Síntoma: «pulsé la carpeta pero no se llegó», con ok=True; de ahí salieron los pegados en la
  carpeta equivocada.
- Causa: repetir toda la rutina de clic —enfocar, desplazar a la vista, releer la caja, mover el
  cursor— mete ~300 ms entre pulsaciones y Windows deja de verlo como doble clic: lo interpreta
  como dos clics sueltos, que solo SELECCIONAN. El primero hace el trabajo caro; el segundo va
  inmediatamente después, en el mismo punto y sin recolocar nada.
- Fecha: 2026-08-02. Código: `UiaSurface.RealDoubleClick`.

### «Subir» es estructural; «Atrás» es histórico
- `upButton` desde una carpeta lleva SIEMPRE a la que la contiene, se haya llegado como se haya
  llegado: es una propiedad del sitio y por tanto una arista legítima. `backButton` depende del
  historial y no lo es. Al bajar por un elemento de lista se aprende la subida al padre; sin eso
  el grafo bajaba y no subía, y una tarea que creaba carpetas hermanas las creaba ANIDADAS
  —Windows lo paró con «la carpeta de destino es una subcarpeta de la de origen»—.
- Fecha: 2026-08-02. Código: `SurfaceMapTools.AprenderSubida`, `GraphCrawler.PulsarYAprenderAsync`.

### Antes de una acción destructiva, di sobre QUÉ actúa
- «Cortar», «Copiar», «Eliminar» y «Cambiar nombre» operan sobre LO SELECCIONADO, y el asistente
  no tenía forma de saber qué era. Un paso previo falló, la selección se quedó en una carpeta
  recién creada, y el siguiente «Cortar» la cortó a ella. Nadie mintió —cada paso reportó su
  fallo— pero quien actuaba no sabía sobre qué actuaba.
- La selección se lee ANTES de pulsar (Cortar la vacía) y con lectura propia (el snapshot del
  lector puede ser de otra pantalla). Solo cuenta la lista de contenido: una pestaña activa o un
  botón de radio marcado también están «seleccionados» y no son sobre lo que actúa Cortar.
- Fecha: 2026-08-02. Código: `SurfaceMapTools.SeleccionActual`.

### Un paso encadenado sin comprobar envenena todo lo que viene detrás
- Los fallos de esta sesión no fueron del sistema —reportó cada uno— sino del guion que seguía
  adelante igual. Toda secuencia de acciones debe PARAR en el primer resultado que no confirme lo
  esperado; si no, un fallo silencioso se convierte en daño real varios pasos después.

### Capa 1 determinista; LLM en capa 2
- Lo que es regla (carpeta-vs-archivo, nombres de panel) se codifica y es gratis e
  instantáneo. El LLM entra a lo que es CRITERIO (¿estas dos pantallas son la misma?, ¿qué
  vale la pena explorar primero?) y siempre sobre datos ya saneados por la capa 1 — un LLM
  razona mal sobre datos sucios.

### Orden de exploración: como lee una persona
- Columna izquierda primero, de arriba abajo; luego contenido, de arriba abajo. En
  profundidad: se agota la sección antes de pasar a la hermana. Reproducible = se nota qué falta.

### Aceptado no es ejecutado (principio transversal del proyecto)
- Toda API de UI reporta éxito de cosas que no hicieron nada: clics fuera de área, clics a
  (0,0), Process.Start de un alias inexistente. Cada acción se VERIFICA por su efecto
  (¿cambió la superficie? ¿llegué a donde esperaba?), nunca por su valor de retorno.

---

## La estructura por NIVELES (2026-08-05 â†’ 08-07)

> Esta secciÃ³n es el contrato del grafo. Todo lo de arriba describe cÃ³mo se lee y se cruza una
> interfaz; esto describe **quÃ© forma tiene el mapa resultante** y por quÃ© esa forma no se mueve.
> Si un nivel cambia solo, es un fallo de uno de estos invariantes, y el log de `grafo:` dice cuÃ¡l.

### QuÃ© significa Â«primer nivelÂ»: PERMANENCIA, no visibilidad
- La prueba es una pregunta, y se hace elemento por elemento:
  **Â«si me voy a cualquier otra subpÃ¡gina de esta app, Â¿ESTO seguirÃ­a ahÃ­?Â»**
- SÃ­ â†’ primer nivel. Desaparece al cambiar de secciÃ³n â†’ no lo es, por muy visible que estÃ© ahora.
  Ejemplos canÃ³nicos: la barra de tareas de Windows, las pestaÃ±as del navegador, el menÃº principal
  de una web, el panel lateral del explorador. Lo que NO es: el contenido de la pantalla actual
  (archivos, accesos rÃ¡pidos, filas) y las acciones (Copiar, Eliminar, Nuevo).
- El nombre de industria es **chrome** (*application chrome*); en este cÃ³digo, Â«cromoÂ».
- **El panel entero, tambiÃ©n lo que cuelga dentro.** Si dentro del panel permanente hay un grupo
  desplegado â€”Â«OneDriveÂ» con sus carpetas, Â«Este equipoÂ» con sus unidadesâ€”, esos hijos tambiÃ©n son
  del primer nivel. Lo que decide es dÃ³nde VIVEN, no cuÃ¡nto se sangran.
- SÃ­ntoma que lo motivÃ³: se pedÃ­a Â«lo que estÃ¡ siempre a la vistaÂ» y el maestro elegÃ­a los accesos
  rÃ¡pidos del centro, que se ven grandes y desaparecen al moverse.
- Fecha: 2026-08-06, definido por el usuario. CÃ³digo: `MaestroDeApps.Instruccion`.

### El nivel es de la SALIDA, no del camino por el que se llegÃ³
- Entrar en Â«DescargasÂ» viniendo de Â«NotasÂ» no pone Descargas debajo de Notas: las dos son del
  primer nivel. Un nivel medido desde el paseo cambia al dÃ­a siguiente, entrando en otro orden.
- Corolario operativo: el nivel se fija la primera vez y **no lo mueve nada** â€”ni navegar, ni
  aÃ±adir elementos nuevos, ni remapearâ€”. Lo que se declara a mano gana siempre sobre la deducciÃ³n.
- Fecha: 2026-08-04 (mapa) y 2026-08-06 (dibujo, que iba por libre). CÃ³digo: `EdgeInfo.NivelNav`,
  `NivelFijado`, `GraphExplorerWindow.DibujarGrafo`.

### El nivel viaja con la salida a TODA la app, y con sus copias
- Una salida del nivel se hereda a todas las pantallas como una COPIA (`Heredada`), y una pantalla
  nueva anota su propia apariciÃ³n de las mismas puertas. Las dos veces hay que llevar `NivelNav`,
  `NivelFijado` y `PorPersona`.
- SÃ­ntomas de haberlo olvidado: (a) se enseÃ±aban quince y no se ponÃ­a azul ninguno â€”la copia
  heredada nacÃ­a con nivel âˆ’1â€”; (b) se marcaban cuatro y al ENTRAR en ellas volvÃ­an a verde â€”la
  apariciÃ³n nueva nacÃ­a sin el sello humanoâ€”. La correcciÃ³n es de la salida en toda la app, no de
  la pantalla desde la que se hizo.
- Fecha: 2026-08-06. CÃ³digo: `SurfaceMap.Heredada`, `ObserveExits`.

### Una arista nace por TRES vÃ­as; la enseÃ±anza se repone en una sola funciÃ³n
- Observarla (`ObserveExits`), cruzarla a propÃ³sito (`LearnTraversal`) y ver pasar una navegaciÃ³n
  hecha a mano (`Observe`). La reposiciÃ³n del nivel estaba copiada en dos y ausente en la tercera
  â€”justo la que usa una persona paseandoâ€” y el diagnÃ³stico midiÃ³ Â«NINGÃšN nodo llega declaradoÂ»
  con diecisiete enseÃ±adas en disco.
- Una pregunta, una respuesta, un sitio: `AplicarEnsenanza`, llamada en las tres vÃ­as **y al
  cargar el mapa**, que sana lo persistido venga de la versiÃ³n de cÃ³digo que venga.
- Fecha: 2026-08-06/07. CÃ³digo: `SurfaceMap.AplicarEnsenanza`.

### La enseÃ±anza llega a los nodos por su NOMBRE, no solo por las aristas
- Buscar el nivel en las aristas es un puente frÃ¡gil: la arista puede no existir aÃºn, y las que
  nacen viendo pasar una navegaciÃ³n pierden la etiqueta cuando la atribuciÃ³n del clic falla.
- El puente que no se rompe es la IDENTIDAD: la pantalla `explorer.exe/notas` nace de la puerta
  Â«NotasÂ» â€” su nombre ES la etiqueta enseÃ±ada. Se casan por nombre normalizado (`Reconocedor`).
  Las aristas quedan como primera fuente; el nombre, como red.
- Fecha: 2026-08-07. CÃ³digo: `GraphExplorerWindow` (bloque `declarados`), `SurfaceMap.EnsenanzasDe`.

### El centro del grafo es la APP, no la pantalla por la que se entrÃ³
- La raÃ­z era el primer sitio pisado, asÃ­ que entrando por Â«MÃºsicaÂ» esa pantalla quedaba en el
  centro y sus HERMANAS colgaban a un escalÃ³n. **Un nivel medido desde uno de sus miembros no
  puede tener a todos sus miembros a la misma altura.**
- El centro es un nodo que no es ninguna pantalla (`nivel://<app>`). La fila 0 es suya y de nadie
  mÃ¡s; el primer nivel es la fila 1.
- Fecha: 2026-08-06, medido con el diagnÃ³stico (`raÃ­z Â«explorer.exe/mÃºsicaÂ»`).

### Cada fuente escribe SOLO donde la anterior no llegÃ³
- La profundidad se asignaba en siete escrituras encadenadas sobre el mismo diccionario, cada una
  pisando a la anterior: **el orden decidÃ­a el resultado**, y por eso cada arreglo movÃ­a el fallo
  de sitio en vez de eliminarlo. Tres intentos se perdieron ahÃ­.
- Orden Ãºnico, sin reposiciones ni hundidores: (1) lo declarado â€”nivel N â†’ fila Nâ€”, (2) el nivel
  que el mapa conoce de cada pantalla, (3) el paseo, solo para rellenar huecos.
- Regla general, mÃ¡s allÃ¡ de este caso: cuando varias fuentes escriben el mismo dato, o se ordenan
  explÃ­citamente o el resultado lo decide el azar del orden de llamada.
- Fecha: 2026-08-06/07. CÃ³digo: `GraphExplorerWindow.DibujarGrafo`.

### Llegar al primer nivel no es estructura; salir de Ã©l hacia dentro, sÃ­
- Al cromo se llega desde cualquier parte â€”eso es lo que lo hace cromoâ€”, asÃ­ que la arista del
  centro ya lo dice entero. Dibujar ademÃ¡s el paseo Â«nivel 3 â†’ NotasÂ» insinuaba que para llegar a
  Notas hay que pasar por el nivel 3, lo contrario de lo que significa ser del primer nivel.
- La navegaciÃ³n nunca se confundiÃ³: `Route()` busca en anchura y ofrece el cromo como tramo
  virtual desde cada nodo, asÃ­ que llega en un salto. El mapa CONSERVA la transiciÃ³n â€”pasÃ³ de
  verdad, es terreno honestoâ€”; solo el dibujo de niveles deja de contarla como jerarquÃ­a.
- Fecha: 2026-08-07, observado por el usuario. CÃ³digo: `GraphExplorerWindow` (bucle de `traza`).

### Aristas virtuales al planificar, nunca materializadas
- Â«Que se creen las aristas entre todos los de primer nivelÂ» se resuelve haciendo que `Route()`
  considere el cromo disponible desde cualquier pantalla de la app. Materializarlas guardarÃ­a el
  mismo hecho una vez por pantalla â€”diecisÃ©is salidas por cada sitioâ€” y dos copias de una misma
  verdad acaban siempre desincronizadas.
- Fecha: 2026-08-05, decidido con el usuario. CÃ³digo: `SurfaceMap.Route`.

### Lo DECLARADO no espera al contador
- El umbral de Â«visto desde dos pantallasÂ» existe para DEDUCIR quÃ© es mobiliario fijo cuando nadie
  lo ha dicho. Cuando alguien lo declara â€”el usuario seÃ±alando, o el maestro mirandoâ€” ya estÃ¡
  dicho: hacerle esperar es pedir pruebas de algo que acaban de afirmar.
- Mientras se mide la enseÃ±anza, `SoloLoDeclarado` silencia la deducciÃ³n por repeticiÃ³n. Con las
  dos fuentes activas, cada punto azul podÃ­a venir de cualquiera y el experimento era ilegible.
- Fecha: 2026-08-05/06. CÃ³digo: `SurfaceMap.CromoDe`, `SoloLoDeclarado`.

---

## La enseÃ±anza de apps (el Â«maestroÂ»)

### El maestro ENSEÃ‘A; nosotros ejecutamos y verificamos
- Un modelo que mira la pantalla distingue un panel lateral de una lista de archivos porque
  entiende para quÃ© sirve cada cosa, no porque la haya contado. Eso es insustituible.
- Pero NO pulsa. Computer use acciona por coordenadas, y las coordenadas fallan en silencio
  reportando Ã©xito â€” justo lo que este sistema lleva meses evitando. Cruzar y comprobar sigue
  siendo nuestro, por identidad y por consecuencia.
- Fecha: 2026-08-05, decidido con el usuario. CÃ³digo: `MaestroDeApps`.

### EL NÃšMERO ES EL PUENTE entre ver y accionar
- Un modelo de visiÃ³n puede decir Â«el menÃº de la izquierdaÂ» y eso no identifica nada: describir no
  es seÃ±alar. Con un nÃºmero pintado sobre cada puerta, su respuesta â€”Â«el 3, el 7 y el 12Â»â€” se
  traduce al elemento exacto del Ã¡rbol UIA. **La imagen le da el sentido; el nÃºmero, la identidad.**
- Un nÃºmero que no exista se ignora: el puente son los que pintamos nosotros, no los que Ã©l imagine.
- Los nÃºmeros van grandes y opacos mientras se enseÃ±a: a 18 px sobre una ventana maximizada son el
  1 % de la imagen, y un modelo que no los lee bien responde lo cÃ³modo (Â«no reconozco navegaciÃ³nÂ»)
  en vez de equivocarse.
- Fecha: 2026-08-05/06. CÃ³digo: `GraphExplorerWindow.Numerar`, `PuertasNumeradas`.

### La app, los puntos y la foto vienen de la MISMA ventana y del mismo instante
- Entre leer los puntos y sacar la foto pasan segundos, y en esos segundos el foco puede irse: se
  llegÃ³ a leer los puntos del explorador y aplicar la lecciÃ³n a `claude.exe`. Antes que enseÃ±ar
  mal, no enseÃ±ar.
- La foto es de la VENTANA, no del escritorio: con la pantalla entera se le manda tambiÃ©n el
  editor y el navegador, y Â«Â¿quÃ© es aquÃ­ navegaciÃ³n permanente?Â» se queda sin Â«aquÃ­Â» â€”la primera
  respuesta asÃ­ fue la lista vacÃ­aâ€”.
- Fecha: 2026-08-05/06. CÃ³digo: `MaestroDeApps.EnsenarAsync`, `Screenshotter.CaptureVentanaBase64Png`.

### El mapa tiene que CONOCER la pantalla antes de poder fijarle niveles
- Fijar un nivel busca la SALIDA con ese nombre; con el grafo reciÃ©n limpiado no existe ninguna,
  asÃ­ que las quince elecciones del maestro rebotaban una a una y el resumen decÃ­a Â«no reconociÃ³
  navegaciÃ³n permanenteÂ» â€” que era falso y mandÃ³ a buscar el fallo en el prompt.
- Se anotan las puertas ANTES de preguntar, y justo las que llevan nÃºmero: asÃ­ lo que el maestro
  seÃ±ale existe por construcciÃ³n.
- Fecha: 2026-08-06. CÃ³digo: `GraphExplorerWindow.EnsenarLaAppAsync`.

### El maestro no se confirma a sÃ­ mismo
- Se guarda lo que enseÃ±an LOS DOS â€”volver a preguntarle algo que ya acertÃ³ cuesta tiempo y
  dineroâ€” pero se recuerda de quiÃ©n vino: solo lo humano se le devuelve como correcciÃ³n. Si le
  devolviÃ©ramos lo suyo, repetirÃ­a su criterio de ayer creyÃ©ndolo una correcciÃ³n humana y **un
  error se volverÃ­a doctrina por el mero hecho de haberse cometido una vez**.
- Fecha: 2026-08-06. CÃ³digo: `EdgeInfo.PorPersona`, `SurfaceMap.CorreccionesDe`.

### Â«Ahora mismo noÂ» no es Â«noÂ»
- Un 503, un 429 o un fallo de DNS dicen que el servicio estÃ¡ ocupado o inalcanzable, no que la
  pregunta estÃ© mal. Sin reintentar se perdÃ­a la lecciÃ³n entera por un momento malo. Los errores
  de verdad â€”clave invÃ¡lida, peticiÃ³n mal formadaâ€” no se reintentan: repetirlos no los arregla.
- Fecha: 2026-08-05/06. CÃ³digo: `MaestroDeApps.PreguntarAsync`.

---

## Almacenamiento: quÃ© vive dÃ³nde, y por quÃ© todavÃ­a no hay base de datos

### TERRENO local, APRENDIZAJE aparte
- **Terreno** (`surface-map.json`): por dÃ³nde se ha pasado en ESTA mÃ¡quina. Se lee cada 800 ms, se
  escribe en cada observaciÃ³n, y se tira sin pena â€” borrarlo es la forma de hacer una prueba
  limpia. Contiene nombres de archivos personales: jamÃ¡s se comparte.
- **Aprendizaje** (`jerarquias-ensenadas.json`): quÃ© es la navegaciÃ³n permanente de cada app. Vale
  maÃ±ana, en otro equipo y para cualquiera que use esa app. **Sobrevive a borrar el grafo** y se
  reaplica al observar.
- Es la misma frontera de la capa 2 de este documento, ya materializada en dos archivos: se
  comparten las reglas, nunca el terreno.
- Fecha: 2026-08-06, pedido por el usuario. CÃ³digo: `SurfaceMap.RutaEnsenanzas`.

### Neo4j/graphify: para el APRENDIZAJE, cuando haya un segundo usuario
- El terreno debe seguir local: meterle red a algo que se lee cada 800 ms compra latencia y fallos
  por un problema que no existe â€”el JSON nunca ha sido el cuello de botellaâ€”. Los fallos de esta
  semana fueron todos SEMÃNTICOS, y una base de datos no arregla semÃ¡ntica.
- Lo que merece base compartida es `jerarquias-ensenadas.json`: pequeÃ±o, por-app, y es exactamente
  Â«enseÃ±a una app una vez y sirve para todosÂ». Como ya estÃ¡ separado, migrar es cambiar el
  transporte de un archivo, no rediseÃ±ar nada.
- Mismo criterio que ya regÃ­a para la capa 2: **no construir el almacÃ©n antes que el productor.**
- Fecha: 2026-08-07, preguntado por el usuario.

---

## CÃ³mo se depura esto

### El paso a paso vale mÃ¡s que el log cuando el fallo estÃ¡ entre lo que crees y lo que pasa
- El log cuenta lo que el sistema CREE que hizo. Se numeraron los puntos de una app mientras se
  abrÃ­a otra, y en el log las dos lÃ­neas se leÃ­an perfectamente correctas por separado.
- El botÃ³n de paso a paso detiene el mapeo en cada paso, enseÃ±a los datos de ese paso y deja
  escribir lo observado AHÃ, junto al paso al que pertenece. Un Â«esto no se actualizÃ³Â» dicho media
  hora despuÃ©s ya no se puede situar; dicho en el momento, sÃ­. EncontrÃ³ tres fallos el primer dÃ­a.
- Fecha: 2026-08-06, pedido por el usuario. CÃ³digo: `Ui/PasoAPaso`.

### Cuando dos hipÃ³tesis compiten, se instrumenta antes de tocar
- Dos arreglos seguidos por el sitio equivocado suponiendo dÃ³nde estaba el fallo. El tercero
  habrÃ­a sido otro parche sobre la misma suposiciÃ³n.
- El log de `grafo:` dice quiÃ©n es la RAÃZ, cuÃ¡ntos nodos llegan DECLARADOS y a quÃ© profundidad
  acabÃ³ cada uno. Las hipÃ³tesis vivas â€”se pierde al cruzar, o se pierde al dibujarâ€” dejan huellas
  distintas, y una prueba las separa.
- Regla: si un arreglo mueve el fallo de sitio en vez de eliminarlo, la causa estÃ¡ una capa mÃ¡s
  abajo. Deja de parchear y mide.
- Fecha: 2026-08-06/07.

### Toda prueba del mapa empieza con el grafo a cero
- Un grafo con historia esconde justo lo que se quiere medir: si las puertas ya estÃ¡n cruzadas, no
  se ve si el sistema sabe llegar a un sitio nuevo. El botÃ³n de escoba borra el terreno; el de
  birrete olvida el aprendizaje, por aplicaciÃ³n â€”enseÃ±ar bien el explorador no es motivo para
  perder lo que se aprendiÃ³ del navegadorâ€”.
- Ojo: borrar no deja el grafo vacÃ­o mucho rato, porque se arma solo de lo visible. Lo que
  estorbaba en las pruebas era el COLOR, no los nodos.
- Fecha: 2026-08-05/06. CÃ³digo: `SurfaceMap.OlvidarTodo`, `OlvidarJerarquiaDe`, `Ui/OlvidarJerarquias`.


---

## El núcleo está CONGELADO (2026-08-08)

### El contrato ejecutable manda sobre la memoria
- El día que el grafo alcanzó su punto estable, sus promesas se escribieron como pruebas que
  llaman al código real: `tests\ContratoDelGrafo\Contrato.cs`. Nueve invariantes, cada uno pagado
  con una prueba manual del usuario y un diagnóstico: el dwell y el paso de largo, la enseñanza
  que sobrevive al borrado, el nivel fijado que no se mueve, el atrás efímero, dos puertas dos
  aristas, el robo de foco, el cromo como propiedad, la persistencia y las rutas sin huecos.
- Se corre con `scripts\contrato-del-grafo.ps1` ANTES de dar por bueno cualquier cambio al núcleo.
  El primer día ya pagó su existencia: encontró el último sitio donde cromo y nivel 1 seguían
  siendo sinónimos (`CromoDe` filtraba por `NivelNav != 1` en vez de por `EsCromo`).
- Si una prueba estorba para un cambio, la conversación es sobre el CONTRATO, no sobre la prueba:
  cambiarla es cambiar lo que el grafo le promete a todo lo que se construye encima.

### Editar el núcleo exige la contraseña del dueño
- `SurfaceMap.cs`, el contrato y el propio guardián están tras un hook de Claude Code
  (`~\.claude\hooks\guardia-nucleo.ps1`): editar cualquiera abre un popup que pide una contraseña
  que solo el usuario conoce (queda el hash SHA-256, nunca el texto). Sin ella, la edición se
  bloquea y el agente recibe el porqué.
- No es criptografía contra atacantes: es fricción deliberada para que tocar la base sea siempre
  una decisión humana consciente, nunca un paso intermedio de otra tarea. Habrá cambios futuros
  —el candado tiene llave— pero serán pocos y elegidos.
- Fecha: 2026-08-08, pedido por el usuario.

### Las superficies se conectan por ADAPTADORES; el núcleo no conoce ninguna
- El núcleo habla exactamente tres verbos: «estoy aquí» (`Observe`), «veo estas puertas»
  (`ObserveExits`) y «crucé por esta» (`LearnTraversal`). Todo lo demás —niveles, cromo, rutas,
  enseñanza, persistencia— se deriva de esos tres. En `SurfaceMap` no hay un solo `if` por tipo
  de superficie más allá del esquema del identificador, y así debe seguir.
- Lo específico de cada superficie vive en su ADAPTADOR, fuera del núcleo: quién soy
  (`SurfaceLocator` produce `uia://`, `web://`, `sapgui://`), qué se ve (el lector de UIA, la
  omnibox del navegador, el scripting de SAP), cómo se acciona, y qué gesto es «volver». Ejemplos
  ya vivos: `SapGuiSurface`, `PestanasAbiertas`, la resolución UWP de `AppAligner`.
- Soportar una superficie nueva = escribir su adaptador + enseñarle jerarquías. CERO ediciones al
  núcleo. Si un adaptador «necesita» tocar el núcleo, lo que de verdad pasa es que el núcleo tiene
  una promesa mal contada — y eso se discute con el contrato delante.
- Que dos páginas web usen el grafo distinto no es código: son DATOS. Las diferencias por sitio
  viven en `jerarquias-ensenadas.json` (qué es cromo aquí, qué es atrás allá), nunca en ramas
  `if (dominio == …)`. El mismo principio de las dos capas de graphify: se comparten reglas,
  nunca terreno.
- Fecha: 2026-08-08, diseñado al congelar el núcleo.

---

## Versiones del núcleo y el CI (2026-08-08)

### El núcleo se versiona: romper no cuesta nada
- El candado hace que editar el núcleo sea una decisión consciente, pero una decisión consciente
  también rompe cosas. La salida no es prohibir más: es que volver atrás cueste UN clic. La v0 es
  el núcleo congelado original; se trabaja sobre la versión que el dueño elija, y la tira
  IZQUIERDA del explorador salta entre versiones precompiladas al instante (ámbar = corriendo,
  lápiz azul = en edición).
- El salto NO recompila: arranca el binario de esa versión (`C:\U-versiones\vN\bin`) heredando el
  entorno entero —mismo terreno, mismas enseñanzas— y apaga el actual. Se descartó el intercambio
  en caliente a propósito: estado estático y eventos WPF lo hacen frágil, y un sistema de
  seguridad no se construye sobre algo frágil.
- El agente de código SOLO puede editar la versión en edición, porque el archivo de trabajo
  (`src\Navigation\SurfaceMap.cs`) ES esa versión por construcción; las instantáneas
  (`versiones\nucleo\vN.cs`) y el registro los veta el guardián sin popup siquiera. El popup de
  contraseña dice qué versión se está editando, para que la autorización sea informada.
- Comandos: `version-nucleo.ps1 -Crear` / `-Editar N` / `-Construir`. Construir refresca la
  instantánea («la instantánea es lo construido») y corre el contrato antes de dar la build por
  buena. La v0 no se edita jamás: de ella solo se sale con `-Crear`.
- Fecha: 2026-08-08, pedido por el usuario. Código: `Navigation/NucleoVersiones`,
  `scripts/version-nucleo.ps1`, tira en `GraphExplorerWindow.DibujarVersiones`.

### El CI tiene dos mitades porque las pruebas tienen dos naturalezas
- CI = integración continua: cada cambio se construye y se prueba SOLO, sin que nadie se acuerde.
  Aquí en dos niveles, porque nuestras pruebas son de dos especies distintas:
  · Las PROMESAS del núcleo (el contrato) no tocan pantalla → corren en la nube en cada push
    (`.github/workflows/contrato.yml`, un Windows alquilado de GitHub). Rojo en GitHub = una
    promesa rota antes de que nadie construya encima.
  · El RESULTADO sobre el terreno real (abrir una app, recorrerla, el maestro) necesita ESTE PC,
    su escritorio y sus apps → corre local con `scripts/ci-local.ps1`, contra la versión que se
    pida (`-Version 0` responde «¿lo nuevo rompió lo viejo?»).
- Los escenarios los graba el paso a paso: la casilla «al terminar, guardar esta prueba para CI»
  congela lo logrado en un mapeo como MÍNIMO exigible (el 80%, contra el ruido del maestro y del
  terreno — exigir igualdad exacta fallaría sin que nada se hubiera roto). Viven en
  `C:\U-versiones\escenarios\`, no en el repo: son la vara de medir de ESTA máquina.
- El CI local corre en entorno limpio y aparte (`C:\U-ci\vN`): ni contamina el terreno del
  usuario ni hereda historia que esconda una regresión. Y juzga por el ARCHIVO guardado, no por
  lo que la app diga de sí misma.
- Fecha: 2026-08-08. Código: `Ui/PasoAPaso.GuardarComoCi`, `Navigation/EscenarioCi`,
  `scripts/ci-local.ps1`.

### Autorizar un archivo no es autorizar un cambio: se declara la intención
- El popup del candado decía «se va a editar SurfaceMap.cs», que nombra el archivo pero no el
  CAMBIO. Ahora el agente tiene que escribir antes en `C:\U-versiones\intencion.txt` qué se
  propone hacer y por qué; el guardián lo exige (sin declaración fresca, bloquea y explica cómo
  declararla) y lo muestra en el diálogo. La contraseña se teclea sabiendo a qué se dice que sí.
- Caduca a los 20 minutos: una declaración vieja describe otro cambio. La ventana deja que un
  mismo cambio abarque varias ediciones sin redeclarar en cada una.
- Dos fallos que solo se ven en pantalla, y por eso hay que mirarla: PowerShell 5.1 lee con la
  ANSI del sistema y el archivo es UTF-8 («añadir» salía «aÃ±adir»), y el TextBox de WinForms solo
  corta líneas con CRLF, así que un archivo con LF salía en un párrafo ilegible.
- Fecha: 2026-08-08, pedido por el usuario. Código: `~\.claude\hooks\guardia-nucleo.ps1`.

### La versión que se está ejecutando no se puede recompilar
- Su `U.exe` está bloqueado y el build muere con un MSB3027 que no explica nada. Se RECHAZA con
  un mensaje que dice qué hacer, en vez de cerrarla por las buenas: esa instancia la lanzó la app
  con SU entorno —`U_DATA_DIR`, claves, sonda— y relanzarla desde el script le daría otro terreno
  sin avisar. Un sistema que cambia en silencio los datos que miras es peor que uno que pide un
  clic — y el clic ya existe: saltar a otra versión en la tira relanza con el mismo entorno.
- El botón «+» no tiene este problema: crea un directorio nuevo, así que nunca choca con lo vivo.
- Fecha: 2026-08-08, tropezado en vivo mientras el usuario corría v1.

### Las pruebas del núcleo se corren DENTRO del núcleo que se juzga
- Qué juzgan, y solo eso: si la estructura y el comportamiento del núcleo PUESTO AHORA MISMO
  siguen siendo los que eran. No miden si el maestro acertó más ni si la app movió un botón — por
  eso los mínimos son el 80% de lo grabado y no una igualdad.
- El botón ▶ de la tira izquierda las corre en este mismo proceso, y eso no es comodidad: quien
  mapea es este binario, así que lo que quede en su mapa es exactamente lo que ESE núcleo sabe
  hacer. Lanzar otro proceso mediría otro núcleo, que es lo contrario de la pregunta.
- Cada prueba empieza con el grafo a cero, el paso a paso se apaga mientras corre (una prueba
  automática no puede depender de que alguien pulse Continuar quince veces) y se devuelve como
  estaba. Y `GuardarComoCi` se fuerza a falso: una prueba no reescribe la vara con la que se mide.
- Las versiones se borran con clic derecho y confirmación. Se niegan tres: la v0 (es el suelo al
  que se vuelve), la que corre (su exe está abierto) y la que está en edición (el archivo de
  trabajo la contiene ahora mismo).
- Fecha: 2026-08-08, pedido por el usuario. Código: `Navigation/EscenarioCi.Juzgar`,
  `GraphExplorerWindow.CorrerPruebasAsync`, `NucleoVersiones.Borrar`.

### Las capacidades nuevas se piden POR NOMBRE: así conviven núcleos de épocas distintas
- `OlvidarApp` nació en la v1, y dos cosas se rompieron a la vez contra la v0: la UI no habría
  compilado llamándolo directo, y el CONTRATO dejó de compilar de verdad (medido reconstruyendo
  v0). La regla que quedó: la UI y el contrato piden las capacidades nuevas por nombre
  (reflexión); un núcleo que no la tiene se degrada a lo que sabía hacer —y se dice en el log—,
  y la promesa correspondiente es «no aplicable», no «rota». Un núcleo viejo no promete
  capacidades que no conoce.
- Las pruebas del ▶ limpian SOLO el terreno de la app que van a probar: lo andado en las demás no
  tiene nada que ver con lo medido. Sin maestro: se mide si la estructura se arma recorriendo, no
  si el modelo vuelve a acertar — lo enseñado ya está guardado y se repone solo sobre las puertas
  que renacen.
- Antes de correr, un selector con casillas (todas marcadas) elige sobre qué apps va la corrida;
  el clic derecho sobre ▶ abre el mismo selector (desmarcado) para BORRAR pruebas guardadas, con
  confirmación. Y la v0 ni siquiera abre el diálogo de borrado de versiones: ofrecer una
  confirmación para algo que se va a negar hace perder un clic y la confianza.
- Fecha: 2026-08-08. Código: `GraphExplorerWindow.{BorrarTerrenoDe, ElegirEscenariosAsync}`,
  `SurfaceMap.OlvidarApp` (v1+), `Contrato.OlvidarPorApp`.

---

## El arquitecto: un cerebro externo que navega y contrasta (2026-08-08)

### El crawler pone las manos; el arquitecto pone el criterio
- Evaluación hecha a fondo antes de decidir: el crawler SÍ tiene oro — frontera explícita (un
  fallo pierde solo su nodo), recolocación automática, parada ante diálogos cruzados, clic por
  identidad con el mismo ejecutor de los workflows. Lo que no tiene es criterio: recorre todo por
  igual y no puede juzgar si lo construido se parece a la app real.
- Por eso el arquitecto NO lo reemplaza ni duplica: es un agente Claude (Agent SDK, Node, en
  `agente-arquitecto/`) cuyo único brazo es la sonda MCP local — las MISMAS herramientas map_* por
  las que actúa el asistente. Navega pidiendo (`map_take`), mira (`map_what_i_see`), contrasta
  (`map_hierarchy`, nueva) y deja hallazgos (`map_feedback`, nueva →
  `C:\U-versiones\feedback-arquitecto\<app>.md`).
- Sus límites no son promesas, son ausencia de herramientas: no tiene Edit, ni Bash, ni archivos.
  Solo organizar el grafo (fijar_nivel) y reportar. Y la autoridad sigue el orden del grafo: lo
  que declaró UNA PERSONA no se toca — si discrepa, lo dice en el feedback.
- `map_hierarchy` enseña la PROCEDENCIA de cada nivel (persona / maestro / deducción) a propósito:
  sin ella el agente «corregiría» lo que el dueño acaba de fijar a mano.
- Se corre: `node agente-arquitecto\arquitecto.mjs explorer.exe 40`. Requiere `claude /login`
  hecho una vez en esa máquina (la sesión del escritorio no se hereda: la autenticación del CLI
  headless es suya propia — medido, no supuesto).
- Fecha: 2026-08-08, pedido por el usuario. El maestro (Gemini, visión) y el arquitecto (Claude,
  navegación) conviven: uno mira de un vistazo, el otro camina y contrasta.
