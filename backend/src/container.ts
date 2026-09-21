// Composition root del backend: cablea las implementaciones concretas de los stores.
//
// Primera construcción: stores en memoria (se pierden entre cold starts de Vercel). Para producción,
// sustituir por Supabase/KV/Neo4j aquí — sin tocar el cerebro, el engine, ni el cliente.

import { GraphMemoryStore, MemoryStore } from './memory/store.js';
import { SupabaseMemoryPersistence } from './memory/supabase.js';
import { memoryArchiveEnabled } from './config.js';
import { InMemoryLearningStore, LearningStore } from './learning/workflows.js';

let memory: MemoryStore | null = null;
let learning: LearningStore | null = null;

export function deps(): { memory: MemoryStore; learning: LearningStore } {
  // Vercel no conserva el disco entre cold starts. Cuando Supabase está configurado, el grafo y
  // sus recordatorios viven en un objeto privado durable; local sigue usando MEMORY_FILE o RAM.
  if (!memory) memory = memoryArchiveEnabled()
    ? new GraphMemoryStore('', new SupabaseMemoryPersistence())
    : new GraphMemoryStore();
  if (!learning) learning = new InMemoryLearningStore();
  return { memory, learning };
}
