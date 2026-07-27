using System.Security.Cryptography;
using System.Text;

namespace U.Graph.Surfaces;

/// <summary>
/// Cómo se resume la estructura de una pantalla en una cadena corta y comparable.
///
/// Una sola implementación para las dos superficies, a propósito: si UIA y SAP calcularan la huella de
/// formas distintas, dos veredictos sobre la misma pregunta —«¿es esta la misma pantalla?»— podrían
/// discrepar, y ya sabemos cómo acaba eso (SurfacePlace nació de tener tres comparadores de lugar).
///
/// Reglas: se ORDENA (el orden de enumeración de un árbol de UI no es estable entre lecturas), se
/// DEDUPLICA (dos filas iguales no son más pantalla que una), y se corta a 12 hex — es una firma para
/// detectar cambios, no un identificador criptográfico, y en el registro tiene que caber.
/// </summary>
public static class Fingerprints
{
    public static string Of(IEnumerable<string> ids)
    {
        var clean = ids.Where(s => !string.IsNullOrWhiteSpace(s))
                       .Select(s => s.Trim())
                       .Distinct(StringComparer.Ordinal)
                       .OrderBy(s => s, StringComparer.Ordinal)
                       .ToList();
        if (clean.Count == 0) return "";

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", clean)));
        var sb = new StringBuilder(12 + 6);
        for (int i = 0; i < 6; i++) sb.Append(hash[i].ToString("x2"));
        // El recuento va delante y en claro: al leer el log, «36:a1b2…» dice de un vistazo si la
        // diferencia es de tamaño (media pantalla sin cargar) o de composición (otra pantalla).
        return $"{clean.Count}:{sb}";
    }
}
