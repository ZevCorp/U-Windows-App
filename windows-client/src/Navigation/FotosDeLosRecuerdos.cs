using System.IO;

namespace U.WindowsClient.Navigation;

/// <summary>
/// DÓNDE VIVEN LAS FOTOS DE LOS RECUERDOS. Lo único de un recuerdo que se queda en disco.
/// </summary>
/// <remarks>
/// El significado vive en el grafo —es del elemento, como su etiqueta o su tipo— pero la imagen no:
/// un PNG de doscientos kilobytes dentro de un nodo engorda el grafo entero y no aporta nada a
/// ninguna consulta. Lo que se guarda ahí es la RUTA; el archivo queda aquí.
///
/// SOLO SE LLAMA CUANDO HAY UN RECUERDO DE VERDAD (2026-08-24, pedido por el usuario). Antes se
/// guardaba una foto cada vez que se señalaba algo, se le pusiera significado o no — la mayoría de
/// los señalados son solo mirar, no enseñar, y cada uno dejaba un PNG que nadie iba a leer nunca.
/// Guardar solo cuando <c>map_esto_es</c> tiene éxito es lo que hace que cada archivo aquí
/// signifique un recuerdo real.
///
/// Se guarda la VENTANA y no el recuadro del elemento: «aquí va el número de factura» se entiende
/// viendo el formulario entero, no un botón recortado. El contexto es la mitad del recuerdo.
///
/// Y con la fecha en el nombre, para poder mirar DESPUÉS si lo recordado sigue teniendo sentido
/// cuando la pantalla cambie — la única forma de ver que un recuerdo envejeció mal en vez de
/// enterarse el día que falla.
/// </remarks>
public static class FotosDeLosRecuerdos
{
    public static string Carpeta { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "U", "recuerdos", "fotos");

    /// <summary>Guarda la foto en JPEG y devuelve su ruta, o vacío si no se pudo.</summary>
    /// <remarks>
    /// JPEG DESDE EL 2026-09-16, y no por gusto: había 294 fotos en PNG ocupando 72 MB —252 KB de media— y
    /// para mirar una pantalla el PNG no aporta nada. Las que ya estaban se quedan como están: su ruta vive
    /// dentro del grafo, y reescribirla es otro trabajo; van cayendo solas seguún envejecen.
    /// </remarks>
    public static string Guardar(string nombre, byte[]? jpeg)
    {
        if (jpeg == null || jpeg.Length == 0) return "";
        try
        {
            string limpio = new string(nombre.Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray())
                .Trim().Replace(' ', '-').ToLowerInvariant();
            if (limpio.Length > 40) limpio = limpio[..40];
            if (limpio.Length == 0) limpio = "sin-nombre";

            Directory.CreateDirectory(Carpeta);
            string ruta = Path.Combine(Carpeta, $"{DateTime.Now:yyyyMMdd-HHmmss}-{limpio}.jpg");
            File.WriteAllBytes(ruta, jpeg);
            return ruta;
        }
        catch (Exception e)
        {
            Diagnostics.LogBus.Log("recuerdo", $"no pude guardar la foto: {e.Message}");
            return "";
        }
    }
}
