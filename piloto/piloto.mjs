#!/usr/bin/env node
// EL PILOTO: el Agent SDK conduciendo el terreno de U por la puerta MCP.
//
// Es la F3 del plan (docs/plan-batch-sobre-nodos-vivos.md): un proceso pequeño que recibe una
// tarea en texto, se conecta al servidor MCP de U (127.0.0.1:8790) y la resuelve con las
// herramientas del mapa — con map_batch como ruta predilecta. No sabe de UIA, ni de selectores,
// ni de Windows: sabe pedir. Las manos, los vetos y el freno viven en U.
//
// LA MÉTRICA ES EL PUNTO. Antes: un viaje al modelo por cada clic, más los intentos fallidos.
// La tesis: el grueso de una tarea debe caber en 1-2 llamadas de map_batch, con el modelo
// interviniendo solo donde el terreno diverge de lo previsto. Por eso este arnés cuenta y
// enseña cada viaje: si la cuenta no baja, la tesis no vale — y hay que verlo, no suponerlo.
//
// Uso:  node piloto.mjs "entra al correo enviado a Jerónimo"
// Auth: la misma de Claude Code en esta máquina (el SDK la resuelve solo).

import { query } from "@anthropic-ai/claude-agent-sdk";

const tarea = process.argv.slice(2).join(" ").trim();
if (!tarea) {
  console.error('uso: node piloto.mjs "<tarea>"');
  process.exit(2);
}

// LA RUTA PREDILECTA, dicha una vez y como sistema. Reemplaza el prompt de Claude Code a
// propósito: este agente no programa — conduce un computador, y sus únicas manos son el mapa.
const SISTEMA = `Eres el piloto del computador de esta persona. Tu ÚNICA forma de actuar son las
herramientas mcp__u__* — el mapa del terreno que U ya aprendió. No tienes archivos, ni bash, ni
navegador propio: solo el mapa. Respondes en español, corto.

LA RUTA PREDILECTA ES map_batch. Predice los pasos de la tarea y mándalos JUNTOS en una sola
llamada: cada paso se pulsa solo si su elemento está VIVO en pantalla, así que no arriesgas nada
por predecir de más — el batch llega tan lejos como el terreno deje. Una llamada con cinco pasos
vale más que cinco llamadas de un paso.

CUANDO EL BATCH PARA A MEDIAS no es un fallo: su respuesta te dice cuántos pasos hizo, dónde
quedaste y qué SÍ está vivo ahí. Replanifica con ESO — no vuelvas a preguntar dónde estás ni qué
hay, ya te lo dijo. Manda el resto de los pasos en otro batch.

MIRA EL TERRENO ANTES DE ANDAR: map_ahead te dice qué habrá tras cada puerta cruzada —a qué
pantalla lleva y qué recuerda el mapa allí— sin tocar nada. Con esa predicción arma UN map_batch
hondo (los pasos de varias pantallas seguidas). Lo «por descubrir» no tiene promesa: ahí el batch
aprende yendo. La predicción es memoria; el batch verifica vivo a vivo igual.

Cómo trabajar:
- Empieza con map_where_am_i si no sabes dónde estás. Una vez. Luego map_ahead para planificar.
- Para abrir una app: map_open_app. Para una web: map_go_to con surface=«web://dominio».
- Navegar y accionar: map_batch. Un paso {"exit":"nombre tal como se ve"} pulsa; {"text":"..."}
  escribe en el campo con foco. Los nombres exactos salen de lo que las herramientas contestan.
- «Lo conozco pero AHORA no lo veo» significa FUERA DE LA VISTA: usa map_scroll
  (direction=abajo o arriba) y repite el MISMO batch — las listas largas esconden sus filas.
- map_what_i_see solo cuando de verdad no sepas qué hay — el batch ya te cuenta lo vivo al parar.
- No pidas permiso. No repitas una llamada idéntica más de dos veces: si falló dos, prueba otra
  vía o di qué te falta.
- Verifica por lo que las herramientas CONTESTAN, nunca por lo que pretendías.`;

let turnos = 0;
let llamadas = 0;
const porHerramienta = new Map();
const t0 = Date.now();

const recorte = (s, n) => (s.length > n ? s.slice(0, n) + "…" : s);

for await (const msg of query({
  prompt: tarea,
  options: {
    systemPrompt: SISTEMA,
    mcpServers: { u: { type: "http", url: "http://127.0.0.1:8790/mcp" } },
    allowedTools: ["mcp__u__*"],
    maxTurns: 15,
    // EL CLAUDE DE LA MÁQUINA, no el que trae el SDK en node_modules: la sesión de esta máquina ya
    // está autenticada con él, y el empaquetado intentó refrescar un OAuth que no era el suyo —
    // «OAuth session expired and could not be refreshed» (2026-08-24, primera corrida).
    pathToClaudeCodeExecutable: "C:\\Users\\felip\\.local\\bin\\claude.exe",
  },
})) {
  if (msg.type === "system" && msg.subtype === "init") {
    const u = (msg.mcp_servers ?? []).find((s) => s.name === "u");
    const delMapa = (msg.tools ?? []).filter((t) => t.startsWith("mcp__u__"));
    console.log(`· U: ${u?.status ?? "?"} · herramientas del mapa: ${delMapa.length}`);
  }

  if (msg.type === "assistant") {
    turnos++;
    for (const b of msg.message.content) {
      if (b.type === "text" && b.text.trim()) console.log(`\n[piloto] ${b.text.trim()}`);
      if (b.type === "tool_use") {
        llamadas++;
        porHerramienta.set(b.name, (porHerramienta.get(b.name) ?? 0) + 1);
        console.log(`  → ${b.name} ${recorte(JSON.stringify(b.input), 220)}`);
      }
    }
  }

  if (msg.type === "user") {
    const c = msg.message?.content;
    if (Array.isArray(c))
      for (const b of c) {
        if (b.type !== "tool_result") continue;
        const texto = Array.isArray(b.content)
          ? b.content.map((x) => x.text ?? "").join(" ")
          : String(b.content ?? "");
        console.log(`  ← ${recorte(texto.replace(/\s+/g, " ").trim(), 260)}`);
      }
  }

  if (msg.type === "result") {
    console.log(`\n════ resultado (${msg.subtype}) en ${((Date.now() - t0) / 1000).toFixed(1)}s ════`);
    if (msg.result) console.log(msg.result);
    console.log(`\nLA MÉTRICA · viajes al modelo: ${turnos} · llamadas a herramientas: ${llamadas}`);
    for (const [nombre, n] of porHerramienta) console.log(`  ${nombre}: ${n}`);
  }
}
