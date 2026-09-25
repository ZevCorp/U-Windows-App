namespace U.Ciclo;

/// <summary>Un rectángulo en píxeles FÍSICOS de pantalla, que es lo que dan UIA y SetCursorPos.</summary>
public readonly record struct Caja(int X, int Y, int Ancho, int Alto);

/// <summary>Lo que trae la lectura, antes de decidir si se ofrece.</summary>
public sealed record Crudo(string Nombre, string Tipo, Caja Caja, bool Habilitado, bool FueraDePantalla);

/// <summary>
/// Un accionable de ESTE ciclo. Su identidad es el número (promesa 431): la etiqueta no identifica —en
/// Configuración hay un «Sistema» botón y un «Sistema» elemento de lista, y en main la mano se paraba a
/// preguntar cuál (2026-09-24, 22:43)—.
/// </summary>
public sealed record Accionable(int Numero, string Nombre, string Tipo, Caja Caja)
{
    public string Id => $"{Numero}) {Nombre} ({Tipo})";
}

public static class Accionables
{
    /// <summary>
    /// Numera en orden de lectura lo que se ve, y SOLO lo que se ve (promesa 432): fuera de pantalla,
    /// sin tamaño, deshabilitado o sin nombre no se le ofrece a Jev — elegiría algo que no se puede pulsar.
    /// </summary>
    public static IReadOnlyList<Accionable> Numerar(IEnumerable<Crudo> crudos)
    {
        var salida = new List<Accionable>();
        foreach (var c in crudos ?? Array.Empty<Crudo>())
        {
            if (c == null || c.FueraDePantalla || !c.Habilitado) continue;
            if (c.Caja.Ancho <= 0 || c.Caja.Alto <= 0) continue;
            string nombre = (c.Nombre ?? "").Trim();
            if (nombre.Length == 0) continue;
            if (nombre.Length > 80) nombre = nombre[..80] + "…";
            salida.Add(new Accionable(salida.Count + 1, nombre, c.Tipo ?? "", c.Caja));
        }
        return salida;
    }

    /// <summary>
    /// La huella de una pantalla: nombres, tipos y cajas. Es lo que decide si un clic «agarró» (promesa 436).
    /// Main miraba la VENTANA, que no cambia al pasar de Inicio a Sistema en Configuración, y esperaba 1,8 s
    /// por nada; los accionables sí cambian.
    /// </summary>
    public static string Huella(IReadOnlyList<Accionable> lista) => Huella(lista, Array.Empty<string>());

    /// <summary>
    /// La huella con los textos (promesa 443). En la Calculadora pulsar «7» no cambia ningún botón: cambia
    /// «La pantalla muestra 7». Sin los textos, el banco del 2026-09-24 vio «no cambió» tres veces seguidas
    /// y el motor paró por repetición con el clic funcionando.
    /// </summary>
    public static string Huella(IReadOnlyList<Accionable> lista, IReadOnlyList<string> textos)
    {
        unchecked
        {
            long h = 1469598103934665603;
            foreach (var t in textos ?? Array.Empty<string>())
                foreach (char ch in t) h = (h ^ ch) * 1099511628211;
            foreach (var a in lista)
            {
                foreach (char ch in a.Nombre) h = (h ^ ch) * 1099511628211;
                foreach (char ch in a.Tipo) h = (h ^ ch) * 1099511628211;
                h = (h ^ a.Caja.X) * 1099511628211;
                h = (h ^ a.Caja.Y) * 1099511628211;
            }
            return lista.Count + ":" + (textos?.Count ?? 0) + ":" + h.ToString("x");
        }
    }
}

/// <summary>Lo que se lee en un ciclo: lo que se puede pulsar, y lo que la pantalla dice.</summary>
public sealed record Lectura(IReadOnlyList<Accionable> Accionables, IReadOnlyList<string> Textos)
{
    public static readonly Lectura Vacia = new(Array.Empty<Accionable>(), Array.Empty<string>());
    public string Huella => U.Ciclo.Accionables.Huella(Accionables, Textos);
}

/// <summary>Todo lo que Jev necesita para decidir UNA vuelta, incluido lo ya hecho (promesa 442).</summary>
public sealed record Contexto(string Pantalla, string Objetivo, IReadOnlyList<Accionable> Accionables,
    IReadOnlyList<string> Textos, IReadOnlyList<string> Hecho);

/// <summary>
/// Qué ventanas de encima cuentan como parte de la de delante (promesa 444). Las del mismo proceso no
/// basta: la barra de tareas y el escritorio son de explorer.exe, y el banco del 2026-09-24 leyó 60
/// «accionables» del Explorador en 319 ms porque se llevaba la barra entera.
/// </summary>
public static class Emergentes
{
    public static IReadOnlyList<IntPtr> Elegir(IntPtr delante, IReadOnlyList<(IntPtr Ventana, IntPtr Duenno, string Clase, bool MismoProceso)> encima)
    {
        var salida = new List<IntPtr>();
        foreach (var (v, dueno, clase, mismo) in encima ?? Array.Empty<(IntPtr, IntPtr, string, bool)>())
        {
            if (!mismo) continue;
            // Suya: la ventana de delante es su dueña. O un menú clásico (#32768), que no tiene dueño pero
            // solo existe mientras alguien de ese proceso lo tiene abierto.
            if (dueno == delante || clase == "#32768") salida.Add(v);
        }
        return salida;
    }
}
