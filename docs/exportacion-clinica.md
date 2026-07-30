# El ejecutor de exportaciones clínicas

Cómo Miracle Operations toma una nota firmada de la cola de Graph y la escribe en SAP.
Contrato del lado servidor: `docs/note-export-contract.md` del repo Graph.

## Qué recorre

```
Graph  ──POST /api/v1/operations/exports/claim──►  reclama (FIFO, lease, attempts)
                                                        │
                                        ExportJobReview  │  ¿el trabajo es ejecutable?
                                          PatientGuard   │  ¿es el paciente correcto?
                                        WorkflowPlayer   │  escribe en SAP
                                         SapSaveSignal   │  ¿SAP lo confirmó?
                                                        ▼
       ◄──POST /api/v1/operations/exports/:id/result──  reporta hasta el ack
```

Una consulta pasa a `exportada` **solo** cuando aquí se reporta `ok`, y `ok` **solo** sale de un
mensaje tipo **S** en la barra de estado de SAP, distinto del que ya había en pantalla. Que el
workflow termine sin fallos no es la señal.

## Encenderlo

Panel **Backend** de la carita → **Exportar a historia clínica** → *Atender exportaciones*.

Mientras está encendido, pregunta a Graph cada 15 s. No reclama si se está enseñando, si corre un
workflow a mano o si hay un ofrecimiento clínico en pantalla: reclamar sin poder ejecutar quema uno
de los tres intentos del trabajo y bloquea su lease diez minutos.

## Configuración

`%APPDATA%\U\graph.json` (mismo archivo que la URL y la API key de Graph):

| Clave | Default | Para qué |
|---|---|---|
| `PatientPolicy` | `OperatorConfirms` | `OperatorConfirms`: una persona confirma el paciente antes de escribir. `Off`: no se verifica (**solo QAS con datos ficticios**). Un valor desconocido cae en el default seguro. |
| `PatientFieldNames` | `[]` | Nombres técnicos ABAP extra que identifican al paciente en las pantallas de este hospital, sin el prefijo de estructura: de `RNPA1-PASSNR` va `PASSNR`. Se suman a los que ya conoce `PatientGuard`. |

Ninguna credencial nueva: se usa la `X-API-Key` de Graph que ya resuelve `GraphConfig`
(`graph.json` → `GRAPH_API_KEY` → key embebida en el build de distribución). **No hay secretos en
el repositorio.**

Del lado de Graph hace falta, y hoy no está confirmado (por eso la cola sigue con cero trabajos):

- `GRAPH_NOTE_EXPORT_WORKFLOW_ID` — el workflow de automatización. Sin él, crear una exportación
  responde `503 WORKFLOW_NOT_CONFIGURED` y el botón del médico no encola nada.
- La API key del ejecutor en `MIRACLE_API_KEYS`.
- `ALLOWED_ORIGINS` con el origen de Miracle Notes (afecta al navegador, no a este cliente).

Y hace falta **un workflow grabado cuyo último paso sea el guardado**: la señal que se verifica es la
que SAP publica al guardar. Un workflow que solo rellene campos nunca podrá reportar `ok`.

## Identidad del equipo

El `device` es `Environment.MachineName` y viaja **igual** en el claim y en el result. No es un
detalle: Graph compara ese string contra `claimed_by` de forma exacta, y si cambia devuelve
`409 EXPORT_NOT_OWNED`, deja el trabajo bloqueado hasta que venza el lease y quema un intento. Por eso
el resultado persistido guarda el device con el que se reclamó.

## La compuerta del paciente

**Hoy la máquina no puede verificar el paciente, y el ejecutor no finge que sí.**

El trabajo identifica al paciente con `patient_ref`, que es el uuid de `consultations.patient_id` en
Miracle. SAP no lo conoce. Y en el esquema de Miracle Notes no existe ningún identificador
institucional: solo `patients.documento` — texto libre, opcional, no único — que además no viaja en
el payload (Graph tiene una prueba que falla si aparece «nombre» o «documento» dentro).

La omisión es correcta: es minimización de PHI. Pero descansa sobre una suposición que el código de
Graph deja escrita — *«el ejecutor ya está dentro del contexto del paciente en el HIS»* — y que nadie
comprueba. Con una cola asíncrona esa suposición se rompe sola: un trabajo encolado hace veinte
minutos se ejecuta cuando SAP puede tener abierto otro paciente.

Lo que hace `PatientGuard` en su lugar:

1. **Lee** los campos de identidad de la pantalla (`PATNR`, `FALNR`, `PASSNR`, `GBDAT`, `NNAME`…) y
   los enseña. No concluye: si no reconoce ninguno, lo dice en la ventana en vez de callarse.
2. **Pregunta** a una persona, con lo que hay en SAP y de qué consulta es el trabajo al lado. Sin
   confirmación no se escribe: el resultado es `needs_doctor` y el médico ve «Requiere acción».
3. **Frena en seco** si algún día hay un identificador y *no coincide*. Ese camino ya está escrito y
   probado; ninguna política ni confirmación humana puede levantarlo.

**El cambio mínimo que lo automatiza** —y no se puede hacer desde este cliente— es que el payload de
la cola lleve el identificador del paciente en el HIS. El único punto a tocar aquí es
`PatientGuard.HisPatientIdFrom`; el resto de la compuerta ya funciona. Requiere que Miracle Notes
guarde ese identificador (hoy no existe en su esquema) y que Graph lo incluya en el snapshot.

## Reinicios, cortes y doble escritura

`ExportJournal` persiste en `%LOCALAPPDATA%\U\note-exports\` (un archivo por trabajo, escritura
atómica) la fase alcanzada: `Claimed → Executing → Verified → Reported`. Es lo único que sobrevive a
un cierre, y por tanto lo único que distingue «no se había empezado» de «pudo haber escrito».

| Qué pasó | Qué hace al arrancar |
|---|---|
| Corte **antes** de tocar SAP | Nada que deshacer. El trabajo se reejecuta con normalidad. |
| Corte **durante** la escritura | Se marca **incierto**. Si Graph vuelve a servirlo → `needs_doctor` con «Verificar si la nota ya se registró». **No se reejecuta.** |
| SAP confirmó, falta el ack | El resultado ya está en disco: **se reenvía** antes de reclamar nada nuevo. El endpoint es idempotente. |
| Graph rechaza un `ok` (409) | Se registra a gritos y el trabajo queda **incierto**: la nota ya está escrita y podría duplicarse. |

Reenviar un resultado **nunca** reejecuta el workflow: la reentrega parte del resultado guardado, no
del trabajo.

**Lo que no se puede resolver solo:** si otro equipo escribió la nota y murió sin reportar, este
equipo no tiene forma de saberlo — `attempts > 1` es la única pista y no distingue ese caso de un
reintento legítimo. Se avisa en la ventana de confirmación y decide la persona.

## Qué se registra y qué no

Al log local (`%LOCALAPPDATA%\U\logs\`) van los ids de los campos de identidad hallados, nunca sus
valores; el texto completo de los mensajes de SAP sí, porque puede hacer falta para diagnosticar y el
equipo ya opera SAP con esos datos a la vista.

Hacia Graph solo viajan `outcome`, un `error_code` tipado, un `detail_code` (el código del mensaje de
SAP, p. ej. `V1-311`, nunca su texto), el `folio` y etiquetas de campo. **Nunca contenido clínico.**
Lo comprueba `scripts/fake-graph-exports.js`.

Las etiquetas de `unresolved_fields` las pinta Miracle Notes literalmente dentro de la frase
«Quedaron campos sin completar en la historia clínica: …», así que van en español, cortas y legibles.
