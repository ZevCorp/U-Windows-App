# Plan hasta el producto: controlar cualquier app por el grafo

> Documento vivo. Cada fase tiene una PRUEBA que la cierra. Los problemas que aparezcan durante
> las pruebas se anotan aquí y se convierten en pasos — el plan se corrige con lo que se mide,
> no al revés.

## Dónde estamos (medido, 2026-08-03)

| Capacidad | Estado | Evidencia |
|---|---|---|
| Mapear una app sola | **funciona** | explorador 16 pantallas/16 rutas en 68 s; Configuración 11/98 |
| Navegar por el grafo | **funciona** | 40 saltos, 39 correctos, media 1,2 s |
| Ejecutar por interfaz | **funciona** | 21 pasos, 0 fallos: crear carpeta, renombrar, selección múltiple, cortar, pegar |
| No hacer daño | **funciona** | rechazó 28 acciones tras un error, 0 archivos tocados |
| Salir de un bloqueo | **funciona** | detecta diálogo, escala la decisión, reanuda |
| Elegir la app a aprender | **funciona** | `map_learn_app` la trae al frente ella sola |
| **Repetibilidad** | **NO medida** | corridas sueltas perfectas; nunca 4/4 seguidas |
| **Planificador** | **NO existe** | los 21 pasos los escribió una persona |
| **Tarea aprendida** | **NO existe** | cada paso es una consulta; no hay «esto ya lo sé hacer» |

**El hueco grande es el planificador.** Todo lo demás son piezas que funcionan y que nadie
orquesta todavía a partir de una orden en lenguaje natural.

---

## Fase 1 · Que la espera sea por ESTADO, no por reloj

**Por qué:** hoy la capa del mapa espera tiempos fijos (300-800 ms por acción). `SurfaceReadiness`
—que ya existe y usan el grabador y el reproductor— sabe decir cuándo una pantalla está lista.
Usarlo quita esperas muertas y, sobre todo, quita fallos: hoy se actúa a veces antes de tiempo.

**Distinción que no hay que perder:** *readiness* responde «¿puedo actuar ya?» y da velocidad;
*plazo* responde «¿esta llamada murió?» y evita cuelgues. Hacen falta los dos.

**Pasos**
1. `SurfaceMapTools` y `GraphCrawler` usan `SurfaceReadiness` para esperar la pantalla.
2. Las esperas fijas restantes (`EsperarCambio`, la del menú, la del campo de renombrado) pasan a
   condición de estado.
3. Los plazos anti-cuelgue se quedan como red, no como mecanismo de espera.

**PRUEBA QUE LA CIERRA:** la tarea de organizar 8 archivos baja de ~32 s a menos de 20 s, con
0 fallos. Si baja pero aparecen fallos, es que las esperas fijas estaban tapando una carrera —y
eso es un hallazgo, no un retroceso.

---

## Fase 2 · Repetibilidad

**Por qué:** para que un usuario pida algo tranquilo, la misma tarea tiene que salir igual siempre.
Hoy tenemos corridas perfectas sueltas y no una tasa.

**Pasos**
1. Banco de pruebas que se reinicia SIN romper el estado de la app (aparcar la app fuera de la
   zona antes de tocar el disco; no dejar anclajes rotos en el acceso rápido).
2. Diez corridas seguidas de la misma tarea, contando perfectas.
3. Cada fallo se diagnostica y se convierte en regla de Graphify o en paso de este plan.

**PRUEBA QUE LA CIERRA:** 10/10 perfectas. Con menos, no se avanza a la fase 3 — construir un
planificador sobre una base intermitente produce fallos que parecen del modelo y son del terreno.

---

## Fase 3 · El planificador

**Por qué:** es el hueco grande. Nadie convierte «organiza mis archivos» en pasos.

**Pasos**
1. El modelo recibe: dónde está, qué salidas hay (navegación y acciones separadas), y el objetivo.
2. Devuelve el siguiente paso, no el plan entero — porque el terreno cambia y un plan largo
   envejece mal.
3. Cada paso pasa por el ancla de ubicación y por los vetos que ya existen.
4. Ante un bloqueo, `map_unblock` con la decisión del modelo.

**PRUEBA QUE LA CIERRA:** una orden en lenguaje natural —«ordena la carpeta de pruebas por tipo de
archivo»— se ejecuta entera sin que nadie escriba los pasos.

---

## Fase 4 · La tarea aprendida (ejecución rápida de verdad)

**Por qué:** hoy cada paso es una consulta al modelo. Una tarea de 21 pasos son 21 consultas. La
velocidad real llega cuando la secuencia entera es algo conocido.

**Distinción:** el grafo aporta el CÓMO SE NAVEGA; un procedimiento aprendido aporta el QUÉ SE
HACE. En este proyecto eso ya tiene nombre: los workflows.

**Pasos**
1. Al completar una tarea con éxito, guardarla como procedimiento (los pasos, no las coordenadas).
2. Al repetirla, ejecutarla sin consultar al modelo entre pasos.
3. Si un paso falla, caer al planificador para ese tramo — no abandonar la tarea entera.

**PRUEBA QUE LA CIERRA:** la segunda vez que se pide la misma tarea tarda una fracción de la
primera, con 1 consulta al modelo en vez de 21.

---

## Fase 5 · Cualquier app

**Por qué:** dos apps ya nos enseñaron que varias reglas «universales» eran del explorador. La
tercera y la cuarta enseñarán más.

**Pasos**
1. Mapear una app web (navegador) y una de escritorio clásica (Notepad, Word).
2. Cada regla nueva va a Graphify, en su sección: universal o por app.
3. Revisar qué reglas «universales» se caen con cada app nueva.

**PRUEBA QUE LA CIERRA:** una app nunca vista se mapea y se navega sin tocar código.

---

## Problemas abiertos (se actualizan con cada prueba)

- **Mapeo lento en apps grandes.** Configuración: 516 s para 4 pantallas en la última corrida,
  frente a 98 rutas en una anterior. Sin diagnosticar: ¿plazos demasiado cortos o algo más?
- **Una app normal que se aferra al foco** (un navegador con diálogo modal) bloquea y el sistema
  se para en vez de resolverlo. Los paneles del shell sí se apartan con Escape; una app no.
- **Diálogos opacos a UIA** (clase `Shell_Dialog`): no se pueden leer ni pulsar. Se detectan y se
  reportan; no hay forma de resolverlos por esta vía.
- **Sub-navegación dentro de una sección.** La identidad llega hasta la sección
  (`#bluetooth-y-dispositivos`); dentro de una página con varias subpáginas probablemente vuelva
  a hacer falta profundizar.
