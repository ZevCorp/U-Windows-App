using System.IO;

namespace Medidor.App;

/// <summary>
/// El log del medidor: `%LOCALAPPDATA%\MedidorU\logs\medidor-AAAAMMDD.log`. El log es la fuente de
/// verdad para diagnosticar (regla de la casa) — pero AQUÍ con una restricción extra que el LogBus
/// de U.exe no tiene: en este log no entra NUNCA un título de ventana, un identificador de
/// paciente ni un valor de campo. Solo etiquetas, conteos y estados. El medidor promete no sacar
/// contenido clínico del PC, y un log que lo guardara sería sacarlo a medias.
/// </summary>
internal static class Registro
{
    private static readonly object Candado = new();
    private static bool _discoRoto;

    public static void Anota(string etiqueta, string mensaje)
    {
        var linea = $"[{DateTime.Now:HH:mm:ss}] {etiqueta}: {mensaje}";
        if (_discoRoto) return;
        try
        {
            lock (Candado)
                File.AppendAllText(
                    Path.Combine(Rutas.CarpetaDeLogs, $"medidor-{DateTime.Now:yyyyMMdd}.log"),
                    linea + Environment.NewLine);
        }
        catch
        {
            // Si el disco falla una vez, el archivo se apaga y el medidor sigue: un log no puede
            // tumbar al instrumento. (Mismo criterio que LogBus._fileBroken.)
            _discoRoto = true;
        }
    }

    public static void Excepcion(string etiqueta, Exception e)
    {
        // La cadena ENTERA (patrón nº3: un catch que se traga el motivo está prohibido).
        for (var x = e; x != null; x = x.InnerException)
            Anota(etiqueta, $"✘ {x.GetType().Name}: {x.Message}");
    }
}
