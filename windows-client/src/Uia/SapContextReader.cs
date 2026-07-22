using System.Text;
using U.Graph.Surfaces;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Uia;

/// <summary>
/// AÑADE (no reemplaza) SAP GUI Scripting al árbol de lectura de la UI que ve el cerebro. UIA apenas
/// ve nada dentro de SAP GUI; el scripting oficial sí: campos con nombre, tipo y valor actual. Cuando
/// la app en foco es SAP, este bloque se apendea al UiContext del turno para que el cerebro "vea" la
/// pantalla SAP de verdad (requiere sapgui/user_scripting habilitado en server y cliente).
/// </summary>
public sealed class SapContextReader
{
    private readonly SapGuiSurface _sap = new();

    /// <summary>Bloque de texto con la pantalla SAP actual, o null si SAP/scripting no está disponible.</summary>
    public string? Read()
    {
        try
        {
            var availability = _sap.Check();
            if (!availability.Available)
            {
                LogBus.Log("sap", $"scripting no disponible: {availability.Reason}");
                return null;
            }

            var id = _sap.Identity();
            var fields = _sap.ReadFields();
            var sb = new StringBuilder();
            sb.AppendLine($"PANTALLA SAP (vía SAP GUI Scripting) — {id.Url}{(string.IsNullOrWhiteSpace(id.Title) ? "" : $" · {id.Title}")}");
            sb.AppendLine($"Campos SAP visibles ({fields.Count}):");
            foreach (var f in fields.Take(80))
            {
                string val = string.IsNullOrWhiteSpace(f.CurrentValue) ? "" : $" = «{f.CurrentValue}»";
                sb.AppendLine($"- {f.Label} ({f.ControlType} · {f.ActionType}){val}");
            }
            return sb.ToString().TrimEnd();
        }
        catch (Exception e)
        {
            LogBus.Log("sap", $"lectura SAP falló: {e.Message}");
            return null;
        }
    }
}
