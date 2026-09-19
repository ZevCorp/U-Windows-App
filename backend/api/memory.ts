import type { VercelRequest, VercelResponse } from '@vercel/node';
import { handleForget, handleMemory, handleRemember, handleScheduleReminder } from '../src/http/handleMemory';

export default async function handler(req: VercelRequest, res: VercelResponse): Promise<void> {
  const body = (req.body || {}) as Record<string, unknown>;
  try {
    if (req.method === 'GET') { res.status(200).json(await handleMemory(body, req.headers.authorization)); return; }
    if (req.method === 'DELETE') { const result = await handleForget(body, req.headers.authorization); res.status(result.status).json(result.json); return; }
    if (req.method !== 'POST') { res.status(405).json({ error: 'usa GET o POST' }); return; }
    const result = body.dueAt ? await handleScheduleReminder(body, req.headers.authorization) : await handleRemember(body, req.headers.authorization);
    res.status(result.status).json(result.json);
  } catch (e) { res.status(400).json({ error: (e as Error).message }); }
}
