# Los papeles del paciente: nota, fórmula e indicaciones

> Spec 059 · 2026-09-26 · rama `claude/admiring-brahmagupta-fghedd` · promesas 475-478

## Qué se pidió

> «Imprimir o sacar PDF para el paciente: el médico privado entrega fórmula y recomendaciones en
> papel. Mejora el PDF que se genera, el formato mejóralo, y estandarízalo, y que dependiendo del
> médico y el paciente se generen esas cosas.»

## Diagnóstico

- **U no imprimía nada** (ni `PrintDialog`, ni `FlowDocument`, ni PDF).
- **La web tenía un solo papel** (`lib/pdf/note-print.ts`): la nota, **sin plan y egreso**, sin EPS,
  sin número de página, con la institución solo en la primera hoja. Desde la consulta en vivo
  salía sin paciente ni médico. **No había fórmula médica ni indicaciones para el paciente.**
- El Decreto 2200 de 2005 pide en la prescripción: lugar y fecha, paciente y documento,
  medicamento con concentración y forma farmacéutica, vía, dosis y frecuencia, duración, cantidad
  total **en números y letras**, indicaciones, y nombre, firma y registro del prescriptor.

## La decisión: un modelo, dos pintores

El documento se decide en UN sitio —`lib/pdf/patient-documents.ts` en la web, `construirDocumento`—
y cada lado solo lo pinta: la web en HTML (`patient-documents-html.ts`, con `@page`, institución y
número de página en cada hoja), U en un `FlowDocument` de WPF que se imprime con `PrintDialog`
(«Microsoft Print to PDF» da el PDF sin librerías nuevas). U porta el modelo y el contrato le exige
**la misma salida, campo a campo**, sobre los mismos casos (vectores `documentos`, `letras`,
`cantidades`): el paciente se lleva el mismo papel lo imprima quien lo imprima.

Tres papeles:

| Papel | Qué lleva |
|---|---|
| Nota clínica | resumen, secciones, plan y egreso, codificación, adendas, sello del hospital |
| Fórmula médica | por medicamento: concentración y forma, dosis, vía, frecuencia, duración, cantidad en números y letras, indicaciones; firma con registro |
| Indicaciones para el paciente | sus medicamentos, cuidados, controles, signos de alarma agrupados por urgencia |

**La fórmula nunca inventa**: lo que la nota no trae sale en blanco, con una raya para escribirlo a
mano. Un «N/A» o un valor por defecto en una receta es peor que un espacio.

## Las promesas

- **475** — los tres papeles de U son los de la web: para las mismas entradas, el mismo documento
  —membrete, datos, bloques, firma, sello, pie— campo a campo. `DocumentosDelPaciente.Construir`,
  contra el grupo `documentos`.
- **476** — la fórmula nunca inventa: un medicamento sin concentración ni cantidad lleva esos
  campos vacíos, uno sin nombre no se receta, y sin medicamentos el papel lo dice.
- **477** — lo que falta del médico, del paciente o de la institución se omite: ni «undefined», ni
  «null», ni una fila vacía en ningún papel.
- **478** — la cantidad total va en números y letras como en la web (grupos `letras` y
  `cantidades`).

## Fases

| Fase | Promesas | Qué toca |
|---|---|---|
| web | vitest | `lib/pdf/patient-documents.ts`, `patient-documents-html.ts`, `consultation-documents.ts`; detalle, panel rápido y consulta en vivo; se retira `note-print.ts` |
| 0 | 475-478 en rojo | `Contrato.cs`, vectores |
| 1 | 475-478 | `Clinical/DocumentosDelPaciente.cs` (puro) |
| 2 | nivel 4 | `Ui/PapelesDelPaciente.cs` (FlowDocument + PrintDialog) y «Imprimir ▾» en la cabecera de la nota |

## Hallazgos

1. **El papel de la web podía inyectar HTML por el nombre del paciente.** El nombre va también en el
   margen de página (`@top-right { content: "…" }`), DENTRO de `<style>`: un nombre con «</style>»
   cerraba la hoja de estilos. Lo encontró el test del HTML antes de que llegara a producción; `<` y
   `>` salen ahora como escapes de CSS.
2. **Abrir la ventana después de pedir el plan** la hacía bloquear como emergente no pedida. Se
   abre en el mismo clic y se llena al llegar el plan.
3. **El espejo `consultations` no guarda el plan y el egreso**: el detalle web los pide al backend
   clínico al imprimir. Sin ellos, la nota sale igual y la fórmula dice que no pudo leerlos.
4. **El renombrado de un tipo tocó dos textos visibles** («Documento» → otro nombre) y la 475 lo
   cantó con el campo exacto. Es lo que vale comparar el papel entero y no una muestra.

## Cierre

- [x] 475-478 verdes (2026-09-26). Sabotaje 5/5: inventar «N/A» o recetar sin nombre ponen rojas la
      475 y la 476; dejar filas vacías, la 475 y la 477; una tilde de menos en las letras, la 478;
      las urgencias fuera de su orden, la 475. Contrato entero sin rojas nuevas.
- [x] Web: `tests/patient-documents.test.ts` (12); 903/903; typecheck limpio.
- [ ] Nivel 4: imprimir Nota, Fórmula e Indicaciones a «Microsoft Print to PDF» desde U y desde la
      web para la misma consulta, y compararlos
