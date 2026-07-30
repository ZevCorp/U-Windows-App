using U.Graph.NoteExport;
using Xunit;

namespace U.Graph.Tests;

/// <summary>
/// El diario es lo único que sobrevive a un cierre de la aplicación, y por tanto lo único capaz de
/// distinguir «este trabajo no se había empezado» de «este trabajo pudo haber escrito en la historia
/// clínica». Cada prueba de aquí es un corte en un punto distinto del camino.
/// </summary>
public class ExportJournalTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "u-export-journal-tests", Guid.NewGuid().ToString("N"));

    private ExportJournal New() => new(log: null, folder: _folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { }
    }

    private static ExportResultRequest Result(string outcome = "ok") =>
        new() { Device = "equipo-1", Outcome = outcome, Folio = "4711" };

    [Fact]
    public void TrabajoNuevo_EmpiezaEnReclamado()
    {
        ExportJournal journal = New();
        journal.Begin("exp_1", "equipo-1", attempts: 1);

        ExportRecord? record = journal.Find("exp_1");

        Assert.NotNull(record);
        Assert.Equal(ExportPhase.Claimed, record!.Phase);
        Assert.False(record.Uncertain);
    }

    [Fact]
    public void CorteAntesDeEjecutar_NoDejaIncertidumbre()
    {
        // Si nunca se tocó SAP, repetir el trabajo es seguro y no hay nada que advertir.
        New().Begin("exp_1", "equipo-1", 1);

        var tras = New();
        tras.Recover();

        Assert.False(tras.Find("exp_1")!.Uncertain);
    }

    [Fact]
    public void CorteDurenteLaEscritura_DejaElTrabajoIncierto()
    {
        // El caso peligroso: la aplicación muere con SAP a medio escribir. Al arrancar hay que
        // marcarlo, porque si Graph vuelve a servir el trabajo NO se puede ejecutar a ciegas.
        ExportJournal journal = New();
        journal.Begin("exp_1", "equipo-1", 1);
        journal.Mark("exp_1", ExportPhase.Executing);

        var tras = New();
        tras.Recover();

        ExportRecord record = tras.Find("exp_1")!;
        Assert.True(record.Uncertain);
        Assert.NotEmpty(record.UncertainNote);
    }

    [Fact]
    public void CorteTrasVerificar_TambienEsIncierto()
    {
        // SAP ya confirmó, pero el resultado no llegó a construirse: la nota está escrita y Graph no
        // lo sabe. Reejecutar duplicaría.
        ExportJournal journal = New();
        journal.Begin("exp_1", "equipo-1", 1);
        journal.Mark("exp_1", ExportPhase.Verified);

        var tras = New();
        tras.Recover();

        Assert.True(tras.Find("exp_1")!.Uncertain);
    }

    [Fact]
    public void ResultadoGuardadoSinAck_SeDevuelveParaReenviar()
    {
        ExportJournal journal = New();
        journal.Begin("exp_1", "equipo-1", 1);
        journal.Mark("exp_1", ExportPhase.Executing);
        journal.SaveResult("exp_1", Result());

        var tras = New();
        IReadOnlyList<ExportRecord> pendientes = tras.Recover();

        Assert.Single(pendientes);
        Assert.Equal("exp_1", pendientes[0].ExportId);
        Assert.Equal("ok", pendientes[0].Result!.Outcome);
    }

    [Fact]
    public void ResultadoGuardado_ResuelveLaIncertidumbre()
    {
        // Tener el resultado ya decidido es exactamente lo que faltaba: deja de ser incierto.
        ExportJournal journal = New();
        journal.Begin("exp_1", "equipo-1", 1);
        journal.MarkUncertain("exp_1", "una corrida anterior murió");
        journal.SaveResult("exp_1", Result());

        Assert.False(journal.Find("exp_1")!.Uncertain);
    }

    [Fact]
    public void ElResultadoConservaLaIdentidadDelClaim()
    {
        // Graph compara `claimed_by` como string exacto. Si el reenvío tras un reinicio cambiara de
        // identidad, el result daría 409, bloquearía el lease diez minutos y quemaría un intento.
        ExportJournal journal = New();
        journal.SaveResult("exp_1", Result());

        Assert.Equal("equipo-1", New().Find("exp_1")!.Result!.Device);
    }

    [Fact]
    public void AckRecibido_CierraElTrabajo()
    {
        ExportJournal journal = New();
        journal.Begin("exp_1", "equipo-1", 1);
        journal.SaveResult("exp_1", Result());
        journal.Complete("exp_1");

        Assert.Null(journal.Find("exp_1"));
        Assert.Empty(New().Recover());
    }

    [Fact]
    public void LaFaseNuncaRetrocede()
    {
        // Reclamar de nuevo un trabajo que ya llegó a Executing no puede rebajar su fase: eso
        // borraría justo la señal que impide la doble escritura.
        ExportJournal journal = New();
        journal.Begin("exp_1", "equipo-1", 1);
        journal.Mark("exp_1", ExportPhase.Executing);
        journal.Begin("exp_1", "equipo-1", 2);

        Assert.Equal(ExportPhase.Executing, journal.Find("exp_1")!.Phase);
    }

    [Fact]
    public void ArchivoCorrupto_SeDescarta_NoRompeElArranque()
    {
        // Reenviar un resultado que no se puede leer sería inventar; y un JSON truncado no puede
        // impedir que el ejecutor arranque.
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "exp_roto.json"), "{ esto no es json");

        Assert.Empty(New().Recover());
    }

    [Fact]
    public void VariosTrabajos_SeRecuperanEnOrdenDeAntiguedad()
    {
        ExportJournal journal = New();
        journal.Begin("exp_1", "equipo-1", 1);
        journal.SaveResult("exp_1", Result());
        Thread.Sleep(15); // los sellos son ISO-8601 con milisegundos
        journal.Begin("exp_2", "equipo-1", 1);
        journal.SaveResult("exp_2", Result("error"));

        IReadOnlyList<ExportRecord> pendientes = New().Recover();

        Assert.Equal(2, pendientes.Count);
        Assert.Equal("exp_1", pendientes[0].ExportId);
    }
}
