# Tras navegar se cuenta la página, no su esqueleto

> Spec 044 · 2026-09-18 · rama `jose/ir-devuelve-la-pagina-asentada` · promesa **335** (bloque 330-339)

## De dónde sale

Desde la promesa 263 (spec 029) cada acto cuenta lo que dejó delante, para que el modelo no gaste otra
llamada en preguntar «¿y ahora qué hay?». Pero ese inventario se lee **una vez, en el instante en que
el acto termina**, y una página web en ese instante está a medio pintar.

## Lo que se midió antes de escribir código

Log del 2026-09-18, las tres pruebas del dueño: 123 actos con inventario. De los **9** tras los que el
modelo volvió a pedir `map_what_i_see` en menos de 6 s, en **8** el inventario del acto se había quedado
corto o ya ni era de esa pantalla:

| Hora | Acto | Dijo | Poco después había |
|---|---|---|---|
| 12:10:56 | `map_go_to` docs.google.com | 24 | 64, y ya en `…/document/u/0` (redirigió) |
| 12:11:41 | `map_go_to` google.com | 26 | 49 |
| 12:36:36 | `map_take` «Docs home» | 57 | 100, y en otra dirección |
| 12:39:10 | `map_take` «Search» (arXiv) | 33 | 65 |
| 12:40:16 | `map_go_to` búsqueda | 33 | 84 |
| 12:40:28 | `map_go_to` búsqueda | 32 | 95 |
| 14:48:15 | `map_go_to` búsqueda | 27 | 59 |

Veintitantos elementos es poco más que el cromo del navegador —pestañas, barra, botones— sin la página.
El modelo decide con eso: o vuelve a mirar (~2 s de ida y vuelta), o nombra una puerta que aún no está,
o cree que la página está rota: **tres veces pulsó «Volver a cargar»**, 3-5 s cada una.

## La regla

Tras un acto que **navegó a una web**, el inventario se toma cuando la página está asentada: se vuelve a
mirar, con una pausa corta entre miradas, hasta que dos seguidas ven la misma pantalla con el mismo
número de elementos. Tope de reloj (promesa 245). Es la misma idea que la promesa 299, aplicada al otro
lado: allí «asentada» permite rendirse antes; aquí, contar lo que de verdad hay.

- **Cuándo**: `map_go_to` que acaba en una web (una búsqueda nueva deja la misma dirección y otra
  página); y `map_take`, `map_decidir`, `map_type` y `map_open_app` cuando la ubicación **cambió** y
  acabó en una web.
- **Cuándo no**: un clic o un texto que no cambió de página; el Explorador, SAP y el resto de apps
  nativas, que no cargan por partes y tienen sus propias esperas.
- **Coste**: una página ya asentada paga una pausa (250 ms) y una mirada (~100-300 ms desde las
  promesas 297/298). Solo en navegaciones.
- **Si no para de cambiar** (un vídeo, un contador): se corta al tope y **se dice**, para que el modelo
  sepa que el listado puede estar incompleto.

## La promesa

**335.** Tras navegar a una web, lo que se cuenta que hay delante es la página ASENTADA: se vuelve a
mirar hasta que dos miradas seguidas ven la misma pantalla con los mismos elementos, con un tope de
reloj; si ya estaba asentada cuesta una sola mirada de más; un acto que no navegó, o que no acabó en una
web, no espera nada; y si el tope se agota se entrega lo último que se vio, diciendo que seguía cambiando.

## Fases

| Fase | Qué | Promesa | Archivo |
|---|---|---|---|
| 0 | la promesa, en rojo | 335 | `tests/ContratoDelGrafo/Contrato.cs` |
| 1 | la regla (pura) y su uso en el despacho, donde pasan todos los actos | 335; la 263 intacta | `Mcp/ComoSeContesta.cs`, `Mcp/SurfaceMapTools.cs` (solo `Call`) |

## APARCADA el 2026-09-18 — no entró a `main`, y por qué

Tres versiones en una tarde, y las tres las tumbó el nivel 4, no el contrato:

1. **Esperar tras toda navegación.** Funcionaba (Wikipedia «Medellín»: de entregar 102 elementos a 314;
   Google: 121 → 163) y costaba **+1 a +2,6 s por `map_go_to`** (0,8-1,7 s → 1,9-3,4 s). El modelo solo
   había vuelto a mirar tras una de cada cinco navegaciones, perdiendo ~2 s: más caro que el problema.
2. **Esperar solo si lo visto es un esqueleto (≤ 60 elementos).** Seguía lento: para saber si el acto
   había navegado se preguntaba «¿dónde estoy?» dos veces por acto, y con el navegador ocupado cada
   consulta costaba hasta 1 s. **Todo acto —también el clic que no navega— pasó de 1,6 s a 4,5 s.**
3. **El «dónde» sale de la cabecera del inventario ya leído.** Coste cero, medido: `map_go_to` 0,25-1,7 s,
   el clic que navega 1,0 s, la barra 1,2 s, igual que antes del corte.

Y aun así **no entra**, porque la versión 3 no se pudo ver disparar ni una vez, y al buscar por qué
apareció el fallo del criterio: **todas** las páginas entregaban ~115 elementos al instante, fuera cual
fuera el sitio. No era contenido: era el cromo de Chrome con una veintena de pestañas abiertas (cada
pestaña es un `TabItem`). Por la mañana, con pocas pestañas, el esqueleto medía 24-38. **El umbral de 60
depende de cuántas pestañas tenga abiertas la persona**: con muchas, la regla no dispara nunca y nadie se
entera. Es el aprendizaje nº18 —un guardia que se cree puesto es peor que ninguno—, y no se mergea algo así.

**Lo que haría falta para retomarla:** que «esqueleto» se mida sobre los elementos de la PÁGINA (lo que
cuelga del documento web), no sobre la ventana entera. El lector ya distingue el `Document` de Chrome;
falta que el inventario lo cuente aparte. Y medir el beneficio de verdad: cuántas vueltas a mirar ahorra
en una sesión real, no en un guion.

**Lo que sí queda de esta rama, por si se retoma:** `ComoSeContesta.DondeDice` (la ubicación, gratis, de
la cabecera del inventario) y el ajuste del accesorio de la promesa 263 (80 elementos y no 2), que solo
hace falta si la 335 entra. La promesa 335 y su número quedan reservados en esta rama.
