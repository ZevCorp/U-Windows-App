using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using U.WindowsClient.Clinical;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Ui;

/// <summary>
/// EL PINTOR DE WINDOWS de los papeles del paciente (spec 059): el <see cref="PapelDelPaciente"/>
/// —el mismo que la web, promesa 475— en un <see cref="FlowDocument"/> que se imprime con el
/// diálogo de Windows. «Microsoft Print to PDF» da el PDF sin librerías nuevas.
/// </summary>
/// <remarks>
/// LO MISMO QUE EL PAPEL DE LA WEB: la institución y el número de página en CADA hoja (un paginador
/// que envuelve al del FlowDocument), el cuerpo en serif, la raya para escribir a mano lo que la
/// fórmula no trae, y espacio para firma. Los colores de las alarmas son los de la web.
/// </remarks>
public static class PapelesDelPaciente
{
    // Carta en DIPs (8,5 × 11 in a 96 ppp). Márgenes de ~18 mm, con aire arriba y abajo para el marco.
    private const double Ancho = 816, Alto = 1056, Margen = 68, MargenVertical = 92;

    private static readonly Brush Tinta = Pincel(0x0E, 0x17, 0x26);
    private static readonly Brush TintaFuerte = Pincel(0x0C, 0x14, 0x24);
    private static readonly Brush Tenue = Pincel(0x5D, 0x6B, 0x80);
    private static readonly Brush Azul = Pincel(0x1A, 0x4F, 0xA0);
    private static readonly Brush Linea = Pincel(0xD4, 0xDB, 0xE6);
    private static readonly Brush FondoSuave = Pincel(0xF3, 0xF6, 0xFA);

    /// <summary>Abre el diálogo de imprimir con el papel. Devuelve si se imprimió.</summary>
    public static bool Imprimir(PapelDelPaciente papel)
    {
        var dialogo = new PrintDialog();
        if (dialogo.ShowDialog() != true) return false;
        var doc = Pintar(papel);
        var paginador = new PaginadorConMarco(((IDocumentPaginatorSource)doc).DocumentPaginator, papel);
        string paciente = papel.Datos.FirstOrDefault(d => d.Etiqueta == "Paciente")?.Valor ?? "";
        dialogo.PrintDocument(paginador, $"{papel.Titulo} · {paciente}");
        // Sin el nombre del paciente en el log: es un dato clínico.
        LogBus.Log("papeles", $"«{papel.Titulo}» enviado a imprimir · {papel.Bloques.Count} bloque(s)");
        return true;
    }

    public static FlowDocument Pintar(PapelDelPaciente p)
    {
        var doc = new FlowDocument
        {
            PageWidth = Ancho,
            PageHeight = Alto,
            PagePadding = new Thickness(Margen, MargenVertical, Margen, MargenVertical),
            ColumnWidth = double.PositiveInfinity,
            FontFamily = Estudio.FuenteCuerpo,
            FontSize = 13.5,
            Foreground = Tinta,
        };

        if (p.Demo)
            doc.Blocks.Add(new Paragraph(new Run("DOCUMENTO DE DEMOSTRACIÓN — generado a partir de una conversación simulada. No válido como historia clínica."))
            {
                FontWeight = FontWeights.Bold, Foreground = Pincel(0x7C, 0x3A, 0x05), Background = Pincel(0xFD, 0xEE, 0xCF),
                BorderBrush = Pincel(0xA3, 0x4A, 0x06), BorderThickness = new Thickness(2), Padding = new Thickness(10, 6, 10, 6),
            });

        doc.Blocks.Add(Encabezado(p));
        doc.Blocks.Add(Datos(p));
        foreach (var b in p.Bloques) doc.Blocks.Add(PintarBloque(b));
        if (p.Firma.Nombre.Length > 0 || p.Firma.Lineas.Count > 0) doc.Blocks.Add(FirmaDelMedico(p.Firma));
        if (p.Sello.Count > 0)
        {
            var sello = new Section { Margin = new Thickness(0, 18, 0, 0), FontSize = 12 };
            foreach (var l in p.Sello) sello.Blocks.Add(new Paragraph(new Run(l)) { Margin = new Thickness(0, 1, 0, 1) });
            doc.Blocks.Add(sello);
        }
        return doc;
    }

    private static Block Encabezado(PapelDelPaciente p)
    {
        var tabla = new Table { CellSpacing = 0, BorderBrush = TintaFuerte, BorderThickness = new Thickness(0, 0, 0, 2) };
        tabla.Columns.Add(new TableColumn { Width = new GridLength(3, GridUnitType.Star) });
        tabla.Columns.Add(new TableColumn { Width = new GridLength(2, GridUnitType.Star) });
        var grupo = new TableRowGroup();
        var fila = new TableRow();

        var izquierda = new TableCell { Padding = new Thickness(0, 0, 0, 10) };
        if (p.Membrete.Nombre.Length > 0)
            izquierda.Blocks.Add(new Paragraph(new Run(p.Membrete.Nombre.ToUpper(CultureInfo.GetCultureInfo("es-CO"))))
                { FontWeight = FontWeights.Bold, FontSize = 14.5, Foreground = TintaFuerte, Margin = new Thickness(0) });
        if (p.Membrete.Lineas.Count > 0)
            izquierda.Blocks.Add(new Paragraph(new Run(string.Join(" · ", p.Membrete.Lineas)))
                { FontSize = 11.5, Foreground = Tenue, Margin = new Thickness(0, 3, 0, 0) });

        var derecha = new TableCell { Padding = new Thickness(0, 0, 0, 10), TextAlignment = TextAlignment.Right };
        derecha.Blocks.Add(new Paragraph(new Run(p.Titulo))
            { FontFamily = Estudio.FuenteTitulo, FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = TintaFuerte, Margin = new Thickness(0) });
        var fecha = p.Datos.FirstOrDefault(d => d.Etiqueta == "Fecha");
        if (fecha != null)
            derecha.Blocks.Add(new Paragraph(new Run(fecha.Valor)) { FontSize = 12, Foreground = Tenue, Margin = new Thickness(0, 2, 0, 0) });

        fila.Cells.Add(izquierda);
        fila.Cells.Add(derecha);
        grupo.Rows.Add(fila);
        tabla.RowGroups.Add(grupo);
        return tabla;
    }

    private static Block Datos(PapelDelPaciente p)
    {
        var datos = p.Datos.Where(d => d.Etiqueta != "Fecha").ToList();
        var tabla = new Table { CellSpacing = 0, Background = FondoSuave, Margin = new Thickness(0, 14, 0, 6), Padding = new Thickness(12, 8, 12, 8) };
        for (int i = 0; i < 4; i++)
            tabla.Columns.Add(new TableColumn { Width = new GridLength(i % 2 == 0 ? 1 : 2.4, GridUnitType.Star) });
        var grupo = new TableRowGroup();
        for (int i = 0; i < datos.Count; i += 2)
        {
            var fila = new TableRow();
            for (int j = i; j < i + 2; j++)
            {
                if (j < datos.Count)
                {
                    fila.Cells.Add(new TableCell(new Paragraph(new Run(datos[j].Etiqueta.ToUpper(CultureInfo.GetCultureInfo("es-CO"))))
                        { FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Tenue, Margin = new Thickness(0, 3, 0, 3) }));
                    fila.Cells.Add(new TableCell(new Paragraph(new Run(datos[j].Valor))
                        { FontSize = 12.5, FontWeight = FontWeights.Medium, Margin = new Thickness(0, 2, 8, 2) }));
                }
                else { fila.Cells.Add(new TableCell()); fila.Cells.Add(new TableCell()); }
            }
            grupo.Rows.Add(fila);
        }
        tabla.RowGroups.Add(grupo);
        return tabla;
    }

    private static Paragraph Rotulo(string texto) => new(new Run(texto.ToUpper(CultureInfo.GetCultureInfo("es-CO"))))
    {
        FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Azul, Margin = new Thickness(0, 18, 0, 4),
        KeepWithNext = true,
    };

    private static Block PintarBloque(BloqueDelPapel b)
    {
        var s = new Section();
        switch (b.Tipo)
        {
            case "parrafo":
                s.Blocks.Add(Rotulo(b.Titulo));
                s.Blocks.Add(new Paragraph(new Run(b.Texto))
                    { FontFamily = Estudio.FuenteDocumento, FontSize = 14.5, LineHeight = 22, Foreground = Pincel(0x19, 0x1F, 0x28), Margin = new Thickness(0) });
                break;
            case "lista":
                s.Blocks.Add(Rotulo(b.Titulo));
                s.Blocks.Add(Lista(b.Items, Estudio.FuenteDocumento, 14.5));
                break;
            case "medicamento":
                s.Blocks.Add(Medicamento(b));
                break;
            case "alarma":
                var (borde, fondo, tinta) = b.Nivel switch
                {
                    "emergency" => (Pincel(0xC0, 0x39, 0x2B), Pincel(0xFD, 0xEC, 0xEA), Pincel(0x8E, 0x1F, 0x14)),
                    "priority" => (Pincel(0xB7, 0x79, 0x1F), Pincel(0xFD, 0xF3, 0xE1), Pincel(0x7A, 0x4A, 0x06)),
                    "monitor" => (Pincel(0x32, 0x72, 0xE3), Pincel(0xEE, 0xF4, 0xFE), Azul),
                    _ => (Pincel(0x94, 0xA3, 0xB8), FondoSuave, Tinta),
                };
                s.BorderBrush = borde;
                s.BorderThickness = new Thickness(4, 0, 0, 0);
                s.Background = fondo;
                s.Padding = new Thickness(14, 8, 14, 8);
                s.Margin = new Thickness(0, 10, 0, 0);
                s.Foreground = tinta;
                s.Blocks.Add(new Paragraph(new Run(b.Titulo + ":")) { FontWeight = FontWeights.Bold, FontSize = 14, Margin = new Thickness(0, 0, 0, 2) });
                s.Blocks.Add(Lista(b.Items, Estudio.FuenteCuerpo, 13.5));
                break;
            case "tabla":
                s.Blocks.Add(Rotulo(b.Titulo));
                s.Blocks.Add(Tabla(b));
                break;
            case "aviso":
                s.Blocks.Add(new Paragraph(new Run(b.Texto))
                {
                    Foreground = Tenue, BorderBrush = Linea, BorderThickness = new Thickness(1), Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 16, 0, 0),
                });
                break;
        }
        return s;
    }

    private static List Lista(IReadOnlyList<string> items, FontFamily fuente, double tamano)
    {
        var l = new List { MarkerStyle = TextMarkerStyle.Disc, Margin = new Thickness(0), Padding = new Thickness(18, 0, 0, 0), FontFamily = fuente, FontSize = tamano };
        foreach (var i in items) l.ListItems.Add(new ListItem(new Paragraph(new Run(i)) { Margin = new Thickness(0, 1, 0, 1) }));
        return l;
    }

    /// <summary>Un medicamento de la fórmula; lo que la nota no trae, una raya para escribirlo a mano.</summary>
    private static Block Medicamento(BloqueDelPapel b)
    {
        var s = new Section
        {
            BorderBrush = Linea, BorderThickness = new Thickness(1), Padding = new Thickness(14, 10, 14, 8),
            Margin = new Thickness(0, 12, 0, 0), BreakPageBefore = false,
        };
        var nombre = new Paragraph { FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = TintaFuerte, Margin = new Thickness(0, 0, 0, 6), KeepWithNext = true };
        nombre.Inlines.Add(new Run($"{b.Numero}.  ") { Foreground = Pincel(0x32, 0x72, 0xE3) });
        nombre.Inlines.Add(new Run(b.Nombre));
        s.Blocks.Add(nombre);

        var tabla = new Table { CellSpacing = 0 };
        tabla.Columns.Add(new TableColumn { Width = new GridLength(1.1, GridUnitType.Star) });
        tabla.Columns.Add(new TableColumn { Width = new GridLength(2, GridUnitType.Star) });
        var grupo = new TableRowGroup();
        foreach (var c in b.Campos)
        {
            var fila = new TableRow();
            fila.Cells.Add(new TableCell(new Paragraph(new Run(c.Etiqueta)) { FontSize = 12, Foreground = Tenue, Margin = new Thickness(0, 3, 8, 3) }));
            var valor = new Paragraph { FontSize = 13.5, FontWeight = FontWeights.Medium, Margin = new Thickness(0, 3, 0, 3) };
            if (c.Valor.Length > 0) valor.Inlines.Add(new Run(c.Valor));
            else valor.Inlines.Add(new InlineUIContainer(new Border
            {
                Width = 260, Height = 16, BorderBrush = Pincel(0x94, 0xA3, 0xB8), BorderThickness = new Thickness(0, 0, 0, 1),
            }));
            fila.Cells.Add(new TableCell(valor));
            grupo.Rows.Add(fila);
        }
        tabla.RowGroups.Add(grupo);
        s.Blocks.Add(tabla);
        return s;
    }

    private static Table Tabla(BloqueDelPapel b)
    {
        var t = new Table { CellSpacing = 0, FontSize = 12, BorderBrush = Linea, BorderThickness = new Thickness(1, 1, 0, 0) };
        foreach (var _ in b.Columnas) t.Columns.Add(new TableColumn());
        var grupo = new TableRowGroup();
        TableCell Celda(string texto, bool cabeza) => new(new Paragraph(new Run(texto)) { Margin = new Thickness(6, 3, 6, 3), FontWeight = cabeza ? FontWeights.SemiBold : FontWeights.Normal })
            { BorderBrush = Linea, BorderThickness = new Thickness(0, 0, 1, 1), Background = cabeza ? FondoSuave : null };
        var cabecera = new TableRow();
        foreach (var c in b.Columnas) cabecera.Cells.Add(Celda(c, true));
        grupo.Rows.Add(cabecera);
        foreach (var f in b.Filas)
        {
            var fila = new TableRow();
            foreach (var c in f) fila.Cells.Add(Celda(c, false));
            grupo.Rows.Add(fila);
        }
        t.RowGroups.Add(grupo);
        return t;
    }

    private static Block FirmaDelMedico(FirmaDelPapel f)
    {
        var s = new Section { Margin = new Thickness(0, 56, 260, 0) };
        s.Blocks.Add(new Paragraph(new Run(f.Nombre.Length > 0 ? f.Nombre : "Firma del profesional"))
        {
            BorderBrush = TintaFuerte, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 6, 0, 0),
            FontWeight = FontWeights.SemiBold, Margin = new Thickness(0),
        });
        foreach (var l in f.Lineas) s.Blocks.Add(new Paragraph(new Run(l)) { FontSize = 12, Foreground = Tenue, Margin = new Thickness(0, 1, 0, 0) });
        return s;
    }

    private static SolidColorBrush Pincel(byte r, byte g, byte b)
    {
        var p = new SolidColorBrush(Color.FromRgb(r, g, b));
        p.Freeze();
        return p;
    }

    /// <summary>
    /// Envuelve el paginador del FlowDocument para poner en CADA hoja la institución y el título
    /// arriba, y el pie con «Página n de N» abajo. El FlowDocument solo no sabe hacerlo.
    /// </summary>
    private sealed class PaginadorConMarco : DocumentPaginator
    {
        private readonly DocumentPaginator _dentro;
        private readonly PapelDelPaciente _papel;

        public PaginadorConMarco(DocumentPaginator dentro, PapelDelPaciente papel)
        {
            _dentro = dentro;
            _papel = papel;
            _dentro.PageSize = new Size(Ancho, Alto);
            _dentro.ComputePageCount();
        }

        public override DocumentPage GetPage(int numero)
        {
            var pagina = _dentro.GetPage(numero);
            var marco = new ContainerVisual();
            marco.Children.Add(pagina.Visual);

            string paciente = _papel.Datos.FirstOrDefault(d => d.Etiqueta == "Paciente")?.Valor ?? "";
            string arriba = string.Join(" · ", new[] { _papel.Membrete.Nombre, _papel.Titulo }.Where(x => x.Length > 0));
            var dibujo = new DrawingVisual();
            using (var dc = dibujo.RenderOpen())
            {
                // La primera hoja ya lleva el encabezado grande: el marco de arriba es para las demás.
                if (numero > 0)
                {
                    Texto(dc, arriba, Margen, 44, TextAlignment.Left, Ancho / 2 - Margen);
                    Texto(dc, paciente, Ancho - Margen, 44, TextAlignment.Right, Ancho / 2 - Margen);
                }
                Texto(dc, _papel.Pie, Margen, Alto - 60, TextAlignment.Left, Ancho - 2 * Margen - 110);
                Texto(dc, $"Página {numero + 1} de {_dentro.PageCount}", Ancho - Margen, Alto - 60, TextAlignment.Right, 110);
            }
            marco.Children.Add(dibujo);
            return new DocumentPage(marco, new Size(Ancho, Alto), pagina.BleedBox, pagina.ContentBox);
        }

        private static void Texto(DrawingContext dc, string texto, double x, double y, TextAlignment alineado, double ancho)
        {
            if (texto.Length == 0) return;
            var ft = new FormattedText(texto, CultureInfo.GetCultureInfo("es-CO"), FlowDirection.LeftToRight,
                new Typeface(Estudio.FuenteCuerpo, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal), 10, Tenue, 1.0)
            {
                MaxTextWidth = Math.Max(20, ancho),
                TextAlignment = alineado,
                MaxLineCount = 2,
                Trimming = TextTrimming.CharacterEllipsis,
            };
            double izquierda = alineado == TextAlignment.Right ? x - ft.MaxTextWidth : x;
            dc.DrawText(ft, new Point(izquierda, y));
        }

        public override bool IsPageCountValid => _dentro.IsPageCountValid;
        public override int PageCount => _dentro.PageCount;
        public override Size PageSize { get => _dentro.PageSize; set => _dentro.PageSize = value; }
        public override IDocumentPaginatorSource Source => _dentro.Source;
    }
}
