// EL PILOTO (spec 013): UN agente Claude, UNA conversación, tres pasos.
//
//   node piloto.mjs --leccion=C:\Users\...\U\lecciones\leccion_20260906_1030
//   node piloto.mjs --encargo=C:\Users\...\U\encargos\encargo_20260910_1500   (spec 015)
//
// EN MODO ENCARGO no hay lección: la app deja solo `mensaje.json` con la nota marcada y el catálogo
// de skills comprobadas (con los datos que cada una necesita). El piloto ELIGE la skill por su
// criterio, arma los datos con lo que la nota trae y la corre con map_skill_run. Sin manos sueltas y
// sin preguntas (decisión del dueño, 2026-09-10): lo que la nota no trae queda en blanco.
//
// La app (U.exe) deja en esa carpeta `leccion.json` (la línea de tiempo de la demo) y
// `mensaje.json` (los bloques ya armados en orden: texto con t=MM:SS y las rutas de los cuadros;
// las dos cajas de herramientas; el modelo; la URL del MCP). Aquí no se decide qué cuadros van ni
// con qué etiqueta —eso lo juzga el contrato en C# (promesa 173)—; aquí se convierte en imágenes y
// se conversa.
//
// POR QUÉ NO SE RECICLA agente-arquitecto: el dueño lo pidió limpio («es una cosa vieja»). Se toma
// de allí solo lo que ya se midió: resumir por id de sesión (no continueConversation, que retomó la
// sesión del usuario en 2026-08-10) y bypassPermissions porque no hay consola que conteste.
//
// LAS MANOS SON LAS DE LA APP, por MCP en 127.0.0.1:8790. Este proceso solo aporta ver_momento:
// la pantalla de cualquier segundo de la demo, aunque ahí no hubiera clic (pedido del dueño,
// 2026-09-06: «yo puedo solo señalar algo y decir esta es la opción que usarías»).

import { readFileSync, existsSync } from "node:fs";
import { join, resolve } from "node:path";
import { query, tool, createSdkMcpServer } from "@anthropic-ai/claude-agent-sdk";
import { z } from "zod";

const arg = (n) => process.argv.find((a) => a.startsWith(`--${n}=`))?.slice(n.length + 3);
const MODO = arg("encargo") ? "encargo" : "leccion";
const CARPETA = arg("leccion") ?? arg("encargo");
if (!CARPETA) { console.error("uso: node piloto.mjs --leccion=<carpeta de la lección> | --encargo=<carpeta del encargo>"); process.exit(2); }

const leccion = MODO === "leccion"
  ? JSON.parse(readFileSync(join(CARPETA, "leccion.json"), "utf8"))
  : { Id: "", Eventos: [], Cuadros: [] };
const mensaje = JSON.parse(readFileSync(join(CARPETA, "mensaje.json"), "utf8"));
const MODELO = arg("model") ?? mensaje.Modelo ?? "claude-opus-5";
const MCP_URL = mensaje.Mcp ?? "http://127.0.0.1:8790/mcp/";
const TURNOS = parseInt(arg("turnos") ?? "120", 10);

// Una línea JSON por cosa que pasa: la app la lee y la muestra. Nunca prosa suelta en stdout.
const di = (tipo, texto = "", extra = {}) => console.log(JSON.stringify({ tipo, texto, ...extra }));

// ── Las imágenes: de ruta a bloque ──────────────────────────────────────────
function imagen(ruta) {
  const abs = resolve(ruta);
  if (!existsSync(abs)) return null;
  const mime = abs.toLowerCase().endsWith(".png") ? "image/png" : "image/jpeg";
  return { type: "image", source: { type: "base64", media_type: mime, data: readFileSync(abs).toString("base64") } };
}
function bloquesDelMensaje() {
  const out = [];
  for (const b of mensaje.Bloques ?? []) {
    if (b.Tipo === "text") out.push({ type: "text", text: b.Texto });
    else if (b.Tipo === "image") {
      const img = imagen(b.Ruta);
      if (img) { if (b.Texto) out.push({ type: "text", text: `[${b.Texto}]` }); out.push(img); }
      else out.push({ type: "text", text: `[${b.Texto}: el cuadro ${b.Ruta} no está en disco]` });
    }
  }
  return out;
}

// ── ver_momento: la pantalla de un segundo cualquiera de la demo ─────────────
const MMSS = (ms) => { const s = Math.max(0, Math.floor(ms / 1000)); return `${String(Math.floor(s / 60)).padStart(2, "0")}:${String(s % 60).padStart(2, "0")}`; };
function cuadroMasCercano(ms) {
  let mejor = null, dist = Infinity;
  for (const c of leccion.Cuadros ?? []) { const d = Math.abs(c.HoraMs - ms); if (d < dist) { dist = d; mejor = c; } }
  return mejor;
}
function segundosDe(texto) {
  const m = /^(\d{1,2}):(\d{2})(?:[.,](\d+))?$/.exec(String(texto).trim());
  if (m) return parseInt(m[1], 10) * 60 + parseInt(m[2], 10) + (m[3] ? parseFloat("0." + m[3]) : 0);
  const n = parseFloat(String(texto).replace(",", "."));
  return Number.isFinite(n) ? n : null;
}
function bloqueDeMomento(seg) {
  const c = cuadroMasCercano(Math.round(seg * 1000));
  if (!c) return [{ type: "text", text: "la lección no tiene cuadros grabados." }];
  const img = imagen(c.Ruta);
  const cabecera = `t=${MMSS(c.HoraMs)} (pediste ${MMSS(seg * 1000)}) · el ratón estaba en (${c.CursorX},${c.CursorY}) del cuadro, marcado con el anillo naranja.`;
  return img ? [{ type: "text", text: cabecera }, img] : [{ type: "text", text: cabecera + " (el archivo del cuadro ya no está)" }];
}
const laLeccion = createSdkMcpServer({
  name: "leccion",
  version: "1.0.0",
  alwaysLoad: true,
  tools: [
    tool("ver_momento",
      "MIRA LA PANTALLA DE UN MOMENTO EXACTO DE LA DEMO, aunque ahí no hubiera clic. Úsalo cuando la transcripción diga algo que necesitas ver: «esta opción», «aquí arriba», «este otro botón». El anillo naranja es dónde estaba el ratón: eso es lo que se señalaba. Devuelve el cuadro más cercano (se grabó uno cada 250 ms).",
      { momento: z.string().describe("El momento, como MM:SS o en segundos (p. ej. «00:33» o «33.5»).") },
      async ({ momento }) => {
        const s = segundosDe(momento);
        if (s == null) return { content: [{ type: "text", text: `no entiendo el momento «${momento}»: dame MM:SS o segundos.` }] };
        return { content: bloqueDeMomento(s) };
      }),
    tool("ver_alrededor",
      "TRES CUADROS ALREDEDOR DE UN MOMENTO (medio segundo antes, el momento, medio segundo después). Para seguir el ratón mientras alguien señalaba y hablaba.",
      { momento: z.string().describe("El momento central, como MM:SS o en segundos.") },
      async ({ momento }) => {
        const s = segundosDe(momento);
        if (s == null) return { content: [{ type: "text", text: `no entiendo el momento «${momento}».` }] };
        return { content: [...bloqueDeMomento(s - 0.5), ...bloqueDeMomento(s), ...bloqueDeMomento(s + 0.5)] };
      }),
  ],
});

// ── Quién es el piloto ───────────────────────────────────────────────────────
const SISTEMA_LECCION = `Eres Ü, el asistente que opera aplicaciones de escritorio en un hospital (SAP GUI IS-H). Hablas español, en primera persona y corto.

Te enseñaron una tarea con una demostración grabada. Te llega como LECCIÓN: una línea de tiempo con un solo reloj —cada clic con su hora y su punto, el cuadro de ANTES (el anillo naranja es el ratón: ahí se hizo clic) y el de DESPUÉS (la pantalla ya asentada), lo que la persona decía en ese momento, y a veces el selector que la aplicación reconoció—. EL VIDEO MANDA: los selectores son pistas, y si un selector y lo que ves en el cuadro no cuadran, manda el cuadro y lo dices. Si la transcripción menciona algo que no ves en ningún cuadro de clic («esta opción», «aquí arriba»), pide ese instante con ver_momento.

Tu trabajo es COMPROBAR la lección: entenderla, ENTREGAR EL PLAN, y tomar las manos solo si la app para.

1. Lee la lección entera. Di con voz_decir, en dos o tres frases, qué tarea es y de dónde a dónde va.
2. Mira dónde estás (map_where_am_i). Si no estás donde empieza la lección, ve hasta allí con map_take / map_go_to / map_open_app.
3. ENTREGA EL PLAN con leccion_plan: una lista, en orden, con un paso por evento de la lección que haga algo. Cada paso lleva «n» (el evento que cumple), «exit» (la PUERTA por su NOMBRE, tal como viene en el evento tras «puerta»; NUNCA el selector: el selector de una fila cambia con la lista y el nombre no; si el evento no trae puerta, mira con map_what_i_see y elige la que corresponde a lo que ves en el cuadro), «text» y «tecla» si el evento tecleó, «recuerdo» (qué es y para qué sirve ese elemento, con tus palabras, incluyendo lo que la persona dijo de él) y «decir» (una frase corta que Ü dirá antes de darlo). Dos clics seguidos en la misma puerta son UN paso. Los clics sobre la lista de pacientes o sobre una fila son un paso con la puerta de esa fila.
   La app recorre el plan por ti: dice, cuelga el recuerdo estando en su pantalla, da el paso y juzga la llegada. Te devuelve la cuenta.
4. MIRA CON map_shot cuando lo que hiciste NO cambia de pantalla: seleccionar una fila de una lista, marcar una casilla, escribir en un campo. La pantalla sigue llamándose igual, así que la foto es la única prueba de que salió. Si en la foto no pasó, corrígelo antes de seguir.
5. Si la app PARÓ en un paso, sigue tú desde ese paso con las manos, de uno en uno: mira (map_shot para ver la pantalla, map_what_i_see para las puertas por su nombre), actúa (map_take / map_type / map_scroll) PASANDO SIEMPRE «decir» (una frase corta) y «recuerdo» (qué es y para qué sirve): la app señala el elemento con la carita, lo dice, cuelga y muestra el recuerdo y solo entonces actúa, igual que en el plan; es la experiencia de siempre y no se omite. Y declara cada llegada con leccion_llegue(n), también en lo tecleado: la app LEE el campo y decide si dice lo que la demo tecleó. Corrige una vez; si no cuadra, dilo y sigue.
6. Si algo te queda ambiguo y de verdad cambia el plan, pregunta con voz_preguntar antes de entregarlo. No preguntes lo que puedas resolver mirando.
7. Al terminar llama leccion_guardar_skill(nombre corto en español, descripcion de cuándo usarla) y despídete con voz_decir en una frase.

Reglas: NUNCA uses map_batch ni map_skill_run: comprobar es de uno en uno con la app juzgando en medio. Lo que la demo TECLEÓ viaja en el plan tal cual, en «text» del paso que cumple ese evento: la app lo escribe en el mismo campo de la lección. Solo si un valor hace falta y la lección no lo trae, pregúntalo con voz_preguntar. Con las manos, map_type lleva en «target» la etiqueta del campo tal como la ves en map_what_i_see («Motivo de Consulta»), su nombre técnico («Y0000000-ZTRNOMPAC») o el selector de la lección. Si el siguiente paso graba o finaliza algo en el sistema real (Grabar, Finalizar, Guardar), NO lo pulses: dilo y para ahí. No anuncies el futuro más de una frase; no repitas la lección de vuelta; nunca digas «terminé» como si fuera un veredicto: el veredicto lo da la app.`;

// EL ENCARGO (spec 015, promesa 194): elegir una skill y correrla. Sin manos sueltas, sin preguntas.
const SISTEMA_ENCARGO = `Eres Ü, el asistente que opera aplicaciones de escritorio en un hospital (SAP GUI IS-H). Hablas español, en primera persona y corto.

Te llega un ENCARGO: el médico marcó secciones de su nota clínica para llevarlas a SAP, y tienes un CATÁLOGO de tareas que te enseñaron (skills), cada una con los DATOS que necesita. Tu trabajo es ELEGIR la skill que corresponde y CORRERLA con los datos de la nota. Eliges por tu propio criterio: lo que la skill hace (su nombre y su descripción) y los datos que pide, contra lo que la nota trae.

1. Lee el encargo y el catálogo. Elige UNA skill. Si ninguna corresponde, dilo con voz_decir en una frase y para: no inventes una tarea.
2. Di con voz_decir, en una frase, qué tarea vas a hacer.
3. Mira dónde estás (map_where_am_i). Cada skill empieza en una pantalla; si no estás en ella, ve con map_go_to (o map_open_app para traer SAP al frente). Si no llegas, dilo y para.
4. Arma «datos» para map_skill_run: un objeto JSON cuyas claves son EXACTAMENTE los nombres que el catálogo lista en «datos que necesita», y cuyos valores salen de la nota, en el formato que SAP espera (números sin unidades: «70», no «70 kg»; la tensión arterial son dos datos, sistólica y diastólica; una escala como Glasgow va por partes si la skill las pide por partes). Lo que la nota NO trae, NO va: ni lo preguntas ni lo inventas; ese campo queda en blanco y listo.
5. Llama map_skill_run(nombre, datos). La app señala cada campo con la carita, lo dice y lo escribe; te devuelve la cuenta: qué hizo, qué quedó en blanco, y si paró en algún paso.
6. Termina con voz_decir en una o dos frases: qué escribiste, qué quedó en blanco, y si algo paró. Grabar en SAP es de la persona, no tuyo.

Reglas: no tienes manos sueltas (map_take, map_type y map_batch no existen para ti) y no haces preguntas: lo que falta se queda en blanco. Una sola skill por encargo. Si map_skill_run paró, no lo repitas: cuenta dónde paró y por qué.`;

const SISTEMA = MODO === "encargo" ? SISTEMA_ENCARGO : SISTEMA_LECCION;

// UNA SOLA CAJA, con manos desde el principio (promesa 174, reescrita el 2026-09-06 tras la primera
// corrida: el piloto abrió SAP mientras «entendía» y el dueño dijo que era lo correcto — un recuerdo
// se cuelga en la pantalla donde estás, así que entender es IR). Lo permitido es una sugerencia; lo
// prohibido es la regla, porque el Agent SDK busca herramientas por su cuenta. Las dos listas las
// decide la app (C#), que es donde el contrato las juzga.
const SIN_ARCHIVOS = ["Bash", "Read", "Write", "Edit", "Glob", "Grep", "WebFetch", "WebSearch", "Task", "Agent", "NotebookEdit"];
const caja = mensaje.Caja ?? [];
const prohibidas = [...SIN_ARCHIVOS, ...(mensaje.Prohibidas ?? [])];
// TODAS LAS HERRAMIENTAS EN EL PROMPT DESDE EL TURNO 1. El Agent SDK las difiere detrás de una
// búsqueda (ToolSearch) y el piloto gastó 9 y luego 13 turnos buscando lo que ya tenía o lo que no
// existía (2026-09-06, medido dos veces). alwaysLoad las carga de entrada; el catálogo de la app son
// ~26 herramientas, cabe de sobra.
const servidores = { u: { type: "http", url: MCP_URL, alwaysLoad: true }, ...(MODO === "leccion" ? { leccion: laLeccion } : {}) };

async function conversar(prompt, allowedTools, disallowedTools, resume) {
  let sesion = resume ?? null, costo = 0, ultimo = "", fallo = "";
  const corrida = query({
    prompt,
    options: {
      model: MODELO,
      systemPrompt: SISTEMA,
      mcpServers: servidores,
      allowedTools,
      disallowedTools,
      permissionMode: "bypassPermissions",
      maxTurns: TURNOS,
      ...(resume ? { resume } : {}),
    },
  });
  for await (const m of corrida) {
    if (m.session_id) sesion = m.session_id;
    if (m.type === "assistant") {
      for (const b of m.message?.content ?? []) {
        if (b.type === "text" && b.text?.trim()) { ultimo = b.text.trim(); di("texto", ultimo, { sesion }); }
        else if (b.type === "tool_use") di("herramienta", b.name, { args: b.input, sesion });
      }
    } else if (m.type === "result") {
      costo = m.total_cost_usd ?? costo;
      fallo = m.is_error ? ((m.errors ?? []).join(" | ") || ultimo || m.subtype) : "";
      di("resultado", m.subtype, { sesion, costo, turnos: m.num_turns, ok: !m.is_error, denegadas: (m.permission_denials ?? []).map((d) => d.tool_name) });
    }
  }
  return { sesion, costo, ultimo, fallo };
}

// Un solo mensaje de usuario: la lección entera (texto y cuadros, en orden) o el encargo con su catálogo.
const CIERRE = MODO === "encargo"
  ? "Resuelve este encargo: elige la skill del catálogo que corresponde, ve a donde empieza, arma los datos con lo que la nota trae y córrela con map_skill_run. Lo que la nota no trae queda en blanco: no preguntes. Al final cuenta con voz_decir qué escribiste y qué quedó en blanco."
  : "Comprueba esta lección: di qué entendiste, ve a donde empieza, y ENTREGA EL PLAN con leccion_plan (un paso por evento, con puerta, recuerdo y qué decir). Solo si la app para, sigue tú con las manos desde ese paso. Al final, leccion_guardar_skill.";
async function* elMensaje() {
  yield {
    type: "user",
    parent_tool_use_id: null,
    message: {
      role: "user",
      content: [
        ...bloquesDelMensaje(),
        ...(faltan.length > 0 ? [{ type: "text", text: `AVISO: esta build de la aplicación no ofrece ${faltan.join(", ")}. No las busques: no están. Cuenta lo que entiendas por texto y sigue con las que sí hay.` }] : []),
        { type: "text", text: CIERRE },
      ],
    },
  };
}

// ── ANTES DE GASTAR UN TOKEN: ¿está la puerta de la app abierta? ─────────────
// El 2026-09-06, en seco, el piloto arrancó con el MCP caído y se pasó dos turnos —27 centavos—
// descubriendo que no tenía manos. Se comprueba antes, y se dice qué falta. Un agente sin manos no
// es un agente lento: es un agente que no puede hacer nada, y eso se sabe sin preguntárselo.
async function puertaDeLaApp() {
  try {
    const r = await fetch(MCP_URL, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ jsonrpc: "2.0", id: 1, method: "tools/list" }),
      signal: AbortSignal.timeout(5000),
    });
    const j = await r.json();
    return (j?.result?.tools ?? []).map((t) => t.name);
  } catch (e) { return null; }
}
const herramientasDeLaApp = await puertaDeLaApp();
if (herramientasDeLaApp === null) {
  di("error", `la puerta MCP de la app no contesta en ${MCP_URL}: sin ella el piloto no tiene manos ni voz. ¿Está U.exe abierta?`);
  process.exit(1);
}
const faltan = (mensaje.Caja ?? []).map((n) => n.split("__").pop())
  .filter((n) => n !== "ver_momento" && n !== "ver_alrededor" && !herramientasDeLaApp.includes(n));
if (faltan.length > 0)
  di("aviso", `la app contesta pero le faltan herramientas que esta lección necesita: ${faltan.join(", ")} (¿U.exe es de una build anterior a la spec 013?)`);

di("inicio", MODO === "encargo"
  ? `encargo · ${mensaje.Bloques?.length ?? 0} bloque(s) · ${caja.length} herramienta(s) · modelo ${MODELO}`
  : `lección «${leccion.Id}» · ${leccion.Eventos?.length ?? 0} evento(s) · ${leccion.Cuadros?.length ?? 0} cuadro(s) · modelo ${MODELO}`);
const e1 = await conversar(elMensaje(), caja, prohibidas, null);
if (e1.fallo) {
  const pista = /authenticat|OAuth|login|API key/i.test(e1.fallo)
    ? " · el piloto usa la sesión de Claude Code de esta máquina: corre «claude login» o pon ANTHROPIC_API_KEY en el entorno de U.exe"
    : "";
  di("error", `${MODO === "encargo" ? "el encargo" : "la comprobación"} falló: ${e1.fallo}${pista}`, { sesion: e1.sesion });
  process.exit(1);
}
di("fin", e1.ultimo, { sesion: e1.sesion, costo: e1.costo });
