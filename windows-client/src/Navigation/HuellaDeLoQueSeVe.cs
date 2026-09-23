namespace U.WindowsClient.Navigation;

/// <summary>
/// LA HUELLA DE LO QUE SE VE: lo mínimo que hace falta para saber si la pantalla cambió, y por dónde.
/// Spec 047, promesas 352 y 355. Pura: no toca la pantalla; quien la toma es <see cref="HuellaEnVivo"/>.
/// </summary>
/// <remarks>
/// CUATRO PARTES, Y CADA UNA VE ALGO QUE LAS OTRAS NO (medido en el log del 2026-09-21, 16:34-16:39):
///
///   1. EL SITIO: la ubicación de trabajo. Es lo único que la espera de después de pulsar miraba hasta
///      hoy, y por eso 13 de 13 clics que no navegaban esperaron 1.797-1.825 ms a un cambio que no llegaba.
///   2. LA VENTANA DE DELANTE (hwnd + título, por Win32): «Minimizar» a las 16:35:00 cambió la ventana de
///      delante y el veredicto dijo «no cambió» porque la de trabajo seguía en el mismo sitio.
///   3. LO DE DENTRO: las identidades (RuntimeId + tipo) de los accionables de la ventana de trabajo. Un menú
///      que se abre aparece aquí y en ningún otro lado.
///   4. LAS VENTANAS DEL PROCESO de trabajo: el selector de perfiles de Chrome («isabel», 16:34:09) se abrió
///      como OTRA ventana de nivel superior —«(sin título)», log :395— y no cuelga del hwnd de Gmail, así que
///      leer la ventana de trabajo no lo ve. Esta parte sí.
///
/// UNA SOLA DEFINICIÓN DE «CAMBIÓ» (aprendizaje nº16): el 22-09 «cambió» se calculaba en 11 sitios con
/// criterios distintos —ubicación en unos, recuento de botones en otros—. Se construye y se compara aquí, y
/// solo aquí. La compuerta de vida (299) juzga solo sitio y dentro: <see cref="MismaPantallaQueVe"/>.
///
/// LO QUE NO VE, y se dice: un cambio de estado sin cambio de identidades (un botón que pasa a «pulsado», un
/// valor que cambia) es «nada». Para lo que la spec mide —menús, ventanas, navegaciones— basta.
/// </remarks>
public sealed class HuellaDeLoQueSeVe
{
    /// <summary>Qué cambió entre dos huellas, de menos a más: el orden es la prioridad de <see cref="Comparar"/>.</summary>
    public enum QueCambio { Nada, Dentro, Delante, DeSitio }

    /// <summary>
    /// Qué parte de la huella lo vio. «Ventanas» es un «Dentro» que solo la cuarta parte ve; «Titulo» es un «Delante» en el que
    /// la ventana es la misma y solo cambió su título.
    /// </summary>
    /// <remarks>
    /// «TITULO» VA AL FINAL a propósito: los números de las que ya había no se mueven. Nació el 2026-09-23 (revisión de la rama):
    /// «delante» es el hwnd y el título, y cualquier diferencia se contaba «otra ventana pasó al frente». En Gmail «marcar como
    /// leído» cambia «Recibidos (3)» por «Recibidos (2)» sin cambiar de ventana, y «Nuevo chat» retitula ChatGPT.exe: la cuenta
    /// afirmaba una causa que no podía distinguir (patrón nº2) y mandaba al modelo a buscar un diálogo que no existía.
    /// </remarks>
    public enum Parte { Nada, Sitio, Delante, Dentro, Ventanas, Titulo }

    public readonly record struct Diferencia(QueCambio QueCambio, Parte Parte)
    {
        public override string ToString() => QueCambio == QueCambio.Nada ? "nada" : $"{QueCambio} (lo vio: {Parte})";
    }

    /// <summary>Lo que costó tomar cada parte, en ms. Cero cuando la huella se fabricó a mano (el contrato).</summary>
    public readonly record struct Costes(long SitioMs, long DelanteMs, long DentroMs, long VentanasMs)
    {
        public static Costes operator +(Costes a, Costes b) =>
            new(a.SitioMs + b.SitioMs, a.DelanteMs + b.DelanteMs, a.DentroMs + b.DentroMs, a.VentanasMs + b.VentanasMs);
        public static Costes Max(Costes a, Costes b) =>
            new(Math.Max(a.SitioMs, b.SitioMs), Math.Max(a.DelanteMs, b.DelanteMs), Math.Max(a.DentroMs, b.DentroMs), Math.Max(a.VentanasMs, b.VentanasMs));
    }

    public string Sitio { get; }
    public string Delante { get; }
    /// <summary>Ordenadas: dos huellas con las mismas identidades en otro orden son la misma huella.</summary>
    public IReadOnlyList<string> Dentro { get; }
    /// <summary>Ordenadas, por la misma razón.</summary>
    public IReadOnlyList<string> Ventanas { get; }
    /// <summary>No entra en la igualdad: es medida, no identidad.</summary>
    public Costes Coste { get; init; }

    /// <summary>
    /// CUÁNTOS MS TENÍA LA LECTURA DE «DENTRO» al tomar esta huella: 0 = se leyó ahora. No entra en la igualdad: dice CUÁNDO se
    /// vio lo de dentro, no QUÉ se vio.
    /// </summary>
    /// <remarks>
    /// EXISTE PORQUE LA HUELLA REAL REUTILIZA LO DE DENTRO (<see cref="HuellaEnVivo"/>: como mucho una lectura por respiro, y la de
    /// ANTES de tocar es de la misma instancia). Hasta el 2026-09-23 la espera daba la pantalla por asentada con «dos huellas
    /// iguales, un respiro en medio y pasada la primera» medido en el reloj de los sondeos, y la última lectura real de dentro en
    /// la que se apoyaba podía ser de 240 ms antes, o la de antes de tocar: la regla se cumplía en el reloj y no en lo observado
    /// (revisión de la rama; bloqueaba). Con la edad, <see cref="EsperaAsentada"/> cuenta desde cuándo se leyó de verdad.
    /// Las huellas fabricadas a mano (el contrato) valen 0: siempre frescas.
    /// </remarks>
    public long EdadDeDentroMs { get; init; }

    private HuellaDeLoQueSeVe(string sitio, string delante, IReadOnlyList<string> dentro, IReadOnlyList<string> ventanas)
    {
        Sitio = sitio; Delante = delante; Dentro = dentro; Ventanas = ventanas;
    }

    /// <summary>
    /// «DELANTE» SE ESCRIBE AQUÍ, Y SE LEE AQUÍ: el hwnd en hexadecimal, «·» y el título. <see cref="Comparar"/> separa las dos
    /// partes por el mismo camino (aprendizaje nº16) para distinguir otra ventana al frente de la misma con otro título.
    /// </summary>
    public static string DelanteDe(IntPtr hwnd, string titulo) => hwnd == IntPtr.Zero ? "(ninguna)" : $"{hwnd:X}·{titulo}";

    /// <summary>La ventana de un «delante»: lo que va antes del primer «·». Sin «·» —una huella fabricada a mano— es todo.</summary>
    private static string VentanaDe(string delante)
    {
        int punto = delante.IndexOf('·');
        return punto < 0 ? delante : delante[..punto];
    }

    /// <summary>
    /// «DELANTE» PARA EL LOG: la ventana y cuánto mide su título, NUNCA el título. El título de la ventana de delante puede llevar
    /// datos —el nombre de un paciente, el asunto de un correo— y el log no se queda en esta máquina: <c>EspejoDelLog</c> sube cada
    /// línea al panel del backend. La cuenta al modelo ya lo excluía; el log lo escribía en cada pulsación, antes y después, hasta
    /// el 2026-09-23 (revisión de la rama). La igualdad sigue usando el título entero, en memoria.
    /// </summary>
    public static string DelanteParaElLog(string delante)
    {
        int punto = delante.IndexOf('·');
        if (punto >= 0) return $"{delante[..punto]}·(título de {delante.Length - punto - 1} car.)";
        return delante == "(ninguna)" ? delante : $"({delante.Length} car.)";
    }

    /// <summary>El ÚNICO camino para construir una: ordena, y normaliza el vacío como ausente (patrón nº9).</summary>
    public static HuellaDeLoQueSeVe De(string sitio, string delante, IEnumerable<string> identidades, IEnumerable<string> ventanas)
    {
        static IReadOnlyList<string> Ordena(IEnumerable<string>? xs) =>
            (xs ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        return new(sitio ?? "", delante ?? "", Ordena(identidades), Ordena(ventanas));
    }

    public HuellaDeLoQueSeVe ConCoste(Costes coste) => new(Sitio, Delante, Dentro, Ventanas) { Coste = coste, EdadDeDentroMs = EdadDeDentroMs };

    /// <summary>La misma huella, diciendo cuántos ms tenía su lectura de dentro (<see cref="EdadDeDentroMs"/>). Nunca negativa.</summary>
    public HuellaDeLoQueSeVe ConEdadDeDentro(long ms) => new(Sitio, Delante, Dentro, Ventanas) { Coste = Coste, EdadDeDentroMs = Math.Max(0, ms) };

    /// <summary>Las cuatro partes iguales. El coste no cuenta.</summary>
    public static bool Iguales(HuellaDeLoQueSeVe? a, HuellaDeLoQueSeVe? b) =>
        a != null && b != null && Comparar(a, b).QueCambio == QueCambio.Nada;

    /// <summary>
    /// Qué cambió de <paramref name="antes"/> a <paramref name="ahora"/>. El sitio manda sobre las otras tres; después
    /// la ventana de delante; después lo de dentro, y ahí la parte dice si lo vio la ventana de trabajo o una ventana
    /// nueva de su proceso.
    /// </summary>
    public static Diferencia Comparar(HuellaDeLoQueSeVe antes, HuellaDeLoQueSeVe ahora)
    {
        if (!string.Equals(antes.Sitio, ahora.Sitio, StringComparison.Ordinal)) return new(QueCambio.DeSitio, Parte.Sitio);
        // LA MISMA VENTANA CON OTRO TÍTULO NO ES OTRA VENTANA AL FRENTE: es un «delante» que vio el título (Parte.Titulo).
        if (!string.Equals(antes.Delante, ahora.Delante, StringComparison.Ordinal))
            return new(QueCambio.Delante, string.Equals(VentanaDe(antes.Delante), VentanaDe(ahora.Delante), StringComparison.Ordinal) ? Parte.Titulo : Parte.Delante);
        if (!antes.Dentro.SequenceEqual(ahora.Dentro, StringComparer.Ordinal)) return new(QueCambio.Dentro, Parte.Dentro);
        if (!antes.Ventanas.SequenceEqual(ahora.Ventanas, StringComparer.Ordinal)) return new(QueCambio.Dentro, Parte.Ventanas);
        return new(QueCambio.Nada, Parte.Nada);
    }

    /// <summary>
    /// El criterio de la compuerta de vida (299, spec 040): sitio y lo de dentro. «Delante» y «ventanas» no cuentan
    /// ahí, para que la 299 diga lo mismo que decía.
    /// </summary>
    public static bool MismaPantallaQueVe(HuellaDeLoQueSeVe a, HuellaDeLoQueSeVe b) =>
        string.Equals(a.Sitio, b.Sitio, StringComparison.Ordinal) && a.Dentro.SequenceEqual(b.Dentro, StringComparer.Ordinal);

    public override string ToString() => $"sitio «{Sitio}» · delante «{DelanteParaElLog(Delante)}» · dentro {Dentro.Count} · ventanas {Ventanas.Count}";
}
