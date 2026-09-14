# Traspaso: llevar la ejecución de la nota clínica a SAP

Esto es el traspaso del frente «ejecución»: que lo que un médico dicta en una consulta acabe
escrito en SAP, y que lo haga **por lo que se le enseñó** a Ü, no por reglas fijas. Está escrito
para leerse de corrido, sin abrir código. Al final digo qué está probado, qué **no**, y dónde entra
tu trabajo.

Para meterle mano con tu agente hay un segundo archivo, más técnico y más largo:
[`TRASPASO-EJECUCION-PARA-EL-AGENTE.md`](TRASPASO-EJECUCION-PARA-EL-AGENTE.md). Dáselo entero.

---

## 1. El problema, en una frase

Un médico del Hospital General de Medellín atiende hablando. Al terminar tiene una nota clínica
por secciones (motivo, hallazgos, plan). Esos datos tienen que acabar en SAP IS-H, en la pantalla
del triage, campo por campo. Hoy los teclea alguien a mano.

## 2. La apuesta, y por qué no es «un bot que rellena formularios»

Ü **no sabe de SAP**. Sabe lo que un humano le enseñó haciéndolo una vez delante de él. La regla
de oro del proyecto es que **la ejecución sale de la enseñanza, no de un guion escrito por un
programador**. Si mañana el hospital cambia la pantalla, no se cambia código: se le vuelve a
enseñar.

Eso obliga a tres cosas que cuestan más que un bot, y que son el producto:

- **Enseñar es una demostración grabada.** Clics, tecleos, pantalla y voz, con un solo reloj.
- **Lo enseñado se comprueba antes de usarse.** Ü lo repite una vez con el humano mirando, paso a
  paso, con la carita al lado de cada elemento diciendo qué hace. Solo lo que se vio andar queda
  como tarea utilizable.
- **Al ejecutar, nada se inventa.** Los valores que el humano tecleó en la demo eran de un paciente
  de prueba y **jamás** se repiten. Cada campo es un hueco que se llena con el dato de la nota de
  hoy, y si la nota no lo trae, el campo queda en blanco y se dice. Grabar en SAP es siempre del
  médico.

## 3. El circuito completo, tal como está hoy

```
 🎓 enseñar          ▶ comprobar            ✓ en la nota
 ───────────         ──────────────         ─────────────────────
 demostración   →    lección          →     tarea comprobada  →  encargo   →  el piloto elige   →  SAP
 (clics, voz,        (línea de tiempo       (pasos verificados,   (secciones     la tarea de su        (campo a
  pantalla)           con cuadros)           huecos por campo)     marcadas)      catálogo y la corre    campo, con
                                                                                                        la carita)
```

1. **Enseñar.** Se pulsa 🎓 con SAP delante, se hace la tarea hablando, se vuelve a pulsar. Queda
   una *lección* en disco: 48 eventos con su cuadro de pantalla, lo que se dijo, y lo que SAP
   observó.
2. **Comprobar.** Un *piloto* (un agente Claude con manos por MCP) lee la lección, entrega un plan
   y la app lo recorre paso a paso, juzgando cada uno: ¿llegué a la misma pantalla? ¿el campo dice
   lo que se tecleó? Solo lo que aterrizó entra en la tarea. Si todo aterrizó, queda **comprobada**.
3. **El panel de aprendizajes.** En la ventana de la consulta, detrás del icono del cerebro: la
   lista de tareas, qué hace cada una en castellano, qué datos necesita, y un botón «Mostrar» que
   la corre delante de ti (o la comprueba, si aún no lo está).
4. **El ✓ de la nota.** El médico marca secciones y pulsa ✓. Eso arma un *encargo*: el texto
   marcado más el catálogo de tareas comprobadas con los datos que cada una pide. El piloto
   **elige la tarea por su criterio**, arma los datos con lo que la nota trae, y la corre. Sin
   preguntar nada.

## 4. Lo que está probado, y lo que no

**Probado en el SAP real (QAS), en el triage de Urgencias Adultos:**

- Enseñar y comprobar la tarea entera del triage: 21 pasos, 17 campos, desplegables, cajas de
  texto largo. Trece pruebas en una semana; la última comprobación limpia salió 17 de 19 y esos dos
  fallos ya están arreglados (pantalla cogida a medio cambiar; clic final sin identidad).
- La carita al lado de cada campo diciendo qué escribe, con la voz de la conversación prestada.
- El panel de aprendizajes pintando con datos reales, y «Mostrar» lanzando la comprobación.
- El ✓ con **cero** tareas comprobadas: el piloto se niega a inventar. Correcto.

**No probado todavía (es lo primero que te toca):**

- **El ✓ de punta a punta con una tarea comprobada.** Está construido y con sus promesas verdes,
  pero nadie lo ha visto rellenar el triage desde una nota real. Requiere: comprobar la tarea de
  nuevo con los últimos arreglos, grabar una consulta con signos vitales, pulsar ✓.
- **«Mostrar» esperando al veredicto** en el panel (antes decía «mira la pantalla» y volvía).

**Límites conocidos que vas a ver:**

- La tarea elige al paciente por la fila que se tocó en la demo («GIRALDO»). Con otro paciente
  parará ahí. El paciente tiene que volverse un hueco. Es la siguiente spec natural.
- La presión diastólica tiene por etiqueta «/» en SAP, así que su hueco se llama «/» y la nota
  nunca lo casa. Queda en blanco y se dice.
- Botones de opción y casillas, campos de fecha con ayuda, controles de tabla, pestañas y
  diálogos modales: no se han ejercitado ni una vez.

## 5. Las reglas de la casa (no se negocian, y cada una costó algo)

1. **Ninguna línea de producción entra antes que la promesa que la juzga.** El contrato
   (`tests/ContratoDelGrafo/Contrato.cs`, 229 promesas hoy) se escribe en rojo, luego el código lo
   pone verde, luego se **rompe a propósito** para ver que se pone rojo, y luego se prueba en el PC
   real. Un PR sin esas cuatro cosas no entra.
2. **El log es la fuente de verdad.** Antes de teorizar, `%LOCALAPPDATA%\U\logs\u-AAAAMMDD.log`
   (o el de la app de desarrollo). Cuatro rondas se perdieron una vez deduciendo de capturas.
3. **Nunca coordenadas.** Todo se toca por identidad: la puerta por su nombre, el campo por su
   etiqueta. Un selector crudo no llega jamás a la pantalla del médico.
4. **Un solo ejecutor.** Una tarea enseñada se traduce a pasos del mismo batch que usa todo lo
   demás. Un segundo ejecutor sería una segunda opinión del mismo hecho.
5. **Lo que se le pide al modelo se le pide por herramientas, y lo prohibido se prohíbe por su
   nombre.** El SDK busca herramientas por su cuenta; la caja del encargo no tiene manos sueltas ni
   preguntas.
6. **Cerrar la app siempre por ruta, nunca por nombre.** Todas se llaman `U`, y una de ellas es la
   que el médico tiene abierta.

## 6. Dónde entra tu trabajo, en orden

1. **Ver el circuito entero funcionar una vez.** Comprobar → ✓ → triage lleno. Con el log al lado.
   Hasta que no lo veas, no toques diseño.
2. **Decidir el estado de la rama.** `jose/el-check-corre-una-skill` lleva tres specs sin
   commitear (015, 016 y 019): el ✓ por skill, el panel de aprendizajes y la prueba limpia. El repo
   pide una spec por rama. Con Jose: partir en tres PR o entrar juntas.
3. **El paciente como hueco.** Que la fila que se elige en la lista sea un dato del encargo, no un
   nombre fijo de la demo.
4. **Radios, casillas y fechas.** Hoy se graban como clic sin valor y el juez no los cuenta.
5. **Lo que el panel deja fuera a propósito** (buscador, editar pasos a mano, workflows del grafo):
   está escrito en la spec 016 con el porqué. Si alguien lo pide, léelo antes.

## 7. Cómo empezar, en media hora

```powershell
git config core.hooksPath .githooks          # el portero, una vez por clon
.\scripts\contrato-del-grafo.ps1             # ~90 s: tiene que decir CONTRATO INTACTO
$env:U_PILOTO = "<repo>\agente-piloto\piloto.mjs"
.\scripts\dev-paralelo.ps1                   # app de desarrollo aislada; si cierra una y no lanza, repítelo
```

Después: lee `docs/specs/015`, `016` y `019` en ese orden (cada una empieza por lo que se midió),
abre la consulta desde la carita, entra a Aprendizajes, y pulsa «Mostrar» con SAP QAS abierto en
Easy Access. Tarda unos minutos y cuesta unos dólares de modelo. Lo que pase está en el log.
