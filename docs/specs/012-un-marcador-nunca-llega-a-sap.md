# Plan de implementación: un marcador nunca llega a SAP, y el log no lleva datos del paciente

Estado: **propuesto** · Nace de la auditoría de privacidad del 2026-09-07 (tres repos) · Rama: `jose/un-marcador-nunca-llega-a-sap`

> Contexto. El 2026-09-07 Graph estrenó el **escudo de privacidad** hacia los proveedores de IA
> (`docs/privacy-egress-gateway.md` en Graph, D21 en el portal): la nota y los valores de los
> campos SAP salen hacia el modelo con marcadores tipados (`[PACIENTE_NOMBRE_1]`, `[DOCUMENTO_1]`)
> y Graph devuelve los `matches` ya rehidratados con los datos reales. **Este repo no tiene que
> tapar nada** — la frontera está en el servidor —, pero sí tiene que cumplir dos cosas para que
> el claim sea verdad de punta a punta: no escribir jamás un marcador en la historia clínica, y
> no reenviar a Graph (por el espejo del log) lo que el escudo acaba de tapar.
>
> Esta spec la escribió una sesión que no corre en Windows y por eso **no toca código** (regla
> `solo-mac.md`): deja las promesas escritas para una rama abierta desde Windows, con la compuerta
> de cuatro niveles.

## Diagnóstico: qué se midió

Sobre `main` en `1c2af36` (2026-09-07), leyendo el código; ninguna corrida sobre SAP en esta
sesión.

| Qué | Medida | Fuente |
|---|---|---|
| El ejecutor consume la nota firmada entera y la manda a Graph con los valores de hasta 80 campos SAP en pantalla | `payload.context` → `RellenadorSap._nota`; `/api/v1/pipeline` con `note.content`, `fields[].currentValue`, `transcript` en dictado | `Clinical/EjecutorDeExportaciones.cs:189-194` · `Clinical/RellenadorSap.cs:200-212, 253-265, 331-343` |
| No manda ningún identificador de consulta a Graph: el escudo siembra solo desde la nota | `session_id = _sesion`; no viaja `export.id` | `RellenadorSap.cs:253-265` |
| Lo que vuelve se escribe en SAP sin mirarlo | `valor.Length != 0` es la única guarda; `node.Text = step.Value` | `RellenadorSap.cs:357-362` · `windows-graph/src/Surfaces/SapGuiSurface.cs:2024-2065` |
| Un marcador que llegara se escribiría, se releería no vacío y se reportaría `ok` | relectura por `LeerAhora`; `outcome: "ok"` marca la consulta exportada | `RellenadorSap.cs:402-410` · `EjecutorDeExportaciones.cs:250` |
| El log escribe el valor puesto en cada campo, y el log entero se espeja a Graph | `«{campo.Label}» = «{despues}» (pedido «{valor}»)`; `EspejoDelLog` reenvía toda línea a `/api/v1/agent/events` | `RellenadorSap.cs:405, 409` · `Telemetry/EspejoDelLog.cs:59-71` · también `ConversacionEnVivo.cs:1310-1311`, `FaceWindow.xaml.cs:309, 322, 335, 675`, `Mcp/SurfaceMapTools.cs:2188` |
| El puente consciente manda la pantalla completa y los valores SAP a OpenAI/Gemini | `Screenshotter.CaptureBase64Png` (pantalla entera) + `SapContextReader.Read()` (80 campos con valor) | `Agent/AgentLoop.cs:194-224` · `Capture/Screenshotter.cs:50-62` · `Uia/SapContextReader.cs:31-38` |
| Ya existe la captura por ventana, y no se usa en el camino clínico | `CaptureVentanaBase64Png(hwnd)` | `Capture/Screenshotter.cs:29` (la usan `SurfaceMapTools.cs:460, 2048`) |
| Graph ya descarta valores con marcador antes de responder | `NoteFieldMatcher.normalizeResult` y `DynamicValueResolver.resolve` (Graph, 2026-09-07) | Graph `docs/privacy-egress-gateway.md` |

## Por qué esto va dirigido por especificación

Porque las dos cosas que pueden fallar lo hacen **en silencio y pareciendo éxito** —el patrón nº10
de este repo—: un marcador escrito en SAP se relee no vacío y se reporta `ok`, y un valor en el log
se espeja a Graph sin que nadie lo vea. Una prueba escrita después del código se escribiría para
que pasara. Cada promesa se juzga sin pantalla, sobre funciones puras.

**La regla del flujo, y no tiene excepciones:** ninguna línea de producción entra antes que la
promesa que la juzga. Cada fase empieza con el contrato ROTO y termina con el contrato INTACTO.

## La especificación

En continuación de la 170. Los números no se reciclan.

| # | Promesa | Fase que la pone verde |
|---|---|---|
| 171 | el envío a Graph lleva la consulta: `/api/v1/pipeline` sale con `consultation_id` igual al `export.id` reclamado, y en dictado sin exportación no lleva ninguno | 1 |
| 172 | un valor con forma de marcador (`[PACIENTE_NOMBRE_1]`, `[DOCUMENTO_1_2]`, con o sin corchetes en mayúsculas) no se escribe en SAP: el campo queda sin llenar y el trabajo termina `needs_doctor` nombrando la etiqueta | 2 |
| 173 | el log del rellenador dice etiqueta y longitud, nunca el valor: ninguna línea anotada al escribir un campo contiene lo que se escribió | 3 |
| 174 | mientras hay una exportación reclamada, el puente consciente captura la ventana de SAP o nada, nunca la pantalla completa; la decisión es una función pura de (hay exportación reclamada, política configurada) | 4 |

La que cierra el asunto es la **172**: mientras no exista, «Graph ya lo descarta» es una promesa
de otro repo que este no verifica.

### Con qué se juzga cada una

| # | Cómo se juzga, sin pantalla |
|---|---|
| 171 | cuerpo construido por `RellenadorSap` para un trabajo con `export.id = X` → `consultation_id == X`; para un dictado sin trabajo → sin la clave. Mapa a mano en la prueba, como las promesas 1-10 |
| 172 | `Privacidad.EsPlaceholder` sobre un fixture de 20 cadenas: `[PACIENTE_NOMBRE_1]`, `[documento 2]`, `DOCUMENTO_1_2`, `**[PATIENT_NAME_1]**` → sí; `Juan Pérez`, `1023456789`, `el documento 1 dice`, `Documento_1` (mezcla de mayúsculas sin corchetes) → no. Y `Escribir` con un valor que es marcador → no llama a la superficie, deja la etiqueta en `unresolved_fields`, y el resultado del lote es `needs_doctor` |
| 173 | un sumidero falso de `LogBus` captura las líneas de escribir tres campos con valores conocidos; ninguna línea contiene ninguno de los tres valores; todas contienen la etiqueta y una longitud |
| 174 | `PoliticaDeCaptura.Decidir(hayExportacionReclamada: true, permitida: "pantalla")` → `ventana`; `(false, "pantalla")` → `pantalla`; `(true, "ninguna")` → `ninguna` |

La gramática de los marcadores es la de Graph (`src/domain/privacy/tokens.js`): con corchetes,
tolerante a mayúsculas, espacios, guiones y traducciones (`PATIENT_NAME`); sin corchetes, solo la
forma exacta en mayúsculas con guion bajo. El fixture de la 172 se copia de ese archivo para que
los dos lados juzguen lo mismo.

## Las fases

| Fase | Promesa | Qué toca | ¿Núcleo congelado? |
|---|---|---|---|
| 0 | las cuatro en rojo | `tests/ContratoDelGrafo/Contrato.cs` | no |
| 1 | 171 | `Clinical/RellenadorSap.cs` (el cuerpo del pipeline recibe el id del trabajo desde `EjecutorDeExportaciones`) | no |
| 2 | 172 | `Clinical/Privacidad.cs` (nuevo, puro: `EsPlaceholder`) · `RellenadorSap.Escribir` (la guarda antes de la superficie) · `EjecutorDeExportaciones` (`needs_doctor` con `unresolved_fields`) | no |
| 3 | 173 | `RellenadorSap.cs:405, 409` y los otros cinco sitios contados arriba (**6 sitios**, contados con grep, no de memoria) | no |
| 4 | 174 | `Agent/PoliticaDeCaptura.cs` (nuevo, puro) · `AgentLoop.ReadStateAsync` (elige `CaptureVentanaBase64Png` según la política) · `Config` (la política, por defecto `ventana` cuando hay exportación) | no |

Nivel 4, a mano y con log: una exportación real contra QAS con un `match` forzado a
`[PACIENTE_NOMBRE_1]` (Provider Studio apuntado al proveedor falso de Graph, o un breakpoint) →
el campo queda vacío, el trabajo vuelve `needs_doctor`, SAP no tiene el marcador, y el log del día
no contiene el nombre del paciente. En ≥2 pantallas, con nombre: el triage (`SAPLY000`) y la
admisión (`NV2000`).

## Lo que NO entra

- **Tapar nada aquí.** La frontera es Graph; duplicarla en el cliente sería dos jueces que
  discrepan (aprendizaje nº7 del CLAUDE.md).
- **La clave de la voz en vivo.** `ConversacionEnVivo.cs:263-268` reparte una `OPENAI_API_KEY` de
  alcance completo por instalación. Es deuda conocida y necesita primero un endpoint de token
  efímero en Graph, como ya tiene la dictación; va en su propia spec.
- **El video de enseñanza y la nube de Omi.** Fuera del flujo de la nota; decisiones de
  cumplimiento, no técnicas (`docs/omi-como-microfono.md:184-186`).

## Hallazgos

- 2026-09-07 · El emparejamiento por código de ocho caracteres (`CLAUDE.md` §*El puente con el
  portal clínico*) no existe en este cliente y fue sustituido por el pull de exportaciones
  (`EjecutorDeExportaciones.cs:21-24`). La ruta `GET /api/agent/values` del portal sigue viva, sin
  sesión y con enlaces que expiran a 10 años. Se anotó como hallazgo en el portal; aquí solo hay
  que corregir el `CLAUDE.md` cuando esta spec se implemente.

## Cierre

- [ ] Todas las promesas verdes (`.\scripts\contrato-del-grafo.ps1` → CONTRATO INTACTO)
- [ ] `.\scripts\verificar.ps1` pasa, con evidencia en `out\evidencia.md`
- [ ] Probado en ≥2 pantallas, con nombre: …
- [ ] Estado de este documento: **implementado** (AAAA-MM-DD)
