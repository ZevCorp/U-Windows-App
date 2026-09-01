using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;

namespace Medidor.App;

/// <summary>Un médico del roster: lo que el selector muestra. El id es opaco (uuid del roster del
/// portal); el nombre es lo que el médico reconoce.</summary>
public sealed record MedicoDelRoster(string Id, string Nombre);

/// <summary>
/// Las dos ventanas del medidor, construidas en código (sin XAML, para que el proyecto compile
/// desde Linux). Ninguna es la app: la app es el icono de bandeja. Estas se abren a pedido.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class Ventanas
{
    /// <summary>El selector de turno: elige tu nombre entre ~10. Simple a propósito — se usa al
    /// cambio de turno, con prisa. Devuelve null si se cierra sin elegir (el turno sigue anónimo,
    /// que mide igual).</summary>
    public static MedicoDelRoster? ElegirMedico(IReadOnlyList<MedicoDelRoster> roster, string? actual)
    {
        var win = new Window
        {
            Title = "¿Quién está en este turno?",
            Width = 360, Height = 120 + Math.Min(roster.Count, 12) * 40,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize, Topmost = true,
        };
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock
        {
            Text = "Elige tu nombre para este turno. Se mide el trabajo, no lo que escribes.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12),
        });

        MedicoDelRoster? elegido = null;
        var lista = new ListBox { MaxHeight = 320 };
        foreach (var m in roster)
        {
            lista.Items.Add(new ListBoxItem { Content = m.Nombre, Tag = m, IsSelected = m.Id == actual });
        }
        lista.MouseDoubleClick += (_, _) => { if (lista.SelectedItem is ListBoxItem li) { elegido = (MedicoDelRoster)li.Tag; win.DialogResult = true; } };
        panel.Children.Add(lista);

        var botones = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "Es mi turno", Padding = new Thickness(12, 4, 12, 4), IsDefault = true };
        ok.Click += (_, _) => { if (lista.SelectedItem is ListBoxItem li) { elegido = (MedicoDelRoster)li.Tag; win.DialogResult = true; } };
        botones.Children.Add(ok);
        panel.Children.Add(botones);

        win.Content = panel;
        return win.ShowDialog() == true ? elegido : null;
    }

    /// <summary>El diálogo de enrolamiento: se teclea una vez, el código corto que da el superadmin.
    /// Devuelve el código o null.</summary>
    public static string? PedirCodigoDeEnrolamiento()
    {
        var win = new Window
        {
            Title = "Conectar el medidor",
            Width = 380, Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize, Topmost = true,
        };
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock
        {
            Text = "Escribe el código de instalación que te dieron. Se pide una sola vez en este computador.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12),
        });
        var caja = new TextBox { FontSize = 22, CharacterCasing = CharacterCasing.Upper, MaxLength = 8, Margin = new Thickness(0, 0, 0, 12) };
        panel.Children.Add(caja);

        string? codigo = null;
        var ok = new Button { Content = "Conectar", Padding = new Thickness(12, 4, 12, 4), IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right };
        ok.Click += (_, _) => { codigo = caja.Text.Trim(); win.DialogResult = codigo.Length == 8; };
        panel.Children.Add(ok);

        win.Content = panel;
        caja.Loaded += (_, _) => caja.Focus();
        return win.ShowDialog() == true ? codigo : null;
    }

    public static void QueSeMide()
        => MessageBox.Show(
            "Este computador mide TIEMPOS de trabajo, no contenido.\n\n"
            + "• Cuánto tiempo se usa cada aplicación y el sistema clínico.\n"
            + "• Cuántos clics y cuánto tecleo hay — NUNCA qué se escribe.\n"
            + "• Qué pantallas del sistema se recorren — NUNCA los datos del paciente.\n\n"
            + "El nombre del paciente y su historia no salen de este computador.\n"
            + "Puedes pausar la medición desde el icono cuando quieras.",
            "¿Qué mide el medidor?", MessageBoxButton.OK, MessageBoxImage.Information);
}
