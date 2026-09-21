/** Tiempo del asistente: la zona local es parte del contrato, no una inferencia del modelo. */
export interface TimeContext {
  nowUtc: string;
  timezone: string;
  locale: string;
  localDate: string;
  localTime: string;
  weekday: string;
  offset: string;
  dayStartUtc: string;
  dayEndUtc: string;
  clockSkewMinutes?: number;
}

export interface LocalDateTime { year: number; month: number; day: number; hour: number; minute: number; }
export class TemporalResolutionError extends Error {
  constructor(public readonly code: 'nonexistent' | 'ambiguous', message: string) { super(message); }
}

const formatters = new Map<string, Intl.DateTimeFormat>();

function validZone(zone: string | undefined): string {
  const candidate = (zone || '').trim() || 'UTC';
  try { return new Intl.DateTimeFormat('en-US', { timeZone: candidate }).resolvedOptions().timeZone; }
  catch { return 'UTC'; }
}

function validLocale(locale: string | undefined): string {
  const candidate = (locale || '').trim() || 'es-CO';
  try { new Intl.DateTimeFormat(candidate); return candidate; }
  catch { return 'es-CO'; }
}

function zonedParts(at: Date, timezone: string): Record<string, number> {
  let fmt = formatters.get(timezone);
  if (!fmt) {
    fmt = new Intl.DateTimeFormat('en-CA', {
      timeZone: timezone, year: 'numeric', month: '2-digit', day: '2-digit',
      hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23',
    });
    formatters.set(timezone, fmt);
  }
  return Object.fromEntries(fmt.formatToParts(at).filter((p) => p.type !== 'literal').map((p) => [p.type, Number(p.value)]));
}

function offsetMinutes(at: Date, timezone: string): number {
  const p = zonedParts(at, timezone);
  const wall = Date.UTC(p.year, p.month - 1, p.day, p.hour, p.minute, p.second);
  return Math.round((wall - at.valueOf()) / 60_000);
}

function offsetText(minutes: number): string {
  const sign = minutes < 0 ? '-' : '+';
  const abs = Math.abs(minutes);
  return `${sign}${String(Math.floor(abs / 60)).padStart(2, '0')}:${String(abs % 60).padStart(2, '0')}`;
}

function dateTimeParts(at: Date, timezone: string): LocalDateTime {
  const p = zonedParts(at, timezone);
  return { year: p.year, month: p.month, day: p.day, hour: p.hour, minute: p.minute };
}

function sameLocal(a: Record<string, number>, target: LocalDateTime): boolean {
  return a.year === target.year && a.month === target.month && a.day === target.day
    && a.hour === target.hour && a.minute === target.minute;
}

/** Convierte una fecha de pared en una zona IANA, incluyendo offsets de 30/45 minutos y DST. */
export function localToUtc(local: LocalDateTime, timezoneInput: string, allowAmbiguous = false): Date {
  const timezone = validZone(timezoneInput);
  const wallMs = Date.UTC(local.year, local.month - 1, local.day, local.hour, local.minute, 0);
  const matches: Date[] = [];
  // Los offsets actuales del mundo están entre -12:00 y +14:00. Barrer minutos evita asumir
  // que todas las zonas tienen horas enteras y permite detectar horas dobles o inexistentes de DST.
  for (let offset = -12 * 60; offset <= 14 * 60; offset++) {
    const candidate = new Date(wallMs - offset * 60_000);
    if (sameLocal(zonedParts(candidate, timezone), local)) matches.push(candidate);
  }
  if (matches.length === 0) throw new TemporalResolutionError('nonexistent', `la hora ${local.year}-${local.month}-${local.day} ${local.hour}:${String(local.minute).padStart(2, '0')} no existe en ${timezone} por un cambio de horario`);
  if (matches.length > 1 && !allowAmbiguous) throw new TemporalResolutionError('ambiguous', `la hora ${local.year}-${local.month}-${local.day} ${local.hour}:${String(local.minute).padStart(2, '0')} ocurre dos veces en ${timezone}`);
  // En la hora repetida elegimos la primera aparición; queda determinista y no desplaza el día.
  return new Date(Math.min(...matches.map((m) => m.valueOf())));
}

function startOfDayUtc(date: LocalDateTime, timezone: string): string {
  return localToUtc({ ...date, hour: 0, minute: 0 }, timezone, true).toISOString();
}

function endOfDayUtc(date: LocalDateTime, timezone: string): string {
  const nextWall = new Date(Date.UTC(date.year, date.month - 1, date.day + 1));
  return localToUtc({ year: nextWall.getUTCFullYear(), month: nextWall.getUTCMonth() + 1, day: nextWall.getUTCDate(), hour: 0, minute: 0 }, timezone, true).toISOString();
}

export function buildTimeContext(options: { timezone?: string; locale?: string; nowUtc?: Date; clientNowUtc?: string }): TimeContext {
  const now = options.nowUtc ?? new Date();
  const timezone = validZone(options.timezone);
  const locale = validLocale(options.locale);
  const local = dateTimeParts(now, timezone);
  const weekday = new Intl.DateTimeFormat(locale, { weekday: 'long', timeZone: timezone }).format(now);
  const offset = offsetMinutes(now, timezone);
  const result: TimeContext = {
    nowUtc: now.toISOString(), timezone, locale,
    localDate: `${local.year}-${String(local.month).padStart(2, '0')}-${String(local.day).padStart(2, '0')}`,
    localTime: `${String(local.hour).padStart(2, '0')}:${String(local.minute).padStart(2, '0')}`,
    weekday, offset: offsetText(offset),
    dayStartUtc: startOfDayUtc(local, timezone), dayEndUtc: endOfDayUtc(local, timezone),
  };
  if (options.clientNowUtc) {
    const client = Date.parse(options.clientNowUtc);
    if (Number.isFinite(client)) result.clockSkewMinutes = Math.round((client - now.valueOf()) / 60_000);
  }
  return result;
}

export function timePrompt(context: TimeContext): string {
  const skew = context.clockSkewMinutes != null && Math.abs(context.clockSkewMinutes) >= 2
    ? ` El reloj del computador difiere aproximadamente ${Math.abs(context.clockSkewMinutes)} minutos; usa el servidor como autoridad.` : '';
  return `RELOJ VERIFICADO: ${context.weekday} ${context.localDate}, ${context.localTime}, zona ${context.timezone} (UTC${context.offset}).${skew}`;
}

export function localDateFrom(base: LocalDateTime, dayOffset: number): LocalDateTime {
  const next = new Date(Date.UTC(base.year, base.month - 1, base.day + dayOffset));
  return { year: next.getUTCFullYear(), month: next.getUTCMonth() + 1, day: next.getUTCDate(), hour: base.hour, minute: base.minute };
}

export function currentLocal(context: TimeContext): LocalDateTime {
  const [year, month, day] = context.localDate.split('-').map(Number);
  const [hour, minute] = context.localTime.split(':').map(Number);
  return { year, month, day, hour, minute };
}

export function weekdayIndex(name: string): number | null {
  const normalized = name.toLocaleLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, '');
  const names = ['domingo', 'lunes', 'martes', 'miercoles', 'jueves', 'viernes', 'sabado'];
  const idx = names.indexOf(normalized);
  return idx >= 0 ? idx : null;
}
