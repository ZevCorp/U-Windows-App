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
