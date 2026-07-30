#!/usr/bin/env node
// Un Graph FALSO que habla el carril de exportaciones — banco de pruebas del ejecutor real.
//
// Para qué: probar el ejecutor de U-Windows-App de punta a punta sin Graph, sin Supabase y sin
// pacientes. Se levanta, se apunta el cliente con `set GRAPH_BASE_URL=http://127.0.0.1:8787`, y el
// arnés observa lo que el cliente hace y dicta un veredicto de CONFORMIDAD.
//
// Reproduce las reglas que de verdad tiene Graph, verificadas leyendo la RPC y las rutas:
//   · claim FIFO, `attempts` YA INCREMENTADO (la primera entrega vale 1), lease sellado.
//   · 204 cuando no hay trabajo.
//   · `claimed_by` = `device` del cuerpo; el result exige el MISMO string exacto o 409 NOT_OWNED.
//   · lease vencido en el result → 409 LEASE_EXPIRED.
//   · fila ya terminal → 200 con `idempotent:true`, `status` el VIEJO y `consultation_exported:false`
//     SIEMPRE — aunque el primer resultado sí exportara. Es la trampa que hace que un cliente ingenuo
//     crea que su reenvío falló.
//   · outcome fuera de {ok,needs_doctor,error} → 400.
//
// Escenarios (--scenario):
//   ok             un trabajo; el result se acepta a la primera.
//   empty          cola vacía: el cliente debe sondear sin tratarlo como error.
//   flaky          los 2 primeros result responden 503: el cliente DEBE reintentar hasta el ack.
//   not-owned      el trabajo lo tiene otro equipo: el result da 409 y NO debe reintentarse.
//   lease-expired  el lease ya venció: 409 y NO debe reintentarse.
//   double         se sirve el MISMO trabajo dos veces (simula lease vencido tras un corte), para
//                  ver si el cliente lo vuelve a ejecutar a ciegas o pide intervención.
//
// Autocomprobación: `--self-test` corre el SIMULADOR DE REFERENCIA de Graph contra este arnés y
// exige que salga conforme. Si el arnés se desviara del contrato, el cliente de referencia lo
// delataría — es lo que impide que este banco de pruebas mida con una vara inventada.
//
// Uso:
//   node scripts/fake-graph-exports.js --scenario ok --port 8787
//   node scripts/fake-graph-exports.js --self-test

const http = require('http');
const { spawn } = require('child_process');
const path = require('path');

const API_KEY = 'miracle_arnes_local_no_es_un_secreto';
const LEASE_SECONDS = 600;
const VALID_OUTCOMES = ['ok', 'needs_doctor', 'error'];

function parseArgs(argv) {
  const options = { scenario: 'ok', port: 8787, selfTest: false, quiet: false, exitAfter: 0 };
  for (let i = 2; i < argv.length; i += 1) {
    const next = () => argv[++i];
    switch (argv[i]) {
      case '--scenario': options.scenario = `${next()}`; break;
      case '--port': options.port = Number(next()); break;
      case '--self-test': options.selfTest = true; break;
      case '--quiet': options.quiet = true; break;
      case '--exit-after': options.exitAfter = Number(next()); break;
      case '--help': case '-h':
        console.log('uso: node scripts/fake-graph-exports.js [--scenario ok|empty|flaky|not-owned|lease-expired|double] [--port N] [--self-test]');
        process.exit(0);
        break;
      default:
        console.error(`argumento desconocido: ${argv[i]}`);
        process.exit(2);
    }
  }
  return options;
}

// Payload sintético con la forma EXACTA que emite Graph (verificada contra
// NoteExportSnapshot.buildNoteExportPayload). Ningún dato corresponde a una persona real.
function syntheticPayload() {
  const rendered = [
    'MOTIVO DE CONSULTA:\nDolor lumbar de dos semanas.',
    'PLAN:\n- Analgesia\n- Control en 15 días',
    'RESUMEN:\nPaciente sintético para pruebas de integración.',
    'CODIFICACIÓN:\nCIE-10 M54.5 — Lumbago no especificado'
  ].join('\n\n');

  return {
    note: [
      { id: 'motivo', titulo: 'Motivo de consulta', kind: 'texto', texto: 'Dolor lumbar de dos semanas.', items: [] },
      { id: 'plan', titulo: 'Plan', kind: 'lista', texto: '', items: ['Analgesia', 'Control en 15 días'] }
    ],
    resumen: 'Paciente sintético para pruebas de integración.',
    codigos: [{ sistema: 'CIE-10', codigo: 'M54.5', descripcion: 'Lumbago no especificado' }],
    firma: { por: 'Profesional de prueba', fecha: '2026-07-30T12:00:00Z', hash: 'a'.repeat(64) },
    patient_ref: '00000000-0000-4000-8000-0000000000aa',
    especialidad: 'Medicina general',
    servicio: 'Consulta externa',
    fecha: '2026-07-30',
    rendered_text: rendered,
    context: rendered
  };
}

class Harness {
  constructor(scenario, log) {
    this.scenario = scenario;
    this.log = log;
    this.observations = [];
    this.resultAttempts = 0;
    this.claimCount = 0;

    this.job = {
      id: 'exp_sintetico_0001',
      workflow_id: 'wf_prueba_exportacion',
      attempts: 0,
      status: 'pending',
      claimed_by: null,
      lease_expires_at: null,
      result: null
    };
  }

  note(rule, ok, detail) {
    this.observations.push({ rule, ok, detail });
    this.log(`  ${ok ? '✓' : '✗'} ${rule}${detail ? ` — ${detail}` : ''}`);
  }

  claim(device, hasKey) {
    if (!hasKey) return { status: 401, body: { error: 'API key requerida' } };
    this.claimCount += 1;

    if (this.claimCount === 1) {
      this.note('manda X-API-Key en el claim', true);
      this.note('manda un `device` no vacío en el claim', Boolean(device), device || '(vacío)');
    }

    if (this.scenario === 'empty') return { status: 204 };
    // 'double' sirve el mismo trabajo dos veces: es lo que pasa de verdad cuando un ejecutor muere
    // a media escritura, vence el lease y la cola vuelve a ofrecer el trabajo.
    const alreadyServed = this.job.status !== 'pending';
    if (alreadyServed && this.scenario !== 'double') return { status: 204 };
    if (alreadyServed && this.claimCount > 2) return { status: 204 };

    this.job.attempts += 1;             // Graph incrementa en el CLAIM: la 1ª entrega vale 1
    this.job.status = 'claimed';
    this.job.claimed_by = device;
    this.job.lease_expires_at = new Date(
      Date.now() + (this.scenario === 'lease-expired' ? -1000 : LEASE_SECONDS * 1000)).toISOString();

    return {
      status: 200,
      body: {
        export: {
          id: this.job.id,
          workflow_id: this.job.workflow_id,
          attempts: this.job.attempts,
          lease_expires_at: this.job.lease_expires_at
        },
        payload: syntheticPayload(),
        plan: null
      }
    };
  }

  result(exportId, body, hasKey) {
    if (!hasKey) return { status: 401, body: { error: 'API key requerida' } };
    this.resultAttempts += 1;
    const device = `${body?.device || ''}`.trim();
    const outcome = `${body?.outcome || ''}`.trim();

    if (this.resultAttempts === 1) {
      this.note('la identidad del claim y la del result coinciden',
        device === this.job.claimed_by,
        `claim='${this.job.claimed_by}' result='${device}'`);
      this.note('el outcome pertenece al contrato', VALID_OUTCOMES.includes(outcome), outcome || '(vacío)');
      this.checkNoPhi(body);
      this.checkShape(body, outcome);
    }

    // Fila terminal: ack idempotente con el estado VIEJO. Igual que Graph, esta rama va ANTES que
    // las comprobaciones de propiedad y lease.
    if (['completed', 'needs_doctor', 'failed', 'cancelled'].includes(this.job.status)) {
      this.note('reenvía el mismo resultado hasta el ack (idempotencia)', true,
        `intento ${this.resultAttempts}`);
      return {
        status: 200,
        body: {
          acknowledged: true, idempotent: true, status: this.job.status,
          consultation_exported: false, export: { id: this.job.id }
        }
      };
    }

    if (!VALID_OUTCOMES.includes(outcome)) {
      return { status: 400, body: { error: { code: 'EXPORT_INVALID', message: 'outcome inválido' } } };
    }
    if (this.scenario === 'not-owned' || (device && device !== this.job.claimed_by)) {
      return { status: 409, body: { error: { code: 'EXPORT_NOT_OWNED', message: 'el trabajo es de otro equipo' } } };
    }
    if (new Date(this.job.lease_expires_at).getTime() < Date.now()) {
      return { status: 409, body: { error: { code: 'EXPORT_LEASE_EXPIRED', message: 'el plazo venció' } } };
    }
    if (this.scenario === 'flaky' && this.resultAttempts <= 2) {
      return { status: 503, body: { error: { code: 'UPSTREAM', message: 'no disponible, reintenta' } } };
    }

    this.job.status = outcome === 'ok' ? 'completed' : outcome === 'needs_doctor' ? 'needs_doctor' : 'failed';
    this.job.result = body;
    return {
      status: 200,
      body: {
        acknowledged: true, idempotent: false, status: this.job.status,
        consultation_exported: outcome === 'ok', export: { id: this.job.id }
      }
    };
  }

  /** Nada del contenido clínico puede viajar de vuelta en los campos de diagnóstico. */
  checkNoPhi(body) {
    const diagnostic = JSON.stringify({
      error_code: body?.error_code, detail_code: body?.detail_code,
      unresolved_fields: body?.unresolved_fields, folio: body?.folio
    });
    const leaked = ['Dolor lumbar', 'Lumbago', 'Analgesia', 'Profesional de prueba', '00000000-0000']
      .filter((needle) => diagnostic.includes(needle));
    this.note('no devuelve contenido clínico en los campos de diagnóstico',
      leaked.length === 0, leaked.length ? `filtrado: ${leaked.join(', ')}` : '');
  }

  /** Las formas que Graph y la interfaz del médico esperan de cada outcome. */
  checkShape(body, outcome) {
    if (outcome === 'needs_doctor') {
      const fields = body?.unresolved_fields;
      this.note('needs_doctor manda unresolved_fields como array de texto',
        Array.isArray(fields) && fields.every((f) => typeof f === 'string'),
        JSON.stringify(fields));
      // Miracle Notes los pinta dentro de «Quedaron campos sin completar…: X, Y.» — deben leerse.
      if (Array.isArray(fields) && fields.length) {
        this.note('las etiquetas de unresolved_fields son legibles (no ids técnicos)',
          fields.every((f) => f.length > 2 && !/^[A-Z0-9_\-]+$/.test(f)), fields.join(' · '));
      }
    }
    if (outcome === 'error') {
      this.note('error trae error_code tipado', Boolean(`${body?.error_code || ''}`.trim()),
        `${body?.error_code}`);
    }
  }

  verdict() {
    const failed = this.observations.filter((o) => !o.ok);
    return { total: this.observations.length, failed };
  }
}

function serve(harness, port, log) {
  const server = http.createServer((req, res) => {
    let raw = '';
    req.on('data', (chunk) => { raw += chunk; });
    req.on('end', () => {
      let body = null;
      try { body = raw ? JSON.parse(raw) : null; } catch { body = null; }

      const hasKey = Boolean(req.headers['x-api-key']);
      const url = (req.url || '').split('?')[0];
      let out;

      if (req.method === 'POST' && url === '/api/v1/operations/exports/claim') {
        out = harness.claim(`${body?.device || ''}`.trim(), hasKey);
      } else if (req.method === 'POST' && /^\/api\/v1\/operations\/exports\/[^/]+\/result$/.test(url)) {
        out = harness.result(url.split('/')[5], body, hasKey);
      } else {
        out = { status: 404, body: { error: 'ruta no servida por el arnés' } };
      }

      log(`  → ${req.method} ${url} · ${out.status}`);
      if (out.status === 204) { res.writeHead(204).end(); return; }
      res.writeHead(out.status, { 'content-type': 'application/json' });
      res.end(JSON.stringify(out.body));
    });
  });
  return new Promise((resolve) => server.listen(port, '127.0.0.1', () => resolve(server)));
}

/** Corre el simulador de referencia de Graph contra el arnés: valida el arnés, no al cliente. */
function runReferenceClient(port, graphRepo) {
  const script = path.join(graphRepo, 'scripts', 'simulate-operations-executor.js');
  return new Promise((resolve) => {
    const child = spawn(process.execPath, [script, '--once', '--device', 'arnes-referencia', '--work-seconds', '0'], {
      env: { ...process.env, GRAPH_BASE_URL: `http://127.0.0.1:${port}`, MIRACLE_API_KEY: API_KEY },
      stdio: ['ignore', 'pipe', 'pipe']
    });
    let output = '';
    child.stdout.on('data', (d) => { output += d; });
    child.stderr.on('data', (d) => { output += d; });
    child.on('close', (code) => resolve({ code, output }));
  });
}

async function selfTest() {
  console.log('Autocomprobación del arnés: el cliente de REFERENCIA de Graph debe salir conforme.\n');
  const graphRepo = process.env.GRAPH_REPO || path.resolve(__dirname, '..', '..', 'Graph');
  let allOk = true;

  for (const scenario of ['ok', 'flaky', 'empty']) {
    const port = 8790 + ['ok', 'flaky', 'empty'].indexOf(scenario);
    const harness = new Harness(scenario, () => {});
    const server = await serve(harness, port, () => {});
    const { code, output } = await runReferenceClient(port, graphRepo);
    server.close();

    const { total, failed } = harness.verdict();
    // El simulador sale 3 cuando no había trabajo: es el desenlace correcto del escenario `empty`.
    const expectedExit = scenario === 'empty' ? 3 : 0;
    const ok = failed.length === 0 && code === expectedExit;
    allOk = allOk && ok;

    console.log(`  ${ok ? '✓' : '✗'} escenario «${scenario}»: ${total} regla(s) observada(s), ` +
                `${failed.length} incumplida(s), salida ${code} (esperada ${expectedExit})`);
    if (scenario === 'flaky') {
      const retried = harness.resultAttempts >= 3;
      console.log(`  ${retried ? '✓' : '✗'} el cliente de referencia reintentó el result hasta el ack ` +
                  `(${harness.resultAttempts} envíos)`);
      allOk = allOk && retried;
    }
    if (!ok) {
      for (const f of failed) console.log(`      · ${f.rule} — ${f.detail}`);
      console.log(output.split('\n').map((l) => `      | ${l}`).join('\n'));
    }
  }

  console.log(allOk
    ? '\n✓ El arnés reproduce el contrato: el cliente de referencia lo pasa entero.'
    : '\n✗ El arnés NO reproduce el contrato: corrígelo antes de juzgar con él al ejecutor real.');
  process.exit(allOk ? 0 : 1);
}

async function main() {
  const options = parseArgs(process.argv);
  if (options.selfTest) return selfTest();

  const log = options.quiet ? () => {} : (msg) => console.log(msg);
  const harness = new Harness(options.scenario, log);
  await serve(harness, options.port, log);

  console.log(`Graph falso escuchando en http://127.0.0.1:${options.port}`);
  console.log(`  escenario: ${options.scenario}`);
  console.log(`  API key:   ${API_KEY}`);
  console.log('\nEn el equipo Windows, antes de abrir U.exe:');
  console.log(`  set GRAPH_BASE_URL=http://127.0.0.1:${options.port}`);
  console.log(`  set GRAPH_API_KEY=${API_KEY}\n`);
  console.log('Ctrl+C para ver el veredicto de conformidad.\n');

  const finish = () => {
    const { total, failed } = harness.verdict();
    console.log(`\n── Veredicto ──  ${total} regla(s) observada(s), ${failed.length} incumplida(s)`);
    for (const f of failed) console.log(`  ✗ ${f.rule} — ${f.detail}`);
    if (total === 0) console.log('  (el cliente nunca llegó a hablar con el arnés)');
    process.exit(failed.length === 0 && total > 0 ? 0 : 1);
  };
  process.on('SIGINT', finish);
  if (options.exitAfter > 0) setTimeout(finish, options.exitAfter * 1000);
}

main().catch((error) => {
  console.error(`✗ ${error.message}`);
  process.exit(1);
});
