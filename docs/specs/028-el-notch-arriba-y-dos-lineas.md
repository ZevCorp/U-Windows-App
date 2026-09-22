# Plan de implementación: el notch se sube arriba y dice una sola cosa

Estado: **implementado** (fase 1, PR #73) · **fase 2 en curso** · **refinamiento visual e interacción** · 2026-09-22 · Rama: `codex/notch`

## Refinamiento de interacción y lectura (2026-09-22)

La primera versión conservaba dos jerarquías visibles. La interfaz final las reduce a una sola frase
grande y gruesa: durante una actividad se muestra el paso vivo; cuando queda libre, la misma pieza
mantiene la última tarea. La memoria interna sigue separando tarea y actividad para no perder contexto.

El ancho permanece compacto y la frase larga no se trunca con `…`: se desplaza con una marquesina
pausada dentro del cristal, manteniendo el tamaño estable. La franja superior y la caja visible forman
una única zona de intención; al cruzar hacia la pieza, el notch se mantiene y abre el campo de texto
existente con el foco listo, sin activar la voz.

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
| Qué dice | tres frases del mismo peso | **una frase grande y gruesa**: el paso vivo mientras trabaja, o la última tarea cuando queda libre |
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

**Una sola frase, con una jerarquía clara, y eso es lo que hace que se lea de un vistazo:**

- Cuando Ü está trabajando, enseña el paso vivo: lo que está pasando ahora.
- Cuando queda libre, enseña la última tarea. La memoria interna conserva tarea y paso separados,
  pero la superficie no duplica el mensaje.
- La frase usa una única escala grande y gruesa. Si no cabe, se desplaza despacio dentro del cristal;
  nunca se reemplaza por `…` ni cambia el tamaño de la pieza.

**Cómo se convierte lo dicho en tarea:** mientras la persona habla, su frase aparece como el mensaje
vivo. Cuando cierra el turno, esa frase pasa a ser la tarea interna. Nada que pedirle al modelo: la
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
| 252 | el notch enseña un solo texto con una jerarquía clara: mientras hay actividad muestra lo que está pasando, y cuando no la hay muestra la última tarea; la tarea y el paso siguen conservándose por separado para no perder contexto | 1 |
| 253 | cada estado tiene su icono y todos salen del mismo juego: la misma caja, el mismo grosor de trazo y la forma dibujada como vector; no hay dos estados con el mismo dibujo, y ninguno es una letra ni un emoji | 1 |
| 259 | el notch dice que lo pararon a mano: el paso pasa a decirlo, el icono es el de lo que se queda sin desenlace y no el del fallo ni el del éxito, y la tarea no se mueve | 2 |
| 260 | acercar el cursor al borde de arriba, centrado donde vive el notch, cae dentro de la franja que lo asoma; lejos de esa franja no cae dentro, así que el gesto no dispara con cualquier paso del ratón por arriba | 2 |

### Con qué se juzga

Sin pantalla: la regla del sitio con un área libre y una pieza más grande que ella (251); la máquina
de estados del texto único, frase a frase, incluida la que pasa a tarea al cerrar el turno (252 y
259); el juego de iconos, que devuelve un dibujo distinto por estado y ninguno es texto (253); y la
geometría de la franja del borde de arriba, con puntos dentro y fuera (260).

Sobre la máquina: abrirlo y verlo arriba al centro, con una sola frase que cambia según el estado;
pararlo con ⏹ a media tarea y ver que el texto pasa a decirlo con el aro y la raya; y acercar el
cursor al borde de arriba, en el centro, con el notch escondido, y verlo caer.

### Límites dichos, no escondidos

- La tarea es lo último que dijo la persona, literal. Si lo dijo mal dicho, así se lee: no se resume
  ni se reescribe, porque adivinar el título es peor que citarlo.
- «Hasta que se complete» no se detecta: la tarea se queda hasta que llegue otra. Saber que una tarea
  terminó es cosa del modelo, y hoy no lo dice.
- El gesto de asomar (260) es una franja pegada al borde, no todo el medio de arriba de la pantalla:
  ancha para que sea fácil de encontrar, y baja para que subir el cursor a cerrar una ventana no lo
  dispare por accidente.

## Las fases

### Fase 1 — sitio, contenido e iconos (251, 252, 253)

`ReglaDeLaBandeja.ArribaAlCentro`, `Ui.LoQueDiceElNotch`, `Ui.IconosDelNotch`, y `PanelDeAcciones`
dibujando icono y el texto único. **Implementada, PR #73; refinada el 2026-09-22.**

### Fase 2 — se detiene con memoria y asoma con el cursor (259, 260)

Ampliación pedida por el dueño el 2026-09-17, tras probar la fase 1 en vivo: quitar las etiquetas
«Tú:»/«Ü:» del globo de conversación (no lleva promesa: es un cambio de texto en pantalla, no de lo
que el sistema promete), hacer que parar la conversación a mano quede dicho en el notch en vez de
desaparecer sin más (`LoQueDiceElNotch.Detenido`, sobre el estado `Omitido` que la fase 1 ya dejó
escrito y sin usar), y añadir el gesto de acercar el cursor al borde de arriba para asomarlo sin que
haga falta que Ü esté diciendo nada (`ReglaDeLaBandeja.Asoma`). También se pidieron transiciones más
vistosas al aparecer y desaparecer, y un parpadeo suave cuando el texto cambia de verdad — eso vive
en `PanelDeAcciones` y no lleva promesa propia: es cómo se dibuja, no qué se promete.

## Lo que NO entra

- Los escenarios expandidos del notch, que el dueño quiere diseñar aparte.
