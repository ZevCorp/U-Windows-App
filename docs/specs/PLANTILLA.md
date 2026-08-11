# Plan de implementación: <el resultado, no el área>

Estado: **propuesto** · Nace del diagnóstico del <AAAA-MM-DD> · Rama: `<persona>/<que-hace>`

<!-- Copiar a docs/specs/NNN-<slug>.md. El modelo completo, con todas las secciones escritas de
     verdad, es docs/plan-plata-real.md: fue la primera spec del repo y encontró un bug de meses
     antes de que se escribiera una línea de código. -->

## Diagnóstico: qué se midió

<!-- Lo MEDIDO, con fecha y con fuente. El log (%LOCALAPPDATA%\U\logs\u-AAAAMMDD.log) antes que
     las capturas; la API antes que el código. Una spec que describe un futuro sin haber medido el
     presente inventa el problema. -->

| Qué | Medida | Fuente |
|---|---|---|
| | | |

## Por qué esto va dirigido por especificación

<!-- La razón concreta EN ESTE TRABAJO. Normalmente: el subsistema se da por bueno a sí mismo, así
     que una prueba escrita después del código se escribiría para que pasara. Si no encuentras la
     razón, quizá este trabajo no paga el flujo — dilo y hazlo directo. -->

**La regla del flujo, y no tiene excepciones:** ninguna línea de producción entra antes que la
promesa que la juzga. Cada fase empieza con el contrato ROTO y termina con el contrato INTACTO.

## La especificación

<!-- El enunciado es el que irá LITERALMENTE en tests/ContratoDelGrafo/Contrato.cs, porque el
     enunciado ES la spec. Numerar en continuación de las que ya existen; los números no se
     reciclan. Marcar las que ya se cumplen: entran igual, para congelarlas. -->

| # | Promesa | Fase que la pone verde |
|---|---|---|
| NN | | |
| NN | | ninguna: ya se cumple; se congela |

<!-- Cuál es la promesa que de verdad cierra el asunto: la que, mientras no exista, deja que todo
     lo demás sea cosmético. En el plan de la plata fue la 16. -->

### Con qué se juzga cada una

- **Fixture congelado** (`tests/ContratoDelGrafo/bronce/`) — mismo resultado en cualquier máquina y
  para siempre. Se captura una vez, se le quita todo lo declarado antes de guardarlo, y no se vuelve
  a tocar salvo para añadir casos.
- **Escenario de CI local** (`C:\U-versiones\escenarios\`, fuera del repo) — resultado sobre el
  terreno vivo de esta máquina.
- **Mapa a mano** en la propia prueba, como las promesas 1-10.

<!-- Si alguna promesa no se puede juzgar con el arnés actual, decirlo aquí: eso es la fase 0. -->

## Las fases

<!-- Una fase = un commit que pone verde UNA promesa sin romper las anteriores. Media jornada.
     Si no cabe, son dos fases. Toda la spec vive en una rama; no se mergea fase a fase. -->

### Fase 1 — <lo que el sistema hará y hoy no>

| | |
|---|---|
| **Promesa que pone verde** | NN |
| **Qué toca** | `ruta/archivo.cs` |
| **¿Núcleo congelado?** | no / sí → necesita autorización del dueño |
| **Terminado** | promesa NN verde, 1..NN-1 intactas |
| **Sitios con esta clase de error** | N (contados con grep, no de memoria) |

## Lo que NO entra

<!-- Y por qué. Lo que "ya que estoy" se queda fuera: entra en la lista de hallazgos. -->

## Hallazgos

<!-- Se rellena DURANTE la implementación. El plan se corrige con lo que se mide, no al revés.
     Lo que la spec encontró y no estaba en la petición va aquí, con fecha. -->

## Cierre

- [ ] Todas las promesas verdes (`.\scripts\contrato-del-grafo.ps1` → CONTRATO INTACTO)
- [ ] `.\scripts\verificar.ps1` pasa, con evidencia en `out\evidencia.md`
- [ ] Probado en ≥2 pantallas, con nombre: …
- [ ] Estado de este documento: **implementado** (AAAA-MM-DD)
