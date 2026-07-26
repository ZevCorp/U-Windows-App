using System.Linq;
using System.Windows;
using U.WindowsClient.Diagnostics;
using U.WindowsClient.Uia;

namespace U.WindowsClient.Ui;

/// <summary>Ventana simple de registro en vivo, alimentada por <see cref="LogBus"/>.</summary>
public partial class LogWindow : Window
{
    public LogWindow()
    {
        InitializeComponent();
        foreach (var line in LogBus.Snapshot()) List.Items.Add(line);
        ScrollToEnd();

        LogBus.Logged += OnLogged;
        Closed += (_, _) => LogBus.Logged -= OnLogged;
    }

    private void OnLogged(object? sender, string line) => Dispatcher.Invoke(() =>
    {
        List.Items.Add(line);
        ScrollToEnd();
    });

    private void ScrollToEnd()
    {
        if (List.Items.Count > 0) List.ScrollIntoView(List.Items[^1]);
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        LogBus.Clear();
        List.Items.Clear();
    }

    /// <summary>
    /// La sonda de mapeo del árbol SAP (ver SONDA-MAPEO-ARBOL.md): barre el scrolleable visible con el
    /// hit-test nativo y vuelca las bandas crudas al registro. Vive aquí y no en el inspector porque es
    /// un experimento bajo demanda — el operador la dispara UNA vez con la pantalla correcta delante y
    /// copia el resultado con el botón de al lado. Corre en un hilo de fondo: el COM de SAP puede
    /// tardar y la ventana no debe congelarse.
    /// </summary>
    private async void OnProbeSap(object sender, RoutedEventArgs e)
    {
        ProbeBtn.IsEnabled = false;
        ProbeBtn.Content = "⏳ Sondando…";
        LogBus.Log("sap", "sonda de mapeo de árbol: inicio (solo lectura, no toca nada)");
        try
        {
            var reader = new SapInspectorReader();
            var lines = await Task.Run(() => reader.ProbeTreeMapping());
            foreach (string line in lines) LogBus.Log("sap", line);
        }
        catch (Exception ex)
        {
            LogBus.Log("sap", $"sonda: error inesperado: {ex.Message}");
        }
        finally
        {
            ProbeBtn.IsEnabled = true;
            ProbeBtn.Content = "🧭 Sondear SAP";
        }
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        string text = string.Join(Environment.NewLine, List.Items.Cast<string>());
        try
        {
            Clipboard.SetText(text.Length > 0 ? text : "(sin entradas)");
            CopyBtn.Content = "✅ Copiado";
        }
        catch
        {
            CopyBtn.Content = "⚠ No se pudo copiar";
        }
        finally
        {
            var reset = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            reset.Tick += (_, _) => { CopyBtn.Content = "📋 Copiar"; reset.Stop(); };
            reset.Start();
        }
    }
}
