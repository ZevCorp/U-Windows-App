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
// 160 y no 80: la primera corrida con las herramientas ya sanas se quedó sin turnos ANTES de
// poder escribir su informe, que es lo único que nos llevamos. Auditar de verdad —bajar una rama,
// volver, clasificar cuarenta salidas y contrastar— sale caro en turnos, y quedarse corto no
// significa medio informe: significa ninguno (2026-08-10, medido).
// El tercer argumento puede ser un número o una bandera («--chat»), así que se toma solo si es
// un número: con «--chat» salía «presupuesto NaN turnos» y el límite quedaba sin definir.
const TURNOS = /^\d+$/.test(process.argv[3] ?? "") ? parseInt(process.argv[3], 10) : 160;

// CONTINUAR LA CONVERSACIÓN ANTERIOR en vez de empezar de cero. Cuando se acaban los turnos, todo
// lo que el agente ya entendió de la app —qué es cromo, qué ya clasificó, por dónde iba— sigue en
// esa conversación; volver a empezar sería pagarlo otra vez y además llegar a conclusiones
// distintas. Con «continuar» retoma donde estaba y cierra con su informe.
const CONTINUAR = process.argv.includes("--continuar");

// HABLAR CON ÉL CUANDO TERMINA. Un informe contesta lo que el agente decidió contar; una
// conversación contesta lo que TÚ necesitas saber — «¿por qué pusiste Galería en nivel 1?»,
// «¿qué te faltó para cerrar?». Y él sigue teniendo delante la app y sus herramientas, así que
// puede ir a MIRAR en vez de recordar (2026-08-10, pedido por el usuario).
//
//   node arquitecto.mjs explorer.exe --chat            → audita y al terminar abre el turno de preguntas
//   node arquitecto.mjs explorer.exe --chat --solo-chat → solo preguntas, sobre la última corrida
const CHAT = process.argv.includes("--chat");
const SOLO_CHAT = process.argv.includes("--solo-chat");

// CON QUÉ MODELO PIENSA, elegido por quien lo lanza y no adivinado del SDK. Antes no se decía —el
// SDK elegía por su cuenta, y las primeras corridas salieron en opus-5 sin que nadie lo pidiera—.
// Sus conclusiones dependen del modelo tanto como del código: comparar dos auditorías sin saber
// cuál las escribió es comparar dos cosas distintas creyendo que son la misma. Se puede fijar con
// --model=sonnet (o el id completo, --model=claude-opus-5) para correr sin consola —scripts como
// noche-arquitecto.ps1—, y si no se fija y hay una consola de verdad delante, se pregunta siempre:
// no hay «el modelo del arquitecto», hay el que elegiste hoy.
const MODELOS = {
  opus: "claude-opus-5", sonnet: "claude-sonnet-5",
  haiku: "claude-haiku-4-5-20251001", fable: "claude-fable-5",
};
async function elegirModelo() {
  const bandera = process.argv.find((a) => a.startsWith("--model="))?.slice("--model=".length);
  if (bandera) return MODELOS[bandera.toLowerCase()] ?? bandera;

  // SIN CONSOLA NO HAY A QUIÉN PREGUNTARLE: sonnet-5 por defecto, más barato que opus para una
  // corrida desatendida, y no bloquear a quien lanzó esto desde un script o la sonda.
  if (!process.stdin.isTTY) return "claude-sonnet-5";

  const readline = await import("node:readline/promises");
  const rl = readline.createInterface({ input: process.stdin, output: process.stdout });
  console.log("¿Con qué modelo piensa el arquitecto?");
  console.log("  1) opus-5    — el más capaz, el más caro");
  console.log("  2) sonnet-5  — el de por defecto");
  console.log("  3) haiku-4.5 — el más rápido y barato");
  let r = "";
  try { r = (await rl.question("modelo [2] > ")).trim().toLowerCase(); } catch { /* stdin cerrado */ }
  rl.close();
  const porNumero = { "1": "opus", "2": "sonnet", "3": "haiku", "": "sonnet" };
  return MODELOS[porNumero[r] ?? r] ?? MODELOS.sonnet;
}
const MODELO = await elegirModelo();

// SU sesión, no «la última». `continueConversation` retoma la conversación más reciente del
// DIRECTORIO, y este directorio es el repo — donde el usuario también corre Claude Code. Al
// probar el turno de preguntas, el arquitecto retomó la sesión del usuario y contestó que no
// tenía ninguna herramienta del grafo, solo conectores de Gmail y Canva (2026-08-10, medido).
// Retomar la conversación de otro no es continuar: es suplantar. Se guarda el id de la suya y se
// resume por id, que es la única forma de saber que se habla con quien se cree.
import { readFileSync, writeFileSync, mkdirSync } from "node:fs";
import { dirname } from "node:path";
// Barras NORMALES, no invertidas: en un literal de JS «\U» y «\s» son escapes y se comen la
// barra, así que la ruta acababa siendo «C:U-versionessesiones…» y el archivo se escribía en un
// sitio inexistente sin quejarse (2026-08-10, medido). Node acepta «/» en Windows.
const MEMORIA = `C:/U-versiones/sesiones/arquitecto-${APP.replace(/[^a-z0-9.]/gi, "_")}.txt`;
function sesionGuardada() {
  try { return readFileSync(MEMORIA, "utf8").trim() || null; } catch { return null; }
}
function guardaSesion(id) {
  if (!id) return;
  try { mkdirSync(dirname(MEMORIA), { recursive: true }); writeFileSync(MEMORIA, id, "utf8"); } catch {}
}
const SESION = (CONTINUAR || SOLO_CHAT || CHAT) ? sesionGuardada() : null;

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

    t("fijar_nivel", "Declarar el nivel de una salida. ÚSALO POCO: el sistema deriva la estructura solo, y tu declaración no sube la cobertura — si el bronce no la sostiene, sale marcada como «declarada sin evidencia», que es lo contrario de haber avanzado. Resérvalo para lo que has COMPROBADO cruzando y el cálculo aún no puede ver. NO muevas lo que declaró una persona: si discrepas, dilo con feedback.",
      {
        salida: z.string().describe("nombre de la salida O SU SELECTOR (uia:name=X;ct=TreeItem). Usa el SELECTOR siempre que el nombre se repita en la app: por nombre se aplica a TODAS las apariciones a la vez"),
        nivel: z.number().int().min(1).max(6),
        cromo: z.boolean().optional(),
      },
      (a) => sonda("map_set_level", { app: APP, exit: a.salida, level: String(a.nivel), ...(a.cromo === undefined ? {} : { cromo: String(a.cromo) }) })),

    t("cuanto_entiende", "TU CRITERIO DE TERMINADO: cuánto entiende el sistema por sí solo (cobertura derivada), qué puertas siguen SIN CRUZAR —esa es tu lista de trabajo real— y en qué discrepan el cálculo y lo declarado. Empieza y termina aquí.",
      {}, () => sonda("map_silver", { app: APP })),

    t("sin_situar", "Lo que nadie ha DECLARADO todavía. Es información, no tu meta: se vacía escribiendo niveles, y escribir no es entender. Para saber si avanzas, mira «cuanto_entiende».",
      {}, () => sonda("map_unsituated", { app: APP })),

    tFoto("mirar", "Una FOTO de la ventana que hay delante. Úsala cuando los nombres no basten para decidir qué es navegación y qué es contenido: un panel lateral se reconoce de un vistazo."),

    t("marcar_atras", "Declarar cuál es el gesto de VOLVER de esta app (el botón «Atrás»). No es una puerta: es historial, y sin marcarlo cada vuelta acuña una arista falsa que ensucia la estructura.",
      { salida: z.string().describe("nombre del botón de volver, tal como se ve") },
      (a) => sonda("map_learn_back", { app: APP, exit: a.salida })),

    t("marcar_accion", "Clasificar una salida como ACCIÓN y no como navegación: hace algo (guardar, copiar, ordenar, crear) pero no lleva a otra pantalla. NO se borra del mapa — el asistente la necesitará para EJECUTAR; solo deja de contar como estructura. Acepta nombre o SELECTOR; usa el selector si el nombre se repite.",
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

TU TRABAJO ES LLEVAR ESTA APP DE BRONCE A PLATA, y esas dos palabras cambiaron de significado el
2026-08-10. Léelas otra vez aunque creas que las sabes:
  · BRONCE es lo observado en crudo: pantallas, puertas, tipos, y si cada puerta se llegó a cruzar.
  · PLATA es lo que el sistema DERIVA de ese bronce y puede sostener con evidencia — qué es
    mobiliario porque está en 9 de 11 pantallas, qué es relativo porque lleva a dos sitios
    distintos, qué es contenido porque tiene cuarenta hermanos iguales, y a qué profundidad vive
    cada pantalla.

PLATA NO ES LO QUE TÚ DECLARES. Esto es lo importante y es lo que ha cambiado: antes tu trabajo era
ponerle nivel a todo, y con eso el sistema PARECÍA entender la app mientras seguía sin entenderla.
Declarar mueve un número; no enseña nada a nadie. Ahora la estructura la calcula el sistema, y tu
trabajo es DARLE EL MATERIAL QUE LE FALTA Y DECIRLE DÓNDE SE EQUIVOCA.

CÓMO SE MIDE QUE HAS TERMINADO, y no es una opinión: la herramienta «cuanto_entiende» da la
COBERTURA DERIVADA —qué parte de la app se sostiene con evidencia— y la lista de puertas SIN CRUZAR.
Empiezas ahí y terminas ahí. Esa cobertura sube de dos maneras, las dos honestas:
  1. CRUZANDO puertas que nadie ha cruzado. Es tu trabajo principal: cada puerta que abres convierte
     una incógnita en un hecho, y ninguna otra cosa que hagas vale tanto.
  2. REPORTANDO con feedback dónde el cálculo se equivoca, con el caso concreto delante.

Y no sube declarando. Si declaras algo que el bronce no sostiene, aparece marcado como «declarada
sin evidencia» — o sea, contado como deuda, no como avance. Usa «fijar_nivel» solo para lo que hayas
COMPROBADO cruzando y el cálculo todavía no pueda ver.

Los DESACUERDOS entre el cálculo y lo declarado son tu material más valioso: cada uno es o una regla
que hay que mejorar o una declaración que estaba mal. Míralos uno a uno y explica en el feedback de
qué lado está la razón.

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
0. Empieza por «cuanto_entiende» (tu criterio y tu lista de puertas sin cruzar) y «mirar» (una foto).
   Los nombres solos engañan: un panel lateral y una lista de archivos son indistinguibles en texto
   y obvios en una imagen. Y marca pronto el gesto de volver con «marcar_atras» — cada vuelta sin
   marcar ensucia el grafo.
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
6. Contrasta contra el CÁLCULO, no contra el vacío: vuelve a «cuanto_entiende» cada pocas puertas.
   ¿Lo que deriva como mobiliario es de verdad transversal? ¿Hay algo transversal que el cálculo no
   ve, y por qué —cuántas pantallas te faltan por visitar para que lo vea—? Cada desacuerdo que
   aparezca, míralo: o la regla se equivoca (feedback) o la declaración estaba mal. Lo que dijo una
   persona no se toca; si discrepas, feedback.
7. Cada desajuste real va a feedback en el momento, concreto: qué esperabas, qué hay, por qué
   importa. Nada de «todo bien» genérico. Y si una REGLA de derivación falla, es el hallazgo más
   valioso que puedes traer: dilo con el caso delante (qué salida, qué dice el cálculo, qué es en
   realidad y cómo lo comprobaste). Arreglar una regla vale por cien declaraciones.
8. Cierra SIEMPRE con un feedback final: la cobertura con la que empezaste y con la que terminas,
   cuántas puertas cruzaste, la arquitectura real de la app en forma de árbol, hasta qué
   profundidad llegaste, y los 3 desajustes más importantes. Si la cobertura no subió, dilo: una
   corrida honesta que no avanzó enseña más que un informe que dice que todo está bien.

Límites duros: no puedes editar código ni archivos —no tienes herramientas para ello—, solo
organizar el grafo y reportar. Si la app se cierra o algo se cruza, dilo en feedback y termina.
Trabaja en español.`;

// LAS MISMAS HERRAMIENTAS EN LOS DOS MODOS. Si el turno de preguntas le diera menos, contestaría
// de memoria donde podría ir a mirar — y una respuesta recordada vale menos que una comprobada.
const PERMITIDAS = [
  "mcp__grafo__donde_estoy", "mcp__grafo__que_veo", "mcp__grafo__cruzar",
  "mcp__grafo__ir_a", "mcp__grafo__jerarquia_del_grafo", "mcp__grafo__rutas_desde",
  "mcp__grafo__fijar_nivel", "mcp__grafo__feedback",
  "mcp__grafo__cuanto_entiende", "mcp__grafo__sin_situar", "mcp__grafo__mirar",
  "mcp__grafo__marcar_atras", "mcp__grafo__marcar_accion",
];
const PROHIBIDAS = ["Bash", "Edit", "Write", "Read", "Glob", "Grep", "WebFetch", "WebSearch", "Task"];

// ── A correr ─────────────────────────────────────────────────────────────────
console.log(`ARQUITECTO sobre «${APP}» · presupuesto ${TURNOS} turnos · modelo elegido: ${MODELO}`
  + `${CONTINUAR ? " · CONTINÚA la corrida anterior" : ""}\n`);

const corrida = query({
  // Al continuar, la instrucción no es «audita» —eso ya lo estaba haciendo— sino «cierra». Lo
  // único que nos llevamos de una auditoría es el informe, así que retomar sin pedirlo
  // explícitamente arriesga gastar los turnos nuevos en seguir explorando y quedarse otra vez sin
  // escribirlo.
  prompt: CONTINUAR
    ? `Se te acabaron los turnos y te doy más. Retoma donde estabas con «${APP}»: mira `
      + `«cuanto_entiende», cruza las puertas que más suban la cobertura y CIERRA con tu informe `
      + `final en feedback. El informe es lo único que nos llevamos: escríbelo aunque la cobertura `
      + `se haya quedado corta, y di con cuál terminaste.`
    : `Audita la jerarquía de «${APP}». La app ya está abierta y la sonda viva.`,
  options: {
    systemPrompt: MISION,
    mcpServers: { grafo: herramientas },
    allowedTools: PERMITIDAS,
    disallowedTools: PROHIBIDAS,
    permissionMode: "bypassPermissions",
    maxTurns: TURNOS,
    model: MODELO,
    ...(CONTINUAR && SESION ? { resume: SESION } : {}),
  },
});

// AGOTAR LOS TURNOS NO ES UN FALLO, es el presupuesto haciendo su trabajo — pero el SDK lo lanza
// como excepción, y sin capturarla Node imprime su propio código minificado entero encima del
// informe que acabas de leer (2026-08-08, visto en la consola). Se recoge y se dice en una línea.
if (!SOLO_CHAT) try {
  for await (const m of corrida) {
    // EL ID SE GUARDA EN CUANTO APARECE, no al final: si la corrida se queda sin turnos o revienta,
    // el final puede no llegar — y entonces se pierde justo lo que permite retomarla.
    if (m.session_id) guardaSesion(m.session_id);

    // CON QUÉ MODELO ESTÁ PENSANDO, dicho por él y no por nosotros. Sus conclusiones dependen del
    // modelo tanto como del código, así que comparar dos auditorías sin saber cuál las escribió es
    // comparar dos cosas distintas creyendo que son la misma (2026-08-10, pedido por el usuario).
    // Se lee del mensaje de arranque del propio SDK: escribirlo a mano seria repetir un dato que
    // el sistema ya sabe, y repetirlo es firmar que algun dia dira una cosa por otra.
    if (m.type === "system" && m.subtype === "init") {
      const extra = [m.model, m.permissionMode && `permisos: ${m.permissionMode}`]
        .filter(Boolean).join(" · ");
      if (extra) console.log(`modelo: ${extra}
`);
    }
    if (m.type === "assistant") {
      for (const b of m.message.content ?? []) {
        if (b.type === "text" && b.text.trim()) console.log(`\n[arquitecto] ${b.text.trim()}`);
        if (b.type === "tool_use") console.log(`  → ${b.name.replace("mcp__grafo__", "")}(${JSON.stringify(b.input)})`);
      }
    } else if (m.type === "result") {
      console.log(`\n${"=".repeat(60)}`);
      console.log(m.subtype === "success" ? "CORRIDA COMPLETA" : `TERMINÓ POR: ${m.subtype}`);
      console.log(`turnos: ${m.num_turns} · duración: ${Math.round(m.duration_ms / 1000)} s`);
      guardaSesion(m.session_id);   // para poder retomar ESTA conversación, y no la de otro
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

// ── EL TURNO DE PREGUNTAS ────────────────────────────────────────────────────
// Un informe contesta lo que el agente decidió contar. Una conversación contesta lo que TÚ
// necesitas saber, que casi nunca coincide: «¿por qué Galería en nivel 1?», «¿qué te faltó?»,
// «¿estás seguro de que Compartido no tiene pantalla propia?».
//
// Y no responde de memoria: mantiene TODAS sus herramientas, así que puede ir a mirar la app otra
// vez para contestarte. Es la diferencia entre preguntarle a un informe y preguntarle a alguien
// que sigue delante del sitio.
async function turnoDePreguntas() {
  const readline = await import("node:readline/promises");
  const rl = readline.createInterface({ input: process.stdin, output: process.stdout });

  console.log(`\n${"=".repeat(60)}`);
  console.log("PREGÚNTALE AL ARQUITECTO. Sigue con la app delante y sus herramientas puestas,");
  console.log("así que puede ir a comprobar en vez de recordar. Enter vacío o «salir» para cerrar.\n");

  for (;;) {
    let q = "";
    try { q = (await rl.question("tú > ")).trim(); } catch { break; }
    if (q.length === 0 || /^(salir|exit|quit|q)$/i.test(q)) break;

    try {
      // 30 turnos por pregunta: suficiente para que vaya a mirar y vuelva, corto para que no se
      // enrede en otra auditoría entera cuando solo se le pidió una aclaración.
      const respuesta = query({
        prompt: q,
        options: {
          systemPrompt: MISION,
          mcpServers: { grafo: herramientas },
          allowedTools: PERMITIDAS,
          disallowedTools: PROHIBIDAS,
          permissionMode: "bypassPermissions",
          maxTurns: 30,
          model: MODELO,
          ...(sesionGuardada() ? { resume: sesionGuardada() } : {}),
        },
      });
      for await (const m of respuesta) {
        if (m.type !== "assistant") continue;
        for (const b of m.message.content ?? []) {
          if (b.type === "text" && b.text.trim()) console.log(`\n[arquitecto] ${b.text.trim()}\n`);
          if (b.type === "tool_use") console.log(`  → ${b.name.replace("mcp__grafo__", "")}(${JSON.stringify(b.input)})`);
        }
      }
    } catch (e) {
      console.log(`\n[no pude contestar: ${String(e?.message ?? e).split("\n")[0]}]\n`);
    }
  }
  rl.close();
  console.log("\nHasta luego.");
}

if (CHAT) await turnoDePreguntas();
