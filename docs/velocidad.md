# La velocidad del mapeador

El único sitio donde se responde «¿por qué va lento?». Si aparece esta pregunta en otro archivo,
sobra ahí.

## 1. Qué medimos, y por qué esas tres cosas

El mapeador hace tres trabajos, y cada uno se cronometra **dentro de quien lo hace**
(`PulsoDelMapeador.Costo`), nunca desde fuera. Un cronómetro externo mide también su propio coste,
y eso ya nos engañó dos veces (§5).

| trabajo | qué es | cadencia |
|---|---|---|
| **localizar** | saber en qué ventana estoy | 250 ms |
| **leer la pantalla** | sacar los elementos con UIA | 900 ms |
| **proyectar** | escribir en Neo4j lo que cambió | con cada lectura |

Se ven en el panel del mapeador (pestaña **Mapeador** del visor, `http://127.0.0.1:8792` →
`/mapeador`). El panel **no calcula nada**: pinta estos contadores tal cual. Si dedujera algo por su
cuenta tendríamos dos opiniones sobre el mismo hecho, y esa es la avería que llevamos toda la semana
pagando.

## 2. El veredicto: cómo se ve un sistema sano

Medido el **2026-08-13**, con Neo4j conteniendo **61 ubicaciones y 7.692 elementos**, y el navegador
delante (~1.700 elementos por pantalla, que es el caso peor real):

```
localizar          media   46 ms   peor   242 ms
leer la pantalla   media  950 ms   peor 4.376 ms
proyectar          media   93 ms   peor   377 ms
saturación: 44 vueltas de pantalla descartadas en 90 s
```

**Las condiciones son parte del número.** Comparar estas cifras con una toma hecha sobre el
explorador de archivos (40 elementos) no dice nada: ya lo intentamos y casi nos lleva a la conclusión
contraria. Al anotar una medida, se anota **qué app estaba delante y cuántos elementos tenía**.

## 3. Las reglas que protegen esto

Son tres, en dos contratos, y **las tres corren solas en cada build**
(`scripts/version-nucleo.ps1 -Construir`). Hasta el 2026-08-13 el build solo corría el contrato del
núcleo VIEJO y estas había que lanzarlas a mano: protegían mientras alguien se acordara. Una promesa
que nadie ejecuta no es una promesa, es una nota.

| dónde | promesa | qué impide |
|---|---|---|
| núcleo | *observar NO afirma que estemos ahí* (1, 11, 12) | el rebobinado que dejaba el mapa clavado en una app |
| núcleo | *proyectar es barato: los ids están indexados* | que `MERGE` recorra los 7.692 elementos |
| mapeador | *una vuelta a la vez* (9 promesas) | que las vueltas se apilen y todo se ralentice |

**Ninguna mide milisegundos, y es deliberado.** Un umbral daría rojos por tener el portátil ocupado,
y un juez que da rojos falsos enseña a desconfiar del juez —ya nos pasó con la comprobación de
fidelidad—. Se comprueba la **causa**: ¿está el índice?, ¿cierra el candado?, ¿quién fija la
ubicación? Todas deterministas.

El veredicto de las dos últimas se ve sin correr nada en la pestaña **Reglas** del visor
(`http://127.0.0.1:8792/visor`), con su hora.

A mano, si hace falta:

```bash
dotnet run --project nucleo/Contrato/Contrato.csproj -c Release
```

```bash
dotnet run --project mapeador/Contrato/Contrato.csproj -c Release
```

**Las promesas de un solo hilo no bastan para un candado.** Con el `Interlocked` quitado, las cinco
primeras del mapeador siguen en verde —un solo hilo nunca ve la carrera— y solo la sexta, la de ocho
hilos, lo caza. Sin ella el contrato certificaría un candado que no cierra.

## 4. Plan de acción cuando vaya lento

En este orden. Cada paso descarta una causa que ya nos ha mordido de verdad.

**0. Mirar el panel antes de tocar nada.** No se arregla lo que no se ha medido. El panel dice cuál
de los tres trabajos es el caro; sin eso se arregla el equivocado. Las dos únicas veces que fuimos
rápido al código, arreglamos lo que no era.

**1. ¿Hay saturación?** Si `descartadas` sube sin parar, algo tarda más que su propia cadencia. Las
vueltas descartadas no rompen nada —se tiran a propósito, encolarlas solo pintaría fotos caducadas—
pero delatan que el intervalo miente sobre el ritmo real.

**2. ¿Proyectar cuesta más de ~300 ms?** Casi seguro son los índices. Correr el contrato (§3). Fue
esto el 2026-08-13: sin índice, `MERGE (e:Elemento {id:…})` recorre los 7.692 elementos para saber
si ese ya existe; con ~1.700 MERGE por pasada eso son trece millones de comparaciones. Medido en la
misma base con las mismas 1.740 filas, cinco pasadas cada uno:

```
sin índice   18.121 · 214 · 21.154 · 9.661 · 9.501 ms
con índice      367 · 146 ·    114 ·   137 ·   113 ms
```

Lo importante no es la media: es que **sin índice el coste salta entre 0,2 y 21 segundos** según lo
que hubiera en caché. Un coste que no se puede presupuestar es peor que un coste alto. Como el
cliente HTTP corta a los 5 s, la mayoría de escrituras se perdían: Neo4j enseñaba lo de antes y el
panel contaba 224 vueltas tiradas.

**3. ¿Se solapan las vueltas?** Un `Timer` de .NET encola la siguiente aunque la anterior siga
corriendo. Cada vuelta hay que protegerla con su `Interlocked` y descartar la que llega tarde. Fue
esto el 2026-08-12: el intervalo bajó a 120 ms, las llamadas se apilaron y el mapeo pasó de 0,4 s a
2,2 s.

**4. ¿Cabe la vuelta en su intervalo?** Si `leer la pantalla` cuesta 950 ms y el latido es de 900 ms,
casi la mitad de los latidos se tiran. Funciona, pero el intervalo está mintiendo: o se acelera la
lectura o se sube el intervalo hasta lo que de verdad se puede sostener.

**5. ¿Está el embudo tragando de más?** `leidos` vs `entregados` en el panel. Cada elemento de más se
paga en cada MERGE de cada pasada.

## 5. Las dos trampas del instrumento

Las dos nos hicieron concluir lo contrario de la verdad, y las dos fueron el mismo error: **medir el
instrumento en vez de la cosa.**

- **`docker exec` cuesta 1.708 ms; la misma consulta por HTTP, 78 ms.** Medir Neo4j lanzando un
  proceso nuevo mide el arranque del proceso. Estuvimos a punto de tirar un arreglo que sí servía.
- **Comparar Gmail (924 elementos) con el explorador (40).** No hay conclusión posible ahí. Toda
  medida se anota con sus condiciones (§2).

Y de ahí la regla: **los contadores viven donde ocurre el trabajo**, no en un observador aparte.

## 6. Lo que sigue abierto

- `leer la pantalla` (950 ms de media) es ahora el gasto dominante, y va justo por encima de su
  intervalo de 900 ms. Es el siguiente sitio donde mirar.
- Las comprobaciones de **fidelidad** e **ida y vuelta** del contrato necesitan Neo4j en exclusiva, y
  desde que Neo4j es también la memoria del núcleo casi siempre hay datos de sesiones anteriores: se
  saltan con ⚪ en vez de correr. Una promesa que nunca se ejecuta no es una promesa. Lo que
  corresponde es darle al contrato su propia base, no vaciar la de trabajo.
