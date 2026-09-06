using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace U.WindowsClient.Ui;

/// <summary>
/// EL ESTUDIO: los colores, las sombras y las piezas con las que se pintan las ventanas de Miracle.
/// Un solo sitio, para que tres pantallas no acaben siendo tres diseños.
/// </summary>
/// <remarks>
/// CLARO, Y LA PROFUNDIDAD LA HACE LA SOMBRA (rediseño del 2026-09-01, pedido por el usuario). La
/// regla de la que sale todo lo demás: **el fondo es claro y lo elevado también; lo que los separa
/// no es el color, es la sombra.** Un botón oscuro sobre fondo claro grita; un botón blanco sobre
/// gris muy claro, con una sombra suave debajo, se lee como un objeto físico que se puede pulsar.
///
/// EL LIENZO ES BLANCO DESDE EL 2026-09-06, y antes no lo era. La regla decía «el fondo no puede
/// ser blanco puro, porque una tarjeta blanca encima sería invisible y solo quedaría la sombra
/// flotando sin objeto», y el argumento sigue siendo cierto: lo que cambió es que el dueño prefiere
/// pagarlo. Ahora lo elevado se lee SOLO por su sombra y su filete, sin el escalón de color que
/// antes hacía la mitad del trabajo. Si una tarjeta deja de distinguirse, se le sube la sombra un
/// nivel — no se devuelve el gris. Ver <see cref="Fondo"/>.
///
/// LA ESCALA DE SOMBRA TIENE TRES NIVELES Y NO ES DECORACIÓN: dice a qué distancia está cada cosa.
/// La ventana flota sobre el escritorio (nivel 3), las tarjetas sobre la ventana (nivel 2) y los
/// botones apenas se despegan (nivel 1). Al pasar el ratón un botón SUBE —más desenfoque, más
/// desplazamiento— y al pulsarlo baja. Es la única animación de la interfaz y cuenta lo que hace
/// falta: esto se puede tocar.
///
/// EL CONTRASTE ESTÁ MEDIDO, no elegido a ojo. Sobre blanco: <see cref="Tinta"/> 17:1,
/// <see cref="TintaMedia"/> 5,9:1 y <see cref="TintaTenue"/> 4,2:1 — las tres por encima del 4,5:1
/// que pide WCAG AA para texto normal (la tenue se reserva a rótulos en negrita). El azul y el rojo
/// se OSCURECIERON respecto de la versión oscura por esto mismo: el #4C8DFF de antes daba 2,8:1 con
/// texto blanco encima, que es ilegible para bastante gente y se ve mal para todas.
/// </remarks>
public static class Estudio
{
    // ── color ────────────────────────────────────────────────────────────────

    /// <summary>El lienzo. Blanco: lo elevado se separa por su sombra, no por un escalón de color.</summary>
    // BLANCO DESDE EL 2026-09-06, y es un cambio de doctrina pedido por el dueño: «prefiero que
    // todo sea muy blanco, la diferenciación se hace a través de las sombras».
    //
    // Lo que decía antes esta regla —y se conserva escrito porque el argumento sigue siendo
    // cierto— es que un lienzo #FFFFFF deja invisible a una tarjeta blanca encima, y solo queda la
    // sombra flotando sin objeto. La decisión asume ese coste a cambio de una interfaz sin grises:
    // lo elevado se lee por su SOMBRA y su filete, que es lo que ya hacía el trabajo pesado. Si en
    // alguna pantalla una tarjeta deja de distinguirse, el arreglo es subirle la sombra un nivel,
    // no devolver el gris a hurtadillas.
    public static readonly Brush Fondo = Congelado(0xFF, 0xFF, 0xFF);

    /// <summary>Lo ELEVADO: tarjetas, botones, la pestaña activa. Blanco de verdad.</summary>
    public static readonly Brush Superficie = Congelado(0xFF, 0xFF, 0xFF);

    /// <summary>Un escalón por debajo de lo elevado: rellenos suaves, el carril del segmentado.</summary>
    public static readonly Brush SuperficieSuave = Congelado(0xE4, 0xE8, 0xEF);

    /// <summary>Texto principal. Casi negro con una gota de azul, para que no sea un negro plano.</summary>
    public static readonly Brush Tinta = Congelado(0x0F, 0x15, 0x24);

    /// <summary>Texto secundario.</summary>
    public static readonly Brush TintaMedia = Congelado(0x5A, 0x64, 0x78);

    /// <summary>Rótulos y metadatos. El más claro que sigue siendo legible.</summary>
    public static readonly Brush TintaTenue = Congelado(0x7C, 0x86, 0x97);

    /// <summary>Filetes de un píxel. Casi no se ven, y esa es la idea.</summary>
    public static readonly Brush Borde = Congelado(0xE3, 0xE7, 0xEE);

    /// <summary>
    /// El azul de Miracle sobre superficie clara. Oscurecido para dar 4,6:1 con texto blanco encima.
    /// </summary>
    /// <remarks>
    /// NO ES UN AZUL NUEVO: es <see cref="UiPalette.Trabajando"/> (#3B82F6) llevado a un fondo
    /// claro. La regla de UiPalette manda —«ningún color puede parecerse a otro con distinto
    /// significado»— y aquí no hay significado nuevo: el mismo azul, la luminosidad que exige el
    /// contraste sobre blanco. Si algún día cambia el azul de la marca, cambian LOS DOS.
    /// </remarks>
    public static readonly Brush Acento = Congelado(0x2E, 0x6B, 0xE6);

    /// <summary>El azul en su versión de fondo, para chips y estados.</summary>
    public static readonly Brush AcentoSuave = Congelado(0xEA, 0xF1, 0xFE);

    /// <summary>
    /// Rojo de grabación y de fallo sobre superficie clara. 5,1:1 con blanco encima.
    /// </summary>
    /// <remarks>
    /// Mismo caso que el azul: es <see cref="UiPalette.Fallo"/> (#FF3B30) sobre claro, no un rojo
    /// aparte. El de UiPalette da 3,5:1 sobre blanco —por debajo de AA para texto pequeño— porque
    /// nació para pintar la carita sobre fondo oscuro. La colisión deliberada que documenta
    /// UiPalette (rojo = «se rompió» y «te estoy grabando») se hereda tal cual: aquí también se
    /// distinguen por lo que las acompaña, nunca por el color a secas.
    /// </remarks>
    public static readonly Brush Alerta = Congelado(0xD3, 0x2F, 0x45);

    public static readonly Brush AlertaSuave = Congelado(0xFD, 0xEC, 0xEF);

    /// <summary>
    /// Verde de «esto está entregando», sobre superficie clara. 4,6:1 con blanco encima.
    /// </summary>
    /// <remarks>
    /// Entra con el selector de micrófono (spec 005) y no antes, porque hasta ahora no había nada
    /// que decir en verde: los estados de esta paleta eran «normal», «te estoy grabando» (Alerta) y
    /// «esto es accionable» (Acento).
    ///
    /// NO es el #6ED88B de la carita, y la diferencia no es un descuido: aquel verde nació para un
    /// punto de 4,8 px sobre fondo oscuro y sobre blanco da 1,8:1, ilegible. Mismo significado, la
    /// luminosidad que exige el contraste sobre claro — la misma regla que ya siguen Acento y
    /// Alerta respecto de UiPalette.
    ///
    /// Lo que este verde promete es ESTRECHO: no «conectado», sino «llegó audio hace poco». La
    /// diferencia costó 56 minutos de demo el 2026-08-25 y vive en <c>Omi.Vigia</c>.
    /// </remarks>
    public static readonly Brush Ok = Congelado(0x1B, 0x8A, 0x5A);

    /// <summary>El verde en su versión de fondo, para chips y estados.</summary>
    public static readonly Brush OkSuave = Congelado(0xE4, 0xF4, 0xEC);

    /// <summary>
    /// Ámbar de «está en pie pero todavía no entrega»: enlazado esperando, o conectado y mudo.
    /// </summary>
    /// <remarks>
    /// Existe para que el indicador no tenga que elegir entre mentir en verde y alarmar en rojo.
    /// El collar recordado que aún no aparece no es un fallo — es el estado normal de los primeros
    /// segundos — y pintarlo de rojo enseñaría a ignorar el rojo.
    /// </remarks>
    public static readonly Brush Espera = Congelado(0xB4, 0x6A, 0x0C);

    /// <summary>El ámbar en su versión de fondo.</summary>
    public static readonly Brush EsperaSuave = Congelado(0xFB, 0xF0, 0xDE);

    // ── la rampa FLOTANTE ────────────────────────────────────────────────────
    //
    // LA CARITA Y SU PANEL SE QUEDAN OSCUROS, y no es una excepción al sistema: es una superficie
    // distinta del sistema. Un sistema de diseño no es un color, es un conjunto de reglas —radios,
    // escala de sombra, jerarquía de texto, semántica del color— y esas se comparten enteras. Lo
    // que cambia es el suelo, porque el trabajo es otro:
    //
    //   · La ventana de consulta es un DOCUMENTO que el médico mira. Vive sola en su rectángulo y
    //     puede permitirse el papel blanco.
    //   · La carita es una HERRAMIENTA que flota ENCIMA de lo que el médico está mirando —SAP,
    //     Chrome, Excel—, con Topmost y sin barra de tareas. Sobre el azul claro de SAP, un panel
    //     blanco se confunde con la aplicación de debajo; el fondo oscuro es justo lo que lo hace
    //     leerse como «capa del sistema» y no como contenido.
    //
    // Y hay una razón medida además de la estética: los colores de estado de UiPalette están
    // calibrados para fondo oscuro. El ámbar de «atención» (#FFA51F) da 1,9:1 sobre blanco — no es
    // que se lea mal, es que no se ve. Cambiar el suelo obligaría a re-inventar los cuatro colores
    // que UiPalette existe precisamente para que no se re-inventen.

    /// <summary>El suelo de un panel flotante. Casi opaco: deja intuir lo de debajo sin competir.</summary>
    public static readonly Brush FondoFlotante = CongeladoAlfa(0xF2, 0x0F, 0x13, 0x1C);

    /// <summary>Lo elevado sobre un panel flotante: campos, botones activos.</summary>
    public static readonly Brush SuperficieFlotante = CongeladoAlfa(0x1A, 0xFF, 0xFF, 0xFF);

    /// <summary>Texto principal sobre suelo flotante.</summary>
    public static readonly Brush TintaFlotante = CongeladoAlfa(0xF2, 0xEA, 0xF2, 0xFF);

    /// <summary>Texto secundario sobre suelo flotante.</summary>
    public static readonly Brush TintaFlotanteMedia = CongeladoAlfa(0x99, 0xEA, 0xF2, 0xFF);

    /// <summary>El filete de un panel flotante: lo que lo despega de lo que hay detrás.</summary>
    public static readonly Brush BordeFlotante = CongeladoAlfa(0x38, 0xFF, 0xFF, 0xFF);

    // ── radios ───────────────────────────────────────────────────────────────
    //
    // Una escala corta y compartida. Antes había 4, 6, 7, 9, 10, 14 y 18 repartidos por el XAML sin
    // criterio: siete radios son siete decisiones que nadie tomó.

    /// <summary>Lo pequeño: chips, botones de icono.</summary>
    public const double RadioChico = 10;

    /// <summary>Lo mediano: botones de texto, campos.</summary>
    public const double RadioMedio = 14;

    /// <summary>Un panel.</summary>
    public const double RadioPanel = 20;

    // ── sombra ───────────────────────────────────────────────────────────────

    /// <summary>Nivel 1: apenas despegado. Botones y pestañas en reposo.</summary>
    public static readonly DropShadowEffect Sombra1 = Sombra(14, 3, 0.10);

    /// <summary>Nivel 2: una tarjeta sobre la ventana.</summary>
    public static readonly DropShadowEffect Sombra2 = Sombra(22, 5, 0.09);

    /// <summary>Nivel 3: la ventana sobre el escritorio. La más marcada de las tres.</summary>
    public static readonly DropShadowEffect Sombra3 = Sombra(48, 12, 0.18);

    /// <summary>Lo que pasa al acercar el ratón: el objeto sube.</summary>
    public static readonly DropShadowEffect SombraHover = Sombra(22, 7, 0.16);

    /// <summary>Y al pulsarlo, baja hasta casi tocar la superficie.</summary>
    public static readonly DropShadowEffect SombraPulsada = Sombra(7, 1, 0.12);

    /// <summary>
    /// La sombra cae siempre HACIA ABAJO (dirección 270). Una luz que viene de arriba es la que
    /// todo el mundo tiene en la cabeza, y en cuanto una sombra contradice a otra el relieve deja
    /// de leerse como relieve y pasa a leerse como suciedad.
    /// </summary>
    private static DropShadowEffect Sombra(double desenfoque, double profundidad, double opacidad)
    {
        var s = new DropShadowEffect
        {
            BlurRadius = desenfoque,
            ShadowDepth = profundidad,
            Direction = 270,
            Opacity = opacidad,
            // Gris azulado y no negro: el negro puro sobre un fondo frío ensucia y hace ver gris
            // sucio donde debería haber profundidad.
            Color = Color.FromRgb(0x1B, 0x24, 0x37),
            RenderingBias = RenderingBias.Quality,
        };
        s.Freeze();
        return s;
    }

    // ── piezas ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Una superficie elevada: fondo blanco y filete. SIN sombra — la pone <see cref="Elevar"/>.
    /// </summary>
    public static Border Tarjeta(double radio = 18) => new()
    {
        CornerRadius = new CornerRadius(radio),
        Background = Superficie,
        BorderBrush = Borde,
        BorderThickness = new Thickness(1),
    };

    /// <summary>
    /// Pone la tarjeta sobre una PLACA que echa la sombra, y devuelve las dos juntas.
    /// </summary>
    /// <remarks>
    /// LA SOMBRA NUNCA VA EN EL ELEMENTO QUE TIENE EL CONTENIDO, y esto costó una ronda entera
    /// (2026-09-01: «no sé por qué la interfaz se ve borrosa, no se ve de alta calidad»).
    ///
    /// Un <c>Effect</c> es un shader: WPF rasteriza TODO el subárbol que cuelga de ese elemento a
    /// una textura intermedia y después le aplica el filtro. Dentro de esa textura el texto pierde
    /// ClearType —el suavizado por subpíxel— y cae a suavizado gris, que se ve fino, lavado y
    /// blando. Con la sombra puesta en el borde que envolvía la ventana entera, ESO le pasaba a
    /// cada letra de la aplicación.
    ///
    /// La placa es un borde gemelo, del mismo tamaño y color, sin un solo hijo: echa exactamente la
    /// misma sombra y se queda escondida detrás. El contenido va delante, hermano suyo, y se pinta
    /// directo a la pantalla con todo su ClearType.
    /// </remarks>
    /// <summary>
    /// EL HUECO QUE UNA SOMBRA NECESITA PARA DIBUJARSE ENTERA.
    /// </summary>
    /// <remarks>
    /// POR QUÉ HACE FALTA (2026-09-06, lo vio el dueño: «hay unas sombras que se ven cortadas… no
    /// solo aquí, en varios lugares»). WPF no recorta a un hijo que se sale de su sitio, así que
    /// dentro de una ventana la sombra asoma sin problema. Lo que SÍ corta es el borde de la
    /// VENTANA: las de esta aplicación son <c>SizeToContent</c> sobre <c>AllowsTransparency</c>, o
    /// sea que el HWND mide exactamente lo que mide el contenido — y el desenfoque, que vive fuera
    /// de ese contenido, se queda al otro lado del cristal. Igual con un <c>Popup</c>, que es una
    /// ventana con otro nombre.
    ///
    /// El síntoma engaña porque no parece un recorte: parece un borde duro, o una sombra más
    /// «plana» de un lado. Es la misma familia que «una caja que miente» (aprendizaje nº4): el
    /// dibujo dice una profundidad que no es.
    ///
    /// EL REPARTO NO ES SIMÉTRICO, y por la misma razón que la sombra existe. La luz de este
    /// estudio viene de arriba (<see cref="Sombra"/> fija la dirección en 270), así que la mancha
    /// cae hacia abajo desplazada <c>ShadowDepth</c>: por abajo hace falta el desenfoque MÁS esa
    /// caída, y por los otros tres lados solo el desenfoque.
    ///
    /// Ya había un sitio que resolvía esto a mano y sabía por qué —el <c>Margin</c> de 28 de la
    /// carita suelta, con su comentario: «deja aire alrededor para que la sombra (blur 24) no se
    /// recorte»—. Esto es esa misma cuenta, dicha una vez para todos.
    /// </remarks>
    public static Thickness HolguraDe(DropShadowEffect? sombra)
    {
        if (sombra == null) return new Thickness(0);

        // BlurRadius es el diámetro del desenfoque; se sale la mitad por cada lado. Se redondea
        // hacia arriba: quedarse corto por medio píxel devuelve el recorte que esto viene a quitar.
        double lados = Math.Ceiling(sombra.BlurRadius / 2);
        double caida = Math.Ceiling(Math.Abs(sombra.ShadowDepth));
        return new Thickness(lados, lados, lados, lados + caida);
    }

    public static Grid Elevar(Border tarjeta, DropShadowEffect? sombra = null)
    {
        var laSombra = sombra ?? Sombra2;

        // El margen se muda al contenedor: si se quedara en la tarjeta, la placa iría desplazada y
        // la sombra asomaría por un lado.
        //
        // Y SE LE SUMA LA HOLGURA DE LA SOMBRA, que es lo que faltaba: sin ella, lo elevado mide
        // exactamente lo que mide la tarjeta, y una ventana o un popup que se ajustan al contenido
        // dejan el desenfoque fuera del cristal. Se SUMA en vez de sustituir para no comerse el
        // margen que cada pantalla ya pedía.
        var holgura = HolguraDe(laSombra);
        var caja = new Grid
        {
            Margin = new Thickness(
                tarjeta.Margin.Left + holgura.Left,
                tarjeta.Margin.Top + holgura.Top,
                tarjeta.Margin.Right + holgura.Right,
                tarjeta.Margin.Bottom + holgura.Bottom),
        };
        tarjeta.Margin = new Thickness(0);

        caja.Children.Add(new Border
        {
            CornerRadius = tarjeta.CornerRadius,
            Background = tarjeta.Background,
            Effect = laSombra,
        });
        caja.Children.Add(tarjeta);
        return caja;
    }

    /// <summary>
    /// La plantilla de un botón: una píldora del color que le pongan, sin borde ni el relieve del
    /// sistema. El relieve lo pone la sombra, que es la regla de este estudio.
    /// </summary>
    /// <remarks>
    /// MISMA REGLA QUE <see cref="Elevar"/>: el fondo del botón echa la sombra y la etiqueta va
    /// DELANTE, hermana suya, no dentro. Si el texto colgara del elemento con el <c>Effect</c>,
    /// cada botón de la aplicación tendría las letras lavadas.
    ///
    /// La sombra viaja por <c>Tag</c> porque tiene que poder cambiar en caliente —sube con el ratón
    /// encima, baja al pulsar— y una plantilla no admite un valor distinto por instancia de otra
    /// forma. Lo pone <see cref="ConRelieve"/>; a mano no se toca.
    /// </remarks>
    /// <param name="estirado">
    /// El contenido ocupa TODO el ancho del botón en vez de quedarse centrado.
    ///
    /// Existe desde el 2026-09-06 y por un fallo que se veía: las tres opciones del selector de
    /// micrófono llevan una rejilla —columna fija para el icono, columna libre para el texto— para
    /// que las tres etiquetas empiecen en la misma x. Con el contenido centrado, esa rejilla se
    /// encogía a su tamaño natural y se centraba entera, así que cada fila arrancaba en un sitio
    /// distinto según lo ancho que fuera su glifo: exactamente lo que la rejilla existía para
    /// impedir. Por defecto sigue centrado, que es lo que quieren los botones de una sola palabra.
    /// </param>
    public static ControlTemplate Pastilla(double radio, bool estirado = false)
    {
        var plantilla = new ControlTemplate(typeof(ButtonBase));

        var caja = new FrameworkElementFactory(typeof(Grid));

        var fondo = new FrameworkElementFactory(typeof(Border));
        fondo.SetValue(Border.CornerRadiusProperty, new CornerRadius(radio));
        fondo.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        fondo.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        fondo.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        fondo.SetValue(UIElement.EffectProperty, new TemplateBindingExtension(FrameworkElement.TagProperty));
        caja.AppendChild(fondo);

        var contenido = new FrameworkElementFactory(typeof(ContentPresenter));
        contenido.SetValue(FrameworkElement.HorizontalAlignmentProperty,
            estirado ? HorizontalAlignment.Stretch : HorizontalAlignment.Center);
        contenido.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        caja.AppendChild(contenido);

        plantilla.VisualTree = caja;
        return plantilla;
    }

    /// <summary>
    /// Le da vida al botón: sube con el ratón encima y baja al pulsarlo. Es la única animación de
    /// la interfaz, y existe porque en un diseño donde todo es del mismo color la sombra es lo
    /// ÚNICO que dice «esto se puede tocar».
    /// </summary>
    public static T ConRelieve<T>(this T boton, DropShadowEffect? reposo = null) where T : ButtonBase
    {
        // Tag y no Effect: la sombra la lleva el FONDO de la plantilla, para que la etiqueta no
        // caiga dentro del shader y se vea lavada. Ver Pastilla y Elevar.
        var enReposo = reposo ?? Sombra1;
        boton.Tag = enReposo;
        boton.MouseEnter += (_, _) => { if (boton.IsEnabled) boton.Tag = SombraHover; };
        boton.MouseLeave += (_, _) => boton.Tag = enReposo;
        boton.PreviewMouseLeftButtonDown += (_, _) => boton.Tag = SombraPulsada;
        boton.PreviewMouseLeftButtonUp += (_, _) => boton.Tag = boton.IsMouseOver ? SombraHover : enReposo;
        boton.IsEnabledChanged += (_, _) => boton.Opacity = boton.IsEnabled ? 1.0 : 0.45;
        return boton;
    }

    /// <summary>
    /// Deja una ventana lista para verse nítida: sin medios píxeles, con el texto ajustado a la
    /// rejilla y con ClearType pedido explícitamente.
    /// </summary>
    /// <remarks>
    /// Las tres hacen falta y ninguna sustituye a las otras:
    ///
    ///   · <c>UseLayoutRounding</c> — sin él, un borde que cae en x=100,5 se pinta repartido entre
    ///     dos columnas de píxeles y se ve como una raya gris en vez de una línea.
    ///   · <c>TextFormattingMode.Display</c> — ajusta los glifos a la rejilla de píxeles. A los
    ///     tamaños de interfaz (12-15 px) es la diferencia entre una letra nítida y una borrosa;
    ///     el modo <c>Ideal</c>, que es el de fábrica, está pensado para texto grande e impreso.
    ///   · <c>ClearTypeHint.Enabled</c> — vuelve a encender el suavizado por subpíxel dentro del
    ///     subárbol. Hace falta porque <c>AllowsTransparency</c> lo apaga: una ventana por capas
    ///     no puede saber qué hay debajo, así que WPF se cae a suavizado gris por si acaso. Con la
    ///     pista puesta —y con el fondo opaco que estas tarjetas tienen— se puede volver a usar.
    /// </remarks>
    public static void Nitida(this Window ventana)
    {
        ventana.UseLayoutRounding = true;
        ventana.SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(ventana, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(ventana, TextRenderingMode.ClearType);
        RenderOptions.SetClearTypeHint(ventana, ClearTypeHint.Enabled);
    }

    // ── la barra de scroll ───────────────────────────────────────────────────

    /// <summary>
    /// La barra de desplazamiento, rehecha. Un pulgar redondeado y nada más: sin flechas, sin
    /// carril, sin el borde gris de Windows.
    /// </summary>
    /// <remarks>
    /// POR QUÉ ESTÁ EN XAML Y NO EN CÓDIGO, que es como se construye el resto de estas ventanas: la
    /// plantilla de un <c>ScrollBar</c> necesita un <c>Track</c> con sus dos <c>RepeatButton</c> y
    /// un <c>Thumb</c> con plantilla propia, y eso en <c>FrameworkElementFactory</c> son sesenta
    /// líneas que no hay quien lea ni corrija. Aquí el XAML se lee de un vistazo, que es justo lo
    /// que se le pide a la definición de un estilo.
    ///
    /// Los <c>RepeatButton</c> siguen ahí con opacidad cero, no borrados: son los que hacen que
    /// pulsar el carril avance una página. Quitarlos habría cambiado el comportamiento por pintar.
    /// </remarks>
    public static Style BarraDeScroll(bool sobreOscuro = false)
    {
        // El pulgar tiene que contrastar con SU suelo, no con uno imaginario: gris sobre claro,
        // blanco translúcido sobre el panel flotante. Es el mismo pulgar con el mismo gesto.
        string reposo = sobreOscuro ? "#59FFFFFF" : "#C3CAD6";
        string encima = sobreOscuro ? "#8CFFFFFF" : "#98A2B3";
        string arrastrando = sobreOscuro ? "#BFFFFFFF" : "#7C8697";

        string xaml = """
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                   xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                   TargetType="{x:Type ScrollBar}">
              <Setter Property="Width" Value="11"/>
              <Setter Property="MinWidth" Value="11"/>
              <Setter Property="Background" Value="Transparent"/>
              <Setter Property="Template">
                <Setter.Value>
                  <ControlTemplate TargetType="{x:Type ScrollBar}">
                    <Grid Background="Transparent">
                      <Track x:Name="PART_Track" IsDirectionReversed="True">
                        <Track.DecreaseRepeatButton>
                          <RepeatButton Command="ScrollBar.PageUpCommand" Opacity="0"
                                        Focusable="False" IsTabStop="False"/>
                        </Track.DecreaseRepeatButton>
                        <Track.IncreaseRepeatButton>
                          <RepeatButton Command="ScrollBar.PageDownCommand" Opacity="0"
                                        Focusable="False" IsTabStop="False"/>
                        </Track.IncreaseRepeatButton>
                        <Track.Thumb>
                          <Thumb x:Name="Pulgar" Focusable="False" IsTabStop="False">
                            <Thumb.Template>
                              <ControlTemplate TargetType="{x:Type Thumb}">
                                <Border x:Name="Cuerpo" Width="5" CornerRadius="3"
                                        HorizontalAlignment="Center"
                                        Background="{REPOSO}" Opacity="0.9"/>
                                <ControlTemplate.Triggers>
                                  <Trigger Property="IsMouseOver" Value="True">
                                    <Setter TargetName="Cuerpo" Property="Width" Value="7"/>
                                    <Setter TargetName="Cuerpo" Property="Background" Value="{ENCIMA}"/>
                                  </Trigger>
                                  <Trigger Property="IsDragging" Value="True">
                                    <Setter TargetName="Cuerpo" Property="Width" Value="7"/>
                                    <Setter TargetName="Cuerpo" Property="Background" Value="{ARRASTRANDO}"/>
                                  </Trigger>
                                </ControlTemplate.Triggers>
                              </ControlTemplate>
                            </Thumb.Template>
                          </Thumb>
                        </Track.Thumb>
                      </Track>
                    </Grid>
                  </ControlTemplate>
                </Setter.Value>
              </Setter>
            </Style>
            """;
        xaml = xaml.Replace("{REPOSO}", reposo)
                   .Replace("{ENCIMA}", encima)
                   .Replace("{ARRASTRANDO}", arrastrando);
        return (Style)XamlReader.Parse(xaml);
    }

    /// <summary>Deja la barra rehecha puesta en todo lo que cuelgue de esta ventana.</summary>
    public static void PonerLaBarraDeScroll(this FrameworkElement raiz, bool sobreOscuro = false) =>
        raiz.Resources.Add(typeof(ScrollBar), BarraDeScroll(sobreOscuro));

    // ── texto ────────────────────────────────────────────────────────────────

    /// <summary>Un rótulo pequeño en versalitas: encabeza secciones sin robarles protagonismo.</summary>
    public static TextBlock Rotulo(string texto) => new()
    {
        Text = texto.ToUpperInvariant(),
        Foreground = TintaTenue,
        FontSize = 10,
        FontWeight = FontWeights.Bold,
        Margin = new Thickness(0, 0, 0, 7),
    };

    /// <summary>Texto de lectura, con el interlineado holgado que pide un párrafo clínico.</summary>
    public static TextBlock Parrafo(string texto, double tamano = 13.5) => new()
    {
        Text = texto,
        Foreground = Tinta,
        FontSize = tamano,
        LineHeight = tamano * 1.55,
        TextWrapping = TextWrapping.Wrap,
    };

    private static SolidColorBrush Congelado(byte r, byte g, byte b)
    {
        var brocha = new SolidColorBrush(Color.FromRgb(r, g, b));
        brocha.Freeze();
        return brocha;
    }

    private static SolidColorBrush CongeladoAlfa(byte a, byte r, byte g, byte b)
    {
        var brocha = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brocha.Freeze();
        return brocha;
    }
}
