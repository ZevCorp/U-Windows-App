using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using U.WindowsClient.Clinical;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Ui;

/// <summary>
/// ESCRIBIR MIENTRAS SE GRABA (spec 057): las secciones de la plantilla bajo la transcripción en
/// vivo, para meter el examen normal, una sospecha o un plan que no se dice en voz alta. Lo escrito
/// se suma a la nota al generarla, con el mismo bloque que la web (promesa 468).
/// </summary>
/// <remarks>
/// EL MISMO SISTEMA QUE LA WEB, no uno parecido: la misma tabla (`encounter_section_drafts`), el mismo
/// autoguardado a 900 ms y la misma regla al abrir (la copia local gana). Lo escrito aquí lo ve la
/// web al abrir la consulta, y al revés.
///
/// PLEGADAS AL EMPEZAR, como en la web: con la consulta corriendo, la pantalla es de lo que se oye.
/// Una sección se abre al tocarla, y una plegada con texto enseña su primer renglón.
/// </remarks>
public sealed partial class ConsultaWindow
{
    private readonly StackPanel _panelBorradores = new() { Visibility = Visibility.Collapsed };
    private IReadOnlyList<SeccionDelBorrador> _seccionesBorrador = Array.Empty<SeccionDelBorrador>();
    private Dictionary<string, string> _borradores = new(StringComparer.Ordinal);
    private readonly HashSet<string> _borradoresPendientes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _borradoresAbiertos = new(StringComparer.Ordinal);
    private DispatcherTimer? _esperaDeBorradores;
    private string _borradoresDe = "";
    private TextBlock? _estadoDeBorradores;
    private Task _guardandoBorradores = Task.CompletedTask;

    /// <summary>
    /// Al empezar a grabar: trae las secciones de la plantilla congelada y lo que ya estuviera
    /// escrito (local y nube), y engancha los borradores a la consulta para que viajen al generar.
    /// </summary>
    private async Task PrepararBorradoresAsync(string encounterId)
    {
        if (encounterId.Length == 0) return;
        _borradoresDe = encounterId;
        _borradores = new(StringComparer.Ordinal);
        _borradoresPendientes.Clear();
        _borradoresAbiertos.Clear();
        _consulta.ConLoEscrito = SumarLoEscritoAsync;

        try
        {
            var enc = await _clinica.LeerEncounterAsync(encounterId);
            _seccionesBorrador = BorradoresDeSeccion.SeccionesDe(enc.PlantillaCongelada);
        }
        catch (Exception e)
        {
            // Sin las secciones no hay dónde escribir; la grabación sigue intacta.
            _seccionesBorrador = Array.Empty<SeccionDelBorrador>();
            LogBus.Log("borradores", $"no se pudieron leer las secciones de la plantilla: {e.GetType().Name}: {e.Message}");
        }
        if (_borradoresDe != encounterId) return;   // ya se paró o empezó otra

        var local = CopiaDeBorradores.Leer(encounterId);
        var remoto = await BorradoresDelMedico.LeerAsync(_sesion, encounterId);
        if (_borradoresDe != encounterId) return;
        _borradores = BorradoresDeSeccion.Combinar(local, remoto);
        LogBus.Log("borradores", $"{_seccionesBorrador.Count} sección(es) para escribir · {_borradores.Count} con texto");
        PintarBorradores();
    }

    /// <summary>Lo que <see cref="Consulta.ConLoEscrito"/> llama al terminar: guarda lo pendiente y arma el bloque.</summary>
    private async Task<string> SumarLoEscritoAsync(string dicho)
    {
        // Lo que se está escribiendo AHORA también cuenta: se lee de la pantalla, no del último guardado.
        await Dispatcher.InvokeAsync(() => { _esperaDeBorradores?.Stop(); PintarEstadoDeBorradores("Guardando…"); });
        await GuardarBorradoresPendientesAsync();
        int n = BorradoresDeSeccion.Contar(_borradores);
        if (n > 0) LogBus.Log("borradores", $"{n} sección(es) escritas viajan con la transcripción");
        return BorradoresDeSeccion.Bloque(dicho, _borradores, _seccionesBorrador);
    }

    /// <summary>Al terminar (con nota o sin ella): se esconde el panel y se borra la copia local.</summary>
    private void CerrarBorradores(bool notaLista)
    {
        _panelBorradores.Visibility = Visibility.Collapsed;
        _esperaDeBorradores?.Stop();
        // La copia local solo existe para que un cierre brusco no se lleve lo escrito. Con la nota
        // generada, lo escrito ya está en ella y en la nube: no se deja texto clínico en disco.
        if (notaLista && _borradoresDe.Length > 0) CopiaDeBorradores.Borrar(_borradoresDe);
    }

    private void PintarBorradores()
    {
        _panelBorradores.Children.Clear();
        if (_seccionesBorrador.Count == 0) { _panelBorradores.Visibility = Visibility.Collapsed; return; }

        var cabeza = new DockPanel { Margin = new Thickness(4, 4, 4, 8) };
        _estadoDeBorradores = new TextBlock
        {
            Foreground = Estudio.TintaTenue, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center,
            Text = _borradores.Count > 0 ? "Guardado · Se suma a la nota al generarla" : "Se suma a la nota al generarla",
        };
        DockPanel.SetDock(_estadoDeBorradores, Dock.Right);
        cabeza.Children.Add(_estadoDeBorradores);
        var rotulo = Estudio.Rotulo("Escribe mientras grabas");
        rotulo.VerticalAlignment = VerticalAlignment.Center;
        cabeza.Children.Add(rotulo);

        var lista = new StackPanel();
        for (int i = 0; i < _seccionesBorrador.Count; i++)
        {
            var fila = FilaDeBorrador(_seccionesBorrador[i]);
            if (i < _seccionesBorrador.Count - 1)
                lista.Children.Add(new Border { Child = fila, BorderBrush = Estudio.Borde, BorderThickness = new Thickness(0, 0, 0, 1) });
            else lista.Children.Add(fila);
        }

        var tarjeta = Estudio.Tarjeta(Estudio.RadioMedio);
        tarjeta.Padding = new Thickness(10, 6, 10, 6);
        tarjeta.Child = lista;

        _panelBorradores.Children.Add(cabeza);
        _panelBorradores.Children.Add(Estudio.Elevar(tarjeta));
        _panelBorradores.Margin = new Thickness(2, 0, 2, 10);
        _panelBorradores.Visibility = Visibility.Visible;
    }

    private UIElement FilaDeBorrador(SeccionDelBorrador s)
    {
        var pila = new StackPanel();
        string texto = _borradores.TryGetValue(s.Clave, out var t) ? t : "";
        bool abierta = _borradoresAbiertos.Contains(s.Clave);

        var titulo = new DockPanel();
        var chevron = Estudio.Icono(abierta ? "chevron-down" : "chevron-right", 14, Estudio.TintaTenue);
        chevron.Margin = new Thickness(0, 0, 8, 0);
        DockPanel.SetDock(chevron, Dock.Left);
        titulo.Children.Add(chevron);
        var nombre = new StackPanel();
        nombre.Children.Add(new TextBlock
        {
            Text = s.Titulo, Foreground = Estudio.TintaFuerte, FontSize = 13.5, FontWeight = FontWeights.SemiBold,
        });
        // PLEGADA CON TEXTO, su primer renglón (90 caracteres, como la web): se sabe qué hay sin abrirla.
        if (!abierta && texto.Trim().Length > 0)
        {
            string primero = texto.Trim().Split('\n')[0];
            nombre.Children.Add(new TextBlock
            {
                Text = primero.Length > 90 ? primero[..90] + "…" : primero,
                Foreground = Estudio.TintaMedia, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 1, 0, 0),
            });
        }
        titulo.Children.Add(nombre);

        var cabecera = new Border
        {
            Child = titulo, Padding = new Thickness(6, 8, 6, 8), Background = Brushes.Transparent,
            Cursor = Cursors.Hand, CornerRadius = new CornerRadius(Estudio.RadioChico),
        };
        cabecera.MouseEnter += (_, __) => cabecera.Background = Estudio.HieloSuave;
        cabecera.MouseLeave += (_, __) => cabecera.Background = Brushes.Transparent;
        // Atendido aquí: la ventana arrastra con este evento, y sin marcarlo la fila no se abriría.
        cabecera.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            if (!_borradoresAbiertos.Remove(s.Clave)) _borradoresAbiertos.Add(s.Clave);
            PintarBorradores();
        };
        pila.Children.Add(cabecera);
        if (!abierta) return pila;

        var caja = new TextBox
        {
            Text = texto,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 64,
            MaxHeight = 260,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontSize = 14,
            Padding = new Thickness(4, 2, 4, 2),
            Background = Brushes.Transparent,
            Foreground = Estudio.Tinta,
            BorderThickness = new Thickness(0),
            CaretBrush = Estudio.Acento,
            IsEnabled = _consulta.Estado == EstadoDeConsulta.Grabando,
        };
        EngancharAtajos(caja, s.Clave);
        caja.TextChanged += (_, __) => AlEscribirBorrador(s.Clave, caja.Text);
        pila.Children.Add(new Border
        {
            Child = caja,
            CornerRadius = new CornerRadius(Estudio.RadioChico),
            BorderBrush = Estudio.Borde,
            BorderThickness = new Thickness(1),
            Background = Estudio.Superficie,
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(28, 0, 4, 10),
        });
        Dispatcher.BeginInvoke(new Action(() => { caja.Focus(); caja.CaretIndex = caja.Text.Length; }), DispatcherPriority.Input);
        return pila;
    }

    /// <summary>
    /// Cada tecla: a la copia local AL MOMENTO (cifrada), y a la nube cuando se deja de escribir 900 ms.
    /// Guardar no puede ir por cada tecla, y lo escrito no se puede perder.
    /// </summary>
    private void AlEscribirBorrador(string clave, string texto)
    {
        _borradores[clave] = texto;
        _borradoresPendientes.Add(clave);
        CopiaDeBorradores.Guardar(_borradoresDe, _borradores);
        _esperaDeBorradores ??= CrearEsperaDeBorradores();
        _esperaDeBorradores.Stop();
        _esperaDeBorradores.Start();
    }

    private DispatcherTimer CrearEsperaDeBorradores()
    {
        var espera = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        espera.Tick += async (_, __) => { espera.Stop(); await GuardarBorradoresPendientesAsync(); };
        return espera;
    }

    /// <summary>
    /// Sube lo pendiente. Lo que no sube se queda pendiente y se reintenta en el siguiente guardado:
    /// la pantalla lo dice, pero no borra nada ni bloquea al médico.
    /// </summary>
    private Task GuardarBorradoresPendientesAsync()
    {
        // UNO A LA VEZ: el temporizador y el «terminar» pueden coincidir, y dos subidas cruzadas de la
        // misma sección podrían dejar en la nube la versión vieja.
        _guardandoBorradores = _guardandoBorradores.ContinueWith(_ => SubirAsync(), TaskScheduler.Default).Unwrap();
        return _guardandoBorradores;

        async Task SubirAsync()
        {
            string enc = _borradoresDe;
            List<(string Clave, string Texto)> pendientes = await Dispatcher.InvokeAsync(() =>
            {
                var l = _borradoresPendientes.Select(k => (k, _borradores.TryGetValue(k, out var t) ? t : "")).ToList();
                _borradoresPendientes.Clear();
                if (l.Count > 0) PintarEstadoDeBorradores("Guardando…");
                return l;
            });
            if (pendientes.Count == 0) return;

            var fallidas = new List<string>();
            foreach (var (clave, texto) in pendientes)
                if (!await BorradoresDelMedico.GuardarAsync(_sesion, enc, clave, texto)) fallidas.Add(clave);

            await Dispatcher.InvokeAsync(() =>
            {
                foreach (var f in fallidas) _borradoresPendientes.Add(f);
                PintarEstadoDeBorradores(fallidas.Count == 0
                    ? "Guardado · Se suma a la nota al generarla"
                    : "Sin conexión: está guardado en este equipo y se reintenta solo");
            });
        }
    }

    private void PintarEstadoDeBorradores(string texto)
    {
        if (_estadoDeBorradores != null) _estadoDeBorradores.Text = texto;
    }
}
