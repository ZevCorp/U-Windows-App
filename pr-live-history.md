## Cambio

La voz deja de reconstruir el contexto con mensajes activos. GPT-Live recibe los turnos persistidos como historial inicial en `session.input`, con roles `user` y `assistant`, al abrir cada sesión nueva. Cargar ese historial no solicita `response.create`, por lo que Ü permanece en silencio hasta que el usuario inicia el siguiente turno.

Esto hace que apagar y volver a encender el micrófono continúe la misma ventana conversacional, incluyendo el hilo anterior y los recuerdos personales cargados por la aplicación.

## Verificación

- `scripts/verificar.ps1`: compila correctamente.
- Contrato del grafo: 282/282 promesas.
- Contrato de voz: 45/45 promesas.
- Promesa 345: el historial inicial de GPT-Live conserva roles y no dispara respuesta automática.
- Ejecutable Debug reconstruido y abierto con PID 39380.
