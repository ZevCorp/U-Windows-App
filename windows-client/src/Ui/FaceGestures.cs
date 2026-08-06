using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace U.WindowsClient.Ui;

/// <summary>
/// Gestos de la carita, calcados de la burbuja de Android (<c>FloatingBubble.kt</c>): un toque, doble
/// toque, mantener oprimido (estático) y arrastrar. Al soltar, la ventana se va SIEMPRE a un lado
/// (ver <see cref="EdgeSnap"/>), con la velocidad del gesto decidiendo a cuál y con cuánto ímpetu.
///
/// Maneja el arrastre a mano (no <c>DragMove</c>) precisamente para poder medir esa velocidad, además
/// de para distinguir toque de arrastre y de mantener.
/// </summary>
public sealed class FaceGestures
{
    private readonly Window _win;
    private readonly UIElement _face;

    /// <summary>Un toque simple (sin arrastre ni doble toque).</summary>
    public Action? SingleTap { get; set; }
    /// <summary>Doble toque.</summary>
    public Action? DoubleTap { get; set; }
    /// <summary>Mantener oprimido en un lugar estático (sin arrastrar).</summary>
    public Action? LongPress { get; set; }

    /// <summary>
    /// La ventana quedó en un sitio nuevo porque el usuario la movió. Llega con el destino FINAL.
    ///
    /// Con el destino y no leyendo <c>Left</c>/<c>Top</c> a propósito: en un lanzamiento esas
    /// propiedades están a mitad de la animación, así que leerlas daría un punto intermedio. Pasar el
    /// destino calculado evita tener que esperar al <c>Completed</c> para saber dónde va a caer.
    /// </summary>
    public Action<double, double>? Moved { get; set; }

    /// <summary>(13 px)² para pasar de «toque» a «arrastre». Eran 10 px, y un pulso normal los cruza
    /// sin querer: ibas a hacer clic y la carita salía disparada hacia un borde.</summary>
    private const double MoveThresholdSq = 169;

    /// <summary>
    /// Cuánto hay que mantener oprimido para cambiar de tema.
    ///
    /// Eran 450 ms, y ese era el motivo de que «los gestos no funcionaran»: un clic deliberado sobre
    /// un objetivo de 42 px dura tranquilamente medio segundo, así que al ir a colapsar la carita le
    /// cambiabas el tema. Y encima el clic se perdía, porque una vez disparado el mantener-oprimido
    /// el soltar ya no cuenta (ver OnUp). 750 ms sigue siendo cómodo de hacer a propósito y deja de
    /// dispararse solo.
    /// </summary>
    private const int LongPressMs = 750;

    private const int TapWindowMs = 250; // ventana para distinguir 1 vs 2 toques

    private readonly DispatcherTimer _longTimer;
    private readonly DispatcherTimer _tapTimer;

    // Estado del gesto en curso.
    private POINT _downCursor;
    private double _downLeft, _downTop;
    private double _scaleX = 1, _scaleY = 1; // px físicos → DIP (alta DPI)
    private bool _pressed, _moved, _longFired;
    private int _tapCount;

    // Muestreo de velocidad para el fling (px físicos / s).
    private POINT _lastCursor;
    private long _lastTick;
    private double _vx, _vy;

    public FaceGestures(Window win, UIElement face)
    {
        _win = win;
        _face = face;

        _longTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(LongPressMs) };
        _longTimer.Tick += OnLongTimer;
        _tapTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TapWindowMs) };
        _tapTimer.Tick += OnTapTimer;

        _face.MouseLeftButtonDown += OnDown;
        _face.MouseMove += OnMove;
        _face.MouseLeftButtonUp += OnUp;
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true; // que el DragMove del Header no dispare además
        _face.CaptureMouse();

        // Corta cualquier fling en curso: fija la posición actual como base antes de seguir.
        FreezeAnimatedPosition();

        var src = PresentationSource.FromVisual(_win);
        if (src?.CompositionTarget != null)
        {
            _scaleX = src.CompositionTarget.TransformToDevice.M11;
            _scaleY = src.CompositionTarget.TransformToDevice.M22;
        }

        GetCursorPos(out _downCursor);
        _downLeft = _win.Left;
        _downTop = _win.Top;
        _lastCursor = _downCursor;
        _lastTick = System.Diagnostics.Stopwatch.GetTimestamp();
        _vx = _vy = 0;
        _pressed = true;
        _moved = false;
        _longFired = false;
        _longTimer.Start();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_pressed) return;
        GetCursorPos(out POINT c);
        double dx = c.X - _downCursor.X;
        double dy = c.Y - _downCursor.Y;

        if (!_moved && dx * dx + dy * dy > MoveThresholdSq)
        {
            _moved = true;
            _longTimer.Stop(); // ya no es "mantener estático": es arrastre
            _tapTimer.Stop();  // y cancela cualquier toque simple pendiente (tocar y luego arrastrar)
            _tapCount = 0;
        }

        if (_moved)
        {
            _win.Left = _downLeft + dx / _scaleX;
            _win.Top = _downTop + dy / _scaleY;

            // EL RELOJ TIENE QUE VER EL GESTO. Esto medía con Environment.TickCount64, que avanza a
            // saltos de ~15 ms: un lanzamiento seco dura 60 ms enteros, así que la mayoría de las
            // muestras caían con dt=0 y se descartaban, y las pocas que pasaban repartían el
            // recorrido sobre un tiempo redondeado hacia arriba. El resultado es que un gesto rápido
            // y uno lento devolvían casi la misma velocidad —medido: 295 px contra 338— y por eso la
            // fuerza no cambiaba nada por más que se ajustara la proyección (2026-08-06).
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            double dt = (now - _lastTick) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (dt > 0.5)   // medio milisegundo: por debajo el ratón no ha llegado a moverse
            {
                // Velocidad instantánea, MUY suavizada con la anterior.
                //
                // El peso importa más de lo que parece: con la instantánea mandando (0.6), un
                // temblor de 5 px en el último fotograma antes de soltar da ~500 DIP/s, y esa cifra
                // —que no describe el gesto, describe el pulso de la mano— era la que decidía si Ü
                // cruzaba la pantalla. Ahora manda la trayectoria (0.65) y no el último instante.
                double vx = (c.X - _lastCursor.X) / dt * 1000.0;
                double vy = (c.Y - _lastCursor.Y) / dt * 1000.0;
                _vx = _vx * 0.65 + vx * 0.35;
                _vy = _vy * 0.65 + vy * 0.35;
                _lastCursor = c;
                _lastTick = now;
            }
        }
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pressed) return;
        _pressed = false;
        _face.ReleaseMouseCapture();
        _longTimer.Stop();

        if (_longFired) return; // el mantener-oprimido ya cambió de modo; el soltar no cuenta

        if (_moved)
        {
            // SIEMPRE a un lado. Ü vive pegado a un borde, como la burbuja de Android: soltarlo en
            // mitad de la pantalla lo dejaba encima del trabajo del usuario, que es justo donde no
            // tiene que estar. La velocidad no decide SI se va al borde, decide a CUÁL y con qué
            // ímpetu llega.
            SnapToSide();
            return;
        }

        // Fue un toque: distinguir 1 de 2 con una ventana breve (como el gesto de Android).
        _tapCount++;
        if (_tapCount >= 2)
        {
            _tapTimer.Stop();
            _tapCount = 0;
            DoubleTap?.Invoke();
        }
        else
        {
            _tapTimer.Start();
        }
    }

    private void OnTapTimer(object? sender, EventArgs e)
    {
        _tapTimer.Stop();
        if (_tapCount == 1)
        {
            _tapCount = 0;
            SingleTap?.Invoke();
        }
    }

    private void OnLongTimer(object? sender, EventArgs e)
    {
        _longTimer.Stop();
        if (_pressed && !_moved)
        {
            _longFired = true;
            _tapTimer.Stop(); // el mantener-oprimido cancela cualquier toque simple pendiente
            _tapCount = 0;
            LongPress?.Invoke();
        }
    }

    /// <summary>
    /// Al soltar, la ventana se va a un lado. La regla vive en <see cref="EdgeSnap"/> porque el
    /// arrastre por el cuerpo de la barra tiene que acabar igual y ese no pasa por aquí.
    /// </summary>
    private void SnapToSide() =>
        EdgeSnap.Aplicar(_win, _vx / _scaleX, _vy / _scaleY, (l, t) => Moved?.Invoke(l, t));

    /// <summary>Fija la posición actual (animada) como valor base y detiene cualquier animación de fling.</summary>
    private void FreezeAnimatedPosition()
    {
        double left = _win.Left, top = _win.Top; // getter devuelve el valor animado actual
        _win.BeginAnimation(Window.LeftProperty, null);
        _win.BeginAnimation(Window.TopProperty, null);
        _win.Left = left;
        _win.Top = top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT p);
}
