using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Cardio;

/// <summary>
/// Lee la historia clínica soltada en la Nota y contesta «¿por qué vino a cardiología?» (spec 051).
/// El «cómo se envía» se inyecta: en la app es OpenAI, en el contrato un modelo de mentira.
/// </summary>
/// <remarks>
/// NO USA ConfigureAwait(false), como <see cref="ClienteCardio"/>: escribe en la historia que la Nota
/// está pintando, y con las continuaciones en el hilo de la interfaz solo la toca un hilo.
/// </remarks>
public sealed class LectorDeLaHistoria
{
    private readonly Func<string, CancellationToken, Task<string>> _enviar;
    private readonly string _modelo;

    public LectorDeLaHistoria(Func<string, CancellationToken, Task<string>> enviar, string modelo)
    {
        _enviar = enviar ?? throw new ArgumentNullException(nameof(enviar));
        _modelo = string.IsNullOrWhiteSpace(modelo) ? ClienteCardio.ModeloPorDefecto : modelo.Trim();
    }

    /// <summary>El mismo modelo y la misma puerta que el panel de estudios. <c>U_HISTORIA_MODELO</c> lo cambia.</summary>
    public static LectorDeLaHistoria DeLaApp()
    {
        string? pedido = Environment.GetEnvironmentVariable("U_HISTORIA_MODELO");
        return new LectorDeLaHistoria(ClienteCardio.EnviarAOpenAIAsync, pedido ?? "");
    }

    /// <summary>
    /// Lee lo que falte y rehace el motivo con TODO lo leído (promesa 423).
    /// </summary>
    /// <remarks>
    /// LO QUE FALTA es lo que no está leído: un documento ya transcrito no se vuelve a pagar al soltar
    /// otro. Las fotos van de 3 en 3 y después cada PDF solo. Si un lote falla, lo leído antes se queda
    /// y se lanza nombrando QUÉ documentos faltaron: un motivo rehecho a medias parecería completo.
    /// </remarks>
    public async Task LeerAsync(HistoriaDeLaConsulta h, Action<string> progreso, CancellationToken ct)
    {
        var porLeer = h.Documentos.Where(d => !d.Leido).ToList();
        var lotes = new List<List<DocumentoDeLaHistoria>>();
        var fotos = porLeer.Where(d => d.Tipo == TipoDeDocumento.Foto).ToList();
        for (int i = 0; i < fotos.Count; i += LecturaDeLaHistoria.FotosPorLote)
            lotes.Add(fotos.Skip(i).Take(LecturaDeLaHistoria.FotosPorLote).ToList());
        foreach (var pdf in porLeer.Where(d => d.Tipo == TipoDeDocumento.Pdf)) lotes.Add(new List<DocumentoDeLaHistoria> { pdf });

        int hechos = 0, total = porLeer.Count;
        foreach (var lote in lotes)
        {
            string nombres = string.Join(", ", lote.Select(d => d.Nombre));
            progreso(total == 1 ? $"Leyendo {nombres}…" : $"Leyendo {hechos + 1}–{hechos + lote.Count} de {total}: {nombres}…");
            string respuesta;
            try
            {
                respuesta = await _enviar(LecturaDeLaHistoria.CuerpoTranscribir(_modelo, lote), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception e)
            {
                throw new InvalidOperationException($"No se pudo leer {nombres}: {Causa(e)}", e);
            }
            LecturaDeLaHistoria.AplicarTranscripcion(respuesta, lote);
            hechos += lote.Count;
            // EL LOG NO LLEVA TEXTO CLÍNICO: cuántos documentos y cuántos párrafos, nada más.
            LogBus.Log("historia", $"{lote.Count} documento(s) leído(s): {lote.Count(d => d.Leido)} con texto "
                + $"({lote.Sum(d => d.Parrafos.Count)} párrafo(s)), {lote.Count(d => !d.Leido)} sin leer");
        }

        var parrafos = h.Parrafos.ToList();
        if (parrafos.Count == 0)
        {
            // Nada leído no es una llamada: se dice tal cual.
            h.Motivo = new MotivoDeCardiologia();
            return;
        }

        progreso("Buscando por qué vino a cardiología…");
        string delMotivo;
        try
        {
            delMotivo = await _enviar(LecturaDeLaHistoria.CuerpoMotivo(_modelo, parrafos), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e)
        {
            throw new InvalidOperationException($"Los documentos se leyeron, pero no se pudo buscar por qué vino a cardiología: {Causa(e)}", e);
        }
        h.Motivo = LecturaDeLaHistoria.InterpretarMotivo(delMotivo, parrafos);
        LogBus.Log("historia", h.Motivo.Dicho
            ? $"motivo encontrado, sostenido por {h.Motivo.Citas.Count} párrafo(s) de {parrafos.Count}"
            : $"los {parrafos.Count} párrafo(s) no dicen por qué vino a cardiología");
    }

    /// <summary>La cadena entera, no solo el mensaje de fuera (patrón nº3).</summary>
    private static string Causa(Exception e)
    {
        var partes = new List<string>();
        for (var x = e; x != null; x = x.InnerException) partes.Add(x.Message);
        return string.Join(" ← ", partes.Distinct());
    }
}
