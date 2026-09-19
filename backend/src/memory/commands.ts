import { MemoryStore, MemoryItem, Reminder } from './store';

export type MemoryCommandResult =
  | { kind: 'remember'; item: MemoryItem; response: string }
  | { kind: 'remind'; reminder: Reminder; response: string }
  | null;

function nextLocalDate(now: Date, dayOffset: number, hour: number, minute: number): string {
  const d = new Date(now);
  d.setDate(d.getDate() + dayOffset);
  d.setHours(hour, minute, 0, 0);
  return d.toISOString();
}

/** Interpreta solo órdenes explícitas; las inferencias nunca escriben memoria silenciosamente. */
export async function applyExplicitMemoryCommand(store: MemoryStore, userId: string, raw: string, now = new Date()): Promise<MemoryCommandResult> {
  const text = raw.trim();
  const match = text.match(/^(?:hey[, ]*)?(acu[eé]rda(?:me|melo)|recuerda(?:me)?|no olvides)\s+(.+)$/i);
  if (!match) return null;
  let payload = match[2].trim();
  const reminder = /acu[eé]rda|no olvides/i.test(match[1]) || /\b(mañana|hoy|en\s+\d+\s+(?:minutos?|horas?)|a\s+las?\s+\d{1,2}(?::\d{2})?)\b/i.test(payload);

  if (!reminder) {
    const item = await store.remember(userId, 'general', payload, { kind: 'fact', confidence: 'explicit', source: 'explicit-command' });
    return { kind: 'remember', item, response: 'Listo, lo guardaré como un recuerdo y lo traeré cuando sea útil.' };
  }

  let dueAt: string;
  const inMatch = payload.match(/\ben\s+(\d+)\s+(minutos?|horas?)\b/i);
  const timeMatch = payload.match(/\ba\s+las?\s+(\d{1,2})(?::(\d{2}))?\b/i);
  const dayOffset = /\bmañana\b/i.test(payload) ? 1 : 0;
  if (inMatch) {
    const amount = Number(inMatch[1]) * (inMatch[2].toLocaleLowerCase().startsWith('hora') ? 60 : 1) * 60_000;
    dueAt = new Date(now.valueOf() + amount).toISOString();
    payload = payload.replace(inMatch[0], '').trim();
  } else {
    const hour = timeMatch ? Number(timeMatch[1]) : now.getHours() + (dayOffset ? 0 : 1);
    const minute = timeMatch?.[2] ? Number(timeMatch[2]) : (dayOffset ? 0 : now.getMinutes());
    dueAt = nextLocalDate(now, dayOffset, hour, minute);
    if (!dayOffset && !timeMatch && Date.parse(dueAt) <= now.valueOf()) dueAt = nextLocalDate(now, 1, hour, minute);
    if (timeMatch) payload = payload.replace(timeMatch[0], '').trim();
  }
  payload = payload.replace(/\b(mañana|hoy)\b/gi, '').replace(/\s+/g, ' ').trim().replace(/[,.]$/, '');
  const scheduled = await store.scheduleReminder({ userId, title: payload || 'Recordatorio', dueAt, source: 'explicit-command' });
  return { kind: 'remind', reminder: scheduled, response: `Hecho. Te recordaré «${scheduled.title}» el ${scheduled.dueAt}.` };
}
