import type { VercelRequest, VercelResponse } from '@vercel/node';
import { handleDueReminders } from '../../src/http/handleMemory';

export default async function handler(req: VercelRequest, res: VercelResponse): Promise<void> {
  if (req.method !== 'POST' && req.method !== 'GET') { res.status(405).json({ error: 'usa GET o POST' }); return; }
  try {
    const body = (req.method === 'POST' ? req.body : req.query) as Record<string, unknown>;
    const result = await handleDueReminders(body || {}, req.headers.authorization);
    res.status(result.status).json(result.json);
  } catch (e) { res.status(400).json({ error: (e as Error).message }); }
}
