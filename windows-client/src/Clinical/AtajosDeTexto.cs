using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using U.WindowsClient.Cuenta;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Clinical;

/// <summary>Un atajo del médico: un texto guardado que se inserta escribiendo «/».</summary>
/// <param name="Categoria">La sección donde suele ir («Plan», «Examen físico»); vacía si ninguna.</param>
/// <param name="Actualizado">Fecha ISO de la última edición: desempata el orden, como en la web.</param>
public sealed record Atajo(string Id, string Titulo, string Contenido, string Categoria, string Actualizado);

/// <summary>El «/» activo bajo el cursor: lo escrito tras la barra y dónde empieza la barra.</summary>
public sealed record Barra(string Consulta, int Inicio);

/// <summary>Un hueco por rellenar dentro de un atajo: <c>___</c> o <c>[algo]</c>.</summary>
public sealed record Hueco(int Inicio, int Fin);

/// <summary>El campo después de insertar, con lo insertado seleccionado.</summary>
public sealed record Insercion(string Texto, int SelInicio, int SelFin);

/// <summary>
/// LOS ATAJOS «/» DE LA NOTA, como en la web. Promesas 455 y 456 (spec 055).
/// </summary>
/// <remarks>
/// PUERTO LÍNEA A LÍNEA de `slash-trigger.ts`, `placeholders.ts`, `insert-text.ts`, `search.ts` y
/// `filterSnippets` de `snippets.ts`. Lo que el médico aprendió en la web —escribir «/» al empezar
/// palabra, Tab para saltar de hueco en hueco— funciona igual aquí.
///
/// EL TEXTO QUE ENTRA ES DEL MÉDICO. Lo pidió el dueño (2026-09-26): «que también se pueda modificar
/// el texto dentro de la nota que tenga los atajos». Un atajo no deja nada bloqueado: se inserta como
/// texto normal de la sección, se edita, se borra, se ajusta por voz y se guarda con la nota.
/// </remarks>
public static class AtajosDeTexto
{
    /// <summary>Más allá de esto no es una búsqueda: es una ruta, una dosis o texto que empezaba con barra.</summary>
    private const int ConsultaMaxima = 40;

    private static readonly Regex PatronDeHueco = new(@"_{3,}|\[[^\[\]\n]{1,40}\]");
    private static readonly Regex NoPalabra = new("[^a-z0-9]+");
    private static readonly CompareInfo Espanol = CultureInfo.GetCultureInfo("es").CompareInfo;
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es");

    /// <summary>`\s` de JavaScript para un carácter suelto (incluye el BOM, que IsWhiteSpace no).</summary>
    private static bool EsEspacio(char c) => char.IsWhiteSpace(c) || c == '﻿';

    /// <summary>
    /// El «/» activo bajo el cursor, o nulo. Solo cuenta si empieza palabra: «120/80», «s/p» o
    /// «mg/dl» no abren nada por accidente.
    /// </summary>
    public static Barra? BarraEn(string valor, int caret)
    {
        if (caret < 0 || caret > valor.Length) return null;
        for (int i = caret; i > 0; i--)
        {
            char previo = valor[i - 1];
            if (previo == '/')
            {
                if (i >= 2 && !EsEspacio(valor[i - 2])) return null;
                string consulta = valor[i..caret];
                if (consulta.Length > ConsultaMaxima) return null;
                return new Barra(consulta, i - 1);
            }
            if (EsEspacio(previo)) return null;
        }
        return null;
    }

    /// <summary>Los huecos del texto, en orden.</summary>
    public static IReadOnlyList<Hueco> Huecos(string texto) =>
        PatronDeHueco.Matches(texto).Select(m => new Hueco(m.Index, m.Index + m.Length)).ToList();

    /// <summary>El primer hueco que empieza en <paramref name="desde"/> o después (Tab).</summary>
    public static Hueco? SiguienteHueco(string texto, int desde) =>
        Huecos(texto).FirstOrDefault(h => h.Inicio >= desde);

    /// <summary>El primer hueco dentro de [inicio, fin): el de lo que se acaba de insertar.</summary>
    public static Hueco? PrimerHuecoEn(string texto, int inicio, int fin) =>
        Huecos(texto).FirstOrDefault(h => h.Inicio >= inicio && h.Fin <= fin);

    /// <summary>
    /// Inserta reemplazando [desde, hasta). Si justo antes hay algo que no es espacio, antepone un
    /// salto de línea: un bloque pegado a media frase no es lo que nadie quiere. Insertar SUMA.
    /// </summary>
    public static Insercion Insertar(string valor, int desde, int hasta, string texto)
    {
        int de = Math.Max(0, Math.Min(desde, valor.Length));
        int a = Math.Max(de, Math.Min(hasta, valor.Length));
        string antes = valor[..de];
        string despues = valor[a..];
        string separador = antes.Length > 0 && !EsEspacio(antes[^1]) ? "\n" : "";
        string insertado = separador + texto;
        return new Insercion(antes + insertado + despues, de + separador.Length, de + insertado.Length);
    }

    // ── buscar ───────────────────────────────────────────────────────────────

    /// <summary>Minúsculas y sin tildes: «Pediatría» y «pediatria» son la misma búsqueda.</summary>
    public static string Normalizar(string valor) =>
        ConceptosClinicos.SinTildes((valor ?? "").ToLower(Es)).Trim();

    private static List<string> Palabras(string normalizado) =>
        NoPalabra.Split(normalizado).Where(p => p.Length > 2).ToList();

    /// <summary>Distancia de edición con transposiciones (Damerau-Levenshtein de alineación óptima).</summary>
    private static int Distancia(string a, string b)
    {
        if (a == b) return 0;
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;
        var dosAtras = Array.Empty<int>();
        var anterior = Enumerable.Range(0, b.Length + 1).ToArray();
        var actual = Array.Empty<int>();
        for (int i = 1; i <= a.Length; i++)
        {
            actual = new int[b.Length + 1];
            actual[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int coste = a[i - 1] == b[j - 1] ? 0 : 1;
                int valor = Math.Min(Math.Min(anterior[j] + 1, actual[j - 1] + 1), anterior[j - 1] + coste);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    valor = Math.Min(valor, dosAtras[j - 2] + 1);
                actual[j] = valor;
            }
            dosAtras = anterior;
            anterior = actual;
        }
        return anterior[b.Length];
    }

    private static int Tolerancia(int longitud) => longitud <= 4 ? 0 : longitud <= 7 ? 1 : 2;

    private static bool SeParece(string candidata, string buscada)
    {
        int margen = Tolerancia(buscada.Length);
        if (margen == 0) return candidata == buscada;
        if (Math.Abs(candidata.Length - buscada.Length) > margen) return false;
        return Distancia(candidata, buscada) <= margen;
    }

    private static bool Coincide(string texto, string consulta)
    {
        string termino = Normalizar(consulta);
        if (termino.Length == 0) return true;
        string heno = Normalizar(texto);
        if (heno.Contains(termino, StringComparison.Ordinal)) return true;
        var buscadas = Palabras(termino);
        if (buscadas.Count == 0) return false;
        var candidatas = Palabras(heno);
        return buscadas.All(b => heno.Contains(b, StringComparison.Ordinal) || candidatas.Any(c => SeParece(c, b)));
    }

    /// <summary>¿La categoría del atajo es esta sección? Contención o una palabra con peso en común.</summary>
    private static bool EsDeLaSeccion(string categoria, string seccion)
    {
        string cat = Normalizar(categoria), sec = Normalizar(seccion);
        if (cat.Length == 0 || sec.Length == 0) return false;
        if (cat == sec || sec.Contains(cat, StringComparison.Ordinal) || cat.Contains(sec, StringComparison.Ordinal)) return true;
        var deLaSeccion = new HashSet<string>(Palabras(sec), StringComparer.Ordinal);
        return Palabras(cat).Any(deLaSeccion.Contains);
    }

    private static int Calidad(Atajo a, string consulta, string termino)
    {
        if (termino.Length == 0) return 0;
        string titulo = Normalizar(a.Titulo);
        if (titulo.StartsWith(termino, StringComparison.Ordinal)) return 0;
        if (titulo.Contains(termino, StringComparison.Ordinal)) return 1;
        if (Normalizar(a.Categoria).Contains(termino, StringComparison.Ordinal)) return 2;
        if (Normalizar(a.Contenido).Contains(termino, StringComparison.Ordinal)) return 3;
        if (Coincide($"{a.Titulo} {a.Categoria}", consulta)) return 4;
        return -1;
    }

    /// <summary>
    /// La lista del «/»: primero la calidad de la coincidencia (título por prefijo &gt; título &gt;
    /// sección &gt; contenido &gt; parecido) y, a igualdad, los de la sección donde se escribe; después
    /// el más reciente y el título.
    /// </summary>
    public static IReadOnlyList<Atajo> Filtrar(IReadOnlyList<Atajo> atajos, string? consulta, string? seccion)
    {
        string c = consulta ?? "";
        string termino = Normalizar(c);
        var puntuados = new List<(Atajo Atajo, int Puntos)>();
        foreach (var a in atajos)
        {
            int calidad = Calidad(a, c, termino);
            if (calidad < 0) continue;
            bool mismaSeccion = !string.IsNullOrEmpty(seccion) && EsDeLaSeccion(a.Categoria, seccion);
            puntuados.Add((a, calidad * 2 + (mismaSeccion ? 0 : 1)));
        }
        return puntuados
            .OrderBy(x => x.Puntos)
            .ThenByDescending(x => x.Atajo.Actualizado, StringComparer.Ordinal)
            .ThenBy(x => x.Atajo.Titulo, Comparer<string>.Create((x, y) => Espanol.Compare(x, y, CompareOptions.None)))
            .Select(x => x.Atajo)
            .ToList();
    }
}

/// <summary>
/// LOS ATAJOS DEL MÉDICO, leídos de la MISMA tabla que la web (`user_snippets`). Promesa 457.
/// </summary>
/// <remarks>
/// El catálogo no se copia: el médico crea y edita sus atajos en la web (Plantillas → Atajos) y aquí
/// aparecen. Si no se pueden leer, la nota sigue sin atajos — una molestia, no una avería.
/// </remarks>
public static class AtajosDelMedico
{
    public const string Ruta = "/rest/v1/user_snippets?select=id,title,content,category,updated_at&order=updated_at.desc&limit=1000";

    public static async Task<IReadOnlyList<Atajo>> LeerAsync(SesionMiracle sesion, CancellationToken ct = default)
    {
        try
        {
            var (http, cuerpo) = await sesion.RestAsync(HttpMethod.Get, Ruta, ct: ct);
            if (http < 200 || http >= 300)
            {
                LogBus.Log("atajos", $"no se pudieron leer los atajos · HTTP {http}");
                return Array.Empty<Atajo>();
            }
            using var doc = JsonDocument.Parse(cuerpo);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<Atajo>();
            var lista = new List<Atajo>();
            foreach (var f in doc.RootElement.EnumerateArray())
                lista.Add(new Atajo(Cad(f, "id"), Cad(f, "title"), Cad(f, "content"), Cad(f, "category"), Cad(f, "updated_at")));
            LogBus.Log("atajos", $"{lista.Count} atajo(s) del médico");
            return lista;
        }
        catch (Exception e)
        {
            LogBus.Log("atajos", $"no se pudieron leer los atajos: {e.GetType().Name}: {e.Message}");
            return Array.Empty<Atajo>();
        }
    }

    private static string Cad(JsonElement o, string campo) =>
        o.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
