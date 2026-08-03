#!/usr/bin/env node
// Comprueba que el contrato-espejo C# (windows-graph/src/Clinical/ClinicalContracts.cs) cubre
// EXACTAMENTE lo que el carril clínico de Graph responde de verdad.
//
// Por qué existe: un contrato-espejo escrito a mano se desincroniza en silencio — System.Text.Json
// ignora los campos que el C# no declara, sin excepción ni warning (aprendizaje nº16; ya costó tres
// campos en el ejecutor de exportaciones). Aquí no se relee nada: se LEVANTA el carril real del
// repo Graph (el mismo arnés de fake-graph-clinical.js), se recorre la cadena del aparato, y cada
// respuesta capturada se contrasta contra las anotaciones [JsonPropertyName] del C#.
//
// Uso:
//   node scripts/verify-clinical-contract-mirror.js [--graph <ruta al repo Graph>]
//
// Sale 0 si el espejo cubre las respuestas; 1 si falta algo. Los campos que el C# declara de MÁS
// solo se avisan: el contrato puede crecer y el cliente tolera lo que no llega.

const fs = require('fs');
const path = require('path');
const http = require('http');
const { createClinicalHarness, ENROLL_KEY } = require('./fake-graph-clinical');

function parseArgs(argv) {
  const options = {
    graph: process.env.GRAPH_REPO || path.resolve(__dirname, '..', '..', 'Graph'),
    mirror: path.resolve(__dirname, '..', 'windows-graph', 'src', 'Clinical', 'ClinicalContracts.cs')
  };
  for (let i = 2; i < argv.length; i += 1) {
    if (argv[i] === '--graph') options.graph = argv[++i];
    else if (argv[i] === '--mirror') options.mirror = argv[++i];
    else if (argv[i] === '--help' || argv[i] === '-h') {
      console.log('uso: node scripts/verify-clinical-contract-mirror.js [--graph <repo>] [--mirror <archivo.cs>]');
      process.exit(0);
    } else {
      console.error(`argumento desconocido: ${argv[i]}`);
      process.exit(2);
    }
  }
  return options;
}

/** Las claves JSON que el C# declara, por clase (mismo lector que el espejo de exportaciones). */
function parseMirror(file) {
  const source = fs.readFileSync(file, 'utf8');
  const classes = {};
  let current = null;
  for (const line of source.split('\n')) {
    const declaration = line.match(/(?:class|record)\s+(\w+)/);
    if (declaration) {
      current = declaration[1];
      classes[current] = classes[current] || new Set();
      continue;
    }
    const property = line.match(/\[JsonPropertyName\("([^"]+)"\)\]/);
    if (property && current) classes[current].add(property[1]);
  }
  return classes;
}

function compare(label, actual, declaredKeys, problems) {
  const actualKeys = Object.keys(actual || {});
  const missing = actualKeys.filter((key) => !declaredKeys.has(key));
  const extra = [...declaredKeys].filter((key) => !actualKeys.includes(key));
  if (missing.length) {
    problems.push(`${label}: el C# NO declara ${missing.map((k) => `\`${k}\``).join(', ')} — ` +
                  'Graph lo manda y el cliente lo descarta en silencio.');
  }
  console.log(`  ${missing.length ? '✗' : '✓'} ${label}: ${actualKeys.length} campo(s) reales, ` +
              `${declaredKeys.size} declarado(s)` +
              `${missing.length ? ` · FALTAN: ${missing.join(', ')}` : ''}` +
              `${extra.length ? ` · de más (tolerable): ${extra.join(', ')}` : ''}`);
}

async function main() {
  const options = parseArgs(process.argv);
  console.log('Contraste del espejo C# contra las respuestas REALES del carril clínico\n');
  console.log(`  Graph:  ${options.graph}`);
  console.log(`  espejo: ${options.mirror}\n`);

  const mirror = parseMirror(options.mirror);
  const problems = [];

  const harness = createClinicalHarness(options.graph);
  const server = http.createServer(harness.app);
  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
  const base = `http://127.0.0.1:${server.address().port}`;

  async function call(method, pathName, { body, apiKey, bearer } = {}) {
    const res = await fetch(`${base}${pathName}`, {
      method,
      headers: {
        'Content-Type': 'application/json',
        ...(apiKey ? { 'X-API-Key': apiKey } : {}),
        ...(bearer ? { Authorization: 'Bearer arnes' } : {})
      },
      ...(body ? { body: JSON.stringify(body) } : {})
    });
    const text = await res.text();
    return { status: res.status, body: text ? JSON.parse(text) : null };
  }

  // La cadena real del aparato, capturando cada respuesta.
  let r = await call('POST', '/api/v1/enroll', { body: { device_id: 'mirror', label: 'Espejo' }, apiKey: ENROLL_KEY });
  compare('EnrollResponse', r.body, mirror.EnrollResponse, problems);
  compare('EnrolledDevice', r.body.device, mirror.EnrolledDevice, problems);
  const token = r.body.token;

  r = await call('POST', '/api/v1/devices/pair-code', { apiKey: token });
  compare('PairCodeResponse', r.body, mirror.PairCodeResponse, problems);
  await call('POST', '/api/clinical/devices/claim-pairing', { body: { code: r.body.code }, bearer: true });

  r = await call('POST', '/api/clinical/templates', {
    apiKey: token,
    body: { name: 'Espejo general', specialty: 'medicina_general', sections: ['Motivo de consulta', 'Plan'] }
  });
  compare('TemplateResponseEnvelope', r.body, mirror.TemplateResponseEnvelope, problems);
  compare('ClinicalTemplateInfo', r.body.template, mirror.ClinicalTemplateInfo, problems);
  const templateId = r.body.template.id;

  r = await call('GET', '/api/clinical/templates', { apiKey: token });
  compare('TemplatesResponse', r.body, mirror.TemplatesResponse, problems);

  r = await call('POST', '/api/clinical/encounters', {
    apiKey: token, body: { consultation_type: 'presencial', template_id: templateId }
  });
  compare('CreateEncounterResponse', r.body, mirror.CreateEncounterResponse, problems);
  const encounterId = r.body.encounter_id;

  r = await call('POST', `/api/clinical/encounters/${encounterId}/transcript`, {
    apiKey: token, body: { transcript: 'Dictado sintético para el espejo.' }
  });
  compare('TranscriptSaveResponse', r.body, mirror.TranscriptSaveResponse, problems);

  r = await call('GET', `/api/clinical/encounters/${encounterId}`, { apiKey: token });
  compare('EncounterEnvelope', r.body, mirror.EncounterEnvelope, problems);
  compare('ClinicalEncounterInfo', r.body.encounter, mirror.ClinicalEncounterInfo, problems);

  await call('POST', `/api/clinical/encounters/${encounterId}/generate-note`, { apiKey: token, body: {} });

  r = await call('POST', '/api/clinical/assistant/note-adjustment', {
    apiKey: token, body: { encounter_id: encounterId, instruction: 'Resume el plan.' }
  });
  compare('AdjustNoteResponse', r.body, mirror.AdjustNoteResponse, problems);
  const proposed = r.body.proposed_note_json;

  // Se fuerza la razón 'web_edito' editando el historial por fuera: así la captura
  // de MirrorOutcome trae AMBAS claves (refreshed y reason).
  const consultationRow = harness.fake.tables.consultations.find((row) => row.id === encounterId);
  consultationRow.note = [{ id: 'x', titulo: 'X', kind: 'texto', texto: 'La médica editó a mano.' }];
  r = await call('PUT', `/api/clinical/encounters/${encounterId}/note`, {
    apiKey: token, body: { note_json: proposed }
  });
  compare('NoteResponse', r.body, mirror.NoteResponse, problems);
  compare('MirrorOutcome', r.body.mirror, mirror.MirrorOutcome, problems);

  r = await call('GET', '/api/clinical/consultations', { apiKey: token });
  compare('ConsultationsResponse', r.body, mirror.ConsultationsResponse, problems);
  compare('ConsultationSummary', r.body.consultations[0], mirror.ConsultationSummary, problems);

  server.close();

  if (problems.length) {
    console.log(`\n✗ El espejo NO cubre el contrato:\n  - ${problems.join('\n  - ')}`);
    process.exit(1);
  }
  console.log('\n✅ El espejo C# cubre todas las respuestas reales del carril clínico.');
}

main().catch((error) => {
  console.error(`FALLO: ${error.message}`);
  process.exit(1);
});
