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

    /// <summary>
    /// Se soltó tras arrastrar, con el cursor en (x, y) en coordenadas de PANTALLA y en DIP.
    /// Devolver <c>true</c> es quedarse el gesto: no se lanza a ningún borde.
    /// </summary>
    /// <remarks>
    /// Sin esto no había forma de que soltar significara otra cosa: <see cref="SnapToSide"/> corre
    /// SIEMPRE, y con razón —la carita vive pegada a un borde—, pero soltarla encima del muelle
    /// tiene que guardarla y sacarla de él tiene que dejarla donde la sueltas. El gancho decide
    /// antes que el borde, y quien no lo cablea se comporta exactamente como antes.
    /// </remarks>
    public Func<double, double, bool>? Soltada { get; set; }

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

    // La ventana para distinguir 1 de 2 toques ya NO vive aquí: la decide ReglaDelToque, que es a
    // quien pregunta el contrato. Tenerla en los dos sitios era pedir que discreparan (nº16).

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
        // El intervalo se pone al soltar y no aquí: DoubleTap se cablea DESPUÉS del constructor (con
        // un inicializador de objeto), así que en este punto todavía no se sabe si hay un segundo
        // toque que distinguir.
        _tapTimer = new DispatcherTimer();
        _tapTimer.Tick += OnTapTimer;

        _face.MouseLeftButtonDown += OnDown;
        _face.MouseMove += OnMove;
        _face.MouseLeftButtonUp += OnUp;

        // AQUÍ HUBO UN PARCHE QUE RECUPERABA LA CAPTURA, y se borra en vez de ajustarse. Nació de un
        // diagnóstico correcto a medias —«mostrar una ventana se lleva la activación»— pero la causa
        // de verdad era otra: quien perdía la captura era la carita PEQUEÑA del muelle, porque al
        // salir el cursor el muelle se plegaba y ese elemento desaparecía del árbol. Recuperar la
        // captura de un elemento que ya no existe no podía funcionar, y de hecho no funcionó dos
        // veces. Sacar la carita ya no pasa por aquí (ver FaceWindow.SacarYArrastrar), así que la
        // maquinaria de compensación sobra — aprendizaje nº6: cuando la causa se entiende, se borra
        // el parche, no se afina.
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
            // PRIMERO, POR SI SOLTAR AQUÍ SIGNIFICA OTRA COSA. Soltarla encima del muelle la guarda
            // y sacarla de él la deja donde la sueltas; las dos tienen que decidirse ANTES del borde,
            // porque el borde ya no es una opción sino una asignación (spec 010).
            if (Soltada != null)
            {
                GetCursorPos(out POINT fin);
                if (Soltada(fin.X / _scaleX, fin.Y / _scaleY)) return;
            }

            // SIEMPRE a un lado. Ü vive pegado a un borde, como la burbuja de Android: soltarlo en
            // mitad de la pantalla lo dejaba encima del trabajo del usuario, que es justo donde no
            // tiene que estar. La velocidad no decide SI se va al borde, decide a CUÁL y con qué
            // ímpetu llega.
            SnapToSide();
            return;
        }

        // Fue un toque. Si hay doble toque cableado, hay que distinguir 1 de 2 con una ventana breve
        // (como el gesto de Android); si no lo hay, no hay nada que distinguir y el toque va YA —
        // esperar sería retardo puro sobre el gesto más usado que tiene la aplicación.
        int espera = ReglaDelToque.EsperaMs(DoubleTap != null);
        if (espera == 0)
        {
            _tapCount = 0;
            SingleTap?.Invoke();
            return;
        }

        _tapCount++;
        if (_tapCount >= 2)
        {
            _tapTimer.Stop();
            _tapCount = 0;
            DoubleTap?.Invoke();
        }
        else
        {
            _tapTimer.Interval = TimeSpan.FromMilliseconds(espera);
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
