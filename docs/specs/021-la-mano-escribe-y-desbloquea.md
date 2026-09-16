# Plan de implementación: la mano escribe y desbloquea donde se le pidió

Estado: **implementado** el 2026-09-14 (235 a 238 en verde, contrato intacto, sabotaje comprobado) · Nace de la corrida del 2026-09-14 (19:38 a 20:02) · Rama: `jose/la-ventana-de-trabajo` (continúa la spec 020, que está sin mergear: las cuatro promesas viven sobre la ventana de trabajo)

> El dueño, tras probar la voz nueva con una tarea larga: «logró hacer muchos más clics sin el
> cursor, eso me gusta muchísimo. Pero cerró dos veces Chrome, decía que estaba buscando la barra de
> algo y terminaba cerrando Chrome. Y tuvo un problema para escribir: solucionalo». Y sobre el
> cursor: «cuanto menos dependamos del cursor, mucho mejor».

## Diagnóstico: qué se midió

Todo del log de la app de desarrollo del 2026-09-14 (`C:/U-clic/local/U/logs/u-20260914.log`).

| Qué | Medida | Fuente |
|---|---|---|
| Escribir con `target` por nombre | «no pude escribir en «Git Bash»: no encontré el elemento «» (Git Bash)» y lo mismo con «Preguntar a Google» y «Términos de búsqueda»: el nombre se manda tal cual como selector, no se resuelve, y el mensaje no dice ni el nombre | log 19:39:36, 19:41:44, 20:01:57; `SurfaceMapTools.Type` usa `target` como selector y el paso no lleva `Label` |
| Escribir sin `target` en una terminal | «NO escribo: no hay ningún campo de texto abierto. El foco lo tiene «…;ct=TabItem»» en Windows Terminal y «…;ct=Window» en Git Bash (mintty): una terminal no expone ningún campo con ValuePattern, así que nunca se puede escribir en ella | log 20:00:58, 20:02:19; `Type` exige `AceptaTexto` sobre `FocusedElement`, y `SetValue` solo sabe de ValuePattern |
| Escribir sin `target` con la persona en otra ventana | se mira `AutomationElement.FocusedElement`, que es el campo de la persona, no el de la ventana de trabajo | `Type`, rama sin `target` |
| `map_unblock` cerró Chrome dos veces | el detector leyó la barra «Continúa por donde lo dejaste» (IsDialog) con opción «Cerrar»; `Unblock` construyó `uia:name=Cerrar;ct=Button` y lo resolvió en TODA la ventana: el primer «Cerrar» es el de la barra de título (rect 1863;0;58;50). Cerró la ventana, comprobó que el diálogo «ya no estaba» y dijo «DESBLOQUEADO, resuelto» | log 19:42:29, 19:43:46; `Interrupcion.Leer` devuelve nombres y tira los elementos; `Unblock` pulsa por nombre |
| El `at` de `map_unblock` | el modelo pasó el título del diálogo («Barra de información») como sitio al que volver, y la reanudación falló con ruido («no sé ponerme delante de «Barra de información»») | log 19:42:31; la descripción de `at` no dice que es una superficie |
| El cierre del navegador que sí se pidió | 19:40:40: el modelo decidió pulsar «Cerrar» (recuerdo «Cierra la ventana del navegador»); la mano lo resolvió en la ventana de trabajo, lo pulsó por Invoke y contó «la ventana en la que trabajaba ya no existe». Correcto, y se conserva | log 19:40:43 |
| Cuántos clics fueron sin cursor | 8 de 11 por Invoke; los 3 físicos fueron un campo web sin patrón («Preguntar a Google», dos veces) y una pestaña de terminal con solo selección | log 19:39 a 20:01, líneas `mano: cómo se pulsa` |

## Por qué va dirigido por especificación

Las cuatro cosas son la misma clase de error: la mano hacía algo parecido a lo que se le pidió en
un sitio parecido al que se le pidió. Escribir «en el campo Git Bash» se convertía en buscar un
selector inexistente; desbloquear «la barra de información» se convertía en pulsar el primer botón
de la ventana con ese nombre. Y el cierre correcto del navegador es la prueba de que la mano ya
hace bien lo que se le pide cuando se lo piden bien: hay que dejarlo escrito para que un arreglo de
los otros no lo rompa.

**La regla del flujo, y no tiene excepciones:** ninguna línea de producción entra antes que la
promesa que la juzga.

## El diseño

**Escribir va a la ventana de trabajo, como pulsar.** Con `target` por nombre se busca un campo de
texto con ese nombre dentro de la ventana de trabajo y se escribe por patrón Value, sin foco ni
ratón. Sin `target`, si la ventana de trabajo es la de delante vale el campo con el foco, como hoy;
si está detrás, se busca el único campo de texto visible de la ventana, y si hay varios se dice
cuáles. Y si la ventana de trabajo es una terminal (Windows Terminal, conhost, mintty, cmd,
PowerShell), donde no existe ningún campo, se teclea: se trae al frente con el enganche que ya
existe, se mandan los caracteres por teclado, se confirma con Enter, y se devuelven a la persona su
ventana y su cursor, igual que el clic físico. El error, cuando lo hay, nombra el campo y la
ventana.

**Desbloquear pulsa el botón que leyó.** El detector de interrupciones devuelve el diálogo con sus
botones enganchados, y `map_unblock` pulsa esa opción por Invoke sobre ese elemento. Nunca busca
un nombre en la ventana. Y `at` es una superficie a la que volver: si el modelo manda el título del
diálogo, se ignora sin ruido.

**La escalera del clic tiene un peldaño más antes del ratón.** Invoke y Toggle son el patrón:
se le pide al control que haga su acción, sin foco ni cursor. Lo que no lo admite y no es contenido
de lista recibe un clic por mensaje: WM_LBUTTONDOWN y WM_LBUTTONUP enviados a la ventana del
elemento en su punto pulsable, que no mueve el cursor de la persona ni necesita el foco. El ratón
real queda para el contenido de listas (donde se midió que el patrón no navega) y para lo que no
tiene punto pulsable. Se mide sobre el terreno en dos pantallas; si el mensaje no llega en alguna,
se anota aquí y no se finge.

## La especificación

| # | Promesa | Fase |
|---|---|---|
| 235 | escribir va a la ventana de trabajo: un `target` por nombre se resuelve a un campo de texto de esa ventana y se escribe por patrón, sin foco ni ratón; en una terminal, donde no hay campo, se teclea trayéndola al frente y devolviendo el foco y el cursor; y el error nombra el campo y la ventana en vez de decir «no encontré el elemento «»» | 1 |
| 236 | desbloquear pulsa el botón que leyó dentro del diálogo, no un nombre buscado en toda la ventana: el detector entrega el diálogo con sus botones enganchados, `map_unblock` pulsa esa opción y ninguna otra, la política de opciones seguras y los vetos siguen iguales, y un `at` que es el título del diálogo no dispara ninguna reanudación | 1 |
| 237 | la escalera del clic sin cursor tiene tres peldaños en este orden: el patrón (Invoke o Toggle), el clic por mensaje a la ventana del elemento en su punto pulsable, y el ratón real; el contenido de listas y lo que no tiene punto pulsable van directo al ratón real, y el patrón sigue ganando a todo cuando existe | 2 |
| 238 | cerrar la ventana de trabajo cuando el modelo lo decide sigue siendo un clic normal: «Cerrar» no es un verbo destructivo, se pulsa por patrón sin cursor, y la cuenta dice que la ventana ya no existe en vez de inventar a dónde se fue (regresión del 2026-09-14 19:40:43) | 1 |

### Con qué se juzga cada una

Sin pantalla: la decisión pura de cómo se escribe y qué es una terminal, y el mensaje de error
(235); un lector de diálogos falso con botones que registran si los pulsaron, inyectado en las
herramientas del mapa, y la mano de toda la ventana que nunca recibe nada (236); la tabla de
decisión de la escalera (237); la cadena verbo, gesto y cuenta sobre «Cerrar» (238).

Sobre la máquina: escribir «git clone …» en Git Bash y en Windows Terminal desde la voz; escribir
en «Preguntar a Google» con `target` por nombre; provocar la barra «Continúa por donde lo dejaste»
de Chrome y desbloquearla sin que Chrome se cierre; y el clic por mensaje sobre un campo web y
sobre una pestaña. Va al log, con horas.

### Límites dichos, no escondidos

- Teclear en una terminal necesita que la ventana esté delante un instante: el teclado de Windows
  va a la ventana con el foco y no hay patrón que lo sustituya. Se devuelve el foco al terminar.
- El clic por mensaje no tiene confirmación: la ventana puede ignorarlo (apps XAML que solo oyen
  entrada de puntero). Se cuenta como cualquier paso, y el ejecutor juzga por la consecuencia.
- SAP no cambia: escribe por su API (promesa 185).

## Las fases

### Fase 1 — escribir, desbloquear y la regresión (235, 236, 238)

`U.Graph.Surfaces.ComoSeEscribe` (pura), `UiaSurface.CampoDeTexto/CamposDeTexto/EsTerminal/
TeclearEnLaVentana`, `SurfaceMapTools.Type` sobre la ventana de trabajo. `Navigation.Desbloqueo.
Dialogo` con `Pulsar`, `Interrupcion.LeerDialogo`, `SurfaceMapTools.Desbloquear` con `LeerDialogo`
inyectable. Descripciones de `map_type` y `map_unblock` para el modelo.

### Fase 2 — el clic por mensaje (237)

`ComoSePulsa.Decidir` con el punto pulsable y el gesto `Mensaje`; `UiaSurface.PulsarEnLaVentana`
con el peldaño nuevo.

## Lo que se encontró al implementar

- **Lanzar volvía antes de que existiera la ventana.** «Git Bash» tardó 7 s en mostrar su ventana y
  `map_open_app` ya había contestado «está delante» con la ubicación de la persona; el `map_type`
  siguiente fue al foco de la persona (Chrome) y se negó con razón (20:22:35). Ahora, tras lanzar,
  se espera hasta 8 s a que aparezca una ventana nueva de esa app, se trae y se fija como ventana de
  trabajo; la respuesta dice «Su ventana ya está». Es un hueco de la spec 020 que esta cerró.
- **La ventana de trabajo cambia de pantalla por dentro.** Una pestaña nueva de Chrome es la misma
  ventana con otra ubicación, y si la persona tenía el foco en otra parte nadie la volvía a
  identificar: Ü seguía «en instagram.com» con la pestaña nueva delante (20:26:44). Ahora se vuelve a
  identificar por su hwnd cada vez que se pregunta dónde trabaja («la ventana de trabajo cambió por
  dentro»). También de la spec 020.
- **«No guardar» está vetado como destructivo** («contiene «guardar»»): el veto por verbo no distingue
  «guardar» de «no guardar». Se vio al intentar cerrar el Bloc de notas de prueba (20:26:37) y no se
  tocó aquí: es una regla de `SafeToClick` con su propia historia.
- **Windows Terminal aceptó el teclado pero el comando no dejó archivo** (20:23:22): la pestaña
  «Windows PowerShell (x86)» era del dueño y podía tener un proceso en primer plano leyendo la
  entrada. Por eso la medida válida se hizo en una consola nueva. Teclear en la terminal de otro es
  teclear en lo que esa terminal esté corriendo: el modelo debe preferir `instancia=nueva`.

### La corrida a mano (nivel 4), del log de `C:/U-clic`, 2026-09-14

| Hora | Qué | Resultado |
|---|---|---|
| 20:25:13 | `map_open_app` «cmd» con `instancia=nueva` | abre «Git CMD» (perfil de Windows Terminal), espera su ventana y la fija como ventana de trabajo |
| 20:25:28 | `map_type` «echo hola-desde-u > C:/U-clic/prueba-cmd.txt» sin `target` | «no hay ningún campo … es una terminal»: tecleado, 38 caracteres + Enter, foco devuelto; el archivo existe con «hola-desde-u» |
| 20:26:03 | `map_type` en Bloc de notas sin `target` | por patrón Value en «Editor de texto» (Document), como siempre |
| 20:26:14 | `map_where_am_i` con el diálogo «¿Quieres guardar…?» delante | INTERRUPCIÓN con «Guardar», «No guardar», «Cancelar» |
| 20:26:15 | `map_unblock` `at=Bloc de notas` (el título) `choose=Cancelar` | «pulsado «Cancelar» por Invoke sobre el botón del diálogo», resuelto en 297 ms, sin intentar reanudar hacia el título; Bloc de notas sigue abierto |
| 20:28:33 | `map_take` «Nueva pestaña» en Chrome con la persona en otra ventana | Invoke por patrón; «la ventana de trabajo cambió por dentro: instagram → nueva-pestaña» |
| 20:28:37 | `map_take` «Preguntar a Google» (ComboBox sin Invoke) | «Mensaje»: clic por mensaje a Chrome_WidgetWin_1 en (872,514) de cliente, sin cursor ni foco; el campo quedó con el foco (lo prueba el paso siguiente) |
| 20:28:41 | `map_type` «prueba de u» sin `target` | el campo con el foco era `aid=input` (ComboBox) en la ventana de trabajo: escrito por Value, Enter, Google buscó |
| 20:28:50 | `map_type` «prueba dos» con `target=Barra de direcciones y de búsqueda` | campo por parecido `aid=view_1012` (Edit) en la ventana de trabajo, escrito por Value |

Tres pantallas con nombre: Git CMD (Windows Terminal), Bloc de notas y Chrome. El clic por mensaje se
midió en Chrome y llegó; en una pestaña de Windows Terminal no se midió y queda anotado como
pendiente de medir.

La clase de error vivía en cuatro sitios: `Type` (target por nombre), `Type` (terminal), `Unblock`
(botón por nombre) y `ComoSePulsa` (sin peldaño entre patrón y ratón). Cuatro corregidos. Y dos más
de la spec 020 cerrados de paso: lanzar sin esperar la ventana, y la ventana de trabajo sin volver a
identificar.

## Lo que NO entra

- `LegacyIAccessible.DoDefaultAction`: el envoltorio gestionado de UIA no lo expone; iría por COM y
  es otra spec.
- Que la voz de GPT-Live deje de decir «no vi…» antes de tener el resultado: es de la spec 018 y se
  trata aparte.
