# Plan de implementación: la plantilla se elige y la nota se corrige

Estado: **propuesto** · Nace de la petición del 2026-09-07 · Rama: `jose/la-nota-se-elige-y-se-corrige`

La petición del dueño, en sus palabras: *«terminar el sistema de notes aplicación en base a lo que ya
tenemos en notes navegador»* — que el médico **elija plantilla antes de grabar**, que pueda **entrar a
una consulta y editar** antes de exportar, y que **la nota recién generada también se pueda corregir**
(hoy no deja). Y una condición explícita: *«que si no se selecciona plantilla se haga con la
predeterminada, para urgencias en caso de que el médico no tenga una predeterminada por él mismo»*.

Lo que **se conserva y el portal no tiene**: la exportación **por secciones** con el ✓ y «Todo a SAP»
(spec 008). Eso no se toca: es la razón de ser de esta app.

## Diagnóstico: qué se midió

Todo el 2026-09-07, sobre los dos repos y sobre el log de una consulta real de esta máquina.

| Qué | Medida | Fuente |
|---|---|---|
| ¿Cómo elige plantilla el portal? | Cadena de 4 eslabones: **pin personal** → última usada → institucional `is_default` de su especialidad → primera de la lista | `lib/clinical/template-preferences.ts:147-206` (portal) |
| ¿Dónde vive el pin? | `public.user_template_preferences`, PK `(user_id, specialty_code)`, **RLS estrictamente personal**, `grant` a `authenticated` | `supabase/migrations/20260811141100_user_template_preferences.sql` |
| ¿Puede Windows leerlo/escribirlo? | **Sí**, con el JWT del médico por PostgREST — el mismo camino que ya usa el espejo con `consultations` | `EspejoDeConsulta.EscribirAsync` |
| ¿Sabe Windows la especialidad del médico? | `SesionMiracle` **no** (solo Id, Email, Nombre), pero `PerfilProfesional` **sí** lee `specialty_code` de `profiles` | `SesionMiracle.cs:69-75` vs `:456-472` |
| ¿Cómo elige plantilla Windows hoy? | **No elige**: siempre «Nota abierta (Ü)», creándola si no está | `PlantillaAbierta.cs`, `ConsultaWindow.ResolverPlantillaAsync` |
| ¿Cuántas plantillas hay de verdad? | **204**, y la propia se creó en el acto | log 22:01:28 · `clinica: 204 plantilla(s)` + `plantilla propia creada` |
| ¿Cómo se edita una sección en el portal? | Clic sobre el texto → `textarea` con Guardar/Cancelar y autoguardado a 1,2 s | `components/app/NoteSectionView.tsx` |
| ¿Con qué se guarda? | `PUT /api/clinical/encounters/:id/note`, y después se espeja en `consultations` | `en-vivo/page.tsx:644-669` |
| **Qué exige ese PUT** | **EXACTAMENTE las claves del snapshot: una de más o una de menos → `400 NOTE_JSON_INVALID`** | `docs/backend-clinical-api-contract.md:345-356` (portal) |
| **Qué pinta Windows hoy** | **Solo las secciones con texto**: `if (s.Contenido.Trim().Length == 0) continue;` | `ConsultaWindow.cs:1613` |
| ¿Se puede entrar a una consulta de la lista? | **No.** `FilaDeConsulta` pinta una tarjeta **sin clic**: la lista es un escaparate | `ConsultaWindow.cs:1803` |
| ¿Qué protege a una nota firmada? | Un trigger: `CONSULTATION_IMMUTABLE`. Congela nota, resumen, códigos, transcript y firma, y solo deja `aprobada → exportada` | `supabase/migrations/20260721000000_consultation_immutability_and_addenda.sql:15-52` |
| **Qué manda el espejo de Windows** | `estado: "borrador"` y `firma: null` **siempre**, en un upsert `merge-duplicates` | `EspejoDeConsulta.Fila:60-101` |
| Una consulta real, de punta a punta | 22:09:56 encounter creado · 22:10:09 transcripción (111 car.) · 22:10:13 nota de **4 secciones** y espejada | log de hoy |
| ¿La nota generada se puede corregir? | **No.** `PintarNota` pinta `Estudio.Parrafo(...)`: texto muerto | `ConsultaWindow.cs:1594-1685` |

### Los tres hallazgos que no estaban en la petición

**1. La promesa 94 dice lo contrario de lo que se pide.**

```
94. elegir plantilla deja de ser un paso: la consulta arranca sola
```

Nació el 2026-09-01 de una petición del mismo dueño —*«no quiero preocuparme por seleccionar ninguna
plantilla»*— y hoy pide poder seleccionarla. **Su letra sobrevive** y por eso no se retira: el botón
grande sigue arrancando sin preguntar nada, que es lo que la promesa afirma. Lo que cambia es su
**alcance**: deja de ser cierto que la elegida sea *siempre* la abierta. Se anota aquí porque un
cambio en lo que el sistema promete no puede vivir solo en un commit, y porque los comentarios de
`PlantillaAbierta.cs` afirman hoy una decisión de producto que ya no es la vigente.

**2. Editar mandando solo lo que se ve rompe la nota, y el error no diría eso.**

El backend exige las claves **exactas** del snapshot; Windows pinta solo las secciones con texto. La
nota de las 22:10 tenía 4 secciones y 3 avisos: si alguna vino vacía —y la plantilla abierta deja en
blanco a propósito lo que no se dijo—, el primer guardado contestaría `400 NOTE_JSON_INVALID`. Es el
fallo bueno. **El malo es el otro**: si en vez de fallar se aceptara, las secciones no pintadas
desaparecerían de la historia clínica sin que nadie lo viera. Por eso hay promesa.

**3. El espejo devuelve a «borrador» cualquier consulta que el portal haya avanzado.**

`Fila` escribe `estado: "borrador"` y `firma: null` sin mirar lo que ya había, y el upsert es un
UPDATE cuando la fila existe. Hoy casi no se alcanza —solo re-terminando el mismo encounter— pero
**editar desde Windows lo vuelve el camino normal**. Contra una consulta ya firmada, el trigger lo
para (`CONSULTATION_IMMUTABLE`), así que la base no se corrompe; lo que pasa es peor de diagnosticar:
la nota **sí** cambia en el backend por el `PUT`, el espejo **no**, y la misma consulta queda distinta
según por dónde se mire. Contra una consulta `revisada` no hay trigger que valga: la degrada a
borrador y se lleva por delante el ciclo de revisión del portal.

## Por qué esto va dirigido por especificación

Porque **los tres fallos de arriba son invisibles**. Ninguno da error en la pantalla: la plantilla
equivocada produce una nota bien formada con la estructura de otra cosa; la sección perdida al editar
no deja hueco, deja una nota más corta; y la consulta degradada a borrador se ve perfecta en Windows
y mal en el portal. Una prueba escrita después mediría «se editó y se guardó», que es exactamente lo
que ninguno de los tres rompe.

**La regla del flujo, y no tiene excepciones:** ninguna línea de producción entra antes que la promesa
que la juzga. Cada fase empieza con el contrato ROTO y termina con el contrato INTACTO.

## La especificación

Se numera desde **189**: `main` va por la 179 y **184-188 están reservadas** por la rama del collar
(spec 014), que no se toca desde aquí.

| # | Promesa | Fase |
|---|---|---|
| 189 | la plantilla con la que se graba sale de una cadena de orden fijo —lo que el médico eligió, su sugerida, la de urgencias, la abierta— y nunca de una cualquiera del catálogo | 1 |
| 190 | la sugerida del médico se guarda a su nombre y por su especialidad, y al volver a abrir la app manda sobre la de urgencias | 2 |
| 191 | **corregir una sección manda la nota ENTERA: todas las claves del snapshot, también las que quedaron vacías** | 3 |
| 192 | una nota firmada no se edita desde Windows: se ve, se sigue pudiendo mandar a SAP, y el editor no aparece | 4 |
| 193 | al re-escribir una consulta que ya existe, el espejo no manda estado ni firma: lo que el portal avanzó sobrevive a una edición hecha en Windows | 5 |
| ~~94~~ | *sobrevive, no se retira*: el botón grande sigue arrancando sin preguntar. Cambia su alcance, no su enunciado (ver hallazgo 1) | — |

**La que cierra el asunto es la 191.** Mientras corregir pueda mandar media nota, las otras cuatro son
comodidad: el médico tendría un editor cómodo con el que mutilar la historia clínica sin enterarse.

### Con qué se juzga cada una

Todas por **reflexión sobre reglas puras y sobre el JSON que se manda**, sin pantalla, sin red y sin
SAP — el mismo camino de `ReglaDelToque` (147) y de `EspejoDeConsulta.Fila` (93):

- **189** — `U.WindowsClient.Clinical.ReglaDeLaPlantilla.Elegir(catalogo, elegida, pin, especialidad)`,
  con un catálogo construido a mano. **Falsifica**: poner de primera en el catálogo una plantilla
  ajena y comprobar que NO se elige; quitar el pin y comprobar que gana urgencias; quitar urgencias y
  comprobar que gana la abierta; poner las cuatro y comprobar el orden entero.
- **190** — el cuerpo y la ruta de la escritura del pin, contra el `BackendDeMentira` que ya tiene el
  arnés. **Falsifica**: que el pin viaje sin `specialty_code`; que se escriba con un id de usuario que
  no sea el del token; que al reabrir no se lea.
- **191** — el `note_json` que se compone al guardar, sobre una nota con una sección **vacía**.
  **Falsifica**: que el payload lleve menos claves que el snapshot, o alguna de más.
- **192** — `ReglaDeLaEdicion.SePuedeEditar(estado)` sobre los cinco estados del portal.
  **Falsifica**: que `aprobada` o `exportada` contesten que sí.
- **193** — `EspejoDeConsulta.Fila(...)` con y sin fila previa. **Falsifica**: que la versión de
  actualización contenga `estado` o `firma`.

**Lo que el contrato NO puede juzgar** y va al nivel 4, con el log en el PR: que el caret abra el
selector y se vea bien, que las 204 plantillas se puedan buscar, que el editor se sienta natural, y
que las tarjetas limpias se lean mejor que las de hoy.

## Las fases

| Fase | Promesa | Qué toca | Terminado |
|---|---|---|---|
| **0** | ninguna — arnés | `tests/ContratoDelGrafo/Contrato.cs` | 189-193 **ROJAS**, 1..179 intactas |
| **1** | 189 | `Clinical/ReglaDeLaPlantilla.cs` (nuevo) | 189 verde |
| **2** | 190 | `Clinical/SugeridaDelMedico.cs` (nuevo), `Cuenta/SesionMiracle.cs` | 190 verde |
| **3** | 191 | `Clinical/ClinicaClient.cs` (`PUT /note`), `Clinical/Consulta.cs` | 191 verde |
| **4** | 192 | `Clinical/ReglaDeLaEdicion.cs` (nuevo) | 192 verde |
| **5** | 193 | `Clinical/EspejoDeConsulta.cs` | 193 verde |
| **6** | ninguna — la interfaz | `Ui/ConsultaWindow.cs` | botón partido, selector, editor, entrar a una consulta |
| **7** | ninguna — el aire | `Ui/ConsultaWindow.cs` | las tarjetas de la nota, limpias como las de Consultas |

Las fases 1-5 son lógica pura y no tocan la pantalla; la 6 es la que las junta y es **toda la zona de
choque** (UI de `windows-client`, riesgo alto). Por eso va al final y en un solo commit: cuanto menos
días esté abierta esa parte, menos posibilidad de pisarse con otro.

## Lo que NO entra

| Fuera | Por qué |
|---|---|
| **Firmar la nota** («Aprobar y firmar») | Corre en el **servidor** con un hash canónico que Graph re-verifica al exportar (`actions.ts:computeSignatureHash`). Meterlo en Windows sería reimplementar una firma clínico-legal en el cliente |
| **Adendas** sobre notas firmadas | Es el camino correcto para corregir lo firmado, y es una feature entera con su tabla append-only. La 192 se limita a **no dejar editar**, que es lo que impide el daño |
| Atajos `/snippet`, dictado por sección | Comodidad de escritorio con teclado. No es lo crítico y el médico aquí habla, no teclea |
| Asociar paciente al encounter | Existe (`PATCH /patient`) y no lo pidió. Va en su propia spec |
| Filtros y «cambiar plantilla y regenerar» | Se propusieron y **se dejan fuera de las promesas**: si sobra tiempo tras la fase 7, entran como UI sin contrato |
| Tocar la spec 014 / la rama del collar | Es de otra rama y está en revisión |

## Cierre

- [ ] 189-193 verdes y 1..179 intactas (`.\scripts\contrato-del-grafo.ps1` → CONTRATO INTACTO)
- [ ] Sabotaje comprobado en la 191: si el payload deja fuera una sección vacía, la promesa se pone roja
- [ ] Probado a mano: grabar con la preseleccionada, grabar eligiendo otra, corregir una sección de una
      nota recién generada, y corregir una de una consulta abierta desde la lista
- [ ] El ✓ y «Todo a SAP» siguen funcionando desde las dos pantallas
- [ ] Estado: **implementado** (AAAA-MM-DD)
