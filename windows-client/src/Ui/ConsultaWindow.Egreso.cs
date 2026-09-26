using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using U.WindowsClient.Clinical;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Ui;

/// <summary>
/// EL PLAN Y EL EGRESO en la nota de U (spec 058): lo que el médico receta y lo que el paciente se
/// lleva. Hasta hoy viajaba intacto en cada guardado pero no se veía: no había forma de corregir una
/// dosis sin irse a la web.
/// </summary>
/// <remarks>
/// UN CAMPO POR DATO, y es la corrección del bug de la web: allí «Dosis y vía» era un solo campo que
/// borraba la vía al corregir la dosis. Aquí cada campo escribe SOLO su dato
/// (<see cref="EgresoDeLaNota.CambiarMedicamento"/>, promesa 473).
///
/// SE GUARDA AL SALIR DEL CAMPO y NO se repinta la nota entera: repintar le quitaría el foco al campo
/// siguiente al que el médico acaba de saltar con Tab. Agregar y quitar sí repintan (cambia la forma).
/// </remarks>
public sealed partial class ConsultaWindow
{
    private Task<bool> _guardandoNota = Task.FromResult(true);

    /// <summary>
    /// Guarda la nota ENTERA —la de ahora o una abierta de la lista— y la deja como la nota que se ve.
    /// Uno a la vez: dos guardados cruzados podrían dejar en el backend la versión vieja.
    /// </summary>
    private Task<bool> GuardarNotaEnteraAsync(NotaClinica nota)
    {
        _guardandoNota = _guardandoNota.ContinueWith(async _ => await GuardarAhoraAsync(nota),
            CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.FromCurrentSynchronizationContext()).Unwrap();
        return _guardandoNota;
    }

    private async Task<bool> GuardarAhoraAsync(NotaClinica nota)
    {
        if (_abiertaId.Length > 0)
        {
            try
            {
                var guardada = await _clinica.GuardarNotaEditadaAsync(_abiertaId, nota);
                _abiertaNota = guardada;
                _notaEnPantalla = guardada;
                await EspejoDeConsulta.EscribirAsync(_sesion, _http, EspejoDeConsulta.FilaDeCorreccion(_abiertaId, guardada));
                return true;
            }
            catch (ErrorClinico e) { Estado(e.Message); return false; }
            catch (Exception e) { Estado($"No se pudo guardar: {e.Message}"); return false; }
        }
        if (!await _consulta.GuardarNotaAsync(nota)) { Estado(_consulta.Motivo); return false; }
        _notaEnPantalla = _consulta.Nota;
        return true;
    }

    /// <summary>El bloque «Plan y egreso», bajo el papel de la nota.</summary>
    private UIElement PlanYEgreso(NotaClinica nota, bool editable)
    {
        var eg = EgresoDeLaNota.Leer(nota.Crudo);
        var pila = new StackPanel();

        var titulo = Estudio.Titulo("Plan y egreso", 16);
        titulo.Margin = new Thickness(0, 0, 0, 4);
        pila.Children.Add(titulo);
        pila.Children.Add(new TextBlock
        {
            Text = editable ? "Lo que el paciente se lleva. Cada dato se guarda al salir del campo."
                            : "Lo que el paciente se lleva.",
            Foreground = Estudio.TintaMedia, FontSize = 12.5, Margin = new Thickness(0, 0, 0, 12),
        });

        // ── medicamentos ────────────────────────────────────────────────────
        pila.Children.Add(RotuloDeEgreso("Medicamentos", "pill"));
        if (eg.Medicamentos.Count == 0)
            pila.Children.Add(Vacio("No se documentaron medicamentos."));
        for (int i = 0; i < eg.Medicamentos.Count; i++)
            pila.Children.Add(TarjetaDeMedicamento(eg.Medicamentos[i], i, editable));
        if (editable)
            pila.Children.Add(BotonAgregar("Agregar medicamento",
                () => EgresoDeLaNota.AgregarMedicamento(_notaEnPantalla!)));

        // ── las listas ──────────────────────────────────────────────────────
        pila.Children.Add(ListaDeEgreso("Medidas no farmacológicas", "leaf", "non_pharmacological", eg.NoFarmacologicas, editable));
        pila.Children.Add(ListaDeEgreso("Seguimiento", "calendar", "follow_up", eg.Seguimiento, editable));
        pila.Children.Add(ListaDeEgreso("Recomendaciones", "message-square-text", "recommendations", eg.Recomendaciones, editable));
        pila.Children.Add(ListaDeEgreso("Signos de alarma", "triangle-alert", "alarm_signs",
            eg.SignosDeAlarma.Select(s => s.Texto).ToList(), editable, eg.SignosDeAlarma));

        var tarjeta = Estudio.Tarjeta(Estudio.RadioMedio);
        tarjeta.Padding = new Thickness(22, 18, 22, 18);
        tarjeta.Margin = new Thickness(2, 0, 2, 12);
        tarjeta.Child = pila;
        return Estudio.Elevar(tarjeta, Estudio.Sombra1);
    }

    private UIElement TarjetaDeMedicamento(Medicamento m, int indice, bool editable)
    {
        if (!editable)
        {
            var linea = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 8), FontSize = 13.5 };
            linea.Inlines.Add(new System.Windows.Documents.Run(m.Nombre.Length > 0 ? m.Nombre : "Sin nombre")
                { FontWeight = FontWeights.SemiBold, Foreground = Estudio.TintaFuerte });
            string resto = string.Join(" · ", new[] { m.Concentracion, m.Dosis, m.Via, m.Frecuencia, m.Duracion, m.Cantidad }.Where(x => x.Trim().Length > 0));
            if (resto.Length > 0) linea.Inlines.Add(new System.Windows.Documents.Run("  " + resto) { Foreground = Estudio.TintaMedia });
            if (m.Indicaciones.Trim().Length > 0) linea.Inlines.Add(new System.Windows.Documents.Run("\n" + m.Indicaciones) { Foreground = Estudio.TintaMedia });
            return linea;
        }

        var rejilla = new Grid { Margin = new Thickness(0, 0, 0, 0) };
        for (int c = 0; c < 3; c++) rejilla.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int r = 0; r < 4; r++) rejilla.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        void Poner(UIElement e, int fila, int col, int span = 1)
        {
            Grid.SetRow(e, fila); Grid.SetColumn(e, col); Grid.SetColumnSpan(e, span);
            rejilla.Children.Add(e);
        }
        UIElement Campo(string etiqueta, string campo, string valor, string pista = "") =>
            CampoDeEgreso(etiqueta, valor, pista,
                nuevo => EgresoDeLaNota.CambiarMedicamento(_notaEnPantalla!, indice, campo, nuevo));

        Poner(Campo("Medicamento", "name", m.Nombre, "Nombre genérico"), 0, 0, 2);
        Poner(Campo("Concentración", "concentration", m.Concentracion, "p. ej. 500 mg/tableta"), 0, 2);
        Poner(Campo("Dosis", "dose", m.Dosis, "p. ej. 1 tableta"), 1, 0);
        Poner(Campo("Vía", "route", m.Via, "p. ej. oral"), 1, 1);
        Poner(Campo("Frecuencia", "frequency", m.Frecuencia, "p. ej. cada 8 horas"), 1, 2);
        Poner(Campo("Duración", "duration", m.Duracion, "p. ej. 5 días"), 2, 0);
        Poner(Campo("Cantidad total", "quantity", m.Cantidad, "p. ej. 15 tabletas"), 2, 1);
        Poner(Campo("Indicaciones", "instructions", m.Indicaciones, "p. ej. después de comer"), 3, 0, 3);

        var quitar = Estudio.BotonIcono("trash-2", "Quitar medicamento", 30, 15, Estudio.TintaTenue);
        quitar.HorizontalAlignment = HorizontalAlignment.Right;
        quitar.VerticalAlignment = VerticalAlignment.Top;
        quitar.Click += async (_, e) =>
        {
            e.Handled = true;
            await AplicarAlEgresoAsync(EgresoDeLaNota.QuitarMedicamento(_notaEnPantalla!, indice), repintar: true);
        };

        var dentro = new DockPanel();
        DockPanel.SetDock(quitar, Dock.Right);
        dentro.Children.Add(quitar);
        dentro.Children.Add(rejilla);
        return new Border
        {
            Child = dentro,
            CornerRadius = new CornerRadius(Estudio.RadioChico),
            Background = Estudio.SuperficieSuave,
            Padding = new Thickness(12, 10, 8, 6),
            Margin = new Thickness(0, 0, 0, 8),
        };
    }

    private UIElement ListaDeEgreso(string titulo, string icono, string lista, IReadOnlyList<string> textos, bool editable,
        IReadOnlyList<SignoDeAlarma>? alarmas = null)
    {
        var pila = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        pila.Children.Add(RotuloDeEgreso(titulo, icono));
        if (textos.Count == 0) pila.Children.Add(Vacio("Nada registrado."));
        for (int i = 0; i < textos.Count; i++)
        {
            int indice = i;
            var fila = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            if (alarmas != null)
            {
                var chip = ChipDeUrgencia(alarmas[i].Urgencia, editable, indice);
                chip.Margin = new Thickness(0, editable ? 6 : 1, 8, 0);
                chip.VerticalAlignment = VerticalAlignment.Top;
                DockPanel.SetDock(chip, Dock.Left);
                fila.Children.Add(chip);
            }
            if (editable)
            {
                var quitar = Estudio.BotonIcono("x", "Quitar", 28, 14, Estudio.TintaTenue);
                quitar.VerticalAlignment = VerticalAlignment.Top;
                quitar.Click += async (_, e) =>
                {
                    e.Handled = true;
                    await AplicarAlEgresoAsync(EgresoDeLaNota.QuitarItem(_notaEnPantalla!, lista, indice), repintar: true);
                };
                DockPanel.SetDock(quitar, Dock.Right);
                fila.Children.Add(quitar);
                fila.Children.Add(CampoDeEgreso("", textos[i], "",
                    nuevo => EgresoDeLaNota.CambiarItem(_notaEnPantalla!, lista, indice, nuevo)));
            }
            else
                fila.Children.Add(new TextBlock
                {
                    Text = "• " + textos[i], Foreground = Estudio.Tinta, FontSize = 13.5, TextWrapping = TextWrapping.Wrap,
                });
            pila.Children.Add(fila);
        }
        if (editable)
            pila.Children.Add(BotonAgregar("Agregar", () => EgresoDeLaNota.AgregarItem(_notaEnPantalla!, lista, "")));
        return pila;
    }

    /// <summary>
    /// La urgencia de un signo de alarma, como la web: urgencia, prioritario, vigilar. Pulsarla la
    /// pasa a la siguiente; el color es el de su gravedad.
    /// </summary>
    private FrameworkElement ChipDeUrgencia(string urgencia, bool editable, int indice)
    {
        var (texto, fondo, tinta) = urgencia switch
        {
            "emergency" => ("Urgencia", Estudio.AlertaSuave, Estudio.AlertaTinta),
            "priority" => ("Prioritario", Estudio.EsperaSuave, Estudio.EsperaTinta),
            "monitor" => ("Vigilar", Estudio.AcentoSuave, Estudio.AcentoTinta),
            _ => ("Sin clasificar", Estudio.SuperficieSuave, Estudio.TintaMedia),
        };
        var chip = Estudio.Chip(texto, fondo, tinta);
        if (!editable) return chip;
        chip.Cursor = Cursors.Hand;
        chip.MouseLeftButtonDown += async (_, e) =>
        {
            e.Handled = true;
            string siguiente = urgencia switch { "emergency" => "priority", "priority" => "monitor", "monitor" => "", _ => "emergency" };
            await AplicarAlEgresoAsync(EgresoDeLaNota.CambiarUrgencia(_notaEnPantalla!, indice, siguiente), repintar: true);
        };
        return chip;
    }

    /// <summary>
    /// Un campo que guarda al salir de él (o con Enter en los de una línea), SOLO si cambió. Enseña
    /// «guardado» o el motivo en la barra de estado.
    /// </summary>
    private UIElement CampoDeEgreso(string etiqueta, string valor, string pista, Func<string, NotaClinica> aplicar)
    {
        var caja = new TextBox
        {
            Text = valor,
            FontSize = 13.5,
            Padding = new Thickness(8, 5, 8, 5),
            Background = Estudio.Superficie,
            Foreground = Estudio.Tinta,
            BorderBrush = Estudio.Borde,
            BorderThickness = new Thickness(1),
            CaretBrush = Estudio.Acento,
            TextWrapping = TextWrapping.Wrap,
            ToolTip = pista.Length > 0 ? pista : null,
        };
        string guardado = valor;
        caja.LostKeyboardFocus += async (_, __) =>
        {
            string nuevo = caja.Text ?? "";
            if (nuevo == guardado || _notaEnPantalla == null) return;
            guardado = nuevo;
            await AplicarAlEgresoAsync(aplicar(nuevo), repintar: false);
        };
        caja.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                e.Handled = true;
                caja.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            }
        };

        var pila = new StackPanel { Margin = new Thickness(0, 0, 8, 6) };
        if (etiqueta.Length > 0)
            pila.Children.Add(new TextBlock
            {
                Text = etiqueta, Foreground = Estudio.TintaMedia, FontSize = 11.5, FontWeight = FontWeights.Medium,
                Margin = new Thickness(2, 0, 0, 3),
            });
        pila.Children.Add(caja);
        return pila;
    }

    private async Task AplicarAlEgresoAsync(NotaClinica nueva, bool repintar)
    {
        if (_propuesta != null) { Estado("Primero guarda o descarta el ajuste propuesto."); return; }
        Estado("Guardando el plan…");
        bool ok = await GuardarNotaEnteraAsync(nueva);
        if (!ok) return;
        Estado("Plan guardado. El portal ya ve el cambio.");
        LogBus.Log("consulta-ui", "plan y egreso corregidos desde U");
        if (repintar) RepintarLaNota();
    }

    private Button BotonAgregar(string texto, Func<NotaClinica> aplicar)
    {
        var b = Estudio.BotonTerciario(texto, "plus", 30);
        b.HorizontalAlignment = HorizontalAlignment.Left;
        b.Margin = new Thickness(-8, 0, 0, 4);
        b.Click += async (_, e) =>
        {
            e.Handled = true;
            if (_notaEnPantalla == null) return;
            await AplicarAlEgresoAsync(aplicar(), repintar: true);
        };
        return b;
    }

    private static UIElement RotuloDeEgreso(string texto, string icono)
    {
        var fila = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };
        var i = Estudio.Icono(icono, 14, Estudio.Acento);
        i.Margin = new Thickness(0, 0, 8, 0);
        fila.Children.Add(i);
        var r = Estudio.Rotulo(texto);
        r.VerticalAlignment = VerticalAlignment.Center;
        fila.Children.Add(r);
        return fila;
    }

    private static UIElement Vacio(string texto) => new TextBlock
    {
        Text = texto, Foreground = Estudio.TintaTenue, FontSize = 12.5, Margin = new Thickness(2, 0, 0, 6),
    };
}
