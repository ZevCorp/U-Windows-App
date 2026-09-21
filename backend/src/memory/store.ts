import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';

/**
 * Memoria de You: un grafo temporal pequeño, con una cola de compromisos.
 * El contrato permite cambiar esta implementación por Neo4j + un índice vectorial sin cambiar el
 * cerebro. La versión incluida no tiene dependencias: sirve para desarrollo, tests e instalaciones
 * locales; MEMORY_FILE activa persistencia atómica en disco.
 */
export type MemoryKind = 'fact' | 'preference' | 'commitment' | 'episode' | 'instruction';
export type MemoryConfidence = 'explicit' | 'confirmed' | 'inferred';
export type MemoryNodeType = 'person' | 'place' | 'organization' | 'project' | 'object' | 'event' | 'concept';

export interface MemoryNode {
  id: string; userId: string; type: MemoryNodeType; label: string; aliases: string[];
  createdAt: string; lastSeenAt: string;
}

export interface MemoryEdge {
  id: string; userId: string; from: string; relation: string; to: string;
  validFrom: string | null; validUntil: string | null; recordedAt: string;
  confidence: MemoryConfidence; source: string; supersedes?: string;
}

export interface MemoryItem {
  id: string; userId: string; kind: MemoryKind; text: string; app: string; tags: string[];
  importance: number; confidence: MemoryConfidence; source: string; createdAt: string;
  lastAccessedAt: string; supersededBy?: string; nodeIds: string[]; edgeIds: string[];
}

export type ReminderStatus = 'scheduled' | 'leased' | 'delivered' | 'snoozed' | 'cancelled';
export interface Reminder {
  id: string; userId: string; title: string; dueAt: string; timezone: string; recurrence?: string;
  status: ReminderStatus; attempts: number; leaseUntil?: string; lastAttemptAt?: string;
  deliveredAt?: string; createdAt: string; source: string;
}
export interface ReminderInput {
  userId: string; title: string; dueAt: string; timezone?: string; recurrence?: string; source?: string;
}
export interface DueReminderOptions { now?: Date; limit?: number; leaseMs?: number; }
export interface MemoryHit { item: MemoryItem; score: number; related: MemoryNode[]; }
export interface MemorySnapshot { items: MemoryItem[]; nodes: MemoryNode[]; edges: MemoryEdge[]; reminders: Reminder[]; }

export interface MemoryStore {
  forPrompt(userId: string, query?: string): Promise<string>;
  remember(userId: string, app: string, note: string, options?: Partial<Pick<MemoryItem, 'kind' | 'importance' | 'confidence' | 'source' | 'tags'>>): Promise<MemoryItem>;
  search(userId: string, query: string, limit?: number): Promise<MemoryHit[]>;
  scheduleReminder(input: ReminderInput): Promise<Reminder>;
  dueReminders(userId: string, options?: DueReminderOptions): Promise<Reminder[]>;
  completeReminder(userId: string, reminderId: string, delivered?: boolean): Promise<Reminder | null>;
  cancelReminder(userId: string, reminderId: string): Promise<Reminder | null>;
  forget(userId: string, memoryId: string): Promise<boolean>;
  snapshot(userId?: string): Promise<MemorySnapshot>;
}

interface PersistedState extends MemorySnapshot {}
const STOP = new Set('el la los las un una unos unas de del al y o en con para por mi mis tu tus que es soy me se a lo no hoy ya muy más como'.split(' '));
const WORDS = /[\p{L}\p{N}_@.-]+/gu;
const id = (prefix: string) => `${prefix}_${crypto.randomUUID()}`;
const nowIso = () => new Date().toISOString();
const tokens = (text: string): string[] => [...new Set((text.toLocaleLowerCase().match(WORDS) ?? []).filter((x) => x.length > 1 && !STOP.has(x)))];
const overlap = (a: string[], b: string[]): number => {
  if (!a.length || !b.length) return 0;
  const set = new Set(b);
  return a.filter((x) => set.has(x)).length / Math.max(a.length, 1);
};
function nodeType(label: string): MemoryNodeType {
  if (/^@/.test(label)) return 'person';
  if (/\b(proyecto|project|repo|repositorio)\b/i.test(label)) return 'project';
  return 'concept';
}
function safeDate(value: string): string {
  const parsed = new Date(value);
  if (Number.isNaN(parsed.valueOf())) throw new Error('dueAt debe ser una fecha ISO-8601 válida');
  return parsed.toISOString();
}

/** Store de referencia: event-friendly, con escritura atómica opcional en disco. */
export class GraphMemoryStore implements MemoryStore {
  private state: PersistedState = { items: [], nodes: [], edges: [], reminders: [] };
  private loaded = false;
  private writeChain: Promise<void> = Promise.resolve();

  constructor(private readonly filePath = process.env.MEMORY_FILE || '') {}

  private async ensureLoaded(): Promise<void> {
    if (this.loaded) return;
    this.loaded = true;
    if (!this.filePath) return;
    try {
      const raw = await fs.readFile(this.filePath, 'utf8');
      const parsed = JSON.parse(raw) as Partial<PersistedState>;
      this.state = {
        items: Array.isArray(parsed.items) ? parsed.items : [],
        nodes: Array.isArray(parsed.nodes) ? parsed.nodes : [],
        edges: Array.isArray(parsed.edges) ? parsed.edges : [],
        reminders: Array.isArray(parsed.reminders) ? parsed.reminders : [],
      };
    } catch (error) {
      if ((error as NodeJS.ErrnoException).code !== 'ENOENT') throw error;
    }
  }

  private async persist(): Promise<void> {
    if (!this.filePath) return;
    this.writeChain = this.writeChain.then(async () => {
      await fs.mkdir(path.dirname(this.filePath), { recursive: true });
      const tmp = `${this.filePath}.${process.pid}.tmp`;
      await fs.writeFile(tmp, JSON.stringify(this.state), 'utf8');
      await fs.rename(tmp, this.filePath);
    });
    await this.writeChain;
  }

  async remember(userId: string, app: string, note: string, options: Partial<Pick<MemoryItem, 'kind' | 'importance' | 'confidence' | 'source' | 'tags'>> = {}): Promise<MemoryItem> {
    await this.ensureLoaded();
    const text = note.trim();
    if (!userId.trim() || !text) throw new Error('userId y note son obligatorios');
    const createdAt = nowIso();
    const item: MemoryItem = {
      id: id('mem'), userId, kind: options.kind ?? 'fact', text, app: app.trim() || 'general',
      tags: options.tags ?? tokens(text).slice(0, 8), importance: Math.max(0, Math.min(1, options.importance ?? 0.7)),
      confidence: options.confidence ?? 'explicit', source: options.source ?? 'user', createdAt,
      lastAccessedAt: createdAt, nodeIds: [], edgeIds: [],
    };
    const words = new Set(tokens(text));
    const previous = this.state.items.find((candidate) => candidate.userId === userId && !candidate.supersededBy && candidate.kind === item.kind && overlap([...words], tokens(candidate.text)) >= 0.72);
    if (previous) previous.supersededBy = item.id;
    const labels = [...new Set(tokens(text).filter((t) => t.length >= 3).slice(0, 6))];
    for (const label of labels) {
      const existing = this.state.nodes.find((n) => n.userId === userId && (n.label === label || n.aliases.includes(label)));
      if (existing) { existing.lastSeenAt = createdAt; item.nodeIds.push(existing.id); continue; }
      const node: MemoryNode = { id: id('node'), userId, type: nodeType(label), label, aliases: [], createdAt, lastSeenAt: createdAt };
      this.state.nodes.push(node); item.nodeIds.push(node.id);
    }
    if (item.nodeIds.length >= 2) {
      const edge: MemoryEdge = { id: id('edge'), userId, from: item.nodeIds[0], relation: item.kind === 'preference' ? 'prefiere' : 'relacionado_con', to: item.nodeIds[1], validFrom: createdAt, validUntil: null, recordedAt: createdAt, confidence: item.confidence, source: item.source, supersedes: previous?.edgeIds[0] };
      this.state.edges.push(edge); item.edgeIds.push(edge.id);
    }
    this.state.items.push(item);
    await this.persist();
    return item;
  }

  async search(userId: string, query: string, limit = 8): Promise<MemoryHit[]> {
    await this.ensureLoaded();
    const q = tokens(query);
    const now = Date.now();
    const active = this.state.items.filter((i) => i.userId === userId && !i.supersededBy);
    const ranked = active.map((item) => {
      const lexical = overlap(q, [...tokens(item.text), ...item.tags]);
      const graphBoost = item.nodeIds.reduce((score, nodeId) => score + this.state.edges.filter((e) => e.userId === userId && (e.from === nodeId || e.to === nodeId) && !e.supersedes).length * 0.01, 0);
      const ageDays = Math.max(0, (now - Date.parse(item.createdAt)) / 86_400_000);
      const recency = Math.exp(-ageDays / 120) * 0.1;
      const explicit = item.confidence === 'explicit' || item.confidence === 'confirmed' ? 0.12 : 0;
      return { item, score: lexical * 0.68 + graphBoost + recency + explicit, related: this.state.nodes.filter((n) => item.nodeIds.includes(n.id)) };
    });
    ranked.sort((a, b) => b.score - a.score || b.item.createdAt.localeCompare(a.item.createdAt));
    const selected = ranked.filter((h) => h.score > 0.05).slice(0, limit);
    for (const hit of selected) hit.item.lastAccessedAt = nowIso();
    if (selected.length) await this.persist();
    return selected;
  }

  async forPrompt(userId: string, query = ''): Promise<string> {
    const hits = await this.search(userId, query || 'preferencias compromisos instrucciones contexto', 10);
    // Leer el prompt nunca adquiere un lease: solo el worker de entrega puede reclamar el trabajo.
    // Así una conversación normal no roba un recordatorio al canal de notificaciones.
    const due = (await this.snapshot(userId)).reminders
      .filter((r) => (r.status === 'scheduled' || r.status === 'leased') && Date.parse(r.dueAt) <= Date.now())
      .slice(0, 4);
    if (!hits.length && !due.length) return '';
    const lines = ['MEMORIA RECUPERADA (solo hechos con fuente; no inventes ni mezcles usuarios):'];
    for (const hit of hits) {
      const when = hit.item.createdAt.slice(0, 10);
      const confidence = hit.item.confidence === 'inferred' ? 'inferido, confirmar si es crítico' : 'explícito/confirmado';
      lines.push(`- [${confidence}; ${when}; ${hit.item.kind}] ${hit.item.text}`);
    }
    if (due.length) {
      lines.push('COMPROMISOS PENDIENTES (el usuario espera que los cuides):');
      for (const reminder of due) lines.push(`- ${reminder.title} · ${reminder.dueAt} · id=${reminder.id}`);
    }
    return lines.join('\n');
  }

  async scheduleReminder(input: ReminderInput): Promise<Reminder> {
    await this.ensureLoaded();
    if (!input.userId.trim() || !input.title.trim()) throw new Error('userId y title son obligatorios');
    const reminder: Reminder = { id: id('rem'), userId: input.userId, title: input.title.trim(), dueAt: safeDate(input.dueAt), timezone: input.timezone || 'UTC', recurrence: input.recurrence, status: 'scheduled', attempts: 0, createdAt: nowIso(), source: input.source || 'user' };
    this.state.reminders.push(reminder);
    await this.persist();
    return reminder;
  }

  async dueReminders(userId: string, options: DueReminderOptions = {}): Promise<Reminder[]> {
    await this.ensureLoaded();
    const now = options.now ?? new Date();
    const leaseMs = options.leaseMs ?? 30_000;
    const limit = options.limit ?? 20;
    const candidates = this.state.reminders.filter((r) => r.userId === userId && (r.status === 'scheduled' || (r.status === 'leased' && r.leaseUntil && Date.parse(r.leaseUntil) <= now.valueOf())) && Date.parse(r.dueAt) <= now.valueOf()).slice(0, limit);
    for (const reminder of candidates) { reminder.status = 'leased'; reminder.attempts += 1; reminder.lastAttemptAt = now.toISOString(); reminder.leaseUntil = new Date(now.valueOf() + leaseMs).toISOString(); }
    if (candidates.length) await this.persist();
    return candidates;
  }

  async completeReminder(userId: string, reminderId: string, delivered = true): Promise<Reminder | null> {
    await this.ensureLoaded();
    const reminder = this.state.reminders.find((r) => r.userId === userId && r.id === reminderId);
    if (!reminder) return null;
    reminder.status = delivered ? 'delivered' : 'scheduled'; reminder.leaseUntil = undefined;
    if (delivered) reminder.deliveredAt = nowIso();
    await this.persist(); return reminder;
  }

  async cancelReminder(userId: string, reminderId: string): Promise<Reminder | null> {
    await this.ensureLoaded();
    const reminder = this.state.reminders.find((r) => r.userId === userId && r.id === reminderId);
    if (!reminder) return null;
    reminder.status = 'cancelled'; reminder.leaseUntil = undefined;
    await this.persist(); return reminder;
  }

  async forget(userId: string, memoryId: string): Promise<boolean> {
    await this.ensureLoaded();
    const item = this.state.items.find((i) => i.userId === userId && i.id === memoryId);
    if (!item) return false;
    item.supersededBy = 'forgotten'; await this.persist(); return true;
  }

  async snapshot(userId?: string): Promise<MemorySnapshot> {
    await this.ensureLoaded();
    const match = <T extends { userId: string }>(xs: T[]) => userId ? xs.filter((x) => x.userId === userId) : xs;
    return { items: match(this.state.items), nodes: match(this.state.nodes), edges: match(this.state.edges), reminders: match(this.state.reminders) };
  }
}

/** Alias para código antiguo; la semántica nueva sigue siendo la del grafo. */
export class InMemoryMemoryStore extends GraphMemoryStore { constructor() { super(''); } }
