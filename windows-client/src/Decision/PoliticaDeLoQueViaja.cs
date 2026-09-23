using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace U.WindowsClient.Decision;

/// <summary>
/// QUÉ SUPERFICIES PUEDEN MANDAR SU TEXTO A JEV, qué parte de la ubicación viaja (promesa 393), y con qué id viaja —y
/// se registra— cada puerta: las filas, nunca por su texto (promesa 350). Spec 046.
/// </summary>
/// <remarks>
/// ES EL ÚNICO SITIO QUE DECIDE QUÉ VIAJA, y no por orden: hasta el 2026-09-22 la política vivía solo en prosa
/// —un comentario de <c>PeticionASystemOne</c> y <c>docs/el-decisor-y-typesafe.md</c>—, y las dos decían «solo
/// etiquetas» mientras el cuerpo llevaba la ubicación ENTERA (en <c>uia://</c> el pathname es el título vivo de la
/// ventana) y SAP mandaba lo mismo que cualquier otra app. <see cref="ElDecisor"/> la consulta antes del transporte.
///
/// LO QUE NO PUEDE VIAJAR NO SE DECIDE, Y SE DICE. No se cae a la regla local (<c>Simulado</c>): con
/// <c>U_DECISOR=jev</c> eso era cambiar de juez —una regla que acciona con 1,00 por una palabra mientras el botón
/// dice «jev»—, no fallar cerrado (revisión 5 de la spec). Decide Luna.
///
/// SAP NO MANDA NADA SALVO HABILITACIÓN EXPLÍCITA (<c>U_DECISOR_SAP_TEXTO=si</c>), y la variable no basta sola: la
/// spec exige además la decisión escrita del dueño y del hospital, que hoy no existe (la retención cero de TypeSafe
/// es solo enterprise y aloja en EE. UU.). El prefijo <c>sapgui://</c> se compara por el mismo camino que los 16
/// sitios que ya lo hacen a mano (<c>StartsWith("sapgui://", OrdinalIgnoreCase)</c>, aprendizaje nº16); no se
/// refactorizan, se nombran en el commit de la fase 6.
/// </remarks>
public sealed class PoliticaDeLoQueViaja
{
    /// <summary>Habilita que una pantalla de SAP mande su texto a Jev. Solo con «si»: vacío es ausente (patrón nº9).</summary>
    public const string VariableSap = "U_DECISOR_SAP_TEXTO";

    /// <summary>Orígenes que no mandan texto a Jev, además de <see cref="VetadosPorDefecto"/>; separados por «;».</summary>
    public const string VariableVetados = "U_DECISOR_TEXTO_VETADO";

    /// <summary>
    /// LOS VETADOS QUE NO DEPENDEN DE NINGUNA VARIABLE: un veto que dependa de una variable que nadie puso no es
    /// fallar cerrado. Son los dominios de Miracle que este repo conoce (<c>itsmiracleai.com</c>, y
    /// <c>itsmiracleai.com.co</c> en <c>mapeador/Contrato</c>): el portal clínico cuelga de ellos por deducción, y
    /// el host exacto se le pide al dueño en el PR para que entre como constante, no como variable (2026-09-22).
    /// En <c>web://</c> un veto cubre el host y sus subdominios; <c>itsmiracleai.com.co</c> no es subdominio de
    /// <c>itsmiracleai.com</c>, y por eso van los dos.
    /// </summary>
    public static IReadOnlyList<string> VetadosPorDefecto { get; } =
        Array.AsReadOnly(new[] { "web://itsmiracleai.com", "web://itsmiracleai.com.co" });

    /// <summary>La política sin variables de entorno: SAP no manda, y solo los vetados por defecto. La de <c>Elegir</c> de seis.</summary>
    public static PoliticaDeLoQueViaja PorDefecto { get; } = new PoliticaDeLoQueViaja(false, "", VetadosPorDefecto);

    /// <summary>Si una pantalla <c>sapgui://</c> puede mandar su texto a Jev.</summary>
    public bool SapHabilitado { get; }

    /// <summary>Todos los orígenes vetados: los de por defecto y los de <see cref="VariableVetados"/>.</summary>
    public IReadOnlyList<string> Vetados { get; }

    /// <summary>Lo que se leyó en <see cref="VariableSap"/>, para decirlo cuando no habilita (patrón nº2).</summary>
    private readonly string _leidoSap;

    private PoliticaDeLoQueViaja(bool sapHabilitado, string leidoSap, IReadOnlyList<string> vetados)
    {
        SapHabilitado = sapHabilitado;
        _leidoSap = leidoSap;
        Vetados = vetados;
    }

    /// <summary>Lee las dos variables. El entorno se pasa, no se deduce: el contrato lo necesita de mentira.</summary>
    public static PoliticaDeLoQueViaja Leer(Func<string, string?> entorno)
    {
        if (entorno == null) throw new ArgumentNullException(nameof(entorno));

        string sap = (entorno(VariableSap) ?? "").Trim();
        bool habilitado = sap.Equals("si", StringComparison.OrdinalIgnoreCase) || sap.Equals("sí", StringComparison.OrdinalIgnoreCase);

        var vetados = new List<string>(VetadosPorDefecto);
        foreach (var v in (entorno(VariableVetados) ?? "").Split(';'))
            if (!string.IsNullOrWhiteSpace(v)) vetados.Add(v.Trim());

        return new PoliticaDeLoQueViaja(habilitado, sap, vetados.AsReadOnly());
    }

    /// <summary>
    /// Lo que viaja de la ubicación: su ORIGIN (<c>uia://explorer.exe</c>, <c>web://mail.google.com</c>), nunca el
    /// pathname. En <c>uia://</c> el pathname es el título vivo de la ventana («/Historia clínica de …») y en
    /// <c>web://</c> la ruta de la página. Se recorta con <c>SurfacePlace.OriginOf</c>, el camino del resto del código.
    /// </summary>
    public static string UbicacionQueViaja(string pantalla) => U.Graph.SurfacePlace.OriginOf(pantalla ?? "");

    /// <summary>
    /// ¿Esta pantalla puede mandar su texto a Jev? Si no, <paramref name="porque"/> dice cuál de las dos reglas mordió
    /// —SAP sin habilitar, u origin vetado y bajo qué veto— y que decide Luna; vacío si puede.
    /// </summary>
    public bool PuedeViajar(string pantalla, out string porque)
    {
        porque = "";
        string origin = UbicacionQueViaja(pantalla);

        // SAP TIENE DOS IDENTIDADES, y las dos son SAP (aprendizaje nº16). Cuando el Scripting no da identidad —SAP Busy,
        // Identity() sin contestar—, SurfaceLocator cae al esquema uia:// con el proceso de SAP: «uia://saplogon.exe/…».
        // Hasta el 2026-09-22 aquí solo se miraba sapgui://, y esa pantalla viajaba a Jev con lo que UIA y el terreno
        // publicaran de ella (medido en el rojo de la fase 10, con el código de d905e6a: 1 llamada y Actuar=True). Se reconoce
        // con el MISMO criterio que la acuñó (SurfaceLocator.IsSap: el proceso empieza por «sap»), no con una lista de
        // versiones ni con Mundos.EsSapVistoPorUia, que solo conoce saplogon.exe.
        bool sapPorUia = origin.StartsWith("uia://", StringComparison.OrdinalIgnoreCase)
                         && U.WindowsClient.Uia.SurfaceLocator.IsSap(origin["uia://".Length..]);
        if ((origin.StartsWith("sapgui://", StringComparison.OrdinalIgnoreCase) || sapPorUia) && !SapHabilitado)
        {
            string leido = _leidoSap.Length == 0 ? $"sin {VariableSap}=si" : $"con {VariableSap}=«{_leidoSap}» (se habilita solo con «si»)";
            string cual = sapPorUia ? $"«{origin}» es SAP GUI visto por UIA," : "sapgui://";
            porque = $"{cual} {leido}: no se manda texto de SAP a Jev y no decido por regla local. Decide Luna.";
            return false;
        }

        foreach (var v in Vetados)
            if (CaeBajo(pantalla ?? "", origin, v))
            {
                porque = $"«{origin}» está vetado para Jev (cae bajo «{v}»): no se manda texto y no decido por regla local. Decide Luna.";
                return false;
            }
        return true;
    }

    /// <summary>
    /// En <c>web://</c>, el host del veto y sus subdominios (sin puerto; la ruta del veto no acota: vetar de más es
    /// fallar cerrado). Otro esquema, el prefijo de la pantalla. Sin esquema, el host en cualquier esquema: una
    /// entrada como «historia» no puede quedarse sin vetar nada en silencio (aprendizaje nº18).
    /// </summary>
    private static bool CaeBajo(string pantalla, string origin, string veto)
    {
        int esquema = veto.IndexOf("://", StringComparison.Ordinal);
        if (esquema >= 0 && !veto.StartsWith("web://", StringComparison.OrdinalIgnoreCase))
            return pantalla.Trim().StartsWith(veto, StringComparison.OrdinalIgnoreCase);
        if (esquema >= 0 && !origin.StartsWith("web://", StringComparison.OrdinalIgnoreCase))
            return false;

        string host = Host(origin);
        string hostDelVeto = esquema >= 0 ? Host(U.Graph.SurfacePlace.OriginOf(veto)) : Host(veto);
        return hostDelVeto.Length > 0
            && (host.Equals(hostDelVeto, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith("." + hostDelVeto, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>«web://app.sitio.com:8080» → «app.sitio.com»; sin esquema, lo que hay hasta la primera «/» o «:».</summary>
    private static string Host(string origin)
    {
        string s = origin.Trim();
        int esquema = s.IndexOf("://", StringComparison.Ordinal);
        if (esquema >= 0) s = s[(esquema + 3)..];
        int corte = s.IndexOfAny(new[] { '/', ':' });
        return corte < 0 ? s : s[..corte];
    }

    /// <summary>
    /// LOS TIPOS DE FILA, cuyo texto nunca viaja ni se registra (promesa 350): una fila de rejilla o de árbol es DATO
    /// —un nombre y un documento, en la lista de pacientes de NWP1—, no cromo. Se comparan EXACTOS y por este solo
    /// camino (aprendizaje nº16). Los escriben literalmente los sitios que construyen filas, medidos el 2026-09-22:
    /// <c>MundoQueToca.cs:238, :335, :351</c> y <c>FaceWindow.xaml.cs:380, :5399</c>; los cinco con estos tres nombres.
    /// Hay una sexta rama, <c>MundoQueToca.cs:360-362</c>, que emitiría una fila de árbol con el tipo de su shell y no
    /// se reconocería aquí; hoy es inerte (el único <c>new SapVisualElement(</c> pasa <c>IsNode: false</c>). Si se
    /// despierta, su tipo tiene que ser uno de estos tres, o su texto viajará.
    /// </summary>
    public static IReadOnlyList<string> TiposDeFila { get; } =
        Array.AsReadOnly(new[] { "GuiGridFila", "GuiTreeFila", "GuiTreeCarpeta" });

    /// <summary>
    /// «2) fila de prueba · 000 (GuiGridFila)» → número «2» y tipo «GuiGridFila». Es el INVERSO del formato con que
    /// <c>UnPasoDecidido</c> numera las puertas («N) etiqueta (Tipo)»): el tipo es el ÚLTIMO paréntesis, así que una
    /// etiqueta con paréntesis dentro no engaña. El número puede faltar (una lista sin numerar).
    /// </summary>
    private static readonly Regex _idDePuerta =
        new(@"^\s*(?:(\d+)\)\s*)?(.*?)\s*\(([^()]*)\)\s*$", RegexOptions.Singleline | RegexOptions.CultureInvariant);

    /// <summary>
    /// Si el id de una puerta es una fila (<see cref="TiposDeFila"/>, tipo exacto), con su número («» si no lo trae) y
    /// su tipo. Es el ÚNICO reconocedor: lo usan <see cref="IdQueViaja"/> (qué viaja) y <see cref="NombreParaContar"/>
    /// (qué se registra), para que lo que viaja sin texto y lo que se cuenta sin texto no puedan discrepar.
    /// </summary>
    public static bool EsFila(string id, out string numero, out string tipo)
    {
        numero = ""; tipo = "";
        var m = _idDePuerta.Match(id ?? "");
        if (!m.Success) return false;
        foreach (var t in TiposDeFila)
            if (string.Equals(m.Groups[3].Value, t, StringComparison.Ordinal))
            {
                numero = m.Groups[1].Value;
                tipo = t;
                return true;
            }
        return false;
    }

    /// <summary>
    /// EL ID CON EL QUE UNA PUERTA VIAJA A JEV (350): una fila viaja como «2) fila (GuiGridFila)», sin su texto, TAMBIÉN
    /// con SAP habilitado —habilitar SAP es dejar que viaje el cromo de la pantalla, no la lista de pacientes—; el resto,
    /// tal cual. Jev elige entre filas por su número y su tipo, y <see cref="ElDecisor"/> devuelve su respuesta a la
    /// puerta ofrecida, que es la que lleva detrás el selector.
    /// </summary>
    public static string IdQueViaja(string id)
    {
        if (!EsFila(id, out string numero, out string tipo)) return id;
        return numero.Length > 0 ? $"{numero}) fila ({tipo})" : $"fila ({tipo})";
    }

    /// <summary>
    /// CÓMO SE NOMBRA UNA PUERTA EN LO QUE SE REGISTRA Y SE CUENTA (350): la etiqueta, y si es una fila, «fila 2
    /// (GuiGridFila)». Lo que no viaja por su texto tampoco se escribe por su texto en el log: la línea «decisor:», el
    /// relato de <c>map_decidir</c>, el veto y la línea «paso k:» del tramo salen al disco y al notch.
    /// </summary>
    public static string NombreParaContar(string id, string etiqueta)
    {
        if (!EsFila(id, out string numero, out string tipo)) return etiqueta;
        return numero.Length > 0 ? $"fila {numero} ({tipo})" : $"fila ({tipo})";
    }
}
