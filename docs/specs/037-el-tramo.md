# El tramo: muchos clics de una llamada, desprendido, y la voz siempre libre

Estado: **implementada, contrato intacto (291-295)** · 2026-09-18 · Rama: `jose/el-tramo` (apilada sobre `jose/las-puertas-son-unicas`, PR de la spec 036)

> Fase 3 de `docs/plan-clics-en-tiempo-real.md`. El dueño (2026-09-18): «que la carita flotante pueda
> moverse al lado de cada botón que cliquea y clicar muchos muy rápidamente», y que el alto lo pida
> **la voz**, no la transcripción. Y de la fase 1 (`docs/anatomia-del-clic-2026-09-18.md`): con una
> llamada a herramienta pendiente, GPT-Live **no responde** (`function_call_outputs_required`, medido el
> 2026-09-12). Así que el tramo no puede ser una llamada larga: **corre desprendido**.

## Lo que pasa hoy, medido

Cada clic es una vuelta de Luna: `map_decidir` (o `map_take`) → resultado → Luna piensa 3,0 s de
mediana (spec 029) → el siguiente. Diez clics hacia un objetivo son diez vueltas y ~100 s. Y mientras
una herramienta corre, la voz no puede hablar ni frenar.

`map_batch` ya recorre N pasos con compuerta por paso, freno antes de cada paso y relato
(`RecorrerSegunElNucleo`), pero los pasos los lista Luna de antemano. Y `map_decidir` ya sabe elegir un
paso en la pantalla de ahora con Jev (puertas únicas, tres preguntas, segunda mejor: spec 036). Lo
que falta es juntarlos: **un bucle que elige y pulsa hasta que el objetivo se cumple o algo lo para,
sin volver a Luna en cada paso, y sin bloquear la voz.**

## Las promesas

| # | Lo que el sistema promete |
|---|---|
| **291** | `map_tramo(objetivo, tope)` **contesta al instante** «en marcha» y devuelve el turno: el bucle corre por detrás; pedir otro mientras uno corre no arranca un segundo y dice cuál corre; sin `objetivo` dice qué falta; y con el decisor apagado no existe (como `map_decidir`). |
| **292** | El tramo **para solo** y su cuenta dice por cuál: el decisor dice que el objetivo ya está cumplido; se agota el tope; el decisor no se atreve (duda, peligro, puerta fuera de lista); la mano no pudo; se pidió el **freno**; o **se repitió la misma puerta tres veces** (el detector de bucle). Cada paso cuenta, hecho o no. |
| **293** | `map_alto` para el tramo **en el paso en curso**, sin esperar a que termine el paso siguiente: pone el mismo freno que Escape, y contesta dónde quedó; sin tramo en marcha lo dice. |
| **294** | El tramo **cuenta cada paso** —al notch y al log, con la puerta, el número y la confianza— y la cuenta final lleva lo que hay delante (el inventario), para que Luna decida el siguiente rumbo sin preguntar. `map_tramo_estado` devuelve esa cuenta en cualquier momento. |
| **295** | **La voz se entera sin preguntar**: al parar, la cuenta entra a la sesión de voz como un mensaje y se le pide turno, y solo una vez por tramo; sin sesión de voz, la cuenta queda para `map_tramo_estado`. |

## Cómo se juzga, sin pantalla

`ElTramo` es una clase aparte (`Navigation/ElTramo.cs`) que recibe delegados: quién decide (el mismo
`Decisor` del mapa), quién pulsa (el `Take` del mapa por selector), quién lee las puertas, quién frena
(`Freno.Pidieron`), quién cuenta el progreso, y quién avisa a la voz. Con delegados falsos:

- 291: `map_tramo` devuelve en menos de 100 ms aunque el decisor falso tarde 500 ms por paso; un segundo
  `map_tramo` con el primero corriendo no arranca otro.
- 292: seis tramos, uno por motivo de parada, y en cada uno la cuenta lo nombra; el de bucle: el decisor
  siempre elige la misma puerta y la mano siempre dice «no cambió» → para en el tercero.
- 293: un tramo con el decisor lento; `map_alto` a mitad → el tramo termina en ese paso, la cuenta dice
  «paraste», y no se pulsó más.
- 294: el progreso falso recibe una línea por paso con «N)», etiqueta y confianza; la cuenta final lleva
  «EN PANTALLA AHORA».
- 295: el avisador falso recibe la cuenta exactamente una vez al parar.

Sobre la máquina: un tramo de 3-5 pasos en el Explorador con `simulado` («abrir Descargas y entrar en
la primera carpeta»), otro en Configuración, y `map_alto` por MCP a mitad de un tramo.

## Las fases

| Fase | Qué deja | Promesa |
|---|---|---|
| 1 | `ElTramo` (el bucle, los motivos de parada, el detector de bucle) y `map_tramo` desprendido | 291, 292 |
| 2 | `map_alto` y `map_tramo_estado`; el progreso al notch y al log | 293, 294 |
| 3 | El aviso a la voz al parar (`EnviarTextoAsync` + pedir turno) | 295 |

## Decisiones

| Decisión | Por qué |
|---|---|
| Desprendido desde el diseño, no como respaldo | Con una llamada pendiente GPT-Live no responde (medido); una voz bloqueada no es Astra |
| Un tramo a la vez | Dos bucles pulsando la misma pantalla es un desastre garantizado |
| Tres repeticiones de la misma puerta paran el tramo | El diagnóstico del 7 de septiembre lo pide desde los 80 taps en (330,222); tres es lo que ya usa el tope de intentos |
| Lo irreversible sigue vetado; `peligro` de Jev para el tramo | Es un hospital; el tramo hereda los vetos de `map_take` y además escucha a Jev |
| El tope por defecto es 15 pasos | Diez clics es el escenario del plan; 15 deja margen sin que un tramo perdido dure un minuto |
| La cuenta llega a la voz como mensaje, no como resultado de herramienta | La llamada de `map_tramo` ya se contestó; el único canal que queda abierto es un mensaje nuevo |

## Lo que queda fuera

- Esperar el cambio de pantalla **suscrito** (fase 4 del plan): aquí cada paso sigue pagando el
  presupuesto de `map_take`. El tramo hace que Luna no piense entre clics; la fase 4 hace que cada
  clic cueste menos.
- Medir con Jev real sobre SAP.
