#!/usr/bin/env node
// Un Graph FALSO que habla el carril clínico de aparatos — banco de pruebas del cliente Windows.
//
// Para qué: probar U.exe pilotando Miracle Notes de punta a punta sin backend en la nube, sin
// Supabase y sin pacientes. Se levanta, se apunta el cliente
// (`set GRAPH_BASE_URL=http://127.0.0.1:8788` + `set GRAPH_API_KEY=<key de enrolamiento de abajo>`)
// y el equipo se enrola, pide su código, y trabaja consultas contra este arnés.
//
// A diferencia de un fake escrito a mano, AQUÍ NO HAY CONTRATO INVENTADO: el arnés levanta las
// rutas REALES del repo Graph (registerDeviceRoutes, requireClinicalActor, registerClinicalRoutes
// y sus servicios) sobre la base falsa en memoria del propio Graph. Solo el LLM es de mentira
// (nota y ajuste enlatados). Si el cliente pasa aquí, habló el contrato real — es la misma vara
// del aprendizaje nº16: la comprobación ejecutable, no la relectura.
//
// Escenarios (--scenario):
//   paired    (default) el equipo del arnés ya está enrolado Y vinculado a la médica de prueba.
//             Imprime el token per-install listo para GRAPH_DEVICE_TOKEN.
//   unpaired  nadie está enrolado: el cliente debe enrolarse solo (key de enrolamiento) y al
//             primer notes_* recibir DEVICE_NOT_PAIRED y mostrar su código. El «médico» canjea:
//               curl -X POST http://127.0.0.1:8788/api/clinical/devices/claim-pairing \
//                    -H "Authorization: Bearer da-igual" -H "Content-Type: application/json" \
//                    -d "{\"code\":\"ABCD2345\"}"
//
// Autocomprobación: `--self-test` recorre la cadena completa (enrolar → sin vínculo → código →
// canje → plantilla → consulta → dictado → nota → ajuste → historial) contra sí mismo y sale 0/1.
//
// Uso:
//   node scripts/fake-graph-clinical.js [--scenario paired|unpaired] [--port 8788] [--graph <repo>]
//   node scripts/fake-graph-clinical.js --self-test

const path = require('path');
const fs = require('fs');
const http = require('http');

const ENROLL_KEY = 'arnes-enroll-local-no-es-un-secreto';
const DOCTOR = '11111111-1111-4111-8111-111111111111';
const ORG = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';

function parseArgs(argv) {
  const options = {
    scenario: 'paired',
    port: 8788,
    graph: process.env.GRAPH_REPO || path.resolve(__dirname, '..', '..', 'Graph'),
    selfTest: false
  };
  for (let i = 2; i < argv.length; i += 1) {
    const next = () => argv[++i];
    switch (argv[i]) {
      case '--scenario': options.scenario = `${next()}`; break;
      case '--port': options.port = Number(next()); break;
      case '--graph': options.graph = `${next()}`; break;
      case '--self-test': options.selfTest = true; break;
      case '--help': case '-h':
        console.log('uso: node scripts/fake-graph-clinical.js [--scenario paired|unpaired] [--port N] [--graph <repo>] [--self-test]');
        process.exit(0);
        break;
      default:
        console.error(`argumento desconocido: ${argv[i]}`);
        process.exit(2);
    }
  }
  return options;
}

function requireGraph(graphRepo, relative) {
  const modulePath = path.join(graphRepo, ...relative.split('/'));
  if (!fs.existsSync(modulePath) && !fs.existsSync(`${modulePath}.js`)) {
    console.error(`✗ No encuentro ${relative} en el repo Graph:\n  ${modulePath}`);
    console.error('  Pasa la ruta con --graph <ruta> (y corre `npm install` allí).');
    process.exit(2);
  }
  return require(modulePath);
}

/**
 * Levanta el carril clínico REAL de Graph sobre su base falsa en memoria.
 * Exportado para que verify-clinical-contract-mirror.js use EXACTAMENTE el
 * mismo arnés al contrastar el espejo C#.
 */
function createClinicalHarness(graphRepo) {
  const express = require(path.join(graphRepo, 'node_modules', 'express'));
  const { createFakeClinicalSupabase } = requireGraph(graphRepo, 'tests/helpers/fakeClinicalSupabase');
  const WindowsDeviceService = requireGraph(graphRepo, 'src/application/use-cases/WindowsDeviceService');
  const ConsultationMirrorService = requireGraph(graphRepo, 'src/application/use-cases/ConsultationMirrorService');
  const ConsultationQueryService = requireGraph(graphRepo, 'src/application/use-cases/ConsultationQueryService');
  const ClinicalTemplateService = requireGraph(graphRepo, 'src/application/use-cases/ClinicalTemplateService');
  const ClinicalEncounterService = requireGraph(graphRepo, 'src/application/use-cases/ClinicalEncounterService');
  const ClinicalNoteValidationService = requireGraph(graphRepo, 'src/application/use-cases/ClinicalNoteValidationService');
  const ClinicalNoteGeneratorService = requireGraph(graphRepo, 'src/application/use-cases/ClinicalNoteGeneratorService');
  const ClinicalNotePromptBuilder = requireGraph(graphRepo, 'src/application/use-cases/ClinicalNotePromptBuilder');
  const ClinicalAssistantService = requireGraph(graphRepo, 'src/application/use-cases/ClinicalAssistantService');
  const ClinicalAssistantPromptBuilder = requireGraph(graphRepo, 'src/application/use-cases/ClinicalAssistantPromptBuilder');
  const ClinicalAssistantValidationService = requireGraph(graphRepo, 'src/application/use-cases/ClinicalAssistantValidationService');
  const SupabaseClinicalTemplateRepository = requireGraph(graphRepo, 'src/infrastructure/repositories/SupabaseClinicalTemplateRepository');
  const SupabaseClinicalEncounterRepository = requireGraph(graphRepo, 'src/infrastructure/repositories/SupabaseClinicalEncounterRepository');
  const createRequireClinicalActor = requireGraph(graphRepo, 'web/api/requireClinicalActor');
  const registerClinicalRoutes = requireGraph(graphRepo, 'web/api/registerClinicalRoutes');
  const registerDeviceRoutes = requireGraph(graphRepo, 'web/api/registerDeviceRoutes');

  const fake = createFakeClinicalSupabase({
    profiles: [{ id: DOCTOR, organization_id: ORG, role: 'medico', full_name: 'Dra. de Prueba', email: 'prueba@arnes' }],
    consultations: [],
    audit_events: []
  });

  const windowsDeviceService = new WindowsDeviceService(fake);
  const consultationMirrorService = new ConsultationMirrorService(fake);
  const noteValidationService = new ClinicalNoteValidationService();
  const templateService = new ClinicalTemplateService(new SupabaseClinicalTemplateRepository(fake));
  const encounterService = new ClinicalEncounterService(new SupabaseClinicalEncounterRepository(fake), templateService);

  // El único componente de mentira: el LLM. La nota enlatada rellena las
  // secciones que pida el snapshot; el ajuste retoca la primera.
  const fakeLlm = {
    hasApiKey: () => true,
    parseJsonObject: (raw) => JSON.parse(raw),
    async chatExpectingJson(messages) {
      const prompt = JSON.stringify(messages);
      const keys = [...new Set([...prompt.matchAll(/"key"\s*:\s*"([^"]+)"/g)].map((m) => m[1]))];
      const sections = (keys.length ? keys : ['motivo_de_consulta']).map((key) => ({
        key, label: key, content: `Contenido sintético (${key}).`, confidence: 1, evidence: ''
      }));
      if (prompt.includes('note-adjustment') || prompt.includes('Ajusta') || prompt.includes('instruccion') || prompt.includes('instruction')) {
        return JSON.stringify({
          note_json: { summary: 'Resumen ajustado.', sections: [{ ...sections[0], content: 'Contenido AJUSTADO.' }] },
          explanation: 'Ajuste sintético del arnés.'
        });
      }
      return JSON.stringify({ summary: 'Resumen sintético.', sections, warnings: [], missing_required_sections: [] });
    }
  };

  const noteGeneratorService = new ClinicalNoteGeneratorService({
    encounterService,
    encounterRepository: new SupabaseClinicalEncounterRepository(fake),
    llmProvider: fakeLlm,
    promptBuilder: new ClinicalNotePromptBuilder(),
    validationService: noteValidationService,
    consultationMirrorService
  });
  const assistantService = new ClinicalAssistantService({
    encounterService,
    llmProvider: fakeLlm,
    promptBuilder: new ClinicalAssistantPromptBuilder(),
    validationService: new ClinicalAssistantValidationService(),
    noteValidationService
  });

  const app = express();
  app.use(express.json({ limit: '16mb' }));

  // El MISMO montaje partido de web/server.js: actor clínico vs. solo-médico.
  const requireClinicalActor = createRequireClinicalActor({ windowsDeviceService });
  ['/api/clinical/templates', '/api/clinical/encounters', '/api/clinical/assistant', '/api/clinical/consultations']
    .forEach((prefix) => app.use(prefix, requireClinicalActor));
  // Stand-in de requireClinicalAuth para el canje: en el arnés, cualquier Bearer
  // ES la médica de prueba (no hay Supabase local que verifique JWTs).
  app.use('/api/clinical/devices', (req, res, next) => {
    req.clinicalUser = { id: DOCTOR, email: 'prueba@arnes' };
    next();
  });

  registerDeviceRoutes(app, { windowsDeviceService, enrollKeys: ENROLL_KEY });
  registerClinicalRoutes(app, {
    diagnosisSuggestionService: { suggest: async () => ({ suggestions: [] }) },
    templateService,
    encounterService,
    noteGeneratorService,
    noteValidationService,
    assistantService,
    consultationQueryService: new ConsultationQueryService(fake),
    consultationMirrorService
  });

  return { app, fake, windowsDeviceService };
}

async function pairDevice(harness, label) {
  const enrolled = await harness.windowsDeviceService.enroll({ deviceId: `arnes-${label}`, label });
  const { code } = await harness.windowsDeviceService.createPairingCode({ id: enrolled.device.id });
  await harness.windowsDeviceService.claimPairing({ code, doctor: { id: DOCTOR } });
  return enrolled.token;
}

// ── Self-test: la cadena completa contra el propio arnés ─────────────────────

async function selfTest(options) {
  const assert = require('assert');
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

  let checks = 0;
  const ok = (label) => { checks += 1; console.log(`  ok  ${label}`); };

  let r = await call('POST', '/api/v1/enroll', { body: { device_id: 'self-test', label: 'Self Test' }, apiKey: ENROLL_KEY });
  assert.strictEqual(r.status, 201);
  const token = r.body.token;
  ok('enrolamiento con la key de enrolamiento');

  r = await call('GET', '/api/clinical/consultations', { apiKey: token });
  assert.strictEqual(r.status, 403);
  assert.strictEqual(r.body.error.code, 'DEVICE_NOT_PAIRED');
  ok('sin vínculo → DEVICE_NOT_PAIRED');

  r = await call('POST', '/api/v1/devices/pair-code', { apiKey: token });
  const code = r.body.code;
  r = await call('POST', '/api/clinical/devices/claim-pairing', { body: { code }, bearer: true });
  assert.strictEqual(r.status, 200);
  ok('código canjeado por la médica de prueba');

  r = await call('POST', '/api/clinical/templates', {
    apiKey: token,
    body: { name: 'Arnés general', specialty: 'medicina_general', sections: ['Motivo de consulta', 'Plan'] }
  });
  assert.strictEqual(r.status, 201);
  const templateId = r.body.template.id;
  ok('plantilla creada como aparato');

  r = await call('POST', '/api/clinical/encounters', {
    apiKey: token, body: { consultation_type: 'presencial', template_id: templateId }
  });
  const encounterId = r.body.encounter_id;
  ok('consulta abierta');

  r = await call('POST', `/api/clinical/encounters/${encounterId}/transcript`, {
    apiKey: token, body: { transcript: 'Dictado sintético del arnés, sin pacientes reales.' }
  });
  assert.strictEqual(r.status, 200);
  ok('dictado guardado');

  r = await call('POST', `/api/clinical/encounters/${encounterId}/generate-note`, { apiKey: token, body: {} });
  assert.strictEqual(r.status, 200);
  assert.ok(r.body.note_json.sections.length >= 1);
  ok('nota generada (LLM enlatado) y espejo publicado');
  assert.ok(harness.fake.tables.consultations.find((row) => row.id === encounterId));
  ok('la consulta aparece en el historial como borrador');

  r = await call('POST', '/api/clinical/assistant/note-adjustment', {
    apiKey: token, body: { encounter_id: encounterId, instruction: 'Resume el plan.' }
  });
  assert.strictEqual(r.status, 200);
  assert.ok(r.body.proposed_note_json);
  const proposed = r.body.proposed_note_json;
  ok('ajuste propuesto (no persistido)');

  r = await call('PUT', `/api/clinical/encounters/${encounterId}/note`, {
    apiKey: token, body: { note_json: proposed }
  });
  assert.strictEqual(r.status, 200);
  assert.strictEqual(r.body.mirror.refreshed, true);
  ok('nota guardada y el historial refrescado (mirror.refreshed)');

  r = await call('GET', '/api/clinical/consultations', { apiKey: token });
  assert.ok(r.body.consultations.some((row) => row.id === encounterId));
  assert.ok(!('note' in r.body.consultations[0]));
  ok('el historial lista la consulta, magro (sin cuerpo de nota)');

  server.close();
  console.log(`\n✅ Self-test del arnés clínico: ${checks} comprobaciones OK.`);
}

// ── CLI ──────────────────────────────────────────────────────────────────────

async function main() {
  const options = parseArgs(process.argv);
  if (options.selfTest) {
    await selfTest(options);
    return;
  }

  const harness = createClinicalHarness(options.graph);
  const server = http.createServer(harness.app);
  await new Promise((resolve) => server.listen(options.port, '127.0.0.1', resolve));

  console.log(`Arnés del carril clínico escuchando en http://127.0.0.1:${options.port}`);
  console.log(`  escenario: ${options.scenario}`);
  console.log('  En el equipo con U.exe:');
  console.log(`    set GRAPH_BASE_URL=http://127.0.0.1:${options.port}`);
  console.log(`    set GRAPH_API_KEY=${ENROLL_KEY}`);

  if (options.scenario === 'paired') {
    const token = await pairDevice(harness, 'Equipo del arnés');
    console.log('  Equipo ya enrolado y VINCULADO a la Dra. de Prueba. Token per-install:');
    console.log(`    set GRAPH_DEVICE_TOKEN=${token}`);
  } else {
    console.log('  Nadie está enrolado: U.exe se enrolará solo y al primer notes_* mostrará su código.');
    console.log('  Canjéalo como «médico» con:');
    console.log(`    curl -X POST http://127.0.0.1:${options.port}/api/clinical/devices/claim-pairing -H "Authorization: Bearer x" -H "Content-Type: application/json" -d "{\\"code\\":\\"<CODIGO>\\"}"`);
  }
  console.log('\nCtrl+C para apagar. Nada de esto toca datos reales.');
}

if (require.main === module) {
  main().catch((error) => {
    console.error(`FALLO: ${error.message}`);
    process.exit(1);
  });
}

module.exports = { createClinicalHarness, ENROLL_KEY, DOCTOR, ORG };
