// Composition root del backend: cablea las implementaciones concretas de los stores.
//
// Primera construcción: stores en memoria (se pierden entre cold starts de Vercel). Para producción,
// sustituir por Supabase/KV/Neo4j aquí — sin tocar el cerebro, el engine, ni el cliente.

import { GraphMemoryStore, MemoryStore } from './memory/store.js';
import { InMemoryLearningStore, LearningStore } from './learning/workflows.js';

let memory: MemoryStore | null = null;
let learning: LearningStore | null = null;

export function deps(): { memory: MemoryStore; learning: LearningStore } {
  // En local se puede usar MEMORY_FILE para sobrevivir reinicios. En Vercel este archivo es
  // efímero; producción debe inyectar el adaptador durable descrito en docs/memoria-tiempo-real.md.
  if (!memory) memory = new GraphMemoryStore();
  if (!learning) learning = new InMemoryLearningStore();
  return { memory, learning };
}
