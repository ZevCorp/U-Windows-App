// EL ARQUITECTO: un agente Claude que NAVEGA la app de verdad y contrasta la jerarquía que ve con
// la que el grafo está construyendo.
//
//   node arquitecto.mjs explorer.exe            # explorar el explorador, presupuesto por defecto
//   node arquitecto.mjs chrome.exe 30           # con presupuesto de 30 turnos
//
// Por qué es un CEREBRO EXTERNO y no un crawler nuevo: el crawler ya sabe lo difícil —pulsar por
// identidad, recolocarse, pararse ante diálogos— y todo eso está expuesto por la sonda MCP local
// (U_MCP_PROBE=1, puerto 8791), que es EXACTAMENTE el mismo camino por el que actúa el asistente.
// Este agente no toca la pantalla: le pide a la app que actúe, con las mismas herramientas, y pone
// lo que el crawler no tiene — criterio sobre QUÉ cruzar, memoria de lo que vio, y la capacidad de
// decir «esto que el grafo cree no se parece a lo que la app es».
//
// Lo que PUEDE hacer: mirar, cruzar puertas, fijar niveles (map_set_level) y dejar hallazgos
// escritos (map_feedback → C:\U-versiones\feedback-arquitecto\). Lo que NO puede hacer: editar
// código, tocar archivos, correr comandos — no tiene esas herramientas, no es que prometa no
// usarlas. La autoridad sigue el mismo orden del grafo: lo que dijo UNA PERSONA no se mueve; si el
// arquitecto discrepa de una persona, lo dice en el feedback y no lo toca.

import { query, tool, createSdkMcpServer } from "@anthropic-ai/claude-agent-sdk";
import { z } from "zod";

const APP = process.argv[2];
if (!APP) {
  console.error("uso: node arquitecto.mjs <app> [turnos]   (p. ej. explorer.exe 40)");
  process.exit(2);
}
const TURNOS = parseInt(process.argv[3] ?? "80", 10);   // bajar en profundidad cuesta turnos: agotar una rama son varios cruces + un ir_a por cada vuelta

// ── La sonda: el único brazo del agente ─────────────────────────────────────
async function sonda(toolName, args = {}) {
  const res = await fetch("http://127.0.0.1:8791/mcp/", {
    method: "POST",
    headers: { "content-type": "application/json; charset=utf-8" },
    body: JSON.stringify({ tool: toolName, args }),
  });
  const texto = await res.text();
  // La sonda devuelve texto plano (a veces envuelto en JSON de una cadena): se normaliza.
  try { const j = JSON.parse(texto); return typeof j === "string" ? j : texto; }
  catch { return texto; }
}

const t = (name, description, schema, fn) =>
  tool(name, description, schema, async (args) => ({
    content: [{ type: "text", text: await fn(args) }],
  }));

// MIRAR NO ES LEER. Una herramienta que devuelve una FOTO, no su descripción: un panel lateral y
// una lista de contenido se distinguen de un vistazo y son indistinguibles en una lista de
// etiquetas. La sonda entrega un data-URI; aquí se convierte en un bloque de imagen de verdad para
// que el modelo lo VEA (2026-08-08, pedido por el usuario: «que pueda tomar screenshots y verlos»).
const tFoto = (name, description) =>
  tool(name, description, {}, async () => {
    const r = await sonda("map_shot");
    const m = /^data:image\/png;base64,(.+)$/s.exec(r.trim());
    if (!m) return { content: [{ type: "text", text: r }] };
    // FORMATO MCP, no formato de la API de Anthropic. Aquí iba `source: {type, media_type, data}`
    // —la forma que usa la API de mensajes— y MCP quiere `data` + `mimeType` planos. El servidor
    // rechazaba la respuesta entera con -32602 «Invalid tools/call result», así que el agente se
    // quedó CIEGO justo en la herramienta que existía para que viera: tuvo que clasificar chrome
    // contra contenido solo con nombres, que es el modo de fallo que la foto venía a evitar
    // (2026-08-09, lo reportó él mismo en su auditoría).
    return {
      content: [
        { type: "image", data: m[1], mimeType: "image/png" },
        { type: "text", text: "Foto de la ventana que hay delante ahora mismo." },
      ],
    };
  });

const herramientas = createSdkMcpServer({
  name: "grafo",
  version: "1.0.0",
  tools: [
    t("donde_estoy", "En qué pantalla está parado el sistema ahora mismo (identidad de superficie).",
      {}, () => sonda("map_where_am_i")),

    t("que_veo", "Las salidas VISIBLES de la pantalla actual: puertas por las que se puede entrar, con su nombre y tipo.",
      {}, () => sonda("map_what_i_see")),

    t("cruzar", "Cruzar una puerta VISIBLE por su nombre (o por su selector si el nombre es ambiguo). Es un clic por identidad hecho por la app, nunca por coordenadas. Devuelve a dónde llevó.",
      { puerta: z.string().describe("nombre exacto de la salida, o su selector uia:...") },
      (a) => sonda("map_take", { exit: a.puerta })),

    t("ir_a", "Volver a una pantalla YA CONOCIDA del mapa por su identidad (p. ej. uia://explorer.exe/inicio). Usa las rutas aprendidas.",
      { pantalla: z.string().describe("identidad de la pantalla destino") },
      (a) => sonda("map_go_to", { surface: a.pantalla })),

    t("jerarquia_del_grafo", "La jerarquía que el grafo TIENE de la app: pantallas con nivel, salidas declaradas (con quién las declaró: persona, maestro) y salidas aún sin nivel. Es tu material de contraste.",
      {}, () => sonda("map_hierarchy", { app: APP })),

    t("rutas_desde", "Qué salidas se conocen desde una pantalla y CUÁLES NO SE HAN CRUZADO todavía (las que llevan a destino desconocido). Es tu lista de pendientes para bajar en profundidad: sin esto solo verías la pantalla en la que estás.",
      { pantalla: z.string().optional().describe("identidad de la pantalla; vacío = donde estés ahora") },
      (a) => sonda("map_routes_from", a.pantalla ? { surface: a.pantalla } : {})),

    t("fijar_nivel", "Declarar el nivel de una salida (1 = navegación transversal de la app entera). cromo=true si además te sigue a todas partes. NO muevas lo que declaró una persona: si discrepas, dilo con feedback.",
      {
        salida: z.string().describe("nombre de la salida tal como se ve"),
        nivel: z.number().int().min(1).max(6),
        cromo: z.boolean().optional(),
      },
      (a) => sonda("map_set_level", { app: APP, exit: a.salida, level: String(a.nivel), ...(a.cromo === undefined ? {} : { cromo: String(a.cromo) }) })),

    t("sin_situar", "TU LISTA DE TRABAJO: las pantallas y salidas que el mapa NO sabe situar todavía, con el grupo que declaró la página. Mientras esta lista no esté vacía, la estructura está incompleta.",
      {}, () => sonda("map_unsituated", { app: APP })),

    tFoto("mirar", "Una FOTO de la ventana que hay delante. Úsala cuando los nombres no basten para decidir qué es navegación y qué es contenido: un panel lateral se reconoce de un vistazo."),

    t("marcar_atras", "Declarar cuál es el gesto de VOLVER de esta app (el botón «Atrás»). No es una puerta: es historial, y sin marcarlo cada vuelta acuña una arista falsa que ensucia la estructura.",
      { salida: z.string().describe("nombre del botón de volver, tal como se ve") },
      (a) => sonda("map_learn_back", { app: APP, exit: a.salida })),

    t("marcar_accion", "Clasificar una salida como ACCIÓN y no como navegación: hace algo (guardar, copiar, ordenar, crear) pero no lleva a otra pantalla. NO se borra del mapa — el asistente la necesitará para EJECUTAR; solo deja de contar como estructura.",
      { salida: z.string().describe("nombre de la salida, tal como se ve") },
      (a) => sonda("map_set_kind", { app: APP, exit: a.salida, kind: "accion" })),

    t("feedback", "Dejar escrito un HALLAZGO para el equipo: un desajuste entre la jerarquía real de la app y la del grafo, un nivel que no cuadra, una puerta que el grafo no vio. Es tu entregable.",
      { hallazgo: z.string().describe("el hallazgo, concreto: qué esperabas, qué hay, y por qué importa") },
      (a) => sonda("map_feedback", { app: APP, finding: a.hallazgo })),
  ],
});

// ── La misión ────────────────────────────────────────────────────────────────
const MISION = `Eres el ARQUITECTO del grafo de navegación. Delante tienes la app «${APP}», ya abierta,
y un grafo que el sistema construye solo mientras navega. Tu misión NO es mapear por mapear: es
JUZGAR si la jerarquía que el grafo está construyendo se corresponde con la arquitectura real de
la app, corregir el grafo donde te den autoridad tus herramientas, y dejar constancia del resto.

TU TRABAJO ES LLEVAR ESTA APP DE BRONCE A PLATA, y esas dos palabras tienen un significado exacto
aquí:
  · BRONCE es lo que hay ahora: todo lo visible anotado en crudo, sin jerarquía. El mapa lo tiene
    todo y no sabe qué es qué.
  · PLATA es lo mismo ORDENADO: cada salida en su nivel, el mobiliario marcado como cromo, el
    gesto de volver identificado, y lo que no es navegación fuera de en medio.

CÓMO SE MIDE QUE HAS TERMINADO, y no es una opinión: la herramienta «sin_situar» enumera lo que el
mapa todavía no sabe colocar. Empiezas mirándola y terminas cuando esté vacía o cuando lo que
quede esté explicado en el feedback. Ese es tu criterio de terminado.

QUÉ ES CADA NIVEL:
  · NIVEL 1 + CROMO = el mobiliario que TE SIGUE. Si te vas a cualquier otra pantalla de la app y
    ESO seguiría ahí, es nivel 1 cromo. El panel lateral entero, la barra superior, el menú
    principal. Marca el cromo, porque significa algo concreto: se llega desde cualquier sitio de un
    solo clic, así que el navegador no necesita aristas para moverse entre ellos.
  · NIVEL 2 = la navegación de UNA sección: existe dentro de ella y desaparece al salir.
  · NIVEL 3+ = lo que solo aparece tras abrir algo de nivel 2.
  · CONTENIDO = archivos, filas, resultados. NO es estructura: no le pongas nivel; si ensucia,
    exclúyelo.

BAJAR EN PROFUNDIDAD SIGUE IMPORTANDO. Un mapa de un solo nivel no es una jerarquía: es una lista.
Si al terminar todo cuelga del inicio, has fallado aunque no te hayas equivocado en nada.

Método de trabajo:
0. Empieza por «sin_situar» (tu lista) y «mirar» (una foto). Los nombres solos engañan: un panel
   lateral y una lista de archivos son indistinguibles en texto y obvios en una imagen. Y marca
   pronto el gesto de volver con «marcar_atras» — cada vuelta sin marcar ensucia el grafo.
1. Sigue por jerarquia_del_grafo y que_veo: qué cree el grafo, qué hay de verdad.
2. BAJA. Elige una sección con contenido, entra, y desde DENTRO vuelve a mirar (que_veo y
   rutas_desde): ahí aparecen las puertas del nivel 2. Entra en una de ellas y repite. Agota una
   rama hasta que ya no haya dónde bajar ANTES de volver a la hermana — así se aprende una
   estructura, no un abanico. rutas_desde te dice qué queda sin cruzar: úsalo para no repetirte y
   para saber dónde queda profundidad pendiente.
3. Una puerta a la vez; tras cruzar, donde_estoy confirma dónde aterrizaste — aceptado no es
   ejecutado. Si no te moviste, no insistas: prueba otra. Para volver arriba usa ir_a con la
   identidad de la pantalla, no re-cruces a ciegas.
4. Cruza lo que revele ESTRUCTURA (secciones, paneles, carpetas, pestañas) y no el contenido
   suelto: un archivo o un elemento de lista no enseña arquitectura, y encima puede sacarte de la
   app. Si una rama resulta ser solo contenido, sal y busca otra.
5. El primer nivel es PERMANENCIA, no visibilidad: algo es de nivel 1 si al irte a cualquier otra
   subpágina SEGUIRÍA ahí (panel lateral entero, pestañas, menú principal). Lo que solo existe en
   una pantalla no lo es, por grande que se vea. Y lo que está DENTRO de una sección es nivel 2 o
   más: no lo declares de nivel 1 solo porque lo veas.
6. Contrasta: ¿lo que el grafo declara nivel 1 es de verdad transversal? ¿Hay navegación
   transversal que el grafo aún no declara? Corrígelo con fijar_nivel — SALVO lo que dijo una
   persona: eso no se toca; si discrepas, feedback.
7. Cada desajuste real va a feedback en el momento, concreto: qué esperabas, qué hay, por qué
   importa. Nada de «todo bien» genérico.
8. Cierra SIEMPRE con un feedback final: la arquitectura real de la app en forma de árbol con sus
   niveles, hasta qué profundidad llegaste, qué tan fiel es el grafo (un porcentaje honesto) y los
   3 desajustes más importantes.

Límites duros: no puedes editar código ni archivos —no tienes herramientas para ello—, solo
organizar el grafo y reportar. Si la app se cierra o algo se cruza, dilo en feedback y termina.
Trabaja en español.`;

// ── A correr ─────────────────────────────────────────────────────────────────
console.log(`ARQUITECTO sobre «${APP}» · presupuesto ${TURNOS} turnos\n`);

const corrida = query({
  prompt: `Audita la jerarquía de «${APP}». La app ya está abierta y la sonda viva.`,
  options: {
    systemPrompt: MISION,
    mcpServers: { grafo: herramientas },
    allowedTools: [
      "mcp__grafo__donde_estoy", "mcp__grafo__que_veo", "mcp__grafo__cruzar",
      "mcp__grafo__ir_a", "mcp__grafo__jerarquia_del_grafo", "mcp__grafo__rutas_desde",
      "mcp__grafo__fijar_nivel", "mcp__grafo__feedback",
      "mcp__grafo__sin_situar", "mcp__grafo__mirar",
      "mcp__grafo__marcar_atras", "mcp__grafo__marcar_accion",
    ],
    disallowedTools: ["Bash", "Edit", "Write", "Read", "Glob", "Grep", "WebFetch", "WebSearch", "Task"],
    permissionMode: "bypassPermissions",
    maxTurns: TURNOS,
  },
});

// AGOTAR LOS TURNOS NO ES UN FALLO, es el presupuesto haciendo su trabajo — pero el SDK lo lanza
// como excepción, y sin capturarla Node imprime su propio código minificado entero encima del
// informe que acabas de leer (2026-08-08, visto en la consola). Se recoge y se dice en una línea.
try {
  for await (const m of corrida) {
    if (m.type === "assistant") {
      for (const b of m.message.content ?? []) {
        if (b.type === "text" && b.text.trim()) console.log(`\n[arquitecto] ${b.text.trim()}`);
        if (b.type === "tool_use") console.log(`  → ${b.name.replace("mcp__grafo__", "")}(${JSON.stringify(b.input)})`);
      }
    } else if (m.type === "result") {
      console.log(`\n${"=".repeat(60)}`);
      console.log(m.subtype === "success" ? "CORRIDA COMPLETA" : `TERMINÓ POR: ${m.subtype}`);
      console.log(`turnos: ${m.num_turns} · duración: ${Math.round(m.duration_ms / 1000)} s`);
    }
  }
} catch (e) {
  const msg = String(e?.message ?? e);
  console.log(`\n${"=".repeat(60)}`);
  console.log(/maximum number of turns/i.test(msg)
    ? `SE ACABARON LOS TURNOS (${TURNOS}). Lo aprendido hasta aquí queda en el grafo; su informe, en feedback-arquitecto.`
    : `LA CORRIDA SE CORTÓ: ${msg.split("\n")[0]}`);
  process.exitCode = 0;   // no es un fallo del sistema: es un presupuesto agotado
}
