using U.Graph.NoteExport;
using Xunit;

namespace U.Graph.Tests;

/// <summary>
/// Lo que se comprueba ANTES de tocar SAP. Dos familias: lo que impide escribir con seguridad, y lo
/// que solo merece un aviso para quien aprueba.
/// </summary>
public class ExportJobReviewTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private static ExportJobResponse Job(
        string workflowId = "wf_prueba", int attempts = 1, ExportPayload? payload = null,
        string? lease = null) => new()
        {
            Export = new ExportJobInfo
            {
                Id = "exp_1",
                WorkflowId = workflowId,
                Attempts = attempts,
                LeaseExpiresAt = lease ?? Ahora.AddMinutes(10).ToString("o"),
            },
            Payload = payload ?? Payload(),
        };

    private static ExportPayload Payload(string texto = "MOTIVO:\nAlgo sintético.", string hash = "abc123") => new()
    {
        RenderedText = texto,
        Context = texto,
        Firma = new ExportSignature { Por = "Profesional de prueba", Fecha = "2026-07-30", Hash = hash },
        PatientRef = "00000000-0000-4000-8000-0000000000aa",
    };

    [Fact]
    public void TrabajoCompleto_SePuedeEjecutar()
    {
        JobReview review = ExportJobReview.Review(Job(), record: null, Ahora);

        Assert.True(review.CanProceed);
        Assert.Empty(review.Warnings);
    }

    [Fact]
    public void EjecucionAnteriorIncierta_PideIntervencion_YNoSeEjecuta()
    {
        // El escenario que evita duplicar una nota en la historia de un paciente: una corrida previa
        // tocó SAP y murió sin saber cómo terminó. Nadie puede afirmar desde aquí si la nota está.
        var previo = new ExportRecord
        {
            ExportId = "exp_1",
            Phase = ExportPhase.Executing,
            Uncertain = true,
            UncertainNote = "la aplicación se cerró durante la escritura",
        };

        JobReview review = ExportJobReview.Review(Job(attempts: 2), previo, Ahora);

        Assert.False(review.CanProceed);
        Assert.Equal(ExportVerdict.NeedsDoctor, review.Decision.Verdict);
        Assert.Contains(review.Decision.UnresolvedFields!, f => f.Contains("registró"));
    }

    [Fact]
    public void SinWorkflow_EsFalloDeConfiguracion()
    {
        JobReview review = ExportJobReview.Review(Job(workflowId: ""), null, Ahora);

        Assert.False(review.CanProceed);
        Assert.Equal("SIN_WORKFLOW", review.Decision.ErrorCode);
    }

    [Fact]
    public void PayloadVacio_NoPuedeTerminarEnExportada()
    {
        // Pasa de verdad: el contenido se purga a las 72 h del estado terminal, así que un trabajo
        // viejo puede llegar sin nada. Escribir cero campos y reportar éxito sería un éxito falso.
        var vacio = new ExportPayload
        {
            Firma = new ExportSignature { Hash = "abc123" },
            RenderedText = "",
            Context = "",
        };

        JobReview review = ExportJobReview.Review(Job(payload: vacio), null, Ahora);

        Assert.False(review.CanProceed);
        Assert.Equal("PAYLOAD_VACIO", review.Decision.ErrorCode);
    }

    [Fact]
    public void SinHashDeFirma_NoSeEscribe()
    {
        JobReview review = ExportJobReview.Review(Job(payload: Payload(hash: "")), null, Ahora);

        Assert.False(review.CanProceed);
        Assert.Equal("SIN_FIRMA", review.Decision.ErrorCode);
    }

    [Fact]
    public void SoloSeccionesSinTextoPlano_SigueSiendoEjecutable()
    {
        var soloSecciones = new ExportPayload
        {
            Firma = new ExportSignature { Hash = "abc123" },
            Note = new List<ExportNoteSection>
            {
                new() { Id = "motivo", Titulo = "Motivo", Kind = "texto", Texto = "Algo." },
            },
        };

        JobReview review = ExportJobReview.Review(Job(payload: soloSecciones), null, Ahora);

        Assert.True(review.CanProceed);
    }

    [Fact]
    public void ReintentoSinRegistroLocal_AvisaPeroNoBloquea()
    {
        // `attempts` llega ya incrementado: >1 significa que hubo un claim anterior. Desde aquí no se
        // puede distinguir un reintento legítimo de una escritura que no llegó a reportarse, así que
        // se avisa y decide una persona — bloquear rompería el reintento normal tras un error.
        JobReview review = ExportJobReview.Review(Job(attempts: 3), record: null, Ahora);

        Assert.True(review.CanProceed);
        Assert.Contains(review.Warnings, w => w.Contains("intento nº 3"));
    }

    [Fact]
    public void ReintentoConRegistroPropio_NoAvisa()
    {
        var propio = new ExportRecord { ExportId = "exp_1", Phase = ExportPhase.Claimed };

        JobReview review = ExportJobReview.Review(Job(attempts: 2), propio, Ahora);

        Assert.True(review.CanProceed);
        Assert.DoesNotContain(review.Warnings, w => w.Contains("intento nº"));
    }

    [Fact]
    public void LeaseVencido_AvisaPeroNoBloquea()
    {
        // Aviso y no bloqueo a propósito: el lease lo sella el reloj de Postgres y aquí se compara
        // con el del equipo, que puede ir desfasado. Parar una exportación buena por un reloj mal
        // puesto sería peor que el problema que evita.
        JobReview review = ExportJobReview.Review(
            Job(lease: Ahora.AddMinutes(-5).ToString("o")), null, Ahora);

        Assert.True(review.CanProceed);
        Assert.Contains(review.Warnings, w => w.Contains("vencido"));
    }

    [Fact]
    public void LeaseCorto_Avisa()
    {
        JobReview review = ExportJobReview.Review(
            Job(lease: Ahora.AddSeconds(20).ToString("o")), null, Ahora);

        Assert.True(review.CanProceed);
        Assert.Contains(review.Warnings, w => w.Contains("plazo"));
    }

    [Fact]
    public void Resumen_NoIncluyeElTextoClinico()
    {
        // Lo que se le enseña a quien aprueba para reconocer la consulta no es la nota entera.
        ExportPayload payload = Payload(texto: "MOTIVO:\nDolor lumbar de dos semanas.");
        payload.Servicio = "Consulta externa";
        payload.Fecha = "2026-07-30";

        var filas = ExportJobReview.Summarize(payload);

        Assert.Contains(filas, f => f.Label == "Servicio");
        Assert.DoesNotContain(filas, f => f.Value.Contains("Dolor lumbar"));
    }

    [Fact]
    public void Resumen_AcortaElUuidDelPaciente()
    {
        var filas = ExportJobReview.Summarize(Payload());
        (string Label, string Value) fila = filas.First(f => f.Label == "Referencia de paciente");

        Assert.Contains("…", fila.Value);
        Assert.True(fila.Value.Length < 20);
    }
}
