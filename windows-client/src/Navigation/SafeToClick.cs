namespace U.WindowsClient.Navigation;

/// <summary>
/// Qué puede pulsar un explorador AUTÓNOMO. Es la capa 1: determinista, legible y sin red.
///
/// POR QUÉ NO BASTA EL LLM. La propuesta era usar un modelo para evitar pulsar «Vaciar papelera»,
/// y como priorizador es excelente — sabe qué merece explorarse. Pero como ÚNICA barrera no
/// sirve: acierta casi siempre, y «casi» sobre un botón que borra archivos del usuario no es un
/// nivel de garantía aceptable. Un fallo aquí no es un mapa peor, es un archivo perdido.
///
/// Así que el reparto es el que este proyecto ya tiene escrito como dirección: CAPA 1
/// DETERMINISTA, LLM EN CAPA 2. Aquí se decide qué es tocable —una lista que se puede leer,
/// auditar y probar—, y el modelo elige después ENTRE lo que ya es seguro. El modelo puede
/// equivocarse y lo peor que pasa es que explore en mal orden.
///
/// La regla de fondo: se explora NAVEGANDO, no operando. Navegar es reversible; operar no.
/// </summary>
public static class SafeToClick
{
    /// <summary>
    /// Verbos que MODIFICAN el sistema o los datos. Basta que aparezcan como palabra en la
    /// etiqueta para vetar el elemento. En español e inglés porque la UI de Windows mezcla ambos
    /// —y una lista que solo cubra el idioma del desarrollador es una lista que falla en la
    /// máquina del cliente—.
    /// </summary>
    private static readonly string[] Prohibido =
    {
        // destruir
        "eliminar", "borrar", "delete", "vaciar", "empty", "quitar", "remove",
        "desinstalar", "uninstall", "formatear", "format", "destruir",
        // mover/alterar
        "mover", "move", "renombrar", "rename", "cortar", "cut", "pegar", "paste",
        "reemplazar", "replace", "comprimir", "compress", "extraer", "extract",
        "sobrescribir", "overwrite", "guardar", "save", "aplicar", "apply",
        // compartir / salir del equipo
        "compartir", "share", "enviar", "send", "publicar", "publish", "subir", "upload",
        "sincronizar", "sync", "correo", "mail",
        // sesión y energía
        "cerrar sesión", "cerrar sesion", "sign out", "log out", "apagar", "shut down",
        "reiniciar", "restart", "suspender", "sleep", "bloquear", "lock",
        // decisiones y permisos
        "aceptar", "accept", "confirmar", "confirm", "permitir", "allow", "instalar", "install",
        "comprar", "buy", "pagar", "pay", "suscribir", "subscribe",
        // administración
        "propiedades", "properties", "configurar", "settings", "opciones", "options",
        "administrador", "admin", "seguridad", "security", "permisos", "permissions",
    };

    /// <summary>
    /// Tipos de control que un explorador autónomo puede pulsar. NAVEGACIÓN pura: elementos de
    /// árbol, de lista, pestañas y enlaces. Fuera quedan botones y menús, que es donde vive todo
    /// lo que opera — el «Eliminar» de la cinta del explorador es un Button, y el menú contextual
    /// entero es MenuItem.
    ///
    /// Un humano SÍ puede pulsar botones desde el panel del grafo: ahí hay alguien mirando y
    /// decidiendo. La restricción es para lo autónomo.
    /// </summary>
    private static readonly HashSet<string> TiposNavegables = new(StringComparer.OrdinalIgnoreCase)
    {
        "treeitem", "listitem", "tabitem", "hyperlink",
    };

    /// <summary>
    /// Sitios donde NO se entra al mapear. No es seguridad, es alcance: mapear el disco del sistema
    /// son decenas de miles de carpetas que no enseñan nada sobre CÓMO se navega la app —solo sobre
    /// qué archivos hay, que es otra pregunta y se responde leyendo el sistema de archivos en
    /// milisegundos en vez de clicando durante horas.
    /// </summary>
    private static readonly string[] FueraDeAlcance =
    {
        "este equipo", "this pc", "disco local", "local disk", "unidad", "drive",
        "red", "network", "papelera", "recycle", "panel de control", "control panel",
        "windows", "archivos de programa", "program files",
    };

    /// <summary>
    /// ¿Este elemento de lista es un CONTENEDOR en el que se puede entrar, o un documento que al
    /// abrirse lanzaría otra aplicación?
    ///
    /// Abrir un archivo no explora la app: la abandona. En la corrida del 2026-07-31 el recorrido
    /// hizo doble clic en «Screenshot_…_WhatsApp» y se encontró dentro de Photos.exe, mapeando una
    /// app que nadie había pedido. Los archivos SÍ se registran como puertas —saber que están ahí
    /// es parte del mapa— pero no se cruzan.
    ///
    /// La app dice lo que es cada cosa en <c>ItemType</c> («Carpeta de archivos», «Imagen PNG»), y
    /// esa es la fuente buena. Cuando no lo dice, se cae al nombre: una extensión al final delata
    /// un archivo. Ojo, ese respaldo es débil —Windows oculta las extensiones conocidas— y por eso
    /// existe además la red de seguridad del recorrido: si tras pulsar aparece OTRA aplicación en
    /// primer plano, se vuelve y no se aprende nada.
    /// </summary>
    public static bool EsContenedor(string label, string itemType)
    {
        string t = Normalizar(itemType);
        if (t.Length > 0)
            return t.Contains("carpeta") || t.Contains("folder") || t.Contains("directorio")
                || t.Contains("unidad") || t.Contains("drive") || t.Contains("biblioteca");

        return !System.Text.RegularExpressions.Regex.IsMatch(
            label ?? "", @"\.[A-Za-z0-9]{1,6}$");
    }

    /// <summary>¿Un explorador autónomo puede pulsar esto? Ante la duda, NO.</summary>
    public static bool Auto(string label, string controlType, out string motivo)
    {
        motivo = "";
        string l = (label ?? "").Trim();
        if (l.Length == 0) { motivo = "sin etiqueta"; return false; }

        string n0 = Normalizar(l);
        foreach (string fuera in FueraDeAlcance)
        {
            if (n0.Equals(Normalizar(fuera), StringComparison.Ordinal)
                || n0.StartsWith(Normalizar(fuera) + " ", StringComparison.Ordinal))
            {
                motivo = $"fuera de alcance: «{fuera}»";
                return false;
            }
        }
        // Unidades por letra: «C:», «Disco local (C:)», «USB (E:)»…
        if (System.Text.RegularExpressions.Regex.IsMatch(l, @"\([A-Za-z]:\)|^[A-Za-z]:\\?$"))
        {
            motivo = "es una unidad de disco";
            return false;
        }

        if (!TiposNavegables.Contains(controlType ?? ""))
        {
            motivo = $"tipo «{controlType}» no es de navegación";
            return false;
        }

        string norm = Normalizar(l);
        foreach (string v in Prohibido)
        {
            if (ContienePalabra(norm, Normalizar(v)))
            {
                motivo = $"la etiqueta contiene «{v}»";
                return false;
            }
        }
        return true;
    }

    /// <summary>Minúsculas y sin tildes: «Eliminar» y «eliminar» son lo mismo, y también «Móver».</summary>
    private static string Normalizar(string s)
    {
        string d = s.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(d.Length);
        foreach (char c in d)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(c) || c == ' ' ? c : ' ');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Palabra completa, no trozo. «Documentos» no puede vetarse por contener «mover» dentro de
    /// otra palabra, y a la vez «Eliminar todo» sí tiene que caer. Es la misma lección que ya nos
    /// costó cuatro iteraciones en el emparejamiento de conceptos clínicos.
    /// </summary>
    private static bool ContienePalabra(string texto, string palabra)
    {
        if (palabra.Contains(' ')) return texto.Contains(palabra, StringComparison.Ordinal);
        return texto.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Any(p => p.Equals(palabra, StringComparison.Ordinal));
    }
}
