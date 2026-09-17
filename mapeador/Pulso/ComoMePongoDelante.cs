namespace Mapeador;

/// <summary>
/// PARA PONERME DELANTE DE ESTA SUPERFICIE, ¿QUÉ HAY QUE HACER? Puro: recibe un id y devuelve la
/// vía, sin tocar nada. Quien ejecuta es el cliente; aquí solo se decide.
/// </summary>
/// <remarks>
/// NO TODA SUPERFICIE ES UN PROCESO, y confundirlas nos ha mordido tres veces, siempre igual: tomar
/// la parte de la izquierda del id y tratarla como el nombre de un ejecutable.
///
///   · «web://itsmiracleai.com.co/…» → la izquierda es un DOMINIO. Se buscó un proceso con ese
///     nombre y, al no haberlo, se intentó LANZAR un programa llamado así. El usuario lo vio como
///     «no pude traer "itsmiracleai.com.co" al frente» (2026-08-14).
///   · «sapgui://QAS/…» → la izquierda es un SISTEMA SAP. Habría intentado lanzar «QAS».
///   · Y por el otro lado, la atribución de clics comparaba ese mismo dominio contra el proceso del
///     clic —«chatgpt.com» contra «chrome»— y tiraba cuatro navegaciones web reales seguidas.
///
/// LO DESCONOCIDO SE DICE, NO SE ADIVINA, y esta es la promesa que más protege: un workflow sellado
/// en «uia://desktop» hizo que se intentara «lanzar un programa llamado desktop», y el shell resolvió
/// … Docker Desktop (2026-07-31). Abrir un programa al azar en la máquina de alguien es de las cosas
/// más molestas que puede hacer un agente, y encima la alineación fallaba igual.
/// </remarks>
public static class ComoMePongoDelante
{
    /// <summary>Por dónde se alcanza una superficie.</summary>
    public enum Via
    {
        /// <summary>No se reconoce el tipo de superficie. Se dice y se para.</summary>
        NoSe,
        /// <summary>Una ventana de un proceso: enfocarla, o abrir la app si no está.</summary>
        Proceso,
        /// <summary>Una página: activar SU PESTAÑA, no abrir otra copia del sitio.</summary>
        PestanaDelNavegador,
        /// <summary>Una sesión de SAP: la ventana la pone SAP GUI, no el sistema.</summary>
        SapGui,
    }

    /// <summary>La vía y a quién hay que pedírselo: un proceso, un dominio.</summary>
    public readonly record struct Plan(Via Via, string Que);

    public static Plan De(string idDeSuperficie)
    {
        string id = (idDeSuperficie ?? "").Trim();
        if (id.Length == 0) return new(Via.NoSe, "");

        // IR A UN SITIO Y ABRIR OTRA COPIA DEL SITIO NO SON LA MISMA ACCIÓN: la segunda deja dos
        // estados de la misma página y pierde lo que hubiera a medias en la primera. Por eso lo web
        // se alcanza por su pestaña y con el DOMINIO, que es lo que la memoria de pestañas indexa.
        if (id.StartsWith("web://", StringComparison.OrdinalIgnoreCase))
        {
            string dominio = id[6..].Split('/')[0];
            return dominio.Length == 0 ? new(Via.NoSe, "") : new(Via.PestanaDelNavegador, dominio);
        }

        // SAP se nombra por su sistema —«QAS»—, que no es un ejecutable. La ventana la pone SAP GUI.
        // Pero un «sapgui://» pelado no nombra ninguna sesión, así que tampoco dice a dónde ir: se
        // trata como desconocido. Lo cazó la promesa 22, sobre este mismo archivo.
        if (id.StartsWith("sapgui://", StringComparison.OrdinalIgnoreCase))
            return id[9..].Split('/')[0].Length == 0 ? new(Via.NoSe, "") : new(Via.SapGui, "saplogon");

        if (id.StartsWith("uia://", StringComparison.OrdinalIgnoreCase))
        {
            string proc = id[6..].Split('/')[0];
            if (proc.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) proc = proc[..^4];
            return proc.Length == 0 ? new(Via.NoSe, "") : new(Via.Proceso, proc);
        }

        return new(Via.NoSe, "");
    }

    /// <summary>
    /// ¿Estar en ESTE sitio ya es haber llegado? Solo cuando el destino nombra el sitio entero.
    /// </summary>
    /// <remarks>
    /// «Abre GitHub» con GitHub YA abierto en «…/settings/secrets/actions» contestaba «no hay
    /// ningún camino aprendido de actions hasta ahí»: se traía la pestaña al frente, se comparaba
    /// esa dirección con «web://github.com», no eran iguales, y se buscaba una ruta A PIE dentro de
    /// una web. Decía que no sabía llegar estando ya allí (2026-08-16, lo vio el usuario en el
    /// panel).
    ///
    /// «web://github.com» —sin ruta— nombra el SITIO, y a un sitio se llega estando en cualquiera
    /// de sus páginas; es lo que significa «abre GitHub». Con ruta —«web://github.com/BasedHardware/
    /// omi»— se pide una página concreta y estar en otra NO es haber llegado.
    ///
    /// Y quien lo diga tiene que decir DÓNDE está de verdad: «ya estabas en github.com» a secas
    /// sería cierto y aun así engañoso.
    /// </remarks>
    /// <remarks>
    /// LO QUE EL NAVEGADOR NORMALIZA NO ES OTRA PÁGINA (spec 029, 2026-09-17). En tres días de corridas hubo 21
    /// «no hay ningún camino aprendido» a 15 s cada uno, y en al menos ocho Ü YA ESTABA donde se le pidió: pidió
    /// www.google.com y estaba en google.com; pidió …/document/u/0/ y estaba en …/document/u/0; pidió …/search?q=…
    /// y el localizador no guarda la query; pidió ycombinator.com y el navegador lo mandó a apply.ycombinator.com.
    /// La versión anterior exigía igualdad exacta con cualquier ruta, y declaraba fracaso tras agotar el plazo
    /// estando allí. Lo que la 24 defendía SIGUE EN PIE: pedir una página no se cumple con otra página del sitio.
    /// La regla nueva: mismo sitio (sin «www.», y un subdominio cuenta si se pidió el sitio a secas) y la ruta
    /// pedida como comienzo, por tramos, de la ruta real —sin barra final ni query—.
    /// </remarks>
    public static bool EstarEnElSitioBasta(string destino, string donde)
    {
        var d = Trocear(destino);
        var a = Trocear(donde);
        if (d == null || a == null) return false;

        bool mismoSitio = d.Value.Host.Equals(a.Value.Host, StringComparison.OrdinalIgnoreCase);
        if (d.Value.Ruta.Length == 0)
        {
            // Se pidió el SITIO: vale cualquiera de sus páginas, y también un subdominio suyo (apply.ycombinator.com).
            return mismoSitio || a.Value.Host.EndsWith("." + d.Value.Host, StringComparison.OrdinalIgnoreCase);
        }
        // Se pidió una PÁGINA: mismo sitio, y la ruta pedida es el comienzo de la real, tramo a tramo.
        if (!mismoSitio) return false;
        return a.Value.Ruta.Equals(d.Value.Ruta, StringComparison.OrdinalIgnoreCase)
            || a.Value.Ruta.StartsWith(d.Value.Ruta + "/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>«web://www.Sitio.com/ruta/?q=x» → (sitio.com, ruta). Null si no es una web.</summary>
    private static (string Host, string Ruta)? Trocear(string id)
    {
        string t = (id ?? "").Trim();
        if (!t.StartsWith("web://", StringComparison.OrdinalIgnoreCase)) return null;
        t = t[6..];
        int q = t.IndexOf('?'); if (q >= 0) t = t[..q];           // la query el localizador no la guarda
        int barra = t.IndexOf('/');
        string host = (barra >= 0 ? t[..barra] : t).ToLowerInvariant();
        string ruta = barra >= 0 ? t[(barra + 1)..].Trim('/') : "";
        if (host.StartsWith("www.", StringComparison.Ordinal)) host = host[4..];
        return host.Length == 0 ? null : (host, ruta);
    }

    /// <summary>
    /// ¿Este id pide una PÁGINA concreta, y no el sitio entero? Solo entonces activar la pestaña
    /// del dominio NO basta para estar delante.
    /// </summary>
    /// <remarks>
    /// EL COMPLEMENTO DE <see cref="EstarEnElSitioBasta"/>, y faltaba: «ponme delante de
    /// wiki/Ajedrez» estando en Portal:Ajedrez activaba la pestaña del dominio —que ya estaba
    /// delante—, contestaba «conseguido» sin navegar, y quien esperaba la llegada esperaba en vano
    /// (2026-08-25, revancha del piloto). Pedir el sitio se satisface con cualquiera de sus
    /// páginas; pedir una página, solo con esa.
    /// </remarks>
    public static bool PideUnaPagina(string idDeSuperficie)
    {
        string id = (idDeSuperficie ?? "").Trim().TrimEnd('/');
        if (!id.StartsWith("web://", StringComparison.OrdinalIgnoreCase)) return false;
        return id[6..].Contains('/');
    }

    /// <summary>
    /// ¿La pestaña activa —host y ruta de su barra de direcciones— YA es esta página? Si lo es,
    /// estar delante no requiere tocar nada; si no, hay que ir por la dirección.
    /// </summary>
    /// <remarks>
    /// Se compara contra host y ruta ABSOLUTA porque eso es exactamente lo que el localizador lee
    /// del navegador y lo que el id guarda («web://host/ruta», sin query ni fragmento — estado
    /// volátil, no ubicación). La barra final se ignora en los dos lados por la misma razón de
    /// siempre: «/wiki/Ajedrez/» y «/wiki/Ajedrez» son la misma pantalla.
    /// </remarks>
    public static bool EsLaMismaPagina(string idDeSuperficie, string host, string rutaAbsoluta)
    {
        string id = (idDeSuperficie ?? "").Trim().TrimEnd('/');
        if (!id.StartsWith("web://", StringComparison.OrdinalIgnoreCase)) return false;

        string resto = id[6..];
        int corte = resto.IndexOf('/');
        string sitio = corte < 0 ? resto : resto[..corte];
        string ruta = corte < 0 ? "" : resto[corte..];

        if (!sitio.Equals((host ?? "").Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        return ruta.Equals((rutaAbsoluta ?? "").Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// La dirección que hay que abrir para llegar a una superficie web. Vacío si no es web.
    /// </summary>
    /// <remarks>
    /// SE RECONSTRUYE DESDE EL ID PORQUE EL ID ES LO QUE HAY. El núcleo guarda «web://dominio/ruta»
    /// —sin query ni fragmento, que son estado volátil y no ubicación— y esa es exactamente la
    /// dirección a la que hay que ir para estar en esa pantalla.
    ///
    /// EL ESQUEMA SE RECUERDA, NO SE SUPONE. Poner «https://» a ciegas rompe los sitios que solo
    /// hablan http —un portal cautivo, un equipo en la red local— y el fallo sería mudo: el
    /// navegador abre, no carga, y el mapa dice que no llegó sin decir por qué. Se pasa el que se
    /// vio al mapear; «https» solo cuando no consta ninguno.
    /// </remarks>
    public static string UrlDe(string idDeSuperficie, string esquemaVisto = "")
    {
        string id = (idDeSuperficie ?? "").Trim();
        if (!id.StartsWith("web://", StringComparison.OrdinalIgnoreCase)) return "";

        string resto = id[6..].TrimEnd('/');
        if (resto.Split('/')[0].Length == 0) return "";

        string esquema = string.IsNullOrWhiteSpace(esquemaVisto) ? "https" : esquemaVisto.Trim().ToLowerInvariant();
        // Solo lo que un navegador entiende. Si lo recordado fuera cualquier otra cosa, abrirlo sería
        // pedirle al sistema que ejecute algo que no sabemos qué es.
        if (esquema != "http" && esquema != "https") esquema = "https";
        return esquema + "://" + resto;
    }
}
