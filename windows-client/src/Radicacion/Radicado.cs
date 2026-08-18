using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace U.WindowsClient.Radicacion;

public enum EstadoRadicado { Pendiente, Confirmado }

/// <summary>
/// Un registro de radicación. El número es PROVISIONAL a propósito: Miracle solo debe generar el
/// número OFICIAL cuando esté integrado con el sistema institucional de radicación (condición
/// explícita del cliente). Hasta entonces prepara el registro, deja los documentos validados, y
/// queda a la espera de confirmación humana — que es justo lo que este panel hace.
/// </summary>
public sealed class Radicado
{
    [JsonPropertyName("numero")] public string Numero { get; set; } = "";
    [JsonPropertyName("fechaHoraUtc")] public DateTime FechaHoraUtc { get; set; }
    [JsonPropertyName("archivoOriginal")] public string ArchivoOriginal { get; set; } = "";
    [JsonPropertyName("documento")] public DocumentoClasificado Documento { get; set; } = new();
    [JsonPropertyName("areaClave")] public string AreaClave { get; set; } = "";
    [JsonPropertyName("areaNombre")] public string AreaNombre { get; set; } = "";
    [JsonPropertyName("correoDestinoFicticio")] public string CorreoDestinoFicticio { get; set; } = "";
    [JsonPropertyName("estado")] public EstadoRadicado Estado { get; set; } = EstadoRadicado.Pendiente;
    [JsonPropertyName("correoEnviado")] public bool CorreoEnviado { get; set; }
}

/// <summary>
/// Asigna el número provisional y persiste cada radicado como una línea en un .jsonl append-only —
/// no hace falta una base de datos para una demo, y así queda auditable con solo abrir el archivo.
/// </summary>
public static class RadicadorLocal
{
    private static readonly object _lock = new();

    private static string Carpeta => Path.Combine(U.Graph.UserPaths.Local, "U", "radicacion");
    private static string RutaLog => Path.Combine(Carpeta, "radicados.jsonl");

    /// <summary>PROV-AAAAMMDD-NNNN, NNNN es el consecutivo del día contando las líneas ya escritas hoy.</summary>
    public static string GenerarNumeroProvisional()
    {
        lock (_lock)
        {
            string hoy = DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            int consecutivo = 1;
            if (File.Exists(RutaLog))
            {
                foreach (var linea in File.ReadLines(RutaLog))
                {
                    if (linea.Contains($"\"PROV-{hoy}-", StringComparison.Ordinal)) consecutivo++;
                }
            }
            return $"PROV-{hoy}-{consecutivo:D4}";
        }
    }

    public static void Registrar(Radicado radicado)
    {
        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(Carpeta);
                string linea = JsonSerializer.Serialize(radicado);
                File.AppendAllText(RutaLog, linea + Environment.NewLine);
            }
            catch (Exception e)
            {
                Diagnostics.LogBus.Log("radicacion", $"no se pudo guardar el radicado {radicado.Numero} en disco: {e.Message}");
            }
        }
    }
}
