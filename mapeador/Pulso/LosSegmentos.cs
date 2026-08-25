namespace Mapeador;

/// <summary>Un trozo de grabación: cuándo empezó y dónde está el archivo.</summary>
/// <param name="Empezo">
/// EN UTC, y es la mitad de toda la correlación con el grafo: el segundo dentro de este archivo es
/// la hora del error menos esta. La otra mitad es que el error también guarde su hora en UTC.
/// </param>
public sealed record Segmento(DateTime Empezo, string Archivo)
{
    public DateTime Termina => Empezo + LosSegmentos.Dura;

    /// <summary>Si este trozo pisa esa franja de tiempo, aunque sea un instante.</summary>
    public bool Toca(DateTime desde, DateTime hasta) => Empezo < hasta && Termina > desde;
}

/// <summary>
/// QUÉ TROZOS DE GRABACIÓN HACEN FALTA Y CUÁLES SE PUEDEN BORRAR.
/// </summary>
/// <remarks>
/// SEGMENTOS EN VEZ DE RECORTES, y es la decisión que hace barata toda la fase. Recortar «20 s antes
/// y 20 s después» de un mp4 pide ffmpeg, y este proyecto lo rechazó a propósito: son ~50 MB más en
/// el instalador y una forma nueva de que la app no arranque en una máquina limpia
/// (ScreenRecorder.cs:9). Grabando ya troceado, un clip deja de ser un recorte y pasa a ser «los
/// trozos que tocan esta franja»: se copian tal cual, sin recodificar y sin herramientas de fuera.
///
/// El precio es que un clip trae hasta un trozo de más por cada extremo. Para revisar un fallo eso
/// no estorba — ver un poco de contexto de más suele ayudar.
///
/// LO LIMPIO SE BORRA, y es lo único que hace que grabar un turno entero no llene el disco. Pero
/// «limpio» no basta para borrar: ver <see cref="SePuedeBorrar"/>, que es donde está la única regla
/// no evidente de todo esto.
///
/// PURO Y SIN DISCO: se le pasan los trozos y los errores, y contesta. Así la regla de borrado se
/// puede juzgar sin grabar nada — que es justo lo que hay que poder hacer con una regla que, si se
/// equivoca, borra la prueba del fallo que veníamos a buscar.
/// </remarks>
public static class LosSegmentos
{
    /// <summary>
    /// Cuánto dura cada trozo. Igual al margen a propósito: así un clip son como mucho tres trozos
    /// —el del error y uno a cada lado— y la cuenta es fácil de seguir cuando algo salga raro.
    /// </summary>
    public static readonly TimeSpan Dura = TimeSpan.FromSeconds(20);

    /// <summary>Cuánto se guarda antes y después de un error, tal como se pidió.</summary>
    public static readonly TimeSpan Margen = TimeSpan.FromSeconds(20);

    /// <summary>
    /// LAS FRANJAS QUE HAY QUE GUARDAR, con los errores cercanos ya fundidos en una sola.
    /// </summary>
    /// <remarks>
    /// Fundir no es un detalle de eficiencia: si tres errores caen en diez segundos, tres clips
    /// solapados cuentan la misma historia tres veces y ninguno la cuenta entera. Uno solo, del
    /// primero menos el margen al último más el margen, es lo que se pidió y además es lo que se
    /// entiende al mirarlo.
    /// </remarks>
    public static IReadOnlyList<(DateTime Desde, DateTime Hasta)> FranjasQueGuardar(
        IEnumerable<DateTime> errores)
    {
        var ordenados = errores.OrderBy(e => e).ToList();
        var franjas = new List<(DateTime Desde, DateTime Hasta)>();

        foreach (var e in ordenados)
        {
            var desde = e - Margen;
            var hasta = e + Margen;

            // Se funde con la anterior si se tocan. Se compara contra el FIN de la anterior y no
            // contra su error, porque una ristra larga de errores encadenados tiene que salir como
            // una sola franja y no como parejas sueltas.
            if (franjas.Count > 0 && desde <= franjas[^1].Hasta)
                franjas[^1] = (franjas[^1].Desde, hasta > franjas[^1].Hasta ? hasta : franjas[^1].Hasta);
            else
                franjas.Add((desde, hasta));
        }

        return franjas;
    }

    /// <summary>Los trozos que hacen falta para contar esa franja, en orden.</summary>
    public static IReadOnlyList<Segmento> LosQueCubren(
        IEnumerable<Segmento> trozos, DateTime desde, DateTime hasta)
        => trozos.Where(s => s.Toca(desde, hasta)).OrderBy(s => s.Empezo).ToList();

    /// <summary>
    /// ¿SE PUEDE BORRAR YA ESTE TROZO? Aquí está la única regla no evidente de la fase.
    /// </summary>
    /// <remarks>
    /// Lo tentador es borrar todo lo que no tenga un error dentro. Y estaría MAL, porque el clip de
    /// un error lleva veinte segundos de ANTES: un trozo limpio que acaba de pasar es exactamente el
    /// «antes» del error que puede ocurrir dentro de un instante. Borrarlo en cuanto se ve limpio
    /// deja todos los clips empezando de golpe, justo en el error y sin el contexto que explica cómo
    /// se llegó ahí — que es lo que se viene a mirar.
    ///
    /// Así que hacen falta las dos cosas: que ningún error conocido lo pise, Y que sea lo bastante
    /// viejo como para que ningún error futuro pueda pedirlo. Un error que ocurriera ahora mismo
    /// necesitaría desde <c>ahora − Margen</c>; un trozo que terminó antes de eso ya no lo puede
    /// alcanzar ninguno.
    /// </remarks>
    public static bool SePuedeBorrar(Segmento trozo, DateTime ahora, IEnumerable<DateTime> errores)
    {
        // Todavía puede ser el «antes» de un error que aún no ha pasado.
        if (trozo.Termina > ahora - Margen) return false;

        // O el «antes»/«después» de uno que ya conocemos.
        foreach (var (desde, hasta) in FranjasQueGuardar(errores))
            if (trozo.Toca(desde, hasta)) return false;

        return true;
    }
}
