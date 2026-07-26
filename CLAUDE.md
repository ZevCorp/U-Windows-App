# Ü Windows — guía para trabajar en este repo

## Qué es

Un asistente que **aprende a operar aplicaciones de escritorio mirando a un humano** y después las
opera solo. El caso real es **SAP GUI en un hospital** (Hospital General de Medellín, IS-H): admitir
pacientes, listar radicados, consultar órdenes clínicas. Se le enseña una vez y lo repite.

No es un grabador de macros por coordenadas: aprende *qué elemento* se tocó para poder repetirlo aunque
la ventana cambie de tamaño, posición o scroll.

## Arquitectura: cliente tonto, cerebro remoto

| Carpeta | Qué es | Dónde corre |
|---|---|---|
| `windows-client/` | Frontend C#/WPF (.NET 8) → `U.exe`. Lee la UI, captura pantalla, mueve ratón/teclado, habla. | PC del usuario |
| `windows-graph/` | Grabar/reproducir workflows sobre SAP GUI y UIA. Compila dentro de `U.exe`. | PC del usuario |
| **Graph** | El cerebro: LLM, memoria, catálogo de workflows. | `graph-eight-pied.vercel.app` |

El cliente no contiene prompts ni decisiones — todo va por HTTPS. Es anti-copia deliberado: descompilar
el `.exe` no revela la inteligencia. Ver `WINDOWS.md`.

**Los arreglos de fondo casi siempre van en `windows-graph`** (es quien sabe de SAP). `windows-client`
es presentación y diagnóstico.

## Las dos superficies

1. **UIA** — genérica, cualquier app Windows.
2. **SAP GUI Scripting** (COM) — específica de SAP.

**Dentro de SAP GUI, UIA no ve nada.** Se queda en un `Pane` opaco: ni el árbol, ni los campos del
dynpro, ni la barra. Para SAP es Scripting API o nada. Detalles y trampas:
[`windows-graph/CLAUDE.md`](windows-graph/CLAUDE.md).

## Compilar y correr

```powershell
cd windows-client
dotnet build -c Release
```

Ejecutar `bin\x64\Release\net8.0-windows\U.exe`. **No usar `dotnet run`** para sesiones largas: el
wrapper sale con 255 cuando se cierra la ventana y confunde el diagnóstico.

Si el build falla con `MSB3027 / U.exe está bloqueado`, hay una instancia corriendo:
`Get-Process -Name U | Stop-Process`.

## EL LOG ES LA FUENTE DE VERDAD

```
%LOCALAPPDATA%\U\logs\u-AAAAMMDD.log
```

`LogBus` persiste todo a disco además del ring de 500 líneas en memoria. **Leerlo antes de teorizar.**

Esto no es un consejo de estilo: en la sesión del 2026-07-26 se perdieron **cuatro rondas** deduciendo
de capturas de pantalla lo que el archivo decía literalmente. Una captura no distingue "espaciado
correcto con desplazamiento constante" de "filas equivocadas" de "otra caja pintada encima" — el log sí.

Líneas útiles: `shell subType=`, `filas del árbol ·`, `fila seleccionada`, `CONTRASTE geometría`,
`✋ no se llegó a`, `⏱ TIEMPOS`.

## Estado actual (2026-07-26)

Rama de trabajo: **`test/locator-robusto`** — commit `24533bd`, pusheada a `origin`.

`main` está en `db8e73a` y le faltan **16 commits**, incluido `b669e62` (accionar filas de árbol por
clave). **Nada del trabajo del árbol está en main.**

Ojo con la historia de la rama: ocho commits con mensaje `@`, y dos son *reverts* de intentos de
optimizar el locator por eventos de Windows. Squashearlos limpia el historial pero borra el rastro de
por qué se revirtieron, que ya resultó útil.

### Funciona y está verificado contra el SAP real

- El árbol se enumera y se marca como mapeado (525 y 20 filas en las dos vistas).
- **La fila clicada se identifica por clave** vía `selectedItemNode` → el asistente la reproduce con
  `doubleClickNode(key)`, sin coordenadas ni OCR.
- **Cajas por fila exactas**, con la geometría que da SAP (`GetItemTop`/`GetItemHeight` con columna).
- La compuerta del reproductor **detiene** un paso cuya pantalla no coincide, en vez de clicar a ciegas
  y reportar éxito.

Todo esto probado **solo en `SESSION_MANAGER`** (SAP Easy Access). Ver pendientes.

### Pendientes reales

1. **La grabación no captura la entrada a la transacción.** Un workflow grabado dentro de NWP1 empieza
   asumiendo que ya estás ahí; al reproducir desde Easy Access el paso 1 nunca alcanza su pantalla y se
   detiene (con 9,8 s perdidos esperando algo imposible). **Sin esto ningún workflow arranca solo** —
   es el pendiente más importante.
2. **El techo del reproductor ejecuta igual.** A los 4 s sin confirmar da `ArrivedUnconfirmed` y actúa.
   Es una decisión de diseño ("resiliente") contraria a lo que pidió el operador: debería **abortar**,
   con techo más generoso (~15 s) porque las pantallas clínicas son lentas.
3. **La carrera del `Busy`.** `session.Busy` solo es `true` *durante* el round-trip. Justo después de
   nuestro clic SAP aún no empezó, así que `Busy=false` y los elementos de la pantalla vieja resuelven →
   se puede clicar sobre la pantalla anterior. Fix: exigir la condición en **3 sondeos consecutivos**.
4. `topNode` no resuelve en algunas pantallas (queda en el log). Sin él no hay cajas por fila.
5. `FindByPosition` (hit-test nativo) devuelve null en estos árboles: todos los clics caen a
   `vía bbox (fallback)`. No afecta al accionado por clave, pero es deuda.
6. **Código inerte:** la supresión de sub-elementos dentro de árboles mide `0 sub-elementos`. Nació de
   una hipótesis falsa; borrar o justificar.

## Aprendizajes de método

Estos costaron caro. Aplicarlos ahorra rondas enteras.

1. **Leer el log antes de teorizar.** Ver arriba.

2. **Un mensaje de error no debe concluir, debe describir el paso que falló.** El texto
   *"getters de selección sin resultado"* se imprimía también cuando el árbol **nunca se había
   resuelto** — una conclusión disfrazada de hecho. Mandó la investigación al lugar equivocado dos
   veces. Si un mensaje afirma una causa, tiene que poder distinguirla de las demás.

3. **Un `try/catch` mudo convierte un bug de aridad en "la API no existe".** La creencia documentada de
   que SAP no da geometría por nodo venía de llamar los getters con un argumento de menos. Al capturar
   en silencio, "falló por firma" y "no existe" son indistinguibles.

4. **Una caja que miente es peor que no tener caja.** Invita a confiar en ella. Aplicado dos veces: no
   dibujar filas sin `topNode`, y distinguir geometría *leída* de *estimada*.

5. **Contención no es alineación.** Se quitó una corrección vertical argumentando que el contraste
   `CUADRA` probaba que la banda cruda era correcta. `CUADRA` solo verifica que el clic caiga *dentro*;
   una banda desplazada la pasa igual. El síntoma volvió.

6. **Cuando una limitación documentada resulta falsa, borrar la maquinaria de compensación — no
   parchearla.** Había tres capas (`topNode` + recorrido con plegado + alto calibrado) para reconstruir
   lo visible. Al probarse que SAP sí da geometría por nodo, había que eliminarlas; en su lugar se
   siguieron ajustando, y cada capa aportaba su propio modo de fallo. La versión final es una regla:
   *si el top que da SAP cae dentro del alto del árbol, la fila se ve.*

7. **Arreglar la clase de error, no el caso.** El veredicto rojo falso se arregló para árboles y media
   hora después reapareció idéntico en el grid: **ningún** shell se acciona por etiqueta. Si un arreglo
   se apoya en "este tipo de elemento no usa ese criterio", revisar todos los que tampoco lo usan.

8. **No optimizar la cadencia antes del costo por iteración.** Bajar el refresco a 200 ms antes de
   arreglar las ~2.600 llamadas COM/s amplificó un bug latente y disparó una cacería de una hora.

9. **Antes de mergear, contar en cuántas pantallas se probó.** Una sola pantalla verificada es una
   apuesta a que las demás se comportan igual — y el run de NWP1 demostró que no.
