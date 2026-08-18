using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using U.WindowsClient.Actions;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Radicacion;

/// <summary>
/// Abre la web de la demo (ServidorWebRadicacion) y la diligencia sola, en vivo, usando UIA sobre lo
/// que Chromium expone del DOM — el mismo mecanismo de siempre en este repo (AutomationElement por
/// AutomationId), aquí PREPROGRAMADO y no dirigido por un modelo: se sabe de antemano qué campo es
/// cada uno porque el HTML lo escribimos nosotros mismos (ServidorWebRadicacion.GenerarHtml), así
/// que no hace falta razonar sobre la pantalla — solo ejecutar la secuencia.
///
/// A propósito NO usa SurfaceMap/UiaExecutor/PlanStep (el núcleo congelado, ver CLAUDE.md): este
/// módulo es una demo aislada y no tiene sentido acoplarla al núcleo por una sola pantalla que
/// nosotros mismos controlamos.
///
/// Si se pasa `carita` (la ventana de la carita flotante, típicamente `PanelDeRadicacion.Owner`), se
/// desliza sobre cada campo justo antes de escribirlo — solo mueve Left/Top, nunca roba el foco de
/// la ventana del navegador, así que escribir no se interrumpe. Puramente cosmético: si `carita` es
/// null, la lógica de diligenciar es idéntica.
/// </summary>
public static class RellenadorWeb
{
    public static async Task<bool> DiligenciarAsync(Radicado radicado, AreaDestino area, Window? carita = null)
    {
        ServidorWebRadicacion.AsegurarCorriendo();

        try { Process.Start(new ProcessStartInfo(ServidorWebRadicacion.Url) { UseShellExecute = true }); }
        catch (Exception e)
        {
            LogBus.Log("radicacion", $"no se pudo abrir el navegador en {ServidorWebRadicacion.Url}: {e.Message}");
            return false;
        }

        var ventana = await EsperarVentanaAsync(TimeSpan.FromSeconds(10));
        if (ventana == null)
        {
            LogBus.Log("radicacion", "no encontré la ventana del navegador con la web de la demo — ¿tardó más de 10s en cargar?");
            return false;
        }

        if (carita != null) TraerAlFrenteSinFoco(carita);
        await Task.Delay(1200); // deja asentar el primer render antes de tocar nada

        double origenX = carita?.Left ?? 0, origenY = carita?.Top ?? 0;

        bool ok = true;
        var doc = radicado.Documento;

        ok &= await Clic(ventana, $"area-{area.Clave}", carita);
        await Task.Delay(350);
        ok &= await Escribir(ventana, "campo-numero", radicado.Numero, carita);
        ok &= await Escribir(ventana, "campo-fecha", radicado.FechaHoraUtc.ToString("yyyy-MM-dd HH:mm") + " UTC", carita);
        ok &= await Escribir(ventana, "campo-tipo", doc.TipoDocumento, carita);
        ok &= await Escribir(ventana, "campo-remitente", doc.Remitente, carita);
        ok &= await Escribir(ventana, "campo-cedula", doc.CedulaONit, carita);
        ok &= await Escribir(ventana, "campo-afiliado", doc.AfiliadoIpsEmpleador, carita);
        ok &= await Escribir(ventana, "campo-asunto", doc.Asunto, carita);
        ok &= await Escribir(ventana, "campo-referido", doc.NumeroReferido, carita);
        ok &= await Escribir(ventana, "campo-resumen", doc.Resumen, carita);
        ok &= await Escribir(ventana, "campo-advertencias", string.Join(", ", doc.Advertencias), carita);
        await Task.Delay(500);
        ok &= await Clic(ventana, "btn-radicar", carita);

        if (carita != null) await DeslizarAsync(carita, origenX, origenY); // vuelve a su sitio, no se queda varada sobre el navegador

        LogBus.Log("radicacion", ok
            ? $"formulario web diligenciado para {radicado.Numero} (área {area.Clave})"
            : $"el formulario web quedó incompleto para {radicado.Numero} — revisa el log de arriba, campo por campo");
        return ok;
    }

    private static async Task<AutomationElement?> EsperarVentanaAsync(TimeSpan tope)
    {
        var reloj = Stopwatch.StartNew();
        while (reloj.Elapsed < tope)
        {
            try
            {
                foreach (AutomationElement ventana in AutomationElement.RootElement.FindAll(TreeScope.Children, System.Windows.Automation.Condition.TrueCondition))
                {
                    string nombre;
                    try { nombre = ventana.Current.Name ?? ""; } catch { continue; }
                    if (nombre.Contains("Radicación institucional", StringComparison.OrdinalIgnoreCase))
                        return ventana;
                }
            }
            catch { /* el árbol de ventanas puede cambiar a media lectura; se reintenta */ }
            await Task.Delay(300);
        }
        return null;
    }

    private static AutomationElement? Buscar(AutomationElement raiz, string automationId)
    {
        try
        {
            var condicion = new PropertyCondition(AutomationElement.AutomationIdProperty, automationId);
            return raiz.FindFirst(TreeScope.Descendants, condicion);
        }
        catch { return null; }
    }

    /// <summary>Desliza la carita hasta justo encima del elemento — no lo tapa, así se ve lo que escribe.</summary>
    private static async Task PasarPorAsync(AutomationElement el, Window? carita)
    {
        if (carita == null) return;
        Rect r;
        try { r = el.Current.BoundingRectangle; } catch { return; }
        if (r.IsEmpty) return;

        // Topmost=True (puesto en FaceWindow.xaml) solo garantiza estar por encima de ventanas NO
        // topmost — no sobrevive a que otra ventana se active después. El navegador se activa al
        // abrirse y se queda por encima aunque la carita sea topmost, hasta que se reafirma. Quitar y
        // volver a poner Topmost fuerza un SetWindowPos(HWND_TOPMOST) nuevo sin robar el foco.
        TraerAlFrenteSinFoco(carita);
        await DeslizarAsync(carita, r.X + r.Width / 2 - carita.ActualWidth / 2, r.Y - carita.ActualHeight - 10);
    }

    private static void TraerAlFrenteSinFoco(Window w)
    {
        try { w.Topmost = false; w.Topmost = true; } catch { /* si la ventana ya se cerró, no hay nada que traer al frente */ }
    }

    private static async Task DeslizarAsync(Window carita, double destX, double destY)
    {
        double origenX = carita.Left, origenY = carita.Top;
        const int pasos = 12;
        for (int i = 1; i <= pasos; i++)
        {
            double t = i / (double)pasos;
            double suave = 1 - Math.Pow(1 - t, 3); // ease-out cúbico: arranca rápido, frena al llegar
            carita.Left = origenX + (destX - origenX) * suave;
            carita.Top = origenY + (destY - origenY) * suave;
            await Task.Delay(14);
        }
    }

    private static async Task<bool> Escribir(AutomationElement ventana, string automationId, string valor, Window? carita)
    {
        var el = Buscar(ventana, automationId);
        if (el == null)
        {
            LogBus.Log("radicacion", $"campo web «{automationId}» no encontrado — no se escribió «{Recorta(valor)}»");
            return false;
        }
        await PasarPorAsync(el, carita);
        try { el.SetFocus(); } catch { /* algunos controles no aceptan foco directo; no es fatal */ }

        if (el.TryGetCurrentPattern(ValuePattern.Pattern, out var patronObj) && patronObj is ValuePattern valorPatron)
        {
            try { valorPatron.SetValue(valor); return true; }
            catch (Exception e) { LogBus.Log("radicacion", $"ValuePattern falló en «{automationId}»: {e.Message}"); }
        }

        // Respaldo si el control no expone ValuePattern: clic en su caja + escritura sintética.
        try
        {
            var r = el.Current.BoundingRectangle;
            if (r.IsEmpty) return false;
            int x = (int)(r.X + r.Width / 2), y = (int)(r.Y + r.Height / 2);
            return InputExecutor.Type(x, y, valor);
        }
        catch (Exception e)
        {
            LogBus.Log("radicacion", $"no se pudo escribir en «{automationId}» ni por patrón ni por clic: {e.Message}");
            return false;
        }
    }

    private static async Task<bool> Clic(AutomationElement ventana, string automationId, Window? carita)
    {
        var el = Buscar(ventana, automationId);
        if (el == null)
        {
            LogBus.Log("radicacion", $"botón web «{automationId}» no encontrado");
            return false;
        }
        await PasarPorAsync(el, carita);

        if (el.TryGetCurrentPattern(InvokePattern.Pattern, out var patronObj) && patronObj is InvokePattern invocar)
        {
            try { invocar.Invoke(); return true; }
            catch (Exception e) { LogBus.Log("radicacion", $"InvokePattern falló en «{automationId}»: {e.Message}"); }
        }

        try
        {
            var r = el.Current.BoundingRectangle;
            if (r.IsEmpty) return false;
            return InputExecutor.Tap((int)(r.X + r.Width / 2), (int)(r.Y + r.Height / 2));
        }
        catch (Exception e)
        {
            LogBus.Log("radicacion", $"no se pudo pulsar «{automationId}» ni por patrón ni por clic: {e.Message}");
            return false;
        }
    }

    private static string Recorta(string s) => s.Length <= 40 ? s : s[..40] + "…";
}
