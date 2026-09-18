# La pantalla asentada no se espera

> Spec 040 · 2026-09-18 · rama `jose/la-pantalla-asentada-no-se-espera` · promesa **299**
>
> Nace de la primera prueba del dueño con los cortes 296-298 en `main`: «increíblemente más rápido»,
> pero «todavía hay errores al hacer clics» y «se quedaba quieto».

## Lo que se midió antes de escribir código

Dos sesiones de voz reales del dueño (2026-09-18, 12:09-12:20 sin TypeSafe y 12:34-12:41 con él),
123 llamadas a herramientas en total:

| | sin TypeSafe | con TypeSafe |
|---|---|---|
| `map_take` que no pulsó nada | 9 de 16 | 6 de 11 |
| de esos, por una puerta que **no estaba** («no lo conozco» / «lo conozco pero AHORA no lo veo») | 6 · 25,9 s | 1 · 4,4 s |
| lo que tarda cada uno de esos «no» | 4,2-4,6 s | 4,4 s |
| lo que tarda un `map_take` que sí pulsa (mediana) | 1,2 s | 1,2 s |

El modelo de voz nombra puertas que no existen —«Dan Kost», «TypeSafe AI», «Sin título», «Enter»— o
que el terreno recuerda de otra visita a la misma dirección (los resultados de **otra** búsqueda en
`google.com/search`). Decirle que no le cuesta al arnés **más del triple que pulsar**.

El log reconstruye en qué se van esos 4,3 s (`map_take exit=Dan Kost`, 12:10:20):

```
[12:10:20] mano: señalar «Dan Kost»: leí la ventana en 43 ms (45 elemento(s))
[12:10:20] trabajo: miré otra vez «web://instagram.com/direct/requests» antes de rendirme: 45 elemento(s) en 34 ms
[12:10:24] trabajo: miré otra vez «web://instagram.com/direct/requests» antes de rendirme: 45 elemento(s) en 39 ms
[12:10:24] mapa-mcp: ← (4171 ms) … «Dan Kost» no lo conozco …
```

Mirar cuesta ya 34-89 ms (promesas 297 y 298). Las dos miradas ven **lo mismo** —45 y 45— y entre
una y otra hay cuatro segundos de espera pura (`EsperaMaximaMs = 4000` en la app). La pantalla estaba
quieta y la puerta no estaba: esperar no podía traerla.

**Lo que NO se pudo medir, y se dice:** si esa espera sirvió alguna vez —una puerta que no estaba al
pedirla y apareció esperando—. En los logs anteriores a hoy todo `map_take` tardaba ~5 s por el arnés,
así que «tardó mucho» no distingue «esperó a la puerta» de «el clic era lento», y la compuerta no
deja línea cuando la espera acierta. Por eso **la espera no se quita: se condiciona**, y este corte
deja la línea que faltaba para poder medirlo la próxima vez.

## La regla

La espera existe para la pantalla que **está cargando**. Una pantalla que está cargando cambia entre
dos miradas; una asentada, no. Entonces:

1. La primera mirada es la de siempre, en el acto (promesa 264).
2. La segunda se adelanta: a los ~400 ms en vez de al agotar el presupuesto.
3. **Si las dos ven lo mismo** —la misma ubicación y las mismas puertas vivas—, la pantalla está
   asentada: la compuerta se rinde ahí, diciendo exactamente lo que decía.
4. **Si ven cosas distintas**, está cargando: se espera el presupuesto entero, como hoy, y antes de
   rendirse se mira una vez más — solo si mirar es barato, para que la 264 siga en pie (una mirada
   lenta no se repite).
5. Sin poder mirar (SAP, que se lee por su API; o una mirada que falla), **nada cambia**.

Una web que no para de moverse (un reloj, un vídeo) nunca parecerá asentada: se queda con el
comportamiento de hoy. Es el lado seguro.

## La promesa

**299.** Una pantalla asentada no se espera: si la puerta pedida no está y dos miradas seguidas ven lo
mismo —la misma ubicación y las mismas puertas vivas—, la compuerta se rinde en el acto y no al agotar
el presupuesto, diciendo lo mismo que decía; si entre las dos miradas la pantalla cambió, está
cargando: se espera el presupuesto entero y la puerta que aparece se pulsa; sin poder mirar, nada
cambia; y en los dos casos la compuerta deja dicho cuánto esperó y por qué dejó de esperar.

## Fases

| Fase | Qué | Promesa | Archivo |
|---|---|---|---|
| 0 | la promesa, en rojo | 299 | `tests/ContratoDelGrafo/Contrato.cs` |
| 1 | la regla y su línea de log | 299 verde; 56, 80, 245 y 264 intactas | `windows-client/src/Navigation/RecorrerSegunElNucleo.cs` |

## Lo que queda fuera

- **Que el modelo deje de inventar nombres.** Es de prompt y de catálogo (`ConversacionEnVivo`), que
  tiene abierto la sesión de la spec 039; y es justo lo que `map_decidir` resuelve, porque Jev elige
  por número de una lista real. Se coordina, no se toca aquí.
- **«Barra de direcciones y de búsqueda»**, que el terreno ofrece y la mano no encuentra (4 fallos en
  la prueba). Es otro bug, con su promesa.
- **El recuerdo que no se guarda** porque `map_esto_es` corre cuando la pantalla ya cambió (la mitad
  de las veces). Otro bug, con su promesa.
- El umbral del tramo (0,62 frente a 0,70): se mide con más casos antes de moverlo.

## Lo que pasó al implementarla

Promesa escrita y vista en rojo antes que el código (`8c4686e`: `PENDIENTE`, 265 verdes y solo ella).
Verde con `7124ff0`: contrato intacto, 266; la 56, la 80, la 245 y la 264 siguen en pie — la 264 exige
que mirar no sea un bucle, y la regla cabe dentro: con la pantalla quieta son dos miradas, como antes.

**Sabotaje, comprobado que se aplicó** (el `diff` mostró la línea): sin la comparación de huellas, solo
cae la 299 — «tardó 1203 ms con un presupuesto de 1200», y el diario dice «la pantalla se movió entre
miradas: se esperó entero». Revertido tras commitear.

### Nivel 4: dos pantallas, el mismo build de caja de pruebas antes y después

El «no» de una puerta que no está, pedido por MCP a la app real:

| Pantalla | Puerta pedida | `main` (12d125a) | rama (7124ff0) |
|---|---|---|---|
| Explorador, `C:\` | «Dan Kost» | 4.506 ms | **1.130 ms** |
| Explorador, `C:\` | «Carpeta Que No Existe» | 4.802 ms | **992 ms** |
| Chrome, una página de ajustes | «Dan Kost» / «Discusión» | 4.209 ms | **620 ms** |
| Chrome, una página de ajustes | «TypeSafe AI (+2) - Related results» | 4.149 ms | **587 ms** |
| Chrome, una página de ajustes | «Enter» | 5.485 ms | **636 ms** |
| Explorador, `C:\` | «Descargas» (existe) | — | 1.367 ms, **pulsó** y llegó a Descargas |

```
[13:20:01] compuerta: «Dan Kost» no está y la pantalla está asentada (dos miradas vieron las mismas puertas vivas): me rindo a los 516 ms, sin agotar los 4000
[13:20:02] compuerta: «Enter» no está y la pantalla está asentada (dos miradas vieron las mismas puertas vivas): me rindo a los 532 ms, sin agotar los 4000
[13:20:07] compuerta: «Dan Kost» no está y la pantalla está asentada (dos miradas vieron las mismas puertas vivas): me rindo a los 781 ms, sin agotar los 4000
```

**Lo que el nivel 4 NO cubrió, y se dice:** la rama de «está cargando» —la pantalla cambia entre las dos
miradas, se espera entero y la puerta que aparece se pulsa— la juzga el contrato (casos 3 y 4) pero no
se ejercitó sobre una página real cargando: el dueño estaba trabajando en Chrome y no se condujo su
navegador. La línea nueva del log (`«X» no estaba al pedirla y apareció tras N ms`) es la que lo dirá
con el uso: si en una semana no aparece nunca, la espera de 4 s sobra entera; si aparece, dice cuánta
hace falta.

**Cuántos sitios tienen la clase de error** (esperar el presupuesto entero sin mirar si la pantalla se
mueve): 3 — esta compuerta, `EsperarACambiar` de `PulsarSegunElNucleo` y la llegada del batch. Este
corte arregla 1. Los otros dos esperan un **cambio**, no una puerta, y ahí «no se mueve» es justo lo
que todavía no se sabe: no se les puede aplicar la misma regla sin medir.

### Otro hallazgo de esta corrida, sin tocar

`map_go_to` hacia una web, con el navegador en una pestaña de resultados, tardó **11.743 ms** en
contestar «no hay ningún camino aprendido de «search» hasta ahí» (y ya había pasado en la prueba del
dueño: 11.409 ms desde la terminal). Una web es direccionable (promesa 66): no debería necesitar camino
aprendido, ni tardar once segundos en decir que no. Es el siguiente candidato.
