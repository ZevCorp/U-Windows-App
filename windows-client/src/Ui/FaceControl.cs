using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace U.WindowsClient.Ui;

/// <summary>Modos de la carita: claro (fondo blanco) u oscuro (línea blanca).</summary>
public enum FaceTheme { Light, Dark }

/// <summary>
/// Qué está haciendo Ü, dicho con la cara. Sustituye al viejo booleano <c>Thinking</c>: tener los dos
/// garantizaría que algún día alguien ponga uno sin tocar el otro y la carita diga dos cosas a la vez.
///
/// Ocho y no seis por dos separaciones que importan:
///   · <see cref="Detenido"/> ≠ <see cref="Fallo"/> — «yo lo paré» y «se rompió solo» son causas
///     distintas con acciones distintas; juntarlas es el vicio de los mensajes que no distinguen.
///   · <see cref="Hablando"/> existe porque hubo que crear la señal en VoiceIO, y una capacidad sin
///     quien la use se convierte en código muerto (le pasó a ManifestAsync durante meses).
/// </summary>
public enum FaceMood
{
    Reposo,
    Escuchando,
    /// <summary>
    /// Hay una conversación abierta y ahora mismo no está diciendo nada.
    ///
    /// No es <see cref="Escuchando"/> aunque lo parezca, y la diferencia es CUÁNTO DURA. Escuchando
    /// se hizo para el dictado: ocho segundos como mucho, con los ojos bien abiertos y respirando,
    /// porque en ocho segundos eso se lee como atención. Una conversación en vivo dura minutos, y
    /// ahí lo mismo pasa a ser una carita con los ojos como platos que jadea sin parar delante de
    /// alguien que está trabajando (2026-08-05, lo notó el usuario en cuanto se conectaron las dos
    /// cosas). Atender mucho rato se parece más a estar quieto que a moverse.
    /// </summary>
    Conversando,
    Trabajando,
    Grabando,
    Esperando,
    Hablando,
    Detenido,
    Fallo,
}

/// <summary>
/// La carita del asistente (misma que la app Android: <c>FaceView.kt</c>), portada a WPF:
/// cejas curvas + ojos de línea vertical + sonrisa bezier sobre un squircle. Coordenadas en el sistema
/// original del SVG (viewBox -75..75), escaladas al tamaño del control. Solo dibuja y anima; no decide
/// nada: quién está en cada momento lo decide FaceWindow y lo dice por <see cref="Mood"/>.
///
/// DOS REGLAS DE RENDIMIENTO que no son negociables, porque este control se dibuja a mano y hay dos
/// instancias vivas siempre:
///
///  1. **Los squircles se cachean.** Cada uno son 73 puntos con dos `Math.Pow`, y se dibujan dos por
///     render. Solo dependen del tamaño, así que recalcularlos en cada cuadro es tirar trabajo.
///  2. **Toda animación CONTINUA va en RenderTransform, nunca en una DependencyProperty con
///     AffectsRender.** Las transform las compone el sistema sin repintar; una DP animada en bucle
///     serían 60 repintados por segundo × 2 geometrías × 2 instancias, durante toda una corrida.
///     Las DP animadas quedan para lo puntual: <see cref="Blink"/> y la mirada.
/// </summary>
public sealed class FaceControl : FrameworkElement
{
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly RotateTransform _tilt = new(0);

    public FaceControl()
    {
        RenderTransformOrigin = new Point(0.5, 0.5);
        // Grupo y no una sola transform: el pulso escala y el balanceo de «trabajando» rota, y las
        // dos tienen que poder convivir sin pisarse.
        var group = new TransformGroup();
        group.Children.Add(_scale);
        group.Children.Add(_tilt);
        RenderTransform = group;
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

    /// <summary>Qué está haciendo Ü. Cambia la pose, el acento de color y la animación continua.</summary>
    public FaceMood Mood
    {
        get => (FaceMood)GetValue(MoodProperty);
        set => SetValue(MoodProperty, value);
    }

    public static readonly DependencyProperty MoodProperty = DependencyProperty.Register(
        nameof(Mood), typeof(FaceMood), typeof(FaceControl),
        new FrameworkPropertyMetadata(FaceMood.Reposo, FrameworkPropertyMetadataOptions.AffectsRender,
            (d, e) => ((FaceControl)d).OnMoodChanged((FaceMood)e.NewValue)));

    /// <summary>0 = ojos abiertos, 1 = cerrados. Lo anima <see cref="Blink"/>.</summary>
    public double BlinkClosed
    {
        get => (double)GetValue(BlinkClosedProperty);
        set => SetValue(BlinkClosedProperty, value);
    }

    public static readonly DependencyProperty BlinkClosedProperty = DependencyProperty.Register(
        nameof(BlinkClosed), typeof(double), typeof(FaceControl),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// Cuánto está ABIERTA la boca, 0 (cerrada, la sonrisa de siempre) a 1 (bien abierta).
    /// </summary>
    /// <remarks>
    /// Va en una DependencyProperty con AffectsRender pese a la regla de esta clase —lo continuo va
    /// en RenderTransform— porque aquí no hay transform que valga: la boca no se mueve ni se escala,
    /// CAMBIA DE FORMA, y una geometría distinta hay que dibujarla. Lo que sí se respeta es el
    /// motivo de la regla: esto solo se anima mientras Ü habla (no en reposo, que es casi todo el
    /// tiempo), a ~16 cuadros por segundo y no a 60, y quien la mueve redondea el valor para no
    /// disparar un repintado por cada variación imperceptible.
    /// </remarks>
    public double MouthOpen
    {
        get => (double)GetValue(MouthOpenProperty);
        set => SetValue(MouthOpenProperty, value);
    }

    public static readonly DependencyProperty MouthOpenProperty = DependencyProperty.Register(
        nameof(MouthOpen), typeof(double), typeof(FaceControl),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// La FORMA de la abertura: 0 = ancha y plana (como al decir «i» o «e»), 1 = redonda y estrecha
    /// (como al decir «o» o «u»). Con la altura, es lo que distingue las bocas del dibujo.
    ///
    /// Por el volumen no se puede saber qué vocal se está diciendo —eso exigiría analizar el sonido,
    /// que es otro problema entero— así que esto no pretende acertar la vocal: pretende que la boca
    /// no repita siempre el mismo gesto, que es lo que delata a un muñeco.
    /// </summary>
    public double MouthRound
    {
        get => (double)GetValue(MouthRoundProperty);
        set => SetValue(MouthRoundProperty, value);
    }

    public static readonly DependencyProperty MouthRoundProperty = DependencyProperty.Register(
        nameof(MouthRound), typeof(double), typeof(FaceControl),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    // ── Las poses ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Los diez escalares que definen una expresión, más el acento. Antes eran diez ternarios sobre
    /// un booleano dentro de OnRender; con ocho estados, una tabla.
    /// </summary>
    private readonly record struct FacePose(
        double BrowL, double BrowR, double CurveL, double CurveR,
        double EyeOpen, double Squint, double MouthCurve, double MouthWidth,
        double CornerL, double CornerR, Color? Accent);

    /// <summary>
    /// Reposo y Trabajando conservan EXACTAMENTE los valores que tenían como <c>thinking</c> false y
    /// true: el rediseño no debe cambiar cómo se ve lo que ya existía.
    ///
    /// <c>MouthCurve</c> negativo en <see cref="FaceMood.Fallo"/> no es un truco: con la bezier actual,
    /// un valor negativo pone el punto medio por debajo de las comisuras y sale un ceño, sin tocar el
    /// dibujo.
    /// </summary>
    private static readonly Dictionary<FaceMood, FacePose> Poses = new()
    {
        //                              browL browR curvL curvR  eyeOpen squint mouthCurve  width   cornL cornR  acento
        [FaceMood.Reposo] = new(2, 2.5, 0.3, 0.4, 0.85, 0.15, 0.7, 34 * 1.1, 0.3, 0.5, null),
        // SIN TINTE, los dos. Escuchar y trabajar son los estados en los que más rato pasa la carita
        // —con la conversación en vivo, «escuchando» es casi toda la sesión— y teñir la cara entera
        // de verde o de azul durante minutos cansa a quien la tiene siempre delante (2026-08-05,
        // decidido por el usuario). Su gesto ya los distingue: cejas altas y ojos abiertos para
        // escuchar, ceja torcida para trabajar. El color se guarda para lo que interrumpe —grabando,
        // esperando, fallo—, que es cuando merece la pena robar la mirada.
        [FaceMood.Trabajando] = new(-1, 4, 0.1, 0.5, 0.75, 0.20, 0.7, 34 * 0.95, 0.2, 0.1, null),
        // Cejas altas y ojos bien abiertos: la cara de estar prestando atención.
        [FaceMood.Escuchando] = new(6, 6, 0.35, 0.35, 1.00, 0.05, 0.6, 34 * 1.05, 0.35, 0.35, null),
        // Casi el reposo, con la ceja un pelo más alta. A propósito: es lo que se ve durante toda una
        // conversación —el rato en que no dice nada es la mayor parte— y tiene que poder mirarse sin
        // cansar. Quien quiera saber si el micrófono sigue abierto lo tiene en el botón, en rojo.
        [FaceMood.Conversando] = new(3, 3.5, 0.3, 0.4, 0.90, 0.12, 0.7, 34 * 1.1, 0.3, 0.45, null),
        // Quieta y mirando de frente: «te estoy viendo». La quietud es la señal.
        [FaceMood.Grabando] = new(2, 2, 0.3, 0.3, 0.95, 0.10, 0.4, 34 * 0.8, 0.2, 0.2, UiPalette.Fallo),
        // Asimetría interrogativa: una ceja sube, la otra baja.
        [FaceMood.Esperando] = new(6, -1, 0.45, 0.15, 0.9, 0.10, 0.2, 34 * 0.95, 0.4, 0.1, UiPalette.Atencion),
        [FaceMood.Hablando] = new(2, 2.5, 0.3, 0.4, 0.85, 0.15, 0.9, 34 * 1.25, 0.4, 0.4, null),
        // Boca recta y ojos entornados: ni contenta ni enfadada, parada.
        [FaceMood.Detenido] = new(0, 0, 0.2, 0.2, 0.6, 0.25, 0.0, 34 * 0.9, 0.0, 0.0, UiPalette.Inactivo),
        [FaceMood.Fallo] = new(-3, -3, 0.15, 0.15, 0.8, 0.15, -0.5, 34 * 0.9, 0.1, 0.1, UiPalette.Fallo),
    };

    private FacePose CurrentPose => Poses.TryGetValue(Mood, out var p) ? p : Poses[FaceMood.Reposo];

    /// <summary>
    /// Coreografía del estado: limpia lo del anterior y arranca lo del nuevo.
    ///
    /// Lo primero que hace es soltar las animaciones retenidas de <c>BlinkClosed</c> y
    /// <c>EyeShift</c>: <see cref="Blink"/> y la mirada usan <c>BeginAnimation</c> con el
    /// <c>FillBehavior</c> por defecto (HoldEnd), y mientras un valor está retenido WPF IGNORA
    /// cualquier asignación. Sin esta limpieza, una carita que parpadeó justo antes de cambiar de
    /// estado se quedaría con los ojos a medio cerrar para siempre.
    /// </summary>
    private void OnMoodChanged(FaceMood mood)
    {
        BeginAnimation(BlinkClosedProperty, null);
        BeginAnimation(EyeShiftProperty, null);
        BlinkClosed = 0;
        EyeShift = 0;

        StopContinuous();

        switch (mood)
        {
            case FaceMood.Escuchando:
                // Respiración: el único estado con movimiento propio permanente, porque «te escucho»
                // tiene que notarse mientras dura el micrófono (8 s como mucho).
                Breathe(from: 1.0, to: 1.05, ms: 1200);
                break;

            case FaceMood.Trabajando:
                // Balanceo mínimo. Es una rotación, no un repintado: cuesta cero por cuadro.
                Sway(degrees: 3, ms: 2400);
                break;

            case FaceMood.Fallo:
                Pulse();   // un solo golpe al entrar, no en bucle
                break;
        }
    }

    private void StopContinuous()
    {
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _tilt.BeginAnimation(RotateTransform.AngleProperty, null);
        _scale.ScaleX = _scale.ScaleY = 1;
        _tilt.Angle = 0;
    }

    private void Breathe(double from, double to, int ms)
    {
        var a = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    private void Sway(double degrees, int ms)
    {
        var a = new DoubleAnimation(-degrees, degrees, TimeSpan.FromMilliseconds(ms))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        _tilt.BeginAnimation(RotateTransform.AngleProperty, a);
    }

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
    /// <summary>
    /// Mirar hacia un lado y QUEDARSE mirando, hasta que se suelte.
    ///
    /// Es lo que hace creíble que la carita esté señalando algo: ponerse al lado del elemento y
    /// seguir mirando al frente es raro, casi desatento. Como <see cref="IrJuntoA"/> la centra en
    /// vertical con el elemento, la dirección es puramente lateral y basta con EyeShift
    /// (2026-08-05, pedido por el usuario).
    ///
    /// Mientras está fija, los gestos de reposo no le corren los ojos: no se puede estar mirando
    /// algo y distraerse cada ocho segundos.
    /// </summary>
    public void MirarHacia(bool izquierda)
    {
        _mirandoFijo = true;
        double objetivo = (izquierda ? -1 : 1) * 3.5;
        var ease = new KeySpline(0.3, 0, 0.2, 1);
        var a = new DoubleAnimationUsingKeyFrames();
        a.KeyFrames.Add(new SplineDoubleKeyFrame(objetivo, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(320)), ease));
        BeginAnimation(EyeShiftProperty, a);
    }

    /// <summary>Vuelve a mirar al frente y deja que los gestos de reposo sigan su curso.</summary>
    public void DejarDeMirar()
    {
        if (!_mirandoFijo) return;
        _mirandoFijo = false;
        var ease = new KeySpline(0.3, 0, 0.2, 1);
        var a = new DoubleAnimationUsingKeyFrames();
        a.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420)), ease));
        BeginAnimation(EyeShiftProperty, a);
    }

    private bool _mirandoFijo;

    private void LookAround()
    {
        if (_mirandoFijo) return;   // está mirando algo: no se distrae
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

        // --- El acento del estado ---
        //
        // A 42 px de lado la pluma de los rasgos mide 1,12 px: un anillo o un punto de color
        // sencillamente NO SE VEN. Lo único legible a ese tamaño son los rasgos (que son casi toda la
        // tinta) y el bloque de relleno. Así que el acento tiñe los dos: la carita entera cambia de
        // carácter y se distingue de reojo, que es de lo que se trata.
        FacePose pose = CurrentPose;
        if (pose.Accent is Color accent)
        {
            faceLine = accent;
            // Sobre el relleno casi negro del tema oscuro hace falta más peso para que se aprecie.
            double peso = dark ? 0.20 : 0.14;
            fillTop = UiPalette.Blend(fillTop, accent, peso);
            fillBottom = UiPalette.Blend(fillBottom, accent, peso);
        }

        // Relleno del rostro (degradado diagonal, esquina sup-izq → inf-der).
        var fill = new LinearGradientBrush(fillTop, fillBottom, new Point(0, 0), new Point(1, 1));
        fill.Freeze();
        dc.DrawGeometry(fill, null, OuterSquircle(cx, cy, r));

        if (dark)
        {
            // Hairline blanca delgada, separada del borde hacia adentro lo mismo que su grosor.
            double hairline = 0.35 * s;
            var pen = new Pen(new SolidColorBrush(faceLine), hairline);
            pen.Freeze();
            dc.DrawGeometry(null, pen, InnerSquircle(cx, cy, r - hairline * 1.5));
        }
        else
        {
            var pen = new Pen(new SolidColorBrush(faceBorder), 1.5 * s);
            pen.Freeze();
            dc.DrawGeometry(null, pen, OuterSquircle(cx, cy, r));
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

        double browL = pose.BrowL, browR = pose.BrowR;
        double curveL = pose.CurveL, curveR = pose.CurveR;
        double eyeOpen = pose.EyeOpen, squint = pose.Squint;
        double mouthCurve = pose.MouthCurve, mouthWidth = pose.MouthWidth;
        double cornerL = pose.CornerL, cornerR = pose.CornerR;

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

        // ABIERTA O CERRADA. Cerrada es la sonrisa de siempre —una línea— y así se queda en reposo:
        // esto no puede cambiar la cara que ya existía. Abierta, la MISMA curva pasa a ser el labio
        // de arriba y se le añade otro por debajo, cerrando una figura que se rellena. Un solo
        // dibujo con dos estados, en vez de dos bocas distintas que habría que mantener a la par.
        double abierta = Math.Max(0, Math.Min(1, MouthOpen));
        if (abierta <= 0.02)
        {
            var linea = new StreamGeometry();
            using (var g = linea.Open())
            {
                g.BeginFigure(new Point(X(-half), Y(leftY)), false, false);
                g.BezierTo(new Point(X(-half * 0.3 + shift), Y(midY)), new Point(X(half * 0.3 + shift), Y(midY)), new Point(X(half), Y(rightY)), true, false);
            }
            linea.Freeze();
            dc.DrawGeometry(null, stroke, linea);
        }
        else
        {
            // Redonda estrecha la boca; ancha la deja como está. Es lo que separa una «o» de una «e».
            double redonda = Math.Max(0, Math.Min(1, MouthRound));
            double halfA = half * (1 - redonda * 0.58);
            double alto = 3 + abierta * 20 * (0.75 + redonda * 0.45);

            // La comisura sube un poco al abrir, como una boca de verdad: si las esquinas se quedan
            // clavadas mientras el centro baja, parece una bisagra y no una boca.
            double lY = leftY - abierta * 2, rY = rightY - abierta * 2;
            double mY = midY - abierta * 1.5;
            double centro = (lY + rY) / 2;

            // DE MEDIA LUNA A ÓVALO. Con la sonrisa de siempre arriba, estrechar la boca la cierra en
            // PUNTA y sale un colmillo, no una «o» (2026-08-05, visto al dibujarlas todas seguidas).
            // Así que al redondear no basta con estrechar: el labio de arriba tiene que dejar de
            // sonreír —se levanta hasta curvarse al revés— y los dos tiran hacia fuera, que es lo que
            // convierte la media luna en un óvalo.
            double Mezcla(double plano, double redondo) => plano + (redondo - plano) * redonda;
            double ctrlArribaY = Mezcla(mY, centro - alto * 0.45);
            double ctrlAbajoY = Mezcla(mY + alto, centro + alto * 0.55);
            double anchoArriba = halfA * Mezcla(0.30, 0.62);
            double anchoAbajo = halfA * Mezcla(0.45, 0.78);

            var boca = new StreamGeometry();
            using (var g = boca.Open())
            {
                g.BeginFigure(new Point(X(-halfA), Y(lY)), true, true);
                g.BezierTo(new Point(X(-anchoArriba + shift), Y(ctrlArribaY)), new Point(X(anchoArriba + shift), Y(ctrlArribaY)), new Point(X(halfA), Y(rY)), false, false);
                g.BezierTo(new Point(X(anchoAbajo), Y(ctrlAbajoY)), new Point(X(-anchoAbajo), Y(ctrlAbajoY)), new Point(X(-halfA), Y(lY)), false, false);
            }
            boca.Freeze();
            dc.DrawGeometry(stroke.Brush, null, boca);

            // La lengua. Solo cuando la boca está lo bastante abierta para que se vea algo dentro:
            // dibujarla siempre la convierte en una mancha pegada al labio.
            if (abierta > 0.35)
            {
                // DÓNDE ACABA LA BOCA NO ES DONDE ESTÁ SU PUNTO DE CONTROL. Una bezier cúbica no
                // llega hasta sus controles: con los dos a la misma altura se queda en tres cuartos
                // del camino. Colocar la lengua contando desde el control la dejaba POR DEBAJO del
                // labio, asomando fuera de la boca (2026-08-05). El punto más bajo de la curva sale
                // de evaluarla en la mitad: (P0 + 3·C1 + 3·C2 + P3) / 8.
                double fondo = (lY + rY) / 8 + ctrlAbajoY * 0.75;

                double rx = halfA * 0.42, ry = alto * 0.20;
                var lengua = new EllipseGeometry(new Point(X(shift * 0.4), Y(fondo - ry * 0.25)), rx * s, ry * s);
                lengua.Freeze();

                // Y ADEMÁS se recorta contra la boca, que es lo que garantiza que no pueda salirse
                // aunque la cuenta de arriba falle en algún tamaño raro: la geometría manda sobre la
                // aritmética. De paso es como se ve en el dibujo de referencia — la lengua no es un
                // óvalo entero flotando, es un óvalo cortado por el borde del labio.
                dc.PushClip(boca);
                dc.DrawGeometry(UiPalette.PincelLengua, null, lengua);
                dc.Pop();
            }
        }

        dc.Pop();
    }

    // Caché de los dos squircles. Solo dependen de (cx, cy, r), que solo cambian si el control cambia
    // de tamaño — es decir, casi nunca. Sin caché se reconstruían 2 × 73 puntos con dos Math.Pow cada
    // uno EN CADA REPINTADO, y hay repintados de sobra: cada parpadeo anima BlinkClosed, que lleva
    // AffectsRender, así que ya hoy se repinta a la velocidad del cuadro varias veces por minuto.
    private Geometry? _outerCache, _innerCache;
    private double _cacheCx, _cacheCy, _cacheR, _cacheInnerR;

    private Geometry OuterSquircle(double cx, double cy, double r)
    {
        if (_outerCache == null || cx != _cacheCx || cy != _cacheCy || r != _cacheR)
        {
            _outerCache = Squircle(cx, cy, r);
            _cacheCx = cx; _cacheCy = cy; _cacheR = r;
        }
        return _outerCache;
    }

    private Geometry InnerSquircle(double cx, double cy, double r)
    {
        if (_innerCache == null || r != _cacheInnerR || cx != _cacheCx || cy != _cacheCy)
        {
            _innerCache = Squircle(cx, cy, r);
            _cacheInnerR = r;
        }
        return _innerCache;
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
