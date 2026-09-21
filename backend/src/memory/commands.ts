import { MemoryStore, MemoryItem, Reminder } from './store';
import { buildTimeContext, currentLocal, localDateFrom, localToUtc, weekdayIndex, LocalDateTime, TemporalResolutionError } from '../time/context';

export type MemoryCommandResult =
  | { kind: 'remember'; item: MemoryItem; response: string }
  | { kind: 'remind'; reminder: Reminder; response: string }
  | { kind: 'clarify'; response: string }
  | null;

export interface TemporalOptions { now?: Date; timezone?: string; locale?: string; clientNowUtc?: string; }

function localDateText(date: Date, timezone: string, locale: string): string {
  return new Intl.DateTimeFormat(locale, { dateStyle: 'full', timeStyle: 'short', timeZone: timezone }).format(date);
}

function isReminderText(text: string, command: string): boolean {
  return /acu[eé]rda|no olvides/i.test(command)
    || /\b(hoy|mañana|pasado mañana|en\s+\d+\s+(?:minutos?|horas?|días?)|a\s+(?:(?:las?|la)\s+\d{1,2}(?::\d{2})?|mediodía|medianoche)|el\s+(?:próximo\s+)?(?:lunes|martes|miércoles|jueves|viernes|sábado|domingo))\b/i.test(text);
}

function cleanTitle(payload: string): string {
  return payload
    .replace(/\bpasado mañana\b/gi, '').replace(/\bmañana\b/gi, '').replace(/\bhoy\b/gi, '')
    .replace(/\ben\s+\d+\s+(?:minutos?|horas?|días?)\b/gi, '')
    .replace(/\ba\s+(?:las?|la)\s+(?:\d{1,2}(?::\d{2})?|mediodía|medianoche)\b/gi, '')
    .replace(/\bel\s+(?:próximo\s+)?(?:lunes|martes|miércoles|jueves|viernes|sábado|domingo)\b/gi, '')
    .replace(/\s+/g, ' ').trim().replace(/[,.]$/, '') || 'Recordatorio';
}

function resolveReminder(payload: string, context: ReturnType<typeof buildTimeContext>): { dueAt: string; timezone: string } {
  const now = new Date(context.nowUtc);
  const timezone = context.timezone;
  const local = currentLocal(context);

  const inMatch = payload.match(/\ben\s+(\d+)\s+(minutos?|horas?|días?)\b/i);
  if (inMatch) {
    const amount = Number(inMatch[1]);
    const unit = inMatch[2].toLocaleLowerCase();
    const ms = unit.startsWith('día') ? amount * 86_400_000 : unit.startsWith('hora') ? amount * 3_600_000 : amount * 60_000;
    return { dueAt: new Date(now.valueOf() + ms).toISOString(), timezone };
  }

  let dayOffset = /\bpasado mañana\b/i.test(payload) ? 2 : /\bmañana\b/i.test(payload) ? 1 : 0;
  const weekdayMatch = payload.match(/\b(?:el\s+)?(próximo\s+)?(lunes|martes|miércoles|jueves|viernes|sábado|domingo)\b/i);
  if (weekdayMatch) {
    const target = weekdayIndex(weekdayMatch[2]);
    if (target != null) {
      const current = new Date(Date.UTC(local.year, local.month - 1, local.day)).getUTCDay();
      let delta = (target - current + 7) % 7;
      if (delta === 0 || weekdayMatch[1]) delta = delta || 7;
      dayOffset = delta;
    }
  }

  const timeMatch = payload.match(/\ba\s+(?:las?|la)\s+(\d{1,2})(?::(\d{2}))?\b/i);
  const noon = /\ba\s+mediodía\b/i.test(payload);
  const midnight = /\ba\s+medianoche\b/i.test(payload);
  const explicitTime = Boolean(timeMatch || noon || midnight);
  const hour = noon ? 12 : midnight ? 0 : timeMatch ? Number(timeMatch[1]) : (local.hour + 1) % 24;
  const minute = timeMatch?.[2] ? Number(timeMatch[2]) : (explicitTime ? 0 : local.minute);
  const target: LocalDateTime = { ...localDateFrom(local, dayOffset), hour, minute };
  let due = localToUtc(target, timezone);
  if (dayOffset === 0 && explicitTime && due.valueOf() <= now.valueOf()) {
    due = localToUtc({ ...localDateFrom(local, 1), hour, minute }, timezone);
  }
  return { dueAt: due.toISOString(), timezone };
}

/** Interpreta solo órdenes explícitas; las inferencias nunca escriben memoria silenciosamente. */
export async function applyExplicitMemoryCommand(
  store: MemoryStore, userId: string, raw: string, options: Date | TemporalOptions = {},
): Promise<MemoryCommandResult> {
  const text = raw.trim();
  const match = text.match(/^(?:hey[, ]*)?(acu[eé]rda(?:me|melo)|recuerda(?:me)?|no olvides)\s+(.+)$/i);
  if (!match) return null;
  const temporal = options instanceof Date ? { now: options } : options;
  const context = buildTimeContext({ ...temporal, nowUtc: temporal.now });
  const payload = match[2].trim();
  const reminder = isReminderText(payload, match[1]);

  if (!reminder) {
    const item = await store.remember(userId, 'general', payload, { kind: 'fact', confidence: 'explicit', source: 'explicit-command' });
    return { kind: 'remember', item, response: 'Listo, lo guardaré como un recuerdo y lo traeré cuando sea útil.' };
  }

  let resolved: { dueAt: string; timezone: string };
  try { resolved = resolveReminder(payload, context); }
  catch (error) {
    if (error instanceof TemporalResolutionError) {
      return { kind: 'clarify', response: error.code === 'ambiguous'
        ? `La hora indicada ocurre dos veces en ${context.timezone}. ¿Quieres la primera o la segunda?`
        : `La hora indicada no existe por un cambio de horario en ${context.timezone}. ¿Qué hora quieres usar?` };
    }
    throw error;
  }
  const scheduled = await store.scheduleReminder({ userId, title: cleanTitle(payload), dueAt: resolved.dueAt, timezone: resolved.timezone, source: 'explicit-command' });
  return { kind: 'remind', reminder: scheduled, response: `Hecho. Te recordaré «${scheduled.title}» el ${localDateText(new Date(scheduled.dueAt), scheduled.timezone, context.locale)}.` };
}
