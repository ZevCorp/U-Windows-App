import { config } from '../config.js';
import { deps } from '../container.js';

export interface HttpResult { status: number; json: unknown; }
function auth(authHeader?: string): HttpResult | null {
  if (!config.clientToken) return null;
  const token = (authHeader || '').replace(/^Bearer\s+/i, '');
  return token === config.clientToken ? null : { status: 401, json: { error: 'no autorizado' } };
}
function user(body: Record<string, unknown>): string { return typeof body.userId === 'string' && body.userId.trim() ? body.userId.trim() : 'anon'; }

export async function handleMemory(body: Record<string, unknown>, authHeader?: string): Promise<HttpResult> {
  const denied = auth(authHeader); if (denied) return denied;
  const store = deps().memory;
  const userId = user(body);
  const query = typeof body.query === 'string' ? body.query.trim() : '';
  if (query) return { status: 200, json: { memories: await store.search(userId, query, Number(body.limit) || 10), reminders: (await store.snapshot(userId)).reminders } };
  const snapshot = await store.snapshot(userId);
  return { status: 200, json: snapshot };
}

export async function handleRemember(body: Record<string, unknown>, authHeader?: string): Promise<HttpResult> {
  const denied = auth(authHeader); if (denied) return denied;
  const note = typeof body.note === 'string' ? body.note.trim() : '';
  if (!note) return { status: 400, json: { error: 'falta `note`' } };
  const item = await deps().memory.remember(user(body), typeof body.app === 'string' ? body.app : 'general', note, {
    kind: body.kind === 'preference' || body.kind === 'commitment' || body.kind === 'instruction' ? body.kind : 'fact',
    confidence: body.confidence === 'confirmed' || body.confidence === 'inferred' ? body.confidence : 'explicit',
    source: 'api',
  });
  return { status: 201, json: item };
}

export async function handleForget(body: Record<string, unknown>, authHeader?: string): Promise<HttpResult> {
  const denied = auth(authHeader); if (denied) return denied;
  const memoryId = typeof body.memoryId === 'string' ? body.memoryId : '';
  if (!memoryId) return { status: 400, json: { error: 'falta `memoryId`' } };
  const forgotten = await deps().memory.forget(user(body), memoryId);
  return forgotten ? { status: 200, json: { ok: true, memoryId } } : { status: 404, json: { error: 'recuerdo no encontrado' } };
}

export async function handleScheduleReminder(body: Record<string, unknown>, authHeader?: string): Promise<HttpResult> {
  const denied = auth(authHeader); if (denied) return denied;
  if (typeof body.title !== 'string' || typeof body.dueAt !== 'string') return { status: 400, json: { error: 'faltan `title` y `dueAt`' } };
  const reminder = await deps().memory.scheduleReminder({ userId: user(body), title: body.title, dueAt: body.dueAt, timezone: typeof body.timezone === 'string' ? body.timezone : 'UTC', recurrence: typeof body.recurrence === 'string' ? body.recurrence : undefined, source: 'api' });
  return { status: 201, json: reminder };
}

export async function handleDueReminders(body: Record<string, unknown>, authHeader?: string): Promise<HttpResult> {
  if (body.userId) {
    const denied = auth(authHeader); if (denied) return denied;
    return { status: 200, json: { reminders: await deps().memory.dueReminders(user(body), { limit: Number(body.limit) || 20 }) } };
  }
  const cronSecret = process.env.CRON_SECRET || '';
  if (!cronSecret || authHeader?.replace(/^Bearer\s+/i, '') !== cronSecret) return { status: 401, json: { error: 'el worker requiere CRON_SECRET' } };
  const snapshot = await deps().memory.snapshot();
  const users = [...new Set(snapshot.reminders.filter((r) => r.status !== 'delivered' && r.status !== 'cancelled').map((r) => r.userId))];
  const reminders = (await Promise.all(users.map((id) => deps().memory.dueReminders(id, { limit: Number(body.limit) || 20 })))).flat();
  return { status: 200, json: { reminders, leasedAt: new Date().toISOString() } };
}

export async function handleReminderMutation(body: Record<string, unknown>, action: 'complete' | 'cancel', authHeader?: string): Promise<HttpResult> {
  const denied = auth(authHeader); if (denied) return denied;
  const reminderId = typeof body.reminderId === 'string' ? body.reminderId : '';
  if (!reminderId) return { status: 400, json: { error: 'falta `reminderId`' } };
  const reminder = action === 'complete' ? await deps().memory.completeReminder(user(body), reminderId, body.delivered !== false) : await deps().memory.cancelReminder(user(body), reminderId);
  return reminder ? { status: 200, json: reminder } : { status: 404, json: { error: 'recordatorio no encontrado' } };
}
