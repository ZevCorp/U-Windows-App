# Plan de implementación: escribir se comprueba, y Ü decide en vez de preguntar

Estado: **propuesto** · 2026-09-15 · Rama: `jose/escribir-de-verdad`

> El dueño, con una captura del chat de Instagram donde el mensaje nunca llegó: «no entiendo por qué
> no logra escribir, soluciónalo». Y en el mismo mensaje: «quiero que sea más proactivo, que no me
> pida tanta información para completar tareas; siempre que le pido una tarea compleja me empieza a
> preguntar cosas, quiero que vaya directo a ejecutar lo que le pido».

## Diagnóstico: qué se midió

**Lo de escribir, medido sobre el campo real de Instagram con una sonda de UIA** (2026-09-15, 19:0x,
sobre la misma ventana que falló a las 18:48):

| Qué se hizo | Qué contestó el campo |
|---|---|
| Leer el valor antes | `"\n"` (vacío) |
| `ValuePattern.SetValue("sonda-de-u")` | no lanza, no falla… y el valor sigue siendo `"\n"` |
| Leer con `TextPattern` | `"\n"`: tampoco está ahí |
| `SetFocus()` y teclear «prueba» con el teclado | el valor pasa a `"prueba"` |
| Seis retrocesos | vuelve a quedar vacío |

Y en el log de las 18:48 se ve la consecuencia exacta: el campo se resuelve bien
(`✓ campo «Enviar mensaje...»`), se escribe por patrón y la herramienta contesta **«escribí «¡Ey,
TGM! …» y confirmé con Enter»** dos veces seguidas, con la caja de Instagram vacía las dos veces. La
voz llegó a decir «ya quedó enviado» de un mensaje que no existía.

La causa es conocida y no es de Instagram: **el editor de un sitio moderno es un `contenteditable`
gobernado por JavaScript**, y escribir su valor por accesibilidad no dispara los eventos de entrada
que ese JavaScript escucha, así que el framework nunca se entera. UIA acepta la orden y devuelve
éxito: nadie miente, nadie comprueba.

**Lo de preguntar:** las instrucciones ya llevan «NO PIDAS PERMISO» en mayúsculas desde hace
semanas, y aun así pregunta. Leyéndolas se ve por qué: prohíben pedir permiso, pero dejan abierta la
puerta de al lado —«si algo es ambiguo, pregunta por el DATO que te falta»— sin decir cuándo un dato
se deduce y cuándo se pregunta de verdad. Ante una tarea larga, cualquier paso tiene algún dato
opinable, así que la excepción se come la regla.

## Por qué va dirigido por especificación

Lo primero, porque «aceptado no es ejecutado» ya ha costado caro tres veces en este repo —el
`SetFocus()` que reportaba clic, el `Select()` que marcaba sin abrir, el `29/30` con diecinueve pasos
comidos— y la única defensa que ha funcionado es **verificar por consecuencia**. Lo segundo, porque
una instrucción de prompt sin promesa se reescribe sola en la siguiente sesión que toque el texto.

## El diseño

**Escribir se comprueba en el campo, y si no cuajó se teclea.** Después de escribir por patrón se
vuelve a leer el campo. Si lo escrito está ahí, listo, y esa sigue siendo la vía normal: no necesita
foco, no mueve el ratón y no interrumpe a nadie. Si el campo se quedó como estaba, se teclea de
verdad: se le da el foco al campo, se mandan los caracteres y se vuelve a comprobar. Y si ni así,
se dice que no se pudo, en vez de contestar «escribí».

**Lo que no se puede leer no se juzga.** Hay controles que no devuelven su valor, y ahí no se puede
saber si cuajó. En ese caso se deja pasar, que es exactamente lo de hoy: el arreglo solo actúa
cuando hay una prueba de que el texto no entró, nunca sobre una sospecha.

**Ü decide en vez de preguntar.** La regla nueva sustituye la puerta abierta: si falta un dato, se
elige la opción más razonable y se dice cuál se eligió al terminar, en una frase. Preguntar se
reserva para cuando elegir mal no se puede deshacer. Y una tarea larga no se pregunta por partes:
se hace entera y se cuenta al final.

## La especificación

| # | Promesa | Fase |
|---|---|---|
| 243 | escribir se comprueba en el campo: tras escribir por patrón se relee, y si el campo se quedó como estaba —el editor de Instagram, que acepta la orden y no guarda nada— se teclea de verdad y se vuelve a comprobar; lo que no se puede leer no se juzga, y si no cuajó por ninguna vía se dice, en vez de contestar «escribí» | 1 |
| 244 | Ü decide en vez de preguntar: sus instrucciones mandan elegir la opción más razonable cuando falta un dato y decir cuál se eligió, dejan preguntar solo cuando elegir mal no se puede deshacer, y prohíben trocear una tarea larga en preguntas | 1 |

### Con qué se juzga

Sin pantalla: la regla de si el texto cuajó, con el valor leído del campo —vacío, con salto de línea,
igual, distinto, ilegible— (243); y el texto de las instrucciones, como ya se juzga la 161 (244).

Sobre la máquina: escribir en el chat de Instagram y que el texto aparezca de verdad en la caja.

### Límites dichos, no escondidos

- Teclear necesita el foco un instante, como el clic físico: se devuelven el foco y el cursor.
- La comprobación cuesta una lectura por escritura. Es barata comparada con dar por escrito lo que no
  se escribió.
- SAP no cambia: allí se escribe por su API y ya tiene su propia verificación.

## Las fases

### Fase 1 — las dos (243, 244)

`ComoSeEscribe.Cuajo`, la verificación y el respaldo de teclado en `UiaSurface.SetValue`, y las
reglas nuevas en las instrucciones del delegado.

## Lo que NO entra

- Cambiar cómo escribe SAP.
- La voz que dice «no vi…» antes de tener resultado, que sigue siendo de la spec 018.
