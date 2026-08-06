using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace U.WindowsClient.Capture;

/// <summary>
/// Captura la pantalla completa a PNG (base64) para computer-use. Se llama SOLO cuando el turno
/// anterior devolvió <c>needsScreenshot</c>: por defecto el cerebro opera con el texto del árbol de UI.
///
/// Captura a la resolución física de pantalla (el manifiesto declara PerMonitorV2), así el screenshot
/// y las coordenadas donde el cerebro pide tocar coinciden 1:1 — sin reescalado.
/// </summary>
public static class Screenshotter
{
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
    private const int SM_CXSCREEN = 0, SM_CYSCREEN = 1;

    /// <summary>
    /// Solo una VENTANA, no toda la pantalla.
    /// </summary>
    /// <remarks>
    /// A quien mira una captura para explicar «la navegación de esta app» hay que darle esa app y
    /// nada más. Con la pantalla entera se le manda también el editor, el navegador y nuestra propia
    /// interfaz, y entonces la pregunta «¿qué es aquí navegación permanente?» no tiene un «aquí»:
    /// la primera vez que se le preguntó así devolvió la lista vacía (2026-08-05).
    /// </remarks>
    public static string? CaptureVentanaBase64Png(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;
        if (!GetWindowRect(hwnd, out RECT r)) return null;
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        if (w < 40 || h < 40) return null;
        try
        {
            using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
                g.CopyFromScreen(r.Left, r.Top, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return Convert.ToBase64String(ms.ToArray());
        }
        catch { return null; }
    }

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT r);

    public static string CaptureBase64Png()
    {
        int w = GetSystemMetrics(SM_CXSCREEN);
        int h = GetSystemMetrics(SM_CYSCREEN);
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(0, 0, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
        }
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return Convert.ToBase64String(ms.ToArray());
    }
}
