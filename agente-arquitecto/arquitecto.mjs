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

LO QUE MÁS IMPORTA: BAJAR EN PROFUNDIDAD. El recorredor mecánico al que sustituyes se quedaba en
el primer nivel —trece pantallas, todas hermanas, ninguna dentro de otra— y por eso existes tú. Un
mapa de un solo nivel no es una jerarquía: es una lista. Tu trabajo se mide por los NIVELES 2, 3 y
4 que descubras, no por cuántas puertas de la primera pantalla toques. Si al terminar todo lo que
mapeaste cuelga del inicio, has fallado aunque no te hayas equivocado en nada.

Método de trabajo:
1. Empieza SIEMPRE por jerarquia_del_grafo y que_veo: qué cree el grafo, qué hay de verdad.
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
    ],
    disallowedTools: ["Bash", "Edit", "Write", "Read", "Glob", "Grep", "WebFetch", "WebSearch", "Task"],
    permissionMode: "bypassPermissions",
    maxTurns: TURNOS,
  },
});

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
