# Plan de implementación: no reescribir lo ya escrito, y reintentar el clic que no movió nada

Estado: **propuesto** · 2026-09-16 · Rama: `jose/escribir-una-sola-vez`

> El dueño, tras la prueba del informe y el correo: «cuando el asistente escribe, se escribe doble vez
> o más veces; lo noté cuando escribió el correo y cuando escribió el documento con el análisis». Y:
> «cuando intentó hacer clic en compose, aunque logró señalar bien el elemento, tampoco pudo».

## Diagnóstico: qué se midió

**Lo de escribir es una regresión mía, de la spec 024.** En el log de la prueba, el mismo texto se
mandó a escribir cuatro veces seguidas:

| Hora | Qué pasó |
|---|---|
| 00:09:35 | `map_type` del informe entero → «el campo aceptó el patrón pero NO se quedó con el texto → se teclea» → 29,7 s → «no pude escribir» |
| 00:10:19 | el modelo lo reintenta entero → otros 29,8 s → «no pude escribir» |
| 00:10:57 | otra vez |
| 00:11:26 | y otra |

Cada intento **sí tecleó el texto**. Lo que falló fue la comprobación, y por dos motivos distintos:

- **En Google Docs el campo no cuenta lo que tiene.** Tras teclear, `el campo dice «»`. Docs dibuja el
  documento en un lienzo y no expone su contenido por accesibilidad. Mi comprobación leyó vacío y
  concluyó «no entró», cuando lo cierto es que **no se puede saber**.
- **En el Bloc de notas el texto SÍ estaba y aun así dio fallo.** El registro lo enseña:
  `el campo dice «RESUMEN DE LA INVESTIGACIÓN: TRES GOLES…RONALDONota: no existe un ranking…»`. La
  comparación exigía encontrar el texto pedido **literal**, con sus saltos de línea, y el editor los
  normaliza: una diferencia de formato se leyó como «no escribió nada».

Las dos veces el veredicto fue «no pude escribir», el modelo lo tomó al pie de la letra y reescribió.
El resultado es el documento con el texto repetido cuatro veces, y dos minutos perdidos.

**Lo del clic de Gmail es otra cosa, y se comprobó con el botón de verdad.** Ü resolvió el elemento
correcto (`uia:name=Compose;ct=Button`, y por eso la carita se puso a su lado), lo pulsó por patrón,
la llamada devolvió éxito y la redacción no se abrió. Probando ahora mismo sobre ese mismo botón, con
Gmail ya asentado, **el mismo Invoke abre la redacción a la primera** (el campo «To recipients»
aparece). O sea: el gesto es el correcto y el elemento es el correcto; lo que falló fue el momento.
Nueve segundos antes se había activado la pestaña de Gmail, que es una aplicación pesada.

Lo caro no es que el primer clic se pierda: es que **enterarse cuesta una vuelta al modelo** —entre
cinco y diez segundos— cuando repetirlo nos costaría uno.

## Por qué va dirigido por especificación

Porque las dos cosas son el mismo vicio con dos caras: **dar por cierto lo que no se comprobó, y dar
por falso lo que no se pudo comprobar.** La primera cara ya tiene promesas (243); esta es la segunda,
y sin escribirla el próximo campo mudo vuelve a provocar cuatro copias de un informe.

## El diseño

**Después de escribir hay tres respuestas, no dos.** *Cuajó* si el campo enseña el texto; *no cuajó*
si el campo enseña otra cosa; y **no se sabe** si el campo no cuenta lo que tiene. Un campo mudo deja
de ser un fallo: se teclea una vez y se da por escrito, diciendo en el log que no se pudo comprobar.
Reescribir un informe entero es un daño mucho peor que no poder confirmarlo.

**Y la comparación mira el texto, no su formato.** Se normalizan los espacios y los saltos de línea, y
para un texto largo basta reconocer su comienzo: es la diferencia entre comprobar que se escribió y
exigir que el editor no toque una coma.

**Un clic que no movió NADA se repite una vez, aquí y no en el modelo.** Nada significa nada: ni
cambió la pantalla, ni cambió el número de cosas vivas en ella. Si algo se movió —un menú que se
abre, un panel que se expande— no se repite, porque repetirlo lo desharía. Y nunca se repite lo que no
se puede deshacer: enviar, borrar, pagar. Eso se dice y lo decide quien tiene la intención.

## La especificación

| # | Promesa | Fase |
|---|---|---|
| 247 | tras escribir hay tres respuestas y no dos: cuajó, no cuajó, o no se sabe porque el campo no cuenta lo que tiene; un campo mudo se da por escrito en vez de por fallido —que es lo que hizo que un informe se escribiera cuatro veces— y la comparación mira el texto normalizado y no su formato | 1 |
| 248 | un clic que no movió NADA se repite una vez, y solo entonces: ni cambió la pantalla ni cambió lo que hay vivo en ella, no es algo que no se pueda deshacer, y no se ha repetido ya | 1 |

### Con qué se juzga

Sin pantalla: el veredicto de escribir con cada combinación de lo que el campo decía antes y después
—vacío, con saltos, con el texto, con otra cosa, ilegible— y la regla de si se da por escrito (247);
y la decisión de repetir con sus cuatro condiciones (248).

Sobre la máquina: escribir un texto largo con saltos en el Bloc de notas y que **no** se reescriba, y
escribir en Google Docs y que no se reescriba.

### Límites dichos, no escondidos

- Un campo mudo que de verdad no recibió el texto se dará por escrito. Es el precio de no duplicar, y
  se elige a sabiendas: el modelo lo verá en la pantalla siguiente y podrá corregir.
- Repetir un clic asume que dos clics idénticos sobre algo que no se movió son inofensivos. Por eso se
  exige que nada haya cambiado y que la etiqueta no sea de las que no se deshacen.

## Las fases

### Fase 1 — las dos (247, 248)

`ComoSeEscribe.TrasEscribir` y `SeDaPorEscrito`; `UiaSurface.SetValue` con el veredicto de tres
estados; `ComoSePulsa.HayQueRepetir`; y `PulsarSegunElNucleo` repitiendo una vez.

## Lo que NO entra

- Distinguir un campo mudo que recibió el texto de uno que no: no hay forma por accesibilidad.
- Escalar a otro gesto cuando el clic no movió nada: primero se mide si repetir el mismo basta.
