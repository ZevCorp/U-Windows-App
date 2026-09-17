using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using U.Graph.Surfaces;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Ui;

/// <summary>
/// LA CONSULTA COMO ANFITRIÓN DE LA CARITA, y el botón de llevarla. Promesas 272 y 273 (spec 031).
/// </summary>
/// <remarks>
/// Archivo parcial aparte a propósito: <c>ConsultaWindow.cs</c> tiene una rama abierta que le mete
/// +858 líneas (<c>jose/la-nota-se-elige-y-se-corrige</c>), y todo lo del anfitrión cabe aquí
/// tocando del original una línea —la que cuelga el hueco de la vista de nota—.
///
/// EL HUECO ESTÁ AL FINAL DE LA NOTA, centrado: donde en el pantallazo del dueño está la carita,
/// debajo de la tarjeta de «pulsa grabar» y encima de «Listo.». Vacío no ocupa nada; con la carita
/// dentro, ella es la señal de «la tengo» —no hace falta otra—, y debajo aparece el botón.
///
/// EL BOTÓN NO ENSEÑA TEXTO AL PASAR EL RATÓN (promesa 164): lo que dice lo dice escrito. Y ofrece
/// los escritorios por su nombre, que es como los piensa la persona (ver <see cref="ReglaDelViaje"/>).
/// </remarks>
public sealed partial class ConsultaWindow : AnfitrionDeLaCarita
{
    private readonly Decorator _huecoDeLaCarita = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(0, 26, 0, 6),
    };

    private Button? _botonLlevar;
    private bool _guardando;

    /// <summary>
    /// Quien lleva de verdad: lo pone <see cref="FaceWindow"/> al sentar la carita aquí. Recibe el
    /// escritorio de destino, o <see cref="Guid.Empty"/> con <c>nuevo</c> para «Uno nuevo».
    /// </summary>
    public Action<Guid, bool>? Llevar { get; set; }

    public string Nombre => "consulta";

    /// <summary>
    /// La caja de la tarjeta visible, en coordenadas de pantalla: la ventana menos el hueco de la
    /// sombra (22/18/22/26, promesa 154). Escondida o minimizada no hay caja: no puede recibir un gesto.
    /// </summary>
    public Rect Caja
    {
        get
        {
            if (!IsVisible || WindowState == WindowState.Minimized) return Rect.Empty;
            double w = ActualWidth - 44, h = ActualHeight - 44;
            if (w <= 0 || h <= 0) return Rect.Empty;
            return new Rect(Left + 22, Top + 18, w, h);
        }
    }

    public bool Guardando
    {
        get => _guardando;
        set
        {
            if (_guardando == value) return;
            _guardando = value;
            if (_botonLlevar != null)
                _botonLlevar.Visibility = ReglaDelViaje.HayBoton(value, Nombre) ? Visibility.Visible : Visibility.Collapsed;
            LogBus.Log("consulta", value ? "la carita queda sentada en el centro" : "la carita sale de la consulta");
        }
    }

    public Decorator Hueco => _huecoDeLaCarita;

    public Window Ventana => this;

    /// <summary>La silla y, debajo, el botón de llevar (solo con la carita sentada).</summary>
    private UIElement HuecoDeLaCarita()
    {
        _botonLlevar = new Button
        {
            Content = new TextBlock
            {
                Text = "Llevar a otro escritorio",
                Foreground = Estudio.Tinta,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            },
            Height = 40,
            Padding = new Thickness(18, 0, 18, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = Estudio.Superficie,
            BorderBrush = Estudio.Borde,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Template = Estudio.Pastilla(20),
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 0, 0, 10),
        };
        _botonLlevar.ConRelieve(Estudio.Sombra2);
        _botonLlevar.Click += (_, __) => OfrecerLosEscritorios();

        var pila = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        pila.Children.Add(_huecoDeLaCarita);
        pila.Children.Add(_botonLlevar);
        return pila;
    }

    /// <summary>El menú con los escritorios por su nombre, menos el actual, y «Uno nuevo» al final.</summary>
    private void OfrecerLosEscritorios()
    {
        var orden = EscritorioVirtual.Orden();
        var actual = EscritorioVirtual.Actual();
        var nombres = orden.Select(EscritorioVirtual.NombrePuesto).ToArray();
        var etiquetas = ReglaDelViaje.Destinos(orden, nombres, actual);
        // Los GUID en el mismo orden que las etiquetas: los que no son el actual, y Guid.Empty para «Uno nuevo».
        var destinos = orden.Where(g => g != actual).Concat(new[] { Guid.Empty }).ToArray();

        var menu = new ContextMenu { PlacementTarget = _botonLlevar, Placement = PlacementMode.Top };
        for (int i = 0; i < etiquetas.Length; i++)
        {
            var destino = destinos[i];
            bool nuevo = destino == Guid.Empty;
            var item = new MenuItem { Header = etiquetas[i] };
            item.Click += (_, __) =>
            {
                if (Llevar == null)
                {
                    LogBus.Log("consulta", $"llevar a «{item.Header}»: todavía no hay quien lleve (fase 5 de la spec 031)");
                    return;
                }
                Llevar(destino, nuevo);
            };
            menu.Items.Add(item);
        }
        LogBus.Log("consulta", $"se ofrecen {etiquetas.Length} destino(s): {string.Join(" · ", etiquetas)}");
        menu.IsOpen = true;
    }
}
