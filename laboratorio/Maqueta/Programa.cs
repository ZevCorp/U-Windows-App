using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace Maqueta;

/// <summary>
/// LA MAQUETA: una app cuya estructura verdadera está escrita (ver <c>verdad.json</c>).
///
/// Cada rasgo de aquí es una TRAMPA deliberada: reproduce un fallo real que costó una prueba y un
/// diagnóstico sobre explorer.exe o GitHub. La lista está en verdad.json y no se repite aquí —una
/// pregunta se responde en un sitio.
///
/// POR QUÉ PARECE UNA APP DE VERDAD Y NO UNA PANTALLA DE PRUEBAS. Un mapeador —y sobre todo el
/// arquitecto, que mira fotos— infiere de los MISMOS indicios que una persona: qué está agrupado
/// con qué, qué parece pulsable, qué tiene aspecto de fila de datos y qué de botón de sección. Una
/// maqueta de botones grises sueltos no ejercita nada de eso, así que mediría un caso que no
/// existe: en el mundo real toda app tiene jerarquía visual. Los indicios son parte de lo que hay
/// que saber leer (2026-08-12, pedido por el usuario).
///
/// Y hay uno puesto a propósito: la lista de artículos tiene COLUMNA «Tipo», que dice «Contenedor»
/// en la única fila que abre pantalla. Es el discriminador gratis que el arquitecto pidió al
/// auditar el explorador —«la columna Tipo dice literalmente Carpeta de archivos»—, aquí puesto
/// para poder comprobar si sabe usarlo.
/// </summary>
public static class Programa
{
    [STAThread]
    public static void Main() => new Application().Run(new Ventana());
}

public sealed class Ventana : Window
{
    // ── Paleta: una sola, para que el aspecto sea coherente y los indicios se lean ──
    private static readonly Brush Fondo = Pincel(0x14, 0x16, 0x1B);
    private static readonly Brush Panel = Pincel(0x1B, 0x1E, 0x25);
    private static readonly Brush Riel = Pincel(0x11, 0x13, 0x18);
    private static readonly Brush Linea = Pincel(0x2A, 0x2E, 0x38);
    private static readonly Brush Texto = Pincel(0xE6, 0xE8, 0xEC);
    private static readonly Brush Tenue = Pincel(0x8A, 0x91, 0x9E);
    private static readonly Brush Acento = Pincel(0x4C, 0x8D, 0xFF);
    private static readonly Brush AcentoTenue = Pincel(0x1E, 0x2E, 0x4D);
    private static readonly Brush Verde = Pincel(0x5C, 0xC2, 0x8D);

    private readonly ContentControl _zona = new();
    private readonly TextBlock _estado = new();
    private readonly TextBlock _miga = new();
    private readonly StackPanel _subcromo = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _actualizar = new();
    private readonly Stack<string> _historial = new();
    private readonly Dictionary<string, Button> _nav = new();
    private string _ubicacion = "";
    private int _accionesHechas;

    public Ventana()
    {
        Title = "Maqueta";
        Width = 1120; Height = 720;
        Background = Fondo;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        // CADA BARRA SE CONSTRUYE UNA VEZ. Llamar al método dos veces —una para SetDock y otra para
        // añadirla— crea DOS controles distintos, y como `_actualizar` y `_subcromo` son campos, el
        // segundo intento de añadirlos lanzaba «el elemento ya tiene un padre» y la ventana moría en
        // el constructor: el proceso quedaba vivo y sin ventana (2026-08-12, medido).
        var raiz = new DockPanel();
        var arriba = BarraSuperior(); DockPanel.SetDock(arriba, Dock.Top); raiz.Children.Add(arriba);
        var abajo = BarraEstado(); DockPanel.SetDock(abajo, Dock.Bottom); raiz.Children.Add(abajo);
        var riel = RielIzquierdo(); DockPanel.SetDock(riel, Dock.Left); raiz.Children.Add(riel);
        raiz.Children.Add(new Border { Background = Fondo, Child = _zona });
        Content = raiz;

        IrAInicio();
    }

    // ── CROMO GLOBAL: está en TODAS las pantallas ────────────────────────────

    private UIElement RielIzquierdo()
    {
        var col = new StackPanel { Width = 210, Background = Riel };
        AutomationProperties.SetAutomationId(col, "rielNavegacion");
        AutomationProperties.SetName(col, "Navegación principal");

        var marca = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 20, 18, 22) };
        marca.Children.Add(new Border
        {
            Width = 26, Height = 26, CornerRadius = new CornerRadius(7), Background = Acento,
            Child = new TextBlock { Text = "M", Foreground = Brushes.White, FontWeight = FontWeights.Bold,
                                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        });
        marca.Children.Add(new TextBlock
        {
            Text = "Maqueta", Foreground = Texto, FontSize = 15, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        });
        col.Children.Add(marca);

        col.Children.Add(Seccion("PRINCIPAL"));
        col.Children.Add(Nav("navInicio", "Inicio", "⌂", IrAInicio));
        col.Children.Add(Nav("navCatalogo", "Catálogo", "▤", IrACatalogo));
        col.Children.Add(Nav("navInformes", "Informes", "▦", IrAInformes));
        col.Children.Add(Seccion("SISTEMA"));
        col.Children.Add(Nav("navAjustes", "Ajustes", "⚙", IrAAjustes));
        return col;
    }

    private UIElement BarraSuperior()
    {
        var fila = new DockPanel { Background = Panel, LastChildFill = false };
        AutomationProperties.SetAutomationId(fila, "barraSuperior");
        AutomationProperties.SetName(fila, "Barra de navegación");

        var izq = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 8, 0, 8) };

        // El gesto de VOLVER. No es una puerta: a dónde lleva depende del historial, no de la
        // estructura, y el mapa tiene que aprender a no acuñar aristas con él.
        izq.Children.Add(Icono("botonAtras", "Atrás", "←", () =>
        {
            if (_historial.Count == 0) { Avisar("no hay a dónde volver"); return; }
            PorNombre(_historial.Pop(), apilar: false);
        }));

        // NOMBRE PLANTILLA: un solo botón cuyo Name lleva dentro la pantalla actual. El
        // AutomationId NO cambia — ahí está la identidad, y es lo que hay que mirar.
        AutomationProperties.SetAutomationId(_actualizar, "botonActualizar");
        VestirIcono(_actualizar, "↻");
        _actualizar.Click += (_, __) => Avisar($"actualizado «{_ubicacion}»");
        izq.Children.Add(_actualizar);

        _miga.Foreground = Tenue;
        _miga.FontSize = 12.5;
        _miga.VerticalAlignment = VerticalAlignment.Center;
        _miga.Margin = new Thickness(12, 0, 0, 0);
        AutomationProperties.SetAutomationId(_miga, "migaDePan");
        AutomationProperties.SetName(_miga, "Ruta actual");
        izq.Children.Add(_miga);

        DockPanel.SetDock(izq, Dock.Left);
        fila.Children.Add(izq);

        var der = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 10, 8) };
        // SUBCROMO: se rellena solo dentro de Catálogo y se vacía al salir. Su dominio es Catálogo
        // y todo lo que cuelga de él.
        AutomationProperties.SetAutomationId(_subcromo, "barraDeVista");
        AutomationProperties.SetName(_subcromo, "Opciones de vista del catálogo");
        der.Children.Add(_subcromo);
        der.Children.Add(Buscador());
        DockPanel.SetDock(der, Dock.Right);
        fila.Children.Add(der);

        return new Border { Child = fila, BorderBrush = Linea, BorderThickness = new Thickness(0, 0, 0, 1) };
    }

    private UIElement Buscador()
    {
        var caja = new Border
        {
            Background = Fondo, CornerRadius = new CornerRadius(6), BorderBrush = Linea,
            BorderThickness = new Thickness(1), Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(8, 0, 4, 0),
        };
        var fila = new StackPanel { Orientation = Orientation.Horizontal };
        fila.Children.Add(new TextBlock { Text = "⌕", Foreground = Tenue, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        var b = new Button { Content = "Buscar" };
        AutomationProperties.SetAutomationId(b, "botonBuscar");
        AutomationProperties.SetName(b, "Buscar");
        Desnudar(b);
        b.Foreground = Tenue; b.Padding = new Thickness(6, 5, 6, 5);
        b.Click += (_, __) => Avisar("buscador abierto · es una ACCIÓN: no lleva a otra pantalla");
        fila.Children.Add(b);
        caja.Child = fila;
        return caja;
    }

    private UIElement BarraEstado()
    {
        _estado.Foreground = Verde;
        _estado.Margin = new Thickness(16, 7, 16, 8);
        _estado.FontSize = 11.5;
        AutomationProperties.SetAutomationId(_estado, "barraEstado");
        AutomationProperties.SetName(_estado, "Estado");
        return new Border { Background = Panel, BorderBrush = Linea, BorderThickness = new Thickness(0, 1, 0, 0), Child = _estado };
    }

    // ── LAS UBICACIONES ──────────────────────────────────────────────────────

    private void IrAInicio() => Pintar("Inicio", () =>
    {
        var col = Pagina("Inicio", "Un vistazo a lo que hay. Las tres pestañas cambian TODO el panel.");

        // Estas tres pestañas sustituyen el contenido ENTERO y no tocan el título de la ventana.
        // Para quien derive la identidad de la ruta, las tres son la misma pantalla — y la captura
        // demuestra que no lo son.
        var pestanas = new TabControl { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Margin = new Thickness(24, 8, 24, 0) };
        AutomationProperties.SetAutomationId(pestanas, "pestanasInicio");
        AutomationProperties.SetName(pestanas, "Secciones de Inicio");
        foreach (var (id, nombre, titular, detalle) in new[]
        {
            ("pestanaResumen", "Resumen", "3 familias · 41 artículos · 2 informes", "Todo el catálogo cabe en cuatro niveles."),
            ("pestanaActividad", "Actividad", "Ayer se exportó el informe mensual", "Hace 2 días: se archivó «Lote A1-2»."),
            ("pestanaFavoritos", "Favoritos", "Todavía no has marcado ningún favorito", "Marca uno desde el catálogo."),
        })
        {
            var p = new TabItem { Foreground = Texto, Header = nombre };
            AutomationProperties.SetAutomationId(p, id);
            AutomationProperties.SetName(p, nombre);
            var cuerpo = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
            cuerpo.Children.Add(Tarjeta(titular, detalle));
            p.Content = cuerpo;
            pestanas.Items.Add(p);
        }
        col.Children.Add(pestanas);
        return col;
    });

    private void IrACatalogo() => Pintar("Catálogo", () =>
    {
        PonerSubcromo();
        var col = Pagina("Catálogo", "Las FAMILIAS abren pantalla propia. Los artículos son contenido — salvo uno.");

        col.Children.Add(Subtitulo("Familias"));
        var familias = new WrapPanel { Margin = new Thickness(24, 0, 24, 6) };
        foreach (var (f, cuantos) in new[] { ("A", "18 artículos"), ("B", "14 artículos"), ("C", "9 artículos") })
            familias.Children.Add(TarjetaNav($"navFamilia{f}", $"Familia {f}", cuantos, () => IrAFamilia(f)));
        col.Children.Add(familias);

        col.Children.Add(Subtitulo("Artículos"));
        col.Children.Add(ListaDeArticulos());
        return col;
    });

    /// <summary>
    /// La lista de contenido, CON COLUMNA «Tipo». Cuarenta filas que no llevan a ningún sitio y una
    /// que sí — y la columna lo dice, igual que el explorador dice «Carpeta de archivos». Es el
    /// discriminador gratis que el arquitecto pidió; existe aquí para ver si sabe usarlo.
    /// </summary>
    private UIElement ListaDeArticulos()
    {
        var lista = new ListView
        {
            Height = 330, Margin = new Thickness(24, 0, 24, 16),
            Background = Panel, Foreground = Texto, BorderBrush = Linea, BorderThickness = new Thickness(1),
        };
        AutomationProperties.SetAutomationId(lista, "listaArticulos");
        AutomationProperties.SetName(lista, "Artículos del catálogo");

        var rejilla = new GridView();
        foreach (var (cab, ancho, ruta) in new[] { ("Nombre", 320.0, "Nombre"), ("Tipo", 150.0, "Tipo"), ("Tamaño", 110.0, "Tamano") })
            rejilla.Columns.Add(new GridViewColumn { Header = cab, Width = ancho, DisplayMemberBinding = new System.Windows.Data.Binding(ruta) });
        lista.View = rejilla;

        for (int i = 1; i <= 40; i++)
        {
            // HOMÓNIMO: «Documentos» aquí es CONTENIDO. En Ajustes, el mismo nombre es navegación.
            // Por nombre son indistinguibles; por identidad, no.
            string nombre = i == 7 ? "Documentos" : $"Artículo {i:00}";
            var it = new ListViewItem { Content = new Fila(nombre, "Artículo", $"{12 + i * 3} KB"), Foreground = Texto };
            AutomationProperties.SetAutomationId(it, $"articulo{i:00}");
            AutomationProperties.SetName(it, nombre);
            it.MouseDoubleClick += (_, __) => Avisar($"«{nombre}» es contenido: no lleva a ninguna parte");
            lista.Items.Add(it);
        }

        // La puerta escondida entre el relleno. Es la lección del explorador —la carpeta es un
        // elemento de lista igual que el archivo— y fue el techo de profundidad del mapa.
        var ficha = new ListViewItem { Content = new Fila("Ficha maestra", "Contenedor", "—"), Foreground = Acento };
        AutomationProperties.SetAutomationId(ficha, "articuloFichaMaestra");
        AutomationProperties.SetName(ficha, "Ficha maestra");
        ficha.MouseDoubleClick += (_, __) => IrAFicha();
        lista.Items.Add(ficha);
        return lista;
    }

    private sealed record Fila(string Nombre, string Tipo, string Tamano);

    private void IrAFamilia(string cual) => Pintar($"Familia {cual}", () =>
    {
        PonerSubcromo();   // el subcromo SIGUE aquí: su dominio es Catálogo entero
        var col = Pagina($"Familia {cual}", "Nivel 3: subfamilias. Debajo hay lotes, y ahí se acaba.");
        var fila = new WrapPanel { Margin = new Thickness(24, 0, 24, 0) };
        for (int i = 1; i <= 2; i++)
        {
            string sub = $"{cual}{i}";
            fila.Children.Add(TarjetaNav($"navSubfamilia{sub}", $"Subfamilia {sub}", "2 lotes", () => IrASubfamilia(sub)));
        }
        col.Children.Add(fila);
        return col;
    });

    private void IrASubfamilia(string cual) => Pintar($"Subfamilia {cual}", () =>
    {
        PonerSubcromo();
        var col = Pagina($"Subfamilia {cual}", "Nivel 4: lotes. Este es el FONDO — no hay nada debajo.");
        var fila = new WrapPanel { Margin = new Thickness(24, 0, 24, 0) };
        for (int i = 1; i <= 2; i++)
        {
            string lote = $"{cual}-{i}";
            fila.Children.Add(TarjetaNav($"navLote{cual}{i}", $"Lote {lote}", "hoja del árbol", () => IrALote(lote)));
        }
        col.Children.Add(fila);
        return col;
    });

    private void IrALote(string cual) => Pintar($"Lote {cual}", () =>
    {
        PonerSubcromo();
        var col = Pagina($"Lote {cual}", "Hoja del árbol. Aquí solo hay contenido y acciones.");
        col.Children.Add(BarraDeAcciones(("accionMarcarLote", "Marcar como revisado", "✓")));
        col.Children.Add(Tarjeta("Sin incidencias", "El lote se revisó por última vez hace 4 días."));
        return col;
    });

    private void IrAFicha() => Pintar("Ficha maestra", () =>
    {
        PonerSubcromo();
        var col = Pagina("Ficha maestra", "Se llegó aquí desde la LISTA de contenido. Era una puerta.");
        col.Children.Add(Tarjeta("Contenedor", "La columna «Tipo» ya lo decía antes de cruzarlo."));
        return col;
    });

    private void IrAInformes() => Pintar("Informes", () =>
    {
        var col = Pagina("Informes", "Todo lo de aquí son ACCIONES: cambian algo y no llevan a otra pantalla.");
        col.Children.Add(BarraDeAcciones(
            ("accionExportar", "Exportar", "↧"),
            ("accionImprimir", "Imprimir", "⎙"),
            ("accionDuplicar", "Duplicar", "⧉")));

        // MENÚ EFÍMERO: existe mientras su padre esté abierto. Entras, te mueves, escapas — se
        // comporta como una ubicación, y su dueño es esta pantalla.
        var menu = new Menu { Background = Brushes.Transparent, Margin = new Thickness(24, 4, 24, 0), Foreground = Texto };
        var raiz = new MenuItem { Header = "Más opciones  ▾", Foreground = Texto };
        AutomationProperties.SetAutomationId(raiz, "menuMasOpciones");
        AutomationProperties.SetName(raiz, "Más opciones");
        foreach (var (id, nombre) in new[]
        {
            ("menuProgramar", "Programar envío"), ("menuPlantilla", "Cambiar plantilla"), ("menuArchivar", "Archivar"),
        })
        {
            var it = new MenuItem { Header = nombre };
            AutomationProperties.SetAutomationId(it, id);
            AutomationProperties.SetName(it, nombre);
            it.Click += (_, __) => Contar($"«{nombre}» ejecutado desde el menú");
            raiz.Items.Add(it);
        }
        menu.Items.Add(raiz);
        col.Children.Add(menu);

        col.Children.Add(Tarjeta("Informe mensual", "Generado el 1 de agosto · 24 páginas"));
        col.Children.Add(Tarjeta("Informe de incidencias", "Generado el 8 de agosto · 6 páginas"));
        return col;
    });

    private void IrAAjustes() => Pintar("Ajustes", () =>
    {
        var col = Pagina("Ajustes", "«Documentos» de aquí NAVEGA. El del catálogo es contenido. Mismo nombre.");
        var fila = new WrapPanel { Margin = new Thickness(24, 0, 24, 0) };
        // HOMÓNIMO, la otra mitad: aquí «Documentos» sí es una puerta.
        fila.Children.Add(TarjetaNav("navDocumentos", "Documentos", "plantillas y adjuntos", () => Pintar("Documentos", () =>
            Pagina("Documentos", "Pantalla propia. El homónimo del catálogo no lleva aquí."))));
        fila.Children.Add(TarjetaNav("navCuenta", "Cuenta", "perfil y sesión", () => Pintar("Cuenta", () =>
            Pagina("Cuenta", "Datos de la persona que usa la maqueta."))));
        col.Children.Add(fila);
        return col;
    });

    // ── La maquinaria ────────────────────────────────────────────────────────

    /// <summary>
    /// Pinta una ubicación y ANUNCIA CUÁL ES EN EL TÍTULO. De ahí sale la identidad de superficie,
    /// igual que en el explorador; sin esto todas las pantallas serían la misma para quien mire
    /// desde fuera. La excepción son las pestañas de Inicio, que no pasan por aquí: eso es una
    /// trampa y se quiere que exista.
    /// </summary>
    private void Pintar(string ubicacion, Func<StackPanel> cuerpo, bool apilar = true)
    {
        if (apilar && _ubicacion.Length > 0 && _ubicacion != ubicacion) _historial.Push(_ubicacion);
        _ubicacion = ubicacion;
        Title = $"Maqueta — {ubicacion}";
        AutomationProperties.SetName(_actualizar, $"Actualizar \"{ubicacion}\" (F5)");
        _miga.Text = Miga(ubicacion);

        bool enCatalogo = ubicacion is "Catálogo" or "Ficha maestra"
                       || ubicacion.StartsWith("Familia") || ubicacion.StartsWith("Subfamilia") || ubicacion.StartsWith("Lote");
        if (!enCatalogo) _subcromo.Children.Clear();   // fuera de su dominio, el subcromo NO existe

        foreach (var (id, b) in _nav)
            b.Background = id == IdDeSeccion(ubicacion) ? AcentoTenue : Brushes.Transparent;

        _zona.Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = cuerpo(),
        };
        Avisar($"estás en «{ubicacion}»");
    }

    private static string IdDeSeccion(string u) =>
        u is "Catálogo" or "Ficha maestra" || u.StartsWith("Familia") || u.StartsWith("Subfamilia") || u.StartsWith("Lote") ? "navCatalogo"
        : u == "Informes" ? "navInformes"
        : u is "Ajustes" or "Documentos" or "Cuenta" ? "navAjustes"
        : "navInicio";

    private static string Miga(string u) => u switch
    {
        "Inicio" or "Catálogo" or "Informes" or "Ajustes" => u,
        "Ficha maestra" => "Catálogo  ›  Ficha maestra",
        "Documentos" or "Cuenta" => $"Ajustes  ›  {u}",
        _ when u.StartsWith("Familia") => $"Catálogo  ›  {u}",
        _ when u.StartsWith("Subfamilia") => $"Catálogo  ›  Familia {u[11]}  ›  {u}",
        _ when u.StartsWith("Lote") => $"Catálogo  ›  Familia {u[5]}  ›  Subfamilia {u[5..7]}  ›  {u}",
        _ => u,
    };

    /// <summary>El subcromo: nivel 2, y su dominio es Catálogo y todo lo que cuelga de él.</summary>
    private void PonerSubcromo()
    {
        if (_subcromo.Children.Count > 0) return;
        foreach (var (id, nombre, icono) in new[]
        {
            ("vistaTarjetas", "Vista tarjetas", "▦"), ("vistaLista", "Vista lista", "☰"), ("filtroCatalogo", "Filtro", "⌄"),
        })
            _subcromo.Children.Add(Icono(id, nombre, icono, () => Avisar($"«{nombre}»: solo existe dentro del catálogo")));
    }

    private void PorNombre(string ubicacion, bool apilar)
    {
        Action ir = ubicacion switch
        {
            "Inicio" => IrAInicio,
            "Catálogo" => IrACatalogo,
            "Informes" => IrAInformes,
            "Ajustes" => IrAAjustes,
            "Ficha maestra" => IrAFicha,
            _ when ubicacion.StartsWith("Familia ") => () => IrAFamilia(ubicacion[8..]),
            _ when ubicacion.StartsWith("Subfamilia ") => () => IrASubfamilia(ubicacion[11..]),
            _ when ubicacion.StartsWith("Lote ") => () => IrALote(ubicacion[5..]),
            _ => IrAInicio,
        };
        if (!apilar) { string guardar = _ubicacion; _ubicacion = ubicacion; ir(); _ubicacion = ubicacion; }
        else ir();
    }

    private void Contar(string que) { _accionesHechas++; Avisar($"{que} · {_accionesHechas} acción(es) en esta sesión"); }
    private void Avisar(string t) => _estado.Text = t;

    // ── Piezas visuales. Los INDICIOS son parte de lo que hay que saber leer ──

    private static StackPanel Pagina(string titulo, string subtitulo)
    {
        var col = new StackPanel();
        col.Children.Add(new TextBlock
        {
            Text = titulo, Foreground = Texto, FontSize = 24, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(24, 22, 24, 2),
        });
        col.Children.Add(new TextBlock
        {
            Text = subtitulo, Foreground = Tenue, FontSize = 12.5, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(24, 0, 24, 18),
        });
        return col;
    }

    private static UIElement Subtitulo(string t) => new TextBlock
    {
        Text = t.ToUpperInvariant(), Foreground = Tenue, FontSize = 10.5, FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(24, 10, 24, 8),
    };

    private static UIElement Seccion(string t) => new TextBlock
    {
        Text = t, Foreground = Pincel(0x5A, 0x61, 0x70), FontSize = 10, FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(20, 14, 20, 6),
    };

    /// <summary>Una TARJETA que navega: parece pulsable y lleva chevrón. El indicio importa.</summary>
    private static UIElement TarjetaNav(string id, string titulo, string pie, Action ir)
    {
        var b = new Button { Width = 210, Margin = new Thickness(0, 0, 12, 12), Cursor = System.Windows.Input.Cursors.Hand };
        AutomationProperties.SetAutomationId(b, id);
        AutomationProperties.SetName(b, titulo);
        Desnudar(b);
        var caja = new Border
        {
            Background = Panel, BorderBrush = Linea, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9), Padding = new Thickness(14, 12, 12, 12),
        };
        var col = new StackPanel();
        var cab = new DockPanel();
        var t = new TextBlock { Text = titulo, Foreground = Texto, FontSize = 13.5, FontWeight = FontWeights.SemiBold };
        DockPanel.SetDock(t, Dock.Left); cab.Children.Add(t);
        var ch = new TextBlock { Text = "›", Foreground = Acento, FontSize = 16, HorizontalAlignment = HorizontalAlignment.Right };
        cab.Children.Add(ch);
        col.Children.Add(cab);
        col.Children.Add(new TextBlock { Text = pie, Foreground = Tenue, FontSize = 11, Margin = new Thickness(0, 5, 0, 0) });
        caja.Child = col;
        b.Content = caja;
        b.Click += (_, __) => ir();
        return b;
    }

    private static UIElement Tarjeta(string titulo, string detalle)
    {
        var caja = new Border
        {
            Background = Panel, BorderBrush = Linea, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9), Padding = new Thickness(14), Margin = new Thickness(24, 0, 24, 10),
            HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 420,
        };
        var col = new StackPanel();
        col.Children.Add(new TextBlock { Text = titulo, Foreground = Texto, FontSize = 13, FontWeight = FontWeights.SemiBold });
        col.Children.Add(new TextBlock { Text = detalle, Foreground = Tenue, FontSize = 11.5, Margin = new Thickness(0, 5, 0, 0), TextWrapping = TextWrapping.Wrap });
        caja.Child = col;
        return caja;
    }

    /// <summary>Las ACCIONES viven juntas y se ven distintas de la navegación. El agrupamiento
    /// visual es un indicio, y aquí se pone a propósito para ver si se lee.</summary>
    private UIElement BarraDeAcciones(params (string Id, string Nombre, string Icono)[] cuales)
    {
        var caja = new Border
        {
            Background = Panel, BorderBrush = Linea, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9), Margin = new Thickness(24, 0, 24, 14),
            Padding = new Thickness(6, 3, 6, 3), HorizontalAlignment = HorizontalAlignment.Left,
        };
        var fila = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (id, nombre, ic) in cuales)
        {
            var b = new Button { Content = $"{ic}  {nombre}" };
            AutomationProperties.SetAutomationId(b, id);
            AutomationProperties.SetName(b, nombre);
            Desnudar(b);
            b.Foreground = Texto; b.FontSize = 12; b.Padding = new Thickness(12, 8, 12, 8);
            b.Click += (_, __) => Contar($"«{nombre}» ejecutado");
            fila.Children.Add(b);
        }
        caja.Child = fila;
        return caja;
    }

    private Button Nav(string id, string nombre, string icono, Action ir)
    {
        var b = new Button { Cursor = System.Windows.Input.Cursors.Hand, Margin = new Thickness(10, 1, 10, 1) };
        AutomationProperties.SetAutomationId(b, id);
        AutomationProperties.SetName(b, nombre);
        Desnudar(b);
        b.HorizontalContentAlignment = HorizontalAlignment.Left;
        b.Padding = new Thickness(10, 9, 10, 9);
        var fila = new StackPanel { Orientation = Orientation.Horizontal };
        fila.Children.Add(new TextBlock { Text = icono, Foreground = Tenue, FontSize = 13, Width = 22 });
        fila.Children.Add(new TextBlock { Text = nombre, Foreground = Texto, FontSize = 13 });
        b.Content = fila;
        b.Click += (_, __) => ir();
        _nav[id] = b;
        return b;
    }

    private static Button Icono(string id, string nombre, string glifo, Action alPulsar)
    {
        var b = new Button();
        AutomationProperties.SetAutomationId(b, id);
        AutomationProperties.SetName(b, nombre);
        VestirIcono(b, glifo);
        b.Click += (_, __) => alPulsar();
        return b;
    }

    private static void VestirIcono(Button b, string glifo)
    {
        b.Content = glifo;
        Desnudar(b);
        b.Foreground = Tenue;
        b.FontSize = 14;
        b.Width = 32; b.Height = 30;
        b.Margin = new Thickness(2, 0, 2, 0);
        b.ToolTip = AutomationProperties.GetName(b);
    }

    private static void Desnudar(Button b)
    {
        b.Background = Brushes.Transparent;
        b.BorderThickness = new Thickness(0);
        b.Cursor = System.Windows.Input.Cursors.Hand;
        b.Template = SinCaja();
    }

    /// <summary>Un botón sin la caja gris de Windows: se ve el contenido y nada más. Sigue siendo
    /// un Button de verdad —control type «button», patrón Invoke—, que es lo que hay que mapear.</summary>
    private static ControlTemplate SinCaja()
    {
        var p = new ControlTemplate(typeof(Button));
        var borde = new FrameworkElementFactory(typeof(Border));
        borde.SetValue(Border.BackgroundProperty, new System.Windows.TemplateBindingExtension(Control.BackgroundProperty));
        borde.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        borde.SetValue(Border.PaddingProperty, new System.Windows.TemplateBindingExtension(Control.PaddingProperty));
        var pres = new FrameworkElementFactory(typeof(ContentPresenter));
        pres.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        pres.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        borde.AppendChild(pres);
        p.VisualTree = borde;
        return p;
    }

    private static SolidColorBrush Pincel(byte r, byte g, byte b)
    {
        var s = new SolidColorBrush(Color.FromRgb(r, g, b));
        s.Freeze();
        return s;
    }
}
