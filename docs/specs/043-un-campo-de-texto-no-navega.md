# Un campo de texto no navega

> Spec 043 · 2026-09-18 · rama `jose/un-campo-no-navega` · promesa **334** (bloque 330-339)

## De dónde sale

Tercera prueba del dueño (2026-09-18, 14:38-14:52), con 296-299 y 330-333 en `main`. Lo que ya no
pasa: ningún `map_go_to` de once segundos (9 entre 0,6 y 2,5 s), ningún Enter deshecho, y el «no» de
una puerta que no está en 0,7 s. Lo que quedó a la vista:

```
[14:47:53] mapa-mcp: → map_take exit=Search which=2 …
[14:47:55] mano: ⏱ pulsar «Search»: la mano 331 ms · esperar el cambio 1801 ms (17 sondeo(s) de «dónde») · no cambió
[14:47:56] mano: ⏱ consultar al terreno si «Search» lleva aquí: 415 ms
[14:47:56] mapa-mcp: ← (3300 ms) hice los 1 paso(s): pulsé «Search» y la pantalla no cambió.
```

Pulsar el cuadro de búsqueda cuesta **3,3-3,9 s**: 0,3 s de mano, **1,8 s esperando un cambio de
pantalla** y 0,3-0,5 s preguntándole al terreno si esa «puerta» lleva a algún sitio. Un campo de
texto no es una puerta.

## Lo que se midió antes de escribir código

Las 50 pulsaciones con reloj del log del día (las tres pruebas del dueño), por tipo de elemento:

| Tipo | Cambió de pantalla | No cambió |
|---|---|---|
| Hyperlink | 17 | 2 |
| Button | 5 | 3 |
| TabItem | 3 | 1 |
| TreeItem | 2 | 2 |
| ListItem | 2 | 1 |
| Text | 2 | 0 |
| **ComboBox** | **0** | **4** |
| **Edit** | **0** | **3** |

**Los campos de texto cambiaron de pantalla 0 veces de 7.** Son casi la mitad de los 16 clics que no
navegaron, y los únicos en los que la espera no puede acertar nunca.

## La regla

Al pulsar un `Edit` o un `ComboBox`:

1. Se espera **poco** (300 ms) en vez del presupuesto entero (1.800 ms). No cero: si la pantalla cambia
   en ese rato, manda lo que pasó y se cuenta como cualquier navegación.
2. No se consulta el terreno («¿esta puerta lleva aquí?», promesa 296) ni se repite el clic (promesa
   248): las dos son para puertas, y un campo no lo es.
3. La respuesta lo dice: es un campo, tiene el foco. Lo siguiente es escribir.

El tipo sale del terreno, y si el terreno aún no conoce el elemento (la primera vez que se ve una
pantalla), del selector (`;ct=Edit`, `;ct=ComboBox`).

**Lo que NO se toca:** los otros 9 clics que no navegaron (botones, enlaces, filas) siguen esperando
el presupuesto entero. Ahí «no cambió» es información —un «Guardar» hace su trabajo sin moverse, un
enlace puede tardar— y acortar esa espera sin medir es la spec 038, aparcada por el dueño.

## La promesa

**334.** Un campo de texto no navega: al pulsar un Edit o un ComboBox no se espera el presupuesto de un
cambio de pantalla —solo una espera corta, por si acaso—, no se consulta el terreno ni se repite el
clic, y la respuesta dice que es un campo y que tiene el foco; si aun así la pantalla cambió se cuenta
como cualquier navegación; y lo que no es un campo espera como siempre.

## Fases

| Fase | Qué | Promesa | Archivo |
|---|---|---|---|
| 0 | la promesa, en rojo | 334 | `tests/ContratoDelGrafo/Contrato.cs` |
| 1 | la regla | 334; la 44, 82, 83, 245, 248 y 296 intactas | `windows-client/src/Navigation/PulsarSegunElNucleo.cs` |

## Lo que queda fuera

- **Que el modelo deje de pulsar el campo antes de escribir.** `map_type` con `target` ya lo enfoca:
  el `map_take` previo sobra entero (y cuesta un viaje de 2 s al modelo). Es prompt: spec 039.
- **`map_go_to` devuelve la página a medio cargar** (27 elementos; un segundo después, 59) y el modelo
  gasta otra llamada en mirar. Siguiente corte.
- **Chrome no vino al frente** al arrancar la prueba («no pude abrir ni encontrar google.com», dos
  veces, con la persona en los ajustes rápidos de Windows): ~8 s hasta que el modelo abrió otra ventana.

## Lo que pasó al implementarla

Promesa escrita y vista en rojo antes que el código (`734975d`: `PENDIENTE`, 270 verdes y solo ella).
Verde con `17560d0`: contrato intacto, 271; la 44, 82, 83, 245, 248 y 296 siguen en pie.

**Sabotaje, comprobado que se aplicó** (el `diff` mostró la línea): con `esCampo = false` solo cae la
334 — «Search tardó 1215 ms de 1200», «pulsé «Search» y la pantalla no cambió.». Revertido tras commitear.

**Sitios** (patrón nº5): `EsperarACambiar` se llama en 3 —el clic, el ensayo del doble y la repetición
de la 248—. Solo el primero puede ser sobre un campo: el ensayo es para contenido de lista y la
repetición para puertas con destino, y un campo sale antes de llegar a ninguno de los dos.

### Nivel 4: dos pantallas (Google y DuckDuckGo, en Chrome)

| `map_take` | `main`, medido hoy | rama (17560d0) |
|---|---|---|
| «Search» (ComboBox), Google | 3.300 · 3.379 ms (prueba del dueño, 14:39 y 14:47) | **1.492 ms** |
| «Barra de direcciones y de búsqueda» (Edit), Google | 3.665 ms (nivel 4 de la spec 041, 13:54) | **1.907 ms** |
| «Barra de direcciones y de búsqueda» (Edit), DuckDuckGo | 3.740 ms (ídem) | **1.978 ms** |
| «About» (Button), DuckDuckGo — no es un campo | — | 4.512 ms: **espera 1.822 ms, como siempre** |
| «Discusión» (Hyperlink), Wikipedia — navega | — | 2.395 ms: cambió a los 388 ms, «Queda aprendido» |

```
[15:02:58] mano: ⏱ pulsar «Search»: la mano 485 ms · esperar el cambio 310 ms (2 sondeo(s) de «dónde») · no cambió
[15:03:00] mano: ⏱ pulsar «Barra de direcciones y de búsqueda»: la mano 665 ms · esperar el cambio 304 ms (2 sondeo(s)) · no cambió
[15:03:31] mano: ⏱ pulsar «About»: la mano 491 ms · esperar el cambio 1822 ms (9 sondeo(s) de «dónde») · no cambió
[15:04:08] mano: ⏱ pulsar «Discusión»: la mano 498 ms · esperar el cambio 388 ms (1 sondeo(s) de «dónde») · cambió
```

La respuesta nueva: «pulsé «Search»: es un campo de texto y ya tiene el foco (la pantalla no cambió,
que es lo normal). Para escribir en él, map_type.»

**Sin adornos, sobre la línea base:** la corrida «antes» de este nivel 4 **no valió** —Windows tenía
abierto el panel de ajustes rápidos, Chrome no pudo venir al frente y los cinco pasos fallaron—, así
que la columna de `main` son las medidas válidas del mismo día con el mismo código, no una corrida
gemela. Se dice porque una comparación con una línea base prestada es más débil que una medida al lado.

**Hallazgo de esa corrida fallida, sin tocar:** con el panel de ajustes rápidos de Windows abierto
(`uia://ShellHost.exe/configuración-rápida`), Ü no consigue traer Chrome al frente: `map_go_to` contesta
«no pude abrir ni encontrar google.com» en 1,4-4,5 s y todo lo demás falla detrás. Le pasó al dueño al
empezar su tercera prueba (~8 s perdidos) y a esta corrida entera.
