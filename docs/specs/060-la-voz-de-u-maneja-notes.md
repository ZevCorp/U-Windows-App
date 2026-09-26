# La voz de U maneja Notes

> Spec 060 · 2026-09-26 · rama `claude/admiring-brahmagupta-fghedd` · promesas 479-484

## Qué se pidió

> «El chat médico de la web, pero hablado con la voz de U: por ejemplo, «Ü, ¿qué le mandé la última
> vez?». Me encantaría que U pudiera ver lo que se genera dentro de Notes (web) y pudiera ver la
> pantalla y moverse más rápido, y no a través de computer use como en una app externa, sino que se
> pudiera mover como su app nativa.»

## Diagnóstico

- La voz (`Voice/ConversacionEnVivo.cs`) tiene herramientas de pantalla (`map_*`), de archivos, de
  memoria y de autocontrol (`self_*`). **Ninguna clínica**: no podía abrir la ventana de la nota,
  ni grabar, ni leer una consulta. Para «¿qué le mandé?» tendría que mirar la pantalla del
  navegador con capturas, lento y frágil.
- U ya lee todo lo que Notes genera, **por datos**: consultas (`EspejoDeConsulta`), pacientes
  (`PacientesDelMedico`), la nota con su plan y egreso (`ClinicaClient.LeerEncounterAsync` +
  `EgresoDeLaNota`). La misma base y el mismo backend que la web.
- `Apuntar` escribe al log la primera línea de CADA resultado de herramienta: con herramientas
  clínicas, eso habría llevado medicamentos y diagnósticos al log.

## La decisión: nativo, por datos, no por pantalla

La voz maneja Notes con **herramientas propias** que hablan con los datos y con la ventana de la
nota de U, no mirando la web:

| Herramienta | Qué hace |
|---|---|
| `nota_abrir` | abre la ventana de la nota; con `paciente`, abre su última consulta |
| `nota_grabar` / `nota_parar` | empieza o termina la consulta |
| `nota_leer` | la nota que se ve, una sección o el plan, en texto para decirlo |
| `nota_historial` | las últimas consultas de un paciente: fecha, motivo, diagnóstico, **medicamentos** y controles |
| `nota_ajustar` | un cambio a la nota o a una sección → la MISMA propuesta que la barra de ajuste |
| `nota_imprimir` | abre el diálogo de imprimir con la nota, la fórmula o las indicaciones |

**Decisión del dueño (2026-09-26): el historial va directo a la voz.** El proveedor de voz recibe
lo que devuelve `nota_historial` y contesta él. Se minimiza lo que viaja: las últimas 3
consultas, recortadas, **nunca la transcripción**.

**La voz no guarda, no firma y no borra.** `nota_ajustar` deja una propuesta en pantalla que el
médico aprueba con Guardar o Ctrl+S. No existe ninguna herramienta de voz que escriba en la historia
clínica sin ese gesto.

## Las promesas

- **479** — la voz tiene las herramientas de Notes con sus argumentos: `nota_abrir`,
  `nota_grabar`, `nota_parar`, `nota_leer`, `nota_historial`, `nota_ajustar`, `nota_imprimir`.
- **480** — el historial de un paciente se pide filtrado por SU `patient_id`, con el token del
  médico y sin mandar `user_id`, y de cada consulta se lee su plan del backend clínico.
- **481** — ninguna herramienta de voz guarda, firma, borra ni aprueba la nota; `nota_ajustar` se
  describe como propuesta que aprueba el médico, y las instrucciones de la voz lo dicen.
- **482** — lo que `nota_historial` entrega a la voz son las últimas 3 consultas como mucho, con
  fecha, motivo, diagnóstico, medicamentos y controles, recortado, y NUNCA la transcripción.
- **483** — sin médico, sin paciente que case o con varios que casan, la herramienta lo dice
  (nombrando a los candidatos) en vez de adivinar.
- **484** — ninguna herramienta de Notes deja texto clínico en el log: ni lo pedido ni lo devuelto.

## Fases

| Fase | Promesas | Qué toca |
|---|---|---|
| 0 | 479-484 en rojo | `Contrato.cs` |
| 1 | 480, 482-484 | `Clinical/HistorialDelPaciente.cs` (nuevo) |
| 2 | 479, 481, 484 | `Voice/ConversacionEnVivo.cs`: catálogo, despacho por el delegado `Nota`, instrucciones, `Apuntar` sin resultado clínico |
| 3 | nivel 4 | `Ui/ConsultaWindow.Voz.cs` (atiende cada herramienta en la ventana) y `Ui/FaceWindow` (inyecta `Nota`) |

## Lo que queda fuera

- Manejar la web abierta en el navegador (CDP/WebView2): U ve lo mismo por datos, más rápido y sin
  depender de qué pestaña esté abierta.
- Preguntas clínicas generales («¿qué dosis de…?»): la voz ya conversa; esto es sobre SUS consultas.

## Hallazgos

1. **`Apuntar` llevaba al log la primera línea de cada resultado de herramienta.** Con nota_*, eso
   eran medicamentos y diagnósticos. Para las herramientas de Notes ahora anota solo cuánto tardó y
   cuántos caracteres devolvió; la 484 lo exige y el sabotaje lo comprobó.
2. **El plan no está en el espejo**: «¿qué le mandé?» lee cada consulta del backend clínico (3 como
   mucho). Es la misma razón por la que el detalle web lo pide al imprimir (spec 059).
3. **`nota_parar` no espera a la nota**: organizarla tarda hasta minutos y la voz no puede quedarse
   muda; contesta al instante y los avisos salen en pantalla al llegar.

## Cierre

- [x] 479-484 verdes (2026-09-26). Sabotaje 6/6: quitar nota_imprimir (479), pedir el historial sin
      filtrar por paciente (480), un ajuste que dice que guarda (481), un historial de 6 (482),
      adivinar entre varios pacientes (483), dejar el resultado en el log (484). Contrato entero sin
      rojas nuevas.
- [ ] Nivel 4, por voz: «¿qué le mandé la última vez a <paciente>?», «abre su última consulta»,
      «léeme el plan», «cámbiale la dosis a …» (propuesta), «imprime la fórmula», «empieza la
      consulta» / «termina»
