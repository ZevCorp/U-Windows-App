# Ü — Windows App

Repositorio de la aplicación de escritorio Windows de Ü, separado del repo Android
como parte de la organización multirepo de la empresa.

## Estructura

- `windows-client/` — frontend C#/WPF (.NET 8) → `U.exe`. Cliente "tonto": lee el
  árbol de UI (UIA), captura pantalla, ejecuta ratón/teclado y voz. Toda la
  inteligencia vive en el backend central **Graph**.
- `windows-graph/` — módulo C# de workflows sobre SAP GUI Scripting / UIA
  (grabar y reproducir), compilado dentro de `U.exe`. Habla con Graph.
- `backend/` — **LEGACY**: el cerebro TypeScript original (`u-windows-backend` en
  Vercel). Sus funcionalidades fueron absorbidas por el backend central Graph
  (`/api/v1/agent/turn`, `/api/v1/teach/*`). Se conserva solo como vía de
  emergencia (`U_BACKEND_URL`) mientras se verifica el corte; después se elimina.

## Backend

El cliente consume el backend central Graph: `https://graph-eight-pied.vercel.app`
con API key (`miracle_...`) en `%APPDATA%\U\graph.json` o env `GRAPH_API_KEY`.
Las keys se generan en el Provider Studio de Graph (sección API keys).

## Build

```powershell
dotnet build windows-client/WindowsClient.csproj -c Debug
```

Release e instalador: ver `RELEASING-WINDOWS.md`. Runbook de producción: `PRODUCTION.md`.
Arquitectura y decisiones: `WINDOWS.md`.

## Flujo con Codex y Claude Code

El repo incluye pstack portable para ambos agentes. Usa `$pstack ...` en Codex o
`/pstack ...` en Claude Code. La instalación, actualización y la relación con las
skills SDD existentes están documentadas en [`docs/pstack.md`](docs/pstack.md).

## Historial

Este repo nace de la separación del monorepo `ZevCorp/Android` (2026-07). El
historial completo previo a la separación vive allí.
