<!--
El PR es donde queda escrito qué entró y por qué. Va aunque lo mergees tú mismo cinco minutos
después. Rellenar esto no es papeleo: cada casilla existe porque su ausencia costó un diagnóstico
(ver CLAUDE.md §Aprendizajes de método).
-->

## Qué promete ahora el sistema que antes no

<!-- Los números de promesa de tests/ContratoDelGrafo/Contrato.cs, con su enunciado.
     Si el cambio no toca ninguna promesa, dilo y explica por qué no hacía falta. -->

- Promesa NN — «…»

Spec: `docs/specs/NNN-<slug>.md`

## Evidencia

<!-- Pegada LITERAL desde out\evidencia.md (`.\scripts\verificar.ps1`). Sin resumir.
     Un nivel que no se corrió va como NO CORRIDO con su motivo — nunca como OK. -->

| Nivel | Resultado | Detalle |
|---|---|---|
| | | |

**En cuántas pantallas se probó, con nombre:**
<!-- «explorer.exe (16 pantallas) y Configuración (11)». Una sola pantalla es una apuesta a que
     las demás se comportan igual, y el run de NWP1 demostró que no. Si fue una, dilo así. -->

**La clase de error vivía en N sitios; N corregidos:**
<!-- El veredicto rojo falso se arregló para árboles y reapareció idéntico en el grid media hora
     después. El número sale de un grep, no de la memoria. -->

## Qué se dejó fuera, y por qué

<!-- Hallazgos que aparecieron y no entran en esta rama. Si van a la spec, dilo. -->

## Riesgo

- [ ] Toca `SurfaceMap.cs` u otro archivo del **núcleo congelado** (autorizado por el dueño)
- [ ] Toca la **UI de `windows-client`** — zona de choque alto: ¿hay otra rama abierta ahí?
- [ ] Cambia el **enunciado** de una promesa ya existente (eso cambia lo que el grafo promete a
      todo lo que se construye encima: se habla antes de mergear)
- [ ] Cambia el comportamiento en **SAP** y se probó contra el SAP real
