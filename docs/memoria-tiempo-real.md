# Memoria de You en tiempo real

## Decisión

You necesita una **memoria híbrida y temporal**, no una bolsa de embeddings ni una ventana de
contexto más grande. Cada episodio se convierte en un hecho versionado; los hechos comparten nodos
de personas, proyectos, lugares y eventos; las relaciones tienen vigencia; y el recuperador combina
texto, recencia, confianza y vecindad del grafo. El contexto que ve el modelo es una proyección corta
de ese grafo, nunca el grafo completo.

Esta decisión recoge cuatro ideas que hoy convergen en los sistemas más sólidos:

- Graphiti/Zep usa un grafo de conocimiento temporal con tiempo del evento y tiempo de ingestión,
  además de búsqueda híbrida semántica, léxica y por recorrido de grafo.
- Mem0 combina memoria vectorial y relaciones de grafo; su propia documentación advierte que el
  grafo aporta contexto relacionado pero no debe sustituir al ranking principal.
- Letta separa memoria de trabajo (siempre visible) y archivo (consultado bajo demanda), y evalúa por
  separado leer, escribir y actualizar memoria.
- El patrón de wiki de Karpathy es una buena interfaz humana: páginas legibles, enlaces y revisión
  explícita. En You lo usamos como proyección/exportación del grafo, no como fuente única de verdad.

Referencias: [Zep: Temporal Knowledge Graph Architecture](https://arxiv.org/abs/2501.13956),
[Graphiti](https://github.com/getzep/graphiti), [Mem0 Graph Memory](https://docs.mem0.ai/open-source/features/graph-memory),
[Mem0 paper](https://arxiv.org/abs/2504.19413), [Letta memory hierarchy](https://www.letta.com/blog/letta-leaderboard/).

## Anclaje temporal: el reloj también es memoria de trabajo

La versión actual **todavía no resuelve bien el tiempo para un asistente general**. Usa `Date.now()`
y `Date` del proceso para comparar vencimientos; el parser entiende algunos casos (`hoy`, `mañana`,
`en N minutos/horas`), pero no recibe de forma explícita la zona horaria del usuario, no inyecta un
reloj verificable en cada prompt y el endpoint API asume UTC cuando no se le pasa una zona. Eso es
suficiente para una prueba local sencilla, no para prometer «¿qué día es hoy?» con precisión mundial.

La siguiente pieza de la arquitectura debe ser un `TimeContext` firmado y presente en cada turno:

```text
serverNowUtc       instante autoritativo del backend
userZone           IANA, por ejemplo America/Bogota
userNow            serverNowUtc proyectado a userZone
localDate          2026-09-19
weekday            sábado
offset             -05:00, incluyendo cambios DST
dayBounds          inicio y fin del día local en UTC
locale             es-CO (formato, no autoridad temporal)
```

El servidor es la autoridad del instante; Windows aporta la zona horaria configurada y un muestreo
del reloj para detectar una máquina desfasada. En el prompt se incluirá una línea estructurada como
`AHORA: sábado 19 de septiembre de 2026, 08:42, America/Bogota (-05:00)` y el modelo no tendrá que
adivinarla. Las fechas persistidas siempre conservarán `dueAtUtc`, `timezone`, la frase original y
la resolución que se tomó.

El resolver temporal debe usar una biblioteca con reglas IANA/DST (Temporal con polyfill o equivalente)
y producir un resultado tipado: `exact`, `relative`, `recurring` o `ambiguous`. «Mañana a las 9» se
resuelve en la zona del usuario; «el próximo lunes» conserva el lunes de la semana correcta; una hora
ambigua por cambio de horario pregunta antes de programar. Nunca se debe convertir primero a UTC y
después intentar reconstruir el día local.

Antes de activar recordatorios para usuarios reales hay que probar medianoche, fin de mes, año nuevo,
semanas que cruzan domingo/lunes, DST, zonas con media hora, reloj local incorrecto, reinicio del
servidor y dos dispositivos en zonas diferentes. La métrica es exactitud del día/hora local y cero
duplicados, no solo que `Date.parse` acepte la cadena.

## Capas

1. **Episodio**: turno, acción, resultado y fuente. Es inmutable y permite reconstruir por qué se
   creó un recuerdo.
2. **Hecho**: texto breve, tipo, confianza, importancia y `supersededBy`. Actualizar una preferencia
   crea una nueva versión; nunca borra silenciosamente la anterior.
3. **Grafo temporal**: nodos canónicos y aristas tipadas (`prefiere`, `trabaja_en`, `vive_en`,
   `compromiso_con`). Cada arista tiene `validFrom`, `validUntil`, fuente y confianza.
4. **Memoria de trabajo**: una proyección de 8–12 hechos relevantes para el objetivo actual, más los
   compromisos próximos. Se regenera en cada turno y está limitada por presupuesto de tokens.
5. **Cola de compromisos**: recordatorios con `scheduled → leased → delivered`, lease con expiración,
   contador de intentos y reintento seguro. El worker puede caerse sin perder el compromiso.

La implementación de referencia está en `backend/src/memory/store.ts`. No depende de un proveedor y
activa un archivo local atómico con `MEMORY_FILE`. Es una base funcional para medir latencia y
semántica; el despliegue multiusuario debe reemplazarla por el adaptador durable.

## Escrituras seguras

- `recuerda que ...` y `acuérdame ...` son órdenes explícitas y se escriben inmediatamente.
- Las observaciones inferidas por el modelo deben entrar con `confidence=inferred`; no se convierten
  en una preferencia crítica sin confirmación.
- La deduplicación no destruye historial: marca la versión anterior como superseded.
- `forget` realiza borrado lógico por usuario; un adaptador de producción debe ejecutar además la
política de retención y borrado físico solicitada por el usuario.

## Continuidad entre sesiones de voz

La voz en vivo tiene hoy una sesión de transporte, no una conversación durable. `AlternarAsync()`
llama a `TerminarAsync()`, el WebSocket se destruye y el siguiente `ArrancarAsync()` abre el protocolo
con un pase vacío, limpia las llamadas pendientes y crea un `sesionId` nuevo. El modelo conserva el
contexto mientras ese socket vive, pero apagar el micrófono equivale a pedirle al proveedor una
conversación nueva. La propiedad `SabeVolver` solo cubre algunos cortes de red; no resuelve el cierre
intencional ni la continuidad entre dispositivos.

El patrón correcto es separar **transporte**, **hilo de conversación** y **memoria**:

```text
micrófono / WebSocket (efímero)
            │
            ▼
threadId durable ── event log de turnos ── snapshot de continuidad
            │                 │                    │
            └──────── memoria grafo + compromisos ┘
```

El `threadId` debe vivir en el cliente y pertenecer al usuario, no al WebSocket. Cada turno cerrado
escribe un evento idempotente con transcripción, respuesta, herramientas, resultado, pantalla de
trabajo y `timeContext`. Un compactador mantiene un snapshot pequeño y legible:

```text
objetivo activo
plan y paso siguiente
qué ya se consiguió
bloqueos y preguntas abiertas
entidades y preferencias relevantes
última aplicación/superficie conocida
compromisos creados o pendientes
```

Al volver a abrir la voz, el cliente reusa el mismo `threadId`. El backend devuelve un paquete de
reanudación limitado por tokens: snapshot + últimos turnos + subgrafo relevante. El socket nuevo se
inicializa con ese paquete y una instrucción explícita: «continúa desde aquí; no pidas repetir lo que
ya está confirmado». Si el proveedor permite retomar una conversación, se usa ese identificador como
optimización; la fuente de verdad sigue siendo nuestro log, porque el contexto del proveedor es
efímero y tiene límites propios. La Realtime API mantiene los ítems dentro de una conversación
mientras la sesión está viva, pero eso no sustituye un hilo durable de la aplicación ([referencia
oficial](https://platform.openai.com/docs/api-reference/realtime-server-events/input_audio_buffer/committed?lang=node)).

Apagar el micrófono debe cambiar solamente `captureState=off`. «Nueva conversación» será una acción
distinta y explícita que crea otro `threadId`. Así el usuario puede pausar, cambiar de dispositivo o
volver al día siguiente sin sentir que cada encendido empieza una relación nueva.

La continuidad no debe meter todo el historial en cada prompt. El orden de recuperación es:

1. snapshot de continuidad del hilo;
2. hechos y compromisos relevantes del grafo;
3. últimos turnos necesarios para resolver pronombres y referencias;
4. historial completo solo bajo una herramienta de auditoría o búsqueda.

La escritura se hace al cerrar cada turno, no al cerrar el socket. Si el proceso muere a mitad de una
respuesta, se conserva el último evento confirmado y se marca el turno como incompleto para que la
reanudación diga qué quedó pendiente en vez de inventar que terminó. Las grabaciones de audio no se
guardan por defecto: se conserva transcripción, resumen y eventos de herramientas, con una política
de retención explícita para audio si el usuario lo activa.

El endpoint `/api/agent/turn` reconoce la orden explícita incluso cuando el proveedor LLM está caído,
por lo que «acuérdame llamar a Ana mañana a las 9» no depende de una ventana de contexto ni de una
respuesta del modelo. También están disponibles `/api/memory`, `/api/reminders/due`,
`/api/reminders/complete` y `/api/reminders/cancel`.

## Recordatorios fiables

`/api/reminders/due` devuelve trabajos vencidos y los arrienda durante 30 segundos. El consumidor
entrega la notificación (Windows toast, voz en vivo o canal remoto) y confirma con
`/api/reminders/complete`. Si muere antes de confirmar, el lease expira y otro worker puede reintentar.
En Vercel se programa cada minuto mediante `vercel.json`; `CRON_SECRET` protege la ejecución global.

Para producción, el worker debe ser externo a la función efímera y registrar un `deliveryId`
idempotente por canal. Recurrencias deben materializar la siguiente ocurrencia solo después de una
entrega confirmada. El tono de voz puede ser adaptativo, pero el contrato de entrega debe seguir
siendo mecánico y observable.

## Persistencia de producción

La primera opción recomendada es Postgres/Supabase para episodios, hechos y recordatorios, con
`pgvector` para embeddings y una tabla de aristas indexada por `from`, `to` y `validFrom`. Si el
recorrido multi-hop se vuelve dominante, Neo4j/Graphiti es el siguiente paso. No se debe meter un
servicio remoto en el camino crítico de cada palabra de Realtime: las escrituras se encolan y la
lectura usa una caché local de memoria de trabajo.

Esquema mínimo:

```text
episodes(id, user_id, session_id, occurred_at, payload, source)
memory_items(id, user_id, kind, text, confidence, importance, created_at, superseded_by)
memory_nodes(id, user_id, type, canonical_label, aliases)
memory_edges(id, user_id, from_id, relation, to_id, valid_from, valid_until, confidence, source)
reminders(id, user_id, due_at, timezone, recurrence, status, lease_until, attempts, delivery_id)
```

## Medición antes de llamarlo «frontera»

La calidad se juzga con un conjunto propio de conversaciones anonimizadas: recordar un hecho
explícito, actualizarlo, resolver una referencia («él», «ese proyecto»), responder «¿qué era cierto
el martes?» y entregar un recordatorio tras reinicio. Medir p50/p95 de lectura, tokens inyectados,
precisión de hechos vigentes, falsos recuerdos, duplicados, pérdida tras crash y entregas duplicadas.

Fases siguientes:

1. Añadir extracción LLM asíncrona con confirmación y resolución de entidades; conservar la escritura
   explícita sin LLM.
2. Implementar adaptador Supabase/Postgres y un índice híbrido (BM25 + vector + grafo) con caché local.
3. Conectar el cliente Windows al lease/ack para toasts y voz, con deduplicación por `deliveryId`.
4. Añadir exportación wiki Markdown con backlinks y una vista de auditoría de «por qué You recuerda
   esto».
5. Ejecutar los escenarios de crash, zona horaria, cambios de preferencia y multi-dispositivo antes
   de activar recurrencias para usuarios reales.
6. Añadir `TimeContext` al contrato de turno, al prompt y a la cola de recordatorios; resolver frases
   relativas con IANA/DST y pedir confirmación cuando sean ambiguas.
7. Separar el WebSocket de la conversación: `threadId`, log idempotente de turnos, snapshot de
   continuidad y paquete de reanudación al abrir la voz.
8. Cambiar el cliente para que apagar/prender sea `captureState`, y reservar la creación de un hilo
   nuevo para una acción explícita.
9. Añadir pruebas de pausa, cierre intencional, corte de red, cambio de dispositivo, tarea incompleta
   y dos turnos que llegan simultáneamente.
