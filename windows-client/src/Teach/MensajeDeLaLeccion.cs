namespace U.WindowsClient.Teach;

/// <summary>Un bloque del mensaje que recibe el piloto: texto, o una imagen por su ruta.</summary>
public sealed record Bloque(string Tipo, string Texto = "", string Ruta = "");

/// <summary>
/// LA LECCIÓN, DICHA COMO UN TUTORIAL. Promesa 173 (spec 013). Puro: de la lección a bloques.
/// </summary>
/// <remarks>
/// EN ORDEN DE TIEMPO Y CON LA HORA DELANTE. Es la forma que la comunidad ya validó para que Claude
/// «vea» un video (cuadros + <c>t=MM:SS</c> + transcripción); lo que cambia aquí es de dónde salen
/// los cuadros: del clic, no del cambio de escena, que en SAP es ciego (1 cambio en 52 s con 5
/// pantallas, medido el 2026-09-06).
///
/// EL PRESUPUESTO. La API admite 600 imágenes, pero cada cuadro son ~1.200 tokens y el modelo
/// atiende peor cuanto más se le da. Si hay más cuadros que presupuesto, se recortan primero los
/// de DESPUÉS que repiten pantalla (misma huella que otro ya incluido), y NUNCA el de antes de un
/// clic con selector: ese es el que enseña qué se tocó.
///
/// NINGÚN CUADRO PASA DE <see cref="LadoMaximoPx"/>: con más de 20 imágenes la API rechaza la
/// petición entera si una sola pasa de 2000 px de lado. La cámara ya reduce a 1280; esto es el
/// cinturón por si algún día alguien le sube el ancho.
/// </remarks>
public static class MensajeDeLaLeccion
{
    public const int LadoMaximoPx = 2000;
    public const int PresupuestoPorDefecto = 60;

    public static IReadOnlyList<Bloque> Armar(Leccion leccion, int presupuestoDeCuadros = PresupuestoPorDefecto)
    {
        var bloques = new List<Bloque>();
        if (leccion == null) return bloques;

        var porRuta = leccion.Cuadros.ToDictionary(c => c.Ruta, c => c, StringComparer.OrdinalIgnoreCase);
        bool Cabe(string ruta) => porRuta.TryGetValue(ruta, out var c) && c.Ancho <= LadoMaximoPx && c.Alto <= LadoMaximoPx;

        // Qué cuadros entran, con el recorte por presupuesto.
        var quieren = new List<(string Ruta, bool Protegido, string Clave)>();
        foreach (var e in leccion.Eventos)
        {
            if (e.CuadroAntes.Length > 0 && Cabe(e.CuadroAntes))
                quieren.Add((e.CuadroAntes, e.Selector.Length > 0, "antes:" + e.N));
            if (e.CuadroDespues.Length > 0 && Cabe(e.CuadroDespues))
                quieren.Add((e.CuadroDespues, false, "despues:" + e.N));
        }
        var entran = new HashSet<string>(quieren.Select(q => q.Ruta), StringComparer.OrdinalIgnoreCase);
        if (entran.Count > presupuestoDeCuadros)
        {
            // Primero los de después que repiten una pantalla ya mostrada por otro cuadro.
            var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var q in quieren)
            {
                if (!entran.Contains(q.Ruta)) continue;
                string pantalla = PantallaDe(q.Ruta, leccion);
                if (q.Clave.StartsWith("despues:") && !vistos.Add(pantalla) && entran.Count > presupuestoDeCuadros)
                    entran.Remove(q.Ruta);
                else vistos.Add(pantalla);
            }
            // Después, si sigue sobrando, los de después a secas, del último al primero.
            foreach (var q in quieren.AsEnumerable().Reverse())
            {
                if (entran.Count <= presupuestoDeCuadros) break;
                if (q.Clave.StartsWith("despues:")) entran.Remove(q.Ruta);
            }
            // Y por último los de antes SIN selector. Los de antes con selector no se tocan.
            foreach (var q in quieren.AsEnumerable().Reverse())
            {
                if (entran.Count <= presupuestoDeCuadros) break;
                if (!q.Protegido && q.Clave.StartsWith("antes:")) entran.Remove(q.Ruta);
            }
        }

        bloques.Add(new Bloque("text",
            $"LECCIÓN «{leccion.Id}» · dura {Hora(leccion.DuracionMs)} · empieza en «{leccion.Empezo}» y termina en «{leccion.Termino}». "
            + $"{leccion.Eventos.Count} evento(s), {leccion.Frases.Count} frase(s) dichas, {leccion.Cuadros.Count} cuadro(s) grabados cada {CamaraDeCuadros.IntervaloMs} ms. "
            + "Cada evento trae el cuadro de ANTES (el anillo naranja es el ratón: ahí se hizo clic) y el de DESPUÉS (cuando la pantalla se asentó). "
            + "Puedes pedir la pantalla de cualquier segundo con ver_momento, por ejemplo para ver qué se señalaba mientras se decía algo sin hacer clic. "
            + "Cada evento trae, si se pudo, la PUERTA por su nombre: es exactamente lo que map_take espera en exit y lo que map_esto_es espera en sobre. Úsalo tal cual; el selector es solo una pista."
            + (leccion.Contexto.Length > 0 ? $"\nLo dicho sin estar sobre ningún clic: «{leccion.Contexto}»" : "")));

        foreach (var e in leccion.Eventos)
        {
            var etiqueta = new System.Text.StringBuilder();
            etiqueta.Append($"t={Hora(e.HoraMs)} · ");
            etiqueta.Append(e.Tipo == "clic" ? $"clic {e.N} en ({e.X},{e.Y})" : $"evento {e.N} por teclado");
            // LA PUERTA POR SU NOMBRE, DELANTE: es lo que map_take entiende (exit=«…»). El selector crudo
            // va detrás como pista; en la primera prueba real el piloto perdió tres minutos tanteando
            // nombres porque solo tenía el selector (2026-09-07).
            if (e.Etiqueta.Length > 0) etiqueta.Append($" · puerta «{e.Etiqueta}»");
            if (e.Selector.Length > 0) etiqueta.Append($" · selector {e.Selector}");
            if (e.Texto.Length > 0) etiqueta.Append($" · tecleó «{e.Texto}»");
            if (e.Tecla.Length > 0) etiqueta.Append($" · tecla {e.Tecla}");
            if (e.Llegada.Length > 0) etiqueta.Append($" · llegó a «{e.Llegada}»");
            foreach (var d in e.Dicho) etiqueta.Append($" · decía: «{d}»");
            if (!e.Asentado && e.CuadroDespues.Length > 0) etiqueta.Append(" · (la pantalla no llegó a asentarse antes del techo)");
            bloques.Add(new Bloque("text", etiqueta.ToString()));
            if (e.CuadroAntes.Length > 0 && entran.Contains(e.CuadroAntes))
                bloques.Add(new Bloque("image", $"antes del evento {e.N}", e.CuadroAntes));
            if (e.CuadroDespues.Length > 0 && entran.Contains(e.CuadroDespues))
                bloques.Add(new Bloque("image", $"después del evento {e.N}", e.CuadroDespues));
        }

        if (leccion.Frases.Count > 0)
        {
            var t = new System.Text.StringBuilder("TRANSCRIPCIÓN COMPLETA, con hora:");
            foreach (var f in leccion.Frases) t.Append($"\n[{Hora(f.HoraMs)}] {f.Texto}");
            bloques.Add(new Bloque("text", t.ToString()));
        }
        return bloques;
    }

    private static string PantallaDe(string ruta, Leccion leccion)
    {
        // La huella no viaja en la lección; la pantalla se identifica por la llegada del evento que la
        // produjo, y a falta de llegada, por la ruta (cada cuadro es su propia pantalla).
        var e = leccion.Eventos.FirstOrDefault(x => x.CuadroDespues.Equals(ruta, StringComparison.OrdinalIgnoreCase));
        return e != null && e.Llegada.Length > 0 ? e.Llegada : ruta;
    }

    public static string Hora(long ms)
    {
        if (ms < 0) ms = 0;
        long s = ms / 1000;
        return $"{s / 60:D2}:{s % 60:D2}";
    }
}
