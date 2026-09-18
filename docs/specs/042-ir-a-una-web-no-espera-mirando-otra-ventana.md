# Ir a una web no espera mirando otra ventana

> Spec 042 · 2026-09-18 · rama `jose/una-web-se-va-directo` · promesas **332** y **333** (bloque 330-339)

## De dónde sale

En las dos pruebas del dueño del 2026-09-18, `map_go_to` hacia una web tardó **11,4-11,7 s en decir
«no hay ningún camino aprendido»** cuatro veces (Instagram, Docs, Scholar, y una más en mi propia
corrida de nivel 4), mientras que a otros sitios iba en 0,6-0,8 s. El modelo de voz aprendió la
lección equivocada: dejó de usar `map_go_to` y pasó a escribir direcciones en la barra, que costaba
6-10 s cada una (spec 041).

El log dice que son **dos bugs distintos con el mismo síntoma**:

### 1. Llegó, y estuvo ocho segundos esperando verse llegar

```
[13:26:04] mapa-mcp: → map_go_to surface=web://docs.google.com
[13:26:04] pestañas: «docs.google.com» estaba en la pestaña «Investigación sobre System One…» …
[13:26:07] pestañas: «docs.google.com» ya estaba activo en una ventana → al frente
[13:26:15] nucleo-paso: hacia «docs.google.com»: NO — no hay ningún camino aprendido de «github.com» hasta ahí
[13:26:15] trabajo: la ventana de trabajo es ahora «web://docs.google.com/document/d/…/edit»
[13:26:15] mapa-mcp: ← (11629 ms) no hay ningún camino aprendido … | EN PANTALLA AHORA, en «web://docs.google.com/document/…
```

Docs estaba abierto en **otra ventana** de Chrome. `PasoDelNucleo` la trae al frente y después sondea
«¿dónde estoy?» hasta 3 s, y otros 8 s por el atajo de las webs. Pero ese «dónde» es el de la **ventana
de trabajo**, que seguía siendo la de GitHub: la de trabajo solo se cambia al terminar todo el recorrido
(`SeguirElFoco`). Once segundos mirando la ventana de la que acababa de salir, y en la misma respuesta
el «no pude» y el «estás en docs.google.com».

### 2. Pedir un subdominio se daba por cumplido estando en el dominio padre

```
[13:35:00] mapa-mcp: → map_go_to surface=web://scholar.google.com
[13:35:01] pestañas: «scholar.google.com» ya estaba activo en una ventana → al frente
[13:35:12] mapa-mcp: ← (11625 ms) no hay ningún camino aprendido … | EN PANTALLA AHORA, en «web://google.com/search»
```

`PestanasAbiertas.MismoSitio(host, dominio)` es **simétrica**: acepta que el host real termine en
`.dominio` (pedí `github.com`, estoy en `api.github.com`: bien) **y también lo contrario** (pedí
`scholar.google.com`, estoy en `google.com`: mal). Así que «ya estaba activo», no se navegó a ninguna
parte, y se esperaron once segundos a una llegada imposible. Es el aprendizaje nº16 otra vez, y la
cuarta comparación de identidades mal hecha que aparece hoy. La dirección inversa estaba sirviendo,
de rebote, al caso `www.`: se quita el `www.` de los dos lados antes de comparar.

## Las promesas

**332.** Ponerse delante de otra ventana la vuelve la de trabajo antes de comprobar la llegada: ir a una
web que ya está abierta en otra ventana del navegador se da por llegado en cuanto esa ventana está
delante, no al agotar los presupuestos mirando la ventana anterior; y ponerse delante se pide UNA vez por
paso —si ya se pidió y no se llegó, no se vuelve a pedir ni se abre un segundo plazo—.

**333.** Pedir un subdominio no se cumple estando en el dominio padre: con `scholar.google.com` pedido,
una pestaña en `google.com` no es «ya estaba abierto»; pedir el sitio a secas sí se cumple en un
subdominio suyo, como hasta hoy; y `www.` no cuenta en ninguno de los dos lados.

## Fases

| Fase | Qué | Promesa | Archivo |
|---|---|---|---|
| 0 | las dos promesas, en rojo | 332, 333 | `tests/ContratoDelGrafo/Contrato.cs` |
| 1 | adoptar la ventana al ponerse delante; un solo «ponme delante» por paso | 332 | `Navigation/PasoDelNucleo.cs`, cableado en `FaceWindow.xaml.cs` |
| 2 | la regla de mismo sitio, con dirección | 333 | `Uia/PestanasAbiertas.cs` |

## Lo que queda fuera

- `map_go_to` devuelve la página **a medio cargar** y el modelo pulsa «Volver a cargar». Siguiente.
- `map_go_to web://google.com` con Scholar abierto dice «te puse delante de google.com» estando en
  Scholar. Es la dirección que la regla SÍ admite (un subdominio cuenta si se pidió el sitio a secas,
  spec 029) y la respuesta dice dónde se está de verdad; no se toca sin hablarlo.

## Lo que pasó al implementarlas

Promesas escritas y vistas en rojo antes que el código (`8967bef`: las dos `PENDIENTE`, 268 verdes).
Verdes con `e75c0b2`: contrato intacto, 270; la 66 (una web es direccionable) y la 261 (llegar es
llegar) siguen en pie.

**Sabotaje, comprobado que se aplicó** (8 líneas en el `diff`): quitando el aviso y la regla de «una
vez», y devolviendo la simetría a `MismoSitio`, caen **exactamente** la 332 y la 333 — y la 332 reproduce
lo medido en la app: «2405 ms (los plazos eran 500 + 1200)», «se pidió 2». Revertido tras commitear.

**Sitios** (patrón nº5): `PasoDelNucleo` se construye en 2; el de `ServidorDelNucleo` pregunta por la
ventana de delante, no por la de trabajo, y no tenía el problema (1 de 2). `MismoSitio` tiene 3
llamadas, las tres con (host real, dominio pedido): las tres quedan bien con la regla con dirección.

### Nivel 4: dos situaciones reales, la misma caja de pruebas antes y después

Se recrearon las dos de la prueba del dueño: una web abierta en **otra ventana** de Chrome que la de
trabajo (Wikipedia en una, Hacker News en otra), y pedir Scholar con Google delante.

| `map_go_to` | `main` (6f0f6e1) | rama (e75c0b2) |
|---|---|---|
| `web://es.wikipedia.org`, abierta en otra ventana | **11.635 ms** · «no hay ningún camino aprendido» (y ya estaba allí) | **1.195 ms** · «te puse delante de es.wikipedia.org» |
| `web://google.com`, abierta en otra ventana | **12.346 ms** · «no hay ningún camino aprendido» (y ya estaba allí) | **1.381 ms** · «te puse delante de google.com» |
| `web://scholar.google.com`, con `google.com` delante | **11.326 ms** · «no hay ningún camino aprendido», seguía en `google.com/search` | **2.522 ms** · abierto de verdad: «estás en web://scholar.google.com» |

```
antes   [14:14:53] pestañas: «scholar.google.com» ya estaba activo en una ventana → al frente
        [14:15:04] nucleo-paso: hacia «scholar.google.com»: NO — no hay ningún camino aprendido de «search» hasta ahí
después [14:16:59] pestañas: «scholar.google.com» no está abierto en ninguna de las 6 ventana(s) de navegador
        [14:16:59] pestañas: «scholar.google.com» no estaba abierto: abriendo https://scholar.google.com en chrome.exe
        [14:16:59] trabajo: la ventana de trabajo es ahora «web://scholar.google.com»
        [14:16:59] nucleo-paso: hacia «scholar.google.com»: LLEGADO
```

Las cuatro ventanas de Chrome que abrió la prueba se cerraron al terminar (solo las de una única
pestaña con esos títulos).

**Sin adornos:** el «no» legítimo —una web que de verdad no carga— sigue costando hasta 8 s, que es el
plazo de una página entera; antes eran 11. No se ejercitó a mano (haría falta una dirección que no
responda) y lo juzga el contrato. Y un sitio de SAP o del Explorador no cambia: su plazo sigue en 3 s.
