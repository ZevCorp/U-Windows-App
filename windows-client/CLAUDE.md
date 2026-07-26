# windows-client — el inspector visual

Cliente C#/WPF. Lee la UI, ejecuta ratón/teclado, dibuja. **No decide nada**: la inteligencia está en
Graph, y lo que sabe de SAP está en `windows-graph`.

Esta guía cubre el **inspector visual**, que es la herramienta de diagnóstico central y donde se
concentró el trabajo del 2026-07-26.

## Qué es el inspector

Overlay transparente, topmost y **click-through** (`WS_EX_TRANSPARENT`: los clics pasan a la app de
abajo) que enmarca todo lo que la app puede ver y accionar. Estilo TalkBack de Android.

Su valor no es decorativo: **hace visible la diferencia entre lo que el usuario tocó y lo que el
asistente resolvería.** Al hacer clic diagnostica ambos y los compara.

Recibe rects en píxeles **físicos** y los convierte a DIPs con la transform del propio HWND, así se
alinean con cualquier escalado de DPI. Cubre la pantalla primaria.

## Paleta — cada color es una afirmación

| Color | Significa |
|---|---|
| Blanco tenue | Elemento detectado por UIA |
| Cian | Campo o botón SAP leído por Scripting |
| **Verde** | Shell **mapeado**: se enumeraron sus filas por clave → accionable sin coordenadas |
| Verde fino (filas) | Una fila visible del árbol. Tono más marcado = carpeta |
| **Ámbar** | Shell **opaco** (grid, imagen, toolbar): se ve la caja, no el contenido |
| **Violeta** | Destello: clicaste lo mismo que resolvería el asistente |
| Rojo | *Mismatch*: sólido lo que tocaste, punteado lo que tocaría el asistente |

Dos decisiones que parecen cosméticas y no lo son:

- **El destello es violeta y no amarillo.** El amarillo original (`0xF2C200`) era indistinguible del
  ámbar de "sin mapear" (`0xFFA51F`). Como una fila de árbol no tenía geometría, el destello enmarcaba el
  **shell entero** durante 1,6 s: el árbol pasaba de verde a un naranja idéntico al de "no mapeado" en
  cada clic y parecía que el mapeo se caía. Si se toca la paleta, **ningún color puede parecerse a otro
  con distinto significado**.
- **Mapeado es verde y no gris.** El gris neutro original (`0xC9C9C9` a alpha `0xAA`) tampoco se
  distinguía del ámbar sobre el azul del árbol de SAP.

Los shells se pintan con los **sin mapear primero**: los shells de SAP se anidan y un ancestro sin mapear
puede tener casi el mismo rectángulo que el árbol que envuelve. El mapeado gana la superposición.

## Rendimiento: dos trampas ya pisadas

**1. Los refrescos se apilaban.** El timer disparaba `Task.Run` sin compuerta; si una lectura tardaba más
que el intervalo se acumulaban, competían por el mismo COM de SAP y el overlay se atrasaba cada vez más
— el síntoma opuesto a lo que sugiere un timer rápido. Hay un guard con `Interlocked`: el tick de en
medio se descarta.

**2. No bajar la cadencia antes de arreglar el costo por iteración.** El tick se bajó a 200 ms cuando
cada refresco costaba ~2.600 llamadas COM/s; amplificó un bug latente y disparó una cacería de una hora.
Orden correcto: primero el costo, después la cadencia.

Las filas de árbol se releen como máximo cada **900 ms** (dos llamadas COM por clave del árbol), no en
cada cuadro. El coste asumido es que un scroll rápido va un poco por detrás. La vía natural para mejorar
esto es invalidar por el evento `EndRequest` de SAP en vez de por timer.

`SapInspectorReader` se comparte entre el hilo del refresco y el del clic → el estado mutable va en
`ConcurrentDictionary` o bajo lock.

## Veredictos: juzgar con el criterio que usa la ejecución real

El diagnóstico compara "¿resolvería el asistente lo mismo?". **Ese criterio depende del tipo de
elemento**, y equivocarlo produce alarmas falsas convincentes:

- **Campos de formulario** → por etiqueta. Tienen etiquetas con significado (`RSYST-BNAME`).
- **Shells** (árbol, grid, imagen) → por **`Id`**. Su etiqueta es el relleno `«shell»`, compartido por
  todos. Juzgarlos por etiqueta daba MISMATCH en cada clic y —peor— pintaba el punteado rojo sobre
  **otro** shell, haciéndolo parecer culpable de un problema ajeno.
- **Filas de árbol** → por **clave**. El criterio real es *¿se identificó la fila?*. Rojo solo si no.

Este bug apareció **dos veces**: se arregló para árboles y media hora después reapareció idéntico en el
grid. Al arreglar algo así, revisar todos los tipos que tampoco usan ese criterio.

## Diagnóstico: describir, no concluir

`InspectorDiagnostics` escribe a `LogBus` (y a disco: `%LOCALAPPDATA%\U\logs\`).

**Un mensaje no debe afirmar una causa que no puede distinguir.** El texto *"getters de selección sin
resultado"* se emitía también cuando el árbol nunca se había resuelto — dos fallos distintos, un mensaje.
Costó dos diagnósticos equivocados. Ahora se nombra el paso: `árbol no resuelto por FindById` vs
`árbol resuelto, pero ningún getter respondió`.

`CONTRASTE geometría` es una **aserción viva**: en cada clic sobre una fila compara la `y` real del clic
contra el `top` que SAP atribuye a esa clave, y dice `CUADRA` / `NO CUADRA`. Si un DPI o una pantalla
distinta rompen el supuesto, se ve en el log en vez de manifestarse como cajas torcidas sin explicación.
No borrarlo: es barato y es lo que valida toda la geometría de filas.

Los volcados de estado (shells, filas) se emiten **una vez por cambio**, con firma, para no inundar el
log en cada tick.

## Cajas por fila

Salen de la geometría que da SAP; el detalle está en
[`../windows-graph/CLAUDE.md`](../windows-graph/CLAUDE.md). Del lado del cliente:

- La caja se recorta contra el borde inferior del árbol: la última fila suele estar medio scrolleada.
  Recortar —no descartar— es lo que el operador ve. Astillas de <3 px se omiten.
- El recuadro sube un **23 % del alto** porque el texto se dibuja en la mitad superior de la banda de
  SAP. Va como fracción para que escale con DPI/zoom/fuente, y se afina con la mediana de dónde caen los
  clics (el operador clica el texto).
- **Si no hay dato, no se dibuja.** Una caja que miente sobre qué fila señala es peor que ninguna:
  invita a confiar en ella.

## Código inerte conocido

La supresión de sub-elementos SAP contenidos en un árbol mapeado mide `0 sub-elementos` en la pantalla
real. Nació de una hipótesis falsa sobre el origen de unos recuadros estrechos. Borrar o justificar.
