# windows-graph — SAP GUI Scripting: hechos duros

Este módulo habla con SAP por COM. Lo de abajo está **comprobado contra el SAP real** (QAS, IS-H,
Hospital General de Medellín) salvo donde se diga lo contrario. Varios puntos contradicen suposiciones
que estuvieron escritas en el propio código, así que conviene leerlos antes de "arreglar" algo aquí.

Investigación de base: [`INVESTIGACION-SAPGUI-UIA.md`](INVESTIGACION-SAPGUI-UIA.md).

## Reglas de la casa

- **Enlace tardío siempre.** Nada de referenciar `sapfewse.ocx`: así compila en máquinas sin SAP GUI
  (CI, portátil de dev) y sobrevive a los cambios de versión (el interop se rompió entre 7.40 → 7.70 →
  8.0). Entrada por ProgID `SapROTWr.SapROTWrapper`.
- **El scripting depende del cliente.** Requiere `sapgui/user_scripting = TRUE` (RZ11; por defecto es
  FALSE) y que SAP GUI local lo permita. Por eso `Check()` distingue los modos de fallo en vez de
  devolver un booleano: "no funciona" es inútil, "tu Basis no lo habilitó" es accionable.
- **`try/catch` que se traga el motivo está prohibido de facto.** Reportar el paso que falló. Un catch
  mudo hizo pasar un bug de aridad por "la API no existe" durante quién sabe cuánto.

## `session.FindById` resuelve rutas RELATIVAS a la sesión

Los ids que devuelve la API son **absolutos**:

```
/app/con[0]/ses[0]/wnd[0]/usr/cntlIMAGE_CONTAINER/shellcont/shell/shellcont[0]/shell
```

Pero `GuiSession.FindById` espera la parte a partir de `wnd[0]`. Pasarle el absoluto devuelve `null`.

**Usar siempre `SapSelector.Normalize(id)` antes de `FindById`.** Este fue un bug real: `SelectedTreeNode`
pasaba el id crudo, obtenía `null` y salía **sin probar un solo getter** — mientras el log culpaba a los
getters de selección de algo que nunca intentaron.

Los índices `con[N]/ses[M]` identifican la conexión y sesión del momento de grabar, así que además no
sirven para persistir: por eso los selectores se guardan como `sap:wnd[0]/...`.

## Árboles (`GuiTree`)

### Una fila no tiene id propio

El `id` de una fila **es el del árbol**. Por eso el selector lleva un fragmento:

```
sap:wnd[0]/shellcont/shellcont/shell/shellcont[0]/shell#node=vw00073
```

Se acciona con `doubleClickNode(key)` / `selectNode(key)`, que hacen el clic real con su round-trip **sin
depender de píxeles ni del scroll**. Además se guarda `nodePath` (`GetNodePathByKey`, p.ej. `1\2`) como
ancla estable: en un árbol clínico "Órdenes Clínicas" aparece 17 veces, así que el texto no identifica
nada y la clave puede cambiar entre sesiones.

### La selección de un árbol de COLUMNAS vive en `selectedItemNode`

SAP Easy Access usa un árbol de columnas. Al clicar una fila se selecciona un **ITEM de columna**, no un
nodo, así que `selectedNode` queda vacío *por diseño*. La clave del nodo dueño está en
**`selectedItemNode`**.

Orden que funciona en `TrySelectedNodeKey`:

1. `GetSelectedNodes()` → colección (multi-selección)
2. **`selectedItemNode`** ← el que faltaba y resuelve este SAP
3. `selectedNode`, `SelectedNode`, `GetSelectedNode`

**`topNode` NO es la selección.** Es la primera fila *visible* (posición del scroll). Tomarlo como
selección hacía que cada rueda de ratón pareciera un clic y la grabación inventaba pasos.

### La geometría por fila SÍ existe: los getters piden `(clave, columna)`

El código afirmaba que `GetItemLeft/Top/Width/Height` y `GetNodeHeight` "devuelven 0 para cualquier
clave". **Falso, al menos para `GetItemTop` y `GetItemHeight`:** toman **DOS** argumentos —clave y
columna— igual que `GetItemText(key, col)`, que sí se usaba y funcionaba. Con un argumento fallan por
**aridad**, indistinguible de "no existe".

Verificado: alto 30 px, y el `top` es la **posición real del nodo** — cuatro clics contrastados, los
cuatro dentro de la banda que predice `bordeDelÁrbol + top`.

> No comprobados todavía: `GetItemLeft`, `GetItemWidth`, `GetNodeHeight`. Puede que la afirmación
> original sea cierta para esos.

Esto permite la regla única de `VisibleTreeRows`:

> **Si el `top` que da SAP cae dentro del alto del árbol, la fila se ve; su caja es ese top con ese alto.**

Sin `topNode`, sin recorrido en profundidad, sin calibrar. No hay orden que acertar ni nada que estimar,
por eso no hay nada que pueda desalinearse. Se adapta solo a DPI, zoom de SAP y tema de fuente.

**Matiz visual:** la banda contiene la fila, pero el texto se dibuja en su **mitad superior**. Un
recuadro que ocupa la banda entera se ve bajo. Se sube un 23 % del alto (fracción, no píxeles, para que
escale con el DPI). *Contención no es alineación* — confundirlas ya costó una ronda.

### Contar filas: `col.Count`, no `ElementAt` por clave

`GetAllNodeKeys().Count` es **una** llamada COM. Traer las claves una a una con `ElementAt(i)` es una
llamada **por fila** — 525 filas × 5 refrescos/s ≈ 2.600 llamadas/s, y con SAP ocupado alguna falla, el
recuento cae a 0 y el árbol parpadea entre "mapeado" y "sin mapear".

Regla: para colorear y rotular basta cuántas hay. Las claves solo se piden cuando se va a accionar.

### Posiciones repetidas = centinela

Dos filas no pueden ocupar la misma `y`. Si un mismo `top` aparece en más de dos claves, no es una
posición sino un valor centinela (típicamente 0) que SAP devuelve para lo no visible: se descarta el
grupo entero. Evita confundir "invisible" con "primera fila" sin adivinar cuál es el centinela.

## `session.Busy`

Es la señal **nativa** de carga, mejor que cualquier heurística de conteo. Dos cosas:

- **Cualquier llamada al scripting con `Busy=true` se bloquea sin retorno** (spec oficial). Los relojes
  deben *saltarse* el tick, no colgar el hilo.
- **Solo es `true` DURANTE el round-trip.** Justo después de que nosotros disparamos un clic, SAP aún no
  empezó: `Busy` es `false` y los elementos de la pantalla anterior todavía resuelven. Preguntar en ese
  instante da "listo" y se actúa sobre la pantalla vieja. Hay que exigir estabilidad (N sondeos
  consecutivos), no un instante. **Pendiente.**

## `FindByPosition` no resuelve NADA en este SAP

El hit-test nativo devuelve `null` en los árboles **y también en los botones** (comprobado el
2026-07-26 al intentar identificar el «Buscar» del dynpro). Por eso la identidad de una fila se obtiene
de la **selección**, no del píxel, y por eso el "barrido por bandas" de `feature/sap-tree-mapping`
tampoco delimitaría filas.

Respaldo que **sí** funciona: hit-test propio con la geometría por componente
(`ScreenLeft/ScreenTop/Width/Height` de `ReadVisibleElements`, la misma que el inspector usa para
dibujar y se ve encajar). Se toma el componente **más pequeño** que contiene el punto — los
contenedores también lo contienen y devolverían el panel entero.

## No existe getter de FOCO. No lo busques.

Comprobado por introspección `ITypeInfo` contra el SAP real, no por prueba y error:

- **`GuiSession`**: `FindById, SendCommand, StartTransaction, EndTransaction, GetVKeyDescription,
  SendCommandAsync, SendMenu, RunScriptControl, FindByPosition, GetIconResourceName, ClearErrorList,
  LockSessionUI, UnlockSessionUI, EnableJawsEvents, GetObjectTree`
- **`GuiFrameWindow`**: `FindById, FindByName(Ex), FindAllByName(Ex), SetFocus, Visualize,
  IsVKeyAllowed, SendVKey, MoveWindow, Iconify, Restore, Maximize, HardCopy, Close, ShowMessageBox,
  TabForward/Backward, JumpForward/Backward, DumpState, ResizeWorkingPane(Ex)`

`SetFocus` para **escribir**, ninguno para **leer**. De ahí que un botón que no cambia ningún valor de
campo sea indistinguible de «no pasó nada»: SAP no dice qué control disparó el round-trip.

Donde **sí** hay foco es dentro de un shell: `GuiGridView.GetToolbarFocusButton`.

> Sonda reutilizable: un binario mínimo con enlace tardío puro resuelve estas preguntas en minutos.
> PowerShell **no sirve** para esto — intenta cargar la typelib y muere con `TYPE_E_CANTLOADLIBRARY`.

## Los botones de una barra de ALV no son componentes

`«Crear Triage Administrativo»` **no aparece** en un recorrido de `Children`, no tiene `Id` propio y el
diff de campos no lo ve. Es un **item del control**:

```
shell subType=GridView  id=…/usr/ssubVIEW_SCREEN:SAPLN1LSTAMB:0007/cntlISH_VIEW_007/shellcont/shell
   ToolbarButtonCount=15
    · «Buscar pacientes»             PSRC
    · «Crear Triage Administrativo»  NV44
```

Se acciona con `PressToolbarButton(clave)` —sin coordenadas, como las filas— y se graba leyendo
`GetToolbarFocusButton`, **que devuelve el ÍNDICE, no la clave**. Hay que convertirlo al grabar: el
índice depende de qué botones muestre la barra y de la autorización del usuario, la clave no.

Selector: `sap:<idDelShell>#tbbtn=NV44`, hermano de `#node=`.

## La identidad de pantalla necesita el subdynpro

Dentro del Puesto de trabajo (NWP1), abrir una fila del árbol cambia el panel derecho pero **no** la
transacción, ni el programa, ni el dynpro. `Identity()` devolvía lo mismo para toda la transacción.

Consecuencia medida: los 20 pasos del formulario de paciente se sellaron igual que los clics del árbol,
el salto-adelante del player los confundió entre sí y **se saltó 19 pasos** —el llenado entero— para ir
a «Buscar», reportando 29 de 30 hechos.

El pathname lleva ahora el subdynpro del área de usuario. **Y son dos prefijos**: `sub` *y* `ssub`
(`usr/ssubVIEW_SCREEN:SAPLN1LSTAMB:0007`). Con solo `sub` se escapaba justo la pantalla que importaba.

## Vacío no es ausente

`step.NodeKey ?? SapSelector.NodeKeyOf(selector)` parecía correcto. **Graph serializa el campo ausente
como cadena vacía**, no como null, así que `??` nunca caía al respaldo: la clave quedaba en `""`, el
paso dejaba de reconocerse como fila de árbol y se iba por `Apply()` → `SetFocus()` → `return true`.
Enfocaba el árbol, no abría nada, y **reportaba éxito**.

Tres rondas de diagnóstico costó. Cualquier dato que venga de Graph merece esa lectura.

## Aceptado ≠ ejecutado

`TryInvoke` devuelve true cuando la llamada COM no lanzó. En un árbol de columnas `doubleClickNode`
**existe, no lanza y no hace nada** — y el respaldo por item solo corría si el primero *lanzaba*, así
que no se probaba nunca. Orden correcto: si el árbol expone columnas, `doubleClickItem` primero.

## Etiquetas de shells: no identifican nada

Los shells de una pantalla comparten la etiqueta genérica `«shell»` — en la pantalla real, tres a la vez
(dos árboles y un grid). **Un shell se acciona por `Id`, nunca por etiqueta.** Cualquier diagnóstico que
resuelva "por etiqueta" dará mismatch en todos ellos; es un criterio que la ejecución real no usa.

## Eventos COM

`SapComEvents` engancha los eventos de `GuiSession` sin la type library, resolviendo IID y DISPID por
introspección (`IProvideClassInfo2`, luego `ITypeInfo`).

**VERIFICADO contra el SAP real (2026-07-26)** — ya no es una apuesta:

```
enganchado Change       (dispid=1280, args=3)
enganchado StartRequest (dispid=514,  args=1)
enganchado EndRequest   (dispid=515,  args=1)
```

`ErrorMessage` **no** aparece en esta versión de SAP GUI: ni enganchado ni fallido, así que la
enumeración no encuentra un método con ese nombre. **No observamos los errores de SAP por eventos.**
El canal real de los mensajes de negocio («Documento grabado», «Rellene los campos obligatorios») es
la **barra de estado** (`wnd[0]/sbar`: `MessageType` S/E/A/W/I + `Text` + `MessageId/Number`), y se
LEE, no se escucha — `AwaitStatusbarMessage` lo hace con la carrera del `Busy` manejada (sondeos
consecutivos en calma antes de tocar COM). Es la señal con la que el ejecutor de exportaciones
distingue «SAP confirmó el guardado» de «no lanzó excepción».

`EndRequest` es la señal de "re-resuelve el árbol, los ids viejos están muertos".

### `StartRequest` es el instante más valioso de la API

Es el único momento en que la pantalla de ORIGEN sigue viva y el campo de comandos aún conserva lo
tecleado (SAP lo vacía al ejecutar). Todo lo que haya que leer "antes del viaje" se lee ahí: el código
de transacción, el botón de toolbar con foco, y el hit-test del último clic.

Con **una advertencia**: cae en el borde del round-trip. Con `Busy=true` cualquier llamada al scripting
se bloquea sin retorno y cuelga el hilo de bombeo para siempre. Preguntar `Busy` primero, y si ya
arrancó, publicar solo desde la sombra que no toca COM.

## El snapshot de campos pertenece a UNA pantalla

Comparar el snapshot estando en otra pantalla no dice «qué cambió»: dice «en qué se diferencian dos
pantallas distintas», y eso son *todos* los campos de la nueva. Así nacían ~20 pasos `input` con valor
vacío por grabación, que al reproducir escriben `""` sobre campos que podían venir precargados.

La línea base se rehace al detectar que la pantalla cambió, **sin publicar nada**. No se pierde nada
real: después de navegar, lo que el operador tecleó en la pantalla vieja ya no se puede leer — eso se
publica en `StartRequest`, antes del viaje.
