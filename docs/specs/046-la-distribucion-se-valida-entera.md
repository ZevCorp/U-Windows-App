# La distribución se valida entera: la decisión de Jev falla cerrada

Estado: **en construcción** · Spec 046 · 2026-09-22 · Rama `jero/jev-la-distribucion-se-valida-entera` ·
promesas **342–350** · reescribe la **289** (su última cláusula era la compuerta abierta)

> Rama A de las cuatro que siguen `arquitectura-jev-en-u.md` (22-09), todas desde `main`. Nace de la
> revisión de `experimento/reemplazo-jeff` (`informe-jev-rapido.md`, 21-09) y de cómo está hecho Jev de
> verdad (`como-esta-hecho-jev.md`). **La rama del dueño no se integra tal cual**: lo que de allí vale
> —el modelo configurado llegando al cuerpo— se rehace aquí con su promesa primero.
>
> **Números.** La arquitectura reservaba 341–350. Esta mañana entró a `main` `f811796` (Felipe, 09:27)
> con la **341** («dos recordatorios vencidos despiertan una sola sesión de voz») en el contrato del
> grafo. Comprobado con `git grep` sobre las 44 refs `origin/*` tras `fetch`: **342–350 están libres y
> son los de esta spec**; la 351 es de la rama B y no se toca. Nueve números, nueve promesas: lo que la
> arquitectura numeraba 350 (el clic físico que no cae en Ü) queda fuera, y se dice abajo por qué.
> Aparte: el checkout principal tiene sin commitear `docs/specs/046-el-decisor-lleva-todo-el-computer-use.md`
> reservando 335–343 —números que en `main` ya gastaron la 044 (335–340), `f811796` (341) y esta spec
> (342–343)—. Propuesta: ese documento pasa
> a **050** y sus promesas a **386+**; si el dueño prefiere lo contrario, esta corre a 050 y las promesas
> se desplazan sin reciclar ninguna. Se le pregunta al abrir el PR.
>
> **Marcas.** **(M)** medido: log, sonda, `grep` o fixture. **(D)** deducido del código.

## Diagnóstico: qué se midió

| Qué | Medida | Fuente |
|---|---|---|
| `peligro` = 1,2 · 80 · 87 · 1,0000001 · 1e400 en la rama del dueño | **pulsa «Grabar»** en las tres sondas; `main` no acciona («irreversible o peligroso (1.20)») | informe §1.1, tres sondas con transporte falso **(M)** |
| `cumplido` = 1,5 en la rama del dueño | acciona; `main` para | ídem **(M)** |
| Por qué: `ProbabilidadSegura` convierte en 0 lo que sale de [0,1] y las compuertas cortan **por arriba** (`≥0,70`, `≥0,50`) | 0 deja pasar | `ElDecisor.cs:250-262` de `4198517` **(M)** |
| En `main`, `Noul` devuelve 0 si la noul no vino, no es objeto o no es número | 0 deja pasar: **la compuerta está abierta hoy** | `ElDecisor.cs:236-239` **(M, lectura)** |
| La promesa 289 lo exige: «y una respuesta sin esas dos sigue valiendo» | su fixture `viejo` manda `null, null` y **exige** `Actuar` | `Contrato.cs:784`, `:12037-12038` **(M)** |
| El fixture de la 289 manda `probabilities` solo con la elegida | Σ = 0,95 | `Contrato.cs:12020-12021` **(M)** |
| `confidence` y `probabilities` se leen sin rango ni suma; una `confidence` de 95 o 1e400 se acepta | «con confianza Infinity» en la base | `ElDecisor.cs:175-182`; informe §1.1 **(M)** |
| En la rama del dueño una `confidence` de 95 queda en el log como `conf=0.00` | igual que una duda real de 0,30 | informe §1.2 **(M)**; patrón nº2 |
| Vetos deterministas en el camino de decidir | **0**: `EsPeligrosa` solo en `InstanciarSkill.cs:46`; `EsDestructivo` en 2 sitios (`SurfaceMapTools.cs:2317`, `PulsarSegunElNucleo.cs:217`) y excluye «guardar» a propósito (`Contrato.cs:9757`) | `grep` 22-09 **(M)** |
| La segunda mejor se pulsa sin pasar por `peligro` (la noul juzgó solo a la elegida) ni por lista alguna | falla abierta | `SurfaceMapTools.cs:304-309` **(M, lectura)** |
| `cumplido` se decide también por subcadena del porqué | `d.Porque.Contains("cumplido")` | `SurfaceMapTools.cs:299` **(M)**; el fixture de la 292 caso 1 depende de eso (`:12155`, sin `Cumplido` puesto) |
| Qué viaja a `api.typesafe.ai` | pantalla, objetivo y el texto de hasta 220 puertas, en `state` y como claves de `criteria`; filas de rejilla incluidas (`LeerRejilla` → `GuiGridFila` → `PuertasDeAhora`) | `PeticionASystemOne.cs:80-86, 133-134`; `MundoQueToca.cs:331-336` **(M, lectura)**; sonda sintética del informe **(M)** |
| Que las filas reales lleven nombre y documento | 0 decisiones de Jev sobre SAP en 23 logs | **(D)**; el fixture `Contrato.cs:1345` lo ilustra |
| La política de lo que viaja | **no existe en código**: solo una prosa en `PeticionASystemOne.cs:122-126` y `docs/el-decisor-y-typesafe.md:88-93`, las dos falsas | **(M, lectura)** |
| Sitios que comparan el prefijo `sapgui://` a mano | **16** (`StartsWith("sapgui://", OrdinalIgnoreCase)`) | `grep` 22-09 **(M)** |
| El modelo del cuerpo | siempre `ModeloPorDefecto` aunque `U_TYPESAFE_MODELO` diga otro; el botón enseña `cfg.Modelo` | `ElDecisor.cs:139`, `InterruptorDelDecisor.cs:70` **(M)** |
| Llamadores de `ElDecisor.Elegir` | **1** en producción (`InterruptorDelDecisor.cs:67`) + **4** líneas en `sondas/DelDecisor/Programa.cs` (`:54, :58, :84, :104`) | `grep` **(M)** |
| Cómo valida jev-ultrafast, que corre contra la API real | claves **iguales** a las ofrecidas, todo finito en [0,1], `|Σ−1| < 0,02`, la elegida ≥ máximo − 1e-6 | `r1/src/jev_ultrafast/model.py:30-44` **(M)** |
| `confidence` ≠ `probabilities[choice]` | 0,91 frente a 0,93 en el ejemplo documentado; TypeSafe la calcula «from how probabilities is spread» | `docs/el-decisor-y-typesafe.md:196-197`; `como-esta-hecho-jev.md:181-183` **(M)** |
| `absent` como compuerta | marcó 0,60 con la opción correcta a la vista | `como-esta-hecho-jev.md:243` (t=127) **(M, vídeo ajeno)** |
| Masa del top-5 normalizada | 17–29× lo plano cuando acierta; **7×** en el paso que no termina; seis puntos, ningún fallo confirmado: **sin calibración para umbral** | `como-esta-hecho-jev.md:149-177` **(M, vídeo ajeno)** |
| `usage.input_tokens` | la API lo devuelve (875 / 1.913 / 4.635 con 20 / 60 / 160 puertas); hoy nadie lo lee | `anatomia-del-clic-2026-09-18.md:57-61` **(M)** |
| Jev en caliente | ~330 ms con 20, 60 o 160 puertas; `cumplido` 0,10–0,11 y `peligro` 0,24–0,32 en 15 llamadas | ídem **(M)** |
| Sabotaje de la rama del dueño | devolver sus tres archivos a `main` deja el contrato **INTACTO 272/272**: nada de lo suyo está juzgado | informe §1.6 **(M)** |

## Por qué esto va dirigido por especificación

El decisor **se da por bueno a sí mismo**: lo que TypeSafe contesta se acepta sin forma, y una cadena
de promesas verdes (275–290) convivía con la compuerta de `peligro` abierta desde el día que se
escribió. La 289 no solo no lo veía: **lo exigía**. Y el arreglo del dueño (`3215466`) pasó el contrato
entero mientras abría la otra compuerta. Una prueba escrita después del código se escribe para que
pase; aquí cada promesa se escribe para que **falle contra `main` hoy**, y el sabotaje de cada una se
comprueba por diff antes de contarla.

Además esto corre en el camino que usa un hospital: lo que viaja a una API en EE. UU. y lo que se pulsa
sin que una persona lo vea tiene que estar escrito como promesa, no como comentario.

**La regla del flujo, y no tiene excepciones:** ninguna línea de producción entra antes que la
promesa que la juzga. Cada fase empieza con el contrato ROTO y termina con el contrato INTACTO.

## La especificación

El enunciado es el que va **literalmente** en `tests/ContratoDelGrafo/Contrato.cs`.

| # | Promesa | Fase |
|---|---|---|
| **342** | la distribución de Jev se valida ENTERA antes de cualquier compuerta: cada probabilidad finita y en [0,1], la suma 1 (±0,02), las claves exactamente las que viajaron —ni una de más ni una de menos—, la elegida el máximo y sin empate, y la confianza finita y en [0,1]; cualquier cosa fuera de forma es «no sé»: no se acciona, el porqué nombra el campo y el valor crudo, la decisión los conserva y no ofrece alternativas, y nada se convierte en 0 ni se satura; y un cuerpo de error que no se pudo leer dice por qué en vez de callarlo | 2 |
| **343** | «cumplido» y «peligro» fallan cerrados: si alguna falta, no es número, no es finita o está fuera de [0,1] se toma el caso peor —peligro 1, cumplido 0—, no se acciona, y el porqué dice cuál falta o cuál vino y con qué valor; un 0 solo abre la compuerta cuando Jev lo dijo | 3 |
| **344** | lo irreversible no se pulsa por decisión: una candidata cuya etiqueta es peligrosa —grabar, guardar, finalizar, borrar, eliminar, enviar, firmar— no se pulsa desde map_decidir ni desde el tramo, ni como elegida ni como segunda mejor, aunque Jev la dé con confianza 0,99 y peligro 0; la mano no la recibe, la cuenta dice cuál se vetó y por qué, el control vuelve con el inventario; y el cuerpo deja de pedirle a Jev «la que menos daño haga» | 5 |
| **345** | «ninguna» es una opción de la pregunta: además de TODAS las puertas ofrecidas viaja «0) ninguna» —nada de esta pantalla avanza hacia el objetivo— como una opción más del choice; si Jev la elige no se acciona y se dice que no lo ve en esta pantalla, con su probabilidad; y la añade quien pregunta, no CuerpoDeEleccion, así que la 282 sigue tal cual | 1 |
| **346** | el modelo configurado llega al cuerpo: con U_TYPESAFE_MODELO=X el cuerpo que manda el interruptor lleva «model»:«X», sin la variable lleva el alias por defecto, y el estado del botón y el cuerpo nombran el mismo modelo; la firma Elegir de seis argumentos se conserva y delega con los valores por defecto | 4 |
| **347** | a Jev solo viaja texto de superficies permitidas: en sapgui:// sin U_DECISOR_SAP_TEXTO=si el transporte no se toca ni una vez y decide la regla local diciendo por qué; una ubicación con un prefijo de U_DECISOR_TEXTO_VETADO tampoco viaja; en el resto viaja como hasta hoy; y la política vive en un solo sitio y compara el prefijo por el mismo camino que el resto del código | 6 |
| **348** | las filas nunca viajan por su texto: GuiGridFila, GuiTreeFila y GuiTreeCarpeta llegan a Jev como «N) fila (tipo)», sin etiqueta, también con SAP habilitado; la respuesta se mapea por id a la puerta ofrecida y la mano sigue pulsando por selector; y la cuenta y la línea «decisor:» dicen cuántos ids viajaron de cuántos, cuántas filas fueron sin texto y cuántos caracteres se mandaron, nunca el texto | 7 |
| **349** | «cumplido» solo con evidencia: un cumplido alto deja de accionar y el tramo para diciendo que Jev cree que ya está, nunca que el objetivo está cumplido; lo decide el número, no el texto del porqué; y la cuenta del tramo devuelve el turno con lo que hay delante, para que lo compruebe quien sí puede —la llegada o la persona— | 8 |
| **350** | la masa de los cinco mejores y los tokens son señal, no compuerta: la decisión lleva N, la masa de los 5 mejores y los input_tokens que usage trajo —sin usage, «sin medir»—; los tres salen en la cuenta y en la línea «decisor:», también cuando no se acciona; ninguna decisión cambia por ellos; y «absent» no se pregunta | 9 |

**La que cierra el asunto es la 342**: mientras la respuesta no se lea entera, todo lo demás —vetos,
política, señal— actúa sobre números que pueden no significar nada.

### La 289, reescrita (el número no se recicla)

> **289.** una llamada, tres preguntas: el cuerpo lleva puerta (choice), cumplido (noul) y peligro
> (noul); con cumplido alto no se acciona y se dice que Jev cree que el objetivo ya está; con peligro
> alto no se acciona y se dice por qué; y una respuesta sin alguna de las dos, o con una fuera de [0,1],
> no se acciona y dice cuál falta (2026-09-22: hasta hoy «seguía valiendo» y 0 abría la compuerta)

Su fixture pasa a mandar `probabilities` completas (Σ = 1, con «0) ninguna») y el caso `viejo`
**invierte su aserción**. Se anota en la spec 036, donde nació.

### Fixtures ajenos que cambian, y sus enunciados no

| Promesa | Qué cambia en el fixture | Por qué | Enunciado |
|---|---|---|---|
| 278, 279, 280 | `RespuestaChoice` (el ayudante común) añade la clave «0) ninguna» con 0 | desde la 345 esa clave viaja, y la 342 exige que la respuesta traiga **exactamente** las que viajaron | intacto |
| 292, caso 1 | la decisión falsa lleva `Cumplido = 0,9` (por `DecisionCon`) además del texto | desde la 349 «cumplido» lo decide el número; hoy ese caso pasa **solo** por la subcadena | intacto: «el decisor dice que el objetivo ya está cumplido» sigue siendo lo que pasa |
| 282 | ninguno | «ninguna» la añade `ElDecisor`, no `CuerpoDeEleccion`; su `criteria.Count == opciones.Length` (`:11731`) sigue verde | intacto |

## Con qué se juzga cada una (sin pantalla, sin SAP, sin TypeSafe)

Todo por reflexión, con transporte falso, como las 275–290. Lo que aún no existe se pide por nombre
y cuenta como `Pendiente`.

| # | Juez | Sabotaje de una línea (verificado por diff) |
|---|---|---|
| 342 | `Elegir("jev", …)` con un transporte que devuelve, uno por caso: `confidence` 95 · 1,0000001 · 1e400 · −0,01 · «alta» (texto); una probabilidad NaN; Σ = 0,95 (solo la elegida) y Σ = 1,10; una clave de más («Grabar») y una de menos; la elegida con 0,31 cuando otra tiene 0,58; empate 0,45/0,45. **Todos**: `Actuar=false`, `Alternativas` vacía, `Porque` y `QueNoCuadro` nombran el campo y el valor crudo («peligro=80», «Σ=0,95», «0,31 < 0,58»), `Confianza` conserva el crudo (95, NaN). Y `ClienteTypeSafe.DetalleDelError(leer)` con un `leer` que lanza devuelve «(no se pudo leer el cuerpo del error: Tipo: mensaje)». Contraste: una respuesta en forma con «0) ninguna» a 0 sí acciona | en `Validar`, la tolerancia de la suma pasa de 0,02 a 10 (el caso Σ=0,95 acciona) |
| 343 | `Elegir("jev", …)` con `peligro` 1,2 · 80 · 1e400 · 1,0000001 · −0,01 · «sí» · ausente, y `cumplido` 1,5 · ausente: `Actuar=false`, `Peligro=1`, `Cumplido=0`, `Porque` con «peligro=80» o «falta «peligro»». Con las dos en [0,1] y bajas, acciona (289). Con `peligro` 0 dicho por Jev, acciona: el 0 vale cuando Jev lo dijo | `Noul` vuelve a devolver 0 cuando la noul falta |
| 344 | `map_decidir` con puertas inyectadas «Nuevo»/«Grabar»/«Buscar» y un decisor falso que da «2) Grabar (Button)» con conf 0,99 y `Peligro=0`: **0 pulsos**, la cuenta lleva «Grabar» y «no se puede deshacer», y el inventario. Con la elegida «1) A» que no está viva y la segunda «2) Guardar» a 0,40: se pulsa A, **no** Guardar, y la cuenta dice que se vetó. `InstruccionesDeLaPuerta` no contiene «menos daño». Y por el tramo (`MapaParaTramo`) igual: 0 pulsos y para | quitar `PuertasPeligrosas.EsPeligrosa` del filtro de candidatas |
| 345 | `Elegir("jev", …)` con transporte que **captura** el cuerpo: `criteria` = las ofrecidas + «0) ninguna…», y ni una más; `state` no lista «ninguna» como puerta; la respuesta con `choice` = ninguna a 0,80 → `Actuar=false`, `Porque` con «no lo veo en esta pantalla» y «0,80». `CuerpoDeEleccion` con 3 opciones sigue dando 3 criterios (282) | no añadir `IdNinguna` a la lista que viaja |
| 346 | `InterruptorDelDecisor` con `FabricaDeTransporte` inyectada (captura el cuerpo) y entorno `U_DECISOR=jev`, `TYPESAFE_API_KEY=x`, `U_TYPESAFE_MODELO=jev-1.13.0`: `Encender` y una llamada al `Decisor` del mapa → el cuerpo lleva `"model":"jev-1.13.0"` y `Estado` lo nombra; sin la variable, `"model":"jev-latest"`. `Elegir` de 6 argumentos sigue existiendo y da la misma decisión que la larga con los valores por defecto | el interruptor pasa `ModeloPorDefecto` en vez de `cfg.Modelo` |
| 347 | `Elegir` (la sobrecarga con política) con `pantalla="sapgui://QAS/NWP1/…"` y un transporte que cuenta: **0 llamadas**, `Porque` con «sapgui» y «U_DECISOR_SAP_TEXTO», y la decisión es la de la regla local (`Porque` con «simulad»); con `U_DECISOR_SAP_TEXTO=si`, 1 llamada; con `U_DECISOR_TEXTO_VETADO=web://portal;web://historia` y `pantalla="web://portal/consulta"`, 0 llamadas; con `uia://explorer.exe/…`, 1. `PoliticaDeLoQueViaja.PuedeViajar` es un solo método público y `Leer(entorno)` lee las dos variables (vacío = ausente, patrón nº9) | `PuedeViajar` devuelve siempre `true` |
| 348 | `Elegir` con SAP habilitado y puertas «1) Nuevo (Button)», «2) fila de prueba · 000 (GuiGridFila)», «3) Triage/Urgencias (GuiTreeFila)», «4) Favoritos (GuiTreeCarpeta)» y transporte que captura: el cuerpo **no contiene** «fila de prueba», «Triage/Urgencias» ni «Favoritos» y sí «2) fila (GuiGridFila)»; la respuesta que elige «2) fila (GuiGridFila)» devuelve `Puerta` = «2) fila de prueba · 000 (GuiGridFila)» (la ofrecida) y `Viajaron=4`, `FilasSinTexto=3`, `Caracteres` = longitud del cuerpo. Por `map_decidir` con `LogBus.Logged` suscrito: la línea `decisor:` lleva «viajan 4 de 4 · 3 filas sin texto · N caracteres» y **no** lleva «fila de prueba» aunque sea la elegida; la mano recibe el selector de la fila | `IdQueViaja` devuelve el id tal cual |
| 349 | `map_decidir` con un decisor falso `Actuar=false`, `Cumplido=0,1` y `Porque`=«…ya está cumplido…»: el paso **no** lleva `Cumplido` y el tramo para por «no se atrevió»; con `Cumplido=0,9` y un `Porque` sin esa palabra: `Paso.Cumplido=true` y el motivo del tramo empieza por «Jev cree que ya está» y no por «el objetivo ya está cumplido:»; la cuenta lleva el inventario. `Elegir` con `cumplido` 0,9 dice «Jev cree» (289) | volver a `\|\| d.Porque.Contains("cumplido")` |
| 350 | `Elegir` con 6 claves (0,58 · 0,14 · 0,13 · 0,09 · 0,04 · 0,02) y `usage.input_tokens=312`: `N=6`, `Masa5` = 0,98 ± 0,001, `InputTokens=312`; sin `usage`: `InputTokens=null`. Los tres salen igual con `Actuar=false` (umbral 0,99). `map_decidir` con `LogBus.Logged`: la línea lleva «masa5», «×» y «tokens 312» o «tokens sin medir»; con `Masa5` de 0,23 y conf 0,99 **se acciona igual** (no es compuerta). El cuerpo no lleva ninguna pregunta «absent»/«ausente» | `Masa5` se queda en 0 (no se calcula) |
| 289 | el fixture de hoy con `Respuesta()` completa (Σ = 1, con ninguna); el caso `viejo` (sin nouls) exige `!Actuar` y «falta» en el porqué | ídem 343 |

**El sabotaje se comprueba, no se supone** (memoria del repo, 2026-08-21): copia del archivo, la línea,
`git diff` que la muestre, `dotnet build` sin silenciar, `contrato-del-grafo.ps1` con el veredicto
`CONTRATO ROTO` nombrando **esa** promesa y solo esa, restaurar, diff vacío, recompilar, `INTACTO`.

## Diseño

### El punto del ciclo que cambia: VALIDAR, y todo lo que hay detrás falla cerrado

```
DECIDIR  Jev: 1 POST = puerta(choice, + «0) ninguna») + cumplido(noul) + peligro(noul)
   │     lo que viaja lo decide PoliticaDeLoQueViaja (347) y IdQueViaja (348), ANTES del transporte
   ▼
VALIDAR  RespuestaDeJev.Validar(json, idsQueViajaron): la distribución ENTERA (342) y las nouls (343)
   │     fuera de forma ──▶ DecisionDeUnPaso.No con QueNoCuadro=«campo=valor», sin Alternativas
   │     en forma ──▶ compuertas, todas cerradas: ofrecida · ninguna (345) · cumplido≥0,70 (349: «Jev cree»)
   │                  · peligro≥0,50 · confianza<umbral
   ▼
PULSAR   UnPasoDecidido: candidatas = elegida + segunda, ambas por EsPeligrosa (344) ANTES del Take
   ▼
CONTAR   la línea «decisor:» y la cuenta: viajan N de M · K filas sin texto · C caracteres · masa5 · tokens (348, 350)
```

### `DecisionDeUnPaso`, con lo que le faltaba

Se conserva todo lo que hay (`Actuar`, `Puerta`, `Confianza`, `Porque`, `Alternativas`, `Cumplido`,
`Peligro`, las factorías internas `Si`/`No`/`Con` que el contrato invoca por reflexión). Se añade, como
`init`:

| Propiedad | Tipo | Qué es |
|---|---|---|
| `QueNoCuadro` | `string` | vacío si la respuesta estaba en forma; si no, el campo y el valor crudo («peligro=80 fuera de [0,1]», «Σ=0,95», «choice 0,31 < 0,58», «falta «cumplido»»). **Dato, no conclusión** (patrón nº2): es lo que D pinta y lo que se calibra |
| `N` | `int` | cuántas claves viajaron (ofrecidas que viajan + «ninguna») |
| `Masa5` | `double` | la suma de las `min(5, N)` mayores probabilidades, en crudo; la cuenta la enseña también «÷ lo plano» (`Masa5 / (k/N)`) |
| `InputTokens` | `int?` | lo que `usage.input_tokens` trajo; `null` = «sin medir» (vacío no es ausente: un 0 sería un dato) |
| `Viajaron`, `FilasSinTexto`, `Caracteres` | `int` | cuántos ids fueron a Jev, cuántos sin texto, y la longitud del cuerpo |

**Lo que conserva el valor crudo y lo que toma el caso peor** —y por qué no es lo mismo:

- `Confianza` **conserva el crudo** (95, 1,0000001, `NaN` si no era número). La línea `decisor:` y el
  notch lo imprimen tal cual: un 95 registrado como «0,00» se leería como «Jev duda siempre» al calibrar
  (informe §1.2). Con `Actuar=false` ese número no acciona nada.
- `Peligro` y `Cumplido` **toman el caso peor** (1 y 0) cuando no cuadran, y el crudo va en
  `QueNoCuadro` y en `Porque`. Conservar un `Cumplido=1,5` en la propiedad haría que
  `SurfaceMapTools.cs:299` (`d.Cumplido >= CumplidoMinimo`) marcara el paso como cumplido y el tramo
  dijera «ya está»: exactamente la conclusión que no se puede sacar de un número que no se entiende.
- `Alternativas` **queda vacía** cuando no cuadra: la segunda mejor de una distribución inválida no es
  una segunda mejor, y D no pinta barras de nada.

**Lo que NO se hace**: convertir en 0 (`3215466`), saturar a [0,1], ni tomar «la más probable» de un
empate. Un número fuera de dominio es una respuesta que no se entiende, y lo que no se entiende no
acciona.

### La validación, con sus tolerancias

| Campo | Regla | Tolerancia |
|---|---|---|
| cada `probabilities[k]` | número, finito, en [0,1] | ≤ 1 + 1e-6 se lee como 1 |
| claves de `probabilities` | **exactamente** las que viajaron (ofrecidas que viajan + «0) ninguna») | ordinal, por el mismo camino con que se construyó la lista que viajó (aprendizaje nº16) |
| `Σ probabilities` | 1 | ± 0,02 (jev-ultrafast `model.py:38`, que corre contra la API real) |
| `choice` | = la clave de mayor probabilidad | ≥ máximo − 1e-6; si **otra** clave también alcanza el máximo, empate → no se acciona |
| `confidence` | número, finito, en [0,1] | ≤ 1 + 1e-6 |
| `cumplido.noul`, `peligro.noul` | presentes, número, finitas, en [0,1] | ≤ 1 + 1e-6 |

**Desviación declarada frente a `arquitectura-jev-en-u.md` §2.1:** allí se exigía
`confidence = probabilities[choice] ± 0,02`. **No se exige**: el ejemplo documentado da 0,91 con
`probabilities[choice] = 0,93`, y TypeSafe dice que la calcula «from how probabilities is spread»
(`como-esta-hecho-jev.md:181-183`). Esa regla dejaría a Jev sin accionar sobre respuestas correctas.
La confianza se valida por rango; su relación con la distribución se **registra** (masa5, 350) y se
mira con cien pasos encima.

### `PoliticaDeLoQueViaja` (nuevo, `Decision/PoliticaDeLoQueViaja.cs`): un solo sitio, juzgado

| Ubicación (prefijo) | ¿Viaja el texto? | Decide entonces |
|---|---|---|
| `sapgui://` | **no**, salvo `U_DECISOR_SAP_TEXTO=si` **y** la decisión escrita del dueño y del hospital citada aquí (hoy no existe: la retención cero de TypeSafe es solo enterprise y aloja en EE. UU., `como-esta-hecho-jev.md` §tres cosas) | la regla local (`ElDecisor.Simulado`, sin red; con cero coincidencias no actúa, 281). Es **andamiaje** y lo dice en su porqué; el decisor local de verdad para SAP es otra rama (arquitectura §9) |
| con un prefijo de `U_DECISOR_TEXTO_VETADO` (lista separada por `;`; el dueño pone ahí el dominio del portal clínico, que también enseña pacientes) | no | ídem |
| `web://`, `uia://` y el resto | sí | Jev |
| cualquiera, tipos `GuiGridFila` · `GuiTreeFila` · `GuiTreeCarpeta` | **el texto nunca**, ni con SAP habilitado: viajan como «N) fila (GuiGridFila)» (348) | Jev decide entre lo demás; la mano resuelve por selector |

Se aplica **dentro de `ElDecisor.ConJev`, antes del transporte** —con `sapgui://` sin habilitación el
transporte no se toca ni una vez (275 y 277 siguen)— y **sobre los ids que viajan, no sobre los que se
ofrecen**: la lista que `UnPasoDecidido` numera no cambia (285 intacta); `ElDecisor` construye la lista
para Jev, la manda, y mapea la respuesta id→id ofrecido. Los tipos de fila se reconocen por el
sufijo «(Tipo)» del id, con el inverso del mismo formato con que se construyó (`EtiquetaDe` ya hace lo
mismo para la etiqueta). `ConfiguracionDelDecisor.Leer` lee las dos variables nuevas en una `Politica`,
y la comparación del prefijo va por `StartsWith("sapgui://", OrdinalIgnoreCase)`, el mismo camino que los
**16** sitios que hoy lo hacen a mano (contados arriba). No se refactorizan; se nombran en el commit.

**El log también es una salida.** La línea `decisor:` (`SurfaceMapTools.cs:293-295`) imprime hoy
`d.Puerta` con su texto. Con la 348 una fila elegida sale como «2) fila (GuiGridFila)» y la línea lleva
las cuentas (viajan/filas/caracteres), nunca el cuerpo. Las líneas `mano:` de `map_take` ya escriben la
etiqueta de una `GuiGridFila` en el log: es anterior a Jev, queda fuera y se anota como hallazgo.

### «Ninguna», el modelo y la firma

- `PeticionASystemOne.IdNinguna = "0) ninguna: nada de esta pantalla avanza hacia el objetivo"`. La añade
  `ConJev` a `criteria` (no al `state`, que lista puertas). El «0)» no choca con la numeración «1)…»
  de `UnPasoDecidido`. Si Jev la elige, `No("Jev no ve en esta pantalla nada que avance hacia el
  objetivo (0,80): no se acciona. Decide Luna.")`. **No es compuerta calibrada**: se registra, y con cien
  pasos se mira si separa aciertos de pérdidas (arquitectura §2.4).
- La instrucción «Si ninguna avanza hacia el objetivo, elige la que menos daño haga»
  (`PeticionASystemOne.cs:141`) **desaparece**: es el «clicking best guess» del vídeo, en español.
- `Elegir(quien, pantalla, objetivo, puertas, umbral, transporte)` **se conserva** (el contrato la
  invoca por reflexión con exactamente esos 6: `Contrato.cs:11539` y siguientes) y delega en la larga
  `Elegir(…, modelo, politica)` con `ModeloPorDefecto` y `PoliticaDeLoQueViaja.PorDefecto` (SAP no
  manda, sin vetos). El interruptor llama a la larga con `cfg.Modelo` y `cfg.Politica`, y gana una
  `FabricaDeTransporte` inyectable (por defecto `ClienteTypeSafe.TransporteSegun`) para que la 346 juzgue
  **el cableado real**, que es lo que estaba roto — hoy devolver `ModeloPorDefecto` seguiría en verde.
- La sonda `sondas/DelDecisor/Programa.cs` llama a `Elegir` en 4 líneas con el modelo fijo: pasa a la
  larga en la fase 4 (patrón nº5: «de los 3 sitios que llaman al decisor, solo se arregló 1»).

### El veto determinista, y dónde vive

`PuertasPeligrosas.EsPeligrosa(etiqueta)` (grabar, guardar, finalizar, salir del sistema, borrar,
eliminar, enviar, firmar; por etiqueta aplanada) se evalúa en `UnPasoDecidido` sobre **cada candidata
que se vaya a pulsar** —la elegida y la segunda— al construir `candidatos`, antes de cualquier `Take`.
Es el único sitio por el que pulsan `map_decidir` y el tramo (los dos pasan por `UnPasoDecidido`):
1 sitio nuevo, y `EsPeligrosa` pasa de 1 a 2 llamadores. La cuenta reutiliza
`PuertasPeligrosas.PorQue` (««Grabar» no se puede deshacer: te la dejo a ti.») palabra por palabra.
`SafeToClick.EsDestructivo` no sirve: excluye «guardar» a propósito (`Contrato.cs:9757`).

Consecuencia deliberada: **el tramo y `map_decidir` nunca pulsan «Guardar» aunque el objetivo sea
guardar**. Lo irreversible lo pulsa la persona, o Luna con un `map_take` explícito, como hasta hoy.

### «Jev cree que ya está» no es «cumplido»

- `cumplido ≥ 0,70` sirve para **dejar de accionar**, nunca para declarar éxito. `ElDecisor` dice «Jev
  cree que el objetivo ya está cumplido en esta pantalla (0,90): no acciono más. Que lo esté lo dice la
  llegada, no el modelo. Decide Luna.» (la 289 y la 292 siguen encontrando «cumplido» en el texto).
- `SurfaceMapTools.cs:299` decide `cumplido` **solo por el número** (`d.Cumplido >= CumplidoMinimo`); se
  quita `Porque.Contains("cumplido")`.
- `ElTramo.cs:157-159` para con «Jev cree que ya está ({c}): no acciono más; te devuelvo lo que hay
  delante para que lo compruebes» y la cuenta lleva el inventario. **Evidencia** = la llegada al destino
  esperado o el `observedSurface` de un workflow, juzgados por la compuerta del player (CLAUDE.md §*El
  agente que se rescata solo*, punto 3). El tramo de hoy no tiene destino esperado —`map_tramo` solo
  recibe el objetivo—, así que **nunca dice «cumplido»**.

### La señal que se registra y no decide

`N`, `Masa5` e `InputTokens` van a la decisión, a la cuenta de `map_decidir` («masa5 0,58 · 19× lo plano
· tokens 1.913») y a la línea `decisor:`, **desde una sola composición** (aprendizaje nº16: dos frases
para lo mismo discrepan). Ninguna compuerta los mira. `absent` no se pide: marcó 0,60 con la opción a la
vista (t=127); si algún día se pide, en sombra.

### Un catch mudo menos

`ClienteTypeSafe.cs:83` traga el motivo al leer el cuerpo de un error (patrón nº3). Se extrae
`DetalleDelError(Func<string> leer)`: devuelve el cuerpo, o «(no se pudo leer el cuerpo del error:
Tipo: mensaje)». Es la única línea de la rama en el único sitio que toca la red, y así tiene juez.

## Las fases

Una fase = un commit que pone verde **una** promesa sin romper las anteriores. El orden lo dictan las
dependencias: la «ninguna» tiene que viajar antes de que la 342 exija que la respuesta la traiga.

### Fase 0 — las promesas, en rojo

| | |
|---|---|
| **Deja** | 342–350 escritas y `PENDIENTE`; la 289 reescrita con su fixture (Σ = 1, `viejo` invertido); `RespuestaChoice` con «0) ninguna»; el caso 1 de la 292 con `Cumplido=0,9` |
| **Toca** | `tests/ContratoDelGrafo/Contrato.cs` (registro bajo `// ── Spec 046`, cuerpos en su región antes de `Debe`), `docs/specs/036-…md` (nota de la 289) |
| **Terminado** | `contrato-del-grafo.ps1` → **CONTRATO ROTO** exactamente por 342–350 y la 289; 341 y anteriores intactas |

### Fase 1 — «ninguna» viaja en la pregunta

| | |
|---|---|
| **Promesa** | 345 |
| **Toca** | `Decision/PeticionASystemOne.cs` (`IdNinguna`; sin «menos daño» lo hace la fase 5), `Decision/ElDecisor.cs` (la añade a lo que viaja; la acepta en la respuesta) |
| **Terminado** | 345 verde; 282 intacta byte a byte; 278–280 intactas con el ayudante ya cambiado |
| **Sitios** | 1 lista que viaja (`ConJev`); `CuerpoDeEleccion` no cambia |

### Fase 2 — la distribución se valida entera

| | |
|---|---|
| **Promesa** | 342 |
| **Toca** | `Decision/ElDecisor.cs` (`RespuestaDeJev.Validar`, `QueNoCuadro`, `Confianza` cruda, sin alternativas), `Decision/ClienteTypeSafe.cs:80-84` (`DetalleDelError`) |
| **Terminado** | 342 verde; 278, 279, 280, 289 intactas |
| **Sitios** | 3 lecturas numéricas sin rango hoy (`confidence` :175-177, `probabilities` :181, `Noul` :236-239) → 1 validador; 1 catch mudo |

### Fase 3 — las nouls fallan cerradas

| | |
|---|---|
| **Promesa** | 343 (y la 289 reescrita pasa a verde) |
| **Toca** | `Decision/ElDecisor.cs` (`Noul` deja de devolver 0 por ausencia; caso peor 1/0 con crudo en `QueNoCuadro`) |
| **Terminado** | 343 y 289 verdes; 342 intacta |
| **Sitios** | 1 (`Noul`), 2 llamadores (cumplido, peligro) |

### Fase 4 — el modelo configurado llega al cuerpo

| | |
|---|---|
| **Promesa** | 346 |
| **Toca** | `Decision/ElDecisor.cs` (sobrecarga larga; la de 6 delega), `Decision/InterruptorDelDecisor.cs:64-67` (`cfg.Modelo`, `FabricaDeTransporte`), `sondas/DelDecisor/Programa.cs` (4 líneas, a la larga) |
| **Terminado** | 346 verde; 275–281 y 290 intactas (la firma de 6 sigue) |
| **Sitios** | llamadores de `Elegir`: 1 producción + 1 sonda (4 líneas); los 5 pasan el modelo |

### Fase 5 — lo irreversible no se pulsa por decisión

| | |
|---|---|
| **Promesa** | 344 |
| **Toca** | `Mcp/SurfaceMapTools.cs:301-309` (`EsPeligrosa` sobre cada candidata; cuenta con `PorQue`), `Decision/PeticionASystemOne.cs:141` (fuera «menos daño») |
| **Terminado** | 344 verde; 285–288, 291–295 intactas (sus fixtures pulsan A/B/C/Detalles/Nuevo: comprobado con `grep` en fase 0, ninguna etiqueta peligrosa) |
| **Sitios** | `EsPeligrosa`: de 1 a 2 llamadores; `EsDestructivo` sigue en sus 2 |

### Fase 6 — la política de lo que viaja

| | |
|---|---|
| **Promesa** | 347 |
| **Toca** | **nuevo** `Decision/PoliticaDeLoQueViaja.cs`, `Decision/ConfiguracionDelDecisor.cs` (`U_DECISOR_SAP_TEXTO`, `U_DECISOR_TEXTO_VETADO` → `Politica`), `Decision/ElDecisor.cs` (antes del transporte; cae a `Simulado` diciéndolo), `docs/el-decisor-y-typesafe.md:88-93` (la prosa falsa) |
| **Terminado** | 347 verde; 275, 277, 281 intactas |
| **Sitios** | 16 comparaciones de `sapgui://` a mano, nombradas en el commit, sin tocar; la política es el sitio 17 y el único que decide qué viaja |

### Fase 7 — las filas nunca viajan por su texto

| | |
|---|---|
| **Promesa** | 348 |
| **Toca** | `Decision/PoliticaDeLoQueViaja.cs` (`IdQueViaja`, `EsFila`), `Decision/ElDecisor.cs` (lista que viaja ↔ ofrecida; `Viajaron`, `FilasSinTexto`, `Caracteres`), `Mcp/SurfaceMapTools.cs:293-295` (la línea `decisor:` con las cuentas y sin texto de fila) |
| **Terminado** | 348 verde; 285, 287, 288 intactas (la mano sigue recibiendo el selector de la ofrecida) |
| **Sitios** | 3 sitios construyen los tipos de fila (`MundoQueToca.cs:335, 351`, `FaceWindow.xaml.cs:379`); se reconocen por tipo exacto, los 3 |

### Fase 8 — «Jev cree que ya está» no es «cumplido»

| | |
|---|---|
| **Promesa** | 349 |
| **Toca** | `Mcp/SurfaceMapTools.cs:299` (solo por número), `Navigation/ElTramo.cs:154-161` (el motivo), `Decision/ElDecisor.cs:213-216` (el texto) |
| **Terminado** | 349 verde; 289 y 292 intactas (sus fixtures ya llevan el dato desde la fase 0) |
| **Sitios** | 1 decisión por subcadena (`:299`); 1 motivo del tramo; 1 mensaje del decisor |

### Fase 9 — la señal que se registra y no decide

| | |
|---|---|
| **Promesa** | 350 |
| **Toca** | `Decision/ElDecisor.cs` (`N`, `Masa5`, `InputTokens` al parsear), `Mcp/SurfaceMapTools.cs:293-295, 340-343` (una composición para la línea y la cuenta) |
| **Terminado** | 350 verde; 342–349 intactas; `contrato-del-grafo.ps1` → **CONTRATO INTACTO** |
| **Sitios** | 1 lectura de `usage` (hoy 0); 1 composición para 2 salidas |

**¿Núcleo congelado?** No: nada de `nucleo/`. `PuertasPeligrosas` y `Nombres` no cambian.

## Lo que NO entra

| Qué | Por qué |
|---|---|
| **Ningún clic físico cae en una ventana de Ü** (`SiCaeEnU`, arquitectura §2.8, numerada 350 allí) | el encargo de esta rama prohíbe tocar `UiaSurface`, y con la 341 gastada no queda número: va en una rama propia con el siguiente número libre después de D (386+). Hasta entonces, el precedente sigue (la carita tapó lo señalado, `SurfaceMapTools.cs:1262-1272`) |
| **El tramo protegido ante callbacks rotos** (`dc58914`: `AlEmpezar`, `HayQueParar`, `AlTerminar`, 3 de 6 `Log`) | el informe lo midió como correcto pero **a medias** (3 `Log` sin proteger, `ElTramo.cs:220, 225, 238`) y dice que el sitio único es `LogBus.cs:50`, fuera de esta rama; la arquitectura §7 lo deja como rama aparte (`tramo-callbacks-seguros`) que ninguna de las cuatro toca; y esta rama no tiene número para él. Es una feature distinta: no se casa con esta (una rama, una cosa) |
| `ProbabilidadSegura` (`3215466`) | convierte en 0 y 0 abre `peligro` y `cumplido` (M) |
| El arranque de `4198517` | elimina el onboarding (M); UI de riesgo alto; otra rama |
| Calentar la conexión, una lectura por ciclo, la observación con versión | rama C (`ClienteTypeSafe.cs:27-43, 110-121`; esta rama solo toca `:80-84`, a más de 10 líneas) |
| La espera por huella | rama B |
| Overlay, panel, flecha | rama D; consume `QueNoCuadro`, `Alternativas`, `Masa5`, `InputTokens` |
| Usar «ninguna», la masa del top-5 o `absent` como compuerta | sin calibración: seis lecturas de un vídeo ajeno y ningún fallo confirmado. Se registran; se mira con cien pasos |
| Cuerpo sin duplicados (`state` + `criteria`) y candidatos por relevancia (mejoras 18, 19) | rozan la 285 y la 282; se miden en sombra antes (C) |
| El decisor local de verdad para SAP | la 337 del dueño deja el hueco; aquí la regla local es andamiaje y lo dice |
| Habilitar `sapgui://` | es del dueño y del hospital, por escrito. Aquí solo se garantiza que sin eso no sale nada, y que con eso las filas tampoco |
| Refactorizar las 16 comparaciones de `sapgui://` | se nombran, no se tocan: la 347 compara por el mismo camino |
| Las líneas `mano:` con etiqueta de `GuiGridFila` en el log | anteriores a Jev; hallazgo para el dueño |
| Sombra (335 del dueño) | otra rama |

## Hallazgos

Se rellena durante la implementación. Lo encontrado al especificar, con fecha:

- **2026-09-22.** `f811796` gastó la 341 esta mañana: 342–350 en vez de 341–350.
- **2026-09-22.** `arquitectura-jev-en-u.md` §2.1 exigía `confidence = probabilities[choice] ± 0,02`;
  el ejemplo documentado (0,91 / 0,93) y la definición de TypeSafe lo desmienten. No se exige.
- **2026-09-22.** La 282 asegura `criteria.Count == opciones.Length` (`Contrato.cs:11731`): «ninguna»
  la tiene que añadir quien pregunta, no `CuerpoDeEleccion`, o la 282 rompe. La arquitectura decía «282
  sigue verde: todas las ofrecidas más una» sin mirar el fixture.
- **2026-09-22.** La 292 caso 1 pasa hoy **solo** por `Porque.Contains("cumplido")`
  (`Contrato.cs:12155`: el `Cumplido` no se pone). Quitar la subcadena sin tocar ese fixture la rompe.
- **2026-09-22.** `RespuestaChoice` (`Contrato.cs:11509`) es el ayudante de 275–281: con la 342 y la
  345 tiene que mandar la clave «ninguna», o 279 y 280 caen por «falta una clave».
- **2026-09-22.** `sondas/DelDecisor/Programa.cs` llama a `Elegir` con el modelo fijo en 4 líneas: la
  sonda que mide latencia mide otro modelo del configurado.

## Nivel 4, pendiente (no lo corre esta sesión: no se ejecuta U.exe ni se llama a TypeSafe)

1. **Con transporte falso, sin red**: la sonda `sondas/DelDecisor` con los valores del informe
   (1,2 · 80 · 1e400 · 1,0000001 · 1,5 · −0,01), Σ = 0,95, empate, clave de más y de menos: cada uno
   `Actuar=false` con su `QueNoCuadro`. Salida pegada en el PR.
2. **En el PC, `U_DECISOR=simulado`, dos pantallas con nombre**: Explorador (la barra trae «Eliminar»)
   con `map_decidir objetivo=eliminar el archivo` → veto con «no se puede deshacer» y 0 pulsos; y
   Configuración con un objetivo que la regla local sí casa → línea `decisor:` con «viajan N de M · 0
   filas sin texto · C caracteres · masa5 · tokens sin medir». Log pegado con horas.
3. **Con Jev real, solo con permiso del dueño (cuesta dinero)**: 3 decisiones sobre el Explorador
   (nunca sobre SAP), para contestar lo que sigue sin medir:
   - si TypeSafe manda alguna vez nouls o `confidence` fuera de [0,1] (registrando números y claves,
     nunca etiquetas);
   - si la respuesta trae **todas** las claves con 60+ puertas y si Σ cuadra en ±0,02 (si no, la
     tolerancia se cambia **por spec**, no en silencio);
   - qué probabilidad recibe «0) ninguna» cuando la puerta buena está a la vista;
   - `usage.input_tokens` frente a los 875/1.913/4.635 del 18-09.
4. **El botón**: `U_TYPESAFE_MODELO=jev-1.13.0` y el estado del interruptor nombrando ese modelo, con
   una decisión cuyo `decisor:` lo confirme (con permiso, cuenta como una de las 3).

## Cierre

- [ ] Todas las promesas verdes (`.\scripts\contrato-del-grafo.ps1` → CONTRATO INTACTO)
- [ ] Un sabotaje por promesa, verificado por diff, con el veredicto literal pegado aquí
- [ ] `.\scripts\verificar.ps1` pasa, con evidencia en `out\evidencia.md`
- [ ] Probado en ≥2 pantallas, con nombre: …
- [ ] Estado de este documento: **implementado** (AAAA-MM-DD)
