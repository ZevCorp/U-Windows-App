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
/// LOS VALORES SON LOS DE MIRACLE DESDE EL 2026-09-26 (spec 054). Cada color sale de <see cref="Marca"/>,
/// que los copia de la web con los blancos de U y un azul un punto más claro: «que el Notes de Windows
/// y el de la web se entiendan como el mismo». Aquí ya no se escribe un hexadecimal — se escribe en
/// Marca, que el contrato juzga sin pantalla (446), y la promesa 447 comprueba que esto la obedece.
/// Lo que sigue siendo de U y no de la web es la SOMBRA: tres niveles, luz de arriba, la placa
/// aparte. Es la base de U que el dueño pidió conservar.
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
    public static readonly Brush Fondo = Desde(Marca.Fondo);

    /// <summary>Lo ELEVADO: tarjetas, botones, la pestaña activa. Blanco de verdad.</summary>
    public static readonly Brush Superficie = Desde(Marca.Superficie);

    /// <summary>Un escalón por debajo de lo elevado: rellenos suaves, el carril del segmentado.</summary>
    public static readonly Brush SuperficieSuave = Desde(Marca.SuperficieSuave);

    /// <summary>Texto principal. Casi negro con una gota de azul, para que no sea un negro plano.</summary>
    public static readonly Brush Tinta = Desde(Marca.Tinta);

    /// <summary>Texto secundario.</summary>
    public static readonly Brush TintaMedia = Desde(Marca.TintaMedia);

    /// <summary>Rótulos y metadatos. El más claro que sigue siendo legible.</summary>
    public static readonly Brush TintaTenue = Desde(Marca.TintaTenue);

    /// <summary>Filetes de un píxel. Casi no se ven, y esa es la idea.</summary>
    public static readonly Brush Borde = Desde(Marca.Linea);

    /// <summary>
    /// El suelo de la BARRA de Ü: blanco con una gota de azul frío, no blanco puro.
    /// </summary>
    /// <remarks>
    /// ES LA EXCEPCIÓN QUE EL DUEÑO PIDIÓ, y conviene que esté escrita porque contradice de frente
    /// la doctrina de arriba («prefiero que todo sea muy blanco, la diferenciación se hace a través
    /// de las sombras»). Lo que cambió no es el gusto: es DÓNDE vive esta superficie. Una ventana
    /// de la aplicación flota sobre su propio lienzo, que controlamos; la barra flota sobre el
    /// escritorio de otro —un Word, un Excel, un navegador con fondo blanco— y ahí el blanco puro
    /// no se despega de nada. La sombra sola no llega cuando lo de debajo es del mismo color.
    ///
    /// «LIGERAMENTE más notorio» (2026-09-14), y el adverbio manda: el salto es de tres puntos de
    /// luminosidad. Lo que hace el trabajo de verdad es <see cref="BordeDeLaBarra"/>; esto solo
    /// impide que la pieza se funda con un fondo blanco.
    /// </remarks>
    public static readonly Brush SuperficieDeLaBarra = Desde(Marca.SuperficieDeLaBarra);

    /// <summary>
    /// El filete de la barra: el de <see cref="Borde"/> subido hasta que se vea sobre cualquier cosa.
    /// </summary>
    /// <remarks>
    /// El #E3E7EE de la casa está calibrado para separar dos superficies NUESTRAS, las dos claras y
    /// las dos conocidas. Sobre lo que hay detrás de una capa flotante —que puede ser blanco, gris o
    /// una foto— ese filete desaparece y la barra se queda sin contorno: es lo que el dueño está
    /// viendo cuando dice que no se nota. Este gris azulado sigue siendo un hilo y no un marco.
    /// </remarks>
    public static readonly Brush BordeDeLaBarra = Congelado(0xC6, 0xD0, 0xE2);

    /// <summary>
    /// El azul de Miracle sobre superficie clara. Oscurecido para dar 4,6:1 con texto blanco encima.
    /// </summary>
    /// <remarks>
    /// NO ES UN AZUL NUEVO: es <see cref="UiPalette.Trabajando"/> (#3B82F6) llevado a un fondo
    /// claro. La regla de UiPalette manda —«ningún color puede parecerse a otro con distinto
    /// significado»— y aquí no hay significado nuevo: el mismo azul, la luminosidad que exige el
    /// contraste sobre blanco. Si algún día cambia el azul de la marca, cambian LOS DOS.
    /// </remarks>
    public static readonly Brush Acento = Desde(Marca.Acento);

    /// <summary>El azul en su versión de fondo, para chips y estados.</summary>
    public static readonly Brush AcentoSuave = Desde(Marca.AcentoSuave);

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
    public static readonly Brush Alerta = Desde(Marca.Alerta);

    public static readonly Brush AlertaSuave = Desde(Marca.AlertaSuave);

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
    public static readonly Brush Ok = Desde(Marca.Ok);

    /// <summary>El verde en su versión de fondo, para chips y estados.</summary>
    public static readonly Brush OkSuave = Desde(Marca.OkSuave);

    /// <summary>
    /// Ámbar de «está en pie pero todavía no entrega»: enlazado esperando, o conectado y mudo.
    /// </summary>
    /// <remarks>
    /// Existe para que el indicador no tenga que elegir entre mentir en verde y alarmar en rojo.
    /// El collar recordado que aún no aparece no es un fallo — es el estado normal de los primeros
    /// segundos — y pintarlo de rojo enseñaría a ignorar el rojo.
    /// </remarks>
    public static readonly Brush Espera = Desde(Marca.Espera);

    /// <summary>El ámbar en su versión de fondo.</summary>
    public static readonly Brush EsperaSuave = Desde(Marca.EsperaSuave);

    // ── lo que trae la web (spec 054) ────────────────────────────────────────

    /// <summary>Títulos y lo que tiene que pesar (deep de la web).</summary>
    public static readonly Brush TintaFuerte = Desde(Marca.TintaFuerte);

    /// <summary>Texto de apoyo con peso (ink-soft).</summary>
    public static readonly Brush TintaSuave = Desde(Marca.TintaSuave);

    /// <summary>El filete que tiene que verse: bordes de campos y botones secundarios.</summary>
    public static readonly Brush BordeFuerte = Desde(Marca.LineaFuerte);

    /// <summary>Gris neutro: la pista de lo vacío, el borde secundario bajo el ratón.</summary>
    public static readonly Brush Niebla = Desde(Marca.Niebla);

    /// <summary>Superficie azulada suave.</summary>
    public static readonly Brush Hielo = Desde(Marca.Hielo);

    /// <summary>El fondo de lo que está bajo el ratón.</summary>
    public static readonly Brush HieloSuave = Desde(Marca.HieloSuave);

    /// <summary>El azul de un enlace o icono bajo el ratón.</summary>
    public static readonly Brush AcentoEncima = Desde(Marca.AcentoEncima);

    /// <summary>Texto azul sobre <see cref="AcentoSuave"/>.</summary>
    public static readonly Brush AcentoTinta = Desde(Marca.AcentoTinta);

    public static readonly Brush OkTinta = Desde(Marca.OkTinta);
    public static readonly Brush EsperaTinta = Desde(Marca.EsperaTinta);
    public static readonly Brush AlertaTinta = Desde(Marca.AlertaTinta);

    /// <summary>El filete de la nota, cálido: lo que queda del papel de la web sobre el blanco de U.</summary>
    public static readonly Brush DocLinea = Desde(Marca.DocLinea);

    /// <summary>El filete entre secciones de la nota.</summary>
    public static readonly Brush DocLineaSuave = Desde(Marca.DocLineaSuave);

    /// <summary>El cuerpo de la nota.</summary>
    public static readonly Brush DocTinta = Desde(Marca.DocTinta);

    /// <summary>El rótulo de cada sección de la nota.</summary>
    public static readonly Brush DocTenue = Desde(Marca.DocTenue);

    /// <summary>
    /// El fondo del botón primario: el degradado de arriba abajo de la web (<c>--grad-accent</c>),
    /// con el azul un punto más claro que pidió el dueño.
    /// </summary>
    public static readonly Brush AcentoDegradado = Degradado(Marca.AcentoArriba, Marca.AcentoAbajo);

    /// <summary>El mismo degradado al pasar el ratón: sube un punto de luz, como el hover de la web.</summary>
    public static readonly Brush AcentoDegradadoEncima = Degradado("#4584EE", "#3470DF");

    // ── letra (spec 054, promesa 445) ────────────────────────────────────────

    /// <summary>Inter: toda la interfaz.</summary>
    public static readonly FontFamily FuenteCuerpo = Familia(Marca.FuenteCuerpo);

    /// <summary>Schibsted Grotesk: títulos.</summary>
    public static readonly FontFamily FuenteTitulo = Familia(Marca.FuenteTitulo);

    /// <summary>Source Serif 4: el cuerpo de la nota.</summary>
    public static readonly FontFamily FuenteDocumento = Familia(Marca.FuenteDocumento);

    /// <summary>Geist Mono: el cronómetro y los números tabulares.</summary>
    public static readonly FontFamily FuenteMono = Familia(Marca.FuenteMono);

    /// <summary>
    /// Una familia pedida al ENSAMBLADO. La ruta base va aparte y la familia como «./#Nombre»: es la
    /// forma que WPF documenta para fuentes empaquetadas, y la lista de respaldo que sigue a la coma
    /// («, Segoe UI») se resuelve contra el sistema, que es lo que se quiere para los glifos sueltos.
    /// </summary>
    private static FontFamily Familia(string marca)
    {
        string resto = marca.Substring(Marca.RutaDeFuentes.Length);
        return new FontFamily(new Uri(Marca.RutaDeFuentes), "./" + resto);
    }

    // ── iconos (spec 054, promesa 449) ───────────────────────────────────────

    private static readonly Dictionary<string, Geometry> _geometrias = new();

    /// <summary>
    /// Un icono de Lucide, como lo pinta la web: trazo de 2 en su rejilla de 24, extremos y
    /// uniones redondos, sin relleno, escalado a <paramref name="tamano"/>.
    /// </summary>
    /// <remarks>
    /// EL GROSOR SE ESCALA CON EL ICONO, igual que en la web: un icono de 16 lleva un trazo de 1,33.
    /// Por eso el Path vive dentro de un Viewbox y no se le cambia el grosor a mano.
    /// Un nombre que no está en el catálogo es un error de programación y se dice por su nombre,
    /// no se pinta un hueco.
    /// </remarks>
    public static FrameworkElement Icono(string nombre, double tamano, Brush color, double grosor = 2,
                                         Brush? relleno = null)
    {
        if (!_geometrias.TryGetValue(nombre, out var geometria))
        {
            if (!Iconos.Trazos.TryGetValue(nombre, out var trazo))
                throw new ArgumentException($"no hay icono «{nombre}» en Iconos.Trazos", nameof(nombre));
            geometria = Geometry.Parse(trazo);
            geometria.Freeze();
            _geometrias[nombre] = geometria;
        }
        var path = new System.Windows.Shapes.Path
        {
            Data = geometria,
            Stroke = color,
            Fill = relleno,
            StrokeThickness = grosor,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
        var lienzo = new Canvas { Width = 24, Height = 24 };
        lienzo.Children.Add(path);
        return new Viewbox
        {
            Width = tamano,
            Height = tamano,
            Child = lienzo,
            SnapsToDevicePixels = true,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
    }

    /// <summary>Cambia el color de un icono ya pintado (el del micrófono cambia según entra voz o no).</summary>
    public static void Colorear(FrameworkElement icono, Brush color)
    {
        if (icono is Viewbox { Child: Canvas lienzo })
            foreach (var hijo in lienzo.Children)
                if (hijo is System.Windows.Shapes.Path p) p.Stroke = color;
    }

    // ── piezas de Miracle (spec 054) ─────────────────────────────────────────
    //
    // Los botones de la web (`.clinical-primary`, `-secondary`, `-tertiary`, `.icon-btn`) con el
    // relieve de U: la web los separa con un filete y un brillo; U, con la sombra que sube al pasar
    // el ratón. Se llevan las dos cosas.

    /// <summary>Un icono y un texto en fila, como el contenido de los botones de la web.</summary>
    public static StackPanel ConIcono(string? icono, string texto, Brush tinta, double tamano = 14,
                                      double tamanoIcono = 16, FontWeight? peso = null)
    {
        var fila = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        if (icono != null)
        {
            var i = Icono(icono, tamanoIcono, tinta);
            i.Margin = new Thickness(0, 0, texto.Length > 0 ? 8 : 0, 0);
            fila.Children.Add(i);
        }
        if (texto.Length > 0)
            fila.Children.Add(new TextBlock
            {
                Text = texto,
                Foreground = tinta,
                FontFamily = FuenteCuerpo,
                FontSize = tamano,
                FontWeight = peso ?? FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            });
        return fila;
    }

    /// <summary>Recolorea el icono y el texto del contenido de un botón hecho con <see cref="ConIcono"/>.</summary>
    public static void Tintar(object? contenido, Brush tinta)
    {
        if (contenido is not Panel fila) return;
        foreach (var hijo in fila.Children)
        {
            if (hijo is TextBlock t) t.Foreground = tinta;
            else if (hijo is FrameworkElement f) Colorear(f, tinta);
        }
    }

    /// <summary>
    /// El botón primario de Miracle: la píldora azul en degradado, con el texto blanco. Uno por
    /// pantalla: es «lo siguiente que hay que hacer».
    /// </summary>
    public static Button BotonPrimario(string texto, string? icono = null, double alto = 44)
    {
        var b = new Button
        {
            Content = ConIcono(icono, texto, Brushes.White),
            Height = alto,
            Padding = new Thickness(20, 0, 20, 0),
            MinWidth = 44,
            Background = AcentoDegradado,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Template = Pastilla(alto / 2),
        };
        b.MouseEnter += (_, _) => { if (b.IsEnabled) b.Background = AcentoDegradadoEncima; };
        b.MouseLeave += (_, _) => b.Background = AcentoDegradado;
        return b.ConRelieve(Sombra1);
    }

    /// <summary>El secundario: blanco con filete, tinta fuerte. Se aclara hacia el hielo al pasar el ratón.</summary>
    public static Button BotonSecundario(string texto, string? icono = null, double alto = 40, Brush? tinta = null)
    {
        var laTinta = tinta ?? TintaFuerte;
        var b = new Button
        {
            Content = ConIcono(icono, texto, laTinta, 13.5, 15),
            Height = alto,
            Padding = new Thickness(16, 0, 16, 0),
            Background = Superficie,
            BorderBrush = BordeFuerte,
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
            Template = Pastilla(alto / 2),
        };
        b.MouseEnter += (_, _) => { if (!b.IsEnabled) return; b.Background = HieloSuave; b.BorderBrush = Niebla; };
        b.MouseLeave += (_, _) => { b.Background = Superficie; b.BorderBrush = BordeFuerte; };
        return b.ConRelieve(Sombra1);
    }

    /// <summary>El terciario: solo texto azul. Para lo que no compite con la acción principal.</summary>
    public static Button BotonTerciario(string texto, string? icono = null, double alto = 34)
    {
        var b = new Button
        {
            Content = ConIcono(icono, texto, Acento, 13, 15),
            Height = alto,
            Padding = new Thickness(12, 0, 12, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Template = Pastilla(alto / 2),
        };
        b.MouseEnter += (_, _) => { if (!b.IsEnabled) return; b.Background = AcentoSuave; Tintar(b.Content, AcentoEncima); };
        b.MouseLeave += (_, _) => { b.Background = Brushes.Transparent; Tintar(b.Content, Acento); };
        return b;
    }

    /// <summary>
    /// Un botón de solo icono (`.icon-btn` de la web): redondo, callado en reposo, y se enciende
    /// en azul al acercarse — que es cuando importa.
    /// </summary>
    public static Button BotonIcono(string icono, string queHace, double lado = 32, double tamanoIcono = 16,
                                    Brush? tinta = null)
    {
        var reposo = tinta ?? TintaMedia;
        var dibujo = Icono(icono, tamanoIcono, reposo);
        var b = new Button
        {
            Content = dibujo,
            Width = lado,
            Height = lado,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Template = Pastilla(lado / 2),
            Focusable = false,
        };
        System.Windows.Automation.AutomationProperties.SetName(b, queHace);
        b.MouseEnter += (_, _) => { if (!b.IsEnabled) return; b.Background = HieloSuave; Colorear(dibujo, Acento); };
        b.MouseLeave += (_, _) => { b.Background = Brushes.Transparent; Colorear(dibujo, reposo); };
        return b;
    }

    /// <summary>Una ficha pequeña de texto sobre un fondo suave (el `Badge` de la web).</summary>
    public static Border Chip(string texto, Brush fondo, Brush tinta, bool conPunto = false)
    {
        var fila = new StackPanel { Orientation = Orientation.Horizontal };
        if (conPunto)
            fila.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 6, Height = 6, Fill = tinta, Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
        fila.Children.Add(new TextBlock
        {
            Text = texto, Foreground = tinta, FontFamily = FuenteCuerpo, FontSize = 11.5,
            FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
        });
        return new Border
        {
            CornerRadius = new CornerRadius(999),
            Background = fondo,
            Padding = new Thickness(10, 3, 10, 4),
            Child = fila,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    /// <summary>
    /// El estado de una consulta, con los colores y las palabras del portal (`StatusBadge` de la
    /// web): Borrador, Revisada, Aprobada, Exportada, En curso.
    /// </summary>
    public static Border ChipDeEstado(string estado)
    {
        var (etiqueta, tono) = Marca.EstadoDeConsulta(estado);
        var (fondo, tinta) = tono switch
        {
            "espera" => (EsperaSuave, EsperaTinta),
            "acento" => (AcentoSuave, AcentoTinta),
            "ok" => (OkSuave, OkTinta),
            _ => (SuperficieSuave, TintaSuave),
        };
        return Chip(etiqueta, fondo, tinta, conPunto: true);
    }

    /// <summary>
    /// Un aviso en banda (`AlertBanner` de la web): icono, título y cuerpo sobre el fondo suave del
    /// tono. Lo que el médico tiene que leer antes de seguir.
    /// </summary>
    public static Border Aviso(string tono, string titulo, string cuerpo = "")
    {
        var (fondo, tinta, borde, icono) = tono switch
        {
            "alerta" => (AlertaSuave, AlertaTinta, Alerta, "triangle-alert"),
            "ok" => (OkSuave, OkTinta, Ok, "circle-check"),
            "acento" => (AcentoSuave, AcentoTinta, Acento, "info"),
            _ => (EsperaSuave, EsperaTinta, Espera, "triangle-alert"),
        };
        var fila = new DockPanel();
        var i = Icono(icono, 17, tinta);
        i.VerticalAlignment = VerticalAlignment.Top;
        i.Margin = new Thickness(0, 1, 11, 0);
        DockPanel.SetDock(i, Dock.Left);
        fila.Children.Add(i);
        var textos = new StackPanel();
        textos.Children.Add(new TextBlock
        {
            Text = titulo, Foreground = tinta, FontFamily = FuenteCuerpo, FontSize = 13.5,
            FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
        });
        if (cuerpo.Length > 0)
            textos.Children.Add(new TextBlock
            {
                Text = cuerpo, Foreground = tinta, Opacity = 0.9, FontFamily = FuenteCuerpo, FontSize = 12.5,
                LineHeight = 18, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0),
            });
        fila.Children.Add(textos);
        var bordeSuave = new SolidColorBrush(((SolidColorBrush)borde).Color) { Opacity = 0.35 };
        bordeSuave.Freeze();
        return new Border
        {
            CornerRadius = new CornerRadius(RadioChico),
            Background = fondo,
            BorderBrush = bordeSuave,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 11, 14, 12),
            Child = fila,
        };
    }

    /// <summary>
    /// EL ORBE DE MIRACLE, redibujado del de la web (`components/brand/orb-art.tsx`): el halo celeste,
    /// el núcleo en degradado radial, el brillo arriba a la izquierda, el aro y la sonrisa.
    /// </summary>
    /// <remarks>
    /// Se dibuja en su rejilla de 100 y se escala: los números son los del SVG de la web, para que el
    /// día que se cambie allí se sepa qué cambiar aquí. Sin el desenfoque del aro: WPF lo haría con un
    /// efecto que come ClearType (ver <see cref="Elevar"/>), y a 24 px no se nota.
    /// </remarks>
    public static FrameworkElement Orbe(double tamano, bool conSonrisa = true)
    {
        var lienzo = new Canvas { Width = 100, Height = 100 };

        var halo = new RadialGradientBrush { GradientOrigin = new Point(0.5, 0.5), Center = new Point(0.5, 0.5), RadiusX = 0.5, RadiusY = 0.5 };
        halo.GradientStops.Add(new GradientStop(Color.FromArgb(0x73, 0xA6, 0xE4, 0xFF), 0));
        halo.GradientStops.Add(new GradientStop(Color.FromArgb(0x66, 0x96, 0xDF, 0xFF), 0.76));
        halo.GradientStops.Add(new GradientStop(Color.FromArgb(0x35, 0x88, 0xDA, 0xFF), 0.82));
        halo.GradientStops.Add(new GradientStop(Color.FromArgb(0x1A, 0x7E, 0xD4, 0xFF), 0.88));
        halo.GradientStops.Add(new GradientStop(Color.FromArgb(0x09, 0x7E, 0xD4, 0xFF), 0.94));
        halo.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0x7E, 0xD4, 0xFF), 1));
        halo.Freeze();
        lienzo.Children.Add(new System.Windows.Shapes.Ellipse { Width = 100, Height = 100, Fill = halo });

        var nucleo = new RadialGradientBrush { GradientOrigin = new Point(0.44, 0.40), Center = new Point(0.44, 0.40), RadiusX = 0.72, RadiusY = 0.72 };
        nucleo.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#6AD9FD"), 0));
        nucleo.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#42CCFD"), 0.34));
        nucleo.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#1CB4F4"), 0.62));
        nucleo.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#039CEB"), 0.86));
        nucleo.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#0F9AE6"), 1));
        nucleo.Freeze();
        var disco = new System.Windows.Shapes.Ellipse { Width = 76, Height = 76, Fill = nucleo };
        Canvas.SetLeft(disco, 12); Canvas.SetTop(disco, 12);
        lienzo.Children.Add(disco);

        var brillo = new RadialGradientBrush { GradientOrigin = new Point(0.34, 0.30), Center = new Point(0.34, 0.30), RadiusX = 0.38, RadiusY = 0.38 };
        brillo.GradientStops.Add(new GradientStop(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF), 0));
        brillo.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1));
        brillo.Freeze();
        var luz = new System.Windows.Shapes.Ellipse { Width = 76, Height = 76, Fill = brillo };
        Canvas.SetLeft(luz, 12); Canvas.SetTop(luz, 12);
        lienzo.Children.Add(luz);

        var aro = new RadialGradientBrush { GradientOrigin = new Point(0.26, 0.20), Center = new Point(0.26, 0.20), RadiusX = 0.96, RadiusY = 0.96 };
        aro.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), 0));
        aro.GradientStops.Add(new GradientStop(Color.FromArgb(0xD1, 0xFF, 0xFF, 0xFF), 0.45));
        aro.GradientStops.Add(new GradientStop(Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF), 1));
        aro.Freeze();
        var borde = new System.Windows.Shapes.Ellipse { Width = 76, Height = 76, Stroke = aro, StrokeThickness = Math.Max(1.2, 140 / tamano) };
        Canvas.SetLeft(borde, 12); Canvas.SetTop(borde, 12);
        lienzo.Children.Add(borde);

        if (conSonrisa && tamano >= 22)
            lienzo.Children.Add(new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M 44.4 55.8 q 5.6 6.2 11.2 0"),
                Stroke = new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF)),
                StrokeThickness = Math.Max(1.4, 120 / tamano) * 2.2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            });

        return new Viewbox { Width = tamano, Height = tamano, Child = lienzo, VerticalAlignment = VerticalAlignment.Center };
    }

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
    public const double RadioChico = Marca.RadioChico;

    /// <summary>Lo mediano: botones de texto, campos.</summary>
    public const double RadioMedio = Marca.RadioMedio;

    /// <summary>Un panel.</summary>
    public const double RadioPanel = Marca.RadioPanel;

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
    /// <remarks>
    /// Como el de la web desde el 2026-09-26: Inter 11, seminegrita, mayúsculas CON AIRE entre
    /// letras (<see cref="Marca.Rotulo"/>). Sin ese aire era lo primero que delataba otra app.
    /// </remarks>
    public static TextBlock Rotulo(string texto) => new()
    {
        Text = Marca.Rotulo(texto),
        Foreground = TintaTenue,
        FontFamily = FuenteCuerpo,
        FontSize = 11,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 0, 0, 7),
    };

    /// <summary>El rótulo de una sección de la NOTA: el mismo, en el gris tostado del documento.</summary>
    public static TextBlock RotuloDeDocumento(string texto)
    {
        var r = Rotulo(texto);
        r.Foreground = DocTenue;
        r.FontSize = 11.2;
        r.Margin = new Thickness(0);
        return r;
    }

    /// <summary>
    /// El cuerpo de la nota como en la web: Source Serif 4, 17 con interlineado 1,62, tinta del
    /// documento y cifras tabulares. Es lo que hace que la nota se lea como registro y no como control.
    /// </summary>
    public static TextBlock Documento(string texto)
    {
        var t = new TextBlock
        {
            Text = texto,
            Foreground = DocTinta,
            FontFamily = FuenteDocumento,
            FontSize = Marca.TamanoDocumento,
            LineHeight = Marca.TamanoDocumento * Marca.InterlineadoDocumento,
            TextWrapping = TextWrapping.Wrap,
        };
        System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        return t;
    }

    /// <summary>Un título de vista o de tarjeta: Schibsted Grotesk, seminegrita, la tinta fuerte.</summary>
    public static TextBlock Titulo(string texto, double tamano = 17) => new()
    {
        Text = texto,
        Foreground = TintaFuerte,
        FontFamily = FuenteTitulo,
        FontSize = tamano,
        FontWeight = FontWeights.SemiBold,
        TextWrapping = TextWrapping.Wrap,
    };

    /// <summary>Texto de lectura, con el interlineado holgado que pide un párrafo clínico.</summary>
    public static TextBlock Parrafo(string texto, double tamano = 13.5) => new()
    {
        Text = texto,
        Foreground = Tinta,
        FontFamily = FuenteCuerpo,
        FontSize = tamano,
        LineHeight = tamano * 1.55,
        TextWrapping = TextWrapping.Wrap,
    };

    /// <summary>Una brocha congelada desde un «#RRGGBB» de <see cref="Marca"/>.</summary>
    private static SolidColorBrush Desde(string hex)
    {
        var brocha = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brocha.Freeze();
        return brocha;
    }

    private static LinearGradientBrush Degradado(string arriba, string abajo)
    {
        var d = new LinearGradientBrush(
            (Color)ColorConverter.ConvertFromString(arriba),
            (Color)ColorConverter.ConvertFromString(abajo),
            new Point(0, 0), new Point(0, 1));
        d.Freeze();
        return d;
    }

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
