using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace U.WindowsClient.Voice;

/// <summary>
/// UNA FOTO DE LA PANTALLA, cuando hace falta. Antes se llamaba <c>LiveVideo</c> y mandaba un
/// fotograma por segundo sin que nadie lo pidiera: Gemini recibía vídeo por el mismo caño que el
/// audio, así que había que estarlo alimentando aunque nadie mirara. OpenAI no tiene ese caño —y el
/// usuario, al verlo, prefirió que fuera así también aquí—: Ü PIDE ver cuando lo necesita
/// (<c>map_look</c>), o se le manda la foto justo cuando el usuario señala algo. Un fotograma que
/// nadie pidió es trabajo que nadie usa.
///
/// SE DIBUJA EL CURSOR. Las capturas de Windows NO lo incluyen, y sin él «te estoy señalando esto»
/// no significa nada: quien mira la foto no sabría dónde apuntaba. Es el detalle que separa esta
/// función de una que parece funcionar (2026-08-04).
/// </summary>
public static class CapturaDePantalla
{
    /// <summary>Ancho al que se reduce. 1024 basta para leer botones y no dispara el coste.</summary>
    private const int AnchoMaximo = 1024;
    private const long Calidad = 60L;

    /// <summary>Un fotograma de AHORA MISMO, comprimido a JPEG. Null si algo impidió capturarlo.</summary>
    public static byte[]? Capturar()
    {
        // Tamaño por Win32 y no por WinForms: activar WinForms en un proyecto WPF hace ambiguos
        // Brush, Application, MouseEventArgs y media docena más en toda la aplicación.
        var pantalla = new Rectangle(0, 0, GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN));
        if (pantalla.Width <= 0 || pantalla.Height <= 0) return null;

        using var completa = new Bitmap(pantalla.Width, pantalla.Height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(completa))
        {
            g.CopyFromScreen(pantalla.Left, pantalla.Top, 0, 0, pantalla.Size, CopyPixelOperation.SourceCopy);
            DibujarCursor(g);
        }

        double escala = Math.Min(1.0, (double)AnchoMaximo / completa.Width);
        int w = Math.Max(1, (int)(completa.Width * escala));
        int h = Math.Max(1, (int)(completa.Height * escala));

        using var reducida = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using (var g2 = Graphics.FromImage(reducida))
        {
            g2.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
            g2.DrawImage(completa, 0, 0, w, h);
        }

        var codec = ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.MimeType == "image/jpeg");
        if (codec == null) return null;
        using var parametros = new EncoderParameters(1);
        parametros.Param[0] = new EncoderParameter(Encoder.Quality, Calidad);

        using var ms = new MemoryStream();
        reducida.Save(ms, codec, parametros);
        return ms.ToArray();
    }

    /// <summary>
    /// Dibuja el cursor REAL, porque Windows no lo mete en la captura.
    ///
    /// No es una carencia nuestra: el puntero no vive en el contenido de la pantalla, lo compone el
    /// sistema por encima al presentar. Cualquier copia del framebuffer —BitBlt, CopyFromScreen,
    /// duplicación de escritorio— sale sin él. Por eso Windows expone GetCursorInfo/DrawIconEx: la
    /// forma oficial de saber qué puntero hay y pintarlo.
    ///
    /// Se dibuja el icono de verdad, con su punto caliente restado, en vez de un círculo inventado:
    /// así lo que ve el modelo es lo mismo que ve el usuario, incluido el cambio de forma cuando el
    /// puntero pasa por un enlace o un borde. Y encima, un halo para que destaque sobre cualquier
    /// fondo — señalar solo sirve si se distingue a dónde señalas.
    /// </summary>
    private static void DibujarCursor(Graphics g)
    {
        try
        {
            var ci = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
            if (!GetCursorInfo(ref ci) || ci.flags != CURSOR_SHOWING || ci.hCursor == IntPtr.Zero)
                return;

            IntPtr copia = CopyIcon(ci.hCursor);
            if (copia == IntPtr.Zero) return;
            try
            {
                int x = ci.ptScreenPos.X, y = ci.ptScreenPos.Y;
                if (GetIconInfo(copia, out ICONINFO ii))
                {
                    x -= (int)ii.xHotspot;
                    y -= (int)ii.yHotspot;
                    if (ii.hbmMask != IntPtr.Zero) DeleteObject(ii.hbmMask);
                    if (ii.hbmColor != IntPtr.Zero) DeleteObject(ii.hbmColor);
                }

                using (var halo = new Pen(Color.FromArgb(200, 255, 193, 7), 3f))
                    g.DrawEllipse(halo, ci.ptScreenPos.X - 18, ci.ptScreenPos.Y - 18, 36, 36);

                IntPtr hdc = g.GetHdc();
                try { DrawIconEx(hdc, x, y, copia, 0, 0, 0, IntPtr.Zero, DI_NORMAL); }
                finally { g.ReleaseHdc(hdc); }
            }
            finally { DestroyIcon(copia); }
        }
        catch { }
    }

    private const int SM_CXSCREEN = 0, SM_CYSCREEN = 1;
    private const int CURSOR_SHOWING = 0x0001, DI_NORMAL = 0x0003;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CURSORINFO { public int cbSize; public int flags; public IntPtr hCursor; public POINT ptScreenPos; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon; public uint xHotspot; public uint yHotspot;
        public IntPtr hbmMask; public IntPtr hbmColor;
    }

    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
    [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CURSORINFO pci);
    [DllImport("user32.dll")] private static extern IntPtr CopyIcon(IntPtr hIcon);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr hIcon);
    [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);
    [DllImport("user32.dll")] private static extern bool DrawIconEx(IntPtr hdc, int x, int y,
        IntPtr hIcon, int cx, int cy, int istepIfAniCur, IntPtr hbrFlickerFreeDraw, int diFlags);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
}
