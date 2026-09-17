# Plan de implementación: médico o estudiante, la misma app y la misma grabación

Estado: **propuesto** · Nace del diagnóstico del 2026-09-17 · Rama: `jose/medico-o-estudiante` (desde `main`, después de la 031)

> El dueño: «tenemos varios tipos de usuario: médico y, por ahora, estudiante. El médico tiene la
> nota clínica; para el estudiante esa interfaz se convierte en el centro de operaciones de sus
> asistentes. Que tenga todas las capacidades de grabar exactamente igual, pero sin plantillas de
> consultas clínicas: que el modelo organice lo que escuchó en una nota normal, que la coja el
> asistente principal y decida qué hacer con ella. Al registrarte eliges si eres médico o
> estudiante, y puedes cambiar de cuenta como ya se puede hoy.»

Cuarta y última pieza del camino que empieza en la [031](031-la-consulta-lleva-la-carita-a-otro-escritorio.md).
Es la más pequeña: el dueño acertó en que es «un cambio de prompt», y el diagnóstico dice dónde
vive ese prompt.

## Diagnóstico: qué se midió

Del código en `origin/main` (`a2f972a`), 2026-09-17.

| Qué | Medida | Fuente |
|---|---|---|
| Quién organiza la nota | **el backend**, con el snapshot de la plantilla del encounter: `POST /api/clinical/encounters/{id}/generate-note` con cuerpo vacío. El cliente no manda prompt ni rol | `windows-client/src/Clinical/ClinicaClient.cs:132-140` |
| Dónde está, entonces, «el prompt» | en las **instrucciones de las secciones de la plantilla**. La app crea una vez «Nota abierta (Ü)», con tres secciones anchas cuya instrucción le pide al organizador que estructure lo que oyó; la primera ya dice «no fuerces un esquema fijo» | `windows-client/src/Clinical/PlantillaAbierta.cs:36-95`; promesa 94 |
| Cómo se elige la plantilla | sola y sin preguntar: la abierta si existe en el catálogo del usuario, y si no, se crea (promesa 94, spec 004) | `PlantillaAbierta.Elegir`, `ConsultaWindow.cs:1162-1173` |
| Qué sabe la app del tipo de usuario | **nada todavía**: el perfil que lee y guarda es el profesional (nombre, documento, registro, especialidad, país, ciudad) y la RPC de edición **no toca `role` a propósito** | `windows-client/src/Cuenta/PerfilProfesional.cs:20-49`; migración `20260829120100_update_own_profile.sql` (portal) |
| Qué valores admite el rol hoy | `profiles.role` es el `enum app_role` con **`admin`, `supervisor`, `medico`**, por defecto `medico`; un médico B2C nace como `admin`. `estudiante` **no existe**: es un `alter type … add value` en el portal | `Pagina-web-clientes-final/supabase/migrations/20260621041058_auth_profiles_and_roles.sql:5-12`; `PerfilProfesional.cs:26-27` |
| Cambiar de cuenta | ya existe, y solo se impide grabando (promesa 99) | `windows-client/src/Clinical/Consulta.cs:113` |
| Qué hace la consulta con la nota al terminar | la espeja al portal y la deja lista para el triage/SAP (specs 004 y 008) | `EspejoDeConsulta.cs`, `Encargo.cs` |

Y lo que **no** está en este repo: elegir médico o estudiante **al registrarse** es del portal
(`Pagina-web-clientes-final`) y de su base: un valor nuevo de `role` (`estudiante`) puesto en el
alta. Aquí solo se lee. Antes de escribir en ese repo, leer sus reglas (aprendizaje nº15).

## Por qué esto va dirigido por especificación

Porque la plantilla se elige sola y sin preguntar (94): si el rol se lee mal o no se lee, un
estudiante graba y recibe una nota **clínica** con «hallazgos y datos objetivos» vacíos, y nadie
se lo dice. Y porque «nota normal» y «nota clínica» comparten el 95 % del camino: la promesa es lo
único que impide que un arreglo en uno cambie el otro sin que se note.

**La regla del flujo, y no tiene excepciones:** ninguna línea de producción entra antes que la
promesa que la juzga. Cada fase empieza con el contrato ROTO y termina con el contrato INTACTO.

## El diseño

**El rol viene con la cuenta.** Al entrar, junto al perfil, la app lee `role`. Es `medico` o
`estudiante`; cualquier otro valor —`admin`, `supervisor`, vacío— se trata como médico, que es lo
de hoy. Cambiar de cuenta cambia de rol, con la misma regla de siempre: no grabando.

**Una plantilla abierta por rol, elegidas por la misma regla.** `PlantillaAbierta` deja de ser una
y pasa a ser `PlantillaAbierta.Para(rol)`: para el médico, la de hoy, intacta; para el estudiante,
«Nota (Ü)»: dos secciones —«Nota» y «Pendientes y siguientes pasos»— cuyas instrucciones no hablan
de consulta ni de datos clínicos, sino de organizar lo que se dijo con la estructura que mejor le
venga a esa grabación y de sacar aparte lo que queda por hacer. Se crea una vez, igual que la de
hoy; se elige sola, igual que hoy. El backend no cambia.

**La misma grabación, otro destino.** El estudiante pulsa Grabar, ve lo que se va oyendo, para, y
la nota queda organizada en su cuenta, como el médico. La diferencia es al terminar: en vez de
espejarse al triage, la nota **se le entrega al asistente principal** de la 033 como un mensaje
suyo («esto es lo que acabo de grabar»), y él decide qué hacer con ella: un encargo, una pregunta,
nada. Si la 033 no ha entrado, la nota se queda guardada y se ve en la lista, sin más.

**La misma ventana, otro nombre.** Para el estudiante la consulta se llama «centro de operaciones»
y el conmutador dice «Notas» en vez de «Consultas | Nota». Las sillas de los asistentes (033) viven
en ella para los dos roles; lo que cambia es qué hace la grabación.

## La especificación

Numeración provisional, a continuación de la 033; se confirma en `/promesas`.

| # | Promesa | Fase |
|---|---|---|
| 290 | la app sabe el rol de la cuenta: `medico` o `estudiante`, leído con el perfil al entrar; cualquier otro valor es médico; y cambiar de cuenta cambia de rol con la misma regla de hoy, nunca grabando | 1 |
| 291 | la plantilla abierta se elige por rol y sola: para el médico la de hoy, intacta; para el estudiante «Nota (Ü)», sin secciones clínicas, creada una vez si no existe; el backend recibe una plantilla, nunca un rol ni un prompt | 1 |
| 292 | para el estudiante la grabación termina entregándole la nota al asistente principal como un mensaje suyo, y no al triage; sin asistente principal la nota se queda guardada y se ve en la lista | 2 |

### Con qué se juzga cada una

- **290** — `Rol.Leer(json del perfil)` con `medico`, `estudiante`, `admin`, vacío y ausente; y
  `Consulta.PuedeCambiarDeUsuario` intacta (99).
- **291** — `PlantillaAbierta.Para(rol)` devuelve nombres distintos, la del médico con las tres
  secciones de hoy byte a byte, la del estudiante sin la palabra «clínic» ni «signos vitales» en
  ninguna instrucción; `Elegir` sigue siendo por nombre exacto (94 intacta).
- **292** — `DestinoDeLaNota.Decidir(rol, hayPrincipal)` devuelve triage / principal / lista; y el
  mensaje al principal lleva la nota entera y su id.

Sobre la máquina: una cuenta de estudiante (creada en el portal con el rol nuevo) graba dos
frases, y la nota sale sin secciones clínicas y aparece como mensaje del principal; la cuenta del
médico de prueba graba las mismas y sale la de hoy. Dos cuentas, dos notas, en el log con hora.

### Límites dichos, no escondidos

- **El alta con rol es del portal.** Esta spec no entra sin ese valor en `profiles.role`; hasta
  entonces, todo el mundo es médico, que es exactamente lo de hoy.
- **La nota del estudiante no va a SAP** ni al triage: no hay paciente.
- **No hay más roles.** Lo que no es `estudiante` es médico; añadir uno es una fila en la regla.

## Las fases

### Fase 1 — el rol y su plantilla (290, 291)

| | |
|---|---|
| **Qué toca** | `windows-client/src/Cuenta/Rol.cs` (nuevo, puro), `PerfilProfesional.cs` (leer `role`, no escribirlo), `PlantillaAbierta.cs` (`Para(rol)`), `ConsultaWindow.cs:1162-1173` (pasar el rol) |
| **Terminado** | 290 y 291 verdes; 94 y 99 intactas |

### Fase 2 — la nota va al principal (292)

| | |
|---|---|
| **Qué toca** | `windows-client/src/Clinical/DestinoDeLaNota.cs` (nuevo, puro), el cierre de la grabación en `ConsultaWindow.cs`, el buzón del principal de la 033 |
| **Terminado** | 292 verde; 93 (el espejo) intacta para el médico |

## Lo que NO entra

- La pantalla de registro con el rol (portal).
- Cambiar nada de la nota clínica del médico.

## Hallazgos

<!-- Se rellena durante la implementación. -->

## Cierre

- [ ] Todas las promesas verdes · `verificar.ps1` con evidencia · dos cuentas probadas, con nombre
- [ ] Estado: **implementado** (AAAA-MM-DD)
