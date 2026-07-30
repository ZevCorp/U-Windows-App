using U.Graph.NoteExport;
using Xunit;

namespace U.Graph.Tests;

/// <summary>
/// La compuerta del paciente. Lo que se puede probar sin SAP es su parte pura: extraer el nombre
/// técnico del campo y comparar identificadores como los escribe SAP.
///
/// La prueba más importante del archivo es la última, y no comprueba código: fija por escrito que
/// hoy el contrato NO transporta un identificador del paciente utilizable en el hospital. El día que
/// lo haga, esa prueba falla y obliga a mirar aquí — que es exactamente el sitio donde hay que
/// enchufarlo para que la verificación pase a ser automática.
/// </summary>
public class PatientGuardTests
{
    [Theory]
    [InlineData("sap:wnd[0]/usr/subPATEINST:SAPLNCHD:2000/txtRNPA1-PASSNR", "PASSNR")]
    [InlineData("sap:wnd[0]/usr/ctxtRNPA1-PATNR", "PATNR")]
    [InlineData("sap:wnd[0]/usr/txtRNPA-FALNR", "FALNR")]
    [InlineData("sap:wnd[0]/usr/txtSinGuion", "")]
    [InlineData("", "")]
    public void NombreTecnico_SaleDeLoQueVaTrasElGuion(string selector, string esperado)
    {
        // El prefijo del control (txt/ctxt) y la ruta del dynpro cambian entre pantallas; el nombre
        // ABAP que va tras el guion es lo estable, y es lo que identifica al campo.
        Assert.Equal(esperado, PatientGuard.TechnicalName(selector));
    }

    [Theory]
    [InlineData("0000012345", "12345", true)]   // SAP rellena con ceros a la izquierda
    [InlineData("12345", "0000012345", true)]
    [InlineData("12345", "12345", true)]
    [InlineData(" 12345 ", "12345", true)]
    [InlineData("12345", "54321", false)]
    [InlineData("", "12345", false)]
    [InlineData("00000", "12345", false)]       // un campo a ceros no coincide con nada
    public void Comparacion_ToleraElRellenoDeCeros(string pantalla, string esperado, bool coincide)
    {
        Assert.Equal(coincide, PatientGuard.Matches(pantalla, esperado));
    }

    [Fact]
    public void ElContratoNoTraeIdentificadorDelHospital_Todavia()
    {
        // Esto documenta un HECHO del sistema, no una preferencia: el payload identifica al paciente
        // con `patient_ref`, el uuid de Miracle, que SAP no conoce; y en el esquema de Miracle Notes
        // no existe ningún identificador institucional (solo `patients.documento`, texto libre y
        // opcional, que además no viaja). Por eso hoy la verificación la hace una persona.
        //
        // Cuando Graph incluya el identificador del HIS, esta prueba fallará. Es lo que se busca:
        // obliga a volver a HisPatientIdFrom, que es el único punto que hay que tocar para que la
        // compuerta pase a verificar sola.
        var payload = new ExportPayload
        {
            PatientRef = "00000000-0000-4000-8000-0000000000aa",
            RenderedText = "MOTIVO:\nAlgo.",
            Firma = new ExportSignature { Hash = "abc" },
        };

        Assert.Equal("", PatientGuard.HisPatientIdFrom(payload));
    }

    [Fact]
    public void SinTrabajo_TampocoHayIdentificador()
    {
        Assert.Equal("", PatientGuard.HisPatientIdFrom(null));
    }
}
