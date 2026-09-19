import type { VercelRequest, VercelResponse } from '@vercel/node';
import { handleReminderMutation } from '../../src/http/handleMemory';

export default async function handler(req: VercelRequest, res: VercelResponse): Promise<void> {
  if (req.method !== 'POST') { res.status(405).json({ error: 'usa POST' }); return; }
  try {
    const result = await handleReminderMutation((req.body || {}) as Record<string, unknown>, 'cancel', req.headers.authorization);
    res.status(result.status).json(result.json);
  } catch (e) { res.status(400).json({ error: (e as Error).message }); }
}
