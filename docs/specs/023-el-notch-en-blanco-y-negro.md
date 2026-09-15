# Plan de implementación: el notch, en blanco y negro

Estado: **propuesto** · 2026-09-15 · Rama: `jose/notch-y-barra`

> El dueño, al revisar el notch antes de mandarlo a un usuario: «revisa el diseño para que sea
> extremadamente limpio, mucho negro y blanco, sin más colores que dañen la estética para el notch.
> La barra sí con el diseño que esté».

## Diagnóstico: qué pinta hoy el notch

| Qué | Con qué color | Dónde |
|---|---|---|
| El punto de una acción en curso | azul `#3B82F6`, con halo del mismo azul | `UiPalette.Trabajando` |
| El punto de una acción hecha | verde `#2FB457` | `UiPalette.Vivo` |
| El punto de una acción fallida | rojo `#FF3B30` | `UiPalette.Fallo` |
| La marca «Ü» de una frase suya | verde | `Fila.DeVoz` |
| La marca «Tú» de una frase tuya | azul | `Fila.DeVoz` |
| El texto de cada fila | blanco azulado `#EAF2FF` | `Fila` |
| El fondo de la pieza | negro con una gota de azul `#0A0C12` | el borde `_notch` |

Son cuatro tonos y un blanco que no es blanco, en una pieza de dos centímetros que vive encima de
todo lo que la persona hace. La paleta venía de la barra grande, donde el color sí distingue zonas.

## Por qué va dirigido por especificación

Porque «quítale el color» se deshace solo. Un estado nuevo pide un tono nuevo, y el que lo añada
dentro de tres semanas no va a leer esta conversación: va a ver que los estados se distinguen por
color y va a seguir el patrón. La promesa convierte la decisión en algo que se rompe solo si alguien
la contradice, y entonces se ve.

## El diseño

**Una paleta propia, y que solo sepa de grises.** El notch deja de tirar de `UiPalette`, que es la
paleta de la barra grande y tiene que seguir teniendo color. La suya expone cada valor que pinta
como ARGB, y todos cumplen la misma condición: rojo, verde y azul iguales. Negro, blanco, y grises.

**El estado se dice con la forma y con la luz, no con el tono.** Es lo que hace que quitar el color
no cueste información:

| Estado | Cómo se ve |
|---|---|
| En curso | punto blanco lleno, con halo blanco, latiendo |
| Hecho | punto blanco lleno, quieto |
| Fallo | **aro** blanco, hueco por dentro: se lee que no se cerró |
| Omitido | punto gris al 40%, y el texto apagado |

**Las dos voces se separan por luminancia.** «Ü» en blanco entero y «Tú» en blanco al 55%, en vez de
verde y azul. La etiqueta ya dice quién habla; el color solo lo repetía.

**El fondo, negro de verdad.** `#0A0C12` era negro con una gota de azul para que no se leyera como un
agujero. Con el filete blanco de 1 px que ya lleva, el negro puro se despega igual del escritorio, y
es lo que pidió el dueño.

## La especificación

| # | Promesa | Fase |
|---|---|---|
| 242 | el notch es blanco y negro: todo color que pinta tiene sus tres canales iguales, el estado se distingue por forma y por luz —el fallo es un aro y no un punto rojo, lo omitido baja de luz, lo que está en curso late— y la paleta de la barra grande, que sí tiene color, no entra aquí | 1 |

### Con qué se juzga

Sin pantalla: se recorren por reflexión todos los valores que la paleta del notch expone y se exige
que cada uno tenga R, G y B iguales; y se pregunta a la regla de la forma qué hace con cada estado.

Sobre la máquina: abrir el notch y mirarlo, que es lo único que dice si «limpio» se cumplió.

### Límites dichos, no escondidos

- **La barra grande no se toca**: sigue con su paleta y sus colores, como pidió el dueño.
- La promesa juzga que no hay tono, no que sea bonito. Lo segundo lo dice el ojo.

## Las fases

### Fase 1 — la paleta y las formas (242)

`Ui.PaletaDelNotch` con su enum `EstadoDelNotch`, y `PanelDeAcciones` pintando desde ella.

## Lo que NO entra

- Cambiar `UiPalette`, que es de la barra y de las pastillas.
