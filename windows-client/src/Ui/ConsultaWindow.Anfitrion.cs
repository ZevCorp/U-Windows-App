using System.Windows;
using System.Windows.Controls;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Ui;

/// <summary>
/// LA CONSULTA COMO ANFITRIÓN DE LA CARITA. Promesa 272 (spec 031).
/// </summary>
/// <remarks>
/// Archivo parcial aparte a propósito: <c>ConsultaWindow.cs</c> tiene una rama abierta que le mete
/// +858 líneas (<c>jose/la-nota-se-elige-y-se-corrige</c>), y todo lo del anfitrión cabe aquí
/// tocando del original una línea —la que cuelga el hueco de la vista de nota—.
///
/// EL HUECO ESTÁ AL FINAL DE LA NOTA, centrado: donde en el pantallazo del dueño está la carita,
/// debajo de la tarjeta de «pulsa grabar» y encima de «Listo.». Vacío no ocupa nada; con la carita
/// dentro, ella es la señal de «la tengo» —no hace falta otra—.
/// </remarks>
public sealed partial class ConsultaWindow : AnfitrionDeLaCarita
{
    private readonly Decorator _huecoDeLaCarita = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(0, 26, 0, 6),
    };

    private bool _guardando;

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
            LogBus.Log("consulta", value ? "la carita queda sentada en el centro" : "la carita sale de la consulta");
        }
    }

    public Decorator Hueco => _huecoDeLaCarita;

    public Window Ventana => this;

    private UIElement HuecoDeLaCarita() => _huecoDeLaCarita;
}
