# Publicar actualizaciones (Ü Windows)

Cómo sacar una versión nueva de la carita (`U.exe`) y que le llegue **sola** a los clientes ya
instalados. El equivalente de [`RELEASING.md`](RELEASING.md), que cubre la app Android.

> Para el **backend** no hay nada que hacer: vive en Vercel y se actualiza con un `git push`. Este
> documento es solo para el cliente Windows, que vive como `.exe` en la máquina del usuario.

---

## 1. Cómo funciona (resumen)

- La carita se instala **una sola vez** con `U-Setup.exe`, en `%LocalAppData%\U` (**sin pedir admin**).
- Usa **[Velopack](https://velopack.io)**: al arrancar y cada ~30 min consulta el feed, y si hay versión
  nueva **la descarga en segundo plano** sin interrumpir al usuario.
- Cuando está descargada, la carita muestra una pastilla azul: **"⬇ Versión X lista — reiniciar"**.
  - Si el cliente la toca, o dice **«actualízate»**, el halo se vuelve morado, Ü narra el mensaje humano de la release y reinicia en el momento.
  - Si la ignora → se instala sola **al cerrar Ü**. El siguiente arranque ya es la versión nueva.
- El feed son las **releases de este repo** (`ZevCorp/U-Windows-App`). Publicar = lanzar el workflow.
- Después de la primera versión, las descargas son **deltas** (KB, no los ~70 MB completos).

Código relevante:
- `windows-client/src/Update/Updater.cs` — el sondeo, la descarga, el mensaje humano y el aplicar.
- `windows-client/App.xaml.cs` — `VelopackApp.Build().Run()`, lo primero del proceso (obligatorio).
- `windows-client/src/Ui/FaceWindow.xaml` — la pastilla (`UpdateBtn`).
- `windows-client/src/Config.cs` — `UpdateFeedUrl`.

---

## 2. Dónde vive el feed

Son las **releases de este mismo repositorio**. Cada versión es una release `v<version>` con
`releases.win.json` (el índice), el `.nupkg` y el `U-win-Setup.exe`.

**Estuvo en un bucket de Supabase y no podía funcionar.** El plan gratuito corta las subidas en
**50 MB** —un tope *global*, que manda por encima del 1 GB configurado en el bucket— y el paquete
pesa 80. Siete intentos entre el 2026-07-22 y el 2026-08-16 murieron todos en la última línea, cada
uno por una causa que parecía la definitiva: `--endpoint` contra `--region`, el PUT único
(`RequestEntityTooLarge`), el CRC32 que la CLI de `aws` añade a cada parte. Los tres eran problemas
reales y ninguno era la causa de fondo. El bucket estuvo **siempre vacío**, así que el botón de
actualizar sólo podía contestar «ya estás al día»: no mentía, es que al otro lado no había nada.

Si alguna vez se vuelve a mirar hacia un almacenamiento con plan gratuito, la pregunta que ahorra
una semana es **cuál es el tope de subida del PLAN**, no el del bucket.

El repositorio es privado, así que —al revés que el bucket— esto **no se lee sin credenciales**:

- El **workflow** publica con el token del run, que necesita `permissions: contents: write`. Sin eso
  GitHub responde `Resource not accessible by integration`, un 403 que no menciona permisos.
- La **copia distribuida** consulta con un token de **solo lectura** embebido en el build
  (`WindowsClient.csproj` → `UpdateGithubToken`, secreto `UPDATE_GITHUB_TOKEN`). No puede publicar,
  ni borrar, ni leer código: sólo bajarse lo que ya se reparte a esas mismas personas. Va idéntico
  en cada copia, así que retirarlo obliga a rotarlo para todos a la vez.

---

## 3. Sacar una versión nueva

Desde la pestaña Actions → **Windows release** → *Run workflow*, con la versión (SemVer, mayor que
la publicada) y un `request_id` cualquiera. O desde la terminal:

```bash
gh workflow run windows-release.yml -f version=1.1.3 -f request_id=lo-que-sea \
  -f user_message="Ahora Ü recuerda mejor lo que hacemos y retoma la experiencia con más continuidad."
```

`user_message` es obligatorio. Es la promesa que recibe la persona: debe explicar en lenguaje
humano la intención de la versión, no enumerar commits. El workflow lo guarda como
`release-message.json` dentro de la release. Ü lo lee después de descargar el paquete y usa ese
texto como fuente canónica para narrar la actualización; no intenta inventar un resumen de los
cambios técnicos.

El workflow compila, empaqueta, publica la release **y comprueba que el paquete anunciado esté de
verdad subido**. Esa última comprobación existe porque una vez el paso salió en verde con el índice
publicado y el `.nupkg` ausente: el cliente veía la versión, la intentaba bajar y fallaba cada 30
minutos. Un release que miente es peor que uno que no ocurre.

**Cliente nuevo** (primera instalación): mandale el `U-win-Setup.exe` de la release. A partir de ahí
no vuelve a instalar nada nunca.

---

## 4. Verificar que salió bien

```bash
# La release tiene que existir y traer su paquete dentro (no sólo el índice):
gh release view v1.1.3 --json assets --jq '.assets[].name'
```

En la máquina del cliente: el panel **Backend** de la carita muestra `Versión X` abajo, y 📜 (Logs)
tiene las líneas con tag `update`.

---

## 5. Checklist

- [ ] Versión incrementada respecto a la publicada.
- [ ] `user_message` escrito para la persona (obligatorio; se rechaza vacío).
- [ ] El workflow terminó en verde (comprueba solo que el paquete esté publicado).
- [ ] La release trae `releases.win.json`, el `.nupkg` **y** `release-message.json`.
- [ ] Las releases viejas **siguen** publicadas: son la base de los deltas.

---

## 6. Detalles que muerden

- **Firma de código**: sin certificado, SmartScreen avisa al correr `U-Setup.exe` la primera vez
  (Fase 0.3 de `PRODUCTION.md`). El auto-update posterior **no** vuelve a mostrar el aviso.
- **`%LocalAppData%`, no `Program Files`**: Velopack no soporta directorios privilegiados. Es a favor
  nuestro — actualiza sin UAC.
- **Nada de `PublishSingleFile`**: `ScreenRecorderLib` es mixto C++/CLI y no lo soporta. Velopack
  empaqueta la carpeta, así que no hace falta.
- **En desarrollo el updater se apaga solo**: con `dotnet run` no hay instalación detrás, `IsInstalled`
  es false y `Updater` no hace nada. Para probar el update de verdad hay que instalar con el Setup.
- **La config del usuario sobrevive**: vive en `%APPDATA%\U\config.json`, fuera de la carpeta de
  instalación que Velopack reemplaza.
