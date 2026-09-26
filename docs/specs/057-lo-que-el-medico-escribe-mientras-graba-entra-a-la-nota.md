# Lo que el médico escribe mientras graba entra a la nota

> Spec 057 · 2026-09-26 · rama `claude/admiring-brahmagupta-fghedd` · promesas 468-471

## Qué se pidió

> «Escribir en las secciones mientras graba: el médico mete su examen normal durante la consulta y
> la nota generada lo respeta. Implementa el sistema que ya está en Notes para eso, o uno similar.»

## Qué hace la web (medido)

- `components/app/SectionDraftsPanel.tsx`: las secciones de la plantilla, plegadas, con «/» atajos.
- `lib/clinical/use-section-drafts.ts`: guarda en `encounter_section_drafts (encounter_id,
  section_key, content)` con upsert por `encounter_id,section_key`, 900 ms después de dejar de
  escribir; vaciar una sección borra su fila. Copia local en `localStorage`; al abrir, **la local
  gana** sección por sección.
- `lib/clinical/section-drafts.ts`: al generar, `buildTranscriptWithSectionDrafts` añade a la
  **transcripción** un bloque rotulado («--- ANOTACIONES ESCRITAS POR EL MÉDICO DURANTE LA CONSULTA
  ---», instrucciones, y una línea `[Sección] texto` por sección en el orden de la plantilla).
  `stripSectionDraftsBlock` lo quita antes de regenerar. No hay campo propio en el backend (D20):
  por eso va en la transcripción y no por `note-adjustment`, que prohíbe datos nuevos.

U mandaba solo la transcripción (`Consulta.TerminarAsync`).

## La decisión

**El mismo sistema, no uno parecido**: la misma tabla, la misma clave, el mismo bloque carácter a
carácter (vectores de la web). Así lo que el médico escribe en U lo ve la web al abrir la consulta,
y al revés, y la nota sale igual la genere quien la genere.

Lo propio de Windows: las secciones aparecen **debajo de la transcripción en vivo mientras se
graba**, con los atajos de la spec 056 (el examen normal entra entero, sin seleccionar nada), y la
copia local va **cifrada con DPAPI** en `%LOCALAPPDATA%\U\borradores\` — en la web es localStorage
en claro; aquí es un archivo en disco con texto clínico, y eso no se deja en claro.

## Las promesas

- **468** — el bloque que se suma a la transcripción es el de la web: el mismo texto para los
  mismos borradores y la misma plantilla, sin bloque cuando no hay nada escrito, y quitarlo
  devuelve la transcripción sin él, así que regenerar nunca duplica.
  `BorradoresDeSeccion.Bloque/Quitar/Contar`, contra el grupo `borradores` de los vectores.
- **469** — guardar un borrador escribe en `encounter_section_drafts` con upsert por
  `encounter_id,section_key`, sin `user_id` ni `doctor_id` (los pone la base); vaciarlo borra su
  fila; y un fallo de red devuelve «no guardado» sin lanzar ni perder el texto.
  `BorradoresDelMedico.GuardarAsync/LeerAsync`.
- **470** — al terminar de grabar, lo que viaja a `/transcript` es lo dicho MÁS el bloque de lo
  escrito; y si no se dijo nada pero sí se escribió, la nota se genera igual.
  `Consulta.ConLoEscrito`.
- **471** — las secciones salen del `template_snapshot` congelado del encounter, en su orden; y al
  combinar la copia local con la de la nube, gana la local sección por sección y la nube llena lo
  que falta. `BorradoresDeSeccion.SeccionesDe/Combinar`.

## Fases

| Fase | Promesas | Qué toca |
|---|---|---|
| 0 | 468-471 en rojo | `Contrato.cs`, `bronce/generar-desde-la-web.mts` (grupo `borradores`) |
| 1 | 468, 471 | `Clinical/BorradoresDeSeccion.cs` (puro) |
| 2 | 469 | `Clinical/BorradoresDeSeccion.cs` (`BorradoresDelMedico`) |
| 3 | 470 | `Clinical/Consulta.cs` |
| 4 | nivel 4 | `Ui/ConsultaWindow.Borradores.cs`: las secciones bajo la transcripción en vivo, autoguardado a 900 ms, «Guardado · Se suma a la nota al generarla», copia DPAPI |

## Lo que queda fuera

- El campo `section_inputs` en el backend (lo limpio según D20): cuando exista, solo cambia
  `BorradoresDeSeccion.Bloque`, y los vectores de la web lo dirán.

## Hallazgos

1. **El catálogo de plantillas de U no trae sus secciones** (`PlantillaClinica` es id, nombre,
   especialidad): las secciones para escribir salen del `template_snapshot` del encounter, que es
   además lo que usa la web y lo que el motor recibe. Se leen justo después de crear el encounter.
2. **La copia local de la web es localStorage en claro.** Aquí va cifrada con DPAPI y se borra al
   tener la nota: es texto clínico en disco.

## Cierre

- [x] 468-471 verdes (2026-09-26). Sabotaje 6/6: instrucciones distintas y «quitar no quita» ponen
      roja la 468; mandar `doctor_id`, la 469; mandar solo lo dicho, la 470; que gane la nube o
      perder el orden de la plantilla, la 471. Contrato entero sin rojas nuevas frente a la base.
- [ ] Nivel 4: escribir en dos secciones grabando, ver que la nota lo redacta dentro; abrir la
      consulta en la web y ver los mismos borradores
