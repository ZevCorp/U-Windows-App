# Escribir nunca deja el texto dos veces

Estado: **implementado** (2026-09-24) · Nace del log del 2026-09-23 · Rama: `jose/no-escribe-doble`

> Spec 049 y promesa 410. La 046 a la 048 y las promesas hasta la 407 ya están tomadas en ramas
> abiertas (`jose/ensenar-por-voz`, `jero/jev-*`); los números no se reciclan.

## Diagnóstico: qué se midió

El dueño: «un problema viejo que siempre veo: al decirle que escriba algo lo escribe doble. No puede
escribir nada doble; que se solucione de forma determinística».

| Qué | Medida | Fuente |
|---|---|---|
| Pedido | `map_type` con «Hola, José David:\n\nLa reunión quedó configurada el viernes a las dos de la tarde.\n\nSaludos.» | `u-20260923-windows-app-p2684-151936.log`, 15:24:49 |
| Lo que dijo el campo tras `SetValue` | el texto **sin los saltos** (`David:La reunión`), porque es un campo de una línea | ídem, 15:24:51 `NoCuajo → se teclea` |
| Lo que dijo tras teclear | el texto **dos veces seguidas**: `…Saludos.Hola, José David:…Saludos.` | ídem, 15:24:53 |
| Cuántas veces el respaldo tecleó ENCIMA de lo que `SetValue` ya había dejado | 9 `NoCuajo → se teclea` en todos los logs de esta máquina; cada uno añade, ninguno reemplaza | `grep` sobre `%LOCALAPPDATA%\U\logs` |

**La causa son dos defectos que se suman:**

1. `ComoSeEscribe.Cuajo` compara el texto con los espacios **colapsados** (promesa 247), no
   **quitados**. Un campo de una línea no convierte `\n\n` en espacio: lo borra. `David: La` ≠
   `David:La`, y un campo que SÍ tenía el texto se juzgó «no cuajó».
2. `UiaSurface.TeclearEnElCampo`, el respaldo, **teclea donde esté el cursor**. Sobre un campo que ya
   tiene el texto, eso es añadirlo otra vez. Y la comprobación de después (`Contains`) da por bueno
   un campo con el texto dos veces, porque lo contiene.

## Por qué esto va dirigido por especificación

El juez de la escritura se daba por bueno a sí mismo: `Contains` no puede distinguir «lo tiene» de
«lo tiene dos veces». Una prueba escrita después del arreglo se escribiría con el caso de Gmail y
nada más; la promesa tiene que fijar la regla: el respaldo **reemplaza**, nunca añade.

## La especificación

| # | Promesa | Fase |
|---|---|---|
| 410 | escribir nunca deja el texto dos veces: un campo de una línea que se come los saltos SÍ tiene el texto; el respaldo por teclado no teclea si el campo ya lo tiene, reemplaza lo que haya en un campo que se lee y solo teclea encima de un campo vacío o mudo; y un campo que enseña el texto dos veces seguidas donde antes no estaba es «Doble», que no se da por escrito | 1 |

### Con qué se juzga

Mapa a mano en la propia prueba: `ComoSeEscribe` es pura. Los casos son los del log, literales.

## Las fases

### Fase 1 — el respaldo reemplaza y el juez ve el doble

| | |
|---|---|
| **Promesa que pone verde** | 410 |
| **Qué toca** | `windows-graph/src/Surfaces/ComoSeEscribe.cs`, `windows-graph/src/Surfaces/UiaSurface.cs` |
| **¿Núcleo congelado?** | no |
| **Terminado** | 410 verde, 243 y 247 intactas |
| **Sitios con esta clase de error** | 1 respaldo que teclea encima (`TeclearEnElCampo`, llamado desde `SetValue`). `TeclearEnLaVentana` es la consola: allí teclear ES añadir y se queda como está. SAP escribe con `text =`, que reemplaza. |

## Lo que NO entra

- **El cuerpo del correo acabó en el Asunto.** Con `target=""`, `map_type` escribe en el campo con
  el foco, y el foco seguía en «Subject». Es otro defecto (elegir el campo), no este (escribir dos
  veces), y va en su propia rama.
- **Un campo mudo (Google Docs) no se reemplaza.** No se ve qué tiene, y Ctrl+A en un documento
  selecciona el documento entero: reemplazar ahí borraría el trabajo de la persona. Se teclea
  encima, como hoy.

## Hallazgos

- **Había un segundo camino al doble, y la corrida a mano lo encontró (2026-09-24).** Sobre un
  `<input>` de Chrome, justo después de `SetValue` el campo se lee VACÍO (`MudoNoSeSabe`): Chrome
  actualiza el valor que expone por accesibilidad un instante después. El código viejo tecleaba en
  ese instante, y el texto que SetValue sí había dejado quedaba duplicado. Con la 410, el respaldo
  vuelve a leer antes de teclear, ve el texto y no teclea:
  `el campo no enseña el texto (MudoNoSeSabe) → se teclea` seguido de
  `→ el campo ya tiene el texto: NO se teclea, que sería escribirlo dos veces`.
- La promesa ganó durante la implementación un caso más, del mismo enunciado: un campo con el texto
  ya REPETIDO no «lo tiene» (`AntesDeTeclear("hola", "holahola") == Reemplazando`).

## Cierre

- [x] Promesa 410 verde (`.\scripts\contrato-del-grafo.ps1` → CONTRATO INTACTO)
- [x] Sabotaje comprobado en tres sitios: comparar colapsando, teclear siempre encima, juez ciego al
      doble. La 410 se pone roja en los tres.
- [x] Corrida a mano, app completa por MCP (`map_type`), sobre 2 campos de una página en Chrome: un
      `<input>` de una línea —el caso del Asunto de Gmail— y un `<textarea>`. Texto con dos saltos
      dobles. Leído después por UIA: el `<input>` tiene el texto UNA vez y sin saltos; el `<textarea>`
      una vez y con sus saltos; y repetir la misma escritura en el `<input>` lo deja igual.
