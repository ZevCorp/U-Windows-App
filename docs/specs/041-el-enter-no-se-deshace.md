# El Enter no se deshace, y un espacio no esconde una puerta

> Spec 041 · 2026-09-18 · rama `jose/el-enter-no-se-deshace` · promesas **330** y **331**
>
> **Sobre los números.** La 300 la tomó la rama `jose/las-claves-viven-en-el-backend` y la spec 039
> tiene reservado «de la 301 en adelante», sin tope. Para no chocar una tercera vez en un día, esta
> línea de trabajo (la velocidad del clic) toma el bloque **330-339**. Los números no se reciclan; los
> huecos no molestan.

## De dónde sale

Segunda prueba del dueño (2026-09-18, 13:25-13:37), con las promesas 296-299 en `main`. Su frase:
«a veces usa Jev y es muy rápido, y a veces…». Jev decidió 6 clics y acertó los 6 en 218-773 ms; lo
lento estaba alrededor. Dos de las causas son la misma clase de error —**comparar identidades de
forma distinta** (aprendizaje nº16)— y van juntas aquí.

### 1. Escribir y dar Enter intenta deshacer la navegación

`map_type` confirma con Enter y después compara dónde estaba con dónde está. Si cambió, **pulsa
«Atrás»** (`uia:aid=backButton;ct=Button`) y espera 2 s a volver. La protección nació el 2026-08-03
para un caso real: renombrar una carpeta recién creada en el Explorador, donde el Enter que confirma
el nombre también la abre. Pero se aplica a **toda** superficie, y en la web que el Enter navegue es
exactamente lo que se pidió.

Medido en el log de hoy: **11 veces**, las 11 en superficies donde no tocaba —10 en Google, 1 en el
Bloc de notas (escribir un texto le cambió el título a la pestaña)—. Las 11 veces el botón «Atrás» no
se encontró (es el del Explorador), así que no se deshizo nada: fueron **3-4 s tirados por cada
búsqueda** —cinco intentos de resolver el botón más los 2 s de espera—. En la prueba con Jev,
`map_type` fue 53 de los 94 s de herramientas.

```
[13:33:10] mapa-mcp:   resultado acción: ok=True
[13:33:15] mapa-mcp: el Enter abrió «web://google.com/search»; se vuelve a «web://www.google.com/search»
[13:33:15] mapa-mcp: Execute «Atrás» · click …
[13:33:17] mapa-mcp:   ✗ NO resuelto tras 5 intento(s) · falla
[13:33:19] mapa-mcp: ← (10435 ms) escribí «https://www.google.com/search?q=…» y confirmé con Enter
```

Y lo peor no pasó de milagro: en un navegador cuyo botón de volver se llamara igual, **cada búsqueda
se habría deshecho sola**, reportando «escribí y confirmé con Enter».

### 2. La barra de direcciones se ofrece y no se puede pulsar

Chrome la nombra `'Barra de direcciones y de búsqueda '`, **con un espacio al final**. El lector
recorta las etiquetas, así que el selector que se guarda es `uia:name=Barra de direcciones y de
búsqueda;ct=Edit`; el resolvedor busca ese nombre **exacto** con una `PropertyCondition`, y no casa
nunca. 9 fallos en las dos pruebas de hoy, con 5 reintentos internos cada uno, y un tope de «dos
intentos» agotado dos veces. `map_type` sí la encuentra, porque cae a buscarla «por parecido».

## Las promesas

**330.** Escribir y confirmar con Enter solo se deshace donde escribir es renombrar —el Explorador de
archivos—: en la web y en cualquier otra superficie, que el Enter cambie de pantalla es lo que se
pidió; no se pulsa «Atrás», no se espera la vuelta, y la respuesta dice a dónde se llegó. Y dos formas
de la misma pantalla —con `www` y sin él— no cuentan como un cambio.

**331.** Un espacio no esconde una puerta: un selector por nombre encuentra el elemento aunque su
nombre real traiga espacios al principio o al final que la etiqueta guardada no tiene; primero se
busca el nombre exacto, como siempre, y solo si no aparece se compara recortando; un nombre que de
verdad es otro sigue sin casar.

## Fases

| Fase | Qué | Promesa | Archivo |
|---|---|---|---|
| 0 | las dos promesas, en rojo | 330, 331 | `tests/ContratoDelGrafo/Contrato.cs` |
| 1 | la regla de cuándo se deshace | 330 | `windows-client/src/Mcp/SurfaceMapTools.cs` (solo `map_type`; no toca `UnPasoDecidido` ni `Tramo`, que son de la 039) |
| 2 | el nombre recortado, como respaldo | 331 | `windows-graph/src/Surfaces/UiaSelector.cs`, `UiaSurface.cs` |

## Lo que queda fuera (anotado para las siguientes)

- **`map_go_to` tarda 11,6 s en decir «no hay ningún camino aprendido»** hacia un sitio que el terreno
  ya conoce (Docs, Scholar, Instagram); a los desconocidos va directo en 0,7 s. Por eso el modelo dejó
  de usarlo y pasó a escribir URLs en la barra. Es el siguiente corte (332).
- **`map_go_to` devuelve la página a medio cargar** (41 elementos; un segundo después, 116) y el modelo
  pulsa «Volver a cargar» creyéndola rota: 3 veces, 3-5 s cada una.
- **`map_decidir` tarda 3-4,8 s con Jev contestando en 250 ms**: ~2 s antes de mover la mano, y un
  `map_esto_es` que falla el 100 % de las veces con Jev porque recibe el selector donde espera el
  nombre. Ese código es de la spec 039: se le pasa el hallazgo.
- **Leer una página pesada vuelve a costar 2 s** (la guía de Fortinet, 86 elementos útiles): la 299 se
  rindió ahí a los 4,3 s y no a los 0,6. Probar a no pedir lo que está fuera de pantalla.
- **Cero tramos**: Luna encadena `map_decidir` de uno en uno. Prompt y catálogo: spec 039.
