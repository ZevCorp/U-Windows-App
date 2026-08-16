# Plan de implementación: de la plata declarada a la plata derivada

Estado: **propuesto** · Nace del diagnóstico del 2026-08-10 · Rama de arranque: `improve-plata-section`

## Por qué este plan es dirigido por especificación

No por metodología: porque el repo ya lo hace y tiene el arnés montado.
[`tests/ContratoDelGrafo/Contrato.cs`](../tests/ContratoDelGrafo/Contrato.cs) no es «un proyecto de
tests», es **la definición ejecutable de lo que el núcleo promete**, se corre con
`.\scripts\contrato-del-grafo.ps1` en medio minuto y ya vigila diez promesas en la nube sin tocar la
pantalla.

Y hay una razón concreta para escribir la spec primero **en este trabajo en particular**: lo que
estamos arreglando es exactamente un sistema que se daba por bueno a sí mismo. La plata declarada
sube su métrica escribiendo; el criterio de terminado del arquitecto se satisface declarando; y el
CI local mide `Declarados` como mínimo exigible ([`EscenarioCi.cs`](../windows-client/src/Navigation/EscenarioCi.cs)),
así que la vara de medir también premia declarar. Si primero escribimos el código y después la
prueba, la prueba se escribirá para que pase — que es el mismo vicio con otro nombre.

**La regla del flujo, y no tiene excepciones:** ninguna línea de producción entra antes que la
promesa que la juzga. Cada fase empieza con el contrato ROTO y termina con el contrato INTACTO.

## La especificación

Siete promesas nuevas, numeradas a continuación de las diez que ya existen. El enunciado es el que
irá literalmente en `Contrato.cs`, porque el enunciado ES la spec.

| # | Promesa | Fase que la pone verde |
| --- | --- | --- |
| 11 | la misma entrada da la misma plata | ninguna: es la promesa ancla, ya se cumple |
| 12 | el orden del paseo no cambia la estructura derivada | ninguna: ya se cumple |
| 13 | el bronce no se entera de lo declarado | 1 — necesita `Bronce.De` |
| 14 | lo que dijo una persona manda, y queda anotado como desacuerdo | 2 — hoy se contrasta pero no se aplica |
| 15 | lo que dijo un modelo no mueve la derivación | ya se cumple; se congela |
| 16 | sin cromo derivado no hay atajo: quitar la plata rompe una ruta | 2 — `SelectoresCromo()` solo lee lo declarado |
| 17 | una app sin raíz observada no sitúa nada, y lo dice | ya se cumple; se congela para que nadie meta un respaldo que adivine |
| 18 | el archivo del bronce no contiene plata | 3 — es la mitad en disco de la 13 |
| 19 | una sección alcanzada solo por el mobiliario sigue teniendo hijos | 1 |

La 16 es la que de verdad cierra el asunto. Mientras no exista, plata es cosmética por definición:
se puede borrar entera y no se rompe nada. La 13 y la 18 son las dos mitades de un bronce estable
—el lector y el disco— y se separaron al escribirlas: son dos fases distintas y meterlas en una
promesa habría dado un rojo que no dice cuál de las dos falta.

**La 19 no estaba en este plan: la encontró la propia spec.** Una sección a la que solo se llega por
el panel lateral se colocaba en el primer nivel y ahí se detenía el recorrido, así que todo lo que
cuelga de ella quedaba sin situar — en un explorador de archivos, la app entera. Es el argumento
completo a favor de escribir la spec antes que el código: el bug estaba en el derivador desde el
primer día y ninguna lectura del código lo había visto.

### El fixture: un bronce grabado

Las promesas 11, 12 y 16 no se pueden escribir con el arnés actual: cada prueba construye el mapa a
mano y espera `Dwell = 1450 ms` por transición, lo cual sirve para diez promesas y no para derivar
sobre decenas de pantallas.

Hace falta **un `surface-map.json` congelado en el repo**, en `tests/ContratoDelGrafo/bronce/`,
capturado de un mapeo real de `explorer.exe`. No contradice la regla de `EscenarioCi` —que guarda
los escenarios fuera del repo por ser la vara de ESTA máquina—: aquello mide resultado sobre el
terreno vivo, esto es una **entrada congelada** que tiene que dar el mismo resultado en cualquier
máquina y para siempre. Son dos cosas distintas y por eso viven en sitios distintos.

Se captura una vez, se le quita todo lo declarado antes de guardarlo (si no, el fixture nace
contaminado con lo que estamos quitando) y no se vuelve a tocar salvo para añadir casos.

### Cómo se pide una capacidad que aún no existe

Las promesas nuevas llaman a APIs que se van escribiendo por fases. El repo ya resolvió esto: las
capacidades nuevas **se piden por nombre** (reflexión), de modo que un núcleo que no las tiene se
degrada a «no aplicable» en vez de no compilar — es lo que permitió que el contrato siguiera
juzgando a la v0 cuando nació `OlvidarApp`.

Aquí se usa igual, con un matiz que importa: «no aplicable» vale para juzgar **núcleos viejos**. En
la rama donde se está implementando la fase, la promesa tiene que dar **ROJO**, no «no aplicable»,
o el flujo pierde su único mecanismo de honestidad.

---

## Las fases

Seis ramas cortas, no una larga. Cada una es una feature completa, mergeable sola y con el contrato
verde. Ninguna dura más de tres días, que es lo que exige `CLAUDE.md`.

### Fase 0 · La spec, en rojo — **HECHA** (sin verificar: aquí no hay compilador)

**Entró:** las promesas 11-19 en `Contrato.cs`, el bronce de prueba (`MontarBronce`), el contador de
PENDIENTES y `Plata.Huella` — la cadena canónica sin la cual «la misma entrada da la misma plata» no
se puede escribir.
**No entró:** ni una línea que hiciera pasar una promesa.
**Terminado cuando:** `.\scripts\contrato-del-grafo.ps1` dice `CONTRATO ROTO` con las incumplidas
esperadas (13, 14, 16, 18, 19). Si falla alguna de las otras, hay algo que no sabíamos y se
investiga antes de seguir.

El entregable de esta fase es el rojo. Un rojo con los números correctos vale más que el código de
la fase siguiente, porque es la primera vez que este subsistema puede fallar de forma visible.

### Fase 1 · Bronce con lector propio — **HECHA** (sin verificar)

**Cubre:** promesas 13 y 19.
**Entró:** [`Bronce.cs`](../windows-client/src/Navigation/Bronce.cs) — pantallas, puertas,
distancias y huella, **ignorando por construcción** todo campo declarado; la vista BRONCE dejó de
calcularse en el pintor y lee de ahí; y el arreglo de la promesa 19 en `Plata.Derivar` (el recorrido
continúa desde las secciones alcanzadas solo por mobiliario).

**Efecto lateral que hay que mirar en la primera corrida:** la vista BRONCE cambia de aspecto, y a
mejor. Partía su recorrido del nodo sintético `nivel://<app>`, que no tiene aristas en el mapa, así
que con una app seleccionada **todo caía en la fila 1**. Ahora muestra la topología real medida
desde la raíz observada.

**Deuda declarada, no olvidada:** `Bronce.De` lee `NodeInfo.Nivel == 0` para saber cuál es la raíz.
Es la única lectura de un campo compartido con la plata, y desaparece en la fase 3, cuando bronce
tenga su propio `EsRaiz` en disco. Está escrito en el propio archivo para que nadie lo tome por
descuido.

### Fase 2 · Plata, única fuente de cromo y profundidad — **ESCRITA, sin verificar**

**Cubre:** promesas 14 y 16.
**Entró:**

- `CromoDe` y `SelectoresCromo()` añaden el cromo **derivado** después de lo declarado, sin pisarlo.
  Esto es lo que hace que borrar la plata rompa algo: sin ella se pierden los atajos y `Route`
  vuelve a decir «no sé llegar» a un sitio que está a un clic.
- `Plata.Derivar` aplica el override de una **persona** sobre la clase, y sigue anotando el
  desacuerdo contra lo que dijo el cálculo (si se comparara después del override, una corrección
  humana nunca aparecería como desacuerdo). Lo de un modelo se contrasta y no manda.
- Las pantallas que situó una persona se siembran con su nivel en el recorrido, y desde ellas se
  sigue bajando — sin eso, proyectar la derivación habría movido lo fijado a mano y roto la
  promesa 3, que lleva desde el principio impidiéndolo.
- `RecalcularProfundidades` **ya no calcula: proyecta**. Se acabaron los dos recorridos.
- `Plata.DerivadaDe` sirve la derivación una vez por versión del mapa, con la caché **colgada del
  mapa** (`ConditionalWeakTable`) y no de la clase: estática confundía dos mapas vivos con la misma
  versión y el mismo nombre de app, que es justo lo que hace el contrato al crear uno por promesa.
- `ObserveExits` mueve `Version` cuando nacen puertas nuevas — solo entonces. Ver puertas es
  aprender, y hay cachés colgadas de esa versión; pero subirla en cada refresco convertiría cada
  tick en una derivación.

**Cuidado explícito al correrlo:** la promesa 7 («el cromo es propiedad de cualquier nivel y se
alcanza desde cualquier pantalla») es la red de esta fase. Si se pone roja, el cromo derivado no
está reproduciendo lo que el declarado hacía — eso hay que entenderlo, no ajustar el umbral hasta
que pase.

**Terminado cuando:** contrato intacto **y** una app real mapeada de punta a punta sin declarar
nada a mano. Ninguna de las dos se ha hecho todavía.

### Fase 3 · Separar la persistencia — **ESCRITA, sin verificar**

**Cubre:** promesa 18.
**Entró:**

- El terreno se **escribe** como `EdgeBronce` —sin `NivelNav`, `NivelFijado`, `PorPersona`,
  `EsCromo` ni `KindDeclarado`— y se **sigue leyendo** como `EdgeInfo` entero. Esa asimetría es lo
  que permite migrar sin perder nada.
- `Ensenanza` gana `Kind`, y `map_set_kind` lo guarda ahí: la clasificación vivía solo en la arista,
  así que borrar el grafo deshacía el trabajo — el arquitecto marcaba cuarenta acciones y la corrida
  siguiente se las encontraba pendientes. Ahora sobrevive, como los niveles.
- `NodeInfo.EsRaiz`: la marca de «aquí se entró» tiene campo propio y deja de viajar disfrazada de
  `Nivel == 0`, que es plata y se recalcula. Salda la deuda que `Bronce.cs` declaró al nacer.
- Una **cosecha** en `Load`: lo declarado que venga dentro de un terreno antiguo pasa a la capa de
  overrides antes de perderse, y el primer guardado deja el archivo limpio.

**Lo que el plan decía y no se hizo, con su razón:** no se sube `SchemaVersion` a 7. Al leer el
código quedó claro que la migración por versión de este archivo **purga las acciones de todas las
aristas** —es su naturaleza desde la v2— y aquí no hay nada que purgar. Habría destruido el trabajo
de todos para arreglar una mezcla de campos. La cosecha hace la migración sin romper nada.

**Lo que queda de esta fase:** `NodeInfo.Nivel` se sigue guardando. Es plata, pero es la caché que
permite que el grafo arranque situado en vez de plano hasta la primera navegación. Queda dicho aquí
en vez de disimulado: es la última mezcla que queda en el archivo.

**Riesgo:** alto, toca la persistencia de todos. La promesa 8 («guardar y cargar no pierde nada») es
la red. **Antes de mergear esto hay que cargar un `surface-map.json` real de antes del cambio y
comprobar que los niveles siguen ahí** — es la única prueba que importa y no se puede hacer desde
macOS.

### Fase 4 · El criterio de terminado — **ESCRITA, sin verificar**

**Entró:**

- `map_silver` (`cuanto_entiende` para el agente): cobertura derivada, puertas **sin cruzar** como
  lista de trabajo, desacuerdos con quién los declaró, y los contenedores con su afordancia.
- `map_unsituated` deja de poder leerse como meta: cada respuesta suya lleva pegada la cobertura
  derivada y dice que declarar no la sube. Dos herramientas que se leen juntas no pueden dar
  impresiones contrarias — es el hallazgo nº1 de la auditoría del arquitecto, aplicado otra vez.
- La misión reescrita: plata deja de ser «cada salida en su nivel» y pasa a ser lo que el sistema
  deriva; el trabajo del agente pasa de **declarar** a **cruzar puertas y disputar el cálculo**; y
  se le dice explícitamente que declarar sin evidencia se cuenta como deuda, no como avance.
- `fijar_nivel` se describe ahora como herramienta de último recurso, no como el trabajo.
- `EscenarioCi` mide `Cobertura` junto a `Declarados`. Los escenarios ya grabados no la traen y
  siguen valiendo: sin el campo el mínimo es 0, igual que un núcleo viejo no promete lo que no
  conoce.

**Lo que NO entró:** una promesa en el contrato que ate `map_silver` y `map_unsituated` a decir lo
mismo. El contrato no instancia herramientas MCP hoy, y montar ese arnés es más trabajo que la fase
entera. Queda anotado como lo que es: un hueco de cobertura, no algo verificado.

### Fase 5 · Calibrar y limpiar `<tu-prefijo>/plata-calibrada` · 1 día

**Entra:** los tres umbrales (3 pantallas, 0,60 de permanencia, 8 hermanos) medidos contra el
fixture y contra dos apps reales — hoy están razonados, no medidos. Se borra
[`ProfundidadClasica.cs`](../windows-client/src/Ui/ProfundidadClasica.cs) si la plata derivada
reproduce lo que enseña, que es lo que pide su propio encabezado; y la vista PLATA (declarada) se
retira o se queda como «overrides», no como etapa.
**Terminado cuando:** el pipeline tiene tres etapas y no cuatro — bronce, plata, oro — porque la
plata falsa dejó de existir en vez de convivir.

---

## La puerta de cada PR

Ninguna de las seis entra sin las tres cosas:

1. `.\scripts\contrato-del-grafo.ps1` → `CONTRATO INTACTO`.
2. `.\scripts\ci-local.ps1` sin regresión sobre los escenarios grabados.
3. **En cuántas pantallas se probó, escrito en el PR.** Es el aprendizaje nº9 del repo y aquí muerde
   especialmente: una regla derivada que acierta en el explorador puede errar en una web, y el run
   de NWP1 ya demostró que una sola pantalla verificada es una apuesta.

Y el reparto: las fases 1, 2 y 3 tocan `SurfaceMap` **y** la UI. Es zona de choque alto según el
propio reparto por superficie, así que no deberían solaparse con otra feature abierta en la UI.

## Lo que este plan no hace

**Oro.** Queda fuera a propósito. Oro es plata + prueba de ejecución —cada arista estructural
verificada llegando de A a B sin ayuda, los contenedores con su afordancia probada, el contenido
promovido por uso real— y meterlo aquí convertiría seis ramas cortas en una negociación.

**Derivar `Accion`.** No se puede hoy y no es un hueco de plata: `LearnTraversal` solo acuña aristas
cuando el destino es otro nodo, así que un clic que no movió la pantalla no deja rastro en bronce.
Sin ese rastro no hay evidencia de «hace algo y no lleva a ninguna parte». Anotarlo en bronce es
trabajo de otra rama, y hasta entonces `Accion` solo llega declarada — dicho en el código en vez de
tapado con heurística de nombres.
