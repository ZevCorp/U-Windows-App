# Plan de implementación: el panel de los aprendizajes

Estado: **implementada** (198–201 y 225 verdes; contrato intacto, 178 promesas; sabotaje comprobado: las cinco en rojo, cada una por su motivo) · Nace del diagnóstico del 2026-09-11 · Rama: `jose/el-check-corre-una-skill`

> El dueño, mirando el carrusel de workflows: «ni siquiera sé bien cuál pulsar, porque el panel que
> veo sobre los workflows es supermediocre. Quiero un panel de los aprendizajes que yo le haya
> enseñado, pero de nivel user ready, como lo haría Apple, muy pulido y minimalista. Que a cada
> aprendizaje pueda darle *mostrar*, y el agente va y muestra lo que sabe hacer. Que tenga una
> descripción de lo que hace, y de pronto ver los screenshots del paso a paso, pero solo si yo
> quiero verlos. Y analiza tú con criterio propio qué debería llevar y qué no, para que no se
> vuelva un panel ruidoso.»

## Diagnóstico: qué se midió

| Qué | Medida | Fuente |
|---|---|---|
| Lo que el dueño ve hoy | un carrusel con flechas ◀ ▶ que dice «13/20 · The workflow involves navigating through the SAP Easy Access interface primarily (37 paso(s))», y un botón verde que a veces ejecuta un workflow del grafo y a veces comprueba una skill | `FaceWindow.xaml` 321-370, `FaceWindow.xaml.cs:2723` |
| Qué son esos workflows | los del Graph remoto (`GET /api/v1/workflows`, Neo4j), NO los aprendizajes locales | `Workflows/WorkflowSummary.cs` |
| Dónde viven los aprendizajes | `%LOCALAPPDATA%\U\skills\*.skill.json`, 19 archivos, ninguno visible en la interfaz salvo por el botón de comprobar | `SkillEnsenada.CarpetaPorDefecto` |
| Cuántos tienen nombre legible | 8 de 19. Los otros 11 se llaman «The user begins by clicking on the "Triage" tree item within the SAP GUI navigat…» | catálogo en disco, 2026-09-11 |
| Qué sabe una skill de sí misma | nombre, descripción, pasos (puerta, texto, llegada, dicho), huecos, si está comprobada, dónde empieza y dónde acaba | `Navigation/SkillEnsenada.cs` |
| Qué NO sabe | de qué lección salió. Sin ese vínculo no hay capturas que enseñar | mismo archivo: no hay campo |
| Dónde están las capturas | en la lección: `<lección>/cuadros/*.jpg`, y cada evento guarda `CuadroAntes` y `CuadroDespues` | `leccion_20260908_030554`, 735 cuadros |
| Cómo se pide algo desde la ventana de consulta | por un puente estático que la carita cuelga al arrancar; la consulta nace antes que la carita y no puede recibirla por constructor | `Clinical/PuenteASap.cs`, `App.xaml.cs:122` |
| Si se puede renombrar o borrar un aprendizaje | no existe ninguna de las dos | `SkillEnsenada.cs`: sin `Borrar` ni `Renombrar` |

## Por qué esto va dirigido por especificación

Tres reglas nuevas deciden si el panel se entiende, y las tres se equivocan en silencio. **Decir en
castellano lo que hace un paso** puede colar un selector delante del médico y nadie lo notaría
revisando el código. **Emparejar un paso con su captura** puede enseñar el cuadro de otro paso, que
es peor que no enseñar ninguno: una caja que miente invita a confiar en ella (aprendizaje nº8).
Y **qué pasa al pulsar Mostrar** decide entre correr algo ya repasado y repasarlo por primera vez;
confundirlo significa ejecutar en SAP una tarea que nadie ha visto andar, que es justo lo que la
promesa 127 existe para impedir.

**La regla del flujo, y no tiene excepciones:** ninguna línea de producción entra antes que la
promesa que la juzga. Cada fase empieza con el contrato ROTO y termina con el contrato INTACTO.

## El criterio: qué lleva el panel y qué no

Un panel se vuelve ruidoso por acumulación de cosas defendibles. El criterio para cortar es este:
**entra lo que hace falta para confiar en algo que va a escribir en una historia clínica.**

| Entra | Por qué |
|---|---|
| El nombre y una línea de qué hace | si no se entiende en una línea, el problema es el nombre |
| Una sola acción: **Mostrar** | verlo hacer es la única prueba que vale; «terminé» no es un veredicto |
| Los pasos en castellano | sin un selector ni una dirección de pantalla |
| Los datos que necesita | es lo que decide si la nota clínica puede usarlo, y hoy no se ve |
| Las capturas, bajo un toque | con una frase cada una, o son papel pintado |
| Renombrar y borrar | 11 de 19 se llaman «The user begins by clicking…»: sin limpiar, la lista nace sucia |

| No entra | Por qué |
|---|---|
| Contadores de pasos | «37 paso(s)» no ayuda a elegir: nadie sabe si 37 es mucho |
| Selectores y llegadas | son el idioma de la máquina |
| Insignia verde en lo que ya está listo | solo se marca lo que pide atención; lo normal no se decora |
| Los workflows del grafo | son otra cosa y mezclarlos es lo que hace ilegible el panel de hoy |
| Buscador y filtros | con veinte fichas, un buscador es un mueble |
| Costes, modelos, tiempos, registros | eso es el log, y el log tiene su ventana |
| Editar los pasos a mano | un aprendizaje se corrige enseñándolo otra vez; editarlo rompe su única garantía, que alguien lo hizo de verdad |

**Dónde vive.** Un icono de cerebro en la cabecera de la ventana de consulta, junto al micrófono, y
no un tercer segmento en el carril de «Consultas · Nota». El carril responde a «qué estoy mirando
de este paciente»; los aprendizajes no son del paciente, son del asistente. Meterlos ahí sería un
error de categoría, y se notaría como ruido aunque cada pieza estuviera bien dibujada.

## La especificación

| # | Promesa | Fase |
|---|---|---|
| 198 | un aprendizaje sabe de qué lección salió: el id viaja con él al guardarlo, lo empaquete la demostración o la comprobación, y sin lección conocida se queda vacío en vez de inventarse una | 1 |
| 199 | lo que hace un aprendizaje se lee en castellano: cada paso se dice por la puerta que toca o por el dato que escribe, y jamás por un selector ni por una dirección de pantalla | 2 |
| 200 | las capturas de un aprendizaje salen de la lección de la que nació, emparejadas por identidad: cada paso enseña el cuadro de cuando se hizo, y un paso sin cuadro propio no enseña el de otro | 3 |
| 201 | un aprendizaje se puede renombrar y borrar: renombrar conserva todo lo demás y no deja dos archivos, y borrar lo quita del catálogo | 4 |
| 225 | «Mostrar» decide solo: lo comprobado se corre con la coreografía y sin datos de nadie, lo no comprobado se comprueba, y sin manos a las que pedírselo se dice en vez de ofrecerlo | 5 |

La que cierra el asunto es la **199**: mientras los pasos no se puedan leer, el panel es una lista
de nombres y el médico sigue sin saber qué va a pasar al pulsar.

### Con qué se juzga cada una

Mapa a mano en la propia prueba: una lección con eventos y sus cuadros (198, 200), una skill de
prueba con una puerta, un dato, un selector crudo y una tecla (199), una carpeta temporal de skills
(201), y la decisión pura sobre los dos estados posibles (225). La pantalla no la juzga el contrato:
el nivel 4, a mano, con la ventana delante.

## Las fases

### Fase 1 — el aprendizaje recuerda su lección (198)

`SkillEnsenada.DeLaLeccion` (propiedad `init`, como `Huecos` y por la misma razón de compatibilidad).
La rellenan los dos sitios que empaquetan: `WorkflowTeachSession` con `_idLeccion`, y
`SkillDeLoVerificado` con `leccion.Id`. Las 19 de hoy se quedan sin vínculo y el panel lo dice.

### Fase 2 — los pasos en castellano (199)

`Navigation.LoQueHaceLaSkill.EnCastellano(skill)`, puro. Una puerta con nombre se dice tal cual; un
paso que escribe se dice por el nombre de su dato; un `key:` se dice por su tecla; y un selector
crudo no se enseña nunca, se resume por lo que hace.

### Fase 3 — las capturas (200)

`Navigation.CapturasDeLaSkill.De(skill, leccion)`, puro. Empareja por identidad (la etiqueta o el
selector del paso contra los eventos de la lección, el último si hay dos) y devuelve el `CuadroAntes`
de ese evento, que es la pantalla del momento en que se tocó. Sin evento que case, sin cuadro.

### Fase 4 — renombrar y borrar (201)

`SkillEnsenada.Renombrar(archivo, nombre)` y `SkillEnsenada.Borrar(archivo)`. Renombrar escribe con
el nombre nuevo y quita el archivo viejo: el nombre del archivo se deriva del nombre de la skill, así
que sin quitarlo quedarían dos.

### Fase 5 — el puente y la pantalla (225)

`Navigation.LoQuePasaAlMostrar.Decidir(skill, hayManos)`, puro, y `Clinical.PuenteDeAprendizajes`
con el mismo patrón que `PuenteASap`. La carita lo cuelga al arrancar; la consulta lo llama. Encima,
el panel en `ConsultaWindow`: icono de cerebro en la cabecera, lista, ficha y capturas, con los
tokens de `Estudio`.

## Primera tanda: lo que se midió al implementar (2026-09-11)

**El sabotaje, y lo que enseñó.** Con las cinco piezas rotas a propósito —la skill sin recordar su
lección; el selector colándose en la frase; las capturas emparejadas por POSICIÓN; renombrar sin
quitar el archivo viejo; «Mostrar» corriendo siempre— el contrato se puso rojo en las cinco
promesas, cada una por su propio motivo. El veredicto dijo «7 promesa(s) incumplida(s)» porque el
arnés cuenta ASERCIONES falladas y la 200 falló tres veces; las promesas rojas distintas fueron
exactamente 198–202 y ninguna anterior se movió.

**Y el sabotaje encontró una prueba que no probaba.** La 200, tal como se escribió primero, montaba
una lección de tres eventos que aterrizaban los tres: los pasos de la skill y los eventos quedaban
en el mismo orden, así que emparejar por identidad y emparejar por posición daban EL MISMO
resultado. La prueba habría pasado con la regla equivocada dentro. Se reforzó metiendo un evento que
NO aterriza —y que por tanto no entra en la skill (promesa 176)—, que es justo lo que corre las
posiciones y lo que pasa en cualquier comprobación real. Es el paso 5 del ciclo haciendo su trabajo:
no vale haber visto una promesa solo en verde.

**Un detalle de implementación que costó una compilación.** `System.Windows.Shapes.Path` (el dibujo
del cerebro) y `System.IO.Path` (las rutas de los cuadros) chocan en el mismo archivo. Se dejó el de
rutas sin cualificar, que aparece cuatro veces, y se cualificaron los tres usos de las formas.

**Lo que se vio en la máquina** (2026-09-11, 09:55, con la app de desarrollo levantada por
`dev-paralelo.ps1`). El panel pinta con los datos de verdad: «Aprendizajes · 2 listos», las dos
fichas con su nombre, su línea y su chevron, el icono del cerebro en la cabecera junto al micrófono,
y las diecisiete sin repasar plegadas abajo. La captura se tomó por la propia puerta MCP de la app
(`map_shot`), trayendo la ventana al frente desde PowerShell.

**Y la corrida a mano encontró un fallo que el contrato no puede ver.** Con el panel abierto, el
carril de arriba seguía pintando «Nota» como pestaña activa: la interfaz decía que estabas en un
sitio distinto del que estabas. Es la misma avería de categoría que esta spec dice evitar, colándose
por la puerta de atrás. Arreglado: con el panel abierto no se marca ninguna de las dos. Ninguna
promesa lo juzgaba —la pantalla no la toca el contrato— y por eso el nivel 4 no es opcional.

**Dos cosas que la lista deja ver de los datos de hoy:** los dos aprendizajes listos nacieron antes
de la promesa 198, así que ninguno ofrece capturas todavía; y uno de los dos se llama «The user
begins the workflow by navigating to the "Triage" section within the ap», que es justo lo que el
renombrar viene a resolver.

**Lo que queda para la máquina con SAP delante** (nivel 4, con la ventana abierta):

1. ~~Abrir la consulta, pulsar el cerebro y ver la lista~~ — **hecho**, ver arriba.
2. Abrir una ficha y leer el paso a paso: ninguna línea puede enseñar un selector.
3. Pulsar «Mostrar» sobre una sin repasar: tiene que repasar SU lección, no la última grabada.
4. Renombrar una de las once que se llaman «The user begins by clicking…» y comprobar que no quedan
   dos.
5. Enseñar algo nuevo y comprobarlo: esa skill ya nace con el id de su lección, y su ficha tiene que
   ofrecer «Ver capturas del paso a paso».

**Lo que se dejó fuera a propósito, y no es un olvido:** el «Enseñarle algo nuevo» del panel. Enseñar
se dispara desde la carita (🎓) y darle un botón aquí pedía un puente que ninguna promesa de esta
spec juzga. Un control que no hace nada es peor que su ausencia.

## Lo que NO entra

- Retirar el carrusel de workflows del grafo de la carita: es otro camino, con sus promesas, y
  apagarlo entra en su propia rama.
- Editar pasos, huecos o significados a mano.
- Buscador, etiquetas, carpetas, orden manual.
- Ver el vídeo de la demo: hay `demo.mp4` por lección y su ventana ya existe (`VideoLibraryWindow`).
- Capturas para las 19 skills de hoy: nacieron sin vínculo y no se inventa uno por fecha.
