# Plan de implementación: un clic habla, y el panel vive a la derecha

Estado: **propuesto** · Nace de la petición del 2026-09-05 · Rama: `jose/carita-clic-y-muelle`

## Diagnóstico: qué se midió

Esto no nace de un fallo sino de una petición de uso, así que lo medido es **el código que hoy
decide**, leído, no deducido:

| Qué | Medida | Fuente |
|---|---|---|
| Qué hace hoy un clic en la carita | `SingleTap = () => { PlayTick(); ToggleCollapsed(); }` — abre la barra | `FaceWindow.xaml.cs:1880` y `:1893` |
| Qué hace hoy el doble clic | `DoubleTap = StartMicByFace` — abre/cuelga la voz | `FaceWindow.xaml.cs:1881` |
| Cuánto tarda hoy un clic simple en hacer algo | **250 ms**, siempre: espera por si llega un segundo toque | `FaceGestures.cs:55` (`TapWindowMs`) |
| Cuántos elementos con nombre tiene el panel | **~35** (`DemoBtn`, `MenuPanel`, `WorkflowPick`, `BackendDot`, `TalkPanel`…) | `FaceWindow.xaml`, líneas 420-776 |
| Cuánto code-behind los usa | **4.695 líneas** | `wc -l FaceWindow.xaml.cs` |
| Sitios que cablean el gesto de la carita | **2** (la carita de la barra y la suelta), idénticos | `FaceWindow.xaml.cs:1878` y `:1891` |

**El hallazgo que decide la forma del trabajo** (2026-09-05): el panel no hay que reescribirlo para
mudarlo. Los handlers que el XAML cablea (`Click="OnDemoPuntaAPunta"`) se compilan contra **la
instancia de `FaceWindow`**, no contra el padre visual, así que `RootPanel` se puede **reparentar
vivo** a otra ventana y los ~35 `x:Name` y sus 4.695 líneas siguen funcionando sin tocarse. La
alternativa —copiar el árbol a una ventana nueva— era un refactor de la zona que el propio
`CLAUDE.md` marca como **riesgo alto de choque**, para un resultado idéntico en pantalla.

## Por qué esto va dirigido por especificación

Porque cambia **lo que el sistema promete al gesto más usado que tiene**: hoy un clic abre un panel
y mañana abre un micrófono. Se puede escribir la frase que hoy es falsa y después verdadera —que es
la prueba de si el flujo aplica— y de hecho son varias.

Y porque hay una trampa concreta que una prueba escrita después no vería: **el micrófono seguiría
tardando 250 ms** en abrir aunque el cableado fuera correcto, porque el retardo no vive en el
cableado sino en `FaceGestures`. Un botón de encender que tarda un cuarto de segundo se siente roto,
y «funciona» sería un veredicto verdadero sobre algo que nadie querría usar.

**La regla del flujo, y no tiene excepciones:** ninguna línea de producción entra antes que la
promesa que la juzga. Cada fase empieza con el contrato ROTO y termina con el contrato INTACTO.

## La especificación

| # | Promesa | Fase que la pone verde |
|---|---|---|
| 147 | sin gesto de doble toque, el toque simple no espera a nadie: el micrófono abre en el acto, y la espera de 250 ms solo existe mientras haya un segundo toque que distinguir | 1 |
| 148 | el muelle se despliega porque el cursor está encima y se pliega al irse — pero **no mientras haya algo abierto que se perdería**: una conversación en marcha o el cursor dentro de lo desplegado lo mantienen abierto | 2 |
| 149 | soltar la carita **encima** del muelle la guarda, y soltarla en cualquier otro sitio no: la caja que decide es la del muelle desplegado, y se juzga con el punto donde se soltó | 3 |
| 150 | guardada, la carita no está en ningún sitio de la pantalla y el muelle lo dice; sacada, vuelve **al punto donde se soltó**, no a un sitio por defecto | 3 |

**La que de verdad cierra el asunto es la 147.** Mientras el toque siga esperando 250 ms, todo lo
demás es cosmético: el gesto principal de la aplicación se sentiría roto aunque el panel quedara
precioso a la derecha.

### Con qué se juzga cada una

**Mapa a mano en la propia prueba**, contra reglas puras extraídas del dibujo — el mismo camino que
la promesa del aura (spec 006, `ReglaDelAura`): el contrato corre sin pantalla, así que lo que se
juzga es **la regla que el dibujo consulta**, no el dibujo. Es lo que impide que lo juzgado y lo
pintado discrepen (aprendizaje nº16).

Las reglas nuevas son dos, y las dos las usa la interfaz de verdad:

- `U.WindowsClient.Ui.ReglaDelToque.EsperaMs(bool hayDobleToque)` — la usa `FaceGestures` para su
  temporizador. Promesa 147.
- `U.WindowsClient.Ui.ReglaDelMuelle.Desplegado(...)` y `.Guarda(...)` — las usa `Muelle`.
  Promesas 148 y 149.

Lo que **no** puede juzgar el contrato: que la ventana se pinte, que el reparentado no rompa un
handler, que el hover se sienta bien. Eso es nivel 4 —la corrida a mano— y va con su log en el PR.

## Las fases

### Fase 1 — un clic abre el micrófono, y abre YA

| | |
|---|---|
| **Promesa que pone verde** | 147 |
| **Qué toca** | `src/Ui/ReglaDelToque.cs` (nuevo), `src/Ui/FaceGestures.cs`, `src/Ui/FaceWindow.xaml.cs` |
| **¿Núcleo congelado?** | no |
| **Terminado** | promesa 147 verde, 1..146 intactas |
| **Sitios con esta clase de error** | 2 cableados de gesto, contados con grep (`SingleTap|DoubleTap`) — los dos se cambian |

El doble toque se retira. A cambio el toque simple deja de esperar: `FaceGestures` pregunta la
espera a `ReglaDelToque` en vez de llevar una constante.

### Fase 2 — el panel deja de abrirse con el clic y se muda al borde derecho

| | |
|---|---|
| **Promesa que pone verde** | 148 |
| **Qué toca** | `src/Ui/Muelle.cs` (nuevo), `src/Ui/ReglaDelMuelle.cs` (nuevo), `src/Ui/FaceWindow.xaml.cs` |
| **¿Núcleo congelado?** | no |
| **Terminado** | promesa 148 verde, 1..147 intactas |

`RootPanel` se reparenta a `Muelle`, una ventana pegada al borde derecho, siempre presente. En
reposo solo asoma una pestañita; al pasar el cursor se despliega entero. `ToggleCollapsed` deja de
colgar del clic.

### Fase 3 — la carita se guarda dentro del muelle y se saca de él

| | |
|---|---|
| **Promesa que pone verde** | 149, 150 |
| **Qué toca** | `src/Ui/ReglaDelMuelle.cs`, `src/Ui/Muelle.cs`, `src/Ui/FaceGestures.cs`, `src/Ui/FaceWindow.xaml.cs` |
| **¿Núcleo congelado?** | no |
| **Terminado** | promesas 149 y 150 verdes, 1..148 intactas |

`FaceGestures` gana un gancho `Soltada` que puede quedarse el gesto antes de que `EdgeSnap` lo
mande a un borde. Soltar encima del muelle esconde la carita; arrastrar la carita pequeña del
muelle hacia fuera la devuelve al punto donde se suelte.

## Lo que NO entra

- **Rediseñar el contenido del panel.** La imagen de referencia enseña `Learn / Work / Live`; eso es
  otra decisión (qué botones existen) y no la de este trabajo (dónde vive el panel y cómo se abre).
  El panel se muda **tal cual está**.
- **Quitarle a la carita el arrastre, el lanzamiento a los bordes o el seguir al cursor.** Sigue
  flotando exactamente como hoy.
- **Tocar `mac-client/`.** Regla `solo-mac.md`: esta sesión corre en Windows.

## Hallazgos

- **2026-09-05.** Reparentar en vez de reescribir: ver el diagnóstico. Convierte un refactor de ~35
  elementos en una mudanza de dos líneas.
- **2026-09-05, antes de escribir código.** Un panel que solo se abre con el cursor encima **cerraría
  el globo de conversación mientras escribes en él**: al mover el ratón hacia el teclado el cursor
  sale del muelle y el panel se plegaría con el texto a medias. Por eso la promesa 148 no es «abierto
  si el cursor está encima» sino que incluye lo que hay abierto dentro. No estaba en la petición y
  no se habría visto hasta probarlo con las manos.

### Lo que cambió al probarlo con las manos (2026-09-05, segunda ronda)

Cuatro cosas que solo se vieron usándolo, y que confirman por qué el nivel 4 no lo sustituye nadie:

1. **Hablar por voz abría el chat.** El dueño: «se me abre un chat que es superestorboso». Tenía
   razón y el motivo es claro: quien habla está mirando su trabajo, no un globo de texto. Se quitan
   los dos sitios que lo abrían por voz (`OnMic` y `_vivo.Cambio`); el globo sigue apareciendo donde
   hay algo que LEER. **Y con ello cambia lo que alimenta la promesa 148**: lo que mantiene el
   muelle desplegado pasa a ser el globo abierto, no la voz viva — porque con la voz manteniéndolo
   abierto, el panel se quedaba desplegado toda la conversación, que es el mismo estorbo.
2. **El panel se suscribe a `Estudio`**, el manual que ya usa la ventana de la consulta: fondo
   claro, lo elevado blanco, la sombra hace la profundidad, y la barra de scroll propia
   (`PonerLaBarraDeScroll`) en vez de la de Windows. Los colores se ponen **desde código** leyendo
   `Estudio`, no como hexadecimales en el XAML: la paleta en dos sitios es exactamente la forma de
   fallo del aprendizaje nº16.
3. **La carita del panel solo existe mientras está guardada.** Vivía ahí permanente, así que se
   veían dos caras a la vez sin que nada dijera cuál era cuál — la del panel decía «Ü está guardada
   aquí» mintiendo.
4. **Learn / Work / Live**, con la distribución del diseño que dio el dueño. `Live` abre la consulta
   clínica, que hasta hoy solo tenía puerta por `--consulta`: la función más específica del producto
   no se podía alcanzar desde dentro de la aplicación.

### Un hallazgo de método, no de producto (2026-09-05)

**Dos sesiones sobre el mismo `C:\U-dev2\bin` se pisan, y el síntoma no dice que se pisan.** Al
verificar, la app viva no era la de esta rama: otra sesión —el worktree `narrar-mientras-actua`—
había recompilado encima y relanzado. Se vio porque el *stack trace* del log traía la ruta del otro
worktree; sin esa línea, la conclusión honesta habría sido «el muelle no se muestra», y el siguiente
paso, buscar un bug que no existe. Es la nota `dos-ramas-mismo-main-que-build-corre` cumpliéndose.
La salida: **cada rama compila y corre en su propio directorio** (aquí, `C:\U-muelle\bin` con su
`U_DATA_DIR`), no en el compartido.

Y el coste de no comprobarlo antes: `dev-paralelo.ps1` **cerró la app instalada del usuario**
mientras la usaba. Su lista de protegidas incluye `%LOCALAPPDATA%\U\app` y el predicado probado a
mano acierta, así que la causa no está determinada — pero un script que cierra procesos ajenos no se
corre sin mirar qué hay vivo primero.

### Tercera ronda (2026-09-05): el selector de micrófono, y lo que el borrado se llevó

Al retirar `PanelDelCollar` quedaron **dos** APIs sin puerta, no una. Se comprobó una por una contra
lo que el panel usaba, no de memoria:

| Lo que usaba el panel | Sitios que lo usan hoy |
|---|---|
| `Cambio`, `Conectado`, `EncenderAsync`, `Permanente` | consulta, carita, `LiveAudio` — cubierto |
| **`Olvidar()`** | **0** — desenlazar se quedó sin puerta |
| **`Estado`** | **0** — nadie mostraba ya «recordado, esperando (motivo)» |

Las dos vuelven en la sección **«Dispositivos enlazados»** del selector de micrófono, que es donde
les toca: al lado de la elección que esos aparatos sirven. Con ellas entran las promesas **151**
(nombrar un aparato, y que el nombre sobreviva) y **152** (listar solo lo enlazado, y que olvidar se
lleve el nombre).

Y del propio selector, cuatro arreglos que solo se ven usándolo:

- **Se centra en su botón.** Con `PlacementMode.Bottom`, WPF alinea el borde izquierdo del menú con
  el del botón: un menú ancho se desparrama y el botón queda en una punta. Va por
  `CustomPopupPlacementCallback` y no por un desplazamiento fijo porque el menú **crece y encoge**
  con lo que haya enlazado, y un número a mano se rompería en cuanto crezca.
- **Filas en rejilla, no en fila.** Los glifos de Segoe MDL2 no miden lo mismo, así que con un
  `StackPanel` horizontal cada etiqueta arrancaba en una x distinta. Con una columna de ancho fijo
  para el icono, las tres se alinean pase lo que pase con la fuente.
- **Hover.** Sin él, un menú de opciones iguales obliga a fiarse de la puntería.
- **Sitio para el dedo**: 292 px de ancho y 11 px de relleno vertical por fila.

**Lo que no se pudo verificar a mano en esta sesión**: abrir el popup con un clic sintético. Las
coordenadas calculadas no caen donde deberían —casi seguro por escalado de DPI, porque
`GetWindowRect` da píxeles físicos y WPF coloca en DIP— y forzarlo habría sido inventar una
comprobación. Queda para el nivel 4, con las manos.

### Cuarta ronda (2026-09-06): un bug de estado con meses de antigüedad

Mirando la lista de dispositivos, el dueño vio algo que no cuadraba: **aparecían aparatos para
olvidar sin haber enlazado ninguno**. No era del código nuevo. Estaba aquí:

```csharp
public static async Task<bool> EncenderAsync()
{
    Permanente = true;          // ← se marca ANTES de buscar nada
    return await ConectarAsync();
}
```

`Permanente` cargaba con **dos significados a la vez**: la *intención* («quiero que se conecte
solo», que es lo que pone `EncenderAsync` al elegir el collar en el menú) y el *hecho* («hay un
collar emparejado»). El propio comentario de `ConectarAsync` decía cuál era la intención —«un collar
que ya funcionó una vez es un collar conocido»— treinta líneas más abajo del sitio que la
contradecía. Es la misma clase de error que el aprendizaje nº2 señala en los mensajes, aplicada al
**estado**: un dato que no distingue sus causas manda la interfaz al sitio equivocado.

Se separan: `Permanente` sigue siendo la intención (y con ella el bucle de reintento, que **no se
toca** — cambiarlo sería otra conversación y afecta a la reconexión del collar en el hospital), y
nace `CollarPermanente.Enlazado`, el hecho, que se enciende en **un solo sitio**: donde consta que
un collar contestó. Promesa **153**.

Con ello, tres decisiones más:

- **El teléfono sale de la lista.** No se empareja con esta máquina: se le da un código y manda por
  la red. No había nada suyo que «olvidar» aquí. La promesa **152** se reescribió para decirlo — el
  enunciado anterior lo incluía, y cambiar una promesa es una conversación con el dueño, que es
  quien lo pidió.
- **El enlace deja de enseñar el `wss://` entero.** Lo único que una persona necesita reconocer son
  sus ocho letras de código; lo demás es fontanería y viaja igual al portapapeles.
- **Los aparatos, en tarjetas** con retrato, nombre editable y punto verde cuando están activos: un
  collar emparejado puede estar apagado, lejos o sin batería, y las tres cosas se ven igual que uno
  funcionando si nadie lo dice.

Y dos arreglos que solo se explican midiendo:

- **`Estudio.Pastilla` centraba su contenido**, así que la rejilla que alineaba icono y texto se
  encogía y se centraba entera: cada fila empezaba donde le dijera el ancho de su glifo. De ahí el
  desalineado. Ahora acepta `estirado`.
- **El menú saltaba de sitio al elegir «Teléfono»**: `Popup` solo consulta su
  `CustomPopupPlacementCallback` al abrirse, y el menú CRECE cuando aparece la caja del enlace. Se
  le da el empujón conocido para que vuelva a preguntar.

**Un fallo del arnés, corregido en el mismo sitio que lo predice.** Al sabotear la promesa 152, salió
roja por una `TargetParameterCountException` en vez de por su motivo: el juez decía «culpable» donde
debía decir que ni pudo ejecutarla. Es el aprendizaje nº17, cometido dentro del contrato que existe
para eso. La comprobación de firma pasa ahora **antes** de la llamada.

### Quinta ronda (2026-09-06): las sombras cortadas

El dueño lo vio en el muelle y en el selector, y avisó de que pasaba «en varios lugares». Lo era: la
causa es **una sola** y estaba repartida por doce sitios.

**Por qué se cortan, y por qué el síntoma engaña.** WPF no recorta a un hijo que se sale de su hueco
de layout, así que dentro de una ventana una sombra asoma sin problema. Lo que sí corta es el borde
de la **ventana**: las de esta aplicación son `SizeToContent` sobre `AllowsTransparency`, o sea que
el HWND mide exactamente lo que mide el contenido — y el desenfoque, que vive fuera de ese
contenido, se queda al otro lado del cristal. Un `Popup` es una ventana con otro nombre y le pasa lo
mismo. El síntoma no parece un recorte: parece un borde duro, o una sombra más plana de un lado.

**Los sitios, contados con grep y no de memoria: 12 en código + 4 en XAML.** Nueve de los doce pasan
por `Estudio.Elevar`, así que arreglarlo ahí los arregla todos a la vez — que es el patrón nº5 del
repo, *arreglar la clase de error*. Los otros tres ponían la sombra a mano (`BarPlaca`, `TalkPanel`)
y piden la misma cuenta explícitamente.

`Estudio.HolguraDe(sombra)` la dice una vez: el desenfoque se sale la mitad por cada lado, y por
**abajo** hace falta además la caída, porque la luz de este estudio viene de arriba y la sombra
cae. Promesa **154**, que juzga la regla **y** que `Elevar` la aplique de verdad — lo segundo importa
tanto como lo primero: sin ello la cuenta sería correcta y la interfaz seguiría cortando sombras
(aprendizaje nº16).

Ya había un sitio que lo resolvía a mano y sabía por qué: el `Margin` de 28 de la carita suelta, con
su comentario *«deja aire alrededor para que la sombra (blur 24) no se recorte»*. Esto es esa misma
cuenta, dicha en un solo sitio para todos.

Medido tras el arreglo: el panel del muelle pasó de 165×193 a 213×281. Esos 48 de ancho y 88 de alto
son exactamente el hueco que le faltaba.

## Cierre

- [ ] Todas las promesas verdes (`.\scripts\contrato-del-grafo.ps1` → CONTRATO INTACTO)
- [ ] `.\scripts\verificar.ps1` pasa, con evidencia en `out\evidencia.md`
- [ ] Probado a mano en ≥2 pantallas, con nombre, en el PR
