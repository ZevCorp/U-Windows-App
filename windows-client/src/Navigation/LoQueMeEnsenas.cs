using System.IO;
using System.Text.Json;

namespace U.WindowsClient.Navigation;

/// <summary>
/// LO QUE ME ENSEÑAS: qué es cada cosa y para qué sirve, con la foto de cuando me lo dijiste.
/// </summary>
/// <remarks>
/// El grafo YA aprende caminos: cada pulsación que cambia de pantalla deja un tramo, y eso salió
/// solo, sin construir nada (medido el 2026-08-23: cinco tramos aprendidos en una tarde de pruebas).
/// Lo que no sabía guardar es lo otro, y es lo que se pidió el primer día: <em>«ves esto de aquí,
/// aquí vas a escribir X cuando Y»</em>. Un camino no es un significado.
///
/// Y la diferencia importa para lo que se quiere hacer con esto: <em>«quiero poder pedirle tareas
/// por su significado, no solo ve a x lugar»</em>. Para eso el sistema tiene que saber que ese campo
/// ES el número de factura — no que está en el tercer nivel de una pantalla.
///
/// LA FOTO NO ES UN ADORNO. Se guarda una captura de cuando se enseñó porque el significado se dice
/// mirando algo, y sin ese algo la frase se queda coja: «aquí va el número» no se entiende sin ver
/// dónde era «aquí». Además es lo único que permite revisar después si lo aprendido sigue teniendo
/// sentido cuando la pantalla cambie (2026-08-23, pedido por el usuario).
///
/// SE GUARDA POR SELECTOR, no por nombre. Mañana habrá tres cosas que se llamen «Guardar»; el
/// selector es lo único que vuelve a encontrar la misma. Es la misma razón por la que señalar
/// devuelve el selector.
///
/// LO QUE SE SEÑALA SE ANOTA AUNQUE NO SE DIGA NADA, con el significado vacío. Enseñar es un gesto
/// de dos tiempos —se apunta y luego se explica— y guardar solo cuando llega la explicación
/// perdería el primero. Una anotación sin significado no estorba: dice «esto lo miraste».
/// </remarks>
public sealed class LoQueMeEnsenas
{
    /// <summary>Una cosa enseñada. <paramref name="Significado"/> vacío = todavía solo señalada.</summary>
    public sealed record Ensenanza(
        string Superficie, string Selector, string Etiqueta, string Tipo,
        string Significado, string Captura, DateTime Cuando);

    private readonly string _carpeta;
    private readonly List<Ensenanza> _todas = new();
    private readonly object _llave = new();

    public LoQueMeEnsenas(string? carpeta = null)
    {
        _carpeta = carpeta ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "U", "ensenanzas");
        Cargar();
    }

    public string Carpeta => _carpeta;
    private string Archivo => Path.Combine(_carpeta, "ensenanzas.json");

    public IReadOnlyList<Ensenanza> Todas { get { lock (_llave) return _todas.ToList(); } }

    /// <summary>
    /// Se acaba de señalar algo. Se anota con su foto y sin significado todavía.
    ///
    /// Si eso mismo ya estaba anotado NO se duplica: se le pone la foto nueva y se conserva el
    /// significado que ya tuviera. Señalar dos veces la misma cosa es mirarla dos veces, no
    /// aprender dos cosas — y perder el significado por volver a mirar sería un castigo absurdo.
    /// </summary>
    public Ensenanza Senalado(string superficie, string selector, string etiqueta, string tipo, string captura)
    {
        lock (_llave)
        {
            var ya = Buscar(superficie, selector);
            var nueva = new Ensenanza(superficie, selector, etiqueta, tipo,
                ya?.Significado ?? "", captura.Length > 0 ? captura : ya?.Captura ?? "", DateTime.UtcNow);
            if (ya != null) _todas.Remove(ya);
            _todas.Add(nueva);
            Guardar();
            return nueva;
        }
    }

    /// <summary>
    /// «Esto es X» / «aquí va el número de factura». Devuelve false si eso no se ha señalado nunca:
    /// enseñar el significado de algo que nadie ha mirado sería guardar una frase sin sujeto.
    /// </summary>
    public bool Significa(string superficie, string selector, string significado)
    {
        if (string.IsNullOrWhiteSpace(significado)) return false;
        lock (_llave)
        {
            var ya = Buscar(superficie, selector);
            if (ya == null) return false;
            _todas.Remove(ya);
            _todas.Add(ya with { Significado = significado.Trim(), Cuando = DateTime.UtcNow });
            Guardar();
            return true;
        }
    }

    /// <summary>Lo enseñado en una pantalla, para poder contarlo al llegar.</summary>
    public IReadOnlyList<Ensenanza> De(string superficie)
    {
        lock (_llave)
            return _todas.Where(e => e.Superficie.Equals(superficie, StringComparison.OrdinalIgnoreCase)
                                     && e.Significado.Length > 0).ToList();
    }

    private Ensenanza? Buscar(string superficie, string selector) =>
        _todas.FirstOrDefault(e => e.Superficie.Equals(superficie, StringComparison.OrdinalIgnoreCase)
                                && e.Selector.Equals(selector, StringComparison.OrdinalIgnoreCase));

    private void Cargar()
    {
        try
        {
            if (!File.Exists(Archivo)) return;
            var leidas = JsonSerializer.Deserialize<List<Ensenanza>>(File.ReadAllText(Archivo));
            if (leidas != null) _todas.AddRange(leidas);
        }
        catch (Exception e) { Diagnostics.LogBus.Log("enseñar", $"no pude leer lo enseñado: {e.Message}"); }
    }

    private void Guardar()
    {
        try
        {
            Directory.CreateDirectory(_carpeta);
            File.WriteAllText(Archivo, JsonSerializer.Serialize(_todas, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) { Diagnostics.LogBus.Log("enseñar", $"no pude guardar lo enseñado: {e.Message}"); }
    }
}
