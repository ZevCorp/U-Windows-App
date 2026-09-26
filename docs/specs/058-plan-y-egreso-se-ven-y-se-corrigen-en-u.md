# Plan y egreso se ven y se corrigen en U

> Spec 058 · 2026-09-26 · rama `claude/admiring-brahmagupta-fghedd` · promesas 472-474

## Qué se pidió

> «Editar el plan y el egreso: hoy U no los toca» — y en la misma frase, que el PDF para el paciente
> dependa de eso (spec 059).

## Diagnóstico

- El cierre vive en `note_json.discharge`: `plan.medications[] {name, dose, route, frequency,
  duration, instructions, evidence}`, `plan.non_pharmacological[]`, `plan.follow_up[]`,
  `recommendations[]`, `alarm_signs[] {text, urgency}`. Se guarda con la nota entera (`PUT /note`).
- U lo **llevaba intacto** en cada guardado (`NotaClinica.Crudo`, promesa 461) pero no lo enseñaba:
  el médico no podía ver ni corregir qué le recetó al paciente.
- **La web lo corrompe al editar** (`PlanDischargePanel.tsx`): «Dosis y vía» es UN campo que
  escribe `{dose: valor, route: ""}`, y «Frecuencia y duración» escribe `{frequency: valor,
  duration: ""}`. Corregir la dosis borra la vía; corregir la frecuencia borra la duración.
- La fórmula médica (spec 059) necesita **concentración** y **cantidad total**, que el modelo de
  datos no tiene. Decidido por el dueño (2026-09-26): se agregan como campos opcionales
  `concentration` y `quantity`.

## Las promesas

- **472** — el plan y el egreso se leen de la nota como en la web: las mismas listas, vacías cuando
  faltan o no son listas, con los mismos medicamentos, textos y urgencias.
  `EgresoDeLaNota.Leer`, contra el grupo `egresos` de los vectores (`ensureClinicalDischarge`).
- **473** — corregir un campo del plan cambia SOLO ese campo: la vía sobrevive a corregir la dosis,
  la duración a corregir la frecuencia, la evidencia y todo lo demás de la nota viajan igual, y
  concentración y cantidad se guardan como campos propios. Sin cierre previo, añadir crea el cierre
  entero.
- **474** — con el plan, el seguimiento, las recomendaciones y los signos de alarma llenos desde U,
  la revisión deja de decir que falta el cierre.

## Fases

| Fase | Promesas | Qué toca |
|---|---|---|
| 0 | 472-474 en rojo | `Contrato.cs`, vectores (grupo `egresos`) |
| 1 | 472-474 | `Clinical/EgresoDeLaNota.cs` (nuevo, puro, sobre `NotaClinica.Crudo`) |
| 2 | nivel 4 | `Ui/ConsultaWindow.Egreso.cs`: el bloque «Plan y egreso» bajo el papel, un campo por dato, guardado al salir del campo |
| web | vitest | `lib/clinical/discharge-edit.ts` + `PlanDischargePanel.tsx`: un campo por dato, sin borrar el vecino; concentración y cantidad |

## Riesgo abierto

Si Graph descarta campos que no conoce en `PUT /note`, `concentration` y `quantity` no
sobrevivirán y la fórmula (059) dejará su espacio en blanco. No se puede medir desde aquí: se
comprueba en el nivel 4 (guardar una cantidad, reabrir la consulta, ver que sigue).

## Hallazgos

1. **La revisión pide también recomendaciones** para dar el cierre por completo (es la regla de la
   web, portada en la 450). La primera versión de la 474 no las llenaba y salió roja con el código
   bien: el error era de la promesa, y el enunciado se corrigió para decir las cuatro listas.
2. **La web corrompía la receta al corregirla** (dosis → borraba vía; frecuencia → borraba
   duración). Arreglado en la web con `lib/clinical/discharge-edit.ts`: un campo por dato.

## Cierre

- [x] 472-474 verdes (2026-09-26). Sabotaje 5/5: leer la vía de la dosis pone roja la 472; el bug de
      la web, escribir cualquier campo, perder la urgencia al corregir un signo y crear el cierre a
      medias ponen roja la 473 (el último también la 474). Contrato entero sin rojas nuevas.
- [x] Web: `tests/discharge-edit.test.ts` (6), sabotaje comprobado (reintroducir el bug lo pone
      rojo); `npm run typecheck` limpio; 891/891 tests.
- [ ] Nivel 4: corregir la dosis de un medicamento en U y verla en la web con su vía intacta;
      guardar una cantidad y reabrir
