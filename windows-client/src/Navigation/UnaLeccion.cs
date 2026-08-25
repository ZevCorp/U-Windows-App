namespace U.WindowsClient.Navigation;

/// <summary>
/// ¿ESTO QUE ACABAN DE DECIR ERA UNA LECCIÓN? Mira la frase y contesta, sin pantalla ni micrófono.
/// </summary>
/// <remarks>
/// EXISTE PORQUE PEDÍRSELO AL MODELO NO BASTÓ. El catálogo ya decía «úsala cuando oigas recuerda,
/// toma nota, siempre…» y el prompt tenía un apartado entero sobre aprender. Se probó el 2026-08-24:
/// tres lecciones seguidas —«SIEMPRE hacemos clic aquí», «lo primero que haremos SIEMPRE será…»,
/// «SIEMPRE escribirás NWP1»— trece llamadas a herramientas, y map_esto_es CERO veces. El modelo
/// contestaba «lo tengo en mente» y pulsaba el botón en vez de aprenderlo.
///
/// Una instrucción en el prompt es una petición; esto es una garantía. No obliga a guardar —quien
/// decide sigue siendo el modelo, y forzarlo escribiría recuerdos que nadie pidió— pero SÍ obliga a
/// que la omisión se vea: si aquí se dijo «era una lección» y nadie guardó nada, sale un aviso en
/// pantalla y en el log. El usuario no puede saber si está aprendiendo cuando el fallo es silencioso;
/// ese era el problema real («no he visto ni la primera vez el log con el cerebro»).
///
/// SE EQUIVOCA HACIA EL AVISO, no hacia el silencio. Un falso positivo cuesta una línea de más que
/// se ignora; un falso negativo cuesta una lección perdida sin que nadie se entere — y esa asimetría
/// es la razón de que la lista sea generosa.
/// </remarks>
public static class UnaLeccion
{
    /// <summary>
    /// Lo que convierte una frase en lección. Dos familias, y las dos hacen falta:
    ///
    ///   · IMPERATIVOS DE MEMORIA («recuerda», «siempre», «no olvides»). Son los que más se escapan
    ///     porque no tienen la forma «esto es X», y son justo los que usa quien enseña un
    ///     procedimiento: «siempre hacemos clic aquí».
    ///   · DEFINICIONES («esto es», «aquí va», «sirve para»). La forma clásica de explicar qué es
    ///     algo, que ya estaba contemplada y aun así conviene vigilar.
    /// </summary>
    private static readonly string[] Marcas =
    {
        // imperativos de memoria
        "recuerda", "recuérdalo", "recuerdalo", "recuérdate", "ten en cuenta",
        "toma nota", "apunta esto", "apúntalo", "apuntalo", "no olvides", "no se te olvide",
        "de ahora en adelante", "a partir de ahora", "para la próxima", "para la proxima",
        "siempre", "nunca ", "cada vez que",
        // definiciones
        "esto es ", "este es ", "esta es ", "esto se llama", "se llama ",
        "aquí va", "aqui va", "aquí se ", "aqui se ", "acá se ", "aca se ",
        "sirve para", "se usa para", "se utiliza para", "es donde", "es el que", "es la que",
        // procedimiento enseñado
        "lo primero que", "primero hay que", "hay que verificar", "antes de ",
    };

    /// <summary>
    /// Frases que llevan una marca pero NO enseñan nada. Van aparte porque quitarlas de la lista de
    /// arriba se llevaría por delante casos buenos: «nunca» enseña en «nunca pulses eso» y no enseña
    /// en «nunca lo había visto».
    /// </summary>
    private static readonly string[] NoCuentan =
    {
        "siempre y cuando", "como siempre", "lo de siempre", "siempre lo mismo",
        "nunca lo había", "nunca lo habia", "no me acuerdo", "no recuerdo",
        "¿recuerdas", "recuerdas?", "te acuerdas",

        // PREGUNTAR POR LA MEMORIA NO ES ENSEÑAR, y hay que cubrirlo por la FORMA de preguntar y no
        // por una cadena concreta: «¿recuerdas» solo atrapa la pregunta que empieza por ahí, y en
        // cuanto se dice «¿QUÉ recuerdas» deja de pegar — el interrogativo se mete en medio. Saltó
        // de verdad con «Cuéntame qué sabes sobre esta pantalla, ¿qué recuerdas», que es justo la
        // frase con la que se estrena map_recuerdos: la marca «recuerda» encajaba dentro de
        // «recuerdas» y el aviso salía mientras el asistente contestaba bien (2026-08-24).
        "que recuerdas", "que sabes", "que te ense", "que me ense",
        "cuentame que", "dime que sabes", "recuerdas algo", "sabes algo", "recuerdas sobre",
    };

    /// <summary>
    /// Si esa frase suena a que están enseñando algo que debería quedarse.
    /// </summary>
    public static bool Parece(string frase)
    {
        if (string.IsNullOrWhiteSpace(frase)) return false;
        string f = Nombres.Aplanar(frase);
        if (f.Length < 8) return false;   // «sí», «ok», «dale» no enseñan nada

        foreach (string no in NoCuentan)
            if (f.Contains(Nombres.Aplanar(no), StringComparison.Ordinal)) return false;

        foreach (string m in Marcas)
            if (f.Contains(Nombres.Aplanar(m), StringComparison.Ordinal)) return true;

        return false;
    }
}
