using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Teach;

/// <summary>
/// Pantallazo por PASO al enseñar un workflow: cada step queda con su "meta visual" en disco
/// (<c>%LOCALAPPDATA%\U\step-shots\&lt;workflowId&gt;\step_N.png</c>). Es el contexto que consumirá el
/// COMPUTER-USE cuando tenga que concatenar/retomar un workflow que falló: ver cómo se veía la pantalla
/// en ese paso le da una meta clara de ejecución.
///
/// GDI puro (BitBlt) + encoder de WPF: sin dependencias nuevas. La numeración sigue el ORDEN DE LLEGADA
/// de los steps, el mismo con el que Graph los numera (la cola del recorder es de un solo lector).
/// </summary>
public static class StepShotCamera
{
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr hdc, int x, int y, int cx, int cy, IntPtr src, int x1, int y1, int rop);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    private const int SRCCOPY = 0x00CC0020;
    private const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;

    /// <summary>Carpeta de pantallazos de un workflow.</summary>
    public static string FolderFor(string workflowId) => Path.Combine(
        U.Graph.UserPaths.Local, "U", "step-shots", Sanitize(workflowId));

    /// <summary>Captura la pantalla (virtual, multi-monitor) y la guarda como step_N.png del workflow.</summary>
    public static void Capture(string workflowId, int stepNumber)
    {
        try
        {
            int x = GetSystemMetrics(SM_XVIRTUALSCREEN), y = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int w = GetSystemMetrics(SM_CXVIRTUALSCREEN), h = GetSystemMetrics(SM_CYVIRTUALSCREEN);
            if (w <= 0 || h <= 0) return;

            IntPtr screen = GetDC(IntPtr.Zero);
            IntPtr mem = CreateCompatibleDC(screen);
            IntPtr bmp = CreateCompatibleBitmap(screen, w, h);
            IntPtr old = SelectObject(mem, bmp);
            BitBlt(mem, 0, 0, w, h, screen, x, y, SRCCOPY);
            SelectObject(mem, old);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);

            var source = Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            DeleteObject(bmp);

            Directory.CreateDirectory(FolderFor(workflowId));
            string file = Path.Combine(FolderFor(workflowId), $"step_{stepNumber}.png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var fs = File.Create(file);
            encoder.Save(fs);
        }
        catch (Exception ex)
        {
            LogBus.Log("teach", $"no se pudo capturar el pantallazo del paso {stepNumber}: {ex.Message}");
        }
    }

    private static string Sanitize(string id)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) id = id.Replace(c, '_');
        return string.IsNullOrWhiteSpace(id) ? "workflow" : id;
    }
}
