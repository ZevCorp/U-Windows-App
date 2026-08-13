# Plan de implementación: hablarle a la carita por el collar

Estado: **propuesto** · Nace del diagnóstico del 2026-08-13 · Rama: `jose/integrar-plata-derivada`

Que Ü se pueda oír desde un collar Omi CV1 en vez de desde el micrófono del portátil, sin móvil de
por medio y sin pasar por la nube de Omi.

> **Sobre la rama.** Esto va sobre `jose/integrar-plata-derivada` por decisión del dueño, no desde
> `main` como pide `.claude/rules/ramas-y-commits.md`. El coste, medido y no supuesto: esa rama lleva
> **56 commits sobre `main` y hoy está verde** (contrato 20/20, 2026-08-13), o sea que era mergeable
> antes de esto. Al colgarle el Omi, esos 56 commits esperan a que el Omi termine. Queda escrito
> aquí para que dentro de dos semanas nadie tenga que reconstruir por qué.

## Diagnóstico: qué se midió

### El collar

Todo lo de abajo sale de una **sonda de solo lectura** escrita el 2026-08-13 (aprendizaje nº13:
pregúntale a la API antes de creerle al código). Tres corridas, 1.555 paquetes.

| Qué | Medida | Fuente |
|---|---|---|
| ¿El PC se conecta al collar sin el móvil? | **Sí.** `Omi` en `F1:DA:1F:22:CD:B5`, batería 100 % | sonda, 3/3 corridas |
| Servicio y características | `19b10000-…` · audio notify `19b10001` · códec read `19b10002` · batería `0x180F`/`0x2A19` | sonda |
| Códec que declara el CV1 | **21 (Opus FS320)**, tramas de 20 ms = 320 muestras | lectura de `19b10002` |
| Qué Opus es en realidad | TOC `0xB8` en todos los paquetes → CELT wideband, 16 kHz, mono, 1 trama/paquete | volcado hex |
| Cabecera | 3 bytes `[num_lo, num_hi, idx]`; **`idx` siempre 0** → sin fragmentación con el MTU que negocia Windows | volcado hex |
| ¿Pierde el enlace? | **Cero saltos de numeración** en 1.555 paquetes | sonda |
| ¿Decodifica Concentus 2.2.2? | **0 tramas falladas**; cada paquete → exactamente 320 muestras | sonda |
| **El collar suprime el silencio** | hablando seguido: 43,3 tramas/s y **86,7 %** del reloj · sin hablar: 27,0/s y **54 %** | dos corridas comparadas |
| **El contador NO avanza en el silencio** | numeración **contigua a través de un parón de 2,01 s** | histograma de huecos |
| Reparto de los huecos | 80 % < 30 ms · 14,5 % de 30–100 ms · 36 parones > 100 ms sumando 5,68 s | histograma |
| TFM necesario para WinRT | `net8.0-windows` **no basta**; con `net8.0-windows10.0.19041.0` compila limpio | build de la sonda |
| LED | rojo = encendido sin conectar · azul = conectado | [doc de Omi](https://help.omi.me/en/articles/12847359-omi-device-troubleshooting-guide) |

**Lo que el log NO aportó, y hay que decirlo:** el más reciente es `u-20260808.log`, de hace cinco
días. La ruta de voz no se ha ejercitado desde entonces, así que aquí el log no mide el presente —
lo mide la sonda. Una spec que cita un log que no miró es peor que una que dice qué no pudo mirar.

### El repo, que no está donde lo dejaron las reglas

| Qué | Medida | Fuente |
|---|---|---|
| Contratos que existen | **tres**, no uno | `nucleo/Contrato`, `mapeador/Contrato`, `tests/ContratoDelGrafo` |
| El contrato del grafo, hoy, en esta rama | **20/20 · CONTRATO INTACTO** | corrida del 2026-08-13 |
| Patrón que sigue la rama | subsistema en proyecto **`net8.0` puro** fuera de `windows-client`, con su propio contrato, «para poder juzgarlo sin abrir una ventana» | `nucleo/Grafo`, `mapeador/Pulso` |
| Qué juzga la compuerta | `verificar.ps1` nivel 2 y `contrato.yml` corren **solo `contrato-del-grafo.ps1`** | lectura de los dos |
| Los dos contratos nuevos | **no están** en `verificar.ps1` ni en CI; se corren a mano | ídem |

## Por qué esto va dirigido por especificación

Porque el sistema que hay que construir **se daría por bueno a sí mismo**. Una fuente de audio que
entrega tramas puede reportar «500 tramas entregadas, 0 errores» estando comiéndose la mitad del
reloj — es literalmente lo que hizo la primera corrida de la sonda: `0 fallos`, señal real, y **54 %
del tiempo perdido**. Un test escrito después de ese código habría medido tramas entregadas, que es
la métrica que el bug no rompe.

Es el aprendizaje nº10 con otra cara: *lo peor no es que falle, es que parezca que funcionó.*

**La regla del flujo, y no tiene excepciones:** ninguna línea de producción entra antes que la
promesa que la juzga. Cada fase empieza con el contrato ROTO y termina con el contrato INTACTO.

## La forma, que la fija la spec y no el gusto

CI no tiene Bluetooth ni collar. Si la reposición de silencio vive dentro del callback de BLE, **la
promesa 2 nace incomprobable** y toda esta spec se vuelve decorativa. Eso obliga a partir en dos, que
es además el patrón que la rama ya estableció con `nucleo/Grafo` y `mapeador/Pulso`:

| Dónde | Qué | TFM | ¿Se puede juzgar sin hardware? |
|---|---|---|---|
| `voz/Omi/` (nuevo) | trama + instante de llegada → muestras. Códec, cabecera, reposición del silencio | `net8.0` puro | **sí** |
| `windows-client/` | el transporte: BLE, WinRT, suscripción, reconexión | `net8.0-windows10.0.19041.0` | no → nivel 4 |

Sólo `windows-client` sube de TFM. El contrato de la voz nunca toca `U.dll`, así que el salto a WinRT
no puede tumbarlo.

## La especificación

Las promesas van a un **contrato propio, `voz/Contrato`**, numeradas desde 1 — igual que el núcleo
nuevo y el mapeador empiezan por su 1. No van a `tests/ContratoDelGrafo` por dos razones medidas:
su arnés entrega un `SurfaceMap` a cada promesa, y estas no tienen nada que ver con el grafo; y ese
contrato es el único que referencia `U.dll`, que es justo el que sube de TFM.

| # | Promesa | Fase |
|---|---|---|
| 1 | el códec se le pregunta al collar, y uno que no se sabe decodificar se rechaza diciendo cuál era | 3 |
| 2 | el silencio que el collar no transmite se repone: lo entregado dura lo que duró el reloj | 4 |
| 3 | la carita no distingue de dónde viene la voz: el collar y el micrófono local entregan el mismo formato | 5 |
| 4 | perder el collar a media sesión no deja muda a Ü: la voz vuelve al micrófono local | 6 |
| 5 | un paquete corto o vacío no produce muestras ni tumba la sesión | 3 |

**La que cierra el asunto es la 2.** Mientras no exista, todo lo demás es cosmético: el audio llega,
decodifica y suena perfecto en un WAV — y la carita no contesta nunca, porque Gemini Live decide el
turno oyendo la pausa y no hay ninguna. Es la única del grupo cuyo síntoma («no me responde») no se
parece en nada a su causa.

### Con qué se juzga cada una

| # | Cómo se juzga | Necesita collar |
|---|---|---|
| 1 | bytes de códec `0`, `20`, `21` y `7`; el `7` se rechaza **nombrando el 7** | no |
| 2 | secuencia sintética: dos tramas separadas 2.000 ms **con numeración consecutiva**. Tienen que salir ~2 s de muestras, no 40 ms | no |
| 3 | las dos fuentes contra el mismo contrato de formato: 16 kHz, 16 bits, mono | no |
| 4 | se simula la desaparición de la fuente; queda micrófono local vivo y una línea en el log | no |
| 5 | paquetes de 0, 2 y 3 bytes; cero muestras, cero excepciones | no |
| — | que el audio real se entienda, y que la carita conteste | **sí** → nivel 4, a mano |

La numeración consecutiva en el fixture de la 2 **no es un detalle**: es lo único que impide
cumplirla contando paquetes en vez de mirando el reloj. Medimos que el contador no avanza durante el
silencio, así que contar paquetes es la implementación que parece correcta y no lo es.

### La fase de arnés va primero, y son dos cosas

1. **El contrato nuevo tiene que entrar en la compuerta.** Hoy `verificar.ps1` y `contrato.yml`
   corren sólo `contrato-del-grafo.ps1`; un cuarto contrato fuera de ahí es un juez que nadie
   convoca. Sin esto, las cinco promesas pueden estar rojas y `verificar.ps1` decir OK.
2. **El salto de TFM toca al juez viejo.** `tests/ContratoDelGrafo` apunta a `net8.0-windows` y
   referencia `U.dll` **por binario**. Si deja de compilar o de cargar contra un `U.dll` de TFM
   mayor, el veredicto que sale es «CONTRATO ROTO» sin haber juzgado nada — aprendizaje nº17, ya
   pagado dos veces aquí. **Criterio de terminado: el contrato del grafo sigue dando 20/20**, que es
   exactamente lo que da hoy (medido el 2026-08-13), sin una línea de Omi de por medio.

## Las fases

Pendiente de `/fases`. Lo que ya está decidido por el diagnóstico:

- **Fase 1** — el contrato de la voz entra en `verificar.ps1` y en CI. Vacío, pero convocado.
- **Fase 2** — el TFM, solo. Termina cuando el contrato del grafo sigue en 20/20.
- **Fase 4 (promesa 2) es el corazón.** Las demás se pueden reordenar; esa no se puede posponer.

## Lo que NO entra

| Fuera | Por qué |
|---|---|
| La nube de Omi: webhooks, transcripción, memorias | El audio entra por BLE directo. Meter la nube reintroduce el móvil como pasarela y los 10 s de intervalo ya descartados |
| Memoria ambiental / resúmenes de conversación | Es el producto de Omi, no el de Ü. Ü opera aplicaciones |
| Elegir el collar desde la interfaz | Primero que funcione con una variable de entorno. La UI es la zona de choque alto entre los tres |
| Que Ü suene **por** el collar | La salida se queda en los altavoces del PC. Otra spec y otra medición |
| Emparejar más de un collar | Un aparato, un usuario |
| Reencaminar el eco (`_gananciaEco`) | Se **mide** con el collar puesto y se decide después. A ciegas es el aprendizaje nº14 |
| Meter los contratos del núcleo y del mapeador en la compuerta | Es un arreglo que hace falta, pero no es de esta spec. Va a Hallazgos |

## Hallazgos

- **2026-08-13** — El collar suprime silencio y su contador no avanza mientras calla. Descubierto
  midiendo, no leyendo: el protocolo no lo documenta. De aquí sale la promesa 2, la única que no
  estaba en la petición original.
- **2026-08-13** — `.claude/rules/flujo-sdd.md` dice que las promesas van a
  `tests/ContratoDelGrafo/Contrato.cs` «numeradas en continuación». **Esa regla ya no describe el
  repo**: esta rama creó `nucleo/Contrato` y `mapeador/Contrato`, cada uno numerando desde su 1.
  La regla hay que actualizarla o el siguiente que llegue meterá promesas donde no van.
- **2026-08-13** — `verificar.ps1` y `contrato.yml` juzgan **uno de los tres** contratos. Los del
  núcleo y el mapeador se corren a mano, así que pueden estar rojos mientras la compuerta dice OK.
  No es de esta spec, pero es la clase de guardia que se cree puesto (aprendizaje nº18).

## Cierre

- [ ] Las cinco promesas verdes en el contrato de la voz
- [ ] El contrato del grafo sigue en 20/20 tras el salto de TFM
- [ ] `.\scripts\verificar.ps1` pasa, con evidencia en `out\evidencia.md`
- [ ] Probado en ≥2 pantallas, con nombre: …
- [ ] Escuchado a mano: una frase dicha al collar llega a la carita y **se contesta**
- [ ] Estado de este documento: **implementado** (AAAA-MM-DD)
