import { config, memoryArchiveEnabled } from '../config.js';
import { MemoryStatePersistence, PersistedState } from './store.js';

/** Persistencia de memoria en un objeto privado de Supabase Storage. */
export class SupabaseMemoryPersistence implements MemoryStatePersistence {
  private bucketReady: Promise<void> | null = null;
  private readonly path = 'memory/state.json';

  async load(): Promise<PersistedState | null> {
    this.assertConfigured();
    await this.ensureBucket();
    const response = await this.request('GET');
    if (response.status === 404) return null;
    if (!response.ok) throw new Error(`Supabase memory read HTTP ${response.status}: ${(await response.text()).slice(0, 200)}`);
    return await response.json() as PersistedState;
  }

  async save(state: PersistedState): Promise<void> {
    this.assertConfigured();
    await this.ensureBucket();
    const response = await this.request('PUT', JSON.stringify(state));
    if (!response.ok) throw new Error(`Supabase memory write HTTP ${response.status}: ${(await response.text()).slice(0, 200)}`);
  }

  private assertConfigured(): void {
    if (!memoryArchiveEnabled()) throw new Error('la memoria durable requiere SUPABASE_URL y SUPABASE_SERVICE_ROLE_KEY');
  }

  private async ensureBucket(): Promise<void> {
    if (!this.bucketReady) this.bucketReady = this.createBucket();
    await this.bucketReady;
  }

  private async createBucket(): Promise<void> {
    const response = await fetch(`${config.supabaseUrl}/storage/v1/bucket`, {
      method: 'POST', headers: this.headers(),
      body: JSON.stringify({ id: config.supabaseMemoryBucket, name: config.supabaseMemoryBucket, public: false }),
    });
    // 409 means the bucket already exists, which is the normal path after the first cold start.
    if (!response.ok && response.status !== 409) {
      throw new Error(`Supabase memory bucket HTTP ${response.status}: ${(await response.text()).slice(0, 200)}`);
    }
  }

  private request(method: 'GET' | 'PUT', body?: string): Promise<Response> {
    return fetch(`${config.supabaseUrl}/storage/v1/object/${config.supabaseMemoryBucket}/${this.path}`, {
      method, headers: { ...this.headers(), ...(body ? { 'Content-Type': 'application/json', 'x-upsert': 'true' } : {}) }, body,
    });
  }

  private headers(): Record<string, string> {
    return { Authorization: `Bearer ${config.supabaseServiceKey}`, apikey: config.supabaseServiceKey };
  }
}
