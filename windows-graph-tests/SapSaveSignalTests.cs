using U.Graph.NoteExport;
using U.Graph.Surfaces;
using Xunit;

namespace U.Graph.Tests;

/// <summary>
/// La regla que sostiene el producto entero: <c>ok</c> exige la señal de éxito de SAP.
///
/// Cada prueba de aquí corresponde a una forma concreta de reportar un éxito falso. Si alguna se
/// vuelve incómoda y alguien la relaja, lo que se relaja es la garantía de que «exportada» en el
/// portal del médico signifique que la nota está en la historia clínica.
/// </summary>
public class SapSaveSignalTests
{
    private static SapGuiSurface.SapStatusMessage Msg(string type, string text = "texto", string code = "V1-311") =>
        new(type, text, code);

    [Fact]
    public void SinMensaje_NoEsExito()
    {
        ExportDecision decision = SapSaveSignal.Classify(baseline: null, observed: null);

        Assert.NotEqual(ExportVerdict.Proceed, decision.Verdict);
        Assert.Equal("SAP_SIN_CONFIRMACION", decision.ErrorCode);
    }

    [Fact]
    public void MensajeIdenticoAlPrevio_NoConfirmaEsteGuardado()
    {
        // El caso que produciría el peor fallo posible: un «Documento grabado» que llevaba media hora
        // en pantalla leído como el éxito de la exportación de ahora.
        var previo = Msg("S", "Documento 4711 grabado", "V1-311");

        ExportDecision decision = SapSaveSignal.Classify(previo, Msg("S", "Documento 4711 grabado", "V1-311"));

        Assert.NotEqual(ExportVerdict.Proceed, decision.Verdict);
        Assert.Equal("MENSAJE_PREVIO", decision.DetailCode);
    }

    [Fact]
    public void MensajeDeExitoNuevo_SiConfirma()
    {
        var previo = Msg("S", "Otra cosa de antes", "V1-100");

        ExportDecision decision = SapSaveSignal.Classify(previo, Msg("S", "Documento 4711 grabado", "V1-311"));

        Assert.Equal(ExportVerdict.Proceed, decision.Verdict);
    }

    [Fact]
    public void PrimerMensajeDeExitoSinLineaBase_SiConfirma()
    {
        ExportDecision decision = SapSaveSignal.Classify(baseline: null, observed: Msg("S"));

        Assert.Equal(ExportVerdict.Proceed, decision.Verdict);
    }

    [Fact]
    public void ErrorDeValidacion_PideIntervencion_NoFallo()
    {
        // Un mensaje E es casi siempre una validación de negocio: reintentar lo mismo daría lo mismo.
        // El médico debe ver «Requiere acción», no «falló, reintenta».
        ExportDecision decision = SapSaveSignal.Classify(null, Msg("E", "Rellene los campos obligatorios", "V1-042"));

        Assert.Equal(ExportVerdict.NeedsDoctor, decision.Verdict);
        Assert.NotNull(decision.UnresolvedFields);
        Assert.NotEmpty(decision.UnresolvedFields!);
    }

    [Fact]
    public void ErrorDeValidacion_NoFiltraElTextoDeSap()
    {
        // El texto de un mensaje de SAP puede nombrar al paciente. Solo puede viajar el código.
        const string conPhi = "El paciente Juan Pérez no admite esta nota";

        ExportDecision decision = SapSaveSignal.Classify(null, Msg("E", conPhi, "V1-042"));

        Assert.DoesNotContain("Juan", string.Join(" ", decision.UnresolvedFields!));
        Assert.Contains("V1-042", string.Join(" ", decision.UnresolvedFields!));
    }

    [Fact]
    public void Aborto_EsFalloTecnico()
    {
        ExportDecision decision = SapSaveSignal.Classify(null, Msg("A", "Se canceló", "00-001"));

        Assert.Equal(ExportVerdict.Fail, decision.Verdict);
        Assert.Equal("SAP_ABORTO", decision.ErrorCode);
    }

    [Theory]
    [InlineData("W")]  // advertencia
    [InlineData("I")]  // información
    [InlineData("")]   // sin tipo
    public void MensajesQueNoConfirman_NoSonExito(string tipo)
    {
        ExportDecision decision = SapSaveSignal.Classify(null, Msg(tipo, "algo", "X-1"));

        Assert.NotEqual(ExportVerdict.Proceed, decision.Verdict);
    }

    [Theory]
    [InlineData("Documento 4711 grabado", "4711")]
    [InlineData("Se creó el caso 0001234567 correctamente", "0001234567")]
    [InlineData("Grabado", "")]                       // sin nada parecido a un folio
    [InlineData("", "")]
    public void FolioFrom_ExtraeElIdentificadorCuandoLoHay(string texto, string esperado)
    {
        Assert.Equal(esperado, SapSaveSignal.FolioFrom(texto));
    }
}
