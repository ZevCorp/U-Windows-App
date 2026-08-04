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
/// toque, mantener oprimido (estático) y arrastrar. Al soltar tras un arrastre con impulso, la lanza a
/// la esquina/borde más cercano con un movimiento suave (fling). Maneja el arrastre a mano (no
/// <c>DragMove</c>) para poder medir la velocidad y distinguir toque de arrastre y de mantener.
/// </summary>
public sealed class FaceGestures
{
    private readonly Window _win;
    private readonly UIElement _face;
    private readonly bool _fling; // solo la carita suelta (colapsada) se lanza al borde

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

    private const double MoveThresholdSq = 100; // (10 px)² para pasar de "toque" a "arrastre"
    private const int LongPressMs = 450;
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

    public FaceGestures(Window win, UIElement face, bool fling)
    {
        _win = win;
        _face = face;
        _fling = fling;

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
        _lastTick = Environment.TickCount64;
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

            long now = Environment.TickCount64;
            double dt = now - _lastTick;
            if (dt > 0)
            {
                // Velocidad instantánea suavizada un poco con la anterior.
                double vx = (c.X - _lastCursor.X) / dt * 1000.0;
                double vy = (c.Y - _lastCursor.Y) / dt * 1000.0;
                _vx = _vx * 0.4 + vx * 0.6;
                _vy = _vy * 0.4 + vy * 0.6;
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
            // Solo se "lanza" al borde si lo sueltas con impulso; si lo arrastras con calma, se queda
            // justo donde lo dejaste (centro, un lado, donde sea), sin saltar a ningún borde.
            if (_fling && IsThrow()) FlingToEdge();
            else SettleInPlace();
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

    private const double ThrowSpeedDip = 650; // DIP/s a partir de los cuales se considera "lanzamiento"

    /// <summary>¿Se soltó con suficiente impulso como para lanzarla al borde?</summary>
    private bool IsThrow()
    {
        double vxDip = _vx / _scaleX, vyDip = _vy / _scaleY;
        return Math.Sqrt(vxDip * vxDip + vyDip * vyDip) >= ThrowSpeedDip;
    }

    /// <summary>Deja la ventana donde se soltó, pero acotada al área de trabajo (nunca fuera de pantalla).</summary>
    private void SettleInPlace()
    {
        var wa = SystemParameters.WorkArea;
        _win.Left = Math.Clamp(_win.Left, wa.Left, Math.Max(wa.Left, wa.Right - _win.ActualWidth));
        _win.Top = Math.Clamp(_win.Top, wa.Top, Math.Max(wa.Top, wa.Bottom - _win.ActualHeight));
        Moved?.Invoke(_win.Left, _win.Top);
    }

    /// <summary>
    /// Proyecta el impulso y anima hasta el borde izquierdo/derecho más cercano (según hacia dónde
    /// iba), con altura acotada — el "lanzamiento" de la burbuja de Android, con un frenado suave.
    /// </summary>
    private void FlingToEdge()
    {
        var wa = SystemParameters.WorkArea;
        double w = _win.ActualWidth, h = _win.ActualHeight;
        // Velocidad de px físicos a DIP.
        double vxDip = _vx / _scaleX, vyDip = _vy / _scaleY;
        double projX = _win.Left + vxDip * 0.16;
        double projY = _win.Top + vyDip * 0.16;

        double centerX = projX + w / 2;
        double destLeft = centerX < (wa.Left + wa.Right) / 2 ? wa.Left : wa.Right - w;
        double destTop = Math.Clamp(projY, wa.Top, Math.Max(wa.Top, wa.Bottom - h));

        double dist = Math.Sqrt(Math.Pow(destLeft - _win.Left, 2) + Math.Pow(destTop - _win.Top, 2));
        // Más lento y suave: duración proporcional a la distancia, con un frenado (ease-out) sin rebote.
        var dur = TimeSpan.FromMilliseconds(Math.Clamp(460 + dist * 0.85, 520, 1150));
        var ease = new QuarticEase { EasingMode = EasingMode.EaseOut };

        Animate(Window.LeftProperty, destLeft, dur, ease);
        Animate(Window.TopProperty, destTop, dur, ease);

        // Se avisa con el DESTINO, no con la posición actual: la animación acaba de empezar y
        // _win.Left todavía vale lo de antes.
        Moved?.Invoke(destLeft, destTop);
    }

    private void Animate(DependencyProperty prop, double to, Duration dur, IEasingFunction ease)
    {
        var anim = new DoubleAnimation(to, dur) { EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd };
        _win.BeginAnimation(prop, anim);
    }

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
