namespace U.WindowsClient.Ui;

/// <summary>
/// LA MARCA DE MIRACLE, EN VALORES. Los colores, las letras y los radios que comparten la web y
/// Windows, escritos una vez. <see cref="Estudio"/> construye con esto sus brochas; aquí no hay ni un
/// solo tipo de WPF, y es a propósito: así el contrato los juzga sin pantalla (promesas 445-448,
/// spec 054).
/// </summary>
/// <remarks>
/// SALE DE LA WEB (`Pagina-web-clientes-final/app/globals.css`, leído el 2026-09-26), con dos
/// excepciones que pidió el dueño y que no son descuidos:
///
///   · LOS BLANCOS SON LOS DE U. La web pinta su lienzo en <c>#fbfcfe</c> y la nota sobre un papel
///     cálido <c>#fdfcf9</c>; aquí todo es el blanco de U. La nota se sigue distinguiendo de los
///     controles —la razón de uso de la web—, pero por su filete cálido, su rótulo y su letra serif.
///   · EL AZUL ES UN PUNTO MÁS CLARO. «Un azul un poquitico más clarito», y sin perder AA: el más
///     claro que sigue dando 4,5:1 con texto blanco es <see cref="Acento"/>. <c>#3B7BEA</c>, el
///     primero que se probó, daba 4,03:1 — se lee peor en un botón. Donde de verdad se ve lo claro
///     es en el degradado del botón primario (<see cref="AcentoArriba"/>), igual que la web lo
///     muestra en su <c>--grad-accent</c>.
///
/// Si la web cambia uno de estos, la promesa 446 lo dice: su tabla está copiada de la web.
/// </remarks>
public static class Marca
{
    // ── blancos (los de U) ───────────────────────────────────────────────────

    /// <summary>El lienzo. El blanco de U; la web usa #fbfcfe.</summary>
    public const string Fondo = "#FFFFFF";

    /// <summary>Lo elevado: tarjetas, botones, la pestaña activa.</summary>
    public const string Superficie = "#FFFFFF";

    /// <summary>El suelo de la barra de Ü, que flota sobre el escritorio de otros (ver Estudio).</summary>
    public const string SuperficieDeLaBarra = "#F7F9FD";

    /// <summary>
    /// Un escalón por debajo de lo elevado: el carril del segmentado, un campo en reposo. Es el
    /// «pearl» de la web llevado hacia el blanco frío de U.
    /// </summary>
    public const string SuperficieSuave = "#F3F6FA";

    // ── tinta (de la web) ────────────────────────────────────────────────────

    /// <summary>Texto principal (ink). 17:1 sobre blanco.</summary>
    public const string Tinta = "#0E1726";

    /// <summary>Títulos y lo que tiene que pesar (deep).</summary>
    public const string TintaFuerte = "#0C1424";

    /// <summary>Texto de apoyo con peso (ink-soft).</summary>
    public const string TintaSuave = "#44546B";

    /// <summary>Texto secundario (muted). 5,6:1 sobre blanco.</summary>
    public const string TintaMedia = "#5D6B80";

    /// <summary>
    /// Rótulos y metadatos. La web no baja de <see cref="TintaMedia"/>; U tenía un escalón más
    /// claro y se conserva, subido hasta 4,6:1 (el disabled-ink de la web).
    /// </summary>
    public const string TintaTenue = "#69778B";

    /// <summary>Filetes (line).</summary>
    public const string Linea = "#E6EAF0";

    /// <summary>Filetes que tienen que verse (line-strong): bordes de campos y botones secundarios.</summary>
    public const string LineaFuerte = "#D4DBE6";

    /// <summary>Gris neutro (mist): el hover del borde secundario, la pista de lo vacío.</summary>
    public const string Niebla = "#CBD5E1";

    /// <summary>Superficies suaves azuladas (ice).</summary>
    public const string Hielo = "#E7F0FE";

    /// <summary>El fondo de lo que está bajo el ratón (ice-soft).</summary>
    public const string HieloSuave = "#F1F6FE";

    // ── el azul ──────────────────────────────────────────────────────────────

    /// <summary>
    /// El azul de Miracle en Windows: texto, iconos y enlaces. Un punto más claro que el
    /// <c>#2f6fe0</c> de la web y todavía 4,51:1 con blanco.
    /// </summary>
    public const string Acento = "#3272E3";

    /// <summary>El degradado del botón primario, de arriba abajo. Arriba es donde se ve lo claro.</summary>
    public const string AcentoArriba = "#3A7AEA";

    /// <summary>El pie del degradado: lo que sostiene el contraste del texto blanco.</summary>
    public const string AcentoAbajo = "#2C66D8";

    /// <summary>El azul al pasar el ratón por un enlace o un icono.</summary>
    public const string AcentoEncima = "#2559C4";

    /// <summary>El azul en su versión de fondo (accent-soft).</summary>
    public const string AcentoSuave = "#EEF4FE";

    /// <summary>Texto azul sobre <see cref="AcentoSuave"/> (accent-ink).</summary>
    public const string AcentoTinta = "#1A4FA0";

    // ── estados (de la web) ──────────────────────────────────────────────────

    /// <summary>«Esto está bien / al día / entregando» (success).</summary>
    public const string Ok = "#13795B";
    public const string OkSuave = "#DCF4EA";
    public const string OkTinta = "#0D6249";

    /// <summary>«Atención / falta algo / en pie pero no entrega» (warning).</summary>
    public const string Espera = "#A34A06";
    public const string EsperaSuave = "#FDEECF";
    public const string EsperaTinta = "#7C3A05";

    /// <summary>«Crítico / grabando / falló» (danger). La web usa el mismo rojo para el punto de grabar.</summary>
    public const string Alerta = "#B33224";
    public const string AlertaSuave = "#FBE3DF";
    public const string AlertaTinta = "#8F281E";

    // ── el documento ─────────────────────────────────────────────────────────
    //
    // La nota no es un control: tiene valor legal y el médico la lee. En la web vive sobre papel
    // cálido; aquí, sobre el blanco de U, conserva lo demás de ese papel: el filete cálido, la
    // tinta del documento y el rótulo en su gris tostado.

    /// <summary>El filete de la nota: el <c>doc-line</c> de la web, aclarado para vivir sobre blanco.</summary>
    public const string DocLinea = "#ECE7DC";

    /// <summary>El filete entre secciones de la nota (doc-line-soft).</summary>
    public const string DocLineaSuave = "#F1ECE1";

    /// <summary>El cuerpo de la nota (doc-ink).</summary>
    public const string DocTinta = "#191F28";

    /// <summary>El rótulo de cada sección de la nota (doc-muted).</summary>
    public const string DocTenue = "#6D6A62";

    // ── letra ────────────────────────────────────────────────────────────────
    //
    // DENTRO DEL EXE, nunca del sistema (promesa 445): un médico no tiene Inter instalada, y una
    // familia que no existe cae a Segoe UI sin avisar — la nota se vería «casi» igual, que es la peor
    // forma de no ser igual. Van instancias estáticas por peso: WPF no aplica los ejes de una fuente
    // variable. El ", Segoe UI" del final es solo para glifos que el recorte a latín no trae.

    /// <summary>Dónde viven las fuentes dentro del ensamblado.</summary>
    public const string RutaDeFuentes = "pack://application:,,,/U;component/assets/fuentes/";

    /// <summary>Todo lo que es interfaz (Inter).</summary>
    public const string FuenteCuerpo = RutaDeFuentes + "#Inter, Segoe UI";

    /// <summary>Títulos (Schibsted Grotesk), a 600-650 como en la web.</summary>
    public const string FuenteTitulo = RutaDeFuentes + "#Schibsted Grotesk, Segoe UI";

    /// <summary>El cuerpo de la nota (Source Serif 4), a 17 px con interlineado 1,62.</summary>
    public const string FuenteDocumento = RutaDeFuentes + "#Source Serif 4, Georgia";

    /// <summary>El cronómetro y los datos tabulares (Geist Mono).</summary>
    public const string FuenteMono = RutaDeFuentes + "#Geist Mono, Consolas";

    /// <summary>El tamaño del cuerpo de la nota en la web: 1,0625 rem.</summary>
    public const double TamanoDocumento = 17;

    /// <summary>Su interlineado, en múltiplos del tamaño.</summary>
    public const double InterlineadoDocumento = 1.62;

    // ── radios (de la web) ───────────────────────────────────────────────────

    /// <summary>Chips, botones de icono, campos (radius-sm).</summary>
    public const double RadioChico = 12;

    /// <summary>Tarjetas y paneles (radius-md).</summary>
    public const double RadioMedio = 16;

    /// <summary>Paneles grandes y la ventana (radius-lg).</summary>
    public const double RadioPanel = 22;

    // ── el rótulo ────────────────────────────────────────────────────────────

    /// <summary>El espacio de pelo: ≈0,1 em en Inter, el <c>letter-spacing: .11em</c> del rótulo de la web.</summary>
    public const char Espaciado = ' ';

    /// <summary>
    /// Un rótulo de sección como el de la web: en mayúsculas y con aire entre letras (promesa 448).
    /// </summary>
    /// <remarks>
    /// WPF no tiene espaciado de letras, y un rótulo en versalitas sin él se lee apretado: es lo
    /// primero que delata que no es la misma app. Se intercala un espacio de pelo entre cada par de
    /// caracteres. Solo en rótulos: no se copian ni se leen en voz alta, y ahí el truco no cuesta.
    /// </remarks>
    public static string Rotulo(string texto)
    {
        if (string.IsNullOrEmpty(texto)) return "";
        var mayus = texto.ToUpperInvariant();
        var sb = new System.Text.StringBuilder(mayus.Length * 2);
        for (int i = 0; i < mayus.Length; i++)
        {
            if (i > 0) sb.Append(Espaciado);
            sb.Append(mayus[i]);
        }
        return sb.ToString();
    }
}
