// Tipos compartidos por todos los providers del cerebro (evita ciclos de import).
import { BrainTurn, ScreenState } from '../domain/actions.js';
import { McpTool } from '../domain/mcp.js';
import { SessionState } from '../domain/session.js';

export interface TurnInput {
  session: SessionState;
  tools: McpTool[];
  mcpNames: Set<string>;
  memory: string;
  apps: string[];
  state: ScreenState;
  results: string[];
  apiKey: string;
  timeContext: string;
}

export interface TurnOutput {
  session: SessionState;
  turn: BrainTurn;
}
