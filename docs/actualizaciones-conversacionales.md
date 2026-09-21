# Actualizaciones conversacionales de Ü

Una release tiene dos productos: el binario y la intención que el desarrollador quiere comunicar.
El binario se entrega con Velopack; la intención viaja como `release-message.json`, un artefacto
versionado de la misma release. El cliente nunca intenta deducir el anuncio leyendo commits.

## Flujo visible

```text
release publicada
      │
      ├─ paquete Velopack + release-message.json
      ▼
descarga en segundo plano ──► «hay una actualización lista»
      │                         (sin interrumpir)
      ▼
«actualízate» / botón ⬇
      │
      ▼
halo morado + mensaje humano hablado
      │
      ▼
reinicio ──► saludo de la nueva versión
```

El morado representa el estado de actualización y tiene prioridad sobre el color azul/gris de la
fuente de voz. Si la búsqueda no encuentra una versión, el halo vuelve a su estado normal y Ü lo
dice claramente. Si falta el mensaje de la release, se usa una frase genérica de respaldo y se
registra el fallo; nunca se inventa una lista de funcionalidades.

## Contrato obligatorio del release

El workflow `windows-release.yml` rechaza `user_message` vacío. El archivo que publica es:

```json
{
  "schema": 1,
  "version": "1.4.0",
  "message": "Ahora Ü recuerda mejor lo que hacemos y retoma la experiencia con más continuidad.",
  "locale": "es-CO",
  "publishedAt": "2026-09-19T12:00:00Z"
}
```

El mensaje debe hablarle a la persona y explicar la intención del cambio. Los commits, tickets y
detalles internos siguen estando en la release para quien desarrolla, pero no forman parte de la
narración de Ü.

## Próximos pasos de producto

1. Persistir un `updateAnnouncementId` en el hilo durable para que el arranque posterior pueda decir
   «ya volví» una sola vez, incluso si la máquina reinició durante la instalación.
2. Añadir variantes por idioma y accesibilidad (texto visible, voz, subtítulos y ritmo lento).
3. Registrar `detected`, `downloaded`, `announced`, `applied` y `welcomed` con un mismo idempotency
   key para medir fallos sin duplicar anuncios.
4. Añadir una vista de historial: «qué cambió», «cuándo lo instalé» y el mensaje que aprobó el
   desarrollador.
5. Probar releases sin red, asset ausente, instalación cancelada y reinicio inesperado antes de
   activar la entrega automática para todos.
