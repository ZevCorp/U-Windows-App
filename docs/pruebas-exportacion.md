# Pruebas del ejecutor de exportaciones

Cuatro niveles. Los dos primeros corren en cualquier máquina con Node y **no necesitan SAP, ni
Graph, ni Supabase, ni pacientes**. Los dos últimos necesitan Windows y, el último, el hospital.

## 1. El contrato-espejo contra el payload real de Graph

```bash
node scripts/verify-export-contract-mirror.js          # --graph <ruta al repo Graph> si no es ../Graph
```

Importa el constructor de payloads de Graph, lo alimenta con una consulta sintética y comprueba que
las anotaciones `[JsonPropertyName]` del C# cubren todas las claves que Graph emite.

Existe porque un contrato-espejo escrito a mano se desincroniza **en silencio**: un campo que Graph
manda y el cliente no declara no da error de compilación ni excepción — `System.Text.Json` lo ignora
y el dato simplemente no llega. Ya encontró tres campos ausentes en la primera versión del ejecutor
(`especialidad`, `servicio`, `fecha`), que además el propio `note-export-contract.md` no documenta.

Verifica además invariantes que el ejecutor da por ciertas: que solo viajan códigos aceptados, que
`patient_ref` es el uuid, que no hay nombre ni documento, y que `context == rendered_text`.

**Estado: pasa** (10/10 campos, 5/5 invariantes).

## 2. Conformidad contra un Graph falso

```bash
node scripts/fake-graph-exports.js --self-test         # valida el arnés
node scripts/fake-graph-exports.js --scenario ok       # levanta el banco de pruebas
```

Un servidor que implementa el carril con las reglas reales de Graph (leídas de la RPC y las rutas):
claim FIFO con `attempts` ya incrementado, 204 en cola vacía, `claimed_by` exacto o 409, lease
vencido, ack idempotente que devuelve el estado **viejo** y `consultation_exported:false` siempre.

Observa al cliente y dicta un veredicto: si mandó la API key, si la identidad del claim y la del
result coinciden, si el outcome pertenece al contrato, si reintentó hasta el ack, si **no devolvió
contenido clínico** en los campos de diagnóstico, y si las etiquetas de `unresolved_fields` son
legibles para un médico en vez de ids técnicos.

`--self-test` corre el **simulador de referencia de Graph** contra el arnés y exige que salga
conforme: es lo que impide que este banco mida con una vara inventada.

**Estado: pasa** (escenarios `ok`, `flaky` y `empty`; el cliente de referencia reintenta 3 veces en
`flaky`, como exige el contrato).

Escenarios disponibles: `ok`, `empty`, `flaky` (dos 503 antes del ack), `not-owned` (409),
`lease-expired` (409), `double` (sirve el mismo trabajo dos veces, para ver si el cliente lo
reejecuta a ciegas o pide intervención).

### Cómo usarlo con el cliente real

En el equipo Windows, antes de abrir `U.exe`:

```powershell
$env:GRAPH_BASE_URL = "http://127.0.0.1:8787"
$env:GRAPH_API_KEY  = "miracle_arnes_local_no_es_un_secreto"
```

Encender *Atender exportaciones*, dejar que procese, y Ctrl+C en el arnés para ver el veredicto.

## 3. Unitarias de la lógica de decisión

```powershell
cd windows-graph-tests
dotnet test
```

Cubren las tres piezas que deciden sin tocar nada — por eso se escribieron sin dependencias de COM,
red ni interfaz:

- **`SapSaveSignalTests`** — cada prueba es una forma concreta de reportar un éxito falso: sin
  mensaje, mensaje idéntico al previo, advertencia, información, aborto. Más que el texto de un
  mensaje de SAP (que puede nombrar al paciente) no se filtre en lo que se devuelve a Graph.
- **`ExportJobReviewTests`** — ejecución anterior incierta, sin workflow, payload purgado, sin hash
  de firma, `attempts` alto, lease vencido o corto, y que el resumen para quien aprueba no lleve el
  texto clínico.
- **`ExportJournalTests`** — corte en cada fase, reenvío del resultado pendiente, que la identidad
  del claim se conserve, que la fase no retroceda, y que un archivo corrupto no rompa el arranque.
- **`PatientGuardTests`** — extracción del nombre técnico ABAP y comparación con relleno de ceros.
  La última prueba fija por escrito que el contrato **no** transporta hoy un identificador del
  paciente utilizable en el hospital: cuando Graph lo incluya, esa prueba fallará y llevará a
  `HisPatientIdFrom`, que es el punto exacto donde enchufarlo.

> **No se ejecutaron al escribirlas.** El entorno donde se desarrolló esto es Linux y no tiene el SDK
> de .NET; el proyecto es `net8.0-windows` con WPF y no compila fuera de Windows. Hay que correrlas
> en el equipo de desarrollo antes de dar por buena la entrega.

## 4. Contra SAP real (QAS)

No hay atajo: la lectura de la barra de estado, la carrera del `Busy` y la geometría de los dynpros
solo se comportan igual contra el SAP del hospital. Antes de usarlo con pacientes reales:

1. Grabar un workflow de exportación **cuyo último paso sea el guardado**, y comprobar con 🧪 Ensayo
   en seco que el plan llega hasta ahí.
2. Ejecutarlo con `PatientPolicy: "Off"` en QAS con un paciente ficticio, y verificar en el log que
   aparece `barra de estado: tipo='S'` y que el folio sale del mensaje.
3. Repetir con `OperatorConfirms` y comprobar que la ventana enseña los campos de identidad
   correctos. Si sale «No se reconoció ningún campo», añadir el nombre técnico a `PatientFieldNames`.
4. Provocar los desenlaces malos: cerrar `U.exe` a media escritura (debe quedar incierto y pedir
   intervención al volver a servirse), y cortar la red antes del result (debe reenviar al arrancar).

### Lo que ninguna prueba de aquí cubre

- **Que el paciente sea el correcto.** No es cubrible por prueba automática mientras no exista un
  identificador compartido — es una limitación del sistema, no de la suite.
- **El workflow contra el dynpro real**: `WorkflowPlayer` y `SapGuiSurface` necesitan SAP.
- **El recorrido completo desde el navegador del médico**: exige un Supabase de staging, que no
  existe (ver `note-export-contract.md` del repo Graph).
- **Dos ejecutores contra el mismo Graph real**: el `SKIP LOCKED` está probado del lado de Graph
  (`test:note-export-db`, 8 ejecutores), pero no con dos `U.exe` reales.
