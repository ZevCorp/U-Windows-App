# Esperar es suscribirse: un solo lector de pantalla, y el paso que no espera lo que ya pasó

Estado: **propuesta, sin código** · 2026-09-18 · Rama de docs: `docs/spec-038-esperar-es-suscribirse`

> Fase 4 de `docs/plan-clics-en-tiempo-real.md`: la que abarata cada clic. Sale del reloj por fase que
> el tramo (spec 037, PR #87) deja en el log desde `d3238f8`, medido sobre el Explorador y
> Configuración el 2026-09-18 a las 05:51. Y de la spec 029 §6, que ya dejó diseñado «el observador
> único» y recomendó no hacerlo hasta tener los cortes 1-5 dentro y volver a medir. Ya están, y esto
> es la medida.

## Lo que mide el reloj por fase

Un paso del tramo son tres fases: **leer** las puertas de ahora, **decidir** (Jev ~330 ms; simulado 0),
y **pulsar**, que dentro lleva resolver el elemento, el gesto, y **esperar a que cambie la pantalla**.

| Paso | ¿Cambió? | leer | decidir | pulsar | total |
|---|---|---|---|---|---|
| «Descargas» (7) | sí | 987 | 2 | **555** | 1,5 s |
| «Descargas» (7) | **no** | 1.343 | 0 | **6.157** | 7,5 s |
| «Descargas» (7) | sí | 959 | 0 | **2.824** | 3,8 s |
| «Bluetooth y dispositivos» (9) | no | 235 | 0 | 2.249 | 2,5 s |

Tres hechos, y el orden en que pesan:

1. **Un paso que no cambia la pantalla cuesta 6 s en «pulsar».** Es `EsperarACambiar` agotando su
   presupuesto de 1.800 ms, la repetición única de la promesa 248 («un clic que no movió nada se
   repite una vez cuando el terreno sabía que esa puerta lleva a algún sitio»), y otra espera igual.
   En un tramo dirigido por el simulado —que no tiene `cumplido`— es el paso más frecuente; con
   Jev y `cumplido` ≥ 0,70 debería ser raro, **pero un «Guardar» o un botón que hace su trabajo sin
   navegar lo paga siempre**.
2. **Leer cuesta ~1 s por paso** (400 elementos en el Explorador; 235 ms en Configuración con 55).
   Se lee una vez por paso para decidir, y `map_what_i_see` vuelve a leer al final. El observador
   único ya lee la pantalla por su cuenta (`MapaVivo` cada 250-4000 ms) y descarta vueltas
   (`SATURADO ×4` en la corrida del dueño): **se lee más de lo que se usa y se usa menos de lo que
   se lee.**
3. **Detectar el cambio cuesta entre 555 y 2.824 ms cuando sí cambió.** `EsperarACambiar` sondea
   `_donde()` cada 120 ms; la diferencia entre 555 y 2.824 no es la pantalla, es lo que cuesta cada
   `_donde()` (la spec 025 midió 2.771 ms de mediana en `map_where_am_i` antes de la memoria corta
   de 400 ms). Con un sondeo caro, «cada 120 ms» es mentira.

El primer hecho es el más caro y el más fácil; el segundo es el observador único; el tercero
desaparece con el segundo.

## Las promesas (propuestas)

| # | Lo que el sistema prometería |
|---|---|
| **296** | **Un paso del tramo no espera lo que ya pasó**: si al pulsar la puerta la pantalla cambió, se sigue en el instante en que se nota; y si Jev (o el terreno) dice que esa puerta **no navega** —hace su trabajo sin cambiar de pantalla—, no se espera el presupuesto ni se repite el clic: se sigue con lo que hay. La espera de 1.800 ms queda para lo que no se sabe. |
| **297** | **Una sola lectura de pantalla por tick, pregunte quien pregunte**: el tramo, `map_what_i_see`, la compuerta y el localizador leen la misma observación con su número de versión; dos preguntas dentro del mismo tick no leen dos veces; y el log no vuelve a decir `SATURADO` por lectores que se pisan. |
| **298** | **Esperar no lee**: esperar a que cambie la pantalla es esperar a que suba la versión del observador o se agote el presupuesto, y despierta en el instante del cambio, no en el siguiente sondeo del que espera. Mientras alguien espera, el observador acelera a ~120 ms; en reposo, 800. |
| **299** | **La compuerta de antes de actuar sigue mirando ahora** (spec 020): el observador no la sustituye ahí, porque ahí se juega la corrección; y SAP, que no admite la compuerta barata de hwnd+título, sigue por su camino con `Busy`. |
| **300** | **La detección no tarda más que la cadencia acelerada**: un cambio de pantalla se ve en menos de 250 ms desde que ocurre, medido con el reloj y no contando vueltas. |

## Por qué en este orden, y qué compra cada una

| Promesa | Compra (con los números de arriba) | Riesgo |
|---|---|---|
| 296 | los 6 s del paso que no cambia bajan a lo que tarde el clic (≈0,5 s) cuando se sabe que no navega; y el «cambió» de 2,8 s baja a la latencia real del cambio | bajo-medio: toca `PulsarSegunElNucleo.EsperarACambiar` y la 248; la 248 se conserva para lo que no se sabe |
| 297 + 298 | el ~1 s de leer por paso pasa a ~0 cuando la observación es fresca; el `SATURADO` desaparece; `_donde()` deja de costar por sondeo | **alto**: es el observador único de la 029 §6 —diez sitios que preguntan al crudo, tres observadores, el localizador en un temporizador de la interfaz—; se hace en su rama, con `MapaVivo` y `SurfaceLocator` delante |
| 299 | no compra tiempo: conserva lo correcto | — |
| 300 | es la medida que dice si 297/298 valieron | — |

Estimación honesta, a confirmar con el mismo reloj por fase: un paso que cambia baja de 1,5-3,8 s
a **~0,5-0,8 s**; un paso que no cambia y se sabe que no navega, de 7,5 s a **~0,8 s**. Diez clics:
de ~30 s a **~8 s**.

## Cómo se juzgaría, sin pantalla

- 296: `PulsarSegunElNucleo` con un `_donde()` falso que cambia a los 200 ms → «pulsar» termina en
  <300 ms; con un gesto marcado como «no navega» → no se espera ni se repite; sin saber → 1.800 como
  hoy (245 sigue intacta).
- 297/298: un observador con un lector falso contado: N preguntas en un tick → 1 lectura; un
  `EsperarCambio(presupuesto)` con el lector cambiando a los 200 ms → despierta a los ~200, no a
  los 1.800; y sin cambio, al presupuesto.
- 300: el reloj entre el cambio del lector falso y el despertar del que espera.

## Lo que NO se hace aquí

- Tocar la promesa 44 («pulsar y que no se mueva nada no se cuenta como llegada»): sigue. Lo que la
  296 cambia es cuánto se **espera** para saberlo, no qué se cuenta como llegada.
- Quitar el álbum de miradas al llegar (173 KB por pantalla): va en `MapaVivo`, en su propio hilo,
  y no está en el camino del clic; se anota por si el observador único lo absorbe.

## El orden que propongo

1. **296 sola, en su rama** (`jose/el-paso-no-espera-lo-que-ya-paso`): es la mitad del beneficio con
   un décimo del riesgo, y se mide con el reloj por fase que ya existe.
2. Volver a medir. Si el paso que cambia ya está en <1 s, el observador único (297-300) compra
   sobre todo el `SATURADO` y la limpieza; si sigue en 1,5-3 s, compra tiempo. Decidir con eso.
