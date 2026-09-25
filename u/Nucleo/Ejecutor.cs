namespace U.Ciclo;

/// <summary>Cómo acabó un plan: el resultado sobre el plan entero, y una línea por paso para contárselo a Luna.</summary>
public sealed record EjecucionDelPlan(PlanResultado Resultado, IReadOnlyList<string> Detalle)
{
    public string Relato() => Resultado.Resumen + "\n" + string.Join("\n", Detalle);
}

/// <summary>
/// EJECUTA EL PLAN DE LUNA, paso a paso, y para en el primero que falla (promesas 445-446).
///
/// Un paso con prefijo es un gesto que no necesita decidir nada —«abre: notepad», «escribe: hola»,
/// «tecla: Enter»— y no gasta 200 ms de Jev. Un paso sin prefijo es un objetivo, y ese sí va al ciclo.
/// </summary>
public sealed class Ejecutor
{
    private readonly Func<string, bool> _abrir;
    private readonly Action<string> _escribir;
    private readonly Func<string, bool> _tecla;
    private readonly Func<string, IReadOnlyList<string>, Recorrido> _objetivo;
    private readonly Func<bool> _hayQueParar;

    /// <summary>Cada paso en cuanto termina, para la burbuja y el log.</summary>
    public Action<string>? AlTerminarPaso { get; set; }

    public Ejecutor(Func<string, bool> abrir, Action<string> escribir, Func<string, bool> tecla,
        Func<string, IReadOnlyList<string>, Recorrido> objetivo, Func<bool> hayQueParar)
    {
        _abrir = abrir; _escribir = escribir; _tecla = tecla; _objetivo = objetivo; _hayQueParar = hayQueParar;
    }

    public EjecucionDelPlan Ejecutar(IReadOnlyList<string> pasos)
    {
        var hechos = new List<bool>();
        var hecho = new List<string>();     // lo que se hizo, en la voz de quien lo cuenta: viaja a Jev
        var detalle = new List<string>();

        foreach (var paso in pasos)
        {
            if (_hayQueParar()) { detalle.Add("Escape: paré antes de «" + paso + "»"); break; }

            bool ok; string linea;
            if (Prefijo(paso, "abre:", out var app))
            {
                ok = _abrir(app);
                linea = ok ? $"abrí «{app}»" : $"no pude abrir «{app}»";
            }
            else if (Prefijo(paso, "escribe:", out var texto))
            {
                _escribir(texto);
                ok = true;
                linea = $"escribí «{texto}»";
            }
            else if (Prefijo(paso, "tecla:", out var tecla))
            {
                ok = _tecla(tecla);
                linea = ok ? $"pulsé la tecla «{tecla}»" : $"no conozco la tecla «{tecla}»";
            }
            else
            {
                var r = _objetivo(paso, hecho.ToArray());
                ok = r.Cumplido;
                linea = $"«{paso}»: " + (ok ? "cumplido" : r.PorQueParo);
            }

            hechos.Add(ok);
            detalle.Add((ok ? "✔ " : "✘ ") + linea);
            try { AlTerminarPaso?.Invoke(detalle[^1]); } catch { }
            if (!ok) break;
            hecho.Add(linea);
        }
        return new EjecucionDelPlan(Plan.Resultado(pasos, hechos), detalle);
    }

    /// <summary>
    /// Cuánto se espera a que la pantalla cambie tras una tecla (promesa 453). Enter navega —envía una búsqueda,
    /// abre una carpeta— y la página tarda: en Chrome, el «mirar» 25 ms después del Enter aún veía «Nueva
    /// pestaña», y Luna repitió la búsqueda entera creyendo que había fallado (2026-09-24, 23:22).
    /// Se sale en cuanto cambia (Asentado): una tecla que responde rápido no paga el techo.
    /// </summary>
    public static int EsperaTrasTecla(string tecla) =>
        (tecla ?? "").Split('+').Last().Trim().ToLowerInvariant() is "enter" or "intro" ? 1500 : 150;

    /// <summary>
    /// Cuánto se espera, como mucho, a que la app termine de teclear (promesa 459). SendInput vuelve en cuanto encola
    /// las teclas; el Bloc de notas las consume a ~12 ms por carácter, y el «mirar» de 200 ms después veía «*pru»,
    /// «*prue», «*prueb»: Luna creía que faltaba texto y lo volvía a escribir (rondas del 2026-09-25, 02:21 y 05:34).
    /// </summary>
    public static int EsperaTrasEscribir(string texto) => Math.Min(1500, 150 + 15 * (texto ?? "").Length);

    private static bool Prefijo(string paso, string prefijo, out string resto)
    {
        var p = (paso ?? "").TrimStart();
        if (p.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase)) { resto = p[prefijo.Length..].Trim(); return true; }
        resto = "";
        return false;
    }
}

/// <summary>
/// LO QUE SE LE DEVUELVE A LUNA CABE (promesa 447). Main midió el tope contra el servidor el 2026-09-12:
/// un resultado de 40 KB dejó la llamada pendiente y cada turno de la sesión falló después. Se deja margen
/// bajo los 32.768 del mensaje entero, y lo que se corta se dice.
/// </summary>
public static class ParaLuna
{
    public const int Tope = 30_000;

    public static string Recortar(string texto)
    {
        texto ??= "";
        var utf8 = System.Text.Encoding.UTF8;
        int total = utf8.GetByteCount(texto);
        if (total <= Tope) return texto;
        string cola(int n) => $"…[recortado: {n} de {total} bytes]";
        int bajo = 0, alto = texto.Length;
        while (bajo < alto)
        {
            int medio = bajo + (alto - bajo + 1) / 2;
            int m = medio > 0 && char.IsHighSurrogate(texto[medio - 1]) ? medio - 1 : medio;
            int bytes = utf8.GetByteCount(texto.AsSpan(0, m));
            if (bytes + utf8.GetByteCount(cola(bytes)) <= Tope) bajo = medio; else alto = medio - 1;
        }
        int corte = bajo > 0 && char.IsHighSurrogate(texto[bajo - 1]) ? bajo - 1 : bajo;
        string principio = texto[..corte];
        return principio + cola(utf8.GetByteCount(principio));
    }
}
