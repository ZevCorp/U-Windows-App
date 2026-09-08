# Plan de implementación: el recuerdo se ve mientras Ü comprueba

Estado: **implementada** (180–192 verdes; 175 enmendada; 142, 171, 178, 179 y 188 extendidas; contratos del grafo y de la voz intactos; sabotaje comprobado en 142, 171, 175, 178, 179, 181, 182, 184–192) · Nace de la quinta prueba real de la spec 013 (2026-09-07, 17:18) ·
Rama: `jose/recuerdos-que-se-ven`

> El dueño, con la quinta prueba COMPROBADA en la mano: «solo quiero una mejora, y es que agregue
> visualmente los recuerdos mientras va ejecutando: la carita se mueve al lado del elemento
> señalando, ilumina el elemento, escribe el aprendizaje, deja de señalarlo, se ve el recuerdo y
> continúa».

## Diagnóstico: qué se midió

| Qué | Medida | Fuente |
|---|---|---|
| Lo que el plan hace hoy por paso | dice la frase por voz, cuelga el recuerdo con `map_esto_es`, da el paso por el batch, juzga | `FaceWindow.RecorrerElPlan` (spec 013) |
| Lo que la persona ve | el recuadro del `map_take` (con `SenalarAlActuar`) durante el clic; el recuerdo no se ve nunca | log de la quinta prueba: recuerdos colgados a las 17:17:13 sin tarjeta |
| Lo que ya existe para verlo | `Senalador.Senalar(caja, qué)` mueve la carita al lado y enciende el recuadro (2026-08-05 y 2026-08-22); `TarjetasDeRecuerdo.Mostrar(...)` pinta la tarjeta con el texto | `FaceWindow.xaml.cs:195`, `TarjetasDeRecuerdo.cs:75` |

Todo lo necesario estaba; faltaba el orden. **Por eso va con promesa:** el orden es exactamente lo
que se puede equivocar en silencio (tocar antes de mostrar enseña el recuerdo sobre una pantalla que
ya cambió), y nadie lo ve mal en el código.

## La especificación

| # | Promesa | Fase |
|---|---|---|
| 180 | al recorrer el plan, cada paso se VE antes de tocarse: la carita se pone al lado del elemento y el elemento se enciende, se dice y se escribe el recuerdo, la tarjeta con el recuerdo queda a la vista el tiempo que tarda leerla, y solo después se toca; al tocar, la tarjeta se cierra y la señal se suelta; si el elemento no está en pantalla no se muestra tarjeta sobre la nada y el paso va igual al ejecutor | 1 |
| 181 | una herramienta que devuelve una imagen la entrega como IMAGEN y no como un texto de base64 que el modelo no puede mirar: el data URI se parte en su tipo y sus datos y viaja como bloque de imagen; lo que no es imagen, y un data URI roto, siguen viajando como texto | 2 |
| 182 | la fila de una rejilla ALV bajo un clic se elige por su selección, o por el cursor, o —si solo hay una fila— por ser la única, aunque nada esté marcado: un paciente premarcado ya no queda sin identidad | 3 |
| 183 | map_what_i_see suma a lo que ve UIA las puertas que el terreno conoce y UIA no puede ver, sin duplicar las que ya tienen nombre: la fila del paciente deja de ser una puerta invisible | 3 |
| 184 | un paso tecleado que SAP publica tarde —al parar la demo, todos a la vez— se cuelga del clic que abrió ESE campo, por identidad de selector, aunque por tiempo le tocara otro clic; sin clic con esa identidad, rige la cercanía de siempre | 4 |
| 185 | un campo de SAP se encuentra por lo que la persona ve o por lo que la lección trae: su selector exacto, su etiqueta («Nombre del paciente») o el nombre técnico del campo sin el prefijo del tipo («Y0000000-ZTRNOMPAC» para txtY0000000-ZTRNOMPAC); con dos etiquetas iguales no se adivina: nadie | 4 |
| 186 | quién está bajo un punto dentro de SAP se pregunta primero a SAP y, cuando SAP calla —que es lo que hace este SAP siempre—, se decide por geometría: el elemento MÁS PEQUEÑO que contiene el punto, sin contar nodos ni cajas desconocidas; y del id que salga se sube por su ruta hasta el campo que la lección puede reencontrar | 5 |
| 187 | al cerrar la demo, lo que la persona tecleó y SAP no llegó a publicar se descarga LO PRIMERO, con el oyente todavía enganchado: descargar va antes de soltar la superficie, antes de parar el video y antes de armar la lección, y parar al grabador va el último | 5 |
| 188 | lo que la persona NOMBRA dentro de SAP —para colgarle un recuerdo, para señalarlo o para verlo en la lista de lo que hay— se resuelve en UN solo sitio y con el mismo criterio que para escribir: primero las puertas del terreno, después los campos del dynpro por su etiqueta o su nombre técnico; con dos iguales nadie, y lo que ningún sitio conoce no se inventa | 6 |
| 175 (enmendada) | el total = los eventos que navegan más los campos tecleados, uno por campo con su último valor; un campo tecleado está hecho si dice AHORA lo que la demo tecleó, leído y no declarado | 6 |
| 189 | lo que el grabador de SAP ve cruzar —un paso publicado y la pantalla nueva que SAP anuncia justo después— se le enseña al terreno como arista, con el mismo selector con el que el terreno conoce la puerta; la llegada la sigue diciendo el terreno, que ahora también aprende de SAP | 7 |
| 190 | en un desplegable de SAP se fija la CLAVE de la opción, y esa clave se encuentra por la clave misma o por el texto que la persona lee; con dos iguales nadie, y un valor que no es ninguna opción se rechaza diciendo cuáles hay | 7 |
| 178 (extendida) | un clic dado dentro de SAP cuya «llegada» es SAP visto por UIA («uia://saplogon.exe/…») no tiene llegada: es la misma ventana con el ojo equivocado | 7 |
| 179 (extendida) | un «n» del plan que viene como texto numérico vale como número | 7 |
| 191 | dar un paso es UNA coreografía, lo dé el plan o la mano del piloto; map_take y map_type llevan «decir» y «recuerdo»; la identidad del paso se decide en un solo sitio: se señala y se nombra por la puerta aunque el piloto traiga el selector, y se escribe por el selector de la lección aunque traiga el nombre | 8 |
| 171 (extendida) | si SAP trae OTRA puerta que la que el vigía adivinó por geometría, manda la de SAP con su nombre | 8 |
| 188 (extendida) | una puerta del terreno se encuentra también por su selector exacto | 8 |
| 192 | durante la comprobación la voz en vivo es una voz PRESTADA: sin herramientas, dice exactamente lo que la app le pide y calla ante todo lo demás, y la sesión no crea respuestas por su cuenta; al terminar vuelve a ser quien era | 9 |
| 178 (extendida) | al terreno se le pregunta con la puerta que SAP vio, no con la que el vigía adivinó | 10 |
| 175 (extendida) | el juez dice qué falta por su nombre (`Pendientes`) | 10 |
| 179 (extendida) | el relato nombra lo que falta por juzgar, evento por evento | 10 |
| 142 (extendida) | lo dictado se pide FUERA de la conversación (`conversation=none`): sin contexto que la tiente a parafrasear | 10 |

### Con qué se juzga

Mapa a mano sobre `ElRecuerdoQueSeVe.Coreografia(hayElemento, hayRecuerdo, hayDecir)`: el orden de
los gestos en los cuatro casos, y `TiempoDeLectura` con su suelo y su techo. El nivel 4, a mano:
verla en una comprobación real del triage.

## Las fases

### Fase 1 — La coreografía (180)

| | |
|---|---|
| Dónde | `windows-client/src/Piloto/ElRecuerdoQueSeVe.cs` (puro) + `FaceWindow.RecorrerElPlan` (lo ejecuta en ese orden) |
| Termina cuando | 180 verde y una comprobación real donde se vea la tarjeta antes de cada clic |

## Tercera tanda: la fila del paciente (2026-09-07, noche)

**El problema, medido.** La sexta prueba se rompió en el clic que selecciona al paciente. La lista de
Urgencias es una rejilla ALV (`GuiShell` subtipo `GridView`). Tres piezas la leen y solo una la
entendía: el grabador la lee bien pero solo cuando la selección CAMBIA (con un paciente premarcado no
cambia nada); la mano (`map_take` → `SelectGridRow`) la selecciona por nombre y FUNCIONA (probado en
vivo); pero el vigía solo nombra árboles, y `map_what_i_see` solo lista lo de UIA, que ve el Pane
opaco. Así que la fila quedaba muda al enseñar e invisible al comprobar.

**El ajuste, reusando lo que ya existe** (no se estrena ninguna detección):
- **182 — enseñar:** el vigía, cuando el clic no cae en un árbol, prueba la rejilla con
  `SapGuiSurface.FilaDeGridEn`, que reusa `RowKeyAt` (la lectura del grabador) y elige la fila por
  `FilaAElegir(selección, cursor, nº filas)`. Un paciente premarcado ya se nombra.
- **183 — comprobar:** `map_what_i_see` funde lo de UIA con las puertas vivas del terreno
  (`FundirPuertas`), sin duplicar. El piloto ve «GIRALDO» y lo pide por su nombre.
- **181 (ya hecha) — mirar:** el piloto confirma con `map_shot`, que ahora viaja como imagen, que la
  fila quedó marcada, porque seleccionar no cambia de pantalla.

**Lo que NO cambia:** las manos siguen seleccionando por identidad (`SelectGridRow` por
`SelectedRows`), nunca por coordenadas. La vista es para verificar y decidir, no para pulsar.

## Cuarta tanda: lo tecleado en la demo (2026-09-07, séptima prueba)

**El problema, medido en el log.** La demo (`leccion_20260907_214057`, 140 s) rellenó el triage de
GIRALDO entero. El plan llegó al formulario en 19 s (2/3 aterrizados, GIRALDO seleccionado: la
tercera tanda funcionó), pero la lección traía 24 eventos y **ni un texto**, y el piloto no pudo
escribir nada con las manos. Tres causas, cada una en su pieza:

| Pieza | Qué pasaba | Evidencia |
|---|---|---|
| El grabador | con eventos COM, lo tecleado solo se publica cuando la pantalla VIAJA (`Change`/`StartRequest`); la demo paró en el formulario sin viajar, y `StopObserving` descargaba las filas de árbol pero no los campos | `21:42:19 línea base rehecha, 39 campo(s)` y después nada hasta `21:43:18 3 paso(s) observados` |
| El vigía | nombraba un clic en SAP solo si caía en un árbol o una rejilla: los 19 clics sobre campos quedaron sin identidad | `21:42:38–51 clic-sap: ni árbol ni rejilla: que lo intente UIA` ×7 |
| Las manos | `map_type` escribía SIEMPRE por UIA (que dentro de SAP ve un Pane opaco) y remataba con un Enter; la mano de SAP del batch solo entendía un selector `sap:` entero | `no pude escribir en «Y0000000-ZTRNOMPAC»: no se encontró el elemento «»` · `NO escribo: … Gos Container` |

**El ajuste, reusando lo que ya existe:**
- **184 — enseñar:** `ArmarLaLeccion.Eventos` cuelga un paso tecleado del clic con el MISMO selector
  antes que por tiempo. `StopObserving` descarga también los campos (`PublishChangedFields`), no
  solo las filas. El vigía, tras árbol y rejilla, prueba el campo bajo el punto con
  `SapGuiSurface.CampoEn` (mismo `Describe`, mismo selector que el grabador).
- **185 — comprobar:** `SapGuiSurface.ElCampoQueSeLlama` encuentra un campo por selector, etiqueta o
  nombre técnico. Lo usa la mano de SAP del batch (`FaceWindow`, `escribir:`) cuando lo que llega no
  es un selector, y `map_type` en SAP va por ese mismo ejecutor (`Paso(target, texto)`), sin Enter.
- **El plan:** un paso con `text` escribe en el selector del evento de la lección, no en el nombre
  que eligió el piloto. Y el prompt del piloto ya no prohíbe repetir lo tecleado: viaja en `text`.

**Nivel 4 (sonda contra el SAP real, 22:25, pantalla `SAPLY000/0001` «Reg. Triage Modificar»):**
39 campos leídos; «Motivo de Consulta» → el shell `cntlCT__ZTXTMTVCN`, «Y0000000-ZTRNOMPAC» → el
`txt`, «Peso», «Talla», «Presión Arterial» → sus campos; «Fecha» → nadie (no hay etiqueta así).
La descarga al parar y el campo bajo el clic se verifican en la siguiente demo (log `clic-sap:
clic en un campo → …` y `paso(s) observados` con texto).

**Lo que sigue igual:** la promesa 123 (un valor tecleado es un hueco en la SKILL) no cambia: al
COMPROBAR se escribe lo que la demo tecleó, en QAS, para verificar el camino; la skill sigue
guardando huecos.

## Quinta tanda: los dos fallos que la cuarta no vio (2026-09-07, octava prueba)

El dueño pulsó «comprobar» tres veces y no pasó nada. Tres fallos, y **los tres eran míos**:

| Qué | Por qué | Cómo se vio |
|---|---|---|
| El piloto no arrancaba | escribí `` `target` `` con comillas invertidas dentro de la plantilla del prompt: cierra la cadena | `piloto-err: SyntaxError: Unexpected identifier 'target'` · salida 1 en 1,2 s |
| Ningún clic quedaba nombrado | escribí `CampoEn` y `FilaDeGridEn` sobre `FindByPosition`, que **este SAP documentadamente no contesta** | `clic-sap: ni árbol, ni rejilla, ni campo` ×24 en una demo de 92 s |
| La descarga de lo tecleado no servía | la puse en `StopObserving`, que corre en `recorder.StopAsync()` — 47 líneas DESPUÉS de `GuardarLaLeccion` | `0 paso(s) observados` con la descarga ya escrita |

**El segundo es el aprendizaje caro, y ya estaba en el repo.** `CLAUDE.md` §4 dice «pregúntale a la API
antes de creerle al código», y `SapGuiSurface.ClickedComponentId` lleva meses con este comentario:
*«FindByPosition devuelve null en este SAP —ya estaba documentado para las filas de árbol, y resulta
que también para los botones»*, seguido de su hit-test por geometría. Escribí una tercera vía en vez
de reusar la que funciona (patrón nº6: cuando una limitación documentada es real, se reusa la
compensación que ya existe, no se estrena otra).

**Medido sobre el SAP real** (`SAPLY000/0001`, con las coordenadas de los 8 clics de la demo fallida):

| Punto | `FindByPosition` | por geometría |
|---|---|---|
| los 8 (campos, caja de texto largo) | NULL las 8 veces | el campo exacto las 8 veces |

**El ajuste:**
- **186:** `ElMasPequenoQueContiene` y `ElCampoDeEsteId` son puros y los juzga el contrato;
  `ComponentAt` —el hit-test que ya existía— pasa a usar el primero, así que hay UNA geometría y no
  dos. `CampoEn` y `FilaDeGridEn` piden el id a `QuienEstaEn`: SAP primero, geometría después.
  Verificado en vivo: `CampoEn` devuelve «Presión Arterial», «Peso», «Conducta» y «Motivo de
  Consulta» en los cuatro puntos probados.
- **187:** `ElCierreDeLaDemo.Orden` dice el orden del cierre, y `WorkflowTeachSession.StopAsync` lo
  sigue: descargar lo pendiente va lo primero, con el oyente aún enganchado. `IUiSurface` gana
  `DescargarLoPendiente()`; en UIA es un no-op documentado, porque UIA publica cada pulsación.

**Lo que falta por ver en la máquina:** una demo nueva donde la persona teclee. En el log tienen que
aparecer `clic-sap: clic en un campo → «…»` y `paso(s) observados` con texto.

## Sexta tanda: lo que cuenta y lo que se nombra (2026-09-08, novena prueba)

La novena prueba **funcionó**: la lección salió con 11 clics con identidad y 15 pasos observados, todo
lo tecleado en su campo, y el plan dio los 14 pasos en 17 s. El dueño vio dos cosas en el log:

| Qué | Por qué | Evidencia |
|---|---|---|
| «0 de 0 aterrizados · sigue pendiente» tras 14 pasos hechos | el total eran «los eventos que navegan», y esa demo no navega | `comprobar: … 0/0 aterrizados · SIGUE PENDIENTE` |
| 11 recuerdos de 14 rechazados | colgar un recuerdo y señalar buscaban el nombre en UIA y en el terreno; un campo del dynpro no está en ninguno | `no veo nada que se llame «Presión Arterial»` ×11 · `«…» no está en pantalla para señalarlo` ×14 |

**La 175 se enmienda con el dueño** («habrá enseñanzas que no naveguen»). El número se queda; el
enunciado cambia: el total = los eventos que navegan **más los campos tecleados**, uno por campo con
su último valor. Un campo tecleado está hecho si el campo **dice ahora** lo que la demo tecleó, leído
por SAP (`ValorActual`), no declarado por el modelo. Un «llegué» sobre el clic que abrió el campo se
juzga sobre el campo, por identidad. Lo que ni navega ni teclea (un botón de opción, un clic que
solo abre un campo) no cuenta: no hay nada que leer para juzgarlo. `RegistroDeLaComprobacion.EventosQueCuentan`,
`LoMismoTecleado` (80 = 80,000), y `SkillDeLoVerificado` da la skill por completa sobre lo que cuenta.

**188 — lo que se nombra en SAP se resuelve en UN sitio:** `SurfaceMapTools.LoQueSeNombra(nombre,
puertasDelTerreno, camposDeSap)`: primero el terreno, después los campos del dynpro con el resolutor
de escribir (`ElCampoQueSeLlama`, 185). Lo usan `map_esto_es`, señalar (`IluminarUno`) y
`map_what_i_see`. La app entrega los campos por `CamposDeSap`, solo dentro de SAP.

**Tres hallazgos más al verificarlo en vivo, los tres en `map_what_i_see`:** (a) cortaba con «no veo
ningún elemento» antes de mirar el terreno y el dynpro, cuando UIA venía en blanco; (b) el terreno
conoce los campos por su nombre técnico («Y0000000-ZTXTTASIS») y la fusión por selector descartaba
la etiqueta humana: `ConLaEtiquetaQueSeLee` deja el tipo del terreno y la etiqueta que se lee (en
el contrato, dentro de la 188, sabotaje comprobado); (c) el tope de 40 puertas de SAP dejaba fuera
los signos vitales de un formulario de 60: ahora 160.

**Nivel 4 (2026-09-08, 00:58, `SAPLY000/0001` con SAP delante):** `map_what_i_see` lista «Presión
Arterial», «Motivo de Consulta», «Frec. Cardíaca», «Sat. O2», «Conducta»; `map_esto_es
sobre=Presión Arterial` cuelga el recuerdo (antes: «no veo nada que se llame»).

**Lo que falta por ver en la máquina:** una demo que teclee y se compruebe: en el log, `evento N:
HECHO · «Peso» dice «80»…` y recuerdos colgados en los 14 campos.

## Séptima tanda: la décima prueba, desde Easy Access (2026-09-08, 01:10)

**Lo que salió bien.** La lección trajo 33 eventos, 26 con identidad, todo lo tecleado en su campo y los
desplegables de Glasgow con su clave. El plan del piloto tuvo 18 pasos y la app hizo **17**: navegó
desde Easy Access hasta el formulario, seleccionó a GIRALDO, pulsó Triage y rellenó todo hasta la
saturación. Lo que el dueño vio fallar, los selectores de Glasgow, es el paso 18.

**Lo que el log dice además**, cuatro cosas, cada una con su arreglo:

| Qué | Por qué | Arreglo |
|---|---|---|
| El veredicto dijo «2 de 16» con 17 pasos hechos | el piloto mandó `"n":"1"` como texto; el lector lo leyó como 0 y el juez no fue llamado en ningún paso | 179 extendida: `Leer` acepta el texto numérico |
| El paso 18 no llegó a darse | la guarda de una llamada era 100 s fijos y 18 pasos con tarjeta de lectura tardan más | la guarda crece: 8 s por paso, nunca menos de 100 |
| Glasgow no se pudo escribir ni con la lección («4») ni con las manos («Espontánea») | un `GuiComboBox` no acepta `Text`: se le fija `Key` | 190: `ClaveDeLaOpcion` por clave o por texto; `Apply` y `EscribirEnElFoco` la usan |
| «Triage» sin llegada por tercera prueba seguida, y «Apertura Ocular» con llegada `uia://saplogon.exe/…` | SAP tarda más de los 6 s del mapa vivo y el mapa se queda ciego; al volver, el salto queda sin atribuir, y la ventana de SAP vista por UIA se cuelga al último clic | 189: el grabador de SAP —que sí vio el paso a las 01:11:38 y la pantalla nueva a las 01:11:41— enseña la arista al terreno (`ElCruceQueVioSap` + `Nucleo.Cruzar`); 178 extendida: SAP visto por UIA no es llegada |

**Por qué 189 respeta la 178.** La 178 dice que la llegada la dice el terreno y que no se inventa la
pantalla del clic siguiente. Sigue igual: el terreno decide. Lo nuevo es que el terreno tiene un
segundo maestro, el grabador de SAP, que lee el cruce por scripting en el instante del round-trip,
sin depender de la ventana de 6 s ni de que el mapa vivo vea la pantalla.

**Lo que falta por ver en la máquina:** en la próxima demo, `terreno: el grabador de SAP enseñó la
arista: «…#tbbtn=ZMEDTRIAGE» lleva de …» en el log, y la lección con la llegada de «Triage» puesta.
Y en la comprobación, los desplegables de Glasgow escritos y juzgados por su clave.

## Octava tanda: la experiencia estándar (2026-09-08, undécima prueba)

**Lo que salió bien.** El terreno aprendió de SAP las tres aristas (favorito, vista Triage, botón
Triage: `terreno: el grabador de SAP enseñó la arista`), la lección trajo 57 eventos con 36
identidades, y la comprobación dejó el triage entero con veredicto por campo (`evento N: HECHO`).

**Lo que el dueño vio:** «en la prueba anterior la carita se movía al lado de cada campo y narraba
lo que iba haciendo, y justo después sucedía; en esta no. Quiero que esa sea la experiencia estándar
que siempre suceda».

**Por qué no sucedió.** El plan paró en el paso 3: el piloto trajo el SELECTOR de la fila del
paciente («…#row=DATUM=08.09.2026|ZEIT=00:51|…») en vez de su nombre, y el batch dijo «no lo
conozco»: la clave de una fila de ALV lleva la fecha y la hora de la lista. El piloto siguió con las
manos, y las manos no tenían coreografía: la carita, la voz y la tarjeta eran del recorrido del
plan, no del paso.

**El ajuste (191):** la coreografía es UNA función, `FaceWindow.DarUnPasoConCoreografia`, que
usan el plan y las manos (`map_take` y `map_type` llevan ahora «decir» y «recuerdo», y al comprobar
pasan siempre por ahí). La identidad del paso la decide `ElPasoQueSeDa.Resolver`: se señala y se
nombra por la puerta aunque el piloto traiga el selector, y se escribe por el selector de la lección
aunque traiga el nombre. El prompt del piloto lo dice con todas las letras: la puerta por su
NOMBRE, nunca el selector; y con las manos, siempre «decir» y «recuerdo».

**Dos más, vistas en la misma lección:** el vigía nombró el clic en el botón «Triage» de la barra
de la rejilla como la fila «GIRALDO» (el botón vive dentro del shell de la rejilla); SAP publicó el
botón, y ahora la puerta que SAP vio manda sobre la adivinada (171 extendida). Y nombrar por el
selector exacto de una puerta del terreno también vale (188 extendida), para colgar el recuerdo
cuando el plan trae el selector.

## Novena tanda: dos manos a la vez (2026-09-08, duodécima prueba)

**Lo que el dueño oyó:** «la voz varias veces se desalineaba de lo que realmente se estaba haciendo,
y empezaba a sugerir cosas o a decir que iba a hacer algo que no era el siguiente paso».

**Lo que el log dice.** No era un desfase: eran **dos agentes actuando a la vez**. Mientras la app
recorría el plan (`evento N: HECHO`), la conversación de voz en vivo —a la que la comprobación solo
le pide «di exactamente esto» (promesa 142)— iba llamando `map_take` y `map_type` por su cuenta:
`voz-viva: llamada recibida: map_take` con `exit=Urgencias Adultos Triage` desde el formulario,
`map_type target=Presión Arterial text=120/80` (la demo tecleó 100 y 120), `map_take exit=Intro
decir=Cambio de foco para intentar escribir en el campo adecuado`. Tenía el catálogo entero y
respuestas automáticas: cada frase prestada era un turno más de un asistente con manos, y tras
decirla seguía «ayudando».

**El ajuste (192):** `Piloto.VozPrestada`: sin herramientas, instrucciones de altavoz («di
exactamente esto… y calla ante todo lo demás»), y la apertura de la sesión con
`create_response=false` (`IProtocolo.Apertura(…, soloCuandoSeLePide)`): el servidor sigue oyendo
y transcribiendo —el piloto lee la respuesta de la persona por `voz_preguntar`— pero no habla hasta
que la app pide turno. `ComprobarConElPilotoAsync` presta la voz al empezar y la devuelve al
terminar, igual que `ModoAprendiz` al enseñar (138). También se suelta `SenalarAlActuar` al
terminar, que se quedaba puesto.

**Lo que falta por ver en la máquina:** en el log, `voz-viva: modo cambiado: 0 herramienta(s) …
solo habla cuando se le pide` al empezar la comprobación, ninguna línea `voz-viva: llamada recibida`
durante ella, y el modo normal restaurado al final.

## Décima tanda: la voz prestada funciona, y lo que quedaba (2026-09-08, decimotercera prueba)

**Lo que salió bien.** `voz-viva: modo cambiado: 0 herramienta(s) … solo habla cuando se le pide` al
empezar, ni una `llamada recibida` durante la comprobación, el terreno aprendió las tres aristas de
SAP, el plan hizo sus 21 pasos en 116 s con la carita y la frase delante de cada uno, y el juez leyó
17 campos y 4 desplegables con «HECHO». La decimotercera prueba es la primera que recorre el triage
entero sin que el piloto tenga que meter las manos por un fallo.

**Los tres flecos que dejó el log:**

| Qué | Por qué | Arreglo |
|---|---|---|
| «19 de 20» con todo hecho, y el piloto repitió cinco pasos a mano buscando el que faltaba | la llegada de «Triage» salió VACÍA aunque el terreno la había aprendido: las llegadas se buscaban con la identidad que el vigía adivinó («GIRALDO»), y SAP solo la corregía después; el que contaba era el último clic sin identidad, que nadie puede dar | 178 extendida: `ConLaIdentidadDeSap` antes de `Llegadas`; 175 y 179 extendidas: el juez dice qué falta y el relato lo nombra |
| La voz dijo «Glasgow, entre 3 y 15» cuando el plan pedía «Temperatura», y antepuso «Vale, déjame pensar un momento…» a «Elijo al paciente» | la respuesta dictada se generaba DENTRO de la conversación, con el triage entero de contexto | 142 extendida: lo dictado se pide con `conversation=none` |
| El aprendiz habla de más mientras se enseña («Vale, ajá, entiendo. Me estás diciendo que…», ocho frases largas) | `ModoAprendiz` pide una palabra y el modelo contesta párrafos | no se toca hoy: es calidad del modelo, y no rompe la lección; queda anotado |

**Lo que se ha ejercitado de SAP en estas trece pruebas:** árbol de favoritos, árbol de vistas con
carpetas, fila de una rejilla ALV, botón de barra de la rejilla, campos de texto, cajas de texto
largo (shells), desplegables por clave y por texto, y un popup (`btnSPOP-OPTION1` aprendido por el
terreno). **Lo que NO se ha ejercitado todavía:** botones de opción y casillas (se graban como clic
sin texto y el juez no los cuenta), campos de fecha con ayuda de búsqueda, controles de tabla
(`GuiTableControl`), pestañas, navegación por código de transacción con Enter dentro de una lección,
y diálogos modales en medio del plan.

## Lo que NO entra

- Un botón para leer la tarjeta con calma (pausar el plan). Hoy es un tiempo de lectura por texto.
- Que la tarjeta permita corregir el recuerdo en medio del plan: `TarjetasDeRecuerdo` ya lo permite
  a mano, pero el plan no espera a que se termine de escribir.
