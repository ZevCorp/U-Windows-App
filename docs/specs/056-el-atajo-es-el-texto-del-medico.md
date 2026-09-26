# El atajo es el texto del médico, no un formulario

> Spec 056 · 2026-09-26 · rama `claude/admiring-brahmagupta-fghedd` · promesas 466-467

## Qué se pidió, en sus palabras

> «me hiciste la estructura de atajos vieja; los atajos no los debes pensar que sean para rellenar.
> Dale libertad a los médicos de crearlos como quieran. Algunos los van a hacer de rellenar (muy
> pocos) y otros van a hacer más bien textos largos que digan lo que ellos escribirían, y reemplazar
> o quitar lo que no va, y ya. Normalmente son como que el paciente está bien y ellos ya reemplazan
> lo que no está correcto según lo que el paciente les dijo.»

## Diagnóstico

La spec 055 portó los atajos de la web línea a línea (promesas 455-457), y la web está pensada
alrededor del hueco: al insertar se **selecciona el primer `___` o `[texto]`**, Tab salta entre
huecos, y la ayuda del editor de Windows hablaba de huecos. Para el atajo real —un examen normal de
diez renglones— eso sobra y estorba:

- Un texto normal con un corchete de estilo («pupilas [isocóricas]») quedaba **seleccionado** tras
  insertarlo: la siguiente tecla lo borraba.
- Insertar sobre una sección que el generador dejó en relleno («No referido.») **sumaba** debajo del
  relleno: el médico tenía que borrar el relleno a mano en cada sección, que es justo la sección
  donde más se usa el atajo.
- La lista enseñaba una sola línea del atajo: con textos largos que empiezan igual («Paciente en
  buen estado general…») no se distinguen.

## Las promesas

- **466** — insertar un atajo deja el cursor al final de lo insertado y no selecciona nada, aunque
  el texto traiga corchetes o guiones: el atajo es texto del médico, no un formulario.
  `AtajosDeTexto.InsertarEnSeccion(valor, desde, hasta, texto)` devuelve una `Insercion` con
  `SelInicio == SelFin ==` fin del bloque, y el mismo texto que la web cuando la sección tiene
  contenido.
- **467** — sobre una sección vacía o que el generador dejó en relleno, el atajo la sustituye;
  sobre contenido real, se suma como en la web. El relleno es el de `InstruccionDeVoz.EsRelleno`
  (el mismo criterio que ya usa el literal por voz, promesa 452).

Lo que NO cambia: 455-457 siguen juzgando la paridad con la web (el disparo «/», la búsqueda, el
orden). Los huecos siguen existiendo para quien los escriba: Tab salta al siguiente si lo hay, y si
no, Tab es Tab.

## Fases

| Fase | Promesas | Qué toca |
|---|---|---|
| 0 | 466, 467 en rojo | `Contrato.cs` |
| 1 | 466, 467 | `Clinical/AtajosDeTexto.cs` (`InsertarEnSeccion`) |
| 2 | nivel 4 | `Ui/ConsultaWindow.Nota.cs`: inserta con `InsertarEnSeccion`, sin seleccionar; la lista enseña tres renglones y una vista previa del elegido; la ayuda deja de hablar de huecos |

## Lo que queda fuera (propuesto al dueño, sin decidir)

- **«Ajustar a esta consulta»**: tras insertar un normal, un gesto que lo contrasta con la
  transcripción por `note-adjustment` y devuelve una propuesta que solo cambia lo que el paciente
  contradijo. Es lo que el médico hace a mano después de insertar.
- **Insertar por voz** («pon mi examen normal») y **guardar una selección como atajo** desde U.
- **La web** tiene el mismo comportamiento de hueco (`EncounterNote.tsx`, `insert-text.ts`); no se
  toca en esta spec.

## Hallazgos

1. **`EsRelleno` solo mira el principio**, y así lo hace la web: «No referido. Dolor en rodilla…»
   cuenta como relleno. Para el literal por voz da igual (suma), pero para SUSTITUIR habría borrado
   lo dictado. `InsertarEnSeccion` exige que el relleno sea TODO lo que hay (una frase corta); la 467
   lleva el caso.

## Cierre

- [x] 466-467 verdes (2026-09-26). Sabotaje 3/3: volver a seleccionar como la web pone roja la 466;
      no sustituir el relleno, o juzgarlo solo por el principio, pone roja la 467. Contrato entero
      sin rojas nuevas frente a la base.
- [ ] Nivel 4: insertar un examen normal largo sobre «No referido.» y sobre texto real
