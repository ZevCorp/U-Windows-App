## Qué cambia

La conversación de voz conserva un hilo durable cuando el micrófono se apaga y se vuelve a encender, y las preguntas de memoria activan la búsqueda antes de responder. Se elimina la respuesta provisional de «no recuerdo» antes de que termine la búsqueda.

Los recuerdos cotidianos con señales personales claras se capturan automáticamente al cerrar cada turno. Por ejemplo, decir «me encantan los relojes Cartier y prefiero el Santos» queda disponible como recuerdo sin tener que decir «guárdalo».

También se amplía el contexto conversacional durable a 56 turnos/18.000 caracteres y la memoria entregada al modelo a 40 recuerdos.

## Verificación

- `scripts/verificar.ps1`: compila correctamente.
- Contrato del grafo: 282/282 promesas.
- Contrato de voz: 45/45 promesas.
- Promesa 344: los detalles cotidianos de una preferencia se convierten en recuerdo.
- Falta la comprobación manual de dos pantallas; se hará sobre el ejecutable reconstruido después del merge.
