# Plan de implementación: el ✓ de la nota corre una skill enseñada

Estado: **implementada** (193–197 verdes; 113 y 114 retiradas con el dueño; contrato intacto 173/173; sabotaje comprobado: las cinco en rojo, 12 aserciones) · Nace del diagnóstico del 2026-09-10 · Rama: `jose/el-check-corre-una-skill`

> El dueño, con trece pruebas del triage comprobadas y la skill guardada: «el LLM que recibe la info
> del check debe ser capaz de elegir la skill por su inteligencia e interpretación propia, conoce
> muy bien para qué sirve la skill y la info que recibió, entonces elige la skill de su catálogo de
> skills para el caso. El camino que hay hoy al oprimir el check era una fase de prueba, puedes
> eliminarlo definitivamente, lo que importa es la ejecución de skills y sus acciones. No quiero que
> pregunte nada, simplemente si no está la info para llenar el campo entonces no se llena y listo.»

## Diagnóstico: qué se midió

| Qué | Medida | Fuente |
|---|---|---|
| Lo que hace hoy el ✓ de la nota | `Encargo.De` → `PuenteASap.Enviar` → `FaceWindow.EnviarEncargoAsync`: navegación FIJA (`LlegarAlTriageAsync`: NWP1, vista, primera fila, botón Triage), `EnvioAlTriage.PuedeEscribir`, `RellenadorSap.RellenarConNotaAsync` (emparejador del Graph), `EditoresDelTriage.Repartir` + `EscribirEditor` | `FaceWindow.xaml.cs` 5121–5245, spec 008 |
| Lo que sabe una skill comprobada | pasos verificados con su llegada real (176), huecos SOLO donde la demo narró (`HuecosDe`: sin «dicho» no hay hueco) | `SkillEnsenada.HuecosDe`, `SkillDeLoVerificado` |
| Lo que la decimotercera lección trae por campo tecleado | 17 eventos con **etiqueta** del campo («Peso», «Talla», «Temperatura», «Apertura Ocular»…) y un «dicho» que es ruido: sobre «Conducta» venía «Temperatura de 38 y mide 1.70» | `leccion_20260908_030554/leccion.json` |
| Lo que anuncia `map_skills` | nombre, description y si está comprobada; **no dice qué datos necesita** | `SurfaceMapTools.Skills` |
| Cómo corre `map_skill_run` | `InstanciarSkill.Pasos` (hueco sin dato → paso omitido, 123) y UN batch ciego: sin carita, sin frase, sin recuerdo | `SurfaceMapTools.CorrerSkill` |
| Cómo arranca el piloto | `node piloto.mjs --leccion=<carpeta>`; solo sabe comprobar lecciones | `ElPiloto.CorrerAsync`, `piloto.mjs` |
| Skills en disco | 19; comprobadas 2 (ninguna llena el formulario). La del triage entero se guardará al volver a comprobar con esta rama | `%LOCALAPPDATA%\U\skills` |

## Por qué esto va dirigido por especificación

Tres cosas se equivocan en silencio: **qué es un dato** (un hueco de más deja «nwp1» vacío y la skill
no arranca —pasó el 2026-09-03—; uno de menos escribe el «52» del paciente de prueba en la historia
de otro), **qué puede hacer el piloto con un encargo** (con manos sueltas improvisa, como el puente
consciente desde julio), y **qué cuenta al final** (un campo que quedó en blanco sin rastro parece
un envío completo). Y se retira un camino que dos promesas (113, 114) sostenían: eso se decide con
el dueño y queda escrito aquí, no en un commit.

**La regla del flujo, y no tiene excepciones:** ninguna línea de producción entra antes que la
promesa que la juzga. Cada fase empieza con el contrato ROTO y termina con el contrato INTACTO.

## La especificación

| # | Promesa | Fase que la pone verde |
|---|---|---|
| 193 | el catálogo anuncia, con cada skill, los DATOS que necesita —el nombre de cada hueco—, y `map_skills` los dice: quien elige sabe qué pedirle a la nota | 1 |
| 194 | un encargo de la nota va al piloto como mensaje: lo que la nota marcada dice y el catálogo con sus datos; su caja tiene las skills, el catálogo, llegar hasta ellas y la voz, y NO tiene manos sueltas ni preguntas: ni map_take, ni map_type, ni map_batch, ni voz_preguntar | 2 |
| 195 | al pulsar ✓ la nota no recorre un camino fijo: el encargo lo resuelve el piloto eligiendo una skill enseñada; el camino fijo del triage ya no existe, el piloto arranca en modo encargo, y las promesas 113 y 114 se retiran con el dueño | 3 |
| 196 | todo campo tecleado en una lección verificada es un HUECO con el nombre del campo —el valor de la demo es ejemplo, nunca se escribe—, salvo el campo de comandos, que es parte de la tarea; y al correr la skill el hueco sin dato queda en blanco y la cuenta lo nombra: no se pregunta, no se inventa | 4 |
| 197 | los pasos de una skill se dan de uno en uno con la coreografía del plan —la carita al lado, la frase, y solo entonces el toque— y si un paso no se puede dar, para ahí y la cuenta dice cuál, sobre el total del plan | 5 |

La que cierra el asunto es la **195**: mientras el ✓ recorra el camino fijo, todo lo demás es un
catálogo más bonito.

### Con qué se juzga cada una

Todas con **mapa a mano** en la propia prueba: skills empaquetadas y guardadas en una carpeta
temporal (193, 197), una lección con eventos tecleados y sus veredictos (196), la nota de tres
secciones que ya usa la 112 (194), y reflexión sobre los tipos que dejan de existir (195).

Lo que el contrato no puede juzgar y se mide en la máquina: que el piloto elija bien la skill
(criterio del modelo), que los datos que arma casen con los huecos, y que SAP reciba los valores.
Eso va al log del nivel 4, con horas.

### Retiradas: 113 y 114

Las dos sostenían el camino fijo de la spec 008: 113 (`EditoresDelTriage.Repartir`: motivo y
conducta por el título de la sección) y 114 (`EnvioAlTriage.PuedeEscribir`: fuera del triage no se
escribe). El dueño lo retiró el 2026-09-10 («era una fase de prueba, puedes eliminarlo
definitivamente»). Lo que la 114 protegía —no escribir en la pantalla equivocada— lo protege ahora
la skill: cada paso exige su llegada (103, 140) y un paso que no aterriza para el resto (197). Lo
que la 113 hacía —repartir la nota entre dos editores— lo hace el piloto al armar los datos de
cada hueco. **Los números no se reciclan.** 112 (solo viaja lo marcado) y `PuenteASap` se quedan:
el ✓ sigue siendo la aprobación del médico sección por sección. 115 y 116 se quedan: las usa
`EjecutorDeExportaciones`, que es otro camino (la cola del Graph) y no se tocó.

## Las fases

### Fase 1 — el catálogo dice qué datos necesita cada skill (193)

`SkillAnunciada.Huecos` (los significados), llenado por `Catalogo`; `SurfaceMapTools.AnuncioDeLasSkills`
(puro) y `Skills()` lo usa. Sin esto el piloto elegiría sabiendo qué hace la skill pero no qué
pedirle a la nota.

### Fase 2 — el encargo como mensaje, y su caja (194)

`Piloto.MensajeDelEncargo.Armar(encargo, catalogo)` → bloques (mismo tipo que la lección). Solo se
ofrecen como elegibles las comprobadas (127). `CajasDelPiloto.CajaDelEncargo` / `ProhibidasEnElEncargo`:
`map_skills`, `map_skill_run`, `map_go_to`, `map_open_app`, `map_where_am_i`, `map_what_i_see`,
`map_shot`, `voz_decir`. Sin `voz_preguntar` (no pregunta nada), sin `map_take`/`map_type`/`map_batch`
(las acciones son las de la skill), sin la lección.

### Fase 3 — el ✓ arranca el piloto en modo encargo, y el camino fijo se va (195)

`ElPiloto.Argumentos(script, carpeta, modo)`; `CorrerAsync(..., modo)`; `piloto.mjs --encargo=<carpeta>`
con su propio sistema: elige la skill por su criterio, arma `datos` con lo que la nota trae, llama
`map_skill_run`, cuenta qué escribió y qué quedó en blanco. `FaceWindow.EnviarEncargoAsync` presta la
voz (192), señala al actuar (191), lanza el piloto y devuelve la cuenta. Se borran `LlegarAlTriageAsync`,
`EscribirEditor`, `EnvioAlTriage.cs`, `EditoresDelTriage.cs`.

### Fase 4 — todo campo tecleado es un hueco con el nombre del campo (196)

`SkillDeLoVerificado`: el «dicho» de un evento tecleado pasa a ser el NOMBRE del dato: la etiqueta
del campo si la hay, si no lo que se decía; el campo de comandos (`okcd`) no es hueco. Lo medido
en la decimotercera lección manda: la etiqueta está en 17 de 17; lo dicho es ruido en 3 de 3 donde
había. `InstanciarSkill.SinDato` nombra los huecos que quedaron en blanco, y `CorrerSkill` lo dice.

### Fase 5 — la skill se da con la coreografía (197)

`SurfaceMapTools.RecorrerSkill(pasos, darUnPaso)` (puro): de uno en uno, para en el primero que no
se da, total = plan. `CorrerSkill` lo usa con `DarUnPasoConCoreografia` cuando la app señala al
actuar; lo que se dice en cada campo tecleado es el nombre del dato.

## Primera tanda: lo que se midió al implementar (2026-09-10)

**Sabotaje.** Con las siete piezas rotas a propósito (el catálogo sin huecos; `map_take` en la caja
del encargo y las pendientes ofrecidas; `--leccion=` siempre; lo dicho mandando sobre la etiqueta;
`SinDato` mudo; el recorrido que no para), el contrato dio **12 aserciones rojas en las 5 promesas**,
cada una la suya. Sanado, la huella de los siete archivos volvió a la de antes.

**Humo del piloto en modo encargo**, contra el MCP de la app de desarrollo y un `mensaje.json`
escrito a mano con la nota («peso 70 kg, talla 170 cm, TA 120/80, temperatura 36,8») y el aviso de
que no hay skill comprobada: el piloto llamó `map_skills`, razonó que «las dos únicas skills listas
solo navegan hasta la pantalla de Triage, no escriben datos», lo dijo por `voz_decir` y paró sin
tocar nada. 3 turnos, 0,32 USD (`claude-sonnet-5`). Es la conducta pedida: elige por su criterio y,
si nada corresponde, no inventa.

**Lo que queda para la máquina con SAP abierto** (nivel 4, lo hace el dueño):

1. Comprobar de nuevo la decimotercera lección (o enseñar el triage otra vez): con esta rama la
   skill sale con un hueco por campo tecleado, nombrado por su etiqueta («Peso», «Talla»…), y sin
   hueco en el campo de comandos. En el log: `skill «…» guardada con N paso(s) verificado(s), COMPROBADA`.
2. `map_skills` (o el log de `envio`) tiene que decir `necesita: Motivo de Consulta, Presión Arterial, …`.
3. Grabar una consulta, marcar secciones y pulsar ✓: en el log, `envio: encargo: … skill(s)
   comprobada(s)`, `piloto: herramienta: mcp__u__map_skill_run`, `skill: corriendo «…» · con
   coreografía`, y la cuenta con `Quedaron EN BLANCO por falta de dato: …` si la nota no lo traía.

**Hallazgo que queda escrito:** en el formulario del triage la diastólica tiene etiqueta «/» (el
campo de la izquierda es «Presión Arterial»). Ese hueco se llamará «/» y la nota nunca lo casará:
quedará en blanco y se dirá. Arreglarlo es nombrar el campo por su vecino o por su nombre técnico
(`ZTRPADIA` o el que sea) cuando la etiqueta no es una palabra; no entra en esta spec.

## Lo que NO entra

- Botones de opción y casillas como datos (se graban como clic sin texto; siguen sin contarse).
- Que el piloto navegue con manos sueltas hasta donde empieza la skill: solo `map_go_to` (rutas del
  terreno) y `map_open_app`. Si no llega, lo dice y para.
- La cola de exportaciones del Graph (`EjecutorDeExportaciones`, 115/116): otro camino, no se tocó.
- Que la skill continúe después de un paso que no se dio.
