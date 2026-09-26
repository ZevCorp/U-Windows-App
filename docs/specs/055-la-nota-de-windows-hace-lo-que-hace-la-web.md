# La nota de Windows hace lo que hace la de la web

Estado: **en curso** · Nace de la petición del dueño del 2026-09-25 · Rama: `claude/admiring-brahmagupta-fghedd`
(asignada por la sesión). Promesas 450-465; la 440 se retira (ver *Lo que cambia del contrato*).

## Qué se pidió, en sus palabras

«Que Miracle Windows tenga las funcionalidades principales» de Miracle Notes web; «ajustar la nota,
que se pueda en voz alta también»; «conservar que cada que termina de grabar una consulta aparezcan
los avisos de qué cosas están mal, se pueden mejorar o qué faltó»; «que también se pueda modificar
el texto dentro de la nota que tenga los atajos»; «que tanto en U como en Notes funcione con la
lógica de que el médico escoge la plantilla predeterminada»; continuidad en los dos sentidos. Y dos
exclusiones: **la firma, desactivada del todo** («con iniciar sesión el médico se hace
responsable»), y **la experiencia distinta**: Windows más ligero y más rápido.

## Diagnóstico: qué se midió

Sobre `main` + la rama de Jose integrada (spec 053), el 2026-09-26, leyendo los dos repos.

| Qué | Web | Windows hoy | Fuente |
|---|---|---|---|
| Avisos al terminar | `reviewGeneratedNote()`: 10 reglas deterministas, sin IA, en el navegador; orden de lectura propio; «Ver N más» | La tarjeta «Avisos» con `note_json.warnings` — lo que el modelo dijera esa vez | `lib/clinical/note-review.ts:173` · `ConsultaWindow.PintarSecciones` |
| Ajustar con IA | `POST /api/clinical/assistant/note-adjustment`; la propuesta NO se guarda sola | no existe | `lib/api/clinical.ts:986` |
| Ajustar por voz | `parseVoiceInstruction`: literal (local, sin red) · dictado (`dictation`) · ajuste (`rewrite`) | no existe (sí hay `DictadoEnVivo`) | `lib/clinical/voice-instruction.ts:83` |
| Atajos «/» | tabla `user_snippets`; `slashQueryAt`, `filterSnippets`, huecos `___`/`[x]` con Tab | no existe | `lib/clinical/snippets.ts` |
| Plantilla | `pickPreselectedTemplate`: modo `fixed`/`last`/`manual` | cadena de Jose: elegida → sugerida → urgencias → abierta | `template-preferences.ts:151` · `ReglaDeLaPlantilla` |
| Copiar la nota | `noteAsPlainText`: resumen, secciones y el cierre | no existe | `lib/clinical/note-plain-text.ts` (extraído hoy) |
| **Corregir una sección** | manda el `note_json` ENTERO | manda `summary`, `key`+`content` y `warnings`: **se deja el cierre** (plan, medicamentos, recomendaciones, signos de alarma) y la confianza de cada sección | `ClinicaClient.CuerpoDeNotaEditada` |
| Abrir una consulta de la web | — | lee la nota de Graph; las ediciones del detalle web viven en `consultations.note` y no se ven | `AbrirConsultaAsync` · `providers.tsx:567` |
| Lista de consultas | paciente, estado, motivo | fecha, estado, motivo — sin paciente | `EspejoDeConsulta.UltimasAsync` |
| Paciente | buscar en `patients`, asociar con `PATCH /encounters/:id/patient` | no existe | `lib/api/clinical.ts:827` |
| Firma | `estado=aprobada`, hash | no firma (y así se queda) | `consultas/actions.ts` |

**La fila que más pesa es la de «corregir una sección».** No es una función que falte: es un daño
silencioso en una que ya existe. Si el backend reemplaza `note_json` con lo que recibe, corregir una
coma desde Windows borra el plan terapéutico que la web enseña en «Plan y egreso».

## La decisión de fondo: los vectores de la web

«Funciona igual que la web» es una frase hasta que alguien ejecuta la web. Por eso las promesas
450-452, 455, 456, 458 y 460 no copian casos a mano: `tests/ContratoDelGrafo/bronce/generar-desde-la-web.mts`
ejecuta el **TypeScript real** del portal sobre 152 casos (avisos, vitales, voz, atajos, plantilla,
texto plano) y congela la salida en `miracle-notes-web.json`. El contrato exige al puerto en C# la
misma salida, carácter a carácter. Si la web cambia una regla, se regenera y el contrato dice qué
dejó de cuadrar. El JSON lleva el commit de la web del que salió.

Dos trampas de portar regex de JS a .NET que los vectores vigilan: `\b` en JS solo conoce letras
ASCII y en .NET todas (se usa `RegexOptions.ECMAScript` donde hay `\b`), y `Math.round` redondea
hacia arriba en el .5 mientras `Math.Round` de .NET redondea al par.

## La experiencia de Windows (lo que NO se copia de la web)

- **Todo ajuste es una propuesta con un solo gesto para aceptarla.** Escrito, por voz o literal: las
  secciones cambiadas se marcan, arriba una banda «Ajuste propuesto · Guardar (Ctrl+S) · Descartar».
  Es la misma regla de la web (la propuesta no se guarda sola) con menos pasos.
- **Los avisos arriba de la nota**, como en la web, pero plegados a 3 y sin pestaña de auditoría.
- **El micrófono de cada sección** usa el dictado que ya tiene Windows (`DictadoEnVivo`, el mismo
  proveedor que la consulta), no el del navegador: la web usa `webkitSpeechRecognition` porque es lo
  que hay en un navegador.
- **La plantilla se elige sola y se cambia en una línea** debajo del botón de grabar; la estrella la
  deja como predeterminada.
- **Sin firma, sin PDF, sin CIE-10**: la nota queda en `borrador`, como en la web sin firmar.
  CIE-10/CUPS en la web es hoy un catálogo de demo de 31 códigos sin sugerencias; se porta cuando
  sea un catálogo real servido.

## Las promesas

| # | Promesa | Fase |
|---|---|---|
| 450 | los avisos al terminar son los de la web: ante la misma nota, plantilla y transcripción, los mismos hallazgos —clave, severidad, título y detalle— en el mismo orden, con los mismos conteos, el mismo puntaje, el mismo reparto entre visibles y plegados y la misma etiqueta | 1 |
| 451 | los signos vitales y el motivo se leen de la nota como en la web: las mismas claves, los mismos valores y la misma evidencia | 1 |
| 452 | lo que el médico dice por voz a una sección se entiende como en la web —literal, dictado o ajuste, con el mismo texto— y aplicar un literal deja la sección igual que la web | 2 |
| 453 | un literal dicho por voz se aplica SIN llamar a la red; un dictado va a `note-adjustment` con `instruction_kind: dictation` y la sección, un ajuste con `rewrite` y la frase envuelta como la web; y ninguno de los tres guarda la nota solo | 2 |
| 454 | pedir un ajuste escrito manda la instrucción sin sección y con `rewrite`; la propuesta que vuelve se aplica sobre la nota que se ve, y si el backend no cambió ninguna sección la nota queda intacta y se dice por qué | 2 |
| 455 | el «/», los huecos y la inserción de un atajo funcionan como en la web: el mismo disparo bajo el cursor, los mismos huecos, el mismo siguiente hueco y la misma inserción con su selección | 3 |
| 456 | los atajos se buscan y se ordenan como en la web: la misma normalización del texto y el mismo orden para cada búsqueda y cada sección | 3 |
| 457 | los atajos son los del médico: se leen de `user_snippets` con su token y la clave pública, sin mandar su id, y un atajo que no se puede leer no tumba la nota | 3 |
| 458 | la plantilla con la que se graba es la que elegiría la web —su predeterminada, la última o ninguna según el modo del médico, con la misma cadena después— y solo si la web no elige ninguna y el médico no pidió elegirla cada vez, se graba con la abierta | 4 |
| 459 | fijar la predeterminada desde Windows la hace mandar: además del pin, deja `template_start_mode = fixed` en las preferencias del médico, en la misma tabla que la web | 4 |
| 460 | copiar la nota da el mismo texto que «Copiar nota» de la web | 5 |
| 461 | corregir una sección o el resumen no borra nada de la nota: el cierre y todo campo que Windows no entiende viajan en el `PUT` tal cual, y solo cambia lo corregido | 5 |
| 462 | una consulta abierta desde la lista enseña lo que enseña el portal: si su nota se corrigió en la web, esa versión manda sobre la de Graph, sección por sección y en el resumen | 6 |
| 463 | Windows no firma: ningún cuerpo que manda al portal lleva un estado de firmada o exportada, ni una firma | 6 |
| 464 | el paciente se busca en `patients` del médico por nombre o documento, sin que lo escrito pueda romper la consulta a la base, y asociarlo manda `PATCH /encounters/:id/patient` con su id | 7 |
| 465 | la lista de consultas dice de quién es cada una: se piden el nombre y el documento del paciente, y la fila los enseña antes que el motivo | 6 |

**La que cierra el asunto es la 461.** Todas las demás añaden; esta impide que lo que ya existe
destruya datos clínicos al usarse.

### Con qué se juzga cada una

| # | Cómo, sin pantalla ni red |
|---|---|
| 450-452, 455, 456, 458, 460 | los vectores de la web (`bronce/miracle-notes-web.json`, embebido en el contrato) contra el puerto |
| 453, 454 | `BackendDeMentira` anota las peticiones: literal → 0; dictado/ajuste → 1 `POST` con el cuerpo esperado y **ningún** `PUT` |
| 457 | `BackendDeMentira`: `GET /rest/v1/user_snippets?select=…` con `apikey` y `Bearer`, sin `user_id`; un 500 devuelve lista vacía |
| 459 | el cuerpo del upsert a `user_preferences`: `template_start_mode: "fixed"`, sin `user_id` |
| 461 | nota con cierre, confianza y un campo desconocido → corregir una sección → el cuerpo del `PUT` los trae idénticos |
| 462 | nota de Graph + fila de `consultations` con una sección y el resumen corregidos en la web → la nota fusionada trae los textos de la web |
| 463 | `EspejoDeConsulta.Fila` y `FilaDeCorreccion`: ninguno lleva `aprobada`/`exportada` ni `firma` distinta de null |
| 464 | una búsqueda con `,()*"` no cambia la forma de la consulta; asociar → `PATCH` con `{"patient_id": …}` |
| 465 | la ruta de la lista pide `paciente_nombre,paciente_documento`, y una fila los lee |

Nivel 4, a mano, en el PC real: grabar → avisos; editar y ver el cambio en la web; abrir en Windows
una consulta corregida en la web; ajuste escrito y por voz; atajo «/» con huecos; paciente.

## Lo que cambia del contrato

- **Se retira la 440** («la plantilla sale de una cadena de orden fijo —lo que el médico eligió, su
  sugerida, la de urgencias, la abierta—»). La sustituye la 458. Lo pidió el dueño el 2026-09-26:
  «que tanto en U como en Notes funcione con la lógica de que el médico escoge la plantilla
  predeterminada». La de urgencias no se pierde para el hospital: un médico de urgencias tiene
  `specialty_code` de urgencias y la cadena de la web le da la suya. El número 440 no se recicla.

## Las fases

| Fase | Promesas | Qué toca |
|---|---|---|
| 0 | 450-465 en rojo | `Contrato.cs`, `bronce/*` |
| 1 | 450, 451 | `Clinical/RevisionDeLaNota.cs`, `Clinical/ConceptosClinicos.cs` (nuevos, puros) |
| 2 | 452-454 | `Clinical/InstruccionDeVoz.cs`, `Clinical/AjusteDeLaNota.cs`, `ClinicaClient.AjustarNotaAsync` |
| 3 | 455-457 | `Clinical/AtajosDeTexto.cs`, `Clinical/AtajosDelMedico.cs` |
| 4 | 458, 459 | `Clinical/PlantillaPredeterminada.cs`, `Clinical/PreferenciasDelMedico.cs` |
| 5 | 460, 461 | `Clinical/TextoDeLaNota.cs`, `NotaClinica.Crudo` + `CuerpoDeNotaEditada` |
| 6 | 462, 463, 465 | `Clinical/NotaDelPortal.cs`, `EspejoDeConsulta` |
| 7 | 464 | `Clinical/PacientesDelMedico.cs`, `ClinicaClient.AsociarPacienteAsync` |
| 8 | nivel 4 | `ConsultaWindow`: avisos, barra de ajuste, micrófono por sección, «/» en el editor, banda de propuesta, copiar, paciente, lista |

## Lo que queda fuera

- **Firma** — desactivada por decisión del dueño. Por qué importa, dicho aquí para contárselo al
  cerrar: sin firma la nota no se congela (el trigger de inmutabilidad solo actúa sobre `aprobada`),
  no admite adendas, no se puede exportar a la historia clínica por Graph (exige `aprobada` + hash) y
  sigue contando en «por revisar». Iniciar sesión dice QUIÉN escribió; la firma dice QUÉ versión
  aprobó y la hace inalterable.
- **CIE-10/CUPS, PDF, notas privadas, notas por sección, plan y egreso editables** — en la web.
- **Modo oscuro.**

## Hallazgos

1. **El PUT de Windows borraba el cierre de la nota** (`discharge`, `confidence` y todo campo que
   `NotaClinica` no modelaba). Llevaba así desde la spec 053: cada corrección hecha en Windows dejaba
   la nota más pobre que la de la web, sin error. Lo descubrió la promesa 461 al escribirse, antes del
   código. Arreglo: `NotaClinica.Crudo` viaja entero y solo se sustituye lo corregido.
2. **La estrella de la web no servía de nada con el modo por defecto.** La web arrancaba en «la
   última que usé», y en ese modo `pickPreselectedTemplate` ignora los pines: el médico fijaba su
   predeterminada y no pasaba nada. Arreglado en la web (rama `claude/admiring-brahmagupta-fghedd`,
   modo por defecto `fixed` y fijar deja el modo en `fixed`), y aquí la 459 lo exige.
   **La migración que cambia el por defecto de la columna NO está aplicada en producción.**
3. **Los avisos del backend se pintaban dos veces de forma distinta**: la tarjeta «Avisos» los listaba
   en crudo y la web los mete en la revisión con severidad. La tarjeta se retiró; entran en el panel.
4. **`ReglaDeLaPlantilla` quedó sin nadie que la llame** al retirarse la 440 (la sustituye la 458).
   Se borró en vez de dejarla viva al lado de la nueva cadena (patrón nº6).

## La fase 8, en la pantalla

- **Arriba de la nota**: «Nota clínica» con el estado del portal, *Copiar nota* (el texto de la 460) y
  *Abrir en Miracle web* (`MIRACLE_PORTAL_URL`, por defecto `https://itsmiracleai.com.co`,
  `/app/consultas/{id}`).
- **El paciente**: buscar por nombre o documento y asociar (464); se ven alergias en ámbar,
  antecedentes y medicamentos.
- **Los avisos** (450) con los textos exactos de la web, recalculados cada vez que se guarda algo.
  La plantilla congelada y la transcripción se traen detrás: la nota se pinta primero.
- **Ajustar**: una barra (escribir + Enter, o dictar la instrucción a la barra y revisarla) y un
  micrófono en cada sección (452/453). Todo llega como **una propuesta**: se marcan las secciones que
  cambió, la nota no se edita mientras tanto, y se acepta con *Guardar* o **Ctrl+S**, o se descarta.
- **«/» en el editor** (455-457): la lista sale bajo el cursor; flechas, Enter o Tab insertan; Tab
  salta de hueco en hueco. Lo insertado es texto normal y se edita como cualquier otro.
- **Plantilla** (458/459): la cadena de la web con el modo del médico; en «manual», grabar abre el
  selector; la estrella fija la predeterminada en las dos apps; la última usada se anota al grabar.
- **La lista** enseña el paciente; abrir una consulta trae la versión del portal si se corrigió allí
  (462) y su paciente.

## Cierre

- [x] 450-465 verdes en Linux salvo la 447, que necesita WPF (2026-09-26). Sabotaje 16/16
      comprobado, y el contrato entero sin ninguna roja nueva frente a la base.
- [ ] 447 en Windows (`contrato-del-grafo.ps1`).
- [ ] Nivel 4 en el PC real, con log y horas: grabar → parar → avisos; corregir una sección y verla
      en la web; ajuste escrito; ajuste por voz (literal, «agrega que…» y una instrucción); «/» con
      un atajo que tenga huecos; asociar paciente; abrir una consulta corregida en la web; fijar la
      estrella y ver que la web arranca con ella; capturas lado a lado con la web.
