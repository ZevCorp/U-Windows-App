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
    /// ¿Esta puerta NAVEGA (te lleva a otra pantalla) o EJECUTA (hace algo aquí)?
    ///
    /// El grafo registra las dos —los botones de acción son la mitad del valor: sin «Nuevo»,
    /// «Cortar» o «Pegar» el asistente puede llegar a cualquier sitio y no hacer nada al llegar—
    /// pero se comportan distinto: durante el MAPEO solo se cruzan las de navegación (pulsar
    /// «Eliminar» para ver a dónde lleva no es explorar, es romper), y durante la EJECUCIÓN las
    /// de acción se pulsan a propósito vía map_take.
    ///
    /// v1 determinista por tipo de control: árbol/lista/pestaña/enlace navegan, el resto ejecuta.
    /// La etiqueta viaja en la firma porque el refinamiento fino —«Guardar como…» abre un diálogo,
    /// ¿eso navega o ejecuta?— es CRITERIO, no sintaxis, y ahí entrará el LLM de capa 2 leyendo
    /// las puertas ya registradas. La clasificación es dato del mapa, no del clasificador.
    /// </summary>
    public static string Clasificar(string label, string controlType) =>
        TiposNavegables.Contains(controlType ?? "") ? "navegacion" : "accion";

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
        // Dependencias y artefactos de compilación: miles de carpetas generadas que no enseñan
        // NADA sobre cómo se navega la app, solo sobre qué proyectos tiene el usuario. Una corrida
        // se perdió 20 minutos dentro de node_modules/.next/dev sin terminar (2026-08-02). Mismo
        // criterio que «Este equipo»: el mapa describe la app, no el disco.
        "node_modules", ".git", ".next", ".venv", "__pycache__", "obj", "bin",
        "dist", "build", "packages", "vendor", ".vs", ".idea", "target",
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

    /// <summary>
    /// LO QUE NO SE DESHACE. Es la lista de responder diálogos, y es DISTINTA de <see cref="Prohibido"/>
    /// a propósito (promesa 239, spec 022).
    /// </summary>
    /// <remarks>
    /// Las dos listas contestan preguntas distintas y compartirlas costó caro. <see cref="Auto"/>
    /// pregunta «¿puede el explorador pulsar esto MIENTRAS MAPEA, sin nadie mirando?», y por eso su
    /// lista es larguísima y rechaza hasta «Opciones»: al mapear, todo lo que no sea navegar sobra.
    /// Esta pregunta es otra: «¿esto hace daño AUNQUE me lo pidan?». Con la lista del explorador, el
    /// 2026-09-14 Ü no pudo responder «No guardar» al Bloc de notas —«contiene «guardar»»— y dejó el
    /// documento colgado; el dueño: «que pueda guardar o decidir no guardar cosas libremente».
    ///
    /// GUARDAR NO ESTÁ AQUÍ porque guardar es lo que la persona pidió, y no guardar es una decisión
    /// suya igual de legítima. Se quedan las cuatro familias que no se deshacen: destruir, apagar o
    /// cerrar la sesión, sacar los datos de la máquina, y comprometer a la persona con un tercero.
    /// </remarks>
    private static readonly string[] Destructivo =
    {
        // destruir
        "eliminar", "borrar", "delete", "vaciar", "empty", "desinstalar", "uninstall",
        "formatear", "format", "destruir", "sobrescribir", "overwrite",
        // energía y sesión: se lleva por delante lo que la persona tenía abierto
        "apagar", "shut down", "reiniciar", "restart", "cerrar sesion", "sign out", "log out",
        // sacar los datos fuera
        "compartir", "share", "enviar", "send", "publicar", "publish", "subir", "upload",
        // comprometer a la persona
        "aceptar", "accept", "confirmar", "confirm", "permitir", "allow", "instalar", "install",
        "comprar", "buy", "pagar", "pay", "suscribir", "subscribe",
    };

    /// <summary>
    /// Palabras que INVIERTEN el verbo que viene detrás. «No eliminar» no elimina: es justo la opción
    /// que salva el archivo, y hasta el 2026-09-14 estaba vetada por contener el verbo.
    /// </summary>
    /// <remarks>
    /// Se miran las DOS palabras anteriores y no solo una, porque <see cref="Normalizar"/> convierte el
    /// apóstrofo en espacio: «Don't delete» llega como «don t delete», y mirando una sola palabra atrás
    /// se vería «t» y no la negación.
    /// </remarks>
    private static readonly string[] Negaciones =
        { "no", "not", "don", "dont", "never", "nunca", "jamas", "sin" };

    /// <summary>
    /// ¿La etiqueta nombra algo DESTRUCTIVO? Solo mira el verbo, no el tipo de control.
    ///
    /// Hace falta aparte de <see cref="Auto"/> porque aquel responde otra pregunta —«¿puede el
    /// explorador autónomo pulsar esto mientras mapea?»— y por eso rechaza todos los botones: al
    /// mapear, un botón nunca es navegación. Usarlo como veto al responder un diálogo bloqueaba
    /// incluso pulsar «No», que es justo lo que salvaba el archivo (2026-08-03). Responder a un
    /// diálogo es legítimo; lo que no lo es, es responder «Eliminar».
    /// </summary>
    public static bool EsDestructivo(string label, out string motivo)
    {
        motivo = "";
        string norm = Normalizar(label ?? "");
        foreach (string v in Destructivo)
        {
            if (MencionaSinNegar(norm, Normalizar(v)))
            {
                motivo = $"«{label}» contiene «{v}»";
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// ¿Aparece el verbo como palabra Y sin que lo nieguen justo antes? (promesa 239). Aparte de
    /// <see cref="ContienePalabra"/>, que sigue sirviendo al explorador autónomo tal cual: allí la
    /// pregunta es si la etiqueta MENCIONA algo que opera, y «No eliminar» tampoco es navegación.
    /// </summary>
    private static bool MencionaSinNegar(string texto, string palabra)
    {
        string[] busca = palabra.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string[] hay = texto.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (busca.Length == 0) return false;
        for (int i = 0; i + busca.Length <= hay.Length; i++)
        {
            bool casa = true;
            for (int j = 0; j < busca.Length; j++)
                if (!hay[i + j].Equals(busca[j], StringComparison.Ordinal)) { casa = false; break; }
            if (!casa) continue;

            bool negado = false;
            for (int k = Math.Max(0, i - 2); k < i; k++)
                if (Array.IndexOf(Negaciones, hay[k]) >= 0) negado = true;
            if (!negado) return true;   // hay una mención SIN negar: esa manda
        }
        return false;
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
