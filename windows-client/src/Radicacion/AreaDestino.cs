using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace U.WindowsClient.Radicacion;

/// <summary>Un área a la que puede enrutarse un documento radicado.</summary>
public sealed class AreaDestino
{
    public string Clave { get; set; } = "";
    public string Nombre { get; set; } = "";

    /// <summary>
    /// Correo FICTICIO de esta demo (no existe de verdad). El envío real sale a la casilla que
    /// verificó la cuenta de Resend; este valor se muestra en el asunto/cuerpo del correo y en el
    /// registro local como "a dónde habría llegado" — ver EnviadorCorreo.
    /// </summary>
    public string CorreoFicticio { get; set; } = "";
}

/// <summary>
/// Carga y persiste las áreas de radicación. Vive en un JSON editable a mano
/// (%LOCALAPPDATA%\U\radicacion\areas-radicacion.json) para que la demo se pueda ajustar entre
/// corridas sin recompilar — mismo espíritu que Config.Load() en Config.cs, pero aparte: este
/// archivo no tiene nada que ver con la configuración del cliente central.
/// </summary>
public static class AreasConfig
{
    private static string Ruta =>
        Path.Combine(U.Graph.UserPaths.Local, "U", "radicacion", "areas-radicacion.json");

    private static readonly List<AreaDestino> Defecto = new()
    {
        new() { Clave = "pqrd", Nombre = "PQRD — Servicio al Cliente", CorreoFicticio = "pqrd@demo-miracle.local" },
        new() { Clave = "incapacidades", Nombre = "Incapacidades", CorreoFicticio = "incapacidades@demo-miracle.local" },
        new() { Clave = "licencias", Nombre = "Licencias", CorreoFicticio = "licencias@demo-miracle.local" },
        new() { Clave = "devolucion_aportes", Nombre = "Devolución de Aportes", CorreoFicticio = "devolucion.aportes@demo-miracle.local" },
        new() { Clave = "accidentes_laborales", Nombre = "Accidentes Laborales", CorreoFicticio = "accidentes.laborales@demo-miracle.local" },
        new() { Clave = "solicitudes_prestadores", Nombre = "Solicitudes de Prestadores", CorreoFicticio = "prestadores@demo-miracle.local" },
        new() { Clave = "facturacion", Nombre = "Facturación — Área Financiera", CorreoFicticio = "facturacion@demo-miracle.local" },
        new() { Clave = "otro", Nombre = "Otro / sin clasificar", CorreoFicticio = "correspondencia@demo-miracle.local" },
    };

    public static List<AreaDestino> Cargar()
    {
        try
        {
            if (File.Exists(Ruta))
            {
                var leidas = JsonSerializer.Deserialize<List<AreaDestino>>(File.ReadAllText(Ruta));
                if (leidas is { Count: > 0 }) return leidas;
            }
        }
        catch (Exception e)
        {
            Diagnostics.LogBus.Log("radicacion", $"no se pudo leer areas-radicacion.json, uso las por defecto: {e.Message}");
        }

        Guardar(Defecto);
        return Defecto;
    }

    private static void Guardar(List<AreaDestino> areas)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Ruta)!);
            File.WriteAllText(Ruta, JsonSerializer.Serialize(areas, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e)
        {
            Diagnostics.LogBus.Log("radicacion", $"no se pudo escribir areas-radicacion.json: {e.Message}");
        }
    }
}
