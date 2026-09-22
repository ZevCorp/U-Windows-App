# La distribución se valida entera: la decisión de Jev falla cerrada

Estado: **en construcción** · Spec 046 · 2026-09-22 · Rama `jero/jev-la-distribucion-se-valida-entera` ·
promesas **345–350 y 386–388** · reescribe la **289** (su última cláusula era la compuerta abierta)

> Rama A de las cuatro que siguen `arquitectura-jev-en-u.md` (22-09), todas desde `main`. Nace de la
> revisión de `experimento/reemplazo-jeff` (`informe-jev-rapido.md`, 21-09) y de cómo está hecho Jev de
> verdad (`como-esta-hecho-jev.md`). **La rama del dueño no se integra tal cual**: lo que de allí vale
> —el modelo configurado llegando al cuerpo— se rehace aquí con su promesa primero.
>
> **Números.** La arquitectura reservaba 341–350. `main` gastó tres de ellos el mismo día: `f811796`
> (Felipe, 09:27) la **341** («dos recordatorios vencidos despiertan una sola sesión de voz») y
> `5574148` (Felipe, entró a esta rama con el merge `db0cd41`) la **342** («una petición personal
> explícita se puede guardar…») y la **343** («cada proceso de Ü escribe en su propio archivo de
> log…»). Comprobado con `git grep` sobre las 45 refs `origin/*` tras `fetch` **(M)**: 344–352 y
> 384–390 libres. Los nueve de esta spec son **345–350 y 386–388** (la 344 la gastó `7380fd8` de `main` la misma tarde; ver Hallazgos) y, como 351–385 son de las ramas B, C y D,
> **386–387** —los dos siguientes libres después de D—. Nueve números, nueve promesas: lo que la
> arquitectura numeraba 350 (el clic físico que no cae en Ü) queda fuera, y se dice abajo por qué.
> Aparte: el checkout principal tiene sin commitear `docs/specs/046-el-decisor-lleva-todo-el-computer-use.md`
> reservando 335–343 —números que en `main` ya gastaron la 044 (335–340), `f811796` (341) y
> `5574148` (342–343)—. Propuesta: ese documento pasa a **050** y sus promesas a **389+**; si el
> dueño prefiere lo contrario, esta corre a 050 y las promesas se desplazan sin reciclar ninguna. Se
> le pregunta al abrir el PR.
>
> **Marcas.** **(M)** medido: log, sonda, `grep` o fixture. **(D)** deducido del código.
>
> **Revisada el 2026-09-22** contra siete refutaciones; las siete resultaron ciertas y están
> aplicadas. Qué decía, qué se midió y qué cambió: §*Revisiones*, al final.

## Diagnóstico: qué se midió

| Qué | Medida | Fuente |
|---|---|---|
| `peligro` = 1,2 · 80 · 87 · 1,0000001 · 1e400 en la rama del dueño | **pulsa «Grabar»** en las tres sondas; `main` no acciona («irreversible o peligroso (1.20)») | informe §1.1, tres sondas con transporte falso **(M)** |
| `cumplido` = 1,5 en la rama del dueño | acciona; `main` para | ídem **(M)** |
| Por qué: `ProbabilidadSegura` convierte en 0 lo que sale de [0,1] y las compuertas cortan **por arriba** (`≥0,70`, `≥0,50`) | 0 deja pasar | `ElDecisor.cs:250-262` de `4198517` **(M)** |
| En `main`, `Noul` devuelve 0 si la noul no vino, no es objeto o no es número | 0 deja pasar: **la compuerta está abierta hoy** | `ElDecisor.cs:236-239` **(M, lectura)** |
| La promesa 289 lo exige: «y una respuesta sin esas dos sigue valiendo» | su fixture `viejo` manda `null, null` y **exige** `Actuar` | `Contrato.cs:784`, `:12060-12061` **(M)** |
| El fixture de la 289 manda `probabilities` solo con la elegida | Σ = 0,95, y es el **único** Σ≠1 del contrato hoy | `Contrato.cs:12037-12045` **(M)** |
| `RespuestaChoice`, el ayudante de 275–281, no manda nouls; 278 («buena») y 279 («justa») exigen `Actuar=true` con él | una respuesta sin nouls que deje de accionar **rompe 278 y 279** si el ayudante no las añade | `Contrato.cs:11532-11540`, `:11642`, `:11661` **(M)** |
| `confidence` y `probabilities` se leen sin rango ni suma; una `confidence` de 95 o 1e400 se acepta | «con confianza Infinity» en la base | `ElDecisor.cs:175-182`; informe §1.1 **(M)** |
| En la rama del dueño una `confidence` de 95 queda en el log como `conf=0.00` | igual que una duda real de 0,30 | informe §1.2 **(M)**; patrón nº2 |
| `Type.GetMethod(nombre)` sin tipos, con dos métodos del mismo nombre | lanza `AmbiguousMatchException` (PowerShell: `[Enumerable].GetMethod("Where")`); con uno solo, no | sonda 22-09 **(M)** |
| Sitios del contrato que piden `ElDecisor.Elegir` así, sin tipos | **6**: `Contrato.cs:11546` (275), `:11626` (278), `:11649` (279), `:11668` (280), `:11696` (281), `:12021` (289); la excepción cae dentro de `Prueba` (`:11312-11315`) y cuenta como fallo | `grep` 22-09 **(M)** |
| Cómo lo evitó la rama del dueño | la larga se llama `ElegirConModelo`, no `Elegir` | `git diff origin/main..experimento/reemplazo-jeff -- ElDecisor.cs` **(M)** |
| Vetos deterministas en el camino de decidir | **0**: `EsPeligrosa` solo en `InstanciarSkill.cs:46`; `EsDestructivo` en 2 sitios (`SurfaceMapTools.cs:2317`, `PulsarSegunElNucleo.cs:217`) y excluye «guardar» a propósito (su juez en `Contrato.cs`) | `grep` 22-09 **(M)** |
| La segunda mejor se pulsa sin pasar por `peligro` (la noul juzgó solo a la elegida) ni por lista alguna | falla abierta | `SurfaceMapTools.cs:304-309` **(M, lectura)** |
| `cumplido` se decide también por subcadena del porqué | `d.Porque.Contains("cumplido")` | `SurfaceMapTools.cs:299` **(M)** |
| El caso 1 de la 292 juzga la **palabra**, no el dato | `c1.Contains("cumplido")` sobre `map_tramo_estado`; el `Cumplido` no se pone; la cuenta = motivo + `_pasos` («sin acción en el 1») + inventario (A/B/C), así que la palabra solo puede venir del motivo | `Contrato.cs:12178-12180`; `ElTramo.cs:157-159, 186-188, 210` **(M)** |
| `Simulado` **acciona**: confianza = palabras compartidas / palabras de la puerta, y `Si(...)` si ≥ umbral | una etiqueta de una palabra que aparezca en el objetivo da 1,00; su propio remark dice «doble de andamiaje… no para estimar qué haría el modelo» | `ElDecisor.cs:251-259, 283-297` **(M)** |
| El interruptor con `U_DECISOR=jev` dice «encendido: jev (…)» decida quien decida | | `InterruptorDelDecisor.cs:70` **(M)** |
| Qué viaja a `api.typesafe.ai` | la **ubicación entera** («Pantalla actual: {pantalla}»), el **objetivo** (en `state` y en `instructions`) y el texto de hasta 220 puertas, en `state` y como claves de `criteria`; filas de rejilla incluidas (`LeerRejilla` → `GuiGridFila` → `PuertasDeAhora`) | `PeticionASystemOne.cs:80-86, 130-134, 140`; `MundoQueToca.cs:331-336` **(M, lectura)**; sonda sintética del informe **(M)** |
| Qué es el pathname en `uia://` | el **título vivo** de la ventana: `new SurfaceIdentity($"uia://{proc}", "/" + title.Trim(), …)` | `UiaSurface.cs:245`; `SurfacePlace.cs:21` («título vivo (uia://)») **(M)** |
| Que el título, el objetivo o las filas reales lleven nombre y documento | 0 decisiones de Jev sobre SAP en 23 logs | **(D)**; el fixture `Contrato.cs:1385` («GIRALDO HERNAN · 2394346») lo ilustra |
| La política de lo que viaja | **no existe en código**: solo una prosa en `PeticionASystemOne.cs:122-126` y `docs/el-decisor-y-typesafe.md:88-93`, las dos falsas | **(M, lectura)** |
| El dominio del portal clínico | **no está escrito en este repo**; los únicos dominios de Miracle que aparecen son `itsmiracleai.com.co` (`mapeador/Contrato/Contrato.cs:495`) y el correo `@itsmiracleai.com` | `grep` 22-09 **(M)**; que el portal cuelgue de ellos es **(D)** |
| Sitios que comparan el prefijo `sapgui://` a mano | **16** (`StartsWith("sapgui://", OrdinalIgnoreCase)`) | `grep` 22-09 **(M)** |
| Cómo se recorta una ubicación a su origin | `SurfacePlace.OriginOf(url)` (`uia://chrome.exe/x` → `uia://chrome.exe`), ya existe y es el camino del resto del código | `windows-graph/src/SurfacePlace.cs:72-79` **(M)** |
| Dónde más sale al log la etiqueta de la puerta elegida | el relato de `map_decidir` («elegida «{Etiqueta}» (N)…») y la línea `paso k:` del tramo («paso k: «{Etiqueta}» (N) conf …»), al log y al notch; las exigen la 287 y la 294 | `SurfaceMapTools.cs:334-340`; `ElTramo.cs:203-208`; `Contrato.cs:782, :789` **(M)** |
| El modelo del cuerpo | siempre `ModeloPorDefecto` aunque `U_TYPESAFE_MODELO` diga otro; el botón enseña `cfg.Modelo` | `ElDecisor.cs:139`, `InterruptorDelDecisor.cs:70` **(M)** |
| Llamadores de `ElDecisor.Elegir` | **1** en producción (`InterruptorDelDecisor.cs:67`) + **4** líneas en `sondas/DelDecisor/Programa.cs` (`:54, :58, :84, :104`) | `grep` **(M)** |
| El contrato no construye `ElTramo.Paso` a mano | 0 sitios: lo que el tramo cuenta lo decide `UnPasoDecidido` | `grep` 22-09 **(M)** |
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
| **388** | la distribución de Jev se valida ENTERA antes de cualquier compuerta: cada probabilidad finita y en [0,1], la suma 1 (±0,02), las claves exactamente las que viajaron —ni una de más ni una de menos—, la elegida el máximo y sin empate, y la confianza finita y en [0,1]; cualquier cosa fuera de forma es «no sé»: no se acciona, el porqué nombra CADA regla que falló con su campo y su valor crudo —no solo la primera—, la decisión conserva la confianza cruda y no ofrece alternativas, y nada se convierte en 0 ni se satura; y un cuerpo de error que no se pudo leer dice por qué en vez de callarlo | 2 |
| **345** | «cumplido» y «peligro» fallan cerrados: si alguna falta, no es número, no es finita o está fuera de [0,1] se toma el caso peor —peligro 1, cumplido 0—, no se acciona, y el porqué dice cuál falta o cuál vino y con qué valor; un 0 solo abre la compuerta cuando Jev lo dijo | 3 |
| **346** | lo irreversible no se pulsa por decisión: una candidata cuya etiqueta es peligrosa —grabar, guardar, finalizar, borrar, eliminar, enviar, firmar— no se pulsa desde map_decidir ni desde el tramo, ni como elegida ni como segunda mejor, aunque Jev la dé con confianza 0,99 y peligro 0; la mano no la recibe, la cuenta dice cuál se vetó y por qué, el control vuelve con el inventario; y el cuerpo deja de pedirle a Jev «la que menos daño haga» | 5 |
| **347** | «ninguna» es una opción de la pregunta: además de TODAS las puertas ofrecidas viaja «0) ninguna» —nada de esta pantalla avanza hacia el objetivo— como una opción más del choice; si Jev la elige no se acciona y se dice que no lo ve en esta pantalla, con su probabilidad; y la añade quien pregunta, no CuerpoDeEleccion, así que la 282 sigue tal cual | 1 |
| **348** | el modelo configurado llega al cuerpo: con U_TYPESAFE_MODELO=X el cuerpo que manda el interruptor lleva «model»:«X», sin la variable lleva el alias por defecto, y el estado del botón y el cuerpo nombran el mismo modelo; la firma Elegir de seis argumentos se conserva como el ÚNICO método con ese nombre y delega en ElegirConModelo con los valores por defecto | 4 |
| **349** | a Jev solo viaja lo que la política permite: en sapgui:// sin U_DECISOR_SAP_TEXTO=si el transporte no se toca ni una vez y no se acciona —la decisión dice que no manda texto y que decide Luna, sin caer a ninguna regla local—; un origin vetado (los del portal clínico por defecto, más los de U_DECISOR_TEXTO_VETADO) tampoco viaja ni se decide; de la ubicación viaja solo el origin, nunca el título ni la ruta; y la política vive en un solo sitio y compara el prefijo por el mismo camino que el resto del código | 6 |
| **350** | las filas nunca viajan por su texto: GuiGridFila, GuiTreeFila y GuiTreeCarpeta llegan a Jev como «N) fila (tipo)», sin etiqueta, también con SAP habilitado; la respuesta se mapea por id a la puerta ofrecida y la mano sigue pulsando por selector; y lo que viajó no se registra por su texto: la línea «decisor:», el relato de map_decidir y la línea «paso k:» del tramo nombran una fila por número y tipo, y dicen cuántos ids viajaron de cuántos, cuántas filas fueron sin texto y cuántos caracteres se mandaron | 7 |
| **386** | «cumplido» solo con evidencia: un cumplido alto deja de accionar y el tramo para diciendo que Jev cree que ya está —con el porqué del decisor detrás— sin declarar el objetivo cumplido; lo decide el número, no el texto del porqué; y la cuenta del tramo devuelve el turno con lo que hay delante, para que lo compruebe quien sí puede —la llegada o la persona— | 8 |
| **387** | la masa de los cinco mejores y los tokens son señal, no compuerta: la decisión lleva N, la masa de los 5 mejores y los input_tokens que usage trajo —sin usage, «sin medir»—; los tres salen en la cuenta y en la línea «decisor:», también cuando no se acciona; ninguna decisión cambia por ellos; y «absent» no se pregunta | 9 |

**La que cierra el asunto es la 388**: mientras la respuesta no se lea entera, todo lo demás —vetos,
política, señal— actúa sobre números que pueden no significar nada.

### La 289, reescrita (el número no se recicla)

> **289.** una llamada, tres preguntas: el cuerpo lleva puerta (choice), cumplido (noul) y peligro
> (noul); con cumplido alto no se acciona y se dice que Jev cree que el objetivo ya está; con peligro
> alto no se acciona y se dice por qué; y una respuesta sin alguna de las dos, o con una fuera de [0,1],
> no se acciona y dice cuál falta (2026-09-22: hasta hoy «seguía valiendo» y 0 abría la compuerta)

Su fixture pasa a mandar `probabilities` completas (Σ = 1, con «0) ninguna») y el caso `viejo`
**invierte su aserción**. Se anota en la spec 036, donde nació. Su cláusula de ausencia es la misma
que la de la 345, y por eso **queda roja hasta la fase 3** (se dice en cada fase). Su caso «cumplido
alto» juzga «**ya está**», que es lo que comparten el mensaje de hoy («el objetivo ya está cumplido») y
el de la fase 8 («Jev cree que el objetivo ya está»): así la 289 pasa a verde en la fase 3 y no espera
a la 8. Que diga «Jev cree» lo juzga la 386, y solo ella.

### Fixtures ajenos que cambian, y sus enunciados no

| Promesa | Qué cambia en el fixture | Por qué | Enunciado |
|---|---|---|---|
| 278, 279, 280 | `RespuestaChoice` (el ayudante común) añade la clave «0) ninguna» con 0 **y las dos nouls a 0,1** | desde la 347 esa clave viaja y la 388 exige que la respuesta traiga **exactamente** las que viajaron; desde la 345 una respuesta sin nouls no acciona, y 278 («buena», `:11642`) y 279 («justa», `:11661`) exigen `Actuar=true` | intacto |
| 292, caso 1 | la decisión falsa lleva `Cumplido = 0,9` (por `DecisionCon`) desde la fase 0; en la fase 8, en el mismo commit que el motivo nuevo, su aserción pasa de `Contains("cumplido")` a `Contains("Jev cree que ya está")` | hoy el caso pasa por la **palabra**, que viene del `Porque` del fixture y sale igual por las dos ramas del tramo (`:299` la relee; el motivo la relaya): no distingue «cumplido» de «no se atrevió». Desde la 386 el número elige la rama y la aserción juzga la rama | intacto: «el decisor dice que el objetivo ya está cumplido» sigue siendo lo que pasa (`Cumplido=0,9`), y la cuenta lo dice como «Jev cree que ya está» |
| 282 | ninguno | «ninguna» la añade `ElDecisor`, no `CuerpoDeEleccion`; su `criteria.Count == opciones.Length` sigue verde | intacto |
| 287, 294 | ninguno | sus fixtures pulsan Button/Hyperlink, no filas; «fila 2 (GuiGridFila)» solo sustituye a la etiqueta cuando el tipo es fila (350) | intacto: «la cuenta la nombra por su etiqueta y su número» |
| 275, 278–281, 289 | ninguno | sus seis `GetMethod("Elegir")` sin tipos siguen encontrando **un único** método: la larga se llama `ElegirConModelo` (348) | intacto |
| 290 | ninguno | el constructor de tres argumentos del interruptor no cambia; `FabricaDeTransporte` es una propiedad que se puede fijar, con `ClienteTypeSafe.TransporteSegun` por defecto | intacto |

## Con qué se juzga cada una (sin pantalla, sin SAP, sin TypeSafe)

Todo por reflexión, con transporte falso, como las 275–290. Lo que aún no existe se pide por nombre
y cuenta como `Pendiente`. **`Validar` reporta cada violación por su nombre, no solo la primera**, y
cada caso del juez afirma el token de **su** regla: así cada sabotaje tiene una aserción propia.

| # | Juez | Sabotaje de una línea (verificado por diff) |
|---|---|---|
| 388 | `Elegir("jev", …)` con nouls presentes y bajas en todos los casos, y un transporte que devuelve, uno por caso: `confidence` 95 · 1,01 · 1e400 · −0,01 · «alta» (texto) —**1,0000001 es el contraste, no un fallo**: cae en la tolerancia ≤ 1+1e-6 de la tabla de diseño, se lee como 1 y acciona (fase 0, 2026-09-22)—; una probabilidad NaN (JSON no tiene NaN: llega como texto `"NaN"`); **Σ = 0,95 con TODAS las claves presentes y en rango** (0,85 · 0,10 · «0) ninguna» 0,00) y Σ = 1,10 (0,90 · 0,20 · 0,00); una clave de más («Grabar») y una de menos; la elegida con 0,31 cuando otra tiene 0,58; empate 0,45/0,45. **Todos**: `Actuar=false`, `Alternativas` vacía, `QueNoCuadro` y `Porque` llevan el token de **ese** caso («confidence=95», «Σ=0,95», «Σ=1,10», «sobra «Grabar»», «falta «…»», «choice 0,31 < 0,58», «empate»), `Confianza` conserva el crudo (95; `NaN` cuando no era número). Una respuesta que rompe dos reglas (Σ=0,95 **y** una clave de menos) nombra las dos. Y `ClienteTypeSafe.DetalleDelError(leer)` con un `leer` que lanza devuelve «(no se pudo leer el cuerpo del error: Tipo: mensaje)». Contraste: una respuesta en forma con «0) ninguna» a 0 sí acciona | en `Validar`, la tolerancia de la suma pasa de 0,02 a 10: el caso Σ=0,95 —que solo rompe esa regla— acciona y su `QueNoCuadro` pierde «Σ=0,95» |
| 345 | `Elegir("jev", …)` con la distribución en forma y `peligro` 1,2 · 80 · 1e400 · 1,0000001 · −0,01 · «sí» · ausente, y `cumplido` 1,5 · ausente: `Actuar=false`, `Peligro=1`, `Cumplido=0`, `Porque` con «peligro=80» o «falta «peligro»». Con las dos en [0,1] y bajas, acciona (289). Con `peligro` 0 dicho por Jev, acciona: el 0 vale cuando Jev lo dijo | en `ConJev`, el caso peor deja de ser 1/0 y `Peligro` conserva el crudo (80): solo la 345 se pone roja (la 289 no afirma ese valor) |
| 346 | `map_decidir` con puertas inyectadas «Nuevo»/«Grabar»/«Buscar» y un decisor falso que da «2) Grabar (Button)» con conf 0,99 y `Peligro=0`: **0 pulsos**, la cuenta lleva «Grabar» y «no se puede deshacer», y el inventario. Con la elegida «1) A» que no está viva y la segunda «2) Guardar» a 0,40: se pulsa A, **no** Guardar, y la cuenta dice que se vetó. `InstruccionesDeLaPuerta` no contiene «menos daño». Y por el tramo (`MapaParaTramo`) igual: 0 pulsos y para | quitar `PuertasPeligrosas.EsPeligrosa` del filtro de candidatas |
| 347 | `Elegir("jev", …)` con transporte que **captura** el cuerpo: `criteria` = las ofrecidas + «0) ninguna…», y ni una más; `state` no lista «ninguna» como puerta; la respuesta con `choice` = ninguna a 0,80 → `Actuar=false`, `Porque` con «no lo veo en esta pantalla» y «0,80». `CuerpoDeEleccion` con 3 opciones sigue dando 3 criterios (282) | no añadir `IdNinguna` a la lista que viaja |
| 348 | `InterruptorDelDecisor` con `FabricaDeTransporte` fijada (captura el cuerpo) y entorno `U_DECISOR=jev`, `TYPESAFE_API_KEY=x`, `U_TYPESAFE_MODELO=jev-1.13.0`: `Encender` y una llamada al `Decisor` del mapa → el cuerpo lleva `"model":"jev-1.13.0"` y `Estado` lo nombra; sin la variable, el `ModeloPorDefecto`. `Elegir` de 6 argumentos sigue existiendo, `GetMethod("Elegir")` sin tipos **no lanza** (es el único con ese nombre), y da la misma decisión que `ElegirConModelo` con los valores por defecto | el interruptor pasa `ModeloPorDefecto` en vez de `cfg.Modelo` |
| 349 | `ElegirConModelo` con `pantalla="sapgui://QAS/NWP1/…"` y un transporte que cuenta: **0 llamadas**, `Actuar=false`, `Porque` con «sapgui», «U_DECISOR_SAP_TEXTO» y «Decide Luna», y **sin** «simulad»; con `U_DECISOR_SAP_TEXTO=si`, 1 llamada. Con `Leer` de un entorno **vacío** y `pantalla="web://app.itsmiracleai.com/consulta"`: 0 llamadas (el veto por defecto no depende de ninguna variable); con `U_DECISOR_TEXTO_VETADO=web://historia` y `pantalla="web://historia/x"`, 0. Con `uia://explorer.exe/Historia clínica de prueba`, 1 llamada, y el cuerpo capturado contiene «uia://explorer.exe» y **no** «Historia clínica». `PoliticaDeLoQueViaja.PuedeViajar` es un solo método público, `VetadosPorDefecto` una constante no vacía, y `Leer(entorno)` lee las dos variables (vacío = ausente, patrón nº9) | `PuedeViajar` devuelve siempre `true` |
| 350 | `ElegirConModelo` con SAP habilitado y puertas «1) Nuevo (Button)», «2) fila de prueba · 000 (GuiGridFila)», «3) Triage/Urgencias (GuiTreeFila)», «4) Favoritos (GuiTreeCarpeta)» y transporte que captura: el cuerpo **no contiene** «fila de prueba», «Triage/Urgencias» ni «Favoritos» y sí «2) fila (GuiGridFila)»; la respuesta que elige «2) fila (GuiGridFila)» devuelve `Puerta` = «2) fila de prueba · 000 (GuiGridFila)» (la ofrecida) y `Viajaron=4`, `FilasSinTexto=3`, `Caracteres` = longitud del cuerpo. Por `map_decidir` con una `GuiGridFila` «fila de prueba · 000» inyectada, un decisor falso que la elige con esas cuentas, y `LogBus.Logged` suscrito: la línea `decisor:` lleva «viajan 4 de 4 · 3 filas sin texto · N caracteres» y **ni ella, ni la cuenta de `map_decidir` («elegida fila 2 (GuiGridFila)»), ni la línea `paso 1:` del tramo (`MapaParaTramo`, al log y al notch) llevan «fila de prueba»**; la mano recibe el selector de la fila | `IdQueViaja` devuelve el id tal cual |
| 386 | `map_decidir` con un decisor falso `Actuar=false`, `Cumplido=0,1` y `Porque`=«…ya está cumplido…»: el paso **no** lleva `Cumplido` y el tramo para por «no se atrevió»; con `Cumplido=0,9` y un `Porque` sin esa palabra: `Paso.Cumplido=true`, el motivo del tramo empieza por «Jev cree que ya está» —no por «el objetivo ya está cumplido:»— y lleva detrás ese `Porque`; la cuenta lleva el inventario. `Elegir` con `cumplido` 0,9 dice «Jev cree» (289) | volver a `\|\| d.Porque.Contains("cumplido")` |
| 387 | `Elegir` con 6 claves (0,58 · 0,14 · 0,13 · 0,09 · 0,04 · 0,02) y `usage.input_tokens=312`: `N=6`, `Masa5` = 0,98 ± 0,001, `InputTokens=312`; sin `usage`: `InputTokens=null`. Los tres salen igual con `Actuar=false` (umbral 0,99). `map_decidir` con `LogBus.Logged`: la línea lleva «masa5», «×» y «tokens 312» o «tokens sin medir»; con `Masa5` de 0,23 y conf 0,99 **se acciona igual** (no es compuerta). El cuerpo no lleva ninguna pregunta «absent»/«ausente» | `Masa5` se queda en 0 (no se calcula) |
| 289 | el fixture de hoy con `Respuesta()` completa (Σ = 1, con ninguna); el caso `viejo` (sin nouls) exige `!Actuar` y «falta» en el porqué | `Noul` vuelve a devolver 0 cuando la noul falta: el caso `viejo` acciona. Pone rojas **289 y 345 a la vez**, porque comparten la cláusula de ausencia; el veredicto tiene que nombrar las dos y ninguna más |

**El sabotaje se comprueba, no se supone** (memoria del repo, 2026-08-21): copia del archivo, la línea,
`git diff` que la muestre, `dotnet build` sin silenciar, `contrato-del-grafo.ps1` con el veredicto
`CONTRATO ROTO` nombrando **esa** promesa y solo esa (o las dos que la spec declara), restaurar, diff
vacío, recompilar, `INTACTO`.

## Diseño

### El punto del ciclo que cambia: VALIDAR, y todo lo que hay detrás falla cerrado

```
FILTRAR  PoliticaDeLoQueViaja (349): sapgui:// sin habilitar u origin vetado ──▶ No(causa). Decide Luna. (0 llamadas)
   │     lo que sí viaja: el origin de la ubicación, el objetivo, y los ids que IdQueViaja permite (350)
   ▼
DECIDIR  Jev: 1 POST = puerta(choice, + «0) ninguna») + cumplido(noul) + peligro(noul)
   ▼
VALIDAR  RespuestaDeJev.Validar(json, idsQueViajaron): la distribución ENTERA (388), TODAS las violaciones nombradas
   │     las nouls las juzga Noul → double? y el caso peor (345)
   │     fuera de forma ──▶ DecisionDeUnPaso.No con QueNoCuadro=«campo=valor · campo=valor», sin Alternativas
   │     en forma ──▶ compuertas, todas cerradas: ofrecida · ninguna (347) · cumplido≥0,70 (386: «Jev cree»)
   │                  · peligro≥0,50 · confianza<umbral
   ▼
PULSAR   UnPasoDecidido: candidatas = elegida + segunda, ambas por EsPeligrosa (346) ANTES del Take
   ▼
CONTAR   la línea «decisor:», el relato y la línea «paso k:»: viajan N de M · K filas sin texto · C caracteres
         · masa5 · tokens (350, 387); una fila se nombra «fila 2 (GuiGridFila)», nunca por su texto
```

### `DecisionDeUnPaso`, con lo que le faltaba

Se conserva todo lo que hay (`Actuar`, `Puerta`, `Confianza`, `Porque`, `Alternativas`, `Cumplido`,
`Peligro`, las factorías internas `Si`/`No`/`Con` que el contrato invoca por reflexión). Se añade, como
`init`:

| Propiedad | Tipo | Qué es |
|---|---|---|
| `QueNoCuadro` | `string` | vacío si la respuesta estaba en forma; si no, **todas** las reglas que fallaron, cada una con su campo y su valor crudo, separadas por « · » («peligro=80 fuera de [0,1] · Σ=0,95 · falta «2) Grabar (Button)»»). **Dato, no conclusión** (patrón nº2): es lo que D pinta y lo que se calibra |
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

**Lo que NO se hace**: convertir en 0 (`3215466`), saturar a [0,1], tomar «la más probable» de un
empate, ni parar en la primera regla rota (una respuesta que rompe dos reglas dice las dos: es lo que
deja que cada sabotaje tenga su aserción). Un número fuera de dominio es una respuesta que no se
entiende, y lo que no se entiende no acciona.

### La validación, con sus tolerancias, y quién aplica cada regla

| Campo | Regla | Tolerancia | Quién |
|---|---|---|---|
| cada `probabilities[k]` | número, finito, en [0,1] | ≤ 1 + 1e-6 se lee como 1 | `RespuestaDeJev.Validar` (388) |
| claves de `probabilities` | **exactamente** las que viajaron (ofrecidas que viajan + «0) ninguna») | ordinal, por el mismo camino con que se construyó la lista que viajó (aprendizaje nº16); el porqué nombra la clave que sobra o falta (la 278 exige ver «Grabar») | ídem |
| `Σ probabilities` | 1 | ± 0,02 (jev-ultrafast `model.py:38`, que corre contra la API real) | ídem |
| `choice` | = la clave de mayor probabilidad | ≥ máximo − 1e-6; si **otra** clave también alcanza el máximo, empate → no se acciona | ídem |
| `confidence` | número, finito, en [0,1] | ≤ 1 + 1e-6 | ídem |
| `cumplido.noul`, `peligro.noul` | presentes, número, finitas, en [0,1] | ≤ 1 + 1e-6 | `Noul` → `double?` (`null` = no vino, no es objeto o no es número) y el caso peor en `ConJev` (345). **No están en `Validar`**: si `Validar` las rechazara, `Noul` nunca correría con una ausente y el sabotaje de la 289 no podría aplicarse |

**Desviación declarada frente a `arquitectura-jev-en-u.md` §2.1:** allí se exigía
`confidence = probabilities[choice] ± 0,02`. **No se exige**: el ejemplo documentado da 0,91 con
`probabilities[choice] = 0,93`, y TypeSafe dice que la calcula «from how probabilities is spread»
(`como-esta-hecho-jev.md:181-183`). Esa regla dejaría a Jev sin accionar sobre respuestas correctas.
La confianza se valida por rango; su relación con la distribución se **registra** (masa5, 387) y se
mira con cien pasos encima.

### `PoliticaDeLoQueViaja` (nuevo, `Decision/PoliticaDeLoQueViaja.cs`): un solo sitio, juzgado

| Qué | ¿Viaja a Jev? | Decide entonces |
|---|---|---|
| Ubicación `sapgui://` | **no**, salvo `U_DECISOR_SAP_TEXTO=si` **y** la decisión escrita del dueño y del hospital citada aquí (hoy no existe: la retención cero de TypeSafe es solo enterprise y aloja en EE. UU., `como-esta-hecho-jev.md` §tres cosas) | **nadie**: `DecisionDeUnPaso.No("sapgui:// sin U_DECISOR_SAP_TEXTO: no se manda texto a Jev y no decido por regla local. Decide Luna.")`, con 0 llamadas. La regla local (`Simulado`) solo cuando `quien == "simulado"`: caer a ella con `quien == "jev"` no era fallar cerrado, era **cambiar de juez** —una que acciona con 1,00 por una palabra, mientras el botón dice «encendido: jev»— |
| Ubicación con un origin vetado: `VetadosPorDefecto` (constante, no vacía: hoy `web://itsmiracleai.com` y `web://itsmiracleai.com.co`, los únicos dominios de Miracle que este repo conoce; el host exacto del portal clínico **se confirma con el dueño**, §Hallazgos) más lo que liste `U_DECISOR_TEXTO_VETADO` (separado por `;`) | no | ídem: `No(...)` con el origin vetado en el porqué. Para `web://` el veto cubre el host y sus subdominios (`web://app.itsmiracleai.com` cae bajo `itsmiracleai.com`); para los demás esquemas, el prefijo del origin. **Un veto que dependa de una variable que nadie ha puesto no es fallar cerrado** |
| **La ubicación**, en el resto | solo su **origin** (`uia://explorer.exe`, `web://mail.google.com`), recortado con `SurfacePlace.OriginOf` —el mismo camino que usa el resto del código (aprendizaje nº16)—; **nunca el pathname**: en `uia://` es el título vivo de la ventana («/Historia clínica de …») y en `web://` la ruta de la página | Jev, sin el título |
| **El objetivo** | sí, tal cual lo dictó quien pidió el tramo: es la pregunta. Puede llevar un nombre («abre la historia de …»). **Se anota como riesgo aceptado**, no se limpia aquí: un limpiador de nombres sería una red que se cree puesta (aprendizaje nº18); la mitigación —que Luna formule objetivos sin datos de paciente— vive en Graph, fuera de esta rama | Jev |
| **Las etiquetas** en `web://`, `uia://` y el resto | **sí salen de la máquina**: botones, pestañas, nombres de archivo del Explorador, títulos de correos. Es lo que Jev necesita para decidir, y el dueño lo acepta por escrito igual que SAP —esta fila existe para que no se lea como «solo SAP tiene datos» | Jev |
| cualquier ubicación, tipos `GuiGridFila` · `GuiTreeFila` · `GuiTreeCarpeta` | **el texto nunca**, ni con SAP habilitado: viajan como «N) fila (GuiGridFila)» (350) | Jev decide entre lo demás; la mano resuelve por selector |

**Desviación declarada frente a `arquitectura-jev-en-u.md` §1 FILTRAR y §2.6:** allí «lo que no
viaja se ofrece igual al decisor local». **Aquí no**: lo que no puede viajar no se decide, y se dice.
El decisor local de verdad para SAP es otra rama (arquitectura §9); hasta entonces, en SAP con Jev
encendido, decide Luna.

Se aplica **dentro de `ElDecisor.ConJev`, antes del transporte** —con `sapgui://` sin habilitación el
transporte no se toca ni una vez (275 y 277 siguen)— y **sobre los ids que viajan, no sobre los que se
ofrecen**: la lista que `UnPasoDecidido` numera no cambia (285 intacta); `ElDecisor` construye la lista
para Jev, la manda, y mapea la respuesta id→id ofrecido. Los tipos de fila se reconocen por el
sufijo «(Tipo)» del id, con el inverso del mismo formato con que se construyó (`EtiquetaDe` ya hace lo
mismo para la etiqueta), y `UnPasoDecidido` usa **la misma** `EsFila` para nombrar la fila en el
relato. `ConfiguracionDelDecisor.Leer` lee las dos variables nuevas en una `Politica`, y la
comparación del prefijo `sapgui://` va por `StartsWith("sapgui://", OrdinalIgnoreCase)`, el mismo
camino que los **16** sitios que hoy lo hacen a mano (contados arriba). No se refactorizan; se
nombran en el commit.

**El log también es una salida, y son tres líneas, no una.** La etiqueta de la puerta elegida se
escribe hoy en la línea `decisor:` (`SurfaceMapTools.cs:293-295`), en el relato de `map_decidir`
(`:334-340`, «elegida «{Etiqueta}» (N)…») y en la línea `paso k:` del tramo (`ElTramo.cs:203-208`, al
log **y al notch**). Limpiar solo la primera sería cosmético: en NWP1 «GIRALDO HERNAN · 2394346»
saldría igual en `u-AAAAMMDD.log` por las otras dos. Con la 350, `UnPasoDecidido` pasa al relato y al
`Paso` del tramo el **nombre para contar**: la etiqueta si la puerta no es fila, y «fila N (Tipo)» si
lo es —**1 sitio**, porque el tramo no construye nombres: cuenta el que le dan—. La 287 y la 294
siguen tal cual (sus fixtures no tienen filas). Las líneas `mano:` de `map_take` ya escriben la
etiqueta de una `GuiGridFila` en el log: es anterior a Jev, queda fuera y se anota como hallazgo.

### «Ninguna», el modelo y la firma

- `PeticionASystemOne.IdNinguna = "0) ninguna: nada de esta pantalla avanza hacia el objetivo"`. La añade
  `ConJev` a `criteria` (no al `state`, que lista puertas). El «0)» no choca con la numeración «1)…»
  de `UnPasoDecidido`. Si Jev la elige, `No("Jev eligió «ninguna» (0,80): no lo veo en esta pantalla —nada
  de lo que hay avanza hacia el objetivo—. No se acciona. Decide Luna.")` —la frase «no lo veo en esta
  pantalla» es la que el juez de la 347 exige y la que D reutiliza—. **No es compuerta calibrada**: se
  registra, y con cien pasos se mira si separa aciertos de pérdidas (arquitectura §2.4).
- La instrucción «Si ninguna avanza hacia el objetivo, elige la que menos daño haga»
  (`PeticionASystemOne.cs:141`) **desaparece**: es el «clicking best guess» del vídeo, en español.
- `Elegir(quien, pantalla, objetivo, puertas, umbral, transporte)` **se conserva y sigue siendo el único
  método con ese nombre**. La larga se llama **`ElegirConModelo(quien, pantalla, objetivo, puertas,
  umbral, transporte, modelo, politica)`** —el nombre de la rama del dueño, que lo evitó sin decirlo—
  y `Elegir` delega en ella con `ModeloPorDefecto` y `PoliticaDeLoQueViaja.PorDefecto` (SAP no manda;
  vetados por defecto, sin variables). **No es una sobrecarga, y no es estilo**: el contrato pide la
  pieza con `GetMethod("Elegir")` sin tipos en **6** sitios, y con dos métodos del mismo nombre esa
  llamada lanza `AmbiguousMatchException` (medido): 275, 278, 279, 280, 281 y 289 se pondrían rojas a la
  vez en la fase 4, y el portero no dejaría empujar. El interruptor llama a `ElegirConModelo` con
  `cfg.Modelo` y `cfg.Politica`, y gana una `FabricaDeTransporte` (propiedad que se puede fijar; por
  defecto `ClienteTypeSafe.TransporteSegun`; el constructor de tres argumentos no cambia, la 290 lo
  instancia por reflexión) para que la 348 juzgue **el cableado real**, que es lo que estaba roto —
  hoy devolver `ModeloPorDefecto` seguiría en verde.
- La sonda `sondas/DelDecisor/Programa.cs` llama a `Elegir` en 4 líneas con el modelo fijo: pasa a
  `ElegirConModelo` en la fase 4 (patrón nº5: «de los 3 sitios que llaman al decisor, solo se arregló 1»).

### El veto determinista, y dónde vive

`PuertasPeligrosas.EsPeligrosa(etiqueta)` (grabar, guardar, finalizar, salir del sistema, borrar,
eliminar, enviar, firmar; por etiqueta aplanada) se evalúa en `UnPasoDecidido` sobre **cada candidata
que se vaya a pulsar** —la elegida y la segunda— al construir `candidatos`, antes de cualquier `Take`.
Es el único sitio por el que pulsan `map_decidir` y el tramo (los dos pasan por `UnPasoDecidido`):
1 sitio nuevo, y `EsPeligrosa` pasa de 1 a 2 llamadores. La cuenta reutiliza
`PuertasPeligrosas.PorQue` (««Grabar» no se puede deshacer: te la dejo a ti.») palabra por palabra.
`SafeToClick.EsDestructivo` no sirve: excluye «guardar» a propósito.

Consecuencia deliberada: **el tramo y `map_decidir` nunca pulsan «Guardar» aunque el objetivo sea
guardar**. Lo irreversible lo pulsa la persona, o Luna con un `map_take` explícito, como hasta hoy.

### «Jev cree que ya está» no es «cumplido»

- `cumplido ≥ 0,70` sirve para **dejar de accionar**, nunca para declarar éxito. `ElDecisor` dice «Jev
  cree que el objetivo ya está cumplido en esta pantalla (0,90): no acciono más. Que lo esté lo dice la
  llegada, no el modelo. Decide Luna.» — describe lo que Jev cree, no concluye (patrón nº2).
- `SurfaceMapTools.cs:299` decide `cumplido` **solo por el número** (`d.Cumplido >= CumplidoMinimo`); se
  quita `Porque.Contains("cumplido")`.
- `ElTramo.cs:157-159` para con «**Jev cree que ya está: {p.Porque}**» —el porqué del decisor detrás,
  con su número— y la cuenta lleva el inventario. Lo que se promete es que **el tramo no declara éxito**
  («el objetivo ya está cumplido:» desaparece como motivo), no que la palabra no aparezca en el porqué
  que se relaya. **Evidencia** = la llegada al destino esperado o el `observedSurface` de un workflow,
  juzgados por la compuerta del player (CLAUDE.md §*El agente que se rescata solo*, punto 3). El tramo
  de hoy no tiene destino esperado —`map_tramo` solo recibe el objetivo—, así que nunca lo declara.
- El caso 1 de la 292 pasa a juzgar la rama («Jev cree que ya está») y no la palabra: hoy la palabra
  viene del `Porque` del fixture y sale igual por las dos ramas del tramo, así que el caso no
  distinguía «cumplido» de «no se atrevió». Se cambia en la fase 8 (tabla de fixtures ajenos).

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
dependencias: la «ninguna» tiene que viajar antes de que la 388 exija que la respuesta la traiga. La
289 reescrita **está roja desde la fase 0 hasta la 3** por su cláusula de ausencia, que vive en
`Noul` (fase 3); cada fase lo dice.

### Fase 0 — las promesas, en rojo

| | |
|---|---|
| **Deja** | 345–350 y 386–388 escritas y `PENDIENTE`; la 289 reescrita con su fixture (Σ = 1, `viejo` invertido); `RespuestaChoice` con «0) ninguna» **y las dos nouls a 0,1**; el caso 1 de la 292 con `Cumplido=0,9` (su aserción nueva llega en la fase 8) |
| **Toca** | `tests/ContratoDelGrafo/Contrato.cs` (registro bajo `// ── Spec 046`, cuerpos en su región antes de `Debe`), `docs/specs/036-…md` (nota de la 289) |
| **Terminado** | `contrato-del-grafo.ps1` → **CONTRATO ROTO** exactamente por 345–350 y 386–388 y la 289; 343 y anteriores intactas (la 292 sigue verde: `Cumplido=0,9` elige la misma rama que hoy elige la subcadena) |
| **Hecho** | 2026-09-22. Veredicto literal: `CONTRATO ROTO: 23 promesa(s) incumplida(s). El cambio no puede entrar así.` Rojas **exactamente** 289, 388, 345, 346, 347, 348, 349, 350, 386, 387; **280 verdes**; 7 `PENDIENTE` (388, 345, 347, 348, 349, 350, 387) y 3 rojas por aserción contra el código de hoy (289: `viejo` acciona y no dice cuál falta; 346: pulsa «Grabar» elegida, pulsa «Guardar» como segunda, y el tramo pulsa «Grabar» tres veces; 386: la palabra «cumplido» del porqué elige la rama). El **23** es lo que el arnés cuenta desde siempre: aserciones fallidas, no promesas (16 aserciones + 7 pendientes) |

### Fase 1 — «ninguna» viaja en la pregunta

| | |
|---|---|
| **Promesa** | 347 |
| **Toca** | `Decision/PeticionASystemOne.cs` (`IdNinguna`; sin «menos daño» lo hace la fase 5), `Decision/ElDecisor.cs` (la añade a lo que viaja; la acepta en la respuesta) |
| **Terminado** | 347 verde; 282 intacta byte a byte; 278–280 intactas con el ayudante ya cambiado; 289 roja (fase 3) |
| **Sitios** | 1 lista que viaja (`ConJev`); `CuerpoDeEleccion` no cambia |
| **Hecho** | 2026-09-22. `grep CuerpoDeEleccion(` en `windows-client/src`: **1** llamador de producción (`ElDecisor.cs:146`), y es el que ahora manda `puertas + IdNinguna`; el `state` sigue recibiendo solo `puertas`. La sonda `sondas/DelDecisor/Programa.cs:62` arma su propio cuerpo sin «ninguna» —mide latencia, no decisión— y queda fuera. Veredicto literal: `CONTRATO ROTO: 22 promesa(s) incumplida(s). El cambio no puede entrar así.` — rojas exactamente 289, 388, 345, 346, 348, 349, 350, 386, 387 (las de las fases que siguen); 347 ✔, 282 ✔ sin tocarla, 278–280 ✔. Voz: `VOZ ÍNTEGRA: el collar promete lo que dice prometer.` **Sabotaje** (M): quitar `queViaja.Add(PeticionASystemOne.IdNinguna)` (diff contra copia: distinto) → `CONTRATO ROTO: 23 promesa(s)`, con 347 roja por «criteria = las ofrecidas + «0) ninguna», y ni una más; viajaron [1) Nuevo (Button) · 2) Buscar (Button)]»; restaurada (diff idéntico), recompilada: 22 otra vez. El contrato corrió con `TEMP` propio porque `%TEMP%\u-contrato` estaba bloqueado por un `contrato-del-grafo` de otro worktree (MSB3027): el scratch del script es compartido entre worktrees, y eso es un hallazgo aparte |

### Fase 2 — la distribución se valida entera

| | |
|---|---|
| **Promesa** | 388 |
| **Toca** | `Decision/ElDecisor.cs` (`RespuestaDeJev.Validar` con todas las violaciones, `QueNoCuadro`, `Confianza` cruda, sin alternativas), `Decision/ClienteTypeSafe.cs:80-84` (`DetalleDelError`) |
| **Terminado** | 388 verde; 278, 279, 280 intactas; **289 sigue ROJA hasta la fase 3** (su caso `viejo` es la cláusula de ausencia, y `Validar` no la toca) |
| **Sitios** | 2 lecturas numéricas sin rango hoy (`confidence` :175-177, `probabilities` :181) → 1 validador; 1 catch mudo. `Noul` (:236-239) es de la fase 3 |
| **Hecho** | 2026-09-22. `RespuestaDeJev.Validar(JsonElement puerta, queViaja)` en `ElDecisor.cs`: lee `choice`, `confidence` y `probabilities` y devuelve `Violaciones` (todas), `Confianza` cruda (`NaN` si no era número, `Infinity` si vino `1e400` —**(M)** sonda: `Utf8JsonReader` lee `1e400` como `Infinity` sin lanzar—) y `Alternativas` vacía si no cuadra; la suma y el máximo solo se juzgan cuando cada valor se pudo leer (la suma de un `NaN` no dice nada de la distribución, y esa clave ya se nombró). `ConJev` devuelve `No(«la respuesta de Jev no cuadra y no se acciona: …»)` con `QueNoCuadro` antes de cualquier compuerta. `ClienteTypeSafe.DetalleDelError(Func<string>)` sustituye el `catch { }`. **Sitios (M)**: `grep "confidence"\|"probabilities"` en producción → solo `Validar` (`windows-graph/src/Contracts.cs:389` es el contrato de Graph, no Jev); `catch { }` en `Decision/` → 0. Veredicto literal: `CONTRATO ROTO: 41 promesa(s) incumplida(s). El cambio no puede entrar así.` — rojas exactamente 289, 345, 346, 348, 349, 350, 386, 387; **282 verdes**; 388 ✔, 347 ✔, 278–280 ✔, 282 ✔. El salto 22 → 41 es la 345: dejó de ser `PENDIENTE` (existe `QueNoCuadro`) y corre sus 21 aserciones contra el `Noul` de hoy — está roja por su razón y la 289 por la suya (`viejo` acciona: `Validar` no toca las nouls). Voz: `VOZ ÍNTEGRA: el collar promete lo que dice prometer.` **Sabotaje** (M): `ToleranciaDeLaSuma` 0,02 → 10 (diff contra copia: `95c95`, una línea) → `CONTRATO ROTO: 51`, 388 roja con «Σ=0,95: no se acciona (salió Actuar=True)», «QueNoCuadro … dijo «»», «Σ=1,10: no se acciona» y el caso doble conservando «falta «2) Buscar (Button)»» pero sin «Σ=0.95» — lo que la revisión 2 anticipó. Restaurada (`fc /B`: sin diferencias), recompilada y juzgada: 41 otra vez, 388 ✔. **Dos trampas del arnés que se pisaron y se dicen**: (1) `sed -i` de Git Bash pasó el archivo de CRLF a LF —el diff daba `1,507c1,507`, no una línea— y se restauró desde la copia antes de aplicar el sabotaje con una edición que conserva bytes; (2) restaurar con `Copy-Item` conserva la **fecha** del `.bak`, anterior al sabotaje, así que el build incremental no recompiló y el primer veredicto «restaurado» seguía siendo el saboteado (51, 388 roja) con `fc` diciendo «sin diferencias»; se comprobó con las fechas (`.cs` 11:03:54 < `U.dll` 11:04:37) y se resolvió tocando la fecha del archivo. **Un diff idéntico no basta: hay que ver que el binario se rehizo.** |

### Fase 3 — las nouls fallan cerradas

| | |
|---|---|
| **Promesa** | 345 (y la 289 reescrita pasa a verde) |
| **Toca** | `Decision/ElDecisor.cs` (`Noul` → `double?`, deja de devolver 0 por ausencia; caso peor 1/0 en `ConJev` con el crudo en `QueNoCuadro`) |
| **Terminado** | 345 y 289 verdes; 388 intacta; 278, 279 intactas (el ayudante ya manda nouls) |
| **Sitios** | 1 (`Noul`), 2 llamadores (cumplido, peligro) |
| **Hecho** | 2026-09-22. `Noul(answers, id, out crudo)` devuelve `double?` (`null` si no vino o no es número; `crudo` es el texto JSON tal cual: «80», «"sí"», «1e400», vacío si no vino) y `NoulCerrada(answers, id, peor, violaciones)` decide el caso peor y nombra la regla: `falta «peligro»` · `peligro=80 fuera de [0,1]` · `peligro="sí" no es número` · `peligro=1e400 no es finito`; ≤ 1+1e-6 se lee como 1 (la misma tolerancia que las probabilidades). Lo que no cuadra de las nouls se suma a `QueNoCuadro` (después de lo de `Validar`, con « · ») y `ConJev` no acciona por la misma salida que la 388: `Alternativas` vacía —con `Actuar=false` `map_decidir` no mira la segunda mejor, comprobado en `SurfaceMapTools.cs:296`—. **Sitios (M)**: `grep Noul(` en `windows-client/src` y `windows-graph/src` → 1 definición, 2 llamadores (`ElDecisor.cs:372-373`), 0 restos de «0 si no se preguntó». Antes de traer `main` el conflicto: `7380fd8` gastó la **344** y la de esta spec pasó a **388** (Hallazgos). Veredicto de partida tras el merge: `CONTRATO ROTO: 41` (rojas 289, 345, 346, 348, 349, 350, 386, 387; 388 verde). Veredicto literal al terminar: `CONTRATO ROTO: 17 promesa(s) incumplida(s). El cambio no puede entrar así.` — rojas exactamente 346, 348, 349, 350, 386, 387; **345 ✔, 289 ✔, 388 ✔, 278/279/280/282 ✔, 344 (main) ✔**. Voz: `VOZ ÍNTEGRA: el collar promete lo que dice prometer.` **Sabotajes (M), uno por promesa, con copia y `fc`**: (345) `ElDecisor.cs:488` `return peor;` → `return v;` en la regla de rango → `CONTRATO ROTO: 21`, 345 roja con «peligro 80: Peligro toma el caso peor, 1; salió 80» (y 1,2 · −0,01 · cumplido 1,5), la 289 verde como anticipaba la tabla de sabotajes; (289) `:484` `falta «{id}»` → `{id} no vino` → `CONTRATO ROTO: 20`, 289 roja por «y se dice cuál falta (dijo: «… cumplido no vino · peligro no vino»)» y 345 por «falta «peligro»» / «falta «cumplido»». Las dos veces: restaurada con `fc /B` sin diferencias, fecha del `.cs` tocada, `U.dll` posterior al `.cs` (11:22:05 < 11:22:10 y 11:27:56 < 11:28:01), y el juicio de vuelta a 17 |

### Fase 4 — el modelo configurado llega al cuerpo

| | |
|---|---|
| **Promesa** | 348 |
| **Toca** | `Decision/ElDecisor.cs` (`ElegirConModelo`, nombre distinto —no sobrecarga—; `Elegir` de 6 delega), `Decision/InterruptorDelDecisor.cs:64-67` (`cfg.Modelo`, `FabricaDeTransporte`), `sondas/DelDecisor/Programa.cs` (4 líneas, a `ElegirConModelo`) |
| **Terminado** | 348 verde; 275–281, 289 y 290 intactas (`GetMethod("Elegir")` sigue encontrando uno solo) |
| **Sitios** | llamadores de `Elegir`: 1 producción + 1 sonda (4 líneas); los 5 pasan el modelo. 6 `GetMethod("Elegir")` sin tipos en el contrato, sin tocar |
| **Hecho** | 2026-09-22. `ElegirConModelo(quien, pantalla, objetivo, puertas, umbral, transporte, modelo, politica)` en `ElDecisor.cs`; `Elegir` de seis delega con `ModeloPorDefecto` y `PoliticaDeLoQueViaja.PorDefecto` y sigue siendo **el único** método con ese nombre; `ConJev` recibe el modelo y lo pone en el cuerpo (vacío → alias por defecto, patrón nº9). `InterruptorDelDecisor` gana `FabricaDeTransporte` (propiedad; por defecto `ClienteTypeSafe.TransporteSegun`; el constructor de tres no cambia) y llama a `ElegirConModelo` con `cfg.Modelo`. **Lo mínimo que el código exigió fuera de la fase**: `PoliticaDeLoQueViaja` nace con SOLO `PorDefecto` (la 348 la pide por reflexión para delegar); `Leer`/`PuedeViajar`/`VetadosPorDefecto` siguen en la fase 6 y la 349 sigue `PENDIENTE` por ellas. **Sitios (M)**: `ElDecisor.Elegir(` en producción → 0 (el interruptor era el único, y pasa a `ElegirConModelo`); `ElegirConModelo(` → 1 delegación + 1 interruptor + 4 líneas de la sonda; `GetMethod("Elegir")` en el contrato → **18** literales sin tipos (no 6 como decía esta fase), sin tocar, y todos resuelven. La sonda no compila contra su `UBin` por defecto (`C:\U-dev2\bin`, un `U.dll` sin `Decision`); compilada con `-p:UBin=<Release del worktree>`: exit 0. Veredicto literal: `CONTRATO ROTO: 16 promesa(s) incumplida(s). El cambio no puede entrar así.` — rojas exactamente 346, 349, 350, 386, 387 (fases 5–8); **348 ✔, 275–282 ✔, 289 ✔, 290 ✔, 344 ✔, 345 ✔, 347 ✔, 388 ✔**. Voz: `VOZ ÍNTEGRA: el collar promete lo que dice prometer.` **Sabotaje (M)**: `InterruptorDelDecisor.cs:84` `cfg.Modelo` → `ConfiguracionDelDecisor.ModeloPorDefecto` (`fc /N` contra copia: una línea) → `CONTRATO ROTO: 17`, la 348 roja por «con U_TYPESAFE_MODELO=jev-1.13.0 el cuerpo lleva "model":"jev-1.13.0"; llevó «jev-latest»»; restaurada (hash SHA-256 idéntico a la copia), fecha tocada, `U.dll` del scratch posterior al `.cs` (11:40:05 < 11:40:23): 16 otra vez, 348 ✔. **Hallazgo del arnés (M)**: la primera corrida del contrato murió sin veredicto —`NO SE PUDO JUZGAR`— después de la 240 y sin línea de excepción, en medio de la 220 (la sesión de voz que abre un socket); la segunda corrida idéntica dio el 16. No se explica; se anota que el arnés puede morir mudo ahí y que el script lo distingue bien de un rojo |

### Fase 5 — lo irreversible no se pulsa por decisión

| | |
|---|---|
| **Promesa** | 346 |
| **Toca** | `Mcp/SurfaceMapTools.cs:301-309` (`EsPeligrosa` sobre cada candidata; cuenta con `PorQue`), `Decision/PeticionASystemOne.cs:141` (fuera «menos daño») |
| **Terminado** | 346 verde; 285–288, 291–295 intactas (sus fixtures pulsan A/B/C/Detalles/Nuevo: comprobado con `grep` en fase 0, ninguna etiqueta peligrosa) |
| **Sitios** | `EsPeligrosa`: de 1 a 2 llamadores; `EsDestructivo` sigue en sus 2 |

### Fase 6 — la política de lo que viaja

| | |
|---|---|
| **Promesa** | 349 |
| **Toca** | **nuevo** `Decision/PoliticaDeLoQueViaja.cs` (`PuedeViajar`, `VetadosPorDefecto`, `UbicacionQueViaja` sobre `SurfacePlace.OriginOf`), `Decision/ConfiguracionDelDecisor.cs` (`U_DECISOR_SAP_TEXTO`, `U_DECISOR_TEXTO_VETADO` → `Politica`), `Decision/ElDecisor.cs` (antes del transporte: `No(...)` con la causa, sin caer a `Simulado`; el origin en el `state`), `docs/el-decisor-y-typesafe.md:88-93` (la prosa falsa) |
| **Terminado** | 349 verde; 275, 277, 281 intactas |
| **Sitios** | 16 comparaciones de `sapgui://` a mano, nombradas en el commit, sin tocar; la política es el sitio 17 y el único que decide qué viaja. 1 recorte de origin, por `OriginOf` (ya existe) |

### Fase 7 — las filas nunca viajan por su texto, ni al log

| | |
|---|---|
| **Promesa** | 350 |
| **Toca** | `Decision/PoliticaDeLoQueViaja.cs` (`IdQueViaja`, `EsFila`), `Decision/ElDecisor.cs` (lista que viaja ↔ ofrecida; `Viajaron`, `FilasSinTexto`, `Caracteres`), `Mcp/SurfaceMapTools.cs:293-295, 334-343` (la línea `decisor:` con las cuentas; el nombre para contar —etiqueta o «fila N (Tipo)»— en el relato y en el `Paso` del tramo) |
| **Terminado** | 350 verde; 285, 287, 288, 294 intactas (la mano sigue recibiendo el selector de la ofrecida; sus fixtures no tienen filas) |
| **Sitios** | 3 sitios construyen los tipos de fila (`MundoQueToca.cs:335, 351`, `FaceWindow.xaml.cs:379`); se reconocen por tipo exacto, los 3. 3 líneas de log llevaban la etiqueta (`decisor:`, relato, `paso k:`); 1 sitio las alimenta (`UnPasoDecidido`) |

### Fase 8 — «Jev cree que ya está» no es «cumplido»

| | |
|---|---|
| **Promesa** | 386 |
| **Toca** | `Mcp/SurfaceMapTools.cs:299` (solo por número), `Navigation/ElTramo.cs:154-161` (el motivo, con el porqué detrás), `Decision/ElDecisor.cs:213-216` (el texto), y **en el mismo commit** `Contrato.cs:12180` (el caso 1 de la 292 afirma «Jev cree que ya está») |
| **Terminado** | 386 verde; 289 intacta; 292 verde con su aserción nueva |
| **Sitios** | 1 decisión por subcadena (`:299`); 1 motivo del tramo; 1 mensaje del decisor; 1 aserción ajena |

### Fase 9 — la señal que se registra y no decide

| | |
|---|---|
| **Promesa** | 387 |
| **Toca** | `Decision/ElDecisor.cs` (`N`, `Masa5`, `InputTokens` al parsear), `Mcp/SurfaceMapTools.cs:293-295, 340-343` (una composición para la línea y la cuenta) |
| **Terminado** | 387 verde; 345–350, 386 y 388 intactas; `contrato-del-grafo.ps1` → **CONTRATO INTACTO** |
| **Sitios** | 1 lectura de `usage` (hoy 0); 1 composición para 2 salidas |

**¿Núcleo congelado?** No: nada de `nucleo/`. `PuertasPeligrosas`, `Nombres` y `SurfacePlace` no cambian.

## Lo que NO entra

| Qué | Por qué |
|---|---|
| **Ningún clic físico cae en una ventana de Ü** (`SiCaeEnU`, arquitectura §2.8, numerada 350 allí) | el encargo de esta rama prohíbe tocar `UiaSurface`; va en una rama propia con el siguiente número libre (388+). Hasta entonces, el precedente sigue (la carita tapó lo señalado, `SurfaceMapTools.cs:1262-1272`) |
| **El tramo protegido ante callbacks rotos** (`dc58914`: `AlEmpezar`, `HayQueParar`, `AlTerminar`, 3 de 6 `Log`) | el informe lo midió como correcto pero **a medias** (3 `Log` sin proteger, `ElTramo.cs:220, 225, 238`) y dice que el sitio único es `LogBus.cs:50`, fuera de esta rama; la arquitectura §7 lo deja como rama aparte (`tramo-callbacks-seguros`) que ninguna de las cuatro toca; y esta rama no tiene número para él. Es una feature distinta: no se casa con esta (una rama, una cosa) |
| `ProbabilidadSegura` (`3215466`) | convierte en 0 y 0 abre `peligro` y `cumplido` (M) |
| El arranque de `4198517` | elimina el onboarding (M); UI de riesgo alto; otra rama |
| **Caer a la regla local (`Simulado`) en SAP con `quien == "jev"`** | era cambiar de juez, no fallar cerrado (revisión 5): `Simulado` acciona con 1,00 por una palabra y el botón diría «jev». En SAP sin habilitar, decide Luna |
| **Limpiar el objetivo de nombres de paciente** | viaja tal cual; se anota como riesgo aceptado. Un limpiador sería una red que se cree puesta; la mitigación es de Graph (cómo Luna formula el objetivo) |
| Calentar la conexión, una lectura por ciclo, la observación con versión | rama C (`ClienteTypeSafe.cs:27-43, 110-121`; esta rama solo toca `:80-84`, a más de 10 líneas) |
| La espera por huella | rama B |
| Overlay, panel, flecha | rama D; consume `QueNoCuadro`, `Alternativas`, `Masa5`, `InputTokens` |
| Usar «ninguna», la masa del top-5 o `absent` como compuerta | sin calibración: seis lecturas de un vídeo ajeno y ningún fallo confirmado. Se registran; se mira con cien pasos |
| Cuerpo sin duplicados (`state` + `criteria`) y candidatos por relevancia (mejoras 18, 19) | rozan la 285 y la 282; se miden en sombra antes (C) |
| El decisor local de verdad para SAP | la 337 del dueño deja el hueco; aquí no hay regla local en SAP: decide Luna |
| Habilitar `sapgui://` | es del dueño y del hospital, por escrito. Aquí solo se garantiza que sin eso no sale nada, y que con eso las filas tampoco |
| Refactorizar las 16 comparaciones de `sapgui://` | se nombran, no se tocan: la 349 compara por el mismo camino |
| Las líneas `mano:` con etiqueta de `GuiGridFila` en el log | anteriores a Jev; hallazgo para el dueño |
| Sombra (335 del dueño) | otra rama |

## Hallazgos

Se rellena durante la implementación. Lo encontrado al especificar, con fecha:

- **2026-09-22.** `f811796` gastó la 341 por la mañana y `5574148` (merge `db0cd41`) las 342 y 343 al
  mediodía: la spec pasa a 344–350 y 386–387. Dos veces en un día el número reservado en un documento
  de scratchpad dejó de estar libre: **reservar no es gastar**; se comprueba con `git grep` en el
  momento de escribir el registro, no antes.
- **2026-09-22, tarde.** Tercera vez: `7380fd8` (`main`, memoria de voz) gastó la **344** mientras esta rama
  ya la tenía escrita, commiteada y verde (fase 2). Al traer `main` el conflicto fue literal: dos `Prueba("344.`.
  Como `main` no se toca, la de esta spec pasa a **388** (libre en las 45 refs `origin/*`, **(M)**) y los
  commits `f1d0743`, `3317a06` la siguen citando como 344: lo que dicen esos mensajes es la promesa que hoy
  se llama 388. La propuesta para el documento del dueño (§ arriba) pasa de 388+ a 389+.
- **2026-09-22.** `arquitectura-jev-en-u.md` §2.1 exigía `confidence = probabilities[choice] ± 0,02`;
  el ejemplo documentado (0,91 / 0,93) y la definición de TypeSafe lo desmienten. No se exige.
- **2026-09-22.** La 282 asegura `criteria.Count == opciones.Length`: «ninguna» la tiene que añadir
  quien pregunta, no `CuerpoDeEleccion`, o la 282 rompe. La arquitectura decía «282 sigue verde: todas
  las ofrecidas más una» sin mirar el fixture.
- **2026-09-22.** La 292 caso 1 pasa hoy **solo** por `Porque.Contains("cumplido")`
  (`Contrato.cs:12180`: el `Cumplido` no se pone), y esa palabra sale por las dos ramas del tramo: el
  caso no distinguía nada. Quitar la subcadena sin tocar ese fixture la rompe; tocarlo solo con el
  dato la deja verde por la razón equivocada.
- **2026-09-22.** `RespuestaChoice` (`Contrato.cs:11532`) es el ayudante de 275–281: con la 388 y la
  347 tiene que mandar la clave «ninguna», y con la 345 **las dos nouls**, o 278 y 279 caen (afirman
  `Actuar=true`). La primera versión de esta spec vio la clave y no las nouls.
- **2026-09-22.** `GetMethod("Elegir")` sin tipos, 6 sitios: una sobrecarga de `Elegir` habría puesto
  rojas 275–281 y 289 de golpe. La rama del dueño lo esquivó con `ElegirConModelo` sin decirlo.
- **2026-09-22.** `sondas/DelDecisor/Programa.cs` llama a `Elegir` con el modelo fijo en 4 líneas: la
  sonda que mide latencia mide otro modelo del configurado.
- **2026-09-22.** El dominio del portal clínico no está escrito en este repo (CLAUDE.md nombra el repo
  `Pagina-web-clientes-final` y la ruta `/api/agent/values`, no el host). `VetadosPorDefecto` nace con
  los dominios de Miracle que sí aparecen (`itsmiracleai.com`, `itsmiracleai.com.co`, **D**); el host
  exacto se le pide al dueño en el PR y entra como constante, no como variable.
- **2026-09-22, fase 0.** `Utf8JsonWriter` escapa lo no ASCII por defecto («·» → `·`, «í» →
  `í`): una aserción «el cuerpo no contiene «fila de prueba · 000»» o «…«Historia clínica»» sobre el
  JSON crudo habría salido verde **con la fuga presente** (patrón nº7: un criterio que no puede fallar con
  el bug no es un criterio). Los jueces de la 349 y la 350 miran el cuerpo **decodificado**
  (`TextoDelCuerpo`: `state`, `instructions` y las claves de `criteria`).
- **2026-09-22, fase 0.** El primer juez de la 388 listaba 1,0000001 entre lo que no cuadra; la tabla de
  diseño lo lee como 1 (tolerancia ≤ 1+1e-6). Manda la tabla: 1,0000001 es el caso de contraste que
  acciona, y el «fuera de rango por arriba» se juzga con 1,01. Lo mismo vale para `peligro` 1,0000001 en
  la 345: se lee como 1, que cierra la compuerta.
- **2026-09-22, fase 0.** El veredicto del arnés cuenta **aserciones** (`_fallos` sube en cada `Debe`),
  no promesas: «23 promesa(s) incumplida(s)» son 10 promesas. Es así desde el día que nació el contrato
  y no se toca aquí; se deja dicho para que nadie lea 23 como «se rompieron trece de más».

## Revisiones

2026-09-22, siete refutaciones sobre la primera versión. Cada una se comprobó antes de aplicarla; lo
medido está en la tabla del diagnóstico. **Ninguna resultó falsa.**

| # | Decía la spec | Refutación | Comprobado | Qué cambió |
|---|---|---|---|---|
| 1 | la larga era una sobrecarga `Elegir(…, modelo, politica)` y «275–281 y 290 intactas» en la fase 4 | `GetMethod("Elegir")` sin tipos lanza `AmbiguousMatchException` con dos métodos del mismo nombre | **(M)** sonda en PowerShell; 6 sitios en el contrato; la excepción cae en `Prueba` y cuenta como fallo; la rama del dueño usa `ElegirConModelo` | la larga se llama `ElegirConModelo`; la 348 lo dice en su enunciado («el ÚNICO método con ese nombre»); fase 4 y fixtures ajenos |
| 2 | el sabotaje de la 388 era «tolerancia 0,02 → 10 (el caso Σ=0,95 acciona)» y el caso se construía «solo con la elegida» | ese caso rompe dos reglas (suma y claves exactas); con la tolerancia saboteada la clave que falta sigue rechazando: el sabotaje no se aplica y el contrato queda INTACTO —el sabotaje fantasma del 2026-08-21— | **(M)** el único Σ=0,95 del contrato es `Respuesta()` de la 289 con una sola clave (`:12037-12045`) | el caso de suma lleva **todas** las claves en rango (0,85 · 0,10 · 0,00); `Validar` reporta **cada** violación y cada caso afirma su token; la 388 lo dice en su enunciado |
| 3 | las nouls se validaban en `Validar` (fase 2) y la fase 2 terminaba con «289 intacta» mientras la fase 3 decía «289 pasa a verde» | las dos frases no pueden ser verdad a la vez; y si `Validar` rechaza la ausencia, `Noul` nunca corre con una ausente y su sabotaje queda verde | **(M)** líneas 135-136, 184, 113, 301, 308-310 de la primera versión | la ausencia vive en `Noul` → `double?` (fase 3); **289 roja de la fase 0 a la 3**, dicho en cada fase; sabotaje de la 345 = el caso peor vuelve al crudo (solo la 345); el de `Noul` es el de la 289 y pone rojas 289 y 345, declarado |
| 4 | el tramo paraba con «Jev cree que ya está ({c})…» sin el porqué, la 386 decía «nunca que el objetivo está cumplido», y «292 intacta» | el caso 1 de la 292 juzga la **palabra** (`c1.Contains("cumplido")`), que solo puede venir del motivo; el motivo nuevo la borra y la 292 se pone roja; y el «nunca» choca con el propio mensaje del decisor | **(M)** `Contrato.cs:12178-12180`; `ElTramo.cs:157-159, 186-188, 210` | el motivo lleva el porqué detrás («Jev cree que ya está: {p.Porque}»); la 386 promete que el **tramo no declara éxito**, sin el «nunca»; el caso 1 de la 292 pasa a afirmar la rama (fase 8, fixtures ajenos) |
| 5 | en `sapgui://` sin habilitar «decide la regla local diciendo por qué», y eso era fallar cerrado | es cambiar de juez: `Simulado` acciona con 1,00 por una palabra (y ya accionó «Detalles» sobre el Explorador), mientras el interruptor dice «encendido: jev» | **(M)** `ElDecisor.cs:251-259, 283-297`; `InterruptorDelDecisor.cs:70` | `No("sapgui:// sin U_DECISOR_SAP_TEXTO: no se manda texto a Jev y no decido por regla local. Decide Luna.")`; `Simulado` solo con `quien == "simulado"`; desviación declarada frente a la arquitectura §1/§2.6; la 349 lo dice en su enunciado |
| 6 | la política miraba solo el prefijo de la ubicación; «SAP no manda, sin vetos» por defecto; el portal se vetaba solo si el dueño rellenaba una variable | el cuerpo lleva la ubicación entera (en `uia://` el pathname es el título vivo), el objetivo, y las etiquetas de cualquier app; un veto por defecto vacío no es fallar cerrado | **(M)** `PeticionASystemOne.cs:130-131, 140`; `UiaSurface.cs:245`; `SurfacePlace.cs:21`. Que lleven nombres es **(D)** | de la ubicación viaja solo el **origin** (`SurfacePlace.OriginOf`); `VetadosPorDefecto` es una constante no vacía; el objetivo se anota como riesgo aceptado; la tabla dice que en `uia://`/`web://` las etiquetas **sí salen** para que el dueño lo acepte por escrito; la 349 lo dice en su enunciado |
| 7 | la 350 limpiaba la línea `decisor:` y solo reconocía las `mano:` de `map_take` como hallazgo | la etiqueta elegida sale igual por el relato de `map_decidir` y por la línea `paso k:` del tramo (log y notch), exigidas por 287 y 294 | **(M)** `SurfaceMapTools.cs:334-340`; `ElTramo.cs:203-208`; `Contrato.cs:782, :789` | la 350 cubre las tres líneas: una fila se nombra «fila N (Tipo)» en el relato y en el `Paso` del tramo, desde **1 sitio** (`UnPasoDecidido`); 287 y 294 en fixtures ajenos (sin filas: intactas) |
| — | promesas 342–350 | (no era una refutación) el merge de `origin/main` gastó 342 y 343 | **(M)** `git grep` sobre 45 refs | 345–350 y 386–388 |
| — | `RespuestaChoice` añadía «ninguna» | (encontrado al comprobar la 3) 278 y 279 afirman `Actuar=true` con ese ayudante, que no manda nouls | **(M)** `Contrato.cs:11532-11540, :11642, :11661` | el ayudante añade también las dos nouls a 0,1 |

## Nivel 4, pendiente (no lo corre esta sesión: no se ejecuta U.exe ni se llama a TypeSafe)

1. **Con transporte falso, sin red**: la sonda `sondas/DelDecisor` con los valores del informe
   (1,2 · 80 · 1e400 · 1,0000001 · 1,5 · −0,01), Σ = 0,95 con todas las claves, empate, clave de más y
   de menos: cada uno `Actuar=false` con su `QueNoCuadro` completo. Salida pegada en el PR.
2. **En el PC, `U_DECISOR=simulado`, dos pantallas con nombre**: Explorador (la barra trae «Eliminar»)
   con `map_decidir objetivo=eliminar el archivo` → veto con «no se puede deshacer» y 0 pulsos; y
   Configuración con un objetivo que la regla local sí casa → línea `decisor:` con «viajan N de M · 0
   filas sin texto · C caracteres · masa5 · tokens sin medir». Log pegado con horas.
3. **En el PC, `U_DECISOR=jev` sin `U_DECISOR_SAP_TEXTO`, delante de SAP (QAS)**: `map_decidir` → 0
   llamadas en el log, «Decide Luna» en la cuenta, y el interruptor sigue diciendo «jev» — es el caso
   que la primera versión de esta spec habría dejado accionar por la regla local.
4. **Con Jev real, solo con permiso del dueño (cuesta dinero)**: 3 decisiones sobre el Explorador
   (nunca sobre SAP), para contestar lo que sigue sin medir:
   - si TypeSafe manda alguna vez nouls o `confidence` fuera de [0,1] (registrando números y claves,
     nunca etiquetas);
   - si la respuesta trae **todas** las claves con 60+ puertas y si Σ cuadra en ±0,02 (si no, la
     tolerancia se cambia **por spec**, no en silencio);
   - qué probabilidad recibe «0) ninguna» cuando la puerta buena está a la vista;
   - `usage.input_tokens` frente a los 875/1.913/4.635 del 18-09;
   - que en el cuerpo que salió («decisor:» con `Caracteres`) `Pantalla actual:` lleve solo el origin.
5. **El botón**: `U_TYPESAFE_MODELO=jev-1.13.0` y el estado del interruptor nombrando ese modelo, con
   una decisión cuyo `decisor:` lo confirme (con permiso, cuenta como una de las 3).

## Cierre

- [ ] Todas las promesas verdes (`.\scripts\contrato-del-grafo.ps1` → CONTRATO INTACTO)
- [ ] Un sabotaje por promesa, verificado por diff, con el veredicto literal pegado aquí
- [ ] `.\scripts\verificar.ps1` pasa, con evidencia en `out\evidencia.md`
- [ ] Probado en ≥2 pantallas, con nombre: …
- [ ] Estado de este documento: **implementado** (AAAA-MM-DD)
