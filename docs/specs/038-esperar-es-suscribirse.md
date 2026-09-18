# Esperar es suscribirse: un solo lector de pantalla, y el paso que no espera lo que ya pasó

Estado: **primer corte implementado (promesa 296); 297-300 propuestas** · 2026-09-18 · Rama: `jose/la-puerta-que-lleva-aqui`

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

## El primer corte, hecho y medido: la promesa 296 (2026-09-18, 08:35)

**La 296 quedó más estrecha y más segura que la propuesta de arriba**, porque el log dijo otra cosa que la
hipótesis. El paso de 6.157 ms no era «una espera larga»: eran **tres gestos con tres esperas** —clic, el
ENSAYO con doble clic FÍSICO, y la repetición de la 248— sobre una puerta que, desde «Disco local», el
paso anterior acababa de aprender que lleva a la pantalla donde ya estábamos. La regla (`LlevaAqui`, pura):
**la misma puerta, vista desde otra pantalla, lleva aquí → desde aquí no navega: un clic y una espera, sin
ensayo ni repetición.** Va después de la primera espera: si la pantalla sí cambia (un «Siguiente» que vive
en todas las páginas), manda lo que pasó.

No es «su destino es donde estoy»: el grafo guarda el destino por pantalla y rechaza las aristas a sí
mismas, así que esa frase no se cumple nunca.

| Paso que no cambia · «pulsar» | antes | ahora |
|---|---|---|
| total | **6.157 ms**, 3 gestos (uno, doble clic físico) | **2.094 – 3.772 ms**, 1 gesto |
| la mano | — | 239-282 ms |
| esperar el cambio | 3 × 1.800 | 1 × 1.801 (17-18 sondeos de «dónde»: ya es barato, ~100 ms cada uno) |
| consultar al terreno | — | <20 ms |

Paso que sí cambia: 524 ms, con **0 ms de espera** (el cambio se ve en el primer sondeo). Sabotaje: sin la
regla, `[clic · doubleclick · clic]`, tres esperas y «no cambió» a secas; solo cae la 296. Las 82, 83 y 248
siguen verdes. Y el detector de bucle del tramo (292) disparó en vivo: «pulsé la misma puerta tres veces».

**Nivel 4: DOS pantallas — el Explorador y Wikipedia en Chrome.** En el Explorador, dos corridas y cuatro
disparos de la regla en el log (`…lleva justo a donde ya estamos: ni lo ensayo ni lo repito`). La segunda
pantalla llegó después (2026-09-18, 09:28, con la rama rebasada sobre la 297): desde
«Discusión:Guatapé» se pulsa «Artículo», navega y el terreno aprende a dónde lleva; ya en el artículo se
pulsa «Artículo» otra vez:

```
[09:28:12] mano: ⏱ pulsar «Artículo»: la mano 366 ms · esperar el cambio 188 ms (2 sondeo(s) de «dónde») · cambió
[09:28:20] mano: ⏱ pulsar «Artículo»: la mano 554 ms · esperar el cambio 1808 ms (13 sondeo(s) de «dónde») · no cambió
[09:28:20] mano: «Artículo» no movió nada y el terreno sabe que lleva justo a donde ya estamos: ni lo ensayo ni lo repito
```

Un gesto y una espera; `map_decidir` entero, 3.060 ms. Los dos primeros intentos de segunda pantalla habían
fallado por motivos que no son de la 296: en Configuración el lector solo ve el marco
(`ApplicationFrameHost`, 4 puertas); en la portada de Wikipedia el enlace que hacía falta no estaba entre
las puertas ofrecidas. La lógica está además juzgada sin pantalla, con sus tres casos.

## Lo que el nivel 4 enseñó, y cambia el orden de lo que sigue

1. **Leer una página web cuesta ~5 s** (Wikipedia, `map_decidir` sin accionar: 5.240 y 4.901 ms — lectura
   pura). En el Explorador, 1,3-1,8 s. **Leer es ya el coste dominante de un paso**, por encima de la
   espera de 1,8 s. La 297/298 (un solo lector, esperar no lee) deja de ser limpieza: es la palanca.
2. **Jev no puede elegir lo que no se le enseña.** Wikipedia tenía 141 puertas y se ofrecieron 89; el
   Explorador tenía 404 y se ofrecieron 61. Las dos veces la puerta buena («Actualidad», «Windows») quedó
   fuera. Medido en la fase 1: a Jev le da igual recibir 20 o 160 (345 · 322 · 350 ms). **El tope de
   `map_what_i_see` (60 de UIA + 160 del terreno) es para el modelo de voz, no para Jev**: el decisor
   debería ver todas, o las primeras N por relevancia. Es una promesa aparte, barata y de alto valor.
3. La espera única de 1,8 s cuando nada cambia sigue ahí: es la 298.

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

## El corte que se adelantó: leer es una llamada (promesa 297, 2026-09-18)

> **Los números se corren.** La 297 de la tabla de arriba —«una sola lectura por tick»— no llegó a
> escribirse en `Contrato.cs`; el número lo tomó este corte, que se midió primero y costaba menos. El
> observador único pasa a ser **298-300**, y se renumera cuando se abra su rama. De la 301 en adelante
> son de la spec 039.

### Qué se midió antes de escribir código

El reloj por fase decía que «leer» costaba ~1 s por paso en el Explorador y 3,4-5 s en una página
web. Una sonda de solo lectura (aprendizaje nº13) comparó el recorrido de hoy con una sola petición
con caché, sobre las mismas ventanas:

| Ventana | Nodo a nodo | Una petición con caché |
|---|---|---|
| Wikipedia en Chrome (680 nodos, 161 accionables) | 3.400 ms | 270 ms |
| Configuración | 800 ms | 155 ms |
| Explorador | 1.050 ms | 420 ms |
| Bloc de notas | 400 ms | 144 ms |

La causa: `UiaReader.Collect` navega con `TreeWalker` y lee `.Current`, y en UI Automation cada una
de esas es un viaje entre procesos — unos ocho por nodo, accionable o no.

### La promesa

**297.** Leer la pantalla es recorrer lo que UNA petición trajo: el recorrido recibe el árbol ya
traído y no navega; recoge lo mismo que antes —accionable, visible, con etiqueta (nombre, o id, o
ayuda) y con geometría, en orden de lectura, con los mismos topes: 40 niveles, y pasados los 400
elementos no se entra en más ramas—; si la petición con caché falla se lee nodo a nodo como antes; y
el lector dice cuál de los dos caminos usó y por qué.

### Cuántos sitios tienen la clase de error (patrón nº5)

Leer UIA propiedad a propiedad, sin `CacheRequest`: **17 sitios en 5 archivos**. Este corte arregla
**2**, los de `UiaReader` (el árbol con sus ventanas hijas, y los menús). Quedan:

| Archivo | Sitios | |
|---|---|---|
| `windows-graph/src/Surfaces/UiaSurface.cs` | 11 | el siguiente corte: es lo que usa el player |
| `windows-client/src/Mcp/SurfaceMapTools.cs` | 2 | compartido con la spec 039; se coordina |
| `windows-client/src/Uia/Desplazamiento.cs` | 1 | |
| `windows-client/src/Ui/RastroDelCursor.cs` | 1 | |

### Nivel 4: tres pantallas, y lo que el contrato no cazó

**Los dos caminos sobre la MISMA ventana en el mismo momento**, etiqueta por etiqueta (sonda que llama
por reflexión a `LeeNodoANodo` y a `LeeConCache` del build de la rama):

| Ventana | Nodo a nodo | Con caché | Solo en uno de los dos | Mismo orden |
|---|---|---|---|---|
| Wikipedia «Medellín» en Chrome | 27.731-41.808 ms · 288 | 3.647-8.869 ms · 288 | 0 | sí |
| Explorador, `C:\` | 2.811-4.712 ms · 411 | 1.558-3.616 ms · 411 | 0 | sí |
| Explorador, Descargas | 3.999-6.157 ms · 325 | 2.258-6.218 ms · 325 | 0 | sí |
| Configuración (`SystemSettings`) | 860-1.339 ms · 57 | 422-656 ms · 57 | 0 | sí |

**Por la app real** (`map_what_i_see` por MCP, build de `main` contra build de la rama, tres vueltas):

| Pantalla | Antes | Después |
|---|---|---|
| Explorador, `C:\` (406 elementos) | 3.239 · 3.458 · 3.256 ms | 2.064 · 1.918 · 1.586 ms |
| Wikipedia «Medellín» (275-279 elementos) | 26.587 · 25.608 · 37.694 ms | 5.040 · 5.208 · 6.184 ms |
| Configuración (el marco: 4 elementos) | 82 · 100 · 95 ms | 78 · 97 · 96 ms |

Ninguna lectura cayó al respaldo: el log no tiene líneas `[lector]`.

**Lo que cazó el nivel 4 y no el contrato.** La primera versión «arreglaba» de paso una fuga del
tope: el de siempre solo lo miraba al entrar en cada nivel, y cortar en 400 exactos parecía más
correcto. Sobre `C:\` el camino viejo daba 411 y el nuevo 400: las once que faltaban eran **carpetas
de verdad**, las últimas de la lista. Se restauró la semántica exacta y la promesa pasó a decirla
(una rama empezada se termina; pasados los 400 no se entra en la siguiente). Un corte de
rendimiento no cambia lo que se ve; si el tope hay que moverlo, es otra promesa.

**Lo que se dice sin adornos:**

- La ganancia es **grande en la web (5-7×) y moderada en el Explorador (~1,7×)**. Las medidas de la
  sonda inicial (12×) eran de una página más corta y de una sola petición; la app hace una por
  ventana hija más la de los menús.
- Los tiempos bailan con la carga de la máquina: en una vuelta de Descargas los dos caminos empataron
  en 6,2 s. Por eso van los rangos y no un número.
- A `map_what_i_see` en una página larga le siguen quedando ~5 s, y ya no son del lector (3,6 s de
  esos sí; el resto es de lo que viene después). Es lo siguiente que hay que abrir con el reloj.
- Configuración se lee por su marco (`ApplicationFrameHost`, 4 elementos) y no por su contenido
  (`SystemSettings`, 57): es anterior a este corte y no se toca aquí, pero deja a Jev sin puertas en
  esa app. Anotado.
