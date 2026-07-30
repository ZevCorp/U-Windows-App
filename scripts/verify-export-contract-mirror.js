#!/usr/bin/env node
// Comprueba que el contrato-espejo C# (windows-graph/src/NoteExportContracts.cs) cubre EXACTAMENTE
// el payload que Graph emite de verdad.
//
// Por qué existe: un contrato-espejo escrito a mano se desincroniza en silencio. Un campo que Graph
// manda y el cliente no modela no da error de compilación ni excepción en runtime — System.Text.Json
// lo ignora y el dato simplemente no llega. Esta comprobación ya encontró tres campos ausentes
// (`especialidad`, `servicio`, `fecha`) en la primera versión del ejecutor.
//
// No necesita red, ni base de datos, ni SAP: importa el constructor de payloads del repo Graph, lo
// alimenta con una consulta SINTÉTICA (nunca datos de un paciente real) y compara las claves contra
// las anotaciones [JsonPropertyName] del C#.
//
// Uso:
//   node scripts/verify-export-contract-mirror.js [--graph <ruta al repo Graph>]
//
// Sale 0 si el espejo cubre el payload; 1 si falta algo. Los campos que el C# declara de MÁS solo
// se avisan: el contrato puede crecer y el cliente tolera lo que no llega.

const fs = require('fs');
const path = require('path');

function parseArgs(argv) {
  const options = {
    graph: process.env.GRAPH_REPO || path.resolve(__dirname, '..', '..', 'Graph'),
    mirror: path.resolve(__dirname, '..', 'windows-graph', 'src', 'NoteExport', 'NoteExportContracts.cs')
  };
  for (let i = 2; i < argv.length; i += 1) {
    if (argv[i] === '--graph') options.graph = argv[++i];
    else if (argv[i] === '--mirror') options.mirror = argv[++i];
    else if (argv[i] === '--help' || argv[i] === '-h') {
      console.log('uso: node scripts/verify-export-contract-mirror.js [--graph <repo>] [--mirror <archivo.cs>]');
      process.exit(0);
    } else {
      console.error(`argumento desconocido: ${argv[i]}`);
      process.exit(2);
    }
  }
  return options;
}

// Una consulta sintética con las dos formas de sección y un código rechazado, para comprobar de paso
// que Graph filtra por estado 'aceptado'. Nada aquí corresponde a una persona real.
const CONSULTA_SINTETICA = {
  patient_id: '00000000-0000-4000-8000-00000000beef',
  especialidad: 'Medicina general',
  servicio: 'Consulta externa',
  fecha: '2026-07-30',
  resumen: 'Resumen sintético de prueba.',
  note: [
    { id: 'motivo', titulo: 'Motivo de consulta', kind: 'texto', texto: 'Texto sintético.' },
    { id: 'plan', titulo: 'Plan', kind: 'lista', items: ['Punto uno', 'Punto dos'] }
  ],
  codigos: [
    { sistema: 'CIE-10', codigo: 'Z000', descripcion: 'Código sintético', estado: 'aceptado' },
    { sistema: 'CIE-10', codigo: 'Z001', descripcion: 'No debe viajar', estado: 'sugerido' }
  ],
  firma: { por: 'Profesional de prueba', fecha: '2026-07-30T12:00:00Z', hash: 'f'.repeat(64) }
};

function loadPayloadBuilder(graphRepo) {
  const modulePath = path.join(graphRepo, 'src', 'application', 'use-cases', 'NoteExportSnapshot.js');
  if (!fs.existsSync(modulePath)) {
    console.error(`✗ No encuentro el constructor de payloads de Graph en:\n  ${modulePath}`);
    console.error('  Pasa la ruta del repo Graph con --graph <ruta>.');
    process.exit(2);
  }
  return require(modulePath).buildNoteExportPayload;
}

/** Las claves JSON que el C# declara, por clase: { ExportPayload: Set<string>, ... } */
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

function compare(label, actualKeys, declaredKeys, problems) {
  const missing = actualKeys.filter((key) => !declaredKeys.has(key));
  const extra = [...declaredKeys].filter((key) => !actualKeys.includes(key));

  if (missing.length) {
    problems.push(`${label}: el C# NO declara ${missing.map((k) => `\`${k}\``).join(', ')} — ` +
                  'Graph lo manda y el cliente lo descarta en silencio.');
  }
  console.log(`  ${missing.length ? '✗' : '✓'} ${label}: ${actualKeys.length} campo(s) de Graph, ` +
              `${declaredKeys.size} declarado(s) en C#` +
              `${missing.length ? ` · FALTAN: ${missing.join(', ')}` : ''}` +
              `${extra.length ? ` · de más (tolerable): ${extra.join(', ')}` : ''}`);
}

function main() {
  const options = parseArgs(process.argv);
  console.log('Contraste del contrato-espejo C# contra el payload real de Graph\n');
  console.log(`  Graph:  ${options.graph}`);
  console.log(`  espejo: ${options.mirror}\n`);

  const buildNoteExportPayload = loadPayloadBuilder(options.graph);
  const payload = buildNoteExportPayload(CONSULTA_SINTETICA);
  const mirror = parseMirror(options.mirror);
  const problems = [];

  compare('ExportPayload', Object.keys(payload), mirror.ExportPayload || new Set(), problems);
  compare('ExportSignature', Object.keys(payload.firma || {}), mirror.ExportSignature || new Set(), problems);
  if (payload.note?.length) {
    compare('ExportNoteSection', Object.keys(payload.note[0]), mirror.ExportNoteSection || new Set(), problems);
  }
  if (payload.codigos?.length) {
    compare('ExportCode', Object.keys(payload.codigos[0]), mirror.ExportCode || new Set(), problems);
  }

  // Invariantes del contrato que el cliente da por ciertas. Si dejan de cumplirse, el ejecutor está
  // apoyado en una suposición falsa y vale más enterarse aquí que en el hospital.
  console.log('\n  Invariantes del payload:');
  const invariants = [
    ['solo viajan códigos aceptados',
      payload.codigos.length === 1 && payload.codigos[0].codigo === 'Z000'],
    ['patient_ref es el uuid, no un nombre',
      payload.patient_ref === CONSULTA_SINTETICA.patient_id],
    ['el payload NO lleva nombre ni documento del paciente',
      !JSON.stringify(payload).match(/\b(nombre|documento|cedula|cédula|apellido)\b/i)],
    ['rendered_text y context coinciden',
      payload.rendered_text === payload.context],
    ['rendered_text incluye las secciones de lista',
      payload.rendered_text.includes('- Punto uno')]
  ];
  for (const [name, ok] of invariants) {
    console.log(`  ${ok ? '✓' : '✗'} ${name}`);
    if (!ok) problems.push(`invariante rota: ${name}`);
  }

  // El payload NO trae identificador del HIS. No es un fallo del contrato: es una decisión de
  // minimización de PHI. Se afirma aquí para que el día que cambie, el cliente se entere.
  const hisIdentifier = ['patient_number', 'mrn', 'historia', 'his_patient_id', 'patnr']
    .find((key) => key in payload);
  console.log(`\n  ${hisIdentifier ? '!' : 'i'} identificador del paciente en el HIS: ` +
              `${hisIdentifier ? `presente (\`${hisIdentifier}\`) — PatientGuard puede verificar` : 'AUSENTE por diseño — ver PatientGuard'}`);

  if (problems.length) {
    console.error(`\n✗ ${problems.length} problema(s):`);
    for (const problem of problems) console.error(`  · ${problem}`);
    process.exit(1);
  }
  console.log('\n✓ El contrato-espejo cubre el payload que Graph emite hoy.');
}

main();
