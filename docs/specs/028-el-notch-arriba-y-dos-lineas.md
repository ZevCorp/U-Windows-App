# Plan de implementación: el notch se sube arriba y dice dos cosas

Estado: **propuesto** · 2026-09-16 · Rama: `jose/el-notch-arriba`

> El dueño, con una imagen del diseño: «quiero que el notch esté arriba al centro. Donde dice "tarea
> en ejecución" quiero que esté la MACRO TAREA: si le pedí crear un anuncio, que ahí esté "crear un
> anuncio" hasta que se complete o hasta que cambie la tarea. Y donde dice "paso en ejecución", lo que
> el modelo va haciendo, y mi texto cuando yo hable. En el icono, iconos del nivel de Apple».

## Qué hay hoy y qué cambia

Hoy el notch es una lista de tres líneas apoyada en la barra de tareas: cada línea un punto y una
frase, todas del mismo peso. Sirve para ver una secuencia, y no sirve para lo que el dueño quiere
ver de un vistazo, que es **en qué está**.

| | Hoy | Ahora |
|---|---|---|
| Sitio | apoyado en la barra de tareas, en la mitad que dejan los iconos | **arriba y al centro** |
| Qué dice | tres frases del mismo peso | **la tarea** arriba y **lo que pasa** abajo |
| Cuánto dura una frase | hasta que la empujan otras dos | la tarea, hasta que pidas otra |
| La marca | un punto de 6, o «Ü» / «Tú» | **un icono** por estado |

## La promesa 241 se retira, y por qué

La 241 prometía que el notch **se apoya en la barra de tareas y ocupa la mitad que los iconos dejan
libre**. Está cumplida y bien medida —incluida la trampa del Windows 11 de fábrica, donde el valor
del registro no existe y los iconos están al centro—, pero deja de ser cierta en cuanto la pieza se
sube arriba al centro, que es lo que se pidió. Se retira con su motivo escrito y **su número no se
recicla**. Con ella se va el sensor de alineación de los iconos: en el centro de arriba no hay
iconos que esquivar, y el repo prefiere borrar la maquinaria que compensaba algo que ya no pasa
antes que dejarla sin uso (aprendizaje nº6).

Lo que NO se va: `LaBarraDeTareas` sigue diciendo cuál es el área libre, que es lo que impide que la
pieza se meta debajo de una barra puesta arriba.

## El diseño

**Arriba y al centro.** Centrado en el área libre, colgando a una distancia fija del borde superior,
y siempre dentro del cristal. Si la barra de tareas está arriba, el área libre ya la excluye y la
pieza queda justo debajo.

**Dos líneas con dos pesos distintos, y eso es lo que hace que se lea de un vistazo:**

- **La tarea**, en negrita: lo último que pidió la persona, tal cual. Se queda hasta que pida otra
  cosa. Mientras no haya pedido nada, dice «Ü».
- **Lo que pasa ahora**, más ligero y más tenue: el paso que Ü está dando, lo que acaba de salir, o
  lo que la persona está diciendo **mientras lo dice**. Es la línea que cambia.

**Cómo se convierte lo dicho en tarea:** mientras la persona habla, su frase va en la línea de
abajo, viva. Cuando cierra el turno, esa frase sube a ser la tarea. Nada que pedirle al modelo: la
tarea es, literalmente, lo que se pidió.

**Los iconos.** Uno por estado, dibujados como vector en una caja de 24 con un solo grosor de trazo:
un aro con un hueco que gira mientras trabaja, un visto cuando salió, una admiración cuando no, y
una onda cuando alguien habla. Monolínea, extremos redondeados, blanco sobre negro como manda la
paleta de la 242. Ninguno es una letra ni un emoji: un emoji trae su propio color y su propia
métrica, y se lee como un adorno pegado encima.

## La especificación

| # | Promesa | Fase |
|---|---|---|
| 251 | el notch vive arriba y al centro del área libre: se centra en el hueco que deja el sistema, cuelga a una distancia fija del borde de arriba, y nunca se sale del cristal aunque no quepa | 1 |
| 252 | el notch dice dos cosas y siempre las mismas dos: arriba LA TAREA —lo último que pidió la persona, que se queda hasta que pida otra— y abajo LO QUE PASA AHORA, que es el paso de Ü, su desenlace, o lo que la persona está diciendo mientras lo dice | 1 |
| 253 | cada estado tiene su icono y todos salen del mismo juego: la misma caja, el mismo grosor de trazo y la forma dibujada como vector; no hay dos estados con el mismo dibujo, y ninguno es una letra ni un emoji | 1 |

### Con qué se juzga

Sin pantalla: la regla del sitio con un área libre y una pieza más grande que ella (251); la máquina
de estados de las dos líneas, frase a frase, incluida la que sube a tarea al cerrar el turno (252); y
el juego de iconos, que devuelve un dibujo distinto por estado y ninguno es texto (253).

Sobre la máquina: abrirlo y verlo arriba al centro, con la tarea arriba y el paso abajo.

### Límites dichos, no escondidos

- La tarea es lo último que dijo la persona, literal. Si lo dijo mal dicho, así se lee: no se resume
  ni se reescribe, porque adivinar el título es peor que citarlo.
- «Hasta que se complete» no se detecta: la tarea se queda hasta que llegue otra. Saber que una tarea
  terminó es cosa del modelo, y hoy no lo dice.

## Las fases

### Fase 1 — sitio, contenido e iconos (251, 252, 253)

`ReglaDeLaBandeja.ArribaAlCentro`, `Ui.LoQueDiceElNotch`, `Ui.IconosDelNotch`, y `PanelDeAcciones`
dibujando icono, tarea y paso.

## Lo que NO entra

- Los escenarios expandidos del notch, que el dueño quiere diseñar aparte.
