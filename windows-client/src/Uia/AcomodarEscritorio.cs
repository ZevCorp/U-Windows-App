using System.IO;
using System.Text;
using System.Text.Json;
using U.WindowsClient.Actions;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Uia;

/// <summary>
/// ORDENAR EL ESCRITORIO ARRASTRANDO: agrupa los iconos por familia y los lleva, uno a uno y a la
/// vista, a columnas limpias empezando por arriba a la izquierda.
/// </summary>
/// <remarks>
/// Se hace con arrastres de verdad y no con <c>LVM_SETITEMPOSITION</c>, que colocaría los nueve
/// iconos de golpe y sin que se vea nada. Teletransportarlos sería más rápido y sería peor: lo que
/// se pidió es ver a Ü ponerse encima de cada cosa y llevarla, porque lo que convence de que el
/// asistente está en tu pantalla es verlo actuar en ella, no que el resultado aparezca.
///
/// LA REGLA QUE NO SE PUEDE ROMPER: no se suelta jamás sobre una celda ocupada. Con la cuadrícula
/// encendida esto suele ser cosmético, pero si la celda la ocupa una CARPETA, soltar encima no
/// coloca el icono al lado — mete el archivo dentro. Sería una operación de archivos disfrazada de
/// arreglo visual, irreversible de un vistazo y provocada por un «ordena mi escritorio». Por eso
/// cada ronda relee las posiciones vivas y solo mueve hacia huecos que en ESE momento están libres;
/// si no queda ninguno, aparta un icono a un lado antes que forzar.
/// </remarks>
public static class AcomodarEscritorio
{
    /// <summary>Las familias, en el orden en que ocupan columnas de izquierda a derecha.</summary>
    private enum Familia { Sistema, Carpetas, Programas, Documentos, Medios, Otros }

    private static readonly string[] ExtProgramas = { ".lnk", ".exe", ".url", ".appref-ms", ".bat", ".cmd", ".msi" };
    private static readonly string[] ExtDocumentos = { ".pdf", ".doc", ".docx", ".txt", ".md", ".xls", ".xlsx", ".ppt", ".pptx", ".csv", ".rtf", ".odt" };
    private static readonly string[] ExtMedios = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".svg", ".heic", ".mp4", ".mkv", ".avi", ".mov", ".mp3", ".wav" };

    private static string Rotulo(Familia f) => f switch
    {
        Familia.Sistema => "sistema",
        Familia.Carpetas => "carpetas",
        Familia.Programas => "programas",
        Familia.Documentos => "documentos",
        Familia.Medios => "fotos y vídeo",
        _ => "otros",
    };

    private static string ArchivoPrevio =>
        System.IO.Path.Combine(U.Graph.UserPaths.Roaming, "U", "escritorio-antes.json");

    /// <summary>Lo que dejamos NOSOTROS la última vez. Sirve para no confundir nuestra obra con «lo de antes».</summary>
    private static string ArchivoNuestro =>
        System.IO.Path.Combine(U.Graph.UserPaths.Roaming, "U", "escritorio-despues.json");

    /// <summary>
    /// UNA VUELTA A LA VEZ, y aquí no es por rendimiento. Dos ordenaciones simultáneas leen las
    /// posiciones que la otra está cambiando y se pisan los destinos; cuando esto movía los iconos
    /// con arrastres de ratón, además entrelazaban sus pulsaciones y eso llegó a borrar un archivo
    /// (ver <see cref="IconosDelEscritorio.Mover"/>). Basta con que a Ü le pidan «ordena» dos veces
    /// seguidas —lo hizo el modelo de voz solo, tres veces en cuarenta segundos, el 2026-08-16—.
    /// </summary>
    private static readonly Mapeador.VueltaUnica _turno = new("acomodar-escritorio");

    /// <summary>Recoloca el escritorio. Sin forma pedida, la SIGUIENTE del turno: nunca repite.</summary>
    public static string Acomodar(string forma = "")
    {
        if (!_turno.MeToca())
            return "ya estoy recolocando el escritorio ahora mismo; espera a que termine.";
        try { return AcomodarDeVerdad(forma); }
        finally { _turno.Termine(); }
    }

    private static string AcomodarDeVerdad(string formaPedida)
    {
        var iconos = IconosDelEscritorio.Leer();
        if (iconos.Count == 0)
            return "no pude leer los iconos del escritorio: puede que estén ocultos o que el escritorio no esté activo.";

        Escritorio.Mostrar();
        System.Threading.Thread.Sleep(450);
        IconosDelEscritorio.SoltarSeleccion();

        GuardarPrevio(iconos);

        var rejilla = IconosDelEscritorio.MedirRejilla(iconos);
        var familias = ClasificarTodos(iconos);

        // NUNCA «YA ESTÁ ORDENADO». Si la forma elegida deja todo donde ya estaba, no se contesta que
        // no había nada que hacer: se pasa a la siguiente. Quien lo pide otra vez no está informando
        // de que haya desorden, está pidiendo que pase algo.
        var pedida = Interpretar(formaPedida);
        var todas = Enum.GetValues<Forma>();
        var arranque = pedida ?? Siguiente();
        Forma elegida = arranque;
        Dictionary<int, (int X, int Y)> destinos = new();

        for (int intento = 0; intento < todas.Length; intento++)
        {
            elegida = (Forma)(((int)arranque + intento) % todas.Length);
            destinos = Repartir(iconos, familias, rejilla, elegida);
            if (destinos.Count > 0) break;
            if (pedida != null) return $"ya están puestos en {NombreDe(pedida.Value)}: no hay nada que mover.";
        }
        // LA LETRA SE COMPLETA. Una Ü a la que le faltan sitios no es una Ü más pequeña: es una forma
        // rota, porque la letra depende de que estén TODOS sus trazos. Si faltan iconos se duplican
        // accesos directos —solo accesos directos, ver DuplicarAccesos— y se recalcula con los nuevos.
        if (elegida == Forma.LetraU && PlantillaU() is { Count: > 0 } plantilla && iconos.Count < plantilla.Count)
        {
            var nuevos = DuplicarAccesos(plantilla.Count - iconos.Count);
            if (nuevos.Count > 0)
            {
                iconos = EsperarA(iconos.Count + nuevos.Count);
                familias = ClasificarTodos(iconos);
                destinos = Repartir(iconos, familias, rejilla, elegida);
            }
        }

        if (destinos.Count == 0)
            return "solo hay un icono en el escritorio, así que cualquier forma se ve igual.";

        RecordarForma(elegida);
        LogBus.Log("escritorio", $"{elegida}: {destinos.Count} icono(s) a mover");
        Freno.Empezar($"recolocar el escritorio en {NombreDe(elegida)}");

        // Se apaga «alinear a la cuadrícula» para poder dibujar de verdad las formas libres, y se
        // deja como estaba pase lo que pase: es un ajuste del usuario, no nuestro.
        bool cuadriculaAntes = IconosDelEscritorio.Cuadricula;
        List<string> movidos, fallidos;
        try
        {
            if (cuadriculaAntes) IconosDelEscritorio.Cuadricula = false;
            (movidos, fallidos) = Ejecutar(destinos, rejilla);
        }
        finally
        {
            if (cuadriculaAntes) IconosDelEscritorio.Cuadricula = true;
            Freno.Termine();
        }

        Ui.Senalador.Soltar();
        Guardar(ArchivoNuestro, IconosDelEscritorio.Leer());
        return Contar(elegida, movidos, fallidos);
    }

    private static string ArchivoForma =>
        System.IO.Path.Combine(U.Graph.UserPaths.Roaming, "U", "escritorio-forma.txt");

    /// <summary>La que toca ahora: la de después de la última que se usó.</summary>
    private static Forma Siguiente()
    {
        try
        {
            if (File.Exists(ArchivoForma) && Enum.TryParse<Forma>(File.ReadAllText(ArchivoForma).Trim(), out var ultima))
                return (Forma)(((int)ultima + 1) % Enum.GetValues<Forma>().Length);
        }
        catch (Exception e) { LogBus.Log("escritorio", $"leyendo la última forma: {e.Message}"); }
        return Forma.ColumnasPorFamilia;
    }

    private static void RecordarForma(Forma f)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(ArchivoForma);
            if (dir != null) Directory.CreateDirectory(dir);
            File.WriteAllText(ArchivoForma, f.ToString());
        }
        catch (Exception e) { LogBus.Log("escritorio", $"guardando la forma: {e.Message}"); }
    }

    /// <summary>Devuelve cada icono a donde estaba antes del último orden.</summary>
    public static string Deshacer()
    {
        if (!_turno.MeToca())
            return "estoy ordenando el escritorio ahora mismo; espera a que termine y vuelve a pedírmelo.";
        try { return DeshacerDeVerdad(); }
        finally { _turno.Termine(); }
    }

    private static string DeshacerDeVerdad()
    {
        var previo = Leer(ArchivoPrevio);
        if (previo.Count == 0) return "no tengo guardada ninguna disposición anterior del escritorio.";

        Escritorio.Mostrar();
        System.Threading.Thread.Sleep(450);
        IconosDelEscritorio.SoltarSeleccion();

        var iconos = IconosDelEscritorio.Leer();
        var destinos = new Dictionary<int, (int X, int Y)>();
        foreach (var i in iconos)
            if (previo.TryGetValue(i.Nombre, out var p) && (p.X != i.X || p.Y != i.Y))
                destinos[i.Indice] = p;

        if (destinos.Count == 0) return "el escritorio ya está como estaba antes.";

        Freno.Empezar("devolver el escritorio a como estaba");
        bool cuadriculaAntes = IconosDelEscritorio.Cuadricula;
        List<string> movidos, fallidos;
        try
        {
            if (cuadriculaAntes) IconosDelEscritorio.Cuadricula = false;
            (movidos, fallidos) = Ejecutar(destinos, IconosDelEscritorio.MedirRejilla(iconos));
        }
        finally
        {
            if (cuadriculaAntes) IconosDelEscritorio.Cuadricula = true;
            Freno.Termine();
        }
        Ui.Senalador.Soltar();

        return fallidos.Count == 0
            ? $"devolví los {movidos.Count} iconos a donde estaban."
            : $"devolví {movidos.Count} iconos; {fallidos.Count} no pude: {string.Join("; ", fallidos)}.";
    }

    private sealed record PosicionGuardada(string Nombre, int X, int Y);

    /// <summary>
    /// Apunta a dónde volver — pero SOLO si lo que hay ahora no lo pusimos nosotros.
    ///
    /// Guardar en cada llamada rompía el deshacer justo cuando más falta hace. Al pedir «ordena»
    /// dos veces, la segunda guardaba como «antes» el resultado de la primera: el punto de retorno
    /// real se perdía en silencio y «déjalo como estaba» devolvía a un sitio que el usuario no había
    /// elegido nunca. Pasó el 2026-08-16 y dejó los iconos repartidos por el borde derecho.
    /// </summary>
    private static void GuardarPrevio(IReadOnlyList<IconosDelEscritorio.Icono> iconos)
    {
        if (File.Exists(ArchivoPrevio) && EsObraNuestra(iconos))
        {
            LogBus.Log("escritorio", "lo de ahora ya lo ordené yo: conservo el punto de retorno original");
            return;
        }
        Guardar(ArchivoPrevio, iconos);
    }

    /// <summary>¿El escritorio de ahora es exactamente el que dejamos la última vez?</summary>
    private static bool EsObraNuestra(IReadOnlyList<IconosDelEscritorio.Icono> iconos)
    {
        var nuestro = Leer(ArchivoNuestro);
        if (nuestro.Count == 0 || nuestro.Count != iconos.Count) return false;
        return iconos.All(i => nuestro.TryGetValue(i.Nombre, out var p) && p.X == i.X && p.Y == i.Y);
    }

    private static Dictionary<string, (int X, int Y)> Leer(string ruta)
    {
        try
        {
            if (!File.Exists(ruta)) return new();
            var crudo = JsonSerializer.Deserialize<List<PosicionGuardada>>(File.ReadAllText(ruta)) ?? new();
            return crudo.GroupBy(p => p.Nombre).ToDictionary(g => g.Key, g => (g.First().X, g.First().Y));
        }
        catch (Exception e) { LogBus.Log("escritorio", $"leyendo {System.IO.Path.GetFileName(ruta)}: {e.Message}"); return new(); }
    }

    private static void Guardar(string ruta, IReadOnlyList<IconosDelEscritorio.Icono> iconos)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(ruta);
            if (dir != null) Directory.CreateDirectory(dir);
            var datos = iconos.Select(i => new PosicionGuardada(i.Nombre, i.X, i.Y)).ToList();
            File.WriteAllText(ruta, JsonSerializer.Serialize(datos, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) { LogBus.Log("escritorio", $"no pude guardar {System.IO.Path.GetFileName(ruta)}: {e.Message}"); }
    }

    /// <summary>
    /// A qué familia pertenece cada icono. Se cruza con los archivos REALES de las dos carpetas de
    /// Escritorio —la del usuario y la común—, y no con una lista de nombres: los iconos del sistema
    /// («Papelera de reciclaje», «Este equipo») están traducidos y cambian entre versiones, mientras
    /// que «no le corresponde ningún archivo» los distingue en cualquier idioma.
    /// </summary>
    private static Dictionary<int, Familia> ClasificarTodos(IReadOnlyList<IconosDelEscritorio.Icono> iconos)
    {
        var enDisco = new Dictionary<string, (bool EsCarpeta, string Ext)>(StringComparer.OrdinalIgnoreCase);
        foreach (var carpeta in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                                        Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory) })
        {
            try
            {
                if (string.IsNullOrWhiteSpace(carpeta) || !Directory.Exists(carpeta)) continue;
                foreach (var d in Directory.GetDirectories(carpeta))
                    enDisco[new DirectoryInfo(d).Name] = (true, "");
                foreach (var f in Directory.GetFiles(carpeta))
                {
                    var fi = new FileInfo(f);
                    // El nombre visible puede llevar o no la extensión según los ajustes de Windows;
                    // los accesos directos NUNCA la enseñan. Se apuntan las dos formas.
                    enDisco[fi.Name] = (false, fi.Extension);
                    enDisco[System.IO.Path.GetFileNameWithoutExtension(fi.Name)] = (false, fi.Extension);
                }
            }
            catch (Exception e) { LogBus.Log("escritorio", $"listando {carpeta}: {e.Message}"); }
        }

        var res = new Dictionary<int, Familia>();
        foreach (var i in iconos)
        {
            if (!enDisco.TryGetValue(i.Nombre, out var info)) { res[i.Indice] = Familia.Sistema; continue; }
            if (info.EsCarpeta) { res[i.Indice] = Familia.Carpetas; continue; }
            string ext = info.Ext.ToLowerInvariant();
            res[i.Indice] =
                ExtProgramas.Contains(ext) ? Familia.Programas :
                ExtDocumentos.Contains(ext) ? Familia.Documentos :
                ExtMedios.Contains(ext) ? Familia.Medios : Familia.Otros;
        }
        return res;
    }

    /// <summary>
    /// Las disposiciones posibles, en el orden en que se van turnando.
    /// </summary>
    /// <remarks>
    /// Hay varias porque «ordenar» tenía UNA respuesta, y entonces pedirlo dos veces contestaba «ya
    /// está ordenado»: una respuesta correcta y una experiencia muerta. Quien vuelve a pedirlo no
    /// está informando de que el escritorio esté desordenado, está pidiendo que pase algo.
    ///
    /// Las formas libres (marco, diagonal, círculo, onda) solo son posibles porque los iconos se
    /// COLOCAN por API en vez de arrastrarse: la cuadrícula de Windows solo encaja lo que se suelta,
    /// así que al soltar, un círculo se convertía en columnas. Sin arrastre no hay cuadrícula que
    /// obedecer y cada icono va al píxel que se le diga.
    /// </remarks>
    private enum Forma { LetraU, ColumnasPorFamilia, FilaArriba, Marco, Diagonal, Circulo, Onda }

    private static string NombreDe(Forma f) => f switch
    {
        Forma.LetraU => "una Ü dibujada con tus iconos",
        Forma.ColumnasPorFamilia => "columnas por familia",
        Forma.FilaArriba => "una fila arriba",
        Forma.Marco => "un marco alrededor de la pantalla",
        Forma.Diagonal => "una diagonal",
        Forma.Circulo => "un círculo",
        _ => "una onda",
    };

    /// <summary>Reconoce la forma que pide el usuario por voz. Vacío o desconocida = la siguiente del turno.</summary>
    private static Forma? Interpretar(string texto)
    {
        string t = SinTildes((texto ?? "").Trim().ToLowerInvariant());
        if (t.Length == 0) return null;
        // La Ü se pide por su nombre, no por contener una «u»: media lista la lleva («columnas»).
        // Y se compara SIN tildes porque «Ü» no siempre llega entera: viaja por HTTP, por JSON y por
        // un modelo de voz, y basta con que un tramo no hable UTF-8 para que se convierta en otra
        // cosa. Se comprobó llegando rota y cayendo en la forma siguiente (2026-08-16).
        if (t is "u" or "uu" || t.Contains("dieresis")
            || t.Contains("logo") || t.Contains("marca") || t.Contains("letra")) return Forma.LetraU;
        if (t.Contains("column") || t.Contains("familia") || t.Contains("tipo")) return Forma.ColumnasPorFamilia;
        if (t.Contains("fila") || t.Contains("arriba") || t.Contains("linea")) return Forma.FilaArriba;
        if (t.Contains("marco") || t.Contains("borde") || t.Contains("alrededor")) return Forma.Marco;
        if (t.Contains("diagonal") || t.Contains("escalera")) return Forma.Diagonal;
        if (t.Contains("circul") || t.Contains("redond") || t.Contains("corro")) return Forma.Circulo;
        if (t.Contains("onda") || t.Contains("ola") || t.Contains("curva")) return Forma.Onda;
        return null;
    }

    /// <summary>Quita tildes y diéresis para comparar: lo que llega por voz y por HTTP no siempre viene entero.</summary>
    private static string SinTildes(string s)
    {
        var d = s.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (char c in d)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                != System.Globalization.UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }

    /// <summary>Dónde acaba cada icono según la forma pedida. Nunca fuera del área visible.</summary>
    private static Dictionary<int, (int X, int Y)> Repartir(
        IReadOnlyList<IconosDelEscritorio.Icono> iconos,
        Dictionary<int, Familia> familias,
        IconosDelEscritorio.Rejilla rejilla,
        Forma forma)
    {
        var sitios = forma switch
        {
            Forma.LetraU => EnLetraU(iconos, rejilla),
            Forma.ColumnasPorFamilia => PorFamilia(iconos, familias, rejilla),
            Forma.FilaArriba => EnFila(iconos, rejilla),
            Forma.Marco => EnMarco(iconos, rejilla),
            Forma.Diagonal => EnDiagonal(iconos, rejilla),
            Forma.Circulo => EnCirculo(iconos, rejilla),
            _ => EnOnda(iconos, rejilla),
        };

        // Solo se anota lo que de verdad cambia de sitio.
        return sitios.Where(kv => iconos.First(i => i.Indice == kv.Key) is var ic
                                  && (ic.X != kv.Value.X || ic.Y != kv.Value.Y))
                     .ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    /// <summary>Los iconos en el orden en que se recorren: por familia y alfabético dentro.</summary>
    private static List<IconosDelEscritorio.Icono> EnOrden(
        IReadOnlyList<IconosDelEscritorio.Icono> iconos, Dictionary<int, Familia>? familias = null)
        => iconos.OrderBy(i => familias != null ? (int)familias[i.Indice] : 0)
                 .ThenBy(i => i.Nombre, StringComparer.CurrentCultureIgnoreCase)
                 .ToList();

    /// <summary>Que ningún icono acabe donde no se le pueda ver ni alcanzar.</summary>
    private static (int X, int Y) DentroDe(IconosDelEscritorio.Rejilla r, double x, double y)
        => ((int)Math.Round(Math.Clamp(x, r.OrigenX, r.XDeColumna(r.Columnas - 1))),
            (int)Math.Round(Math.Clamp(y, r.OrigenY, r.YDeFila(r.Filas - 1))));

    private static Dictionary<int, (int X, int Y)> PorFamilia(
        IReadOnlyList<IconosDelEscritorio.Icono> iconos,
        Dictionary<int, Familia> familias,
        IconosDelEscritorio.Rejilla rejilla)
    {
        var grupos = Enum.GetValues<Familia>()
            .Select(f => iconos.Where(i => familias[i.Indice] == f)
                               .OrderBy(i => i.Nombre, StringComparer.CurrentCultureIgnoreCase).ToList())
            .Where(g => g.Count > 0).ToList();

        int necesarias = grupos.Sum(g => (g.Count + rejilla.Filas - 1) / rejilla.Filas);
        bool separadas = necesarias <= rejilla.Columnas;
        if (!separadas) LogBus.Log("escritorio", "no caben las familias en columnas propias; se ordena seguido");

        var res = new Dictionary<int, (int X, int Y)>();
        int col = 0, fila = 0;
        foreach (var grupo in grupos)
        {
            foreach (var icono in grupo)
            {
                if (fila >= rejilla.Filas) { fila = 0; col++; }
                if (col >= rejilla.Columnas) break;
                res[icono.Indice] = (rejilla.XDeColumna(col), rejilla.YDeFila(fila));
                fila++;
            }
            if (separadas && fila > 0) { fila = 0; col++; }
        }
        return res;
    }

    private static Dictionary<int, (int X, int Y)> EnFila(
        IReadOnlyList<IconosDelEscritorio.Icono> iconos, IconosDelEscritorio.Rejilla rejilla)
    {
        var res = new Dictionary<int, (int X, int Y)>();
        int i = 0;
        foreach (var icono in EnOrden(iconos))
        {
            int col = i % rejilla.Columnas, fila = i / rejilla.Columnas; // si no caben, siguen abajo
            res[icono.Indice] = DentroDe(rejilla, rejilla.XDeColumna(col), rejilla.YDeFila(fila));
            i++;
        }
        return res;
    }

    private static Dictionary<int, (int X, int Y)> EnMarco(
        IReadOnlyList<IconosDelEscritorio.Icono> iconos, IconosDelEscritorio.Rejilla rejilla)
    {
        // El perímetro, en sentido horario desde arriba a la izquierda. Si sobran iconos para el
        // borde, los últimos se reparten por dentro en vez de amontonarse en una esquina.
        var borde = new List<(int X, int Y)>();
        int cs = rejilla.Columnas, fs = rejilla.Filas;
        for (int c = 0; c < cs; c++) borde.Add((rejilla.XDeColumna(c), rejilla.YDeFila(0)));
        for (int f = 1; f < fs; f++) borde.Add((rejilla.XDeColumna(cs - 1), rejilla.YDeFila(f)));
        for (int c = cs - 2; c >= 0 && fs > 1; c--) borde.Add((rejilla.XDeColumna(c), rejilla.YDeFila(fs - 1)));
        for (int f = fs - 2; f >= 1 && cs > 1; f--) borde.Add((rejilla.XDeColumna(0), rejilla.YDeFila(f)));

        var orden = EnOrden(iconos);
        // Repartidos por todo el perímetro, no apelotonados en el primer tramo.
        var res = new Dictionary<int, (int X, int Y)>();
        for (int i = 0; i < orden.Count; i++)
            res[orden[i].Indice] = borde[(int)((long)i * borde.Count / Math.Max(1, orden.Count)) % borde.Count];
        return res;
    }

    private static Dictionary<int, (int X, int Y)> EnDiagonal(
        IReadOnlyList<IconosDelEscritorio.Icono> iconos, IconosDelEscritorio.Rejilla rejilla)
    {
        var res = new Dictionary<int, (int X, int Y)>();
        var orden = EnOrden(iconos);
        for (int i = 0; i < orden.Count; i++)
        {
            int col = i % rejilla.Columnas;
            int fila = i % rejilla.Filas;
            res[orden[i].Indice] = DentroDe(rejilla, rejilla.XDeColumna(col), rejilla.YDeFila(fila));
        }
        return res;
    }

    private static Dictionary<int, (int X, int Y)> EnCirculo(
        IReadOnlyList<IconosDelEscritorio.Icono> iconos, IconosDelEscritorio.Rejilla rejilla)
    {
        var orden = EnOrden(iconos);
        int n = Math.Max(1, orden.Count);

        // El centro se calcula sobre lo que se VE. Lo que se coloca es la esquina superior izquierda,
        // así que un círculo de esquinas centrado en la pantalla se dibuja media celda desplazado
        // abajo y a la derecha: lo que tiene que quedar centrado es el icono, no su esquina.
        double cx = (rejilla.OrigenX + rejilla.XDeColumna(rejilla.Columnas - 1)) / 2.0 - rejilla.Ancho / 2.0;
        double cy = (rejilla.OrigenY + rejilla.YDeFila(rejilla.Filas - 1)) / 2.0 - rejilla.Alto / 2.0;

        // El radio sale de que no se toquen. Se mide con el lado MAYOR de la celda —en un círculo los
        // iconos se acercan en diagonal— y con un margen: las etiquetas son más anchas que el dibujo,
        // así que dos iconos «justos» se leen como pegados.
        double minimo = 1.2 * n * Math.Max(rejilla.Ancho, rejilla.Alto) / (2 * Math.PI);
        double cabe = Math.Min(cx - rejilla.OrigenX, cy - rejilla.OrigenY);
        double radio = Math.Min(Math.Max(minimo, rejilla.Alto), Math.Max(1, cabe));

        var res = new Dictionary<int, (int X, int Y)>();
        for (int i = 0; i < orden.Count; i++)
        {
            double a = -Math.PI / 2 + 2 * Math.PI * i / n;   // empieza arriba, como un reloj
            res[orden[i].Indice] = DentroDe(rejilla, cx + radio * Math.Cos(a), cy + radio * Math.Sin(a));
        }
        return res;
    }

    private static string ArchivoPlantillaU =>
        System.IO.Path.Combine(U.Graph.UserPaths.Roaming, "U", "escritorio-plantilla-u.json");

    /// <summary>
    /// LA Ü QUE ENSEÑÓ EL USUARIO. Se guarda dónde puso él cada icono y la letra se reproduce ahí.
    /// </summary>
    /// <remarks>
    /// Una Ü calculada sale correcta y sosa: los palos rectos, la curva a la altura que diga la
    /// fórmula. La que dibuja una persona colocando iconos tiene el grosor y las proporciones que le
    /// parecen bien a ella, y para una letra —que es una forma que se RECONOCE, no que se mide— eso
    /// gana. Así que se aprende en vez de deducirse: «así, exactamente como está ahora» (2026-08-16).
    ///
    /// Si no hay plantilla, queda el trazado calculado como reserva: nadie se queda sin Ü por no
    /// haberla enseñado todavía.
    /// </remarks>
    public static string AprenderLaU()
    {
        var iconos = IconosDelEscritorio.Leer();
        if (iconos.Count < 4) return "hay muy pocos iconos en el escritorio para aprender una forma.";

        var puntos = iconos.Select(i => new PosicionGuardada("", i.X, i.Y))
                           .OrderBy(p => p.Y).ThenBy(p => p.X).ToList();
        try
        {
            var dir = System.IO.Path.GetDirectoryName(ArchivoPlantillaU);
            if (dir != null) Directory.CreateDirectory(dir);
            File.WriteAllText(ArchivoPlantillaU, JsonSerializer.Serialize(puntos, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) { return $"no pude guardar la forma: {e.Message}"; }

        var rejilla = IconosDelEscritorio.MedirRejilla(iconos);
        int tapados = puntos.Count(p => p.Y > rejilla.YDeFila(rejilla.Filas - 1));
        string aviso = tapados > 0
            ? $" Ojo: {tapados} de esos sitios quedan por debajo de la zona visible ({rejilla.YDeFila(rejilla.Filas - 1)} es la última fila que se ve), así que ahí los iconos no se verán."
            : "";
        return $"me quedo con esta forma: {puntos.Count} sitios.{aviso}";
    }

    private static List<(int X, int Y)> PlantillaU()
    {
        try
        {
            if (!File.Exists(ArchivoPlantillaU)) return new();
            var crudo = JsonSerializer.Deserialize<List<PosicionGuardada>>(File.ReadAllText(ArchivoPlantillaU)) ?? new();
            return crudo.Select(p => (p.X, p.Y)).OrderBy(p => p.Y).ThenBy(p => p.X).ToList();
        }
        catch (Exception e) { LogBus.Log("escritorio", $"leyendo la plantilla de la Ü: {e.Message}"); return new(); }
    }

    /// <summary>Cuánto se queda quieta la Ü para que dé tiempo a verla, antes de deshacerla.</summary>
    private const int PausaAlAdmirar = 2200;

    /// <summary>Lo que se sostiene una figura intermedia: lo justo para leerla y seguir.</summary>
    private const int PausaEntreFiguras = 1100;

    /// <summary>
    /// LA BIENVENIDA: dibuja la Ü con los iconos de quien acaba de instalar Ü, y lo deja todo
    /// exactamente como estaba.
    /// </summary>
    /// <remarks>
    /// NO PREGUNTA Y NO DEJA RASTRO, y las dos cosas van juntas. Recolocarle el escritorio a alguien
    /// en su primer minuto sería alarmante si fuera un CAMBIO; como es una demostración que se
    /// deshace sola, no hay nada que consentir: mira, entiende que este asistente toca su ordenador
    /// de verdad, y su escritorio sigue como lo dejó. Por eso tampoco hay botón — un «¿te enseño una
    /// cosa?» obliga a decidir sobre algo que aún no se ha visto.
    ///
    /// Se guarda su PROPIO punto de retorno, en memoria, y no toca el archivo del deshacer normal:
    /// si mañana esa persona pide «ordena mi escritorio» y luego «déjalo como estaba», tiene que
    /// volver a donde estaba entonces, no a donde estaba el día que instaló.
    ///
    /// Y los accesos directos que se dupliquen para completar la letra se retiran al terminar. Crear
    /// archivos en el escritorio de alguien para un dibujo se justifica solo si el dibujo se borra y
    /// ellos también; si no, la bienvenida deja deberes.
    /// </remarks>
    public static string Demostrar(params string[] formas)
    {
        if (!_turno.MeToca()) return "";
        var creados = new List<string>();
        try
        {
            var antes = IconosDelEscritorio.Leer();
            if (antes.Count < 4) { LogBus.Log("escritorio", "escritorio casi vacío: me salto la bienvenida"); return ""; }
            var comoEstaba = antes.GroupBy(i => i.Nombre).ToDictionary(g => g.Key, g => (g.First().X, g.First().Y));

            Escritorio.Mostrar();
            System.Threading.Thread.Sleep(700);
            IconosDelEscritorio.SoltarSeleccion();

            var rejilla = IconosDelEscritorio.MedirRejilla(antes);
            var iconos = antes;
            var plantilla = PlantillaU();
            if (plantilla.Count > iconos.Count
                && (formas.Length == 0 || formas.Any(f => Interpretar(f) == Forma.LetraU)))
            {
                creados = DuplicarAccesos(plantilla.Count - iconos.Count);
                if (creados.Count > 0) iconos = EsperarA(antes.Count + creados.Count);
            }

            // Las figuras que se pidan, y la Ü si no se pide nada. Cada una parte de donde quedó la
            // anterior, así que se ve la forma DESHACERSE para convertirse en la siguiente — que es
            // más bonito que volver al orden entre medias y vuelve a demostrar lo mismo.
            var pedidas = (formas.Length > 0 ? formas : new[] { "u" })
                .Select(Interpretar).Where(f => f != null).Select(f => f!.Value).ToList();
            if (pedidas.Count == 0) pedidas.Add(Forma.LetraU);

            bool cuadricula = IconosDelEscritorio.Cuadricula;
            try
            {
                if (cuadricula) IconosDelEscritorio.Cuadricula = false;
                Freno.Empezar("enseñarte lo que puedo hacer");
                try
                {
                    foreach (var forma in pedidas)
                    {
                        if (Freno.Pidieron) break;
                        var vivos = IconosDelEscritorio.Leer();
                        var destinos = Repartir(vivos, ClasificarTodos(vivos), rejilla, forma);
                        if (destinos.Count == 0) continue;
                        AlRitmoDeLaBienvenida(() => Ejecutar(destinos, rejilla));
                        Freno.Duerme(forma == pedidas[^1] ? PausaAlAdmirar : PausaEntreFiguras);
                    }
                }
                finally { Freno.Termine(); }

                Ui.Senalador.Soltar();
                Devolver(comoEstaba, rejilla);
            }
            finally { if (cuadricula) IconosDelEscritorio.Cuadricula = true; }

            BorrarDuplicados(creados);
            return $"le enseñé la Ü con sus {antes.Count} iconos y se lo devolví como estaba.";
        }
        catch (Exception e)
        {
            LogBus.Log("escritorio", $"la bienvenida falló: {e.Message}");
            try { Devolver(null, null); BorrarDuplicados(creados); } catch { }
            return "";
        }
        finally { _turno.Termine(); }
    }

    /// <summary>
    /// Corre algo con el paso corto de la bienvenida y devuelve el ritmo normal pase lo que pase.
    /// Con 14 iconos son unos once segundos en vez de veinte: se sigue viendo perfectamente quién
    /// mueve qué, y no se convierte en una espera.
    /// </summary>
    private static void AlRitmoDeLaBienvenida(Action trabajo)
    {
        int fot = IconosDelEscritorio.Fotogramas, ms = IconosDelEscritorio.MsPorFotograma;
        int mirar = PausaAlMirar, entre = PausaEntreIconos;
        try
        {
            IconosDelEscritorio.Fotogramas = 15;
            IconosDelEscritorio.MsPorFotograma = 20;
            PausaAlMirar = 240;
            PausaEntreIconos = 90;
            trabajo();
        }
        finally
        {
            IconosDelEscritorio.Fotogramas = fot;
            IconosDelEscritorio.MsPorFotograma = ms;
            PausaAlMirar = mirar;
            PausaEntreIconos = entre;
        }
    }

    /// <summary>Copia de seguridad viva, para poder devolver aunque la bienvenida reviente a medias.</summary>
    private static Dictionary<string, (int X, int Y)>? _antesDeLaBienvenida;

    /// <summary>
    /// Devuelve cada icono a donde estaba, DEPRISA y sin recorrido.
    ///
    /// Sin ceremonia a propósito: enseñar merece pausas, recoger no. Y desarma el freno antes de
    /// empezar porque si la persona pulsó Escape a mitad del dibujo, lo que ha pedido es su
    /// escritorio de vuelta — dejárselo a medias por respetar el alto sería cumplir la letra de la
    /// orden e incumplir lo que quería.
    /// </summary>
    private static void Devolver(Dictionary<string, (int X, int Y)>? comoEstaba, IconosDelEscritorio.Rejilla? rejilla)
    {
        comoEstaba ??= _antesDeLaBienvenida;
        if (comoEstaba == null) return;
        _antesDeLaBienvenida = comoEstaba;

        var r = rejilla ?? IconosDelEscritorio.MedirRejilla(IconosDelEscritorio.Leer());
        var vuelta = IconosDelEscritorio.Leer()
            .Where(i => comoEstaba.ContainsKey(i.Nombre)
                     && (comoEstaba[i.Nombre].X != i.X || comoEstaba[i.Nombre].Y != i.Y))
            .ToDictionary(i => i.Indice, i => comoEstaba[i.Nombre]);
        if (vuelta.Count == 0) { _antesDeLaBienvenida = null; return; }

        int fot = IconosDelEscritorio.Fotogramas, ms = IconosDelEscritorio.MsPorFotograma;
        Freno.Empezar("devolverte el escritorio");
        try
        {
            IconosDelEscritorio.Fotogramas = 6;
            IconosDelEscritorio.MsPorFotograma = 8;
            Ejecutar(vuelta, r, conCarita: false);
        }
        finally
        {
            IconosDelEscritorio.Fotogramas = fot;
            IconosDelEscritorio.MsPorFotograma = ms;
            Freno.Termine();
            _antesDeLaBienvenida = null;
        }
    }

    /// <summary>
    /// Retira los accesos directos que creó la bienvenida. A la PAPELERA, no al vacío: son archivos
    /// del escritorio de otra persona, y aunque los pusiéramos nosotros hace veinte segundos, un
    /// borrado sin vuelta atrás no es la forma de recoger lo propio. Solo se tocan las rutas exactas
    /// que se anotaron al crearlas — nunca se busca «lo que parezca una copia».
    /// </summary>
    private static void BorrarDuplicados(List<string> creados)
    {
        var vivos = creados.Where(File.Exists).ToList();
        if (vivos.Count == 0) return;
        int fuera = 0;
        foreach (var ruta in vivos)
        {
            try
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                    ruta,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                fuera++;
            }
            catch (Exception e) { LogBus.Log("escritorio", $"no pude retirar {System.IO.Path.GetFileName(ruta)}: {e.Message}"); }
        }
        LogBus.Log("escritorio", $"retiré {fuera} de los {vivos.Count} acceso(s) que había duplicado");
        SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW, Environment.GetFolderPath(Environment.SpecialFolder.Desktop), IntPtr.Zero);
    }

    /// <summary>
    /// COMPLETA LA LETRA CON ACCESOS DIRECTOS DE MÁS cuando no hay iconos para todos los sitios.
    /// </summary>
    /// <remarks>
    /// SOLO SE DUPLICAN ACCESOS DIRECTOS (.lnk), y esa restricción no es comodidad: un acceso directo
    /// es un puntero, copiarlo no duplica nada de nadie y borrarlo después no pierde nada. Copiar una
    /// CARPETA o un documento para rellenar un dibujo sería crear datos reales del usuario por un
    /// motivo decorativo, y luego alguien tendría que adivinar cuál era el bueno.
    ///
    /// Van a la carpeta de Escritorio del usuario y no a la común (C:\Users\Public\Desktop), que pide
    /// permisos de administrador — comprobado fallando el 2026-08-16.
    /// </remarks>
    private static List<string> DuplicarAccesos(int cuantos)
    {
        var creados = new List<string>();
        cuantos = Math.Min(cuantos, 40);   // tope: rellenar un dibujo no justifica llenar el disco
        string escritorio = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        if (cuantos <= 0 || !Directory.Exists(escritorio)) return creados;

        // El más «limpio» de los que haya: el de nombre más corto suele ser el original y no una copia.
        var fuente = Directory.GetFiles(escritorio, "*.lnk")
                              .OrderBy(f => System.IO.Path.GetFileName(f).Length).FirstOrDefault();
        if (fuente == null) { LogBus.Log("escritorio", "no hay ningún acceso directo que duplicar"); return creados; }

        string raiz = System.IO.Path.GetFileNameWithoutExtension(fuente);
        int corte = raiz.IndexOf(" - copia", StringComparison.OrdinalIgnoreCase);
        if (corte > 0) raiz = raiz[..corte];

        for (int n = 1; creados.Count < cuantos && n < 300; n++)
        {
            string destino = System.IO.Path.Combine(escritorio, $"{raiz} - copia ({n}).lnk");
            if (File.Exists(destino)) continue;
            try { File.Copy(fuente, destino); creados.Add(destino); }
            catch (Exception e) { LogBus.Log("escritorio", $"no pude duplicar: {e.Message}"); break; }
        }
        if (creados.Count > 0)
        {
            LogBus.Log("escritorio", $"dupliqué {creados.Count} acceso(s) directo(s) de «{raiz}» para completar la Ü");
            // Y SE LE AVISA AL ESCRITORIO. Crear los archivos no basta: el escritorio no vigila esa
            // carpeta al segundo, así que los iconos nuevos no existían para él —se crearon cuatro y
            // seguía declarando catorce— y la letra se dibujaba incompleta con archivos que sí
            // estaban en disco (2026-08-16). Esto es el equivalente a pulsar F5 sobre el escritorio.
            SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW, escritorio, IntPtr.Zero);
        }
        return creados;
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern void SHChangeNotify(int evento, uint banderas, string ruta, IntPtr b);
    private const int SHCNE_UPDATEDIR = 0x00001000;
    private const uint SHCNF_PATHW = 0x0005;

    /// <summary>Espera a que el escritorio se entere de los archivos nuevos. No es instantáneo.</summary>
    private static IReadOnlyList<IconosDelEscritorio.Icono> EsperarA(int cuantos)
    {
        var iconos = IconosDelEscritorio.Leer();
        for (int i = 0; i < 25 && iconos.Count < cuantos; i++)
        {
            System.Threading.Thread.Sleep(160);
            iconos = IconosDelEscritorio.Leer();
        }
        return iconos;
    }

    /// <summary>
    /// LA Ü, dibujada con los propios iconos del usuario: dos palos, la curva de abajo y los dos
    /// puntos de la diéresis encima.
    /// </summary>
    /// <remarks>
    /// La letra se traza en coordenadas de 0 a 1 y luego se estira sobre la pantalla, para que salga
    /// igual con ocho iconos que con cuarenta y en cualquier resolución. El ancho se ata a la ALTURA
    /// disponible y no al ancho: sobre una pantalla panorámica, una Ü que ocupara todo lo ancho se
    /// leería como una bañera. Los puntos se reservan primero porque son los que hacen que se
    /// reconozca la letra — una U sin diéresis es otra letra.
    /// </remarks>
    private static Dictionary<int, (int X, int Y)> EnLetraU(
        IReadOnlyList<IconosDelEscritorio.Icono> iconos, IconosDelEscritorio.Rejilla rejilla)
    {
        // Si la enseñó el usuario, manda la suya.
        var plantilla = PlantillaU();
        if (plantilla.Count > 0)
        {
            var puestos = new Dictionary<int, (int X, int Y)>();
            var lista = EnOrden(iconos);
            for (int k = 0; k < lista.Count; k++)
                // Si sobran iconos se APILAN en sitios que ya forman parte de la letra, dando la
                // vuelta a la plantilla. Dejarlos donde estaban sería dejar manchas alrededor de la
                // Ü; encima de un trazo no se notan y la forma queda entera, que es lo que importa.
                puestos[lista[k].Indice] = plantilla[k % plantilla.Count];
            return puestos;
        }

        var orden = EnOrden(iconos);
        var res = new Dictionary<int, (int X, int Y)>();
        if (orden.Count == 0) return res;

        double alto = rejilla.YDeFila(rejilla.Filas - 1) - rejilla.OrigenY;
        double ancho = Math.Min(alto * 0.9, rejilla.XDeColumna(rejilla.Columnas - 1) - rejilla.OrigenX);
        double izq = (rejilla.OrigenX + rejilla.XDeColumna(rejilla.Columnas - 1)) / 2.0 - ancho / 2.0;

        (int X, int Y) Punto(double nx, double ny) =>
            DentroDe(rejilla, izq + nx * ancho, rejilla.OrigenY + ny * alto);

        // Con muy pocos iconos no hay letra que dibujar: se hace la U sola, que aún se reconoce.
        bool conPuntos = orden.Count >= 5;
        int i = 0;
        if (conPuntos)
        {
            res[orden[i++].Indice] = Punto(0.17, 0.02);   // punto izquierdo
            res[orden[i++].Indice] = Punto(0.83, 0.02);   // punto derecho
        }

        int enElTrazo = orden.Count - i;
        double arranque = conPuntos ? 0.30 : 0.10;        // el cuerpo empieza bajo la diéresis
        for (int k = 0; k < enElTrazo; k++)
        {
            double t = enElTrazo == 1 ? 0.5 : (double)k / (enElTrazo - 1);
            double nx, ny;
            if (t < 0.32)                                  // palo izquierdo, bajando
            {
                nx = 0.17;
                ny = arranque + (0.70 - arranque) * (t / 0.32);
            }
            else if (t <= 0.68)                            // la curva de abajo, de izquierda a derecha
            {
                double a = Math.PI * (t - 0.32) / 0.36;    // media vuelta
                nx = 0.50 - 0.33 * Math.Cos(a);
                ny = 0.70 + 0.26 * Math.Sin(a);
            }
            else                                           // palo derecho, subiendo
            {
                nx = 0.83;
                ny = 0.70 - (0.70 - arranque) * ((t - 0.68) / 0.32);
            }
            res[orden[i + k].Indice] = Punto(nx, ny);
        }
        return res;
    }

    private static Dictionary<int, (int X, int Y)> EnOnda(
        IReadOnlyList<IconosDelEscritorio.Icono> iconos, IconosDelEscritorio.Rejilla rejilla)
    {
        var orden = EnOrden(iconos);
        double medio = (rejilla.OrigenY + rejilla.YDeFila(rejilla.Filas - 1)) / 2.0;
        double amplitud = Math.Max(rejilla.Alto, (rejilla.YDeFila(rejilla.Filas - 1) - rejilla.OrigenY) / 3.0);

        var res = new Dictionary<int, (int X, int Y)>();
        for (int i = 0; i < orden.Count; i++)
        {
            int col = i % rejilla.Columnas;
            double x = rejilla.XDeColumna(col);
            double y = medio + amplitud * Math.Sin(2 * Math.PI * col / Math.Max(4, Math.Min(8, rejilla.Columnas)));
            res[orden[i].Indice] = DentroDe(rejilla, x, y);
        }
        return res;
    }

    /// <summary>
    /// El recorrido: uno por uno, en el orden en que van quedando por pantalla, para que la forma se
    /// vea aparecer en vez de saltar entera.
    /// </summary>
    /// <remarks>
    /// AQUÍ HUBO UN PLANIFICADOR que solo movía hacia celdas libres y, si ninguna lo estaba, apartaba
    /// un icono para deshacer el cruce. Existía porque antes esto ARRASTRABA: soltar sobre una celda
    /// ocupada por una carpeta mete el archivo dentro, y sobre la Papelera lo borra. Con el arrastre
    /// fuera, ninguna de esas dos cosas puede ocurrir —colocar por API sobre otro icono solo los deja
    /// superpuestos, que es un asunto estético— y la regla pasó de proteger a estorbar: las formas
    /// libres (círculo, onda) tienen sitios que se solapan A PROPÓSITO, así que ningún destino era
    /// «libre», nadie podía moverse, y el planificador apartaba iconos sin parar. El usuario lo vio
    /// como el cursor rebotando entre dos casillas para siempre (2026-08-16).
    ///
    /// La lección: una protección heredada de un mecanismo que ya no existe no es gratis. Se quita
    /// con el mecanismo, o se queda bloqueando lo que vino después.
    /// </remarks>
    private static (List<string> Movidos, List<string> Fallidos) Ejecutar(
        Dictionary<int, (int X, int Y)> destinos,
        IconosDelEscritorio.Rejilla rejilla,
        bool conCarita = true)
    {
        var movidos = new List<string>();
        var fallidos = new List<string>();
        var vivos = IconosDelEscritorio.Leer().ToDictionary(i => i.Indice);

        foreach (var (idx, destino) in destinos.OrderBy(kv => kv.Value.Y).ThenBy(kv => kv.Value.X))
        {
            if (Freno.Pidieron) { fallidos.Add("paraste tú"); break; }
            if (!vivos.TryGetValue(idx, out var icono)) continue;

            // LA CARITA VA DELANTE. Primero se planta al lado del icono y lo enciende —«voy a por
            // este»—, y solo entonces el icono se pone en marcha, con la carita viajando ya hacia su
            // destino. Sin esa pausa el recorrido no se lee: los iconos cambiarían de sitio sin que
            // se vea quién los mueve, que es justo lo que se pidió poder ver.
            if (conCarita)
            {
                Mirar(icono.X, icono.Y, rejilla, icono.Nombre);
                if (Freno.Duerme(PausaAlMirar)) break;
                Mirar(destino.X, destino.Y, rejilla, icono.Nombre);
            }

            var t = IconosDelEscritorio.Mover(icono, destino.X, destino.Y);
            if (t.Llego) movidos.Add(t.Nombre);
            else fallidos.Add($"«{t.Nombre}» {t.Porque}");
            if (conCarita && Freno.Duerme(PausaEntreIconos)) break;
        }

        return (movidos, fallidos);
    }

    /// <summary>
    /// Cuánto se queda mirando cada icono antes de moverlo, y cuánto respira entre uno y otro.
    /// Se pueden bajar: la bienvenida va más ligera que un «ordena mi escritorio» pedido a mano,
    /// porque nadie ha pedido ver esto y una demostración que se hace larga deja de demostrar.
    /// </summary>
    private static int PausaAlMirar = 420, PausaEntreIconos = 220;

    /// <summary>Lleva la carita junto a una posición de la lista y enciende el recuadro ahí.</summary>
    private static void Mirar(int x, int y, IconosDelEscritorio.Rejilla rejilla, string nombre)
    {
        var caja = IconosDelEscritorio.CajaEnPantalla(x, y, rejilla.Ancho, rejilla.Alto);
        if (caja is { } c) Ui.Senalador.Senalar(c, nombre);
    }


    private static string Contar(Forma forma, List<string> movidos, List<string> fallidos)
    {
        var sb = new StringBuilder();
        sb.Append(movidos.Count == 0 ? "no moví ninguno" : $"moví {movidos.Count} icono(s)");
        sb.Append($" y los dejé en {NombreDe(forma)}.");
        if (fallidos.Count > 0) sb.Append($" No pude con {fallidos.Count}: {string.Join("; ", fallidos)}.");
        return sb.ToString();
    }
}
