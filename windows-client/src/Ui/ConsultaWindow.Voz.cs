using U.WindowsClient.Clinical;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Ui;

/// <summary>
/// LA VOZ MANEJA LA NOTA (spec 060): cada herramienta nota_* de la voz se atiende aquí, en la
/// ventana propia, con los mismos métodos que usan los botones. Es lo que hace que U maneje Notes
/// «como su app nativa»: nada de capturas ni de clics sobre el navegador.
/// </summary>
/// <remarks>
/// LO QUE SE DEVUELVE ES LO QUE LA VOZ DICE. Frases cortas, en la voz de Ü, con el dato que se pidió.
/// Y NADA DE ESTO VA AL LOG más allá de qué herramienta corrió (promesa 484): lo registra
/// ConversacionEnVivo sin el contenido.
///
/// LA VOZ NO GUARDA: nota_ajustar deja la misma propuesta que la barra de ajuste, y la aprueba el
/// médico con su gesto. No hay rama aquí que llame a guardar.
/// </remarks>
public sealed partial class ConsultaWindow
{
    public async Task<string> AtenderVozAsync(string herramienta, IReadOnlyDictionary<string, string> args)
    {
        string A(string k) => args.TryGetValue(k, out var v) ? (v ?? "").Trim() : "";
        if (!_sesion.HayMedico) return "No hay ningún médico con sesión iniciada en Miracle: hay que entrar primero.";

        switch (herramienta)
        {
            case "nota_abrir":
            {
                if (A("paciente").Length == 0) { Mostrar(nota: true); return "Listo, la nota está abierta."; }
                var (p, porque) = await ResolverPacienteAsync(A("paciente"));
                if (p == null) return porque;
                var ultima = await EspejoDeConsulta.UltimaDelPacienteAsync(_sesion, p.Id);
                if (ultima == null) return $"{p.Nombre} no tiene consultas guardadas todavía.";
                await AbrirConsultaAsync(ultima);
                return $"Abrí la última consulta de {p.Nombre}.";
            }

            case "nota_grabar":
                if (_consulta.Estado == EstadoDeConsulta.Grabando) return "Ya estoy grabando la consulta.";
                await AlternarAsync();
                return _consulta.Estado == EstadoDeConsulta.Grabando ? "Grabando la consulta." : _estado.Text;

            case "nota_parar":
                if (_consulta.Estado != EstadoDeConsulta.Grabando) return "No hay ninguna consulta grabándose.";
                // No se espera a la nota: organizarla tarda, y la voz no puede quedarse muda un minuto.
                _ = AlternarAsync();
                return "Terminé de grabar. Miracle está organizando la nota; los avisos salen en pantalla en cuanto esté.";

            case "nota_leer":
                return LeerParaLaVoz(A("seccion"));

            case "nota_historial":
            {
                Paciente? p;
                if (A("paciente").Length == 0)
                {
                    p = _paciente;
                    if (p == null) return "¿De qué paciente? La nota que se ve no tiene paciente asociado.";
                }
                else
                {
                    string porque;
                    (p, porque) = await ResolverPacienteAsync(A("paciente"));
                    if (p == null) return porque;
                }
                var lista = await HistorialDelPaciente.LeerAsync(_sesion, _clinica, p.Id);
                return HistorialDelPaciente.ParaLaVoz(p.Nombre, lista);
            }

            case "nota_ajustar":
                return await AjustarPorVozAsync(A("instruccion"), A("seccion"));

            case "nota_imprimir":
            {
                string papel = ConceptosClinicos.SinTildes(A("papel").ToLowerInvariant());
                string tipo = papel.Contains("formula") || papel.Contains("receta") ? "formula"
                            : papel.Contains("indicacion") || papel.Contains("recomendacion") ? "indicaciones"
                            : "nota";
                if (_notaEnPantalla == null) return "No hay ninguna nota abierta que imprimir.";
                await ImprimirAsync(tipo);
                return _estado.Text;
            }
        }
        return $"«{herramienta}» no es una herramienta de la nota.";
    }

    /// <summary>A quién se refiere: se busca en sus pacientes y, si hay varios, se pregunta (promesa 483).</summary>
    private async Task<(Paciente? Paciente, string Porque)> ResolverPacienteAsync(string dicho)
    {
        var candidatos = await PacientesDelMedico.BuscarAsync(_sesion, dicho);
        return HistorialDelPaciente.ElegirPaciente(candidatos, dicho);
    }

    private string LeerParaLaVoz(string seccion)
    {
        var nota = _notaEnPantalla ?? _consulta.Nota;
        if (nota == null) return "No hay ninguna nota abierta.";
        string pedida = ConceptosClinicos.SinTildes(seccion.ToLowerInvariant());

        if (pedida.Contains("plan") || pedida.Contains("medicament") || pedida.Contains("egreso") || pedida.Contains("formula"))
        {
            var eg = EgresoDeLaNota.Leer(nota.Crudo);
            var partes = new List<string>();
            if (eg.Medicamentos.Count > 0)
                partes.Add("Medicamentos: " + string.Join("; ", eg.Medicamentos.Select(DocumentosDelPaciente.LineaDeMedicamento).Where(x => x.Length > 0)) + ".");
            if (eg.Seguimiento.Count > 0) partes.Add("Controles: " + string.Join("; ", eg.Seguimiento) + ".");
            if (eg.Recomendaciones.Count > 0) partes.Add("Recomendaciones: " + string.Join("; ", eg.Recomendaciones) + ".");
            if (eg.SignosDeAlarma.Count > 0) partes.Add("Signos de alarma: " + string.Join("; ", eg.SignosDeAlarma.Select(a => a.Texto)) + ".");
            return partes.Count > 0 ? string.Join(" ", partes) : "La nota no tiene plan registrado.";
        }

        if (pedida.Length > 0)
        {
            var s = BuscarSeccion(nota, seccion);
            if (s == null) return $"La nota no tiene una sección que se llame «{seccion}». Tiene: {string.Join(", ", nota.Secciones.Select(x => x.Titulo))}.";
            return s.Contenido.Trim().Length > 0 ? $"{s.Titulo}: {Recortar(s.Contenido.Trim(), 1500)}" : $"«{s.Titulo}» está vacía.";
        }
        return Recortar(TextoDeLaNota.DeLaNota(nota), 2500);
    }

    /// <summary>
    /// «Cámbiale la dosis a…»: la MISMA propuesta que la barra y el micrófono de sección. Se devuelve
    /// qué quedó en pantalla para que el médico lo apruebe; nunca se guarda desde aquí.
    /// </summary>
    private async Task<string> AjustarPorVozAsync(string instruccion, string seccion)
    {
        if (instruccion.Length == 0) return "¿Qué cambio quieres en la nota?";
        var nota = _notaEnPantalla;
        if (nota == null) return "No hay ninguna nota abierta para cambiar.";
        if (_propuesta != null) return "Ya hay un cambio esperando en pantalla: guárdalo o descártalo antes de pedir otro.";

        var s = seccion.Length > 0 ? BuscarSeccion(nota, seccion) : null;
        if (seccion.Length > 0 && s == null && !ConceptosClinicos.SinTildes(seccion.ToLowerInvariant()).Contains("plan"))
            return $"La nota no tiene una sección que se llame «{seccion}».";

        if (s != null)
        {
            var pedido = AjusteDeLaNota.PorVoz(instruccion, s.Clave, s.Titulo, s.Contenido);
            if (pedido == null) return "No entendí el cambio.";
            if (pedido.TextoLocal != null) MostrarPropuesta(AjusteDeLaNota.Literal(nota, s.Clave, s.Titulo, pedido.TextoLocal));
            else await AjustarAsync(pedido.Instruccion, pedido.Seccion, pedido.Tipo, $"«{s.Titulo}»");
        }
        else await AjustarAsync(instruccion, null, "rewrite", "la nota");

        LogBus.Log("consulta-ui", _propuesta != null ? "la voz dejó una propuesta en pantalla" : "la voz pidió un cambio que no se aplicó");
        return _propuesta != null
            ? $"Te dejé el cambio en pantalla ({_propuesta.Cambiadas.Count} sección(es) marcadas). Guárdalo con Ctrl+S si está bien, o descártalo."
            : _estado.Text;
    }

    private static SeccionDeNota? BuscarSeccion(NotaClinica nota, string dicho)
    {
        string q = ConceptosClinicos.SinTildes(dicho.Trim().ToLowerInvariant());
        if (q.Length == 0) return null;
        string Plano(SeccionDeNota s) => ConceptosClinicos.SinTildes($"{s.Titulo}".ToLowerInvariant());
        return nota.Secciones.FirstOrDefault(s => Plano(s) == q)
            ?? nota.Secciones.FirstOrDefault(s => Plano(s).Contains(q) || q.Contains(Plano(s)))
            ?? nota.Secciones.FirstOrDefault(s => ConceptosClinicos.SinTildes(s.Clave.ToLowerInvariant()).Contains(q));
    }

    private static string Recortar(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
