using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using U.WindowsClient.Clinical;
using U.WindowsClient.Cuenta;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Ui;

/// <summary>
/// LA NOTA VIVA (spec 055): lo que la nota de la web hace y la de Windows no hacía — los avisos al
/// terminar, ajustar escribiendo o en voz alta, los atajos «/», el paciente, copiar la nota y abrirla
/// en la web.
/// </summary>
/// <remarks>
/// EN SU PROPIO ARCHIVO, y no es orden por gusto: <c>ConsultaWindow.cs</c> pasa de 3.000 líneas y es
/// la zona de más choque del repo (dos personas no deben tener features abiertas en la UI a la vez).
/// Lo que aquí vive decide poco: la lógica está en clases que el contrato juzga sin pantalla
/// (<see cref="RevisionDeLaNota"/>, <see cref="AjusteDeLaNota"/>, <see cref="AtajosDeTexto"/>,
/// <see cref="PacientesDelMedico"/>, <see cref="TextoDeLaNota"/>), y esta parte las pinta.
///
/// LA EXPERIENCIA ES LA DE WINDOWS: todo ajuste —escrito, por voz o literal— llega como UNA propuesta
/// que se acepta con un gesto (Guardar o Ctrl+S) o se descarta. Es la regla de la web —la propuesta
/// no se guarda sola— con menos pasos.
/// </remarks>
public sealed partial class ConsultaWindow
{
    /// <summary>Los atajos del médico, leídos una vez al arrancar. Vacío si no se pudieron leer.</summary>
    private IReadOnlyList<Atajo> _atajos = Array.Empty<Atajo>();

    /// <summary>El modo de plantilla y los pines del médico (las mismas tablas que la web).</summary>
    private string _modoDePlantilla = PreferenciasDelMedico.ModoPorDefecto;
    private IReadOnlyList<Predeterminada> _pines = Array.Empty<Predeterminada>();

    /// <summary>Lo que los avisos necesitan de la nota que se ve: su plantilla congelada y lo que se habló.</summary>
    private JsonElement _contextoPlantilla;
    private string _contextoTranscripcion = "";
    /// <summary>De qué encounter es el contexto cargado, para no pedirlo dos veces.</summary>
    private string _contextoDe = "";

    /// <summary>El paciente de la nota que se ve, o nulo.</summary>
    private Paciente? _paciente;

    /// <summary>Un ajuste que el médico todavía no aceptó. Mientras exista, la nota no se edita.</summary>
    private Propuesta? _propuesta;

    /// <summary>La sección a la que se le está dictando un cambio ("" = la barra de ajuste), o nulo.</summary>
    private string? _dictandoA;

    /// <summary>El texto de la barra de ajuste, para no perder lo escrito al repintar.</summary>
    private string _borradorDeAjuste = "";

    // ── arranque ─────────────────────────────────────────────────────────────

    /// <summary>Lo que se lee una vez al abrir: atajos y preferencias. Nada de esto bloquea grabar.</summary>
    private async Task CargarLoDelMedicoAsync()
    {
        var atajos = AtajosDelMedico.LeerAsync(_sesion);
        var prefs = PreferenciasDelMedico.LeerAsync(_sesion);
        _atajos = await atajos;
        (_modoDePlantilla, _pines) = await prefs;
    }

    /// <summary>El encounter de la nota que se ve: el de una consulta abierta, o el de la de ahora.</summary>
    private string EncounterEnPantalla() => _abiertaId.Length > 0 ? _abiertaId : _consulta.EncounterId;

    /// <summary>
    /// Trae lo que los avisos necesitan de la consulta de AHORA: la plantilla congelada (de ahí salen
    /// las obligatorias) y el paciente. Una vez por encounter, en segundo plano: la nota ya se ve.
    /// </summary>
    private async Task CargarContextoDeLaNotaAsync()
    {
        string id = _consulta.EncounterId;
        if (id.Length == 0 || _contextoDe == id) return;
        _contextoDe = id;
        _contextoTranscripcion = _consulta.Verbatim ?? "";
        try
        {
            var enc = await _clinica.LeerEncounterAsync(id);
            _contextoPlantilla = enc.PlantillaCongelada;
            if (enc.Transcripcion.Length > 0) _contextoTranscripcion = enc.Transcripcion;
            _paciente = await PacientesDelMedico.LeerAsync(_sesion, enc.PacienteId);
        }
        catch (Exception e)
        {
            // Sin plantilla congelada los avisos siguen saliendo: sin las obligatorias de la plantilla,
            // pero con las del backend. Se dice en el log, no se inventa.
            LogBus.Log("consulta-ui", $"contexto de la nota: {e.GetType().Name}: {e.Message}");
        }
        // Solo si se sigue viendo la de ahora: el médico pudo abrir otra mientras esto llegaba.
        if (_abiertaId.Length == 0 && _consulta.Nota != null && _consulta.EncounterId == id) RepintarLaNota();
    }

    // ── la cabecera de la nota ───────────────────────────────────────────────

    /// <summary>«Nota clínica», su estado en el portal, y copiar / abrir en la web.</summary>
    private UIElement CabeceraDeLaNota(NotaClinica nota, string estado)
    {
        var fila = new DockPanel { Margin = new Thickness(2, 2, 2, 12) };

        var acciones = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var copiar = Estudio.BotonSecundario("Copiar nota", "clipboard-copy", 34);
        copiar.Click += (_, e) =>
        {
            e.Handled = true;
            try
            {
                Clipboard.SetText(TextoDeLaNota.DeLaNota(nota));
                Estado("Nota copiada, con el mismo formato que la web.");
            }
            catch (Exception ex) { Estado($"No se pudo copiar: {ex.Message}"); }
        };
        acciones.Children.Add(copiar);

        string id = EncounterEnPantalla();
        if (id.Length > 0)
        {
            var web = Estudio.BotonIcono("external-link", "Abrir en Miracle web", 34, 16);
            web.Margin = new Thickness(6, 0, 0, 0);
            web.Click += (_, e) =>
            {
                e.Handled = true;
                string url = $"{Nube.PortalUrl}/app/consultas/{Uri.EscapeDataString(id)}";
                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                catch (Exception ex) { Estado($"No se pudo abrir el navegador: {ex.Message}"); }
            };
            acciones.Children.Add(web);
        }
        DockPanel.SetDock(acciones, Dock.Right);
        fila.Children.Add(acciones);

        var titulo = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        titulo.Children.Add(Estudio.Titulo("Nota clínica", 18));
        var chip = Estudio.ChipDeEstado(estado.Length > 0 ? estado : "borrador");
        chip.Margin = new Thickness(10, 1, 0, 0);
        titulo.Children.Add(chip);
        fila.Children.Add(titulo);
        return fila;
    }

    // ── el paciente ──────────────────────────────────────────────────────────

    private readonly Popup _menuPaciente = new()
    {
        StaysOpen = false,
        AllowsTransparency = true,
        PopupAnimation = PopupAnimation.Fade,
        Placement = PlacementMode.Bottom,
    };

    /// <summary>
    /// De quién es la nota. Sin paciente, un botón para asociarlo; con él, su nombre y —a la vista,
    /// como en la web— sus alergias, antecedentes y medicamentos.
    /// </summary>
    private UIElement FilaDelPaciente(bool sePuedeCambiar)
    {
        var tarjeta = Estudio.Tarjeta(Estudio.RadioMedio);
        tarjeta.Padding = new Thickness(14, 10, 10, 10);
        tarjeta.Margin = new Thickness(2, 0, 2, 12);

        var fila = new DockPanel();
        var icono = Estudio.Icono("user-round", 17, Estudio.Acento);
        icono.Margin = new Thickness(0, 0, 10, 0);
        icono.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(icono, Dock.Left);
        fila.Children.Add(icono);

        if (sePuedeCambiar && EncounterEnPantalla().Length > 0)
        {
            var boton = _paciente == null
                ? Estudio.BotonTerciario("Asociar paciente", "user-plus", 30)
                : Estudio.BotonTerciario("Cambiar", null, 30);
            boton.VerticalAlignment = VerticalAlignment.Top;
            boton.Click += (_, e) => { e.Handled = true; AbrirBuscadorDePaciente(boton); };
            DockPanel.SetDock(boton, Dock.Right);
            fila.Children.Add(boton);
        }

        var textos = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        if (_paciente == null)
        {
            textos.Children.Add(new TextBlock
            {
                Text = "Sin paciente asociado", Foreground = Estudio.TintaMedia, FontSize = 13.5,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 0, 0),
            });
        }
        else
        {
            var p = _paciente;
            textos.Children.Add(new TextBlock
            {
                Text = p.Nombre, Foreground = Estudio.TintaFuerte, FontSize = 14.5, FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            string meta = string.Join(" · ", new[] { p.Documento, p.Edad.Length > 0 ? $"{p.Edad} años" : "", p.Eps }.Where(x => x.Length > 0));
            if (meta.Length > 0)
                textos.Children.Add(new TextBlock { Text = meta, Foreground = Estudio.TintaMedia, FontSize = 12.5, Margin = new Thickness(0, 1, 0, 0) });
            // LAS ALERGIAS EN ÁMBAR: son lo único de la ficha que puede cambiar lo que se prescribe.
            if (p.Alergias.Count > 0)
                textos.Children.Add(new TextBlock
                {
                    Text = "Alergias: " + string.Join(", ", p.Alergias), Foreground = Estudio.EsperaTinta,
                    FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0),
                });
            foreach (var (rotulo, lista) in new[] { ("Antecedentes", p.Antecedentes), ("Medicamentos", p.Medicamentos) })
                if (lista.Count > 0)
                    textos.Children.Add(new TextBlock
                    {
                        Text = $"{rotulo}: {string.Join(", ", lista)}", Foreground = Estudio.TintaMedia, FontSize = 12,
                        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0),
                    });
        }
        fila.Children.Add(textos);
        tarjeta.Child = fila;
        return Estudio.Elevar(tarjeta, Estudio.Sombra1);
    }

    /// <summary>El buscador de pacientes: por nombre o documento, en la tabla de la web.</summary>
    private void AbrirBuscadorDePaciente(UIElement ancla)
    {
        var caja = new TextBox
        {
            FontSize = 13.5,
            Padding = new Thickness(10, 7, 10, 7),
            Background = Estudio.SuperficieSuave,
            Foreground = Estudio.Tinta,
            BorderThickness = new Thickness(0),
            CaretBrush = Estudio.Acento,
        };
        var ayuda = new TextBlock
        {
            Text = "Escribe el nombre o el documento.", Foreground = Estudio.TintaTenue, FontSize = 12,
            Margin = new Thickness(4, 8, 4, 2), TextWrapping = TextWrapping.Wrap,
        };
        var resultados = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        var pila = new StackPanel { Width = 340 };
        pila.Children.Add(caja);
        pila.Children.Add(ayuda);
        pila.Children.Add(resultados);

        var espera = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        int turno = 0;
        espera.Tick += async (_, __) =>
        {
            espera.Stop();
            int este = ++turno;
            var encontrados = await PacientesDelMedico.BuscarAsync(_sesion, caja.Text);
            if (este != turno) return;   // llegó tarde: ya se escribió otra cosa
            resultados.Children.Clear();
            ayuda.Text = caja.Text.Trim().Length == 0 ? "Escribe el nombre o el documento."
                       : encontrados.Count == 0 ? "No hay pacientes con eso. Créalo en la web y vuelve aquí."
                       : "";
            ayuda.Visibility = ayuda.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var p in encontrados) resultados.Children.Add(FilaDePaciente(p));
        };
        caja.TextChanged += (_, __) => { espera.Stop(); espera.Start(); };

        var tarjeta = Estudio.Tarjeta(Estudio.RadioMedio);
        tarjeta.Padding = new Thickness(12);
        tarjeta.Child = pila;
        _menuPaciente.SetValue(System.Windows.Documents.TextElement.FontFamilyProperty, Estudio.FuenteCuerpo);
        _menuPaciente.PlacementTarget = ancla;
        _menuPaciente.Child = Estudio.Elevar(tarjeta, Estudio.Sombra3);
        _menuPaciente.IsOpen = true;
        Dispatcher.BeginInvoke(new Action(() => caja.Focus()), System.Windows.Threading.DispatcherPriority.Input);
    }

    private UIElement FilaDePaciente(Paciente p)
    {
        var pila = new StackPanel();
        pila.Children.Add(new TextBlock { Text = p.Nombre, Foreground = Estudio.TintaFuerte, FontSize = 13.5, FontWeight = FontWeights.SemiBold });
        string meta = string.Join(" · ", new[] { p.Documento, p.Edad.Length > 0 ? $"{p.Edad} años" : "" }.Where(x => x.Length > 0));
        if (meta.Length > 0) pila.Children.Add(new TextBlock { Text = meta, Foreground = Estudio.TintaMedia, FontSize = 12 });
        var fila = new Button
        {
            Content = pila,
            Padding = new Thickness(10, 7, 10, 7),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Template = Estudio.Pastilla(Estudio.RadioChico, estirado: true),
        };
        fila.MouseEnter += (_, __) => fila.Background = Estudio.HieloSuave;
        fila.MouseLeave += (_, __) => fila.Background = Brushes.Transparent;
        fila.Click += async (_, __) => await AsociarPacienteAsync(p);
        return fila;
    }

    /// <summary>
    /// Asocia el paciente al encounter (como la web) y al espejo del portal, para que la lista de la
    /// web diga de quién es sin esperar a la siguiente corrección.
    /// </summary>
    private async Task AsociarPacienteAsync(Paciente p)
    {
        _menuPaciente.IsOpen = false;
        string id = EncounterEnPantalla();
        if (id.Length == 0) return;
        Estado("Asociando el paciente…");
        try
        {
            await _clinica.AsociarPacienteAsync(id, p.Id);
            var fila = new System.Text.Json.Nodes.JsonObject { ["id"] = id, ["patient_id"] = p.Id }.ToJsonString();
            bool enElPortal = await EspejoDeConsulta.EscribirAsync(_sesion, _http, fila);
            _paciente = p;
            Estado(enElPortal ? $"Consulta asociada a {p.Nombre}." : $"Asociada a {p.Nombre}; el portal lo verá en la siguiente corrección.");
            RepintarLaNota();
        }
        catch (ErrorClinico e) { Estado(e.Message); }
        catch (Exception e) { Estado($"No se pudo asociar el paciente: {e.Message}"); }
    }

    // ── los avisos ───────────────────────────────────────────────────────────

    /// <summary>
    /// «Antes de guardar — 1 crítico · 2 advertencias»: los avisos de la web, con sus mismas palabras,
    /// lo que se ve de entrada y lo que se pliega. Verde cuando no hay nada.
    /// </summary>
    private UIElement PanelDeAvisos(NotaClinica nota)
    {
        JsonElement crudo;
        if (nota.Crudo.ValueKind == JsonValueKind.Object) crudo = nota.Crudo;
        else
        {
            using var doc = JsonDocument.Parse(nota.ComoNodo().ToJsonString());
            crudo = doc.RootElement.Clone();
        }
        var revision = RevisionDeLaNota.Revisar(crudo, _contextoPlantilla, _contextoTranscripcion);

        if (revision.Hallazgos.Count == 0)
        {
            var ok = Estudio.Aviso("ok", "Revisión de la nota: sin observaciones. Está completa y consistente.");
            ok.Margin = new Thickness(2, 0, 2, 12);
            return ok;
        }

        bool critico = revision.Criticos > 0;
        var reparto = RevisionDeLaNota.Repartir(revision);
        var caja = Estudio.Aviso(critico ? "alerta" : "espera", $"Antes de guardar — {RevisionDeLaNota.Etiqueta(revision)}",
            "Esto es lo que quedó pendiente o se puede mejorar de la consulta.");
        caja.Margin = new Thickness(2, 0, 2, 12);

        // Sobre fondo blanco propio, como en la web: los chips de severidad pierden contraste sobre ámbar.
        var lista = new StackPanel();
        foreach (var h in reparto.Principales) lista.Children.Add(FilaDeHallazgo(h));
        if (reparto.Plegados.Count > 0)
        {
            var plegados = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };
            foreach (var h in reparto.Plegados) plegados.Children.Add(FilaDeHallazgo(h));
            int n = reparto.Plegados.Count;
            var ver = Estudio.BotonTerciario($"Ver {n} {(n == 1 ? "observación" : "observaciones")} más", "chevron-down", 28);
            ver.HorizontalAlignment = HorizontalAlignment.Left;
            ver.Margin = new Thickness(-8, 2, 0, 0);
            ver.Click += (_, e) => { e.Handled = true; plegados.Visibility = Visibility.Visible; ver.Visibility = Visibility.Collapsed; };
            lista.Children.Add(ver);
            lista.Children.Add(plegados);
        }
        var blanco = new Border
        {
            CornerRadius = new CornerRadius(Estudio.RadioChico),
            Background = Estudio.Superficie,
            BorderBrush = Estudio.Borde,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 10, 0, 0),
            Child = lista,
        };
        if (caja.Child is DockPanel dentro && dentro.Children.Count > 1 && dentro.Children[1] is StackPanel textos)
            textos.Children.Add(blanco);
        return caja;
    }

    /// <summary>Un hallazgo: su baldosa de severidad (la de `AuditFindings` de la web), el título y el detalle.</summary>
    private static UIElement FilaDeHallazgo(Hallazgo h)
    {
        var (fondo, tinta, icono) = h.Severidad switch
        {
            "critico" => (Estudio.AlertaSuave, Estudio.Alerta, "triangle-alert"),
            "advertencia" => (Estudio.EsperaSuave, Estudio.Espera, "circle-alert"),
            _ => (Estudio.AcentoSuave, Estudio.Acento, "lightbulb"),
        };
        var fila = new DockPanel { Margin = new Thickness(0, 4, 0, 6) };
        var baldosa = new Border
        {
            Width = 24, Height = 24, CornerRadius = new CornerRadius(8), Background = fondo,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 10, 0),
            Child = Estudio.Icono(icono, 14, tinta),
        };
        DockPanel.SetDock(baldosa, Dock.Left);
        fila.Children.Add(baldosa);
        var textos = new StackPanel();
        textos.Children.Add(new TextBlock
        {
            Text = h.Titulo, Foreground = Estudio.TintaFuerte, FontSize = 13.5, FontWeight = FontWeights.Medium,
            TextWrapping = TextWrapping.Wrap,
        });
        textos.Children.Add(new TextBlock
        {
            Text = h.Detalle, Foreground = Estudio.TintaMedia, FontSize = 12, LineHeight = 17,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0),
        });
        fila.Children.Add(textos);
        return fila;
    }

    // ── ajustar escribiendo ─────────────────────────────────────────────────

    /// <summary>
    /// La barra de la web («Pide un ajuste a la nota…»): escribir y Enter, o dictar la instrucción con
    /// el micrófono y revisarla antes de mandarla.
    /// </summary>
    private UIElement BarraDeAjuste()
    {
        var caja = new TextBox
        {
            Text = _borradorDeAjuste,
            FontSize = 13.5,
            Background = Brushes.Transparent,
            Foreground = Estudio.Tinta,
            BorderThickness = new Thickness(0),
            CaretBrush = Estudio.Acento,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 6, 0),
        };
        var pista = new TextBlock
        {
            Text = "Pide un ajuste a la nota… (p. ej. «hazla más breve»)",
            Foreground = Estudio.TintaTenue, FontSize = 13.5, IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0),
            Visibility = _borradorDeAjuste.Length == 0 ? Visibility.Visible : Visibility.Collapsed,
        };
        caja.TextChanged += (_, __) =>
        {
            _borradorDeAjuste = caja.Text;
            pista.Visibility = caja.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        };

        var enviar = new Button
        {
            Width = 32, Height = 32, Background = Estudio.AcentoDegradado, BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand, Template = Estudio.Pastilla(16), Content = Estudio.Icono("send", 15, Brushes.White),
        };
        System.Windows.Automation.AutomationProperties.SetName(enviar, "Pedir el ajuste");
        async Task Mandar()
        {
            string texto = caja.Text.Trim();
            if (texto.Length == 0) return;
            enviar.IsEnabled = false;
            await AjustarAsync(texto, seccion: null, tipo: "rewrite", titulo: "la nota");
            enviar.IsEnabled = true;
        }
        enviar.Click += async (_, e) => { e.Handled = true; await Mandar(); };
        caja.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await Mandar(); } };

        var micro = Estudio.BotonIcono("mic", "Dictar la instrucción", 32, 15);
        micro.Margin = new Thickness(0, 0, 4, 0);
        micro.Click += async (_, e) =>
        {
            e.Handled = true;
            // DICTAR LA INSTRUCCIÓN, NO MANDARLA: lo oído va a la barra y el médico lo lee antes de
            // Enter. Una instrucción sobre la nota entera mal oída cambiaría demasiado de golpe.
            string? oido = await DictarAsync("", micro);
            if (oido != null) { caja.Text = oido; caja.CaretIndex = caja.Text.Length; caja.Focus(); }
        };

        var chispa = Estudio.Icono("sparkles", 16, Estudio.Acento);
        var fila = new DockPanel();
        DockPanel.SetDock(chispa, Dock.Left);
        fila.Children.Add(chispa);
        DockPanel.SetDock(enviar, Dock.Right);
        fila.Children.Add(enviar);
        DockPanel.SetDock(micro, Dock.Right);
        fila.Children.Add(micro);
        var campo = new Grid();
        campo.Children.Add(caja);
        campo.Children.Add(pista);
        fila.Children.Add(campo);

        var pastilla = new Border
        {
            CornerRadius = new CornerRadius(999),
            Background = Estudio.Superficie,
            BorderBrush = Estudio.Borde,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 6, 6, 6),
            Child = fila,
            Margin = new Thickness(2, 0, 2, 12),
        };
        return Estudio.Elevar(pastilla, Estudio.Sombra1);
    }

    /// <summary>
    /// Pide el ajuste al asistente y deja la respuesta como PROPUESTA. Si no cambió nada, se dice por
    /// qué, con la salida de la web: dictarlo como literal.
    /// </summary>
    private async Task AjustarAsync(string instruccion, string? seccion, string tipo, string titulo)
    {
        string id = EncounterEnPantalla();
        if (id.Length == 0 || _notaEnPantalla == null) { Estado("No hay nota que ajustar."); return; }
        if (_propuesta != null) { Estado("Primero guarda o descarta el ajuste propuesto."); return; }
        Estado($"Ajustando {titulo}…");
        try
        {
            var respuesta = await _clinica.AjustarNotaAsync(id, instruccion, seccion, tipo);
            var propuesta = AjusteDeLaNota.Aplicar(_notaEnPantalla, respuesta);
            if (propuesta.Cambiadas.Count == 0) { Estado(propuesta.Explicacion); return; }
            _borradorDeAjuste = "";
            MostrarPropuesta(propuesta);
        }
        catch (ErrorClinico e) { Estado(e.Message); }
        catch (Exception e) { Estado($"No se pudo ajustar la nota: {e.Message}"); }
    }

    // ── ajustar en voz alta ─────────────────────────────────────────────────

    /// <summary>
    /// El micrófono de una sección: la primera pulsación escucha, la segunda para y decide —literal
    /// (aquí mismo, sin red), dictado o ajuste— como la web.
    /// </summary>
    private async Task DictarCambioAsync(SeccionDeNota s, Button micro)
    {
        if (_propuesta != null) { Estado("Primero guarda o descarta el ajuste propuesto."); return; }
        string? dicho = await DictarAsync(s.Clave, micro);
        if (dicho == null || _notaEnPantalla == null) return;

        var pedido = AjusteDeLaNota.PorVoz(dicho, s.Clave, s.Titulo, s.Contenido);
        if (pedido == null) { Estado("No se oyó nada. Vuelve a pulsar el micrófono y habla."); return; }

        if (pedido.TextoLocal != null)
        {
            MostrarPropuesta(AjusteDeLaNota.Literal(_notaEnPantalla, s.Clave, s.Titulo, pedido.TextoLocal));
            return;
        }
        await AjustarAsync(pedido.Instruccion, pedido.Seccion, pedido.Tipo, $"«{s.Titulo}»");
    }

    /// <summary>
    /// Escucha con el mismo dictado de la consulta y devuelve lo oído al pulsar otra vez. Nulo si no
    /// se oyó nada, si ya se estaba escuchando otra cosa o si hay una consulta grabándose.
    /// </summary>
    private async Task<string?> DictarAsync(string para, Button micro)
    {
        if (_consulta.Estado == EstadoDeConsulta.Grabando)
        {
            Estado("Termina la consulta antes de dictar un cambio.");
            return null;
        }
        if (_dictandoA != null && _dictandoA != para) { Estado("Ya estoy escuchando otro cambio: púlsalo para terminar."); return null; }

        if (_dictandoA == para)
        {
            // SEGUNDA PULSACIÓN: parar y recoger. El Parcial deja de pintar la nota en cuanto se suelta.
            string oido = (await _dictado.PararAsync() ?? "").Trim();
            _dictandoA = null;
            PintarMicroDeDictado(micro, false);
            if (oido.Length == 0) { Estado("No se oyó nada."); return null; }
            LogBus.Log("consulta-ui", $"cambio dictado · {oido.Length} caracteres");
            return oido;
        }

        _dictandoA = para;
        PintarMicroDeDictado(micro, true);
        Estado("Te escucho… pulsa el micrófono otra vez al terminar.");
        if (!await _dictado.ArrancarAsync(CancellationToken.None))
        {
            _dictandoA = null;
            PintarMicroDeDictado(micro, false);
            Estado("No se pudo abrir el micrófono para dictar el cambio.");
        }
        return null;
    }

    private static void PintarMicroDeDictado(Button micro, bool escuchando)
    {
        micro.Background = escuchando ? Estudio.AlertaSuave : Brushes.Transparent;
        if (micro.Content is FrameworkElement dibujo) Estudio.Colorear(dibujo, escuchando ? Estudio.Alerta : Estudio.TintaTenue);
    }

    // ── la propuesta ─────────────────────────────────────────────────────────

    private void MostrarPropuesta(Propuesta propuesta)
    {
        _propuesta = propuesta;
        RepintarLaNota();
        Estado(propuesta.Explicacion + " Guarda con Ctrl+S o descarta.");
    }

    /// <summary>«Ajuste propuesto en N secciones» con Guardar (Ctrl+S) y Descartar.</summary>
    private UIElement BandaDePropuesta(Propuesta p)
    {
        int n = p.Cambiadas.Count;
        var textos = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        textos.Children.Add(new TextBlock
        {
            Text = $"Ajuste propuesto en {n} {(n == 1 ? "sección" : "secciones")}",
            Foreground = Estudio.AcentoTinta, FontSize = 13.5, FontWeight = FontWeights.SemiBold,
        });
        textos.Children.Add(new TextBlock
        {
            Text = p.Explicacion + " Revísalo: no se guarda hasta que lo apruebes.",
            Foreground = Estudio.AcentoTinta, Opacity = 0.9, FontSize = 12.5, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0),
        });

        var guardar = Estudio.BotonPrimario("Guardar", "check", 34);
        guardar.Click += async (_, e) => { e.Handled = true; await GuardarPropuestaAsync(); };
        var descartar = Estudio.BotonSecundario("Descartar", null, 34);
        descartar.Margin = new Thickness(8, 0, 0, 0);
        descartar.Click += (_, e) => { e.Handled = true; DescartarPropuesta(); };
        var botones = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        botones.Children.Add(guardar);
        botones.Children.Add(descartar);

        var fila = new DockPanel();
        var chispa = Estudio.Icono("sparkles", 18, Estudio.Acento);
        chispa.Margin = new Thickness(0, 0, 10, 0);
        DockPanel.SetDock(chispa, Dock.Left);
        fila.Children.Add(chispa);
        DockPanel.SetDock(botones, Dock.Right);
        fila.Children.Add(botones);
        fila.Children.Add(textos);

        return new Border
        {
            CornerRadius = new CornerRadius(Estudio.RadioMedio),
            Background = Estudio.AcentoSuave,
            BorderBrush = Estudio.Acento,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 10, 10, 10),
            Margin = new Thickness(2, 0, 2, 12),
            Child = fila,
        };
    }

    /// <summary>¿Esta sección (o el resumen, clave vacía) la cambió la propuesta?</summary>
    private bool LaCambioLaPropuesta(string clave) =>
        _propuesta != null && _propuesta.Cambiadas.Contains(clave.Length > 0 ? clave : "summary");

    private async Task GuardarPropuestaAsync()
    {
        if (_propuesta == null) return;
        var nota = _propuesta.Nota;
        Estado("Guardando el ajuste…");
        bool ok;
        if (_abiertaId.Length > 0)
        {
            try
            {
                var guardada = await _clinica.GuardarNotaEditadaAsync(_abiertaId, nota);
                _abiertaNota = guardada;
                _notaEnPantalla = guardada;
                await EspejoDeConsulta.EscribirAsync(_sesion, _http, EspejoDeConsulta.FilaDeCorreccion(_abiertaId, guardada));
                ok = true;
            }
            catch (ErrorClinico e) { Estado(e.Message); ok = false; }
            catch (Exception e) { Estado($"No se pudo guardar el ajuste: {e.Message}"); ok = false; }
        }
        else
        {
            ok = await _consulta.GuardarNotaAsync(nota);
            if (!ok) Estado(_consulta.Motivo);
        }
        if (!ok) return;   // la propuesta se queda: nada de lo que el médico aprobó se pierde
        _propuesta = null;
        RepintarLaNota();
        Estado("Ajuste guardado. El portal ya ve el cambio.");
    }

    private void DescartarPropuesta()
    {
        _propuesta = null;
        RepintarLaNota();
        Estado("Ajuste descartado: la nota sigue como estaba.");
    }

    /// <summary>Repinta la nota que se ve, sea la de ahora o una abierta desde la lista.</summary>
    /// <remarks>
    /// SIN MOVER EL SCROLL: repintar tras guardar una sección de abajo no puede devolver al médico
    /// arriba del todo — perdería el sitio en cada corrección.
    /// </remarks>
    private void RepintarLaNota()
    {
        double donde = _superficie.VerticalOffset;
        if (_abiertaId.Length > 0 && _abiertaNota != null) PintarLaAbierta();
        else if (_consulta.Nota != null) PintarNota(anunciar: false);
        else return;
        Dispatcher.BeginInvoke(new Action(() => _superficie.ScrollToVerticalOffset(donde)),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // ── los atajos «/» ───────────────────────────────────────────────────────

    private readonly Popup _menuAtajos = new()
    {
        StaysOpen = true,
        AllowsTransparency = true,
        Placement = PlacementMode.Bottom,
    };

    /// <summary>
    /// Engancha los atajos del médico a un editor de sección: «/» al empezar palabra abre la lista,
    /// flechas y Enter (o Tab) insertan, y Tab salta de hueco en hueco. Lo insertado es texto normal:
    /// se edita como cualquier otro (lo pidió el dueño el 2026-09-26).
    /// </summary>
    private void EngancharAtajos(TextBox caja, string seccion)
    {
        _menuAtajos.SetValue(System.Windows.Documents.TextElement.FontFamilyProperty, Estudio.FuenteCuerpo);
        var lista = new StackPanel { Width = 360 };
        var tarjetaDelMenu = Estudio.Tarjeta(Estudio.RadioMedio);
        tarjetaDelMenu.Padding = new Thickness(6);
        tarjetaDelMenu.Child = lista;
        var elevado = Estudio.Elevar(tarjetaDelMenu, Estudio.Sombra3);
        int seleccion = 0;
        IReadOnlyList<Atajo> actuales = Array.Empty<Atajo>();
        Barra? barra = null;

        void Cerrar() { if (_menuAtajos.PlacementTarget == caja) _menuAtajos.IsOpen = false; }

        void Pintar()
        {
            lista.Children.Clear();
            for (int i = 0; i < actuales.Count; i++)
            {
                var a = actuales[i];
                int indice = i;
                var pila = new StackPanel();
                var cabeza = new DockPanel();
                if (a.Categoria.Length > 0)
                {
                    var chip = Estudio.Chip(a.Categoria, Estudio.Hielo, Estudio.AcentoTinta);
                    DockPanel.SetDock(chip, Dock.Right);
                    cabeza.Children.Add(chip);
                }
                cabeza.Children.Add(new TextBlock
                {
                    Text = a.Titulo, Foreground = Estudio.TintaFuerte, FontSize = 13, FontWeight = FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
                });
                pila.Children.Add(cabeza);
                pila.Children.Add(new TextBlock
                {
                    Text = a.Contenido.Replace('\n', ' '), Foreground = Estudio.TintaMedia, FontSize = 12,
                    TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0),
                });
                var fila = new Border
                {
                    Child = pila, Padding = new Thickness(10, 7, 10, 7), CornerRadius = new CornerRadius(10),
                    Background = i == seleccion ? Estudio.AcentoSuave : Brushes.Transparent, Cursor = Cursors.Hand,
                };
                fila.MouseLeftButtonDown += (_, e) => { e.Handled = true; Insertar(actuales[indice]); };
                lista.Children.Add(fila);
            }
        }

        void Evaluar()
        {
            if (_atajos.Count == 0) { Cerrar(); return; }
            barra = AtajosDeTexto.BarraEn(caja.Text, caja.CaretIndex);
            if (barra == null) { Cerrar(); return; }
            actuales = AtajosDeTexto.Filtrar(_atajos, barra.Consulta, seccion).Take(6).ToList();
            if (actuales.Count == 0) { Cerrar(); return; }
            seleccion = Math.Min(seleccion, actuales.Count - 1);
            Pintar();
            // DEBAJO DEL CURSOR, no debajo de la caja: en una sección de doce renglones el menú
            // aparecería lejos de donde se está escribiendo.
            var cursor = caja.GetRectFromCharacterIndex(caja.CaretIndex);
            _menuAtajos.PlacementTarget = caja;
            _menuAtajos.PlacementRectangle = cursor.IsEmpty ? Rect.Empty : cursor;
            if (_menuAtajos.Child != elevado) _menuAtajos.Child = elevado;
            _menuAtajos.IsOpen = true;
        }

        void Insertar(Atajo a)
        {
            if (barra == null) return;
            var r = AtajosDeTexto.Insertar(caja.Text, barra.Inicio, caja.CaretIndex, a.Contenido);
            caja.Text = r.Texto;
            var hueco = AtajosDeTexto.PrimerHuecoEn(r.Texto, r.SelInicio, r.SelFin);
            if (hueco != null) caja.Select(hueco.Inicio, hueco.Fin - hueco.Inicio);
            else caja.CaretIndex = r.SelFin;
            Cerrar();
            caja.Focus();
            LogBus.Log("atajos", "atajo insertado en la nota");
        }

        caja.TextChanged += (_, __) => Evaluar();
        caja.LostKeyboardFocus += (_, __) => Cerrar();
        caja.PreviewKeyDown += (_, e) =>
        {
            bool abierto = _menuAtajos.IsOpen && _menuAtajos.PlacementTarget == caja && actuales.Count > 0;
            if (abierto)
            {
                switch (e.Key)
                {
                    case Key.Down: seleccion = (seleccion + 1) % actuales.Count; Pintar(); e.Handled = true; return;
                    case Key.Up: seleccion = (seleccion - 1 + actuales.Count) % actuales.Count; Pintar(); e.Handled = true; return;
                    case Key.Enter:
                    case Key.Tab: Insertar(actuales[seleccion]); e.Handled = true; return;
                    case Key.Escape: Cerrar(); e.Handled = true; return;
                }
            }
            // TAB SALTA AL SIGUIENTE HUECO si lo hay; si no, Tab sigue siendo Tab.
            if (e.Key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None)
            {
                var h = AtajosDeTexto.SiguienteHueco(caja.Text, caja.SelectionStart + caja.SelectionLength);
                if (h != null) { caja.Select(h.Inicio, h.Fin - h.Inicio); e.Handled = true; }
            }
        };
    }
}
