using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Teach;

/// <summary>Un cuadro que la cámara ya dejó en disco, con dónde estaba el ratón.</summary>
public sealed record CuadroTomado(long HoraMs, string Ruta, ulong Huella, int CursorX, int CursorY, int Ancho, int Alto)
{
    /// <summary>La cara que ven <see cref="CuadroDeAntes"/> y <see cref="CuadroDeDespues"/>.</summary>
    public Cuadro ComoCuadro() => new(HoraMs, Ruta, Huella);
}

/// <summary>
/// LA CÁMARA QUE MIRA AL PASADO. Copia la pantalla cada <see cref="IntervaloMs"/> mientras se
/// enseña y deja TODOS los cuadros en disco, con su hora del reloj de la demo. Spec 012.
/// </summary>
/// <remarks>
/// POR QUÉ TODOS Y NO SOLO LOS DE LOS CLICS (pedido del dueño, 2026-09-06): «yo puedo solo señalar
/// algo y decir "esta es la opción que usarías" sin hacer clic». Lo que se señala sin pulsar no
/// deja evento; deja un momento en la transcripción. Para que el piloto pueda ir a MIRAR ese
/// segundo exacto, el cuadro tiene que existir. A 4 cuadros por segundo y ~60 KB cada uno, una
/// demo de cinco minutos son ~70 MB: cabe, y se puede borrar después de comprobar.
///
/// POR QUÉ NO SE DECODIFICA EL MP4. Sería lo natural —ya se graba— pero necesita ffmpeg (~50 MB y
/// zona gris de licencia, dicho en <c>ScreenRecorder.cs</c>) o Media Foundation a pelo. La cámara
/// ya tiene la pantalla en la mano; guardarla cuesta menos que volver a sacarla del video.
///
/// EL RATÓN SE DIBUJA. BitBlt no copia el cursor, y un cuadro sin cursor no cuenta qué se estaba
/// señalando —que es la mitad de lo que esto existe para conservar—. Se pinta un anillo donde
/// estaba, DESPUÉS de calcular la huella: la huella dice si la pantalla se movió, y el ratón
/// moviéndose no es la pantalla moviéndose.
///
/// LA HORA ES LA DE EMPEZAR A COPIAR, no la de terminar: es la cota que <see cref="CuadroDeAntes"/>
/// necesita para poder afirmar «este cuadro es de antes del clic».
/// </remarks>
public sealed class CamaraDeCuadros : IDisposable
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
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    private const int SRCCOPY = 0x00CC0020;
    private const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;

    /// <summary>Cada cuánto se copia la pantalla.</summary>
    /// <remarks>250 ms: un clic humano dura 80–150 ms, así que entre dos cuadros cabe un clic entero y
    /// el de antes siempre es de antes. Más rápido costaría CPU que compite con SAP (patrón nº14).</remarks>
    public const int IntervaloMs = 250;

    /// <summary>Ancho al que se reducen los cuadros.</summary>
    /// <remarks>1280: el texto de SAP sigue legible, cuesta ~1.200 tokens de visión por cuadro (parches
    /// de 28 px), y queda muy por debajo del tope de 2000 px de lado que la API impone cuando una
    /// petición lleva más de 20 imágenes (medido en la doc el 2026-09-06).</remarks>
    public const int AnchoMaximo = 1280;

    private readonly string _carpeta;
    private readonly System.Diagnostics.Stopwatch _reloj;
    private readonly List<CuadroTomado> _cuadros = new();
    private readonly object _candado = new();
    private Thread? _hilo;
    private volatile bool _parar;
    private long _costoTotalMs, _peorMs; private int _tomas;

    public CamaraDeCuadros(string carpetaDeCuadros, System.Diagnostics.Stopwatch relojDeLaDemo)
    {
        _carpeta = carpetaDeCuadros;
        _reloj = relojDeLaDemo;
    }

    /// <summary>Todo lo tomado hasta ahora, en orden de hora.</summary>
    public IReadOnlyList<CuadroTomado> Cuadros { get { lock (_candado) return _cuadros.ToList(); } }

    public void Arrancar()
    {
        if (_hilo != null) return;
        Directory.CreateDirectory(_carpeta);
        _parar = false;
        _hilo = new Thread(Bucle) { IsBackground = true, Name = "camara-de-cuadros", Priority = ThreadPriority.BelowNormal };
        _hilo.Start();
        LogBus.Log("camara", $"cámara de cuadros en marcha: cada {IntervaloMs} ms, a {AnchoMaximo} px de ancho → {_carpeta}");
    }

    public void Parar()
    {
        _parar = true;
        _hilo?.Join(2000);
        _hilo = null;
        int n; lock (_candado) n = _cuadros.Count;
        LogBus.Log("camara", $"⏱ TIEMPOS cámara: {n} cuadro(s) · media {(_tomas > 0 ? _costoTotalMs / _tomas : 0)} ms · peor {_peorMs} ms");
    }

    private void Bucle()
    {
        while (!_parar)
        {
            long inicio = _reloj.ElapsedMilliseconds;
            try { Tomar(inicio); }
            catch (Exception ex) { LogBus.Log("camara", $"un cuadro no se pudo tomar: {ex.GetType().Name}: {ex.Message}"); }
            long costo = _reloj.ElapsedMilliseconds - inicio;
            _costoTotalMs += costo; _tomas++; if (costo > _peorMs) _peorMs = costo;
            int dormir = (int)Math.Max(20, IntervaloMs - costo);
            Thread.Sleep(dormir);
        }
    }

    private void Tomar(long horaMs)
    {
        int x = GetSystemMetrics(SM_XVIRTUALSCREEN), y = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int w = GetSystemMetrics(SM_CXVIRTUALSCREEN), h = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        if (w <= 0 || h <= 0) return;
        GetCursorPos(out var cursor);

        IntPtr screen = GetDC(IntPtr.Zero);
        IntPtr mem = CreateCompatibleDC(screen);
        IntPtr bmp = CreateCompatibleBitmap(screen, w, h);
        IntPtr old = SelectObject(mem, bmp);
        BitBlt(mem, 0, 0, w, h, screen, x, y, SRCCOPY);
        SelectObject(mem, old);
        DeleteDC(mem);
        ReleaseDC(IntPtr.Zero, screen);

        BitmapSource fuente = Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        DeleteObject(bmp);

        double escala = w > AnchoMaximo ? (double)AnchoMaximo / w : 1.0;
        BitmapSource reducido = escala < 1.0 ? new TransformedBitmap(fuente, new ScaleTransform(escala, escala)) : fuente;
        int ancho = reducido.PixelWidth, alto = reducido.PixelHeight;

        ulong huella = Huella(reducido);

        // El anillo del ratón, en coordenadas del cuadro reducido. Se dibuja sobre una copia para
        // que la huella (ya calculada) no dependa de por dónde pasó el cursor.
        int cx = (int)((cursor.X - x) * escala), cy = (int)((cursor.Y - y) * escala);
        var lienzo = new DrawingVisual();
        using (var dc = lienzo.RenderOpen())
        {
            dc.DrawImage(reducido, new Rect(0, 0, ancho, alto));
            dc.DrawEllipse(null, new Pen(Brushes.OrangeRed, 3), new Point(cx, cy), 11, 11);
            dc.DrawEllipse(Brushes.OrangeRed, null, new Point(cx, cy), 2.5, 2.5);
        }
        var final = new RenderTargetBitmap(ancho, alto, 96, 96, PixelFormats.Pbgra32);
        final.Render(lienzo);
        final.Freeze();

        string ruta = Path.Combine(_carpeta, $"{horaMs:D7}.jpg");
        var codificador = new JpegBitmapEncoder { QualityLevel = 72 };
        codificador.Frames.Add(BitmapFrame.Create(final));
        using (var fs = File.Create(ruta)) codificador.Save(fs);

        lock (_candado) _cuadros.Add(new CuadroTomado(horaMs, ruta, huella, cx, cy, ancho, alto));
    }

    /// <summary>Resumen barato de la imagen: 16×16 celdas en gris, un bit por celda contra la media.</summary>
    /// <remarks>Lo que se compara son dos cuadros seguidos del MISMO puesto: sirve para «¿se movió la
    /// pantalla?», no para reconocer pantallas. Un umbral tan grueso hace que un parpadeo del cursor
    /// de texto no cuente como movimiento — un umbral fino haría que nada se asentara nunca.</remarks>
    public static ulong Huella(BitmapSource imagen)
    {
        var chica = new TransformedBitmap(imagen, new ScaleTransform(16.0 / imagen.PixelWidth, 16.0 / imagen.PixelHeight));
        var gris = new FormatConvertedBitmap(chica, PixelFormats.Gray8, null, 0);
        var px = new byte[16 * 16];
        gris.CopyPixels(px, 16, 0);
        // 256 celdas en 64 bits: se agrupan de 4 en 4 (2×2) y se compara contra la media global.
        int media = 0; foreach (var b in px) media += b; media /= px.Length;
        ulong h = 0;
        for (int by = 0; by < 8; by++)
            for (int bx = 0; bx < 8; bx++)
            {
                int suma = px[(by * 2) * 16 + bx * 2] + px[(by * 2) * 16 + bx * 2 + 1]
                         + px[(by * 2 + 1) * 16 + bx * 2] + px[(by * 2 + 1) * 16 + bx * 2 + 1];
                if (suma / 4 > media) h |= 1UL << (by * 8 + bx);
            }
        return h;
    }

    public void Dispose() => Parar();
}
