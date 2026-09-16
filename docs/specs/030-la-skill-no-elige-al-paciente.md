# Plan de implementación: la skill no elige al paciente

Estado: **propuesto** · Nace del diagnóstico del 2026-09-16 · Rama: `jero/el-check-elige-la-skill`

> Lo decidido con el dueño el 2026-09-16, al leer por qué el ✓ podía escribir en otra historia: **el
> médico tiene abierto el triage de SU paciente; la skill empieza ahí y nunca elige paciente.**

## Diagnóstico: qué se leyó

Todo lo de esta tabla está **leído en el código** de la rama (tras fusionar `main` en `0836ed0`), no
medido en SAP. Lo que solo SAP puede decir va al nivel 4, al final.

| Qué | Lectura | Fuente |
|---|---|---|
| Cómo guarda la skill comprobada la fila del paciente | como un paso más: `Exit` = la etiqueta del evento, que para una fila de ALV es el primer valor con letras de la fila —el apellido del paciente de la demo— | `SkillDeLoVerificado.Empaquetar` (`identidad = e.Etiqueta`), `SapGuiSurface.RowKeyAt` |
| Cómo encuentra el batch esa fila al correr | ni por selector (el paso no lo trae) ni por etiqueta exacta (la etiqueta viva de una fila es «fecha · hora · episodio · nombre», `LeerRejilla`), así que cae al difuso: `LoNombra` casa si la etiqueta viva CONTIENE el apellido | `RecorrerSegunElNucleo.EsperarloVivo`, peldaño 2 |
| Qué pasa con un homónimo | la única fila viva que contiene el apellido es la de OTRA persona: se selecciona, el paso siguiente pulsa «Triage» (`#tbbtn=ZMEDTRIAGE`, que actúa sobre la fila seleccionada) y la nota se escribe en su historia | `SapSelector.RowMark` (el botón del ALV actúa sobre la selección), `InstanciarSkill.Pasos` |
| Qué pasa si la fila no entró en la skill (no aterrizó al comprobar) | el botón «Triage» actúa sobre lo que SAP tenga marcado en ese momento | `SkillDeLoVerificado` (solo entra lo que aterrizó, 176) |
| Si la consulta sabe quién es el paciente | no: `patient_id` viaja nulo y el encargo solo lleva secciones de la nota | `ClinicaClient.cs:86`, `Encargo.De` |
| Dónde empiezan a correr las skills | desde el primer paso, sin mirar dónde está SAP, en los DOS sitios que corren skills | `SurfaceMapTools.CorrerSkill`, `FaceWindow.MostrarAprendizajeAsync` |
| Qué hace el piloto antes de correr | «si no estás en ella, ve con map_go_to»; `map_go_to` es `PasoDelNucleo.Hasta`, y `Grafo.ComoLlego` no distingue una arista aprendida desde una fila de cualquier otra puerta | `piloto.mjs` (`SISTEMA_ENCARGO`, paso 3), `PasoDelNucleo.Hacia` |
| Precedente en el repo | la demo 🚀 ya mira si SAP está en el formulario (`SAPLY000`) y se salta la navegación | `FaceWindow.OnDemoPuntaAPunta` |

## Por qué esto va dirigido por especificación

Porque el fallo es **el peor de este repo**: escribe datos clínicos en la historia de otra persona
con cara de haber salido bien. La cuenta diría «hice los N pasos», el piloto lo contaría por la voz y
nadie miraría el nombre de la cabecera del triage. Es el «29 de 30» con una víctima. Y el
subsistema se da por bueno a sí mismo: la comprobación repasa la demo con el paciente de la demo, así
que la skill sale COMPROBADA justo con el paso que elige paciente dentro.

**La regla del flujo, y no tiene excepciones:** ninguna línea de producción entra antes que la
promesa que la juzga. Cada fase empieza con el contrato ROTO y termina con el contrato INTACTO.

## La especificación

| # | Promesa | Fase que la pone verde |
|---|---|---|
| 254 | una tarea enseñada no elige de quién es la historia: si la lección eligió una fila de una lista, la skill empieza DESPUÉS del primer paso que navega tras esa elección —ahí dice que empieza— y ni la fila, ni ese paso, ni nada anterior queda entre sus pasos; aterrice la fila o no, da igual, y una lección sin filas sale como siempre | 1 |
| 255 | una skill que empieza con el registro abierto solo corre desde ahí: si SAP no está en esa pantalla no se da ni un paso —ni navegar hasta ella— y la cuenta dice qué abrir; en ella corre entera, también cogida a medio cambiar; y una skill que no empieza en un registro corre como siempre | 2 |
| 256 | un registro no es una puerta que se tome sola: ni un paso de una skill ni una ruta de ir-a pulsan jamás una fila de una lista —aunque se llame igual, aunque la ruta aprendida pase por ella—; paran diciendo que la fila la elige la persona. Pulsarla cuando la persona la nombra sigue valiendo | 3 |

La que cierra el asunto es la **256**: las skills que ya están en disco nacieron con la fila dentro, y
254 y 255 no las tocan. Mientras un paso de skill pueda pulsar una fila, basta una skill vieja para
escribir en otra historia.

### Con qué se juzga cada una

Todas con **mapa a mano** en la propia prueba; ninguna toca la pantalla.

**254** — una lección como la de la 196, con sus veredictos:
okcd «nwp1» (llega a N) → rama del árbol (llega a L) → fila `sap:…#row=…` con etiqueta «DEMO» y sin
llegada → `#tbbtn=ZMEDTRIAGE` «Triage» (llega a F) → «Peso» 52 → «Talla» 1.7.

- (i) todo aterrizó: ningún `Exit` contiene «DEMO», «#row=», el botón de Triage ni «nwp1»; el primer
  paso es «Peso»; `EntradaDeLaPersona` vale F y la skill dice con qué se abre («Triage»).
- (ii) la fila sin veredicto: lo mismo.
- (iii) la misma lección sin la fila: todo como hoy (nwp1, la rama y Triage siguen) y la entrada vacía.
- (iv) una fila sin ningún paso que navegue después: la entrada queda vacía, **no se guarda skill** y
  el motivo lo dice (no hay desde dónde empezar sin elegir al paciente).
- Y lo que ve quien elige: el panel (`LoQueHaceLaSkill`) no nombra la fila, y el catálogo
  (`map_skills` y el mensaje del encargo) dice que la tarea empieza con el registro ya abierto.

`EntradaDeLaPersona` es una propiedad `init` NUEVA de `SkillEnsenada`: el constructor posicional de
cuatro argumentos no se toca (102 y 106 lo usan por reflexión) y `DondeEmpieza` tampoco (lo leen
`ElEncargoDeComprobar` y `RecuerdosDeUnaSkill`, que necesitan dónde empezó la DEMO).

**255** — `SurfaceMapTools` con un localizador falso, una carpeta de skills temporal y un recorrido
que cuenta sus llamadas; una skill comprobada con entrada F y otra sin entrada:

- aquí = L: cero llamadas al recorrido, cero a ir-a, y un texto que dice qué abrir («Triage») sin
  nombrar a nadie.
- aquí = F: corre, con el dato de hoy.
- aquí = F cogida a medio cambiar (`SESSION_MANAGER` sobre el programa del formulario): corre.
- aquí = nada (no se sabe dónde está SAP): cero llamadas.
- la skill sin entrada, aquí = L: corre como hoy.

La decisión es UNA función pura, `SkillEnsenada.PuedeEmpezarEn(aqui)`, hermana de `PuedeCorrer` (127),
que compara con `Superficies.MismaPantalla` (226) y que llaman los dos sitios que corren skills.

**256** — dos sitios, dos juicios, y un delegado «esto es un registro» (como `AccionableAunSinVerse`)
para que ni el batch ni el núcleo sepan de SAP:

- (a) el batch sobre un terreno falso con una fila viva `#row=` de etiqueta «01.01.2026 · OTRO DEMO» y
  el botón «Triage»: los pasos que salen de `InstanciarSkill.Pasos` para una skill «DEMO → Triage»
  dan 0 hechos y pulsar no se llama; la cuenta dice que la fila la elige la persona y no nombra a la
  otra. `map_take` con «DEMO», por el mismo batch, sí la pulsa.
- (b) `PasoDelNucleo` con la arista aprendida L –fila→ F: `Hasta(F)` nunca pulsa un `#row=` y dice
  por qué; una ruta sin filas sigue llegando.

Lo que el contrato no puede juzgar y va al nivel 4: que SAP deje de verdad la fila fuera al comprobar
una lección real, que la etiqueta de la fila grabada traiga `#row=` en su selector, y que el piloto
obedezca la frase nueva del catálogo.

## Las fases

### Fase 1 — la skill empieza donde la persona ya eligió (254)

| | |
|---|---|
| **Promesa que pone verde** | 254 |
| **Qué toca** | `Piloto/SkillDeLoVerificado.cs` (`ElRegistroQueAbreLaPersona`, el corte), `Navigation/SkillEnsenada.cs` (`EntradaDeLaPersona`, `SeAbreCon`; `SkillAnunciada.EmpiezaEnElRegistro`), `Mcp/SurfaceMapTools.AnuncioDeLasSkills`, `Piloto/MensajeDelEncargo.cs`, `agente-piloto/piloto.mjs` |
| **¿Núcleo congelado?** | no |
| **Terminado** | 254 verde, 1..253 intactas |
| **Sitios con esta clase de error** | 2 empaquetan skills: `SkillDeLoVerificado` (la que corre) y `WorkflowTeachSession` (la provisional, que no corre: ver «Lo que NO entra») |

### Fase 2 — solo corre desde el registro abierto (255)

| | |
|---|---|
| **Promesa que pone verde** | 255 |
| **Qué toca** | `SkillEnsenada.PuedeEmpezarEn`, `SurfaceMapTools.CorrerSkill` (+ `DondeTrabaja`), `FaceWindow.MostrarAprendizajeAsync`, `ConsultaWindow` (la frase bajo «Mostrar») |
| **¿Núcleo congelado?** | no |
| **Terminado** | 255 verde, 1..254 intactas |
| **Sitios con esta clase de error** | 2 corren skills (`InstanciarSkill.Pasos` tiene 2 llamadas de producción) |

### Fase 3 — una fila no se toma sola (256)

| | |
|---|---|
| **Promesa que pone verde** | 256 |
| **Qué toca** | `RecorrerSegunElNucleo` (`Paso.DeUnaTarea`, `EsUnRegistro`), `InstanciarSkill.Pasos`, `PasoDelNucleo` (`EsUnRegistro`), `ServidorDelNucleo`, el cableado en `FaceWindow` |
| **¿Núcleo congelado?** | no: el grafo puro (`nucleo/`) no se toca; el filtro vive en quien pulsa |
| **Terminado** | 256 verde, 1..255 intactas; `contrato-de-la-voz.ps1` íntegro |
| **Sitios con esta clase de error** | 1 batch de producción + 2 construcciones de `PasoDelNucleo` (ir-a de la voz y del piloto, e ir del visor) |

## Sabotaje (se anota abajo, en «Primera tanda», con lo que salió)

- 254: el índice de corte a 0.
- 255: la primera línea de `PuedeEmpezarEn` devuelve «sí».
- 256: uno por sitio — el batch sin su comprobación, y `PasoDelNucleo` sin la suya.

## Lo que NO entra

- **Reescribir las skills que ya están en disco.** Nacieron con la fila dentro. No se tocan: correrlas
  para en la fila (256) diciendo por qué, y volver a comprobar su lección las regenera sin ella (la
  verificada reemplaza a la de su lección, 228).
- **La skill provisional de la demo** (`WorkflowTeachSession.EmpaquetarLaSkill`). No se corta: no se
  puede correr sin comprobar (127), se reemplaza al comprobar (228), y su panel no enseña la etiqueta
  de la fila porque su `Exit` es el selector crudo («Toca un elemento de la pantalla», 199).
- **Saber quién es el paciente de la consulta.** El `patient_id` sigue nulo; casar la consulta con el
  registro abierto en SAP es otra spec. Aquí la garantía es más simple: Ü nunca elige.
- **La demo 🚀** (`OnDemoPuntaAPunta`) pulsa la primera fila por `map_batch`. Es un botón de
  demostración que la persona pulsa a propósito, no una skill; se queda como está.
- **La comprobación** sigue pulsando la fila de la demo al repasar la lección (`leccion_plan` y
  `map_take` del piloto): repasa lo que la persona hizo, con la persona mirando, en QAS.
- **Filtrar las rutas en el grafo** (`Grafo.ComoLlego`): el núcleo puro no sabe qué es una fila. Si la
  ruta más corta pasa por una fila, ir-a para ahí; no busca otra.
- **Filas de árbol** (`#node=`): son navegación (una rama, una vista), no registros.

## Nivel 4 pendiente (lo ve el dueño en SAP real)

1. Comprobar de nuevo una lección del triage que elija paciente: en el log, la skill guardada con
   `empieza con el registro abierto` y sin el paso de la fila; en el panel, ninguna línea con el
   nombre del paciente de la demo.
2. Con la lista de pacientes delante (sin abrir el triage), pulsar ✓: la cuenta dice que abras el
   registro («Triage») y SAP no se mueve.
3. Con el triage de un paciente abierto, pulsar ✓: escribe en ESE triage.
4. Una skill vieja (de antes de esta spec) con la fila dentro: al correrla, para en la fila diciendo
   que la fila la elige la persona.

## Hallazgos

## Cierre

- [ ] Todas las promesas verdes (`.\scripts\contrato-del-grafo.ps1` → CONTRATO INTACTO)
- [ ] `.\scripts\contrato-de-la-voz.ps1` íntegro
- [ ] Probado en SAP real (nivel 4), con horas
- [ ] Estado de este documento: **implementado**
