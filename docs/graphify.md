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

**Capa 2 — decisiones por app, por usuario (futura; NO implementar aún).** El agente de mapeo
(mapear → diagnosticar → corregir → remapear) alimentará un registro de decisiones por app.
Frontera de diseño fijada desde ya:

- **REGLAS = conocimiento sobre la app** («el contenido del explorador vive en ventanas hijas»).
  Compartibles entre usuarios; un usuario nuevo hereda el paquete de reglas de cada app.
- **TERRENO = datos del usuario** (qué carpetas tiene, cómo se llaman). SIEMPRE privado.
  El mapa del explorador contiene nombres de archivos personales: jamás se comparte.

Se comparte el conocimiento de la app, nunca el contenido del usuario.

Cuándo implementar la capa 2: cuando exista el agente que la alimenta (mismo criterio que
Neo4j para el mapa — no construir el almacén antes que el productor).

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

## Reglas del explorador de archivos de Windows 11 (`explorer.exe`)

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
- Regla: si la misma puerta (mismo selector) aparece en 3+ pantallas distintas, es parte del
  MARCO de la app —panel lateral, barra, menú—, no de ninguna pantalla concreta. Dos
  consecuencias: (a) no se re-explora en cada nivel; (b) sirve de ATAJO para llegar a su
  destino desde CUALQUIER pantalla, sin recorrer el camino.
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
- Fecha: 2026-08-01. Código: `GraphCrawler._destinosSabidos`.

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

### Llegar a un sitio nuevo es el momento de mirar alrededor
- Síntoma: el asistente navegaba a una carpeta llena y leía «0 salidas conocidas».
- Causa: el mapa solo se llenaba durante un recorrido automático. Preguntar «dónde estoy» ahora
  registra las salidas de la pantalla actual si aún no hay ninguna RECORRIBLE (una arista
  observada pasivamente no basta: es conectividad sin acción).
- Fecha: 2026-08-02. Código: `SurfaceMapTools.ObservarAqui`.

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
