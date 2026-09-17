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
    /// <summary>
    /// EL PRESUPUESTO DEL SERVIDOR, que es lo que decide el tamaño —no un tope puesto a ojo—.
    /// </summary>
    /// <remarks>
    /// AQUÍ HABÍA UN TOPE DE 1024 PX y venía de otra época: cuando la imagen viajaba INCRUSTADA en el
    /// mensaje y tenía que caber en un buzón de 32.768 bytes. Desde que viaja por referencia (spec 027,
    /// fase 1) no tiene que caber en nada de eso, así que reducir era maquinaria de compensación de una
    /// limitación que ya no existe —aprendizaje nº6—.
    ///
    /// LO QUE MANDA AHORA es lo que OpenAI cobra. Para la familia gpt-5.x el cobro va por PARCHES de
    /// 32×32 píxeles: tokens = ceil(ceil(w/32) · ceil(h/32) · 1,2). El detalle alto admite 2.500 parches y
    /// 2.048 píxeles de lado. Una pantalla de 1080p son 60×34 = 2.040 parches —entra ENTERA— y cuesta
    /// 2.448 tokens por mirada; una 4K no entra y se reduce hasta caber, no por gusto.
    ///
    /// SE TRATA COMO COMPUTER USE, elegido por el dueño el 2026-09-17, y es lo que la documentación de
    /// OpenAI pide justo para eso: detalle fino para OCR, objetos pequeños y computer use.
    /// </remarks>
    public const int ParchesMaximos = 2500;

    /// <summary>El lado máximo que admite el detalle alto.</summary>
    public const int LadoMaximo = 2048;

    /// <summary>Calidad JPEG. Sube de 60 a 85 por lo mismo: la foto es para leer la pantalla, no para pesar poco.</summary>
    private const long Calidad = 85L;

    /// <summary>Cuántos parches de 32 px cuesta una imagen de ese tamaño.</summary>
    public static int Parches(int ancho, int alto) => (int)(Math.Ceiling(ancho / 32.0) * Math.Ceiling(alto / 32.0));

    /// <summary>
    /// A qué tamaño viaja una pantalla de ese tamaño. Pura, para que el contrato la pueda juzgar sin
    /// pantalla (promesa 256).
    /// </summary>
    public static (int Ancho, int Alto) Medida(int ancho, int alto)
    {
        if (ancho <= 0 || alto <= 0) return (Math.Max(1, ancho), Math.Max(1, alto));

        // NUNCA SE AGRANDA: inventar píxeles no añade nada que ver y sí lo que cobrar.
        double escala = Math.Min(1.0, Math.Min((double)LadoMaximo / ancho, (double)LadoMaximo / alto));
        int w = Math.Max(1, (int)Math.Round(ancho * escala));
        int h = Math.Max(1, (int)Math.Round(alto * escala));

        // Y SOLO HASTA CABER, de a poco, para no pasarse de largo y tirar detalle que sí cabía.
        while (Parches(w, h) > ParchesMaximos && escala > 0.05)
        {
            escala *= 0.97;
            w = Math.Max(1, (int)Math.Round(ancho * escala));
            h = Math.Max(1, (int)Math.Round(alto * escala));
        }
        return (w, h);
    }

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

        var (w, h) = Medida(completa.Width, completa.Height);
        if (w == completa.Width && h == completa.Height) return AJpeg(completa);

        using var reducida = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using (var g2 = Graphics.FromImage(reducida))
        {
            g2.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
            g2.DrawImage(completa, 0, 0, w, h);
        }

        return AJpeg(reducida);
    }

    private static byte[]? AJpeg(Bitmap bmp)
    {
        var codec = ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.MimeType == "image/jpeg");
        if (codec == null) return null;
        using var parametros = new EncoderParameters(1);
        parametros.Param[0] = new EncoderParameter(Encoder.Quality, Calidad);
        using var ms = new MemoryStream();
        bmp.Save(ms, codec, parametros);
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
