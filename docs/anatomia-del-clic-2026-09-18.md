# La anatomía del clic, medida — fase 1 del plan de clics en tiempo real

> 2026-09-18 · rama `jose/la-anatomia-del-clic` · sobre `main@88b0a8e` (con #83 dentro). Es la fase 1 de
> `docs/plan-clics-en-tiempo-real.md` (rama `jose/el-decisor-se-puede-cambiar`, PR #84): **medir el techo
> y despejar la duda que cambia el diseño**. No trae promesas de contrato: trae un guion repetible y tres
> números que no se sabían.

## 1. El guion: `scripts/anatomia-del-clic.ps1`

```powershell
.\scripts\anatomia-del-clic.ps1 -Log C:\U-decisor\local\U\logs\u-20260918.log -Desde 03:22 -Hasta 03:36
```

Lee el log y, por cada acto (`map_take`, `map_decidir`, `map_type`, `map_go_to`, `map_open_app`,
`map_batch`), saca el total que anota el despacho (`← (N ms)`) y tres fases por los sellos de las
líneas intermedias: **antes** (de `→` a la primera señal de la mano o del decisor: leer la pantalla y
la coreografía), **mano** (de `Execute` a `resultado acción`) y **después** (del resultado a `←`:
esperar el cambio, la foto del álbum, el inventario). Y las señales: ms de Jev, homónimos, «hice 0
de», `SATURADO`, «miré otra vez», «leí la ventana». Empareja bien el `←` de `map_esto_es` que llega
en paralelo (el error que la 029 cometió y la 030 corrigió).

Su límite, dicho: las fases van en **segundos enteros**, que es la resolución del log; el total sí
va en ms. Lo que necesite décimas se mide con su propio reloj en el código.

Se corre **antes y después de cada corte**. Los tres números que se comparan (spec 029 §7):
segundos de herramienta, vueltas del modelo, y segundos en llamadas que acabaron en nada.

## 2. La corrida del dueño con Jev, medida con el guion (03:22-03:36)

33 actos: 14 `map_decidir` (los primeros 11 en simulado, los últimos 3 con `jev-1.13.0`), 12
`map_take`, 7 `map_go_to`. Antes de #83 (la coreografía seguía puesta).

| Herramienta | n | total (mediana) | antes | mano | después | perdidos |
|---|---|---|---|---|---|---|
| `map_take` | 12 | **4.895 ms** | **4 s** | 0 s | 1 s | 0 |
| `map_decidir` | 14 | **5.897 ms** | 1 s | 0 s | 2 s | **4 homónimos, 4 «0 de N»** |
| `map_go_to` | 7 | 769 ms | — | — | — | 0 |

- **Herramienta: 147,4 s** en 14 minutos. **En llamadas que acabaron en nada: 26,5 s** (los cuatro
  `map_decidir` que chocaron con homónimos).
- **`SATURADO` dentro de actos: 4 vueltas descartadas** — el observador no da abasto mientras se
  actúa (spec 029 §6).
- El **«antes» de 4 s** de cada `map_take` es la coreografía de la 014 aplicada fuera de una
  comprobación (tarjeta + pausa de lectura + señalar). **#83 la quita**: es lo primero que el guion
  tiene que ver bajar en la siguiente corrida.
- El **«después» de 1-3 s** es esperar el cambio (presupuesto de 1.800 ms), la foto del álbum
  (136 KB) y volver a leer el inventario. Es la fase 4 del plan (esperar es suscribirse).
- Con Jev de verdad: 582 · 186 · 245 ms del total de 7.819 · 6.402 · 6.506. **El modelo es el
  3-8 % del clic.**

## 3. Jev con 20, 60 y 160 puertas: el tamaño del `state` no importa

Cinco llamadas por tamaño, `jev-1.13.0`, tres preguntas por llamada (`choice` puerta · `noul`
«¿objetivo cumplido?» · `noul` «¿es peligroso?»), la puerta buena en mitad de la lista entre
puertas sintéticas:

| puertas | bytes | input_tokens | ms mín | **ms mediana** | ms máx | elegida | confianza |
|---|---|---|---|---|---|---|---|
| 20 | 1.643 | 875 | 300 | **345** | 1.066 | la correcta | 0,99 |
| 60 | 3.729 | 1.913 | 312 | **322** | 552 | la correcta | 0,99 |
| 160 | 9.087 | 4.635 | 328 | **350** | 683 | la correcta | 1,00 |

Tres cosas que esto decide:

1. **Una llamada por paso cuesta ~330 ms, con 20 o con 160 puertas.** Mandar el inventario entero
   de SAP (39 campos + 21 botones) no se paga en latencia. El máximo de 1.066 fue la primera
   llamada (conexión fría): un `HttpClient` vivo la evita.
2. **Las tres preguntas viajan juntas sin coste.** `cumplido` contestó 0,10-0,11 y `peligro`
   0,24-0,32 en las 15 llamadas: el tramo puede preguntar «¿ya llegué?» y «¿esto es irreversible?»
   en el mismo viaje que «¿qué puerta?».
3. **A 20/s de cupo y 330 ms de ida y vuelta, el techo de Jev es ~3 decisiones/s.** El clic entero
   hoy tarda 5-8 s. Jev no es el cuello de botella en ningún escenario del plan.

## 4. La duda que cambia el diseño, contestada sin sonda

¿Puede la voz emitir `map_alto` mientras la llamada de `map_tramo` sigue pendiente?

**No, y ya estaba medido.** `voz/Realtime/ProtocoloGptLive.cs` (comentario sobre
`TopeDeUnResultado`, 2026-09-12, contra el servidor real): un resultado que no cupo dejó la llamada
pendiente, y el servidor contestó `function_call_outputs_required` — **«cada `response.create` de
la sesión falló, con la voz hablando y el delegado ya sin hacer nada»**. Con una llamada pendiente,
GPT-Live no produce respuestas: ni habla, ni llama otra herramienta.

Consecuencia para el plan, y es la que ya prefería: **el tramo corre desprendido.** `map_tramo`
contesta al instante «en marcha» y devuelve el turno; el tramo avanza por detrás y va contando
cada paso (al notch, y al log); la voz queda libre para `map_alto`, para hablar contigo y para
pedir el siguiente tramo. Lo que hace falta para que eso sea correcto: un `map_tramo_estado` (o
que el siguiente acto cuente lo que el tramo hizo) para que Luna no pregunte a ciegas, y que
`map_alto` salte la cola del despacho (`ServidorMcp` encola todo lo que llega detrás).

Lo que **no** contesta esta evidencia: si el audio del usuario que llega durante una llamada
pendiente se conserva y se atiende al volver el turno. Con el tramo desprendido deja de importar.

## 5. Lo que cambia en el plan

- Fase 1: hecha (este documento y el guion). La duda de la voz no necesita sonda.
- Fase 3: `map_tramo` **desprendido desde el diseño**, no como respaldo.
- Fase 2: se confirma que la palanca es **puertas únicas** (26,5 s perdidos en homónimos en una
  corrida de 14 minutos) y **una llamada con tres preguntas** (~330 ms, medido).
- Fase 4: el «después» de 1-3 s y el `SATURADO` son suyos; el guion ya los cuenta para el
  antes/después.
