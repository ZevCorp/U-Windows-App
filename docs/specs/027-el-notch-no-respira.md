# Plan de implementación: el notch mide siempre lo mismo

Estado: **propuesto** · 2026-09-16 · Rama: `jose/el-notch-no-respira`

> El dueño: «quiero que lo compactes mucho más verticalmente, que verticalmente sea muy muy compacto,
> y que el tamaño no cambie y no tenga un tamaño muy grande en un momento y pequeño en otro, sino un
> tamaño muy compacto». Y: «revisa qué cosas del diseño hay que mejorar para que sea 100% Apple».

## Diagnóstico: qué mide hoy

| | Hoy | Consecuencia |
|---|---|---|
| Líneas que guarda | 10 | con diez líneas la pieza mide **252 unidades de alto** |
| Alto de una línea | 23 (texto de 12,5 + 3 y 3 de aire) | |
| Aire de la placa | 11 arriba y 11 abajo | |
| Alto de la ventana | `SizeToContent` | **cambia con cada línea que entra o caduca** |
| Ancho | hasta 420 de texto | cambia con lo larga que sea la frase |

Las dos cosas que molestan salen de la misma línea de código: la ventana se mide por su contenido. Una
frase larga la ensancha, una línea nueva la estira, y una que caduca la encoge. La pieza **respira**,
y una pieza que respira encima del trabajo de alguien se lee como un sobresalto, no como un sistema.

## Por qué va dirigido por especificación

Porque «que sea compacto» no se puede juzgar, y «el alto no depende de cuántas líneas haya» sí. Sin
promesa, la próxima línea que alguien añada vuelve a estirarlo y nadie se entera hasta verlo.

## El diseño

**Tres líneas, siempre.** Las tres últimas, y el hueco reservado aunque no haya ninguna: eso es lo que
hace que la pieza no cambie de tamaño nunca. Tres es lo que hace falta para leer una secuencia —lo que
pediste, lo que está pasando, lo que salió— y diez era un registro, no un aviso.

**Más apretado en vertical.** La línea baja de 23 a 18, y el aire de la placa de 11 a 7 arriba y
abajo. El alto total pasa de 252 a **68**: una cuarta parte.

**Ancho fijo.** 340, con la frase cortada en puntos si no cabe. Hoy el ancho lo decide la frase más
larga que haya pasado, que es la misma respiración por el otro eje.

**Lo demás, para que se lea como una pieza de sistema y no como un cuadro de diálogo:**

- **Ritmo vertical constante**: cada línea ocupa exactamente lo mismo y su texto va centrado en su
  franja. Hoy el alto de una línea depende de su contenido.
- **La marca, más estrecha y más ligera**: la columna de 24 baja a 18 y el punto de 7 a 6. En una pieza
  de 68 de alto, un punto de 7 pesa demasiado.
- **La sombra, más corta**: 24 de desenfoque y 4 de caída en vez de 36 y 8. Una sombra larga bajo una
  pieza pequeña la hace flotar como un cartel; una corta la apoya.
- **La escalera de opacidad, en tres peldaños** (1 · 0,55 · 0,32) en vez de diez de 0,13: con tres
  líneas, la diferencia entre la primera y la última tiene que leerse de un vistazo.
- **El radio se queda en 20**, que sobre 68 de alto es una esquina continua y no una pastilla.

## La especificación

| # | Promesa | Fase |
|---|---|---|
| 249 | el notch mide siempre lo mismo: su alto no depende de cuántas líneas tenga —ni cero, ni una, ni diez— ni su ancho de lo largas que sean; enseña tres líneas, cada una ocupa lo mismo, y la pieza entera cabe en 80 de alto | 1 |

### Con qué se juzga

Sin pantalla: las medidas son una pieza aparte y se le pregunta el alto para 0, 1, 3 y 10 líneas —tiene
que contestar lo mismo—, y que ese alto sea compacto de verdad.

Sobre la máquina: abrirlo con una línea y con cinco y comprobar que la ventana no cambia de tamaño.

### Límites dichos, no escondidos

- Con menos de tres líneas queda hueco vacío. Es el precio de que no cambie de tamaño, y es lo que se
  pidió.
- Lo que pasa de tres líneas se pierde de vista. El registro completo sigue en el log.

## Las fases

### Fase 1 — las medidas (249)

`Ui.MedidaDelNotch` con las constantes y el alto, y `PanelDeAcciones` midiéndose con ellas.

## Lo que NO entra

- La colocación (promesa 241) y la paleta (242) no se tocan.
