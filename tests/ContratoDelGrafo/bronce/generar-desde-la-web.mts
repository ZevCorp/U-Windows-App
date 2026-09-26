// LOS VECTORES DE MIRACLE NOTES (spec 055): lo que la WEB contesta, congelado, para que Windows
// conteste lo mismo.
//
// POR QUÉ ASÍ Y NO COPIANDO LOS TESTS DE LA WEB A MANO: «funciona igual que la web» es una frase
// hasta que alguien ejecuta la web. Este guion ejecuta el TypeScript REAL de
// Pagina-web-clientes-final sobre cada caso y guarda la salida; el contrato exige al puerto en C#
// la misma salida, carácter a carácter. Si la web cambia una regla, se regenera y el contrato dice
// qué dejó de cuadrar en Windows.
//
// Se corre así (desde cualquier sitio, con tsx y la web con sus node_modules):
//   WEB=/ruta/a/Pagina-web-clientes-final TSX_TSCONFIG_PATH=$WEB/tsconfig.json \
//     npx tsx tests/ContratoDelGrafo/bronce/generar-desde-la-web.mts > tests/ContratoDelGrafo/bronce/miracle-notes-web.json

import { execSync } from "node:child_process";

const WEB = process.env.WEB;
if (!WEB) throw new Error("falta WEB=/ruta/a/Pagina-web-clientes-final");

const review = await import(`${WEB}/lib/clinical/note-review.ts`);
const conceptos = await import(`${WEB}/lib/clinical/vital-concepts.ts`);
const voz = await import(`${WEB}/lib/clinical/voice-instruction.ts`);
const barra = await import(`${WEB}/lib/clinical/slash-trigger.ts`);
const huecos = await import(`${WEB}/lib/clinical/placeholders.ts`);
const insertar = await import(`${WEB}/lib/clinical/insert-text.ts`);
const atajos = await import(`${WEB}/lib/clinical/snippets.ts`);
const buscar = await import(`${WEB}/lib/clinical/search.ts`);
const plantillas = await import(`${WEB}/lib/clinical/template-preferences.ts`);
const texto = await import(`${WEB}/lib/clinical/note-plain-text.ts`);

// ── notas de ejemplo ─────────────────────────────────────────────────────────

const LARGA = "El paciente refiere dolor torácico opresivo desde hace tres días, ".repeat(8);
const plantillaGeneral = {
  template_id: "t-general", name: "Consulta general", specialty: "medicina_general",
  sections: [
    { key: "motivo", label: "Motivo de consulta", required: true },
    { key: "enfermedad", label: "Enfermedad actual", required: true },
    { key: "antecedentes", label: "Antecedentes", required: false },
    { key: "examen", label: "Examen físico", required: false },
    { key: "analisis", label: "Análisis", required: false },
  ],
};
const cierreCompleto = {
  plan: {
    medications: [{ name: "Acetaminofén", dose: "500 mg", route: "VO", frequency: "cada 8 horas", duration: "3 días", instructions: "" }],
    non_pharmacological: [{ text: "Reposo relativo" }],
    follow_up: [{ text: "Control en 8 días" }],
  },
  recommendations: [{ text: "Hidratación abundante" }],
  alarm_signs: [{ text: "Dolor que no cede" }],
};
function nota(parcial: Record<string, unknown>) {
  return { summary: "Paciente con dolor torácico atípico, estable.", sections: [], warnings: [], missing_required_sections: [], ...parcial };
}
const seccionesCompletas = [
  { key: "motivo", label: "Motivo de consulta", content: "Dolor en el pecho desde hace tres días", confidence: 0.9 },
  { key: "enfermedad", label: "Enfermedad actual", content: "Dolor opresivo, no irradiado, sin disnea. Niega alergias conocidas.", confidence: 0.85 },
  { key: "antecedentes", label: "Antecedentes", content: "Hipertensión arterial en tratamiento con losartán.", confidence: 0.8 },
  { key: "examen", label: "Examen físico", content: "TA 130/85, FC 78, FR 16, temperatura 36,8, saturación 97%.", confidence: 0.9 },
  { key: "analisis", label: "Análisis", content: "Dolor torácico atípico de bajo riesgo.", confidence: 0.75 },
];

const casosRevision: { nombre: string; note: unknown; template: unknown; transcript: string }[] = [
  { nombre: "sin nota", note: null, template: plantillaGeneral, transcript: "" },
  { nombre: "nota completa", note: nota({ sections: seccionesCompletas, discharge: cierreCompleto }), template: plantillaGeneral, transcript: LARGA },
  { nombre: "obligatoria vacía", note: nota({ sections: seccionesCompletas.map((s) => (s.key === "motivo" ? { ...s, content: "" } : s)), discharge: cierreCompleto }), template: plantillaGeneral, transcript: LARGA },
  { nombre: "obligatoria que ni vino", note: nota({ sections: seccionesCompletas.filter((s) => s.key !== "enfermedad"), discharge: cierreCompleto }), template: plantillaGeneral, transcript: LARGA },
  { nombre: "relleno cuenta como vacío y la negación no", note: nota({ sections: seccionesCompletas.map((s) => (s.key === "antecedentes" ? { ...s, content: "Sin información documentada." } : s.key === "analisis" ? { ...s, content: "No refiere alergias" } : s)), discharge: cierreCompleto }), template: plantillaGeneral, transcript: LARGA },
  { nombre: "opcionales vacías y sin resumen", note: nota({ summary: "  ", sections: seccionesCompletas.map((s) => (s.key === "antecedentes" || s.key === "examen" ? { ...s, content: "N/A" } : s)), discharge: cierreCompleto }), template: plantillaGeneral, transcript: LARGA },
  { nombre: "muchas opcionales vacías", note: nota({ sections: [
      ...seccionesCompletas.slice(0, 2),
      ...["a", "b", "c", "d", "e", "f"].map((k) => ({ key: k, label: `Sección ${k.toUpperCase()}`, content: "pendiente" })),
    ], discharge: cierreCompleto }), template: plantillaGeneral, transcript: LARGA },
  { nombre: "confianza baja", note: nota({ sections: seccionesCompletas.map((s) => (s.key === "examen" ? { ...s, confidence: 0.3 } : s)), discharge: cierreCompleto }), template: plantillaGeneral, transcript: LARGA },
  { nombre: "breve con consulta larga", note: nota({ sections: seccionesCompletas.map((s) => (s.key === "analisis" ? { ...s, content: "Dolor atípico leve hoy" } : s)), discharge: cierreCompleto }), template: plantillaGeneral, transcript: LARGA },
  { nombre: "breve con consulta corta", note: nota({ sections: seccionesCompletas.map((s) => (s.key === "analisis" ? { ...s, content: "Dolor atípico leve hoy" } : s)), discharge: cierreCompleto }), template: plantillaGeneral, transcript: "corta" },
  { nombre: "un dato suelto no es breve", note: nota({ sections: seccionesCompletas.map((s) => (s.key === "analisis" ? { ...s, content: "26-2513" } : s)), discharge: cierreCompleto }), template: plantillaGeneral, transcript: LARGA },
  { nombre: "sin plan", note: nota({ sections: seccionesCompletas, discharge: { plan: { medications: [], non_pharmacological: [], follow_up: [] }, recommendations: [], alarm_signs: [] } }), template: plantillaGeneral, transcript: LARGA },
  { nombre: "plan sin control ni alarma", note: nota({ sections: seccionesCompletas, discharge: { ...cierreCompleto, plan: { ...cierreCompleto.plan, follow_up: [] }, alarm_signs: [] } }), template: plantillaGeneral, transcript: LARGA },
  { nombre: "sin cierre del todo (nota vieja)", note: nota({ sections: seccionesCompletas }), template: plantillaGeneral, transcript: LARGA },
  { nombre: "medicamento sin dosis y sin nombre", note: nota({ sections: seccionesCompletas, discharge: { ...cierreCompleto, plan: { ...cierreCompleto.plan, medications: [
      { name: "Ibuprofeno", dose: "", route: "VO", frequency: "cada 8 horas", duration: "", instructions: "" },
      { name: "", dose: "1 tableta", route: "", frequency: "", duration: "", instructions: "" },
    ] } } }), template: plantillaGeneral, transcript: LARGA },
  { nombre: "sin signos vitales con consulta larga", note: nota({ sections: seccionesCompletas.map((s) => (s.key === "examen" ? { ...s, content: "Paciente alerta, orientado, sin dificultad respiratoria." } : s)), discharge: cierreCompleto }), template: plantillaGeneral, transcript: LARGA },
  { nombre: "prescribe sin mencionar alergias", note: nota({ sections: seccionesCompletas.map((s) => (s.key === "enfermedad" ? { ...s, content: "Dolor opresivo, no irradiado, sin disnea asociada." } : s)), discharge: cierreCompleto }), template: plantillaGeneral, transcript: LARGA },
  { nombre: "la alergia se dijo en la consulta", note: nota({ sections: seccionesCompletas.map((s) => (s.key === "enfermedad" ? { ...s, content: "Dolor opresivo, no irradiado, sin disnea asociada." } : s)), discharge: cierreCompleto }), template: plantillaGeneral, transcript: `${LARGA} Es alérgico a la penicilina.` },
  { nombre: "informe de patología", note: nota({ sections: [{ key: "descripcion", label: "Descripción macroscópica", content: "Fragmento de tejido de 2 x 1 cm, pardo, blando." }] }), template: { template_id: "t-pato", name: "Informe de patología", specialty: "patologia", sections: [{ key: "descripcion", label: "Descripción macroscópica", required: true }] }, transcript: LARGA },
  { nombre: "avisos del backend y obligatoria que dijo el backend", note: nota({ sections: seccionesCompletas.map((s) => (s.key === "antecedentes" ? { ...s, content: "" } : s)), discharge: cierreCompleto, warnings: ["Revisa la dosis", "  ", "No se oyó la edad"], missing_required_sections: ["antecedentes"] }), template: plantillaGeneral, transcript: LARGA },
  { nombre: "sin nombre de plantilla", note: nota({ sections: seccionesCompletas.map((s) => (s.key === "motivo" ? { ...s, content: "" } : s.key === "examen" ? { ...s, content: "" } : s)), discharge: cierreCompleto }), template: { ...plantillaGeneral, name: "" }, transcript: LARGA },
  { nombre: "sin plantilla", note: nota({ sections: seccionesCompletas.map((s) => (s.key === "examen" ? { ...s, content: "" } : s)), discharge: cierreCompleto }), template: null, transcript: "" },
  { nombre: "sección sin etiqueta usa su clave", note: nota({ sections: [...seccionesCompletas, { key: "notas_extra", label: "", content: "" }], discharge: cierreCompleto }), template: plantillaGeneral, transcript: LARGA },
];

const revisiones = casosRevision.map((c) => {
  const r = review.reviewGeneratedNote({ note: c.note, template: c.template, transcript: c.transcript });
  const reparto = review.splitReviewFindings(r, 3);
  return {
    nombre: c.nombre,
    entrada: { note: c.note, template: c.template, transcript: c.transcript },
    salida: {
      hallazgos: r.hallazgos, criticos: r.criticos, advertencias: r.advertencias, sugerencias: r.sugerencias,
      puntaje: review.noteReviewScore(r),
      principales: reparto.principales.map((h: { key: string }) => h.key),
      plegados: reparto.plegados.map((h: { key: string }) => h.key),
      etiqueta: review.noteReviewLabel(r),
    },
  };
});

// ── signos vitales y conceptos ───────────────────────────────────────────────

const textosVitales = [
  "Talla 1.70 m, peso 68 kg.",
  "Talla de 170 cm. Peso: 70,5 kg.",
  "Estatura 1,65. Peso 57. Temperatura 36",
  "FC: 88 x min, FR 18, temp. 37,5",
  "Frecuencia cardíaca 90 lpm y frecuencia respiratoria 20.",
  "Pulso 72. SpO2 95%",
  "Saturación de oxígeno 97 por ciento",
  "Temperatura 896",
  "TA 120/80 mmHg",
  "Tensión arterial 130 sobre 85",
  "presión arterial 80/120",
  "Paciente de 68 años, fc 300",
  "sat O2 91",
  "Peso 57. Temperatura 36.",
  "Sin datos de signos vitales.",
  "",
];
const vitales = textosVitales.map((t) => ({ entrada: t, salida: conceptos.extractConcepts([{ content: t, label: "Examen físico", key: "examen" }]) }));
vitales.push({
  entrada: "(secciones) motivo por su nombre",
  // El motivo se lee de la sección por su nombre, no del texto: se congela aparte.
  salida: conceptos.extractConcepts([
    { key: "motivo", label: "Motivo de consulta", content: "  Dolor de cabeza intenso  " },
    { key: "examen", label: "Examen físico", content: "TA 140/90" },
  ]),
} as never);

// ── voz ──────────────────────────────────────────────────────────────────────

const dictados = [
  "quiero que diga: control en ocho días",
  "Quiero que diga así, paciente estable",
  "que quede lo siguiente: reposo por tres días",
  "textualmente paciente estable sin cambios",
  "escribe literal: dieta blanda",
  "Tal cual, sin fiebre",
  "escribe esto: control en un mes",
  "Anota lo siguiente, reposo relativo",
  "agrega que el paciente niega fiebre",
  "Añade que trae exámenes de laboratorio",
  "agrega que diga: control en ocho días",
  "incluye que la madre refiere tos",
  "hazla más corta",
  "ordena esto por fechas",
  "quiero que diga",
  "",
  "   ",
  "pon que diga lo siguiente: hidratación abundante",
];
const voces = dictados.map((d) => ({ entrada: d, salida: voz.parseVoiceInstruction(d) }));
const literales = [
  ["", "Control en ocho días"],
  ["No referido en la consulta.", "Control en ocho días"],
  ["No se menciona.", "Control en ocho días"],
  ["Sin información documentada", "Control en ocho días"],
  ["Paciente estable.", "Control en ocho días"],
  ["Paciente estable", "Control en ocho días"],
  ["Paciente estable:", "Control"],
  ["Paciente estable   ", "Control"],
  ["Paciente estable", "   "],
  ["Pendiente de resultados", "Control"],
].map(([actual, dictado]) => ({ entrada: { actual, dictado }, salida: voz.aplicarDictadoLiteral(actual, dictado) }));

// ── atajos ───────────────────────────────────────────────────────────────────

const barras = [
  ["/", 1], ["/pla", 4], ["hola /pla", 9], ["hola /pla", 7], ["120/80", 6], ["s/p", 3],
  ["a /b c", 6], ["a /b c", 4], ["línea\n/dx", 9], ["/", 0], ["x", 5],
  ["/" + "a".repeat(40), 41], ["/" + "a".repeat(41), 42], ["uno  /dos", 9],
].map(([valor, caret]) => ({ entrada: { valor, caret }, salida: barra.slashQueryAt(valor as string, caret as number) }));

const textosConHuecos = [
  "Amoxicilina [dosis] cada [frecuencia] por ___ días",
  "Sin huecos aquí.",
  "__ dos no bastan, ____ cuatro sí",
  "[" + "x".repeat(41) + "] demasiado largo, [ok]",
  "[a\nb] no cruza líneas",
];
const huecosV = textosConHuecos.map((t) => ({
  entrada: t,
  salida: {
    todos: huecos.findPlaceholders(t),
    siguienteDesde10: huecos.nextPlaceholderAfter(t, 10),
    primeroEn0a25: huecos.firstPlaceholderIn(t, 0, 25),
  },
}));

const inserciones = [
  ["", 0, 0, "Control en 8 días"],
  ["Paciente estable", 16, 16, "Control"],
  ["Paciente estable ", 17, 17, "Control"],
  ["Paciente estable\n", 17, 17, "Control"],
  ["Paciente /con", 9, 13, "Control en ___ días"],
  ["abc", 10, 2, "X"],
  ["abc", -3, 1, "X"],
].map(([valor, desde, hasta, t]) => ({ entrada: { valor, desde, hasta, texto: t }, salida: insertar.insertSnippetText(valor as string, desde as number, hasta as number, t as string) }));

const catalogo = [
  { id: "1", title: "Control en 8 días", content: "Control en 8 días con resultados", category: "Plan", updatedAt: "2026-09-01T10:00:00Z" },
  { id: "2", title: "Examen físico normal", content: "Paciente alerta, orientado", category: "Examen físico", updatedAt: "2026-09-02T10:00:00Z" },
  { id: "3", title: "Plan de hidratación", content: "Suero oral a libre demanda", category: "Plan y dosis por peso", updatedAt: "2026-09-03T10:00:00Z" },
  { id: "4", title: "Dieta blanda", content: "Dieta blanda por 3 días, control", category: "Recomendaciones", updatedAt: "2026-09-03T10:00:00Z" },
  { id: "5", title: "Ñandú prueba", content: "texto", category: "", updatedAt: "2026-09-01T10:00:00Z" },
  { id: "6", title: "Análisis de riesgo", content: "Riesgo cardiovascular bajo", category: "Análisis e impresión diagnóstica", updatedAt: "2026-09-04T10:00:00Z" },
  { id: "7", title: "Pediatría control niño sano", content: "Crecimiento adecuado", category: "Plan", updatedAt: "2026-09-04T10:00:00Z" },
];
const busquedas = [
  { query: "", sectionTitle: "" },
  { query: "", sectionTitle: "Plan y educación al cuidador" },
  { query: "control", sectionTitle: "" },
  { query: "Control", sectionTitle: "Recomendaciones" },
  { query: "hidrat", sectionTitle: "" },
  { query: "examen", sectionTitle: "Examen físico" },
  { query: "pediatria", sectionTitle: "" },
  { query: "pedriatria", sectionTitle: "" },
  { query: "orientado", sectionTitle: "" },
  { query: "diagnostica", sectionTitle: "" },
  { query: "nada que ver", sectionTitle: "" },
  { query: "ñandu", sectionTitle: "" },
];
const filtros = busquedas.map((b) => ({ entrada: b, salida: atajos.filterSnippets(catalogo, b).map((s: { id: string }) => s.id) }));
const normalizados = ["Pediatría", "  ÑANDÚ  ", "Análisis e Impresión", "ç à ü"].map((t) => ({ entrada: t, salida: buscar.normalizeForSearch(t) }));

// ── plantilla predeterminada ─────────────────────────────────────────────────

const t = (p: Record<string, unknown>) => ({ id: "x", name: "P", specialty: "medicina_general", scope: "institutional", is_default: false, status: "active", note_mode: "auto", sections: [], ...p });
const pref = (id: string, updatedAt = "2026-09-01T00:00:00Z", specialtyCode = "medicina_general") => ({ specialtyCode, templateId: id, updatedAt });
const listas = {
  base: [t({ id: "default-general", is_default: true }), t({ id: "otra-general" }), t({ id: "personal-1", scope: "personal" })],
  especialidades: [
    t({ id: "urg-default", specialty: "medicina_de_urgencias", is_default: true }),
    t({ id: "gen-default", specialty: "medicina_general", is_default: true }),
    t({ id: "archivada", status: "archived" }),
    t({ id: "cardio", specialty: "Cardiología" }),
  ],
};
const casosPlantilla = [
  { lista: "base", preferences: [pref("otra-general")], lastUsedId: "personal-1", specialtyCode: null, mode: "fixed" },
  { lista: "base", preferences: [pref("otra-general")], lastUsedId: "personal-1", specialtyCode: null, mode: "last" },
  { lista: "base", preferences: [pref("otra-general")], lastUsedId: "personal-1", specialtyCode: null, mode: "manual" },
  { lista: "base", preferences: [pref("otra-general")], lastUsedId: null, specialtyCode: "medicina_general", mode: "last" },
  { lista: "base", preferences: [], lastUsedId: null, specialtyCode: null, mode: "fixed" },
  { lista: "base", preferences: [pref("borrada")], lastUsedId: null, specialtyCode: null, mode: "fixed" },
  { lista: "base", preferences: [pref("otra-general", "2026-09-01T00:00:00Z"), pref("personal-1", "2026-09-05T00:00:00Z", "cardiologia")], lastUsedId: null, specialtyCode: null, mode: "fixed" },
  { lista: "especialidades", preferences: [], lastUsedId: null, specialtyCode: "Medicina de Urgencias", mode: "fixed" },
  { lista: "especialidades", preferences: [], lastUsedId: null, specialtyCode: "cardiologia", mode: "fixed" },
  { lista: "especialidades", preferences: [pref("archivada")], lastUsedId: "archivada", specialtyCode: "medicina_general", mode: "fixed" },
  { lista: "especialidades", preferences: [], lastUsedId: null, specialtyCode: "pediatria", mode: "fixed" },
  { lista: "especialidades", preferences: [], lastUsedId: null, specialtyCode: null, mode: "fixed" },
];
const plantillaV = casosPlantilla.map((c) => ({
  entrada: c,
  salida: plantillas.pickPreselectedTemplate({
    templates: listas[c.lista as keyof typeof listas], preferences: c.preferences,
    lastUsedId: c.lastUsedId, specialtyCode: c.specialtyCode, mode: c.mode,
  }),
}));

// ── copiar la nota ───────────────────────────────────────────────────────────

const notasATexto = [
  nota({ sections: seccionesCompletas, discharge: cierreCompleto }),
  nota({ summary: "", sections: [{ key: "a", label: "Examen", content: "  " }] }),
  nota({ sections: [{ key: "d", label: "Descripción", content: "Párrafo uno.\n\nPárrafo dos." }], discharge: { plan: { medications: [{ name: "Losartán", dose: "50 mg", route: "", frequency: "", duration: "", instructions: "en ayunas" }], non_pharmacological: [], follow_up: [] }, recommendations: [], alarm_signs: [{ text: "Fiebre" }, { text: "Vómito" }] } }),
];
const textosPlanos = notasATexto.map((n) => ({ entrada: n, salida: texto.noteAsPlainText(n) }));

const commit = execSync("git rev-parse --short HEAD", { cwd: WEB }).toString().trim();
process.stdout.write(JSON.stringify({
  generado: new Date().toISOString().slice(0, 10),
  web: `joseph1356k/Pagina-web-clientes-final@${commit}`,
  revisiones, vitales, voces, literales, barras, huecos: huecosV, inserciones,
  catalogoDeAtajos: catalogo, filtros, normalizados, listasDePlantillas: listas, plantillas: plantillaV, textosPlanos,
}, null, 1));
