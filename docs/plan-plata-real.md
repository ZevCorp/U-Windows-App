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

### Fase 2 · Plata, única fuente de cromo y profundidad `<tu-prefijo>/plata-manda-en-la-estructura` · 1–2 días

**Cubre:** promesas 12, 16.
**Entra:** `SelectoresCromo()` pasa a consultar el cromo derivado; `RecalcularProfundidades` deja de
tener cálculo propio y se convierte en la proyección de `Plata.Derivar` sobre `NodeInfo.Nivel`.
**Por qué es la fase peligrosa:** ahora mismo hay **dos cálculos** respondiendo la misma pregunta con
entradas distintas, y este repo ya pagó rondas por eso — está escrito en el propio pintor («*el
dibujo no opina: una sola fuente*»). La convivencia es deliberada y temporal; esta fase la termina.
**Cuidado explícito:** la promesa 7 que ya existe («el cromo es propiedad de cualquier nivel y se
alcanza desde cualquier pantalla») es la red. Si se pone roja, el cromo derivado no está
reproduciendo lo que el declarado hacía, y eso hay que entenderlo antes de seguir, no ajustar el
umbral hasta que pase.
**Terminado cuando:** contrato intacto **y** una app real mapeada de punta a punta sin declarar
nada a mano.

### Fase 3 · Separar la persistencia `<tu-prefijo>/bronce-sin-plata-dentro` · 2–3 días

**Cubre:** promesa 13.
**Entra:** los campos declarados salen de `EdgeInfo` a la capa de overrides —que **ya existe**:
`jerarquias-ensenadas.json` con `Ensenanza(Nivel, Humano, Atras, Cromo, Selector)`, y sobrevive al
borrado del grafo—; `SchemaVersion` sube a 7 con su migración, exactamente como se hizo en la 6.
**Trocear si se alarga:** 3a redirige los cuatro escritores
([`map_set_level`](../windows-client/src/Mcp/SurfaceMapTools.cs#L691),
[`MaestroDeApps:297`](../windows-client/src/Navigation/MaestroDeApps.cs#L297),
[`JerarquiaWeb:60`](../windows-client/src/Navigation/JerarquiaWeb.cs#L60), y la deducción de
[`ObserveExits:645`](../windows-client/src/Navigation/SurfaceMap.cs#L645)); 3b migra el esquema.
Son dos PRs, no dos commits: si 3b se tuerce, 3a ya está dentro.
**Riesgo:** alto — toca la persistencia de todos. La promesa 8 que ya existe («guardar y cargar no
pierde nada») es la red, y la migración se prueba cargando el `surface-map.json` de una máquina real
de antes del cambio.

### Fase 4 · El criterio de terminado `<tu-prefijo>/terminado-se-mide-derivando` · 1 día

**Cubre:** promesa 15.
**Entra:** una herramienta MCP que devuelva el resumen derivado (cobertura, sin cruzar, desacuerdos,
declaradas sin evidencia); la misión de [`arquitecto.mjs:172-181`](../agente-arquitecto/arquitecto.mjs#L172)
reescrita —hoy define plata como «cada salida en su nivel» y su terminado como «`sin_situar` vacío»,
que es la definición que produce plata falsa—; y `EscenarioCi.Escenario` gana `Cobertura` junto a
`Declarados`.
**Urgencia por calendario, no por arquitectura:** mientras la misión diga lo que dice, la próxima
corrida nocturna vuelve a producir plata declarada aunque el derivador ya funcione. La parte de la
misión se puede adelantar a la fase 1 si hay una corrida programada antes.

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
