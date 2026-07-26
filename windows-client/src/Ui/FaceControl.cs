using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace U.WindowsClient.Ui;

/// <summary>Modos de la carita: claro (fondo blanco) u oscuro (línea blanca).</summary>
public enum FaceTheme { Light, Dark }

/// <summary>
/// La carita del asistente (misma que la app Android: <c>FaceView.kt</c>), portada a WPF:
/// cejas curvas + ojos de línea vertical + sonrisa bezier sobre un squircle. Coordenadas en el sistema
/// original del SVG (viewBox -75..75), escaladas al tamaño del control. Solo dibuja y anima; no decide
/// nada. Soporta los mismos tres temas que Android: claro (fondo blanco), oscuro (línea blanca) y
/// transparente (solo líneas negras, sin relleno).
/// </summary>
public sealed class FaceControl : FrameworkElement
{
    private readonly ScaleTransform _scale = new(1, 1);

    public FaceControl()
    {
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = _scale;
    }

    /// <summary>Modo de color de la carita (claro/oscuro/transparente). Repinta al cambiar.</summary>
    public FaceTheme Theme
    {
        get => (FaceTheme)GetValue(ThemeProperty);
        set => SetValue(ThemeProperty, value);
    }

    public static readonly DependencyProperty ThemeProperty = DependencyProperty.Register(
        nameof(Theme), typeof(FaceTheme), typeof(FaceControl),
        new FrameworkPropertyMetadata(FaceTheme.Light, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Desplazamiento horizontal de los ojos (unidades del viewBox): mirar a un lado.</summary>
    public double EyeShift
    {
        get => (double)GetValue(EyeShiftProperty);
        set => SetValue(EyeShiftProperty, value);
    }

    public static readonly DependencyProperty EyeShiftProperty = DependencyProperty.Register(
        nameof(EyeShift), typeof(double), typeof(FaceControl),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>true = ojos/cejas "pensativos" mientras ejecuta; false = sonrisa en reposo.</summary>
    public bool Thinking
    {
        get => (bool)GetValue(ThinkingProperty);
        set => SetValue(ThinkingProperty, value);
    }

    public static readonly DependencyProperty ThinkingProperty = DependencyProperty.Register(
        nameof(Thinking), typeof(bool), typeof(FaceControl),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>0 = ojos abiertos, 1 = cerrados. Lo anima <see cref="Blink"/>.</summary>
    public double BlinkClosed
    {
        get => (double)GetValue(BlinkClosedProperty);
        set => SetValue(BlinkClosedProperty, value);
    }

    public static readonly DependencyProperty BlinkClosedProperty = DependencyProperty.Register(
        nameof(BlinkClosed), typeof(double), typeof(FaceControl),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Parpadea <paramref name="times"/> veces (onda triangular: abre→cierra→abre).</summary>
    public void Blink(int times)
    {
        if (times <= 0) return;
        var anim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(340L * times) };
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        for (int i = 0; i < times; i++)
        {
            double closeAt = (i + 0.5) / times;
            double openAt = (i + 1.0) / times;
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(closeAt)));
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(openAt)));
        }
        BeginAnimation(BlinkClosedProperty, anim);
    }

    /// <summary>Pulso de vida: encoge y rebota (señal de acción sin coordenadas, como en Android).</summary>
    public void Pulse()
    {
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, MakePulse());
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, MakePulse());
    }

    private static DoubleAnimationUsingKeyFrames MakePulse()
    {
        var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(290) };
        a.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromPercent(0)));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(0.84, KeyTime.FromPercent(110.0 / 290)));
        a.KeyFrames.Add(new SplineDoubleKeyFrame(1.0, KeyTime.FromPercent(1), new KeySpline(0.2, 0.9, 0.3, 1)));
        return a;
    }

    /* ---------- Gestos casuales en reposo: parpadea, mira a los lados, un pulsito de vida ---------- */

    private readonly Random _rng = new();
    private DispatcherTimer? _idleTimer;

    /// <summary>Arranca los gestos casuales (señal sutil de "está viva"): parpadeo, mirada, pulso.</summary>
    public void StartIdle()
    {
        if (_idleTimer != null) return;
        _idleTimer = new DispatcherTimer();
        _idleTimer.Tick += (_, _) => { DoIdleGesture(); ScheduleNextIdle(); };
        ScheduleNextIdle();
        _idleTimer.Start();
    }

    private void ScheduleNextIdle()
    {
        // Tranquila: un gesto cada 8–18 s (antes cambiaba demasiado seguido y se veía ansiosa).
        if (_idleTimer != null) _idleTimer.Interval = TimeSpan.FromMilliseconds(8000 + _rng.Next(10000));
    }

    private void DoIdleGesture()
    {
        int pick = _rng.Next(100);
        if (pick < 65) Blink(_rng.Next(5) == 0 ? 2 : 1); // ~65%: parpadeo (casi siempre uno solo)
        else if (pick < 92) LookAround();                // ~27%: mirar a un lado y volver
        else Pulse();                                    // ~8%: pequeño pulso de vida
    }

    /// <summary>Corre los ojos a un lado (sutil), los mantiene un momento y los devuelve al centro.</summary>
    private void LookAround()
    {
        double target = (_rng.Next(2) == 0 ? -1 : 1) * (2.0 + _rng.NextDouble() * 1.5); // ±2..3.5 unidades
        var ease = new KeySpline(0.3, 0, 0.2, 1);
        var a = new DoubleAnimationUsingKeyFrames();
        a.KeyFrames.Add(new SplineDoubleKeyFrame(target, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(450)), ease));
        a.KeyFrames.Add(new SplineDoubleKeyFrame(target, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1250)), ease)); // se queda mirando
        a.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1800)), ease));
        BeginAnimation(EyeShiftProperty, a);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        double s = Math.Min(w, h) / 150.0;   // unidades del viewBox → px
        double cx = w / 2, cy = h / 2;
        double X(double v) => cx + v * s;
        double Y(double v) => cy + v * s;

        double r = Math.Min(w, h) / 2 - s;

        // Fondo transparente sobre TODO el control: invisible pero sí recibe clics, así la carita
        // siempre se puede agarrar/arrastrar (nunca queda una zona muerta por donde el clic se cuele).
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));

        // --- Paleta según el tema (mismos valores que Palette.kt de Android) ---
        bool dark = Theme == FaceTheme.Dark;
        Color faceLine = dark ? Colors.White : Colors.Black;
        Color fillTop = dark ? Color.FromRgb(0x1A, 0x1A, 0x1A) : Colors.White;
        Color fillBottom = dark ? Colors.Black : Colors.White;
        Color faceBorder = dark ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1F, 0, 0, 0);

        // Relleno del rostro (degradado diagonal, esquina sup-izq → inf-der).
        var fill = new LinearGradientBrush(fillTop, fillBottom, new Point(0, 0), new Point(1, 1));
        fill.Freeze();
        dc.DrawGeometry(fill, null, Squircle(cx, cy, r));

        if (dark)
        {
            // Hairline blanca delgada, separada del borde hacia adentro lo mismo que su grosor.
            double hairline = 0.35 * s;
            var pen = new Pen(new SolidColorBrush(faceLine), hairline);
            pen.Freeze();
            dc.DrawGeometry(null, pen, Squircle(cx, cy, r - hairline * 1.5));
        }
        else
        {
            var pen = new Pen(new SolidColorBrush(faceBorder), 1.5 * s);
            pen.Freeze();
            dc.DrawGeometry(null, pen, Squircle(cx, cy, r));
        }

        // Rasgos: trazo grueso del color de la línea, con el lienzo rotado -2° como en Android.
        var stroke = new Pen(new SolidColorBrush(faceLine), 4 * s)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        stroke.Freeze();

        dc.PushTransform(new RotateTransform(-2, cx, cy));

        bool thinking = Thinking;
        double browL = thinking ? -1 : 2;
        double browR = thinking ? 4 : 2.5;
        double curveL = thinking ? 0.1 : 0.3;
        double curveR = thinking ? 0.5 : 0.4;
        double eyeOpen = thinking ? 0.75 : 0.85;
        double squint = thinking ? 0.2 : 0.15;
        double mouthCurve = 0.7;
        double mouthWidth = 34 * (thinking ? 0.95 : 1.1);
        double cornerL = thinking ? 0.2 : 0.3;
        double cornerR = thinking ? 0.1 : 0.5;

        // Cejas: bezier cuadrática sobre cada ojo.
        foreach (var (bx, bh, c) in new[] { (-30.0, browL, curveL), (30.0, browR, curveR) })
        {
            var brow = new StreamGeometry();
            using (var g = brow.Open())
            {
                g.BeginFigure(new Point(X(bx - 10), Y(-34 - bh)), false, false);
                g.QuadraticBezierTo(new Point(X(bx), Y(-34 - bh - c * 15)), new Point(X(bx + 10), Y(-34 - bh)), true, false);
            }
            brow.Freeze();
            dc.DrawGeometry(null, stroke, brow);
        }

        // Ojos: líneas verticales (el parpadeo los cierra casi del todo; EyeShift los corre a un lado).
        double eyeLen = 25 * eyeOpen * (1 - squint * 0.4) * (1 - BlinkClosed * 0.92);
        foreach (double ex in new[] { -30.0, 30.0 })
            dc.DrawLine(stroke, new Point(X(ex + EyeShift), Y(-14 - eyeLen / 2)), new Point(X(ex + EyeShift), Y(-14 + eyeLen / 2)));

        // Boca: bezier cúbica asimétrica (sonrisa).
        double @base = mouthCurve * 15;
        double leftY = 34 - @base - cornerL * 8;
        double rightY = 34 - @base - cornerR * 8;
        double midY = 34 - mouthCurve * 12;
        double shift = (cornerR - cornerL) * 10;
        double half = mouthWidth / 2;
        var mouth = new StreamGeometry();
        using (var g = mouth.Open())
        {
            g.BeginFigure(new Point(X(-half), Y(leftY)), false, false);
            g.BezierTo(new Point(X(-half * 0.3 + shift), Y(midY)), new Point(X(half * 0.3 + shift), Y(midY)), new Point(X(half), Y(rightY)), true, false);
        }
        mouth.Freeze();
        dc.DrawGeometry(null, stroke, mouth);

        dc.Pop();
    }

    /// <summary>Squircle (superelipse |x|^n+|y|^n=1, n≈4): el "cuadrado con curva de Euler" de Apple.</summary>
    private static Geometry Squircle(double cx, double cy, double r)
    {
        const double n = 4.0;
        const int steps = 72;
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            for (int i = 0; i <= steps; i++)
            {
                double t = 2.0 * Math.PI * i / steps;
                double ct = Math.Cos(t), st = Math.Sin(t);
                double px = cx + r * Math.Sign(ct) * Math.Pow(Math.Abs(ct), 2.0 / n);
                double py = cy + r * Math.Sign(st) * Math.Pow(Math.Abs(st), 2.0 / n);
                var p = new Point(px, py);
                if (i == 0) g.BeginFigure(p, true, true);
                else g.LineTo(p, true, false);
            }
        }
        geo.Freeze();
        return geo;
    }
}
