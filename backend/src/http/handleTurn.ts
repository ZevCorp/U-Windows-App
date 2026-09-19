// Lógica del endpoint /api/agent/turn, independiente del runtime (Vercel o servidor local). Así el
// mismo código corre en producción (Vercel Function) y en desarrollo (node local-server.mts).

import { activeModel, assertConfigured, config } from '../config';
import { decodeSession, encodeSession, freshSession } from '../domain/session';
import { ScreenState } from '../domain/actions';
import { resolveTurn } from '../application/engine';
import { deps } from '../container';
import { applyExplicitMemoryCommand } from '../memory/commands';

export interface TurnBody {
  session?: string;
  goal?: string;
  userId?: string;
  state?: ScreenState;
  results?: string[];
  inform?: string;
}

export interface HttpResult {
  status: number;
  json: unknown;
}

export async function handleTurn(body: TurnBody, authHeader?: string): Promise<HttpResult> {
  if (config.clientToken) {
    const auth = (authHeader || '').replace(/^Bearer\s+/i, '');
    if (auth !== config.clientToken) return { status: 401, json: { error: 'no autorizado' } };
  }

  if (!body.state || typeof body.state.screen !== 'string') {
    return { status: 400, json: { error: 'falta `state` (screen, uiContext, width, height)' } };
  }

  const userId = body.userId?.trim() || 'anon';

  // Una orden explícita de memoria debe ser fiable aunque el proveedor del LLM esté caído. Se
  // ejecuta antes de abrir sesión/modelo y devuelve el mismo contrato BrainTurn que el cliente ya
  // entiende. Las inferencias conversacionales siguen pasando por el cerebro y no escriben solas.
  if (!body.session && typeof body.goal === 'string') {
    const memoryCommand = await applyExplicitMemoryCommand(deps().memory, userId, body.goal);
    if (memoryCommand) {
      const session = freshSession(config.provider, body.goal.trim(), activeModel(), config.effort);
      const response = memoryCommand.response;
      return {
        status: 200,
        json: {
          session: encodeSession(session), actions: [], question: null, done: true, text: response,
          needsScreenshot: false, narration: '', speech: response, intents: [],
          memory: memoryCommand.kind, memoryId: memoryCommand.kind === 'remember' ? memoryCommand.item.id : memoryCommand.reminder.id,
        },
      };
    }
  }

  try {
    assertConfigured();
  } catch (e) {
    return { status: 500, json: { error: (e as Error).message } };
  }

  let session;
  try {
    session = body.session
      ? decodeSession(body.session)
      : freshSession(config.provider, body.goal?.trim() || '', activeModel(), config.effort);
  } catch (e) {
    return { status: 400, json: { error: `sesión inválida: ${(e as Error).message}` } };
  }
  if (!body.session && !session.goal) {
    return { status: 400, json: { error: 'el primer turno requiere `goal`' } };
  }

  if (typeof body.inform === 'string') session.informText = body.inform;

  try {
    const { session: next, turn } = await resolveTurn(
      { userId, session, state: body.state, results: body.results ?? [] },
      deps(),
    );
    return { status: 200, json: { session: encodeSession(next), ...turn } };
  } catch (e) {
    return { status: 502, json: { error: `cerebro: ${(e as Error).message}` } };
  }
}
