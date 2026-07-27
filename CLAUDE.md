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

## Estado actual (2026-07-26, noche)

`main` está en **`2d0a0a6`**, con todo mergeado. `test/locator-robusto` apunta al mismo commit.

### La cadena completa funciona, verificada contra el SAP real

Un workflow grabado arranca desde SAP Easy Access y llega solo hasta el formulario de paciente:

```
paso 1-2  okcd «nwp1» + Enter        → NWP1                    (cambió)
paso 3    doubleClickItem(Column1)   → …/ssubVIEW_SCREEN…      (cambió)
paso 4    PressToolbarButton(NV44)   → NV2000/…/subPATEINST…   (cambió)
paso 6    input RNPA1-PASSNR                                    ✓
↩ resultado: 5/6 ejecutado(s) · 1 omitido(s)
```

Lo que hay detrás de cada eslabón está en [`windows-graph/CLAUDE.md`](windows-graph/CLAUDE.md).

Herramientas nuevas para diagnosticar, todas en el panel de la carita:

- **🧪 Ensayo en seco** — recorre el plan sin tocar la pantalla. Lo que más pesa: marca **cada cambio de
  pantalla** y, si el paso anterior es un `input` (que no navega), lo declara bloqueante — falta el paso
  que navega. Detecta en dos segundos el fallo que costó un día.
- **👣 Paso a paso** — se detiene ANTES de cada paso, con el veredicto y **la captura de cuando lo
  enseñaste** al lado. Esas capturas llevaban meses guardándose sin que las usara nadie.
- **Huella estructural** por paso: hash de los ids de los elementos interactivos + el tamaño de cada
  shell. Detecta que sigues en la misma transacción pero la pantalla no está en el mismo estado. Hoy
  **avisa, no detiene** — hasta que tenga kilómetros encima.

### Pendientes reales

1. **La carrera del `Busy`.** `session.Busy` solo es `true` *durante* el round-trip. Justo después de
   nuestro clic SAP aún no empezó, así que `Busy=false` y los elementos de la pantalla vieja resuelven →
   se puede clicar sobre la pantalla anterior. Fix: exigir la condición en **3 sondeos consecutivos**.
2. **El puente consciente improvisa.** Cuando el workflow se detiene, computer-use recibe «retoma y
   termina la tarea» y elige por su cuenta — una vez pulsó «Buscar pacientes» en vez de «Crear Triage
   Administrativo». Está atado al origen desde hoy, así que no puede teclear fuera de SAP, pero dentro
   inventa. Lo que le falta está escrito abajo, en *El agente que se rescata solo*.
3. **Se queda en bucle** cuando la compuerta lo frena y no consigue traer SAP al frente: 14 turnos
   rebotando y gastando `wait`.
4. `topNode` no resuelve en algunas pantallas. Sin él no hay cajas por fila en el inspector.
5. **Código inerte:** la supresión de sub-elementos dentro de árboles mide `0 sub-elementos`. Nació de
   una hipótesis falsa; borrar o justificar.
6. La **huella es ciega al contenido** de un shell salvo por su tamaño: dos pantallas con el mismo
   número de filas dan la misma huella.

### El agente que se rescata solo (diseño acordado, sin implementar)

Cuando no encuentra la ruta, hoy improvisa. Le falta, en orden de impacto:

1. **Un objetivo comprobable por máquina.** El workflow ya sabe a dónde tenía que llegar: es el
   `observedSurface` del paso que falló. Decírselo convierte la improvisación en una búsqueda acotada
   con criterio de éxito verificable — y «terminé» deja de ser una opinión del modelo.
2. **El inventario de lo accionable sin coordenadas**: los botones de toolbar con su clave, las filas
   con la suya, los campos con su id. Que diga «pulsa `NV44`» en vez de «clic en (683, 242)».
3. **Validar el aterrizaje con la misma compuerta** que usa el player. Hoy hay dos jueces y uno es un
   modelo optimista.
4. **Que lo aprendido se quede**: insertar el paso descubierto en el workflow, como ya hace
   `PrependAlignmentStepAsync` con la alineación. Eso cierra la premisa del producto — el hueco de
   `NV44` lo tapó un humano leyendo COM; con esto lo tapa el sistema la primera vez.

## El puente con el portal clínico (repo `Pagina-web-clientes-final`)

Mientras el médico dicta, el portal produce **conceptos canónicos** (`vital.talla`, `vital.peso`,
`vital.presion.sistolica`…) y el agente los va escribiendo en los campos de SAP al llegar a la pantalla.

Repartición, decidida a propósito: **el portal no sabe de SAP y el agente no sabe de medicina.** Los
conceptos son estables; las pantallas cambian. El acoplamiento vive en el cliente, que es quien ve la
pantalla y aprende el mapeo concepto↔selector una vez por pantalla.

- El portal expone `GET /api/agent/values?code=XXXXXXXX` → `{ rev, values, evidence }`, con ETag para
  que el sondeo cada 1,5 s sea barato, y `409 + stop:true` cuando la consulta se firma.
- **Emparejamiento por código**, no por credencial: el agente no puede llevar el JWT del médico. El
  código dura 8 h o hasta que se firme la consulta, lo primero que pase.
- Lado Windows: **sin implementar todavía**. Falta pegar el código, sondear, y colocar sin sobrescribir
  lo que ya tenga valor.

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

10. **Lo peor no es que falle: es que parezca que funcionó.** El salto-adelante se comió 19 pasos y
    reportó «29 de 30»; otra corrida devolvió `ok=True pasos=2/2` de un plan de 4. Los dos venían del
    mismo vicio: el denominador se calculaba sobre los pasos *con veredicto*, y los saltados no dejaban
    veredicto, así que el total encogía con ellos. **Un paso no ejecutado tiene que dejar rastro**
    (`Omitted`), o el recuento describe con exactitud una corrida que no hizo el trabajo.

11. **Antes de arreglar la clase de error, cuenta cuántos sitios la tienen.** Cableé el diagnóstico de
    la superficie SAP en dos de los **tres** sitios que la construyen. El que faltaba era justo el que
    usa el operador, así que el fallo siguió mudo una corrida más — mientras yo citaba el aprendizaje
    nº7 en el commit.

12. **Un mensaje que no distingue sus causas manda la investigación al lugar equivocado — otra vez.**
    «sin clic reciente que SAP reconozca» cubría tres situaciones: no hay clic anotado, el clic es
    viejo, o el hit-test no encuentra nada. Es el aprendizaje nº2, incumplido al escribirlo.

13. **Pregúntale a la API antes de creerle al código.** Una sonda de solo lectura con enlace tardío
    puro contestó en veinte minutos tres preguntas que llevaban semanas resueltas «por deducción»: que
    el enganche COM sí calza (con sus DISPIDs), que **no existe getter de foco**, y que los botones de
    una barra de ALV son items con clave. Es barato y sustituye rondas enteras de teoría.

14. **Un dato que viene de la red puede llegar vacío en vez de ausente.** `??` no cae al respaldo con
    cadena vacía. Ese detalle convirtió cada clic de árbol en un `SetFocus()` que reportaba éxito.

15. **Antes de escribir en un repo que no conoces, lee sus reglas.** El portal clínico avisa en su
    `AGENTS.md` de que su Next.js no es el que uno cree, y no usa service-role en ninguna parte: eso es
    una postura de seguridad, no un olvido. Ir con `security definer`, que es lo que ya usan, en vez de
    meter una llave nueva.
