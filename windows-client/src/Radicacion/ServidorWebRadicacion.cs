using System;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Radicacion;

/// <summary>
/// La "web institucional" de la demo: una sola página estática (sin backend, todo en localStorage
/// del navegador) que simula las bandejas por área. Se sirve en localhost con un HttpListener propio
/// —nada de Node/npm, nada que instalar— porque lo que importa aquí es que RellenadorWeb pueda
/// abrirla y encontrar los campos por su AutomationId, no que persista datos de verdad.
///
/// IDs estables a propósito: RellenadorWeb busca cada campo por su `id` HTML (Chromium lo expone
/// como AutomationId). Si se renombra un id aquí, hay que renombrarlo también allá.
/// </summary>
public static class ServidorWebRadicacion
{
    public const int Puerto = 58732; // alto y poco común, para no chocar con nada que el usuario tenga corriendo
    public static string Url => $"http://localhost:{Puerto}/";

    private static HttpListener? _listener;
    private static readonly object _lock = new();

    public static void AsegurarCorriendo()
    {
        lock (_lock)
        {
            if (_listener is { IsListening: true }) return;
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add(Url);
                _listener.Start();
                _ = EscucharAsync(_listener);
                LogBus.Log("radicacion", $"servidor web de la demo escuchando en {Url}");
            }
            catch (Exception e)
            {
                LogBus.Log("radicacion", $"no se pudo levantar el servidor web de la demo: {e.Message}");
            }
        }
    }

    private static async Task EscucharAsync(HttpListener listener)
    {
        string html = GenerarHtml();
        byte[] cuerpo = Encoding.UTF8.GetBytes(html);
        while (listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync(); }
            catch { break; } // el listener se cerró (fin del proceso)

            try
            {
                ctx.Response.ContentType = "text/html; charset=utf-8";
                ctx.Response.ContentLength64 = cuerpo.Length;
                await ctx.Response.OutputStream.WriteAsync(cuerpo);
            }
            catch (Exception e)
            {
                LogBus.Log("radicacion", $"error sirviendo la web de la demo: {e.Message}");
            }
            finally
            {
                ctx.Response.OutputStream.Close();
            }
        }
    }

    private static string GenerarHtml()
    {
        var areas = AreasConfig.Cargar();
        // camelCase a propósito: el JS de abajo lee a.clave/a.nombre en minúscula, y sin esto
        // System.Text.Json serializa las propiedades de AreaDestino tal cual (Clave, Nombre) — el
        // AREAS.find(...) de radicar() fallaba en silencio y reventaba en .nombre antes de repintar
        // la bandeja: el registro SÍ quedaba guardado en localStorage, pero nunca se veía.
        string areasJson = JsonSerializer.Serialize(areas, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        string botonesArea = string.Join("\n", areas.ConvertAll(a =>
            $"""<button class="area-btn" id="area-{a.Clave}" data-clave="{a.Clave}" onclick="elegirArea('{a.Clave}')">{a.Nombre}</button>"""));

        return $$"""
        <!doctype html>
        <html lang="es">
        <head>
        <meta charset="utf-8"/>
        <title>Radicación institucional — demo Miracle</title>
        <style>
          :root { color-scheme: dark; }
          * { box-sizing: border-box; }
          body { margin: 0; font-family: 'Segoe UI', system-ui, sans-serif; background: #16161a; color: #eee; display: flex; height: 100vh; }
          aside { width: 260px; background: #1e1e24; padding: 18px; border-right: 1px solid #2c2c34; overflow-y: auto; }
          aside h1 { font-size: 15px; margin: 0 0 14px; opacity: .8; }
          .area-btn { display: block; width: 100%; text-align: left; padding: 10px 12px; margin-bottom: 6px; border-radius: 8px; border: 1px solid #33333c; background: #26262e; color: #eee; cursor: pointer; font-size: 13px; }
          .area-btn:hover { background: #303038; }
          .area-btn.activa { background: #3a5f8f; border-color: #4a7fc0; }
          .area-btn .contador { float: right; opacity: .6; font-size: 11px; }
          main { flex: 1; padding: 24px 32px; overflow-y: auto; }
          h2 { margin-top: 0; }
          form { background: #1e1e24; border: 1px solid #2c2c34; border-radius: 10px; padding: 20px; max-width: 640px; margin-bottom: 28px; }
          label { display: block; font-size: 12px; opacity: .75; margin: 10px 0 4px; }
          input, textarea { width: 100%; padding: 8px 10px; border-radius: 6px; border: 1px solid #3a3a44; background: #26262e; color: #eee; font-size: 13px; font-family: inherit; }
          textarea { min-height: 60px; resize: vertical; }
          #btn-radicar { margin-top: 16px; padding: 10px 18px; border-radius: 8px; border: none; background: #3a8f5f; color: white; font-size: 14px; cursor: pointer; }
          #btn-radicar:hover { background: #45a870; }
          table { width: 100%; border-collapse: collapse; }
          th, td { text-align: left; padding: 8px 10px; border-bottom: 1px solid #2c2c34; font-size: 13px; }
          th { opacity: .6; font-weight: 600; }
          #toast { position: fixed; bottom: 24px; right: 24px; background: #3a8f5f; color: white; padding: 12px 18px; border-radius: 8px; opacity: 0; transition: opacity .3s; pointer-events: none; }
          #toast.mostrar { opacity: 1; }
          #sin-area { opacity: .6; font-size: 13px; }
        </style>
        </head>
        <body>
          <aside>
            <h1>ÁREAS</h1>
            {{botonesArea}}
          </aside>
          <main>
            <h2>Radicar documento</h2>
            <p id="area-actual" style="opacity:.7;font-size:13px;">Elige un área a la izquierda para radicar ahí.</p>
            <form onsubmit="return radicar(event)">
              <label for="campo-numero">Número provisional</label>
              <input id="campo-numero" autocomplete="off"/>
              <label for="campo-fecha">Fecha / hora</label>
              <input id="campo-fecha" autocomplete="off"/>
              <label for="campo-tipo">Tipo de documento</label>
              <input id="campo-tipo" autocomplete="off"/>
              <label for="campo-remitente">Remitente</label>
              <input id="campo-remitente" autocomplete="off"/>
              <label for="campo-cedula">Cédula / NIT</label>
              <input id="campo-cedula" autocomplete="off"/>
              <label for="campo-afiliado">Afiliado / IPS / Empleador</label>
              <input id="campo-afiliado" autocomplete="off"/>
              <label for="campo-asunto">Asunto</label>
              <input id="campo-asunto" autocomplete="off"/>
              <label for="campo-referido">Número referido</label>
              <input id="campo-referido" autocomplete="off"/>
              <label for="campo-resumen">Resumen</label>
              <textarea id="campo-resumen"></textarea>
              <label for="campo-advertencias">Advertencias</label>
              <input id="campo-advertencias" autocomplete="off"/>
              <button type="submit" id="btn-radicar">Radicar</button>
            </form>

            <h2>Bandeja</h2>
            <div id="bandeja"><p id="sin-area">— sin área seleccionada —</p></div>
          </main>
          <div id="toast"></div>

        <script>
          const AREAS = {{areasJson}};
          let areaElegida = null;

          function contar(clave) {
            return JSON.parse(localStorage.getItem('radicacion-' + clave) || '[]').length;
          }

          function pintarContadores() {
            AREAS.forEach(a => {
              const btn = document.getElementById('area-' + a.clave);
              if (!btn) return;
              btn.innerHTML = a.nombre + ' <span class="contador">' + contar(a.clave) + '</span>';
              btn.classList.toggle('activa', a.clave === areaElegida);
            });
          }

          function elegirArea(clave) {
            areaElegida = clave;
            const area = AREAS.find(a => a.clave === clave);
            document.getElementById('area-actual').textContent = 'Radicando a: ' + (area ? area.nombre : clave);
            pintarContadores();
            pintarBandeja();
          }

          function pintarBandeja() {
            const cont = document.getElementById('bandeja');
            if (!areaElegida) { cont.innerHTML = '<p id="sin-area">— sin área seleccionada —</p>'; return; }
            const items = JSON.parse(localStorage.getItem('radicacion-' + areaElegida) || '[]');
            if (items.length === 0) { cont.innerHTML = '<p id="sin-area">Sin radicados todavía en esta área.</p>'; return; }
            let filas = items.map(it =>
              '<tr><td>' + it.numero + '</td><td>' + it.tipo + '</td><td>' + it.remitente + '</td><td>' + it.fecha + '</td></tr>'
            ).join('');
            cont.innerHTML = '<table><thead><tr><th>Número</th><th>Tipo</th><th>Remitente</th><th>Fecha</th></tr></thead><tbody>' + filas + '</tbody></table>';
          }

          function mostrarToast(texto) {
            const t = document.getElementById('toast');
            t.textContent = texto;
            t.classList.add('mostrar');
            setTimeout(() => t.classList.remove('mostrar'), 2500);
          }

          function radicar(ev) {
            ev.preventDefault();
            if (!areaElegida) { mostrarToast('⚠ Elige un área primero'); return false; }
            const registro = {
              numero: document.getElementById('campo-numero').value,
              fecha: document.getElementById('campo-fecha').value,
              tipo: document.getElementById('campo-tipo').value,
              remitente: document.getElementById('campo-remitente').value,
              cedula: document.getElementById('campo-cedula').value,
              afiliado: document.getElementById('campo-afiliado').value,
              asunto: document.getElementById('campo-asunto').value,
              referido: document.getElementById('campo-referido').value,
              resumen: document.getElementById('campo-resumen').value,
              advertencias: document.getElementById('campo-advertencias').value,
            };
            const clave = 'radicacion-' + areaElegida;
            const items = JSON.parse(localStorage.getItem(clave) || '[]');
            items.push(registro);
            localStorage.setItem(clave, JSON.stringify(items));
            mostrarToast('✓ Radicado guardado en ' + AREAS.find(a => a.clave === areaElegida).nombre);
            pintarContadores();
            pintarBandeja();
            document.querySelectorAll('form input, form textarea').forEach(el => el.value = '');
            return false;
          }

          pintarContadores();
        </script>
        </body>
        </html>
        """;
    }
}
