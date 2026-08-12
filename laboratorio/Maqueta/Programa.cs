using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace Maqueta;

/// <summary>
/// LA MAQUETA: una app cuya estructura verdadera está escrita (ver <c>verdad.json</c>).
///
/// Cada rasgo de aquí es una TRAMPA deliberada: reproduce un fallo real que costó una prueba y un
/// diagnóstico sobre explorer.exe o GitHub. La lista, con lo que cada uno mide:
///
///  1. CROMO GLOBAL (riel + barra) — está en todas las pantallas. Mide que el mapa lo declare una
///     vez y no una por pantalla.
///  2. SUBCROMO (la barra de vista dentro de Catálogo) — está en TODAS las pantallas de Catálogo y
///     en NINGUNA de fuera. Mide que «cromo» necesite un DOMINIO: sin él, el mapa promete que se
///     alcanza desde cualquier sitio y falla en cuatro pantallas.
///  3. ACCIONES (Exportar, Imprimir, Duplicar) — hacen algo visible y NO navegan. Mide que se
///     clasifiquen sin borrarlas: el asistente las necesitará para ejecutar.
///  4. CONTENIDO (los artículos) — no navegan. Mide que no ahoguen la estructura.
///  5. CONTENIDO QUE SÍ NAVEGA (el artículo «Ficha maestra») — la lección del explorador: en una
///     lista de contenido puede haber una puerta, y ocultarla entera es el techo de profundidad.
///  6. HOMÓNIMOS — «Documentos» existe como navegación en Ajustes y como contenido en Catálogo.
///     Mide que el selector distinga por identidad y no por nombre.
///  7. NOMBRE PLANTILLA — «Actualizar "X" (F5)» cambia de nombre en cada pantalla siendo UN botón
///     (AutomationId fijo). Mide que la lista de pendientes agrupe por identidad.
///  8. PESTAÑAS SIN CAMBIO DE IDENTIDAD — Resumen/Actividad/Favoritos sustituyen el contenido
///     entero y NO cambian el título de la ventana. Mide el problema de identidad de superficie:
///     el mapa dirá «la pantalla no cambió» cuando sí cambió.
///  9. MENÚ EFÍMERO — «Más opciones» abre una superficie que existe mientras su padre esté abierto.
///     Mide que un menú sea una UBICACIÓN con dueño y vida, no un grafo aparte.
/// 10. PROFUNDIDAD REAL Y FINITA — Catálogo › Familia › Subfamilia › Lote. A diferencia de un
///     disco, aquí el fondo existe y está escrito, así que «llegué al final» es comprobable.
///
/// La ventana ANUNCIA SU UBICACIÓN EN EL TÍTULO, como hace el explorador, porque de ahí sale la
/// identidad de superficie. La excepción es la trampa nº8, y es deliberada.
/// </summary>
public static class Programa
{
    [STAThread]
    public static void Main()
    {
        var app = new Application();
        app.Run(new Ventana());
    }
}

public sealed class Ventana : Window
{
    // ── El estado, que es mínimo a propósito ────────────────────────────────
    private readonly ContentControl _zona = new();
    private readonly TextBlock _estado = new();
    private readonly StackPanel _subcromo = new() { Orientation = Orientation.Horizontal };
    private readonly Button _actualizar = new();
    private readonly Stack<Action> _historial = new();
    private string _ubicacion = "";
    private int _accionesHechas;

    public Ventana()
    {
        Title = "Maqueta";
        Width = 1000; Height = 680;
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x24));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        // CADA BARRA SE CONSTRUYE UNA VEZ. Llamar al método dos veces —una para SetDock y otra para
        // añadirla— crea DOS controles distintos, y como `_actualizar` y `_subcromo` son campos,
        // el segundo intento de añadirlos lanzaba «el elemento ya tiene un padre» y la ventana
        // moría en el constructor: el proceso quedaba vivo y sin ventana (2026-08-12, medido).
        var raiz = new DockPanel();
        var arriba = BarraSuperior();
        DockPanel.SetDock(arriba, Dock.Top);
        raiz.Children.Add(arriba);

        var abajo = BarraEstado();
        DockPanel.SetDock(abajo, Dock.Bottom);
        raiz.Children.Add(abajo);

        var riel = RielIzquierdo();
        DockPanel.SetDock(riel, Dock.Left);
        raiz.Children.Add(riel);

        raiz.Children.Add(_zona);
        Content = raiz;

        IrAInicio();
    }

    // ── CROMO GLOBAL ────────────────────────────────────────────────────────
    // Está en TODAS las pantallas. Es el nivel 1 de esta app y no depende de dónde estés.

    private UIElement RielIzquierdo()
    {
        var col = new StackPanel
        {
            Width = 170,
            Background = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x2E)),
        };
        AutomationProperties.SetAutomationId(col, "rielNavegacion");
        AutomationProperties.SetName(col, "Navegación principal");

        col.Children.Add(Rotulo("MAQUETA", 13, true));
        col.Children.Add(Nav("navInicio", "Inicio", IrAInicio));
        col.Children.Add(Nav("navCatalogo", "Catálogo", IrACatalogo));
        col.Children.Add(Nav("navInformes", "Informes", IrAInformes));
        col.Children.Add(Nav("navAjustes", "Ajustes", IrAAjustes));
        return col;
    }

    private UIElement BarraSuperior()
    {
        var fila = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Background = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x36)),
        };
        AutomationProperties.SetAutomationId(fila, "barraSuperior");
        AutomationProperties.SetName(fila, "Barra de navegación");

        // El gesto de VOLVER. No es una puerta: a dónde lleva depende del historial, no de la
        // estructura. Está aquí porque el mapa tiene que aprender a no acuñar aristas con él.
        fila.Children.Add(Boton("botonAtras", "Atrás", () =>
        {
            if (_historial.Count == 0) { Avisar("no hay a dónde volver"); return; }
            var volver = _historial.Pop();
            volver();
            _historial.Pop();   // el propio destino se re-apila al pintar; se quita el duplicado
        }));

        // NOMBRE PLANTILLA (trampa nº7): un solo botón cuyo Name lleva dentro la pantalla actual.
        // El AutomationId NO cambia — ahí está la identidad, y es lo que hay que mirar.
        AutomationProperties.SetAutomationId(_actualizar, "botonActualizar");
        Estilar(_actualizar);
        _actualizar.Click += (_, __) => Avisar($"actualizado «{_ubicacion}»");
        fila.Children.Add(_actualizar);

        fila.Children.Add(Boton("botonBuscar", "Buscar", () => Avisar("buscador abierto (no navega)")));

        // SUBCROMO (trampa nº2): se rellena solo dentro de Catálogo y se vacía al salir.
        AutomationProperties.SetAutomationId(_subcromo, "barraDeVista");
        AutomationProperties.SetName(_subcromo, "Opciones de vista del catálogo");
        fila.Children.Add(_subcromo);
        return fila;
    }

    private UIElement BarraEstado()
    {
        _estado.Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xE6, 0xB4));
        _estado.Margin = new Thickness(10, 4, 10, 6);
        _estado.FontSize = 12;
        AutomationProperties.SetAutomationId(_estado, "barraEstado");
        AutomationProperties.SetName(_estado, "Estado");
        return _estado;
    }

    // ── LAS UBICACIONES ─────────────────────────────────────────────────────

    private void IrAInicio() => Pintar("Inicio", () =>
    {
        var col = new StackPanel { Margin = new Thickness(16) };
        col.Children.Add(Rotulo("Inicio", 20, true));

        // TRAMPA nº8: estas tres pestañas sustituyen el contenido ENTERO y no tocan el título de la
        // ventana. Para quien derive la identidad de la ruta, las tres son la misma pantalla — y la
        // captura demuestra que no lo son.
        var pestanas = new TabControl { Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        AutomationProperties.SetAutomationId(pestanas, "pestanasInicio");
        foreach (var (id, nombre, texto) in new[]
        {
            ("pestanaResumen", "Resumen", "3 familias · 42 artículos · 2 informes"),
            ("pestanaActividad", "Actividad", "Ayer: se exportó el informe mensual."),
            ("pestanaFavoritos", "Favoritos", "Todavía no has marcado ningún favorito."),
        })
        {
            var p = new TabItem { Header = nombre, Foreground = Brushes.White };
            AutomationProperties.SetAutomationId(p, id);
            AutomationProperties.SetName(p, nombre);
            p.Content = Rotulo(texto, 13, false);
            pestanas.Items.Add(p);
        }
        col.Children.Add(pestanas);
        return col;
    });

    private void IrACatalogo() => Pintar("Catálogo", () =>
    {
        PonerSubcromo();
        var col = new StackPanel { Margin = new Thickness(16) };
        col.Children.Add(Rotulo("Catálogo", 20, true));
        col.Children.Add(Rotulo("Las FAMILIAS navegan. Los artículos son contenido — salvo uno.", 11, false));

        // Navegación de nivel 2: finita y escrita, así que «llegué al fondo» es comprobable.
        var familias = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 8) };
        foreach (var f in new[] { "A", "B", "C" })
            familias.Children.Add(Boton($"navFamilia{f}", $"Familia {f}", () => IrAFamilia(f)));
        col.Children.Add(familias);

        // CONTENIDO (trampa nº4): cuarenta artículos que no llevan a ningún sitio…
        var lista = new ListBox
        {
            Height = 340,
            Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x2A)),
            Foreground = Brushes.White, BorderThickness = new Thickness(0),
        };
        AutomationProperties.SetAutomationId(lista, "listaArticulos");
        AutomationProperties.SetName(lista, "Artículos del catálogo");
        for (int i = 1; i <= 40; i++)
        {
            // HOMÓNIMO (trampa nº6): «Documentos» aquí es CONTENIDO. En Ajustes, el mismo nombre es
            // navegación. Por nombre son indistinguibles; por identidad, no.
            string nombre = i == 7 ? "Documentos" : $"Artículo {i:00}";
            var it = new ListBoxItem { Content = nombre };
            AutomationProperties.SetAutomationId(it, $"articulo{i:00}");
            AutomationProperties.SetName(it, nombre);
            it.MouseDoubleClick += (_, __) => Avisar($"«{nombre}» es contenido: no lleva a ninguna parte");
            lista.Items.Add(it);
        }
        // …salvo UNO (trampa nº5): la puerta escondida entre el relleno. Es la lección del
        // explorador —la carpeta es ListItem igual que el archivo— y el techo de profundidad.
        var ficha = new ListBoxItem { Content = "Ficha maestra", Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0xB4, 0xF8)) };
        AutomationProperties.SetAutomationId(ficha, "articuloFichaMaestra");
        AutomationProperties.SetName(ficha, "Ficha maestra");
        ficha.MouseDoubleClick += (_, __) => IrAFicha();
        lista.Items.Add(ficha);

        col.Children.Add(lista);
        return col;
    });

    private void IrAFamilia(string cual) => Pintar($"Familia {cual}", () =>
    {
        PonerSubcromo();   // el subcromo SIGUE aquí: su dominio es Catálogo entero
        var col = new StackPanel { Margin = new Thickness(16) };
        col.Children.Add(Rotulo($"Familia {cual}", 20, true));
        col.Children.Add(Rotulo("Nivel 3: subfamilias. Debajo hay lotes, y ahí se acaba.", 11, false));
        var fila = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        for (int i = 1; i <= 2; i++)
        {
            string sub = $"{cual}{i}";
            fila.Children.Add(Boton($"navSubfamilia{sub}", $"Subfamilia {sub}", () => IrASubfamilia(sub)));
        }
        col.Children.Add(fila);
        return col;
    });

    private void IrASubfamilia(string cual) => Pintar($"Subfamilia {cual}", () =>
    {
        PonerSubcromo();
        var col = new StackPanel { Margin = new Thickness(16) };
        col.Children.Add(Rotulo($"Subfamilia {cual}", 20, true));
        col.Children.Add(Rotulo("Nivel 4: lotes. Este es el FONDO — no hay nada debajo.", 11, false));
        var fila = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        for (int i = 1; i <= 2; i++)
        {
            string lote = $"{cual}-{i}";
            fila.Children.Add(Boton($"navLote{cual}{i}", $"Lote {lote}", () => IrALote(lote)));
        }
        col.Children.Add(fila);
        return col;
    });

    private void IrALote(string cual) => Pintar($"Lote {cual}", () =>
    {
        PonerSubcromo();
        var col = new StackPanel { Margin = new Thickness(16) };
        col.Children.Add(Rotulo($"Lote {cual}", 20, true));
        col.Children.Add(Rotulo("Hoja del árbol. Aquí solo hay contenido y acciones.", 11, false));
        col.Children.Add(Boton("accionMarcarLote", "Marcar como revisado", () => Contar("lote marcado")));
        return col;
    });

    private void IrAFicha() => Pintar("Ficha maestra", () =>
    {
        PonerSubcromo();
        var col = new StackPanel { Margin = new Thickness(16) };
        col.Children.Add(Rotulo("Ficha maestra", 20, true));
        col.Children.Add(Rotulo("Se llegó aquí desde la LISTA de contenido. Era una puerta.", 11, false));
        return col;
    });

    private void IrAInformes() => Pintar("Informes", () =>
    {
        var col = new StackPanel { Margin = new Thickness(16) };
        col.Children.Add(Rotulo("Informes", 20, true));
        col.Children.Add(Rotulo("Todo lo de aquí son ACCIONES: cambian algo y no llevan a otra pantalla.", 11, false));

        var fila = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 8) };
        fila.Children.Add(Boton("accionExportar", "Exportar", () => Contar("informe exportado")));
        fila.Children.Add(Boton("accionImprimir", "Imprimir", () => Contar("informe enviado a imprimir")));
        fila.Children.Add(Boton("accionDuplicar", "Duplicar", () => Contar("informe duplicado")));
        col.Children.Add(fila);

        // MENÚ EFÍMERO (trampa nº9): existe mientras su padre esté abierto. Entras, te mueves,
        // escapas — se comporta como una ubicación, y su dueño es esta pantalla.
        var menu = new Menu { Background = Brushes.Transparent, Margin = new Thickness(0, 8, 0, 0) };
        var raiz = new MenuItem { Header = "Más opciones", Foreground = Brushes.White };
        AutomationProperties.SetAutomationId(raiz, "menuMasOpciones");
        AutomationProperties.SetName(raiz, "Más opciones");
        foreach (var (id, nombre) in new[]
        {
            ("menuProgramar", "Programar envío"),
            ("menuPlantilla", "Cambiar plantilla"),
            ("menuArchivar", "Archivar"),
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
        return col;
    });

    private void IrAAjustes() => Pintar("Ajustes", () =>
    {
        var col = new StackPanel { Margin = new Thickness(16) };
        col.Children.Add(Rotulo("Ajustes", 20, true));
        col.Children.Add(Rotulo("«Documentos» de aquí NAVEGA. El del catálogo es contenido. Mismo nombre.", 11, false));
        var fila = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        // HOMÓNIMO (trampa nº6), la otra mitad: aquí «Documentos» sí es una puerta.
        fila.Children.Add(Boton("navDocumentos", "Documentos", () => Pintar("Documentos", () =>
        {
            var c = new StackPanel { Margin = new Thickness(16) };
            c.Children.Add(Rotulo("Documentos", 20, true));
            c.Children.Add(Rotulo("Pantalla propia. El homónimo del catálogo no lleva aquí.", 11, false));
            return c;
        })));
        fila.Children.Add(Boton("navCuenta", "Cuenta", () => Pintar("Cuenta", () =>
        {
            var c = new StackPanel { Margin = new Thickness(16) };
            c.Children.Add(Rotulo("Cuenta", 20, true));
            return c;
        })));
        col.Children.Add(fila);
        return col;
    });

    // ── La maquinaria, deliberadamente pequeña ──────────────────────────────

    /// <summary>
    /// Pinta una ubicación y ANUNCIA CUÁL ES EN EL TÍTULO. De ahí sale la identidad de superficie,
    /// igual que en el explorador; sin esto todas las pantallas serían la misma para quien mire
    /// desde fuera. La excepción son las pestañas de Inicio, que no pasan por aquí: esa es la
    /// trampa nº8 y se quiere que exista.
    /// </summary>
    private void Pintar(string ubicacion, Func<UIElement> cuerpo)
    {
        if (_ubicacion.Length > 0)
        {
            string anterior = _ubicacion;
            _historial.Push(() => PorNombre(anterior));
        }
        _ubicacion = ubicacion;
        Title = $"Maqueta — {ubicacion}";
        AutomationProperties.SetName(_actualizar, $"Actualizar \"{ubicacion}\" (F5)");
        _actualizar.Content = "Actualizar";
        if (!ubicacion.StartsWith("Catálogo") && !ubicacion.StartsWith("Familia")
            && !ubicacion.StartsWith("Subfamilia") && !ubicacion.StartsWith("Lote")
            && ubicacion != "Ficha maestra")
            _subcromo.Children.Clear();   // fuera de su dominio, el subcromo NO existe
        _zona.Content = cuerpo();
        Avisar($"estás en «{ubicacion}»");
    }

    /// <summary>El subcromo: nivel 2, y su dominio es Catálogo y todo lo que cuelga de él.</summary>
    private void PonerSubcromo()
    {
        _subcromo.Children.Clear();
        foreach (var (id, nombre) in new[]
        {
            ("vistaTarjetas", "Vista tarjetas"),
            ("vistaLista", "Vista lista"),
            ("filtroCatalogo", "Filtro"),
        })
            _subcromo.Children.Add(Boton(id, nombre, () => Avisar($"«{nombre}»: solo existe dentro del catálogo")));
    }

    private void PorNombre(string ubicacion)
    {
        switch (ubicacion)
        {
            case "Inicio": IrAInicio(); break;
            case "Catálogo": IrACatalogo(); break;
            case "Informes": IrAInformes(); break;
            case "Ajustes": IrAAjustes(); break;
            case "Ficha maestra": IrAFicha(); break;
            default:
                if (ubicacion.StartsWith("Familia ")) IrAFamilia(ubicacion[8..]);
                else if (ubicacion.StartsWith("Subfamilia ")) IrASubfamilia(ubicacion[11..]);
                else if (ubicacion.StartsWith("Lote ")) IrALote(ubicacion[5..]);
                else IrAInicio();
                break;
        }
    }

    private void Contar(string que) { _accionesHechas++; Avisar($"{que} · {_accionesHechas} acción(es) en esta sesión"); }
    private void Avisar(string t) => _estado.Text = t;

    private Button Nav(string id, string nombre, Action ir)
    {
        var b = Boton(id, nombre, ir);
        b.HorizontalAlignment = HorizontalAlignment.Stretch;
        b.HorizontalContentAlignment = HorizontalAlignment.Left;
        b.Margin = new Thickness(8, 3, 8, 3);
        return b;
    }

    private static Button Boton(string id, string nombre, Action alPulsar)
    {
        var b = new Button { Content = nombre };
        AutomationProperties.SetAutomationId(b, id);
        AutomationProperties.SetName(b, nombre);
        Estilar(b);
        b.Click += (_, __) => alPulsar();
        return b;
    }

    private static void Estilar(Button b)
    {
        b.Margin = new Thickness(6, 6, 0, 6);
        b.Padding = new Thickness(12, 6, 12, 6);
        b.FontSize = 12;
        b.Foreground = Brushes.White;
        b.Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x48));
        b.BorderThickness = new Thickness(0);
        b.Cursor = System.Windows.Input.Cursors.Hand;
    }

    private static TextBlock Rotulo(string t, double tam, bool fuerte) => new()
    {
        Text = t, FontSize = tam, Foreground = Brushes.White,
        FontWeight = fuerte ? FontWeights.SemiBold : FontWeights.Normal,
        Margin = new Thickness(8, 6, 8, 6), TextWrapping = TextWrapping.Wrap,
    };
}
