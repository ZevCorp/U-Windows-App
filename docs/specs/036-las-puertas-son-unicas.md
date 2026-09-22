# Las puertas son únicas: Jev elige por número, pulsa por selector, y se enciende con un botón

Estado: **implementada, contrato intacto (287-290), nivel 4 en dos pantallas; el botón pendiente de pulsar a mano** · 2026-09-18 · Rama: `jose/las-puertas-son-unicas`

> Fase 2 de `docs/plan-clics-en-tiempo-real.md`. Sale de dos medidas de la fase 1
> (`docs/anatomia-del-clic-2026-09-18.md`): en la corrida del dueño con Jev, **2 de 3 decisiones
> chocaron con homónimos** y se perdieron **26,5 s** en llamadas que acabaron en nada; y Jev
> contesta **tres preguntas en ~330 ms** con 20, 60 o 160 puertas. Y de un pedido del dueño: «un
> botón en el panel que haga switch entre con TypeSafe y sin TypeSafe».

## Lo que pasa hoy, medido

`map_decidir` le da a Jev **etiquetas**. En openai.com había dos «Investigación» (Hyperlink y
Button): Jev eligió bien, `Take` recibió la etiqueta, la mano contestó «hay 2 puertas vivas para
«Investigación»» y **el paso se perdió entero** (7,8 s), y Luna gastó otra vuelta con `which`. Tres
veces en una corrida (03:33:18, 03:33:47, 03:34:28).

La mano ya sabe pulsar sin ambigüedad: `EsperarloVivo` resuelve un `exit` **por selector exacto
antes que por nombre** (`RecorrerSegunElNucleo.cs:392`). Lo que falta es que el decisor hable en
selectores.

Y Jev devuelve, con la elegida, **la probabilidad de cada opción**: la segunda mejor viene gratis
en la misma respuesta. Hoy se tira.

## Las promesas

| # | Lo que el sistema promete |
|---|---|
| **287** | Jev elige entre puertas **únicas y numeradas** —«3) Investigación (Hyperlink)»— y lo elegido se acciona **por su selector**, nunca por su etiqueta: dos puertas con el mismo nombre no chocan, la mano recibe el selector de la elegida, y la cuenta la nombra por su etiqueta y su número. |
| **288** | La **segunda mejor sin otra llamada**: si la elegida no está viva al ir a pulsarla, se prueba la siguiente por probabilidad si supera el mínimo, como mucho una vez más; a Jev se le preguntó **una sola vez**; y la cuenta dice qué se probó y por qué. Un homónimo o un fallo de la mano que no sea «no está» no dispara la segunda. |
| **289** | **Una llamada, tres preguntas**: el cuerpo lleva `puerta` (choice), `cumplido` (noul: ¿el objetivo ya está cumplido en esta pantalla?) y `peligro` (noul: ¿accionar la elegida es irreversible?); con `cumplido` alto no se acciona y se dice que el objetivo ya está; con `peligro` alto no se acciona y se dice por qué; y una respuesta sin esas dos (un transporte viejo) sigue valiendo. |
| **290** | **El interruptor en vivo**: encender el decisor deja `map_decidir` en el catálogo y un decisor en el mapa; apagarlo deja el catálogo **byte a byte** como sin decisor y el mapa sin decisor; las dos cosas re-mandan el catálogo a la voz; pedir encender sin clave ni modo válido se queda apagado y dice por qué; y el estado se lee en una línea —en el botón y en la línea de estado, nunca al pasar el ratón (164)—. |

## Cómo se juzga, sin pantalla

- 287: un mapa con las puertas inyectadas `(selector, etiqueta, tipo)` con dos «Detalles» (Button y
  RadioButton); el decisor falso recibe la lista y se comprueba que son ids numerados y únicos;
  contesta «2) Detalles (RadioButton)» y la mano falsa recibe **el selector** del segundo.
- 288: un decisor falso con alternativas {A 0,60 · B 0,35 · C 0,05}; la mano falsa contesta «no lo
  veo» a A y pulsa B; se cuenta **una** llamada; C nunca se intenta; y con la mano contestando
  homónimos para A, no se prueba B.
- 289: `CuerpoDeEleccion` con las tres preguntas; `ElDecisor.Elegir` con un transporte que trae
  `cumplido` 0,9 → no actúa y lo dice; con `peligro` 0,8 → no actúa y lo dice; sin las dos → actúa
  como antes.
- 290: `InterruptorDelDecisor` con delegados falsos y con el estático real de la voz: encender/apagar
  y el catálogo antes y después; encender con `Quien=luna` se queda apagado.

Sobre la máquina (nivel 4): el Explorador tiene dos «Detalles» (Button y RadioButton) —el caso real de
la 287— y Configuración; el botón se pulsa por UI Automation y se comprueba `tools/list` por MCP.

## Las fases

| Fase | Qué deja | Promesa |
|---|---|---|
| 1 | Puertas con selector en `PuertasDeAhora`, ids numerados hacia el decisor, pulsar por selector | 287 |
| 2 | Las tres preguntas en el cuerpo, `Alternativas`/`Cumplido`/`Peligro` en la decisión, la segunda mejor | 288, 289 |
| 3 | `InterruptorDelDecisor` y el botón junto a `Learn`/`Work` | 290 |

## Decisiones

| Decisión | Por qué |
|---|---|
| El botón se llama **Jev** y no «Live» | En el panel ya existe la idea de «Live» para la consulta clínica (comentario en `FaceWindow.xaml`); dos «Live» con dos significados es la caja que miente |
| La segunda mejor solo si su probabilidad ≥ 0,25, y como mucho una vez | Las probabilidades suman 1: exigirle el umbral de confianza (0,70) a la segunda es no probarla nunca; probar la tercera es adivinar |
| `cumplido` ≥ 0,70 no acciona; `peligro` ≥ 0,50 no acciona | El mismo umbral que la confianza para «ya llegué»; y ante lo irreversible se pide menos evidencia para parar, no más |
| El id que ve Jev es «N) etiqueta (tipo)», y el selector no viaja | Jev decide por lo que una persona lee; el selector es cromo técnico que solo sirve a la mano — y no gasta tokens |

## Nivel 4, medido (2026-09-18, 05:24-05:30)

Instancia aislada `C:\U-puertas\bin` con `U_DECISOR=simulado`, llamadas por MCP. **Dos pantallas.**

```
05:27:54  tools/list: 29 herramientas; map_decidir SI
05:28:05  → map_decidir objetivo=ver los archivos en detalles  (4767 ms)
          elegida «Detalles» (1) con confianza 1.00: hice los 1 paso(s): pulsé «Detalles» y la pantalla no cambió.
          HOMONIMOS? no · PULSO POR SELECTOR? si          ← el caso real de la 287
05:28:10  → map_decidir objetivo=abrir la carpeta Descargas  (3490 ms)
          elegida «Descargas» (7) con confianza 1.00: … ahora estás en «uia://explorer.exe/descargas». Queda aprendido.
05:28:16  Estás en «uia://SystemSettings.exe/configuración#bluetooth-y-dispositivos»
05:28:21  → map_decidir objetivo=abrir Bluetooth y dispositivos  (5727 ms)
          elegida «Bluetooth y dispositivos» (9) con confianza 1.00: … pulsé «Bluetooth y dispositivos»
--- log de la app ---
[05:27:48] decisor: interruptor → encendido: simulado (jev-latest), umbral 0.70.
[05:25:06] decisor: «uia://explorer.exe/disco-local-c» · 61 puerta(s) · 6 ms → ACCIONA «1) Detalles (RadioButton)» conf=1.00
[05:25:19] decisor: «uia://SystemSettings.exe/configuración#inicio» · 55 puerta(s) · 0 ms → ACCIONA «9) Bluetooth y dispositivos (ListItem)» conf=1.00
```

En la corrida anterior (05:25) Configuración se abrió por `map_open_app` y «9) Bluetooth y dispositivos»
**navegó** a `#bluetooth-y-dispositivos`; en esta ya estaba allí y «la pantalla no cambió», que es lo
correcto. Las tres decisiones se accionaron **por selector, sin lista de homónimos**.

**Lo que NO se pudo probar en vivo: el botón.** La ventana del muelle no aparece entre las ventanas
que UI Automation expone del proceso (solo la carita y el notch), así que el botón no se encontró ni
desplegando el muelle con el cursor. El interruptor está probado por el contrato (290, con el estático
real de la voz) y su cableado al arrancar se ve en el log (`interruptor → encendido`); **pulsarlo a mano
queda pendiente** y se dice como pendiente, no como hecho.

## Lo que queda fuera

- `map_tramo` (fase 3 del plan): aquí sigue siendo un paso por llamada; la segunda mejor y las tres
  preguntas son lo que el tramo va a usar por paso.
- Medir con Jev real sobre SAP: exige el hospital.

## Reescrituras posteriores

- **2026-09-22 · la 289** se reescribe en la spec 046 (el número no se recicla). Su última cláusula —«una
  respuesta sin esas dos sigue valiendo»— era exactamente la compuerta abierta: `Noul` devolvía 0 cuando
  la noul faltaba y las compuertas cortan por arriba, así que 0 dejaba pasar (medido en la revisión del
  21-09: con `peligro` fuera de [0,1] convertido a 0 se pulsaba «Grabar»). Desde la 046: «una respuesta
  sin alguna de las dos, o con una fuera de [0,1], no se acciona y dice cuál falta». El fixture pasa a
  mandar `probabilities` completas (Σ = 1) y el caso `viejo` invierte su aserción.
