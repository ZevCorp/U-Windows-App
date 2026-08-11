# Plata: qué es, qué NO es, y cómo se comprueba

> Este documento es **el único sitio** donde se define qué significa que el grafo de una
> aplicación está en plata. Si otro archivo lo explica también, uno de los dos está mintiendo:
> enlaza aquí en vez de repetirlo.
>
> Escrito el 2026-08-10, después de alcanzar una **plata falsa** y aprender a distinguirla.

---

## 1. Las tres capas

El mapa de una aplicación pasa por tres estados. No son fases de un plan: son tres cosas
distintas, y confundirlas es la forma más común de creer que se ha avanzado.

| | Qué es | Cómo se ve |
|---|---|---|
| **Bronce** | Terreno crudo. Todo lo que se ha visto, tal como se vio. | Un abanico plano: cientos de salidas colgando del inicio, sin jerarquía. |
| **Plata** | Estructura con procedencia. Se sabe qué es cada cosa y **quién lo dijo**. | Un árbol: cromo arriba, secciones debajo, contenido fuera de la estructura. |
| **Oro** | Rutas y workflows. Secuencias que se graban y se ejecutan. | «Llévame de A a B» funciona sin que nadie mire. |

**El dibujo solo pinta plata.** Bronce y oro tienen sus propias vistas, pero la vista por defecto
es la plata, y debe ser 100 % fiel al grafo — sin opiniones paralelas.

---

## 2. Qué es plata REAL

Plata **no** es un porcentaje ni una lista vacía. Es esto, y las cinco cosas tienen que darse a
la vez:

### 2.1 Todo nivel del grafo lo declaró alguien

Ni un solo nivel deducido del paseo. Si el grafo dice que algo es de nivel 2, existe una persona
o una regla de página (un landmark) detrás. Lo que nadie ha dicho vale **−1**, y decir «no sé»
es más barato que decir un número inventado.

*Se comprueba*: toda arista con `NivelNav >= 0` tiene `NivelFijado = true`.

### 2.2 La estructura no depende del paseo

Recorrer la app en otro orden produce el mismo grafo. Ver una puerta desde más adentro no la
mueve de nivel. Dos personas que exploren distinto llegan a lo mismo.

*Se comprueba*: promesa 3 del contrato, y repetir una corrida cambiando el orden.

### 2.3 El criterio de terminado converge

La lista de pendientes **baja** al trabajar. Cada decisión se toma una vez y vale para toda la
app; entrar en una pantalla nueva no reintroduce trabajo ya hecho.

*Se comprueba*: `sin_situar` es monótona decreciente durante una corrida, y clasificar un
control una vez basta aunque aparezca en cincuenta pantallas.

### 2.4 Las promesas del grafo son ciertas

Si el grafo dice «cromo», se llega desde cualquier sitio de su dominio con un clic. Si dice
«navegación», lleva a una pantalla. Si dice «acción», no navega pero se puede ejecutar. Si dice
«no sé llegar», es que de verdad no sabe — y eso es correcto, no un fallo.

*Se comprueba*: promesas 7 y 9 del contrato, y cruzar a ciegas lo que el grafo ofrece como
camino sin salir nunca de la app.

### 2.5 El mapa no se sale de la app

Ninguna puerta del mapa de una app termina con el foco en otra. Ni arista ni nodo.

*Se comprueba*: promesa 6 del contrato, ampliada a nodos (**pendiente**, ver §5).

---

## 3. La plata FALSA a la que llegamos, y cómo se reconoce

El 2026-08-10 el arquitecto cerró una auditoría de explorer.exe con este veredicto:

> «BRONCE → PLATA alcanzado en lo que depende del arquitecto.»

Y era falso. La lista de pendientes había pasado de 21 a 3, los niveles estaban puestos, el
cromo marcado. **Pero se había llegado a mano y por fuerza bruta.** Estas son las cuatro formas
de plata falsa que encontramos, cada una con su síntoma:

### 3.1 Plata por fuerza bruta

La lista se vació porque alguien repitió la misma decisión una vez por pantalla. Marcó «Nuevo»
como acción **ocho veces**. Al entrar en OneDrive le reaparecieron 27 controles ya clasificados.

*Cómo se reconoce*: la lista baja, pero el coste crece con el número de pantallas. En explorer
el número de pantallas es el número de carpetas del disco — la lista era inalcanzable por
construcción, y solo pareció alcanzable porque se exploraron siete pantallas y no siete mil.

*Regla*: si vaciar la lista cuesta O(controles × pantallas), no es plata. Tiene que ser
O(controles).

### 3.2 Plata que se inventó los números

El grafo tenía niveles en casi todo. Ninguno lo había dicho nadie: una puerta nueva nacía en
«nivel de la pantalla que la revela + 1», y ese número se congelaba para siempre.

*Cómo se reconoce*: un elemento que está en todas las pantallas —un botón de scrollbar— aparece
en «nivel 4». No describe la app: describe por dónde pasaste.

*El daño real*: un **archivo** nació en «nivel 5», se cruzó confiando en esa etiqueta, y abrió
el Bloc de notas. Un número inventado no es solo ruido; es una instrucción falsa.

### 3.3 Plata que midió con una regla rota

`sin_situar` —el criterio de terminado— respondió «está ENTERA situada: eso es la meta» con el
grafo en bronce puro, cero niveles declarados. Un agente que la obedezca cierra la auditoría en
el primer minuto sin haber ordenado nada.

*Cómo se reconoce*: «no queda nada pendiente» y «no sé nada de esta app» dan la misma respuesta.
Vacío y completo no pueden decirse igual.

*Regla*: antes de creerse una medida, comprobar que sabe distinguir sus dos extremos.

### 3.4 Plata plana

El mapa se ordenó bien… en dos niveles, porque las carpetas estaban clasificadas como contenido
y no se ofrecían como camino. En un explorador de archivos **la carpeta es la navegación**: es
la única fuente de niveles 3, 4, 5. El mapa era plano por construcción y parecía limpio.

*Cómo se reconoce*: la cadena más larga de aristas que no son cromo tiene longitud 2.

---

## 4. Lo que se hizo para salir de ahí

Cuatro cambios, cada uno con su medición antes y después.

### 4.1 El nivel deja de nacer del paseo

`SurfaceMap.ObserveExits` — una puerta nueva nace **sin nivel**. Antes nacía en `nivelAqui + 1`.

- Antes: el mismo scrollbar salía sin nivel en `/inicio` y en nivel 4 en `/u-versiones`.
- Después: de 300 aristas, las 50 que tienen nivel lo tienen **todas fijado por declaración**.

### 4.2 La carpeta vuelve a ser camino

`map_routes_from` — una puerta con **destino conocido** no es contenido, diga lo que diga su
grupo. Y el bloque de contenido avisa de que ahí dentro puede haber contenedores.

- Antes: cadena estructural de 2 aristas.
- Después: `inicio → este-equipo → disco-local-c → archivos-de-programa → common-files`.

### 4.3 Clasificar es decidir una vez

`SurfaceMap.ClasificarSalida` — espejo exacto de `FijarNivel`: toca lo que hay delante **y deja
la enseñanza**, indexada por selector.

- El dato que decidió el diseño: el mobiliario que se repite **ya comparte selector**. «Nuevo»
  tiene uno solo en sus seis apariciones, y la familia «Actualizar "X" (F5)» —cinco nombres
  distintos— comparte `uia:aid=refreshButton;ct=Button`.
- Después: una declaración, tres pantallas, tres etiquetas distintas, todas clasificadas.

### 4.4 La lista de pendientes agrupa por identidad

`sin_situar` agrupa por **selector**, no por etiqueta, y avisa cuando el nombre varía entre
apariciones. Cinco «Actualizar "X"» dejan de contar como cinco pendientes.

---

## 5. Lo que queda para plata real

Ordenado por lo que bloquea, no por lo que cuesta.

### 5.1 Subcromo: el cromo necesita un dominio

Hoy `EsCromo` es un sí/no y significa «se llega desde cualquier sitio de un clic». Le falta la
pregunta obvia: *¿desde cualquier sitio **de dónde**?*

```
cromo(panel lateral)   dominio = la app entera            ← el cromo de hoy
cromo(«Colección»)     dominio = uia://explorer.exe/galería
cromo(pestañas)        dominio = uia://explorer.exe/inicio
```

El cromo global es **un subcromo cuyo dominio es la raíz**: un solo concepto, un campo nuevo, y
desaparece la excepción. Es transversal: una web con cabecera global y sub-navegación de
`/docs`; SAP con su barra principal y la barra de la transacción; cualquier maestro-detalle.

*Lo que arregla*: hoy el grafo promete que «Colección» se alcanza desde cualquier pantalla, y
falla en las seis que no son Galería. Con dominio, el navegador sabe que primero tiene que
entrar en Galería.

### 5.2 Separar ROL de PROFUNDIDAD

La palabra «nivel» está haciendo dos trabajos:

| | Qué es | Cuántos hay | ¿Cambia solo? |
|---|---|---|---|
| **Rol** | cromo / navegación de sección / acción / contenido | finito | no — es el diseño de la app |
| **Profundidad** | a cuántos saltos está esta pantalla | ilimitado | sí — depende de los datos |

El rol se enseña una vez. La profundidad se calcula y **no se enseña jamás**.

Los dos campos ya existen (`NivelNav` en la arista, `Nivel` en el nodo, calculado por BFS). El
fallo es que se ha estado enseñando profundidad dentro del campo del rol: fijar «Common Files»
en nivel 4 cablea el disco de **esta** máquina dentro del mapa de la app.

*Regla que falta*: lo clasificado como contenido **no admite nivel enseñado**.

No es específico de explorer. Pasa en toda app con datos: `/categoría/subcategoría/producto` en
una tienda, un hilo dentro de una etiqueta en Gmail, una posición dentro de un pedido en SAP.
Explorer solo lo hizo imposible de ignorar porque ahí los datos **son** la navegación.

### 5.3 La identidad de superficie ignora el estado

`Recientes / Favoritos / Compartido` sustituyen la pantalla entera y comparten identidad, porque
se deriva de la ruta. `cruzar` responde «la pantalla no cambió» cuando la foto prueba que sí —
y ese mensaje es indistinguible de «el clic no hizo nada».

*Media pieza ya construida*: el localizador soporta un sufijo `#sección` que hoy casi no se usa.

### 5.4 Fugas conocidas

- **Nodos de otras apps entran al mapa.** La guarda de «un clic no lleva a otra app» está en la
  arista y falta en el nodo. Cruzar un archivo creó tres nodos de Notepad.
- **`program-manager` (el escritorio) cuelga de explorer.exe** como nivel 0. Sus iconos son
  lanzadores de otras aplicaciones —SAP Logon, Chrome, la VPN— y son las únicas «puertas» que
  dejan el foco fuera. Debería ser superficie propia.
- **Vacío y situado dan la misma respuesta** (§3.3). Cinco minutos de arreglo.
- **El mensaje del selector engaña.** `map_set_level` parte por `;` cuando el selector no existe,
  y contesta «NO encontré 2: uia:name=X, ct=Edit», que hace creer que el parser está roto. La
  guardia de cadena entera existe y funciona; lo que falla es el mensaje de fallback.
- **«Buscar en …» no está registrada como salida**, y es el control que el propio grafo
  recomienda usar para alcanzar contenido.

---

## 6. Cómo se comprueba, sin creerle a nadie

### 6.1 El contrato

`.\scripts\contrato-del-grafo.ps1` — 11 promesas ejecutables. Cada una costó una prueba manual y
un diagnóstico. **Ningún cambio del núcleo entra sin esto en verde.**

Si una promesa estorba para un cambio, la conversación es sobre el contrato, no sobre la prueba:
cambiarla es cambiar lo que el grafo promete a todo lo que se construye encima.

### 6.2 Las métricas que sirven, y una que no

| Métrica | Sirve | Por qué |
|---|---|---|
| Cadena más larga de aristas **no cromo** | ✅ | Es la profundidad real que el mapa sostiene. |
| Veces que hay que declarar el mismo control | ✅ | Distingue plata de plata por fuerza bruta. |
| `sin_situar` monótona decreciente | ✅ | Un criterio de terminado que crece no es un criterio. |
| Aristas con nivel **no** fijado | ✅ | Debe ser cero: mide números inventados. |
| Aristas con destino conocido | ❌ | La domina el cromo repetido. Subió de 2 a 4 en profundidad real mientras el porcentaje no se movía. |

### 6.3 La regla del arquitecto

El arquitecto es un instrumento de medida, no una autoridad. **Sus hallazgos se verifican contra
el grafo antes de actuar.** En tres casos midió bien el síntoma y falló la causa:

- «El ítem seleccionado pierde su nivel» → los datos lo desmienten; era otro control (la primera
  miga del breadcrumb, cuyo nombre cambia con la ruta).
- «El selector no se parsea» → la guardia existe; lo que falla es el mensaje cuando el selector
  no existe.
- «El grafo acuñó la arista a Notepad» → la arista fue bloqueada; el que se creó fue el nodo.

### 6.4 Antes de cada prueba

- **Limpiar el terreno**, siempre. Las enseñanzas se conservan: son aprendizaje, no terreno.
- **Nunca matar el `U.exe` estable.** Se desarrolla contra `C:\U-versiones\v1\bin`.
- **Nunca accionar por coordenadas.** Solo por identidad: un clic por coordenadas falla en
  silencio y reporta éxito.

---

## 7. Dónde está cada cosa

| | Dónde |
|---|---|
| El núcleo del grafo | `windows-client/src/Navigation/SurfaceMap.cs` (congelado; contraseña + declaración de intención) |
| Las promesas | `tests/ContratoDelGrafo/Contrato.cs` · `scripts/contrato-del-grafo.ps1` |
| Las herramientas que usa el arquitecto | `windows-client/src/Mcp/SurfaceMapTools.cs` |
| El arquitecto | `agente-arquitecto/arquitecto.mjs` |
| Sus informes | `C:\U-versiones\feedback-arquitecto\<app>.md` |
| Reglas de mapeo (capa 1) | `docs/graphify.md` |
| Versiones del núcleo | `scripts/version-nucleo.ps1` · `C:\U-versiones\vN\bin` |
