namespace U.WindowsClient.Navigation;

/// <summary>
/// SEÑALAR: qué es esto que estoy apuntando, y ¿puedes hacer algo con ello?
/// </summary>
/// <remarks>
/// LA CAPACIDAD MÁS USADA, y nadie la diseñó como tal. En 26 días de uso real la voz pidió
/// <c>map_pointing_at</c> 168 veces —más que pulsar, más que ir, más que nada— y la cuarta más
/// pedida fue «¿qué me acabas de mostrar?» (90). Dos de las cinco primeras son apuntar: la forma
/// natural de usar esto resultó ser señalar con el ratón mientras se habla, y salió en los datos
/// antes que en ninguna decisión (2026-08-22).
///
/// PARA QUÉ SE SEÑALA DE VERDAD, dicho por quien lo usa: «enseñarle cosas al asistente, o que el
/// asistente me muestre cosas» (2026-08-22). Son los dos sentidos del mismo gesto —yo apunto y le
/// digo qué es; él apunta y me pregunta «¿este o este?»— y ninguno de los dos tiene que ver con
/// mover nada de sitio.
///
/// QUÉ SE QUEDA FUERA, Y POR QUÉ IMPORTA. La versión anterior contestaba «señalas «Guardar» ·
/// nivel 3 · fijado a mano» y terminaba con «Para moverlo de nivel: map_set_level con exit=…».
/// Es decir: la herramienta más usada del sistema estaba ANUNCIANDO otra en cada respuesta. Esos
/// 63 usos de map_set_level no son una necesidad que apareciera sola — son una sugerencia que se
/// repitió 168 veces. Evidencia inducida, no independiente.
///
/// LO QUE SÍ HACE FALTA PARA ENSEÑAR ES LA IDENTIDAD. Recordar «Guardar» no sirve: mañana habrá
/// tres cosas que se llaman así. Lo que se puede volver a encontrar es el SELECTOR, así que cuando
/// el núcleo conoce lo señalado se devuelve — es lo único con lo que después se puede decir «eso
/// que me enseñaste» y acertar. Guardar la enseñanza es otra capacidad y todavía no existe; esta
/// pone el cimiento de poder nombrarla sin ambigüedad.
///
/// LEER LA PANTALLA NO ES ASUNTO DE ESTA CLASE. Averiguar qué hay bajo el cursor —el punto, la
/// puerta más pequeña que lo contiene, subir por el árbol si no tiene nombre— es trabajo de UIA y
/// vive donde vive. Aquí entra ya resuelto, y por eso esto se puede juzgar sin una pantalla
/// delante: es lo que permite que las promesas 32-34 corran en un segundo y en cualquier máquina.
/// </remarks>
public sealed class LoQueSenalas
{
    /// <summary>Lo que hay bajo el cursor, ya resuelto por quien sabe leer la pantalla.</summary>
    public readonly record struct Senalado(string Nombre, string Tipo, bool SePudoIluminar);

    /// <summary>Un candidato a «lo que estás señalando»: algo con nombre y con caja.</summary>
    public readonly record struct Candidato(string Nombre, string Tipo, System.Windows.Rect Caja);

    /// <summary>
    /// CUÁL DE TODOS ES EL QUE SEÑALAS: el más pequeño que contiene el punto.
    /// </summary>
    /// <remarks>
    /// Bajo un mismo píxel hay siempre varias cosas —la ventana, el panel, la fila, el botón, el
    /// texto del botón— y todas «contienen» el cursor. La que una persona diría que está señalando
    /// es SIEMPRE la más pequeña con nombre: es la más específica.
    ///
    /// Se mira ARRIBA Y ABAJO, y eso es el arreglo. Antes solo se subía por el árbol buscando un
    /// nombre, y en la barra de tareas de Windows 11 eso no encuentra nada: `FromPoint` cae en un
    /// `Pane` sin nombre cuyo padre tampoco lo tiene, porque la barra es XAML. Medido el 2026-08-22:
    /// ese Pane tiene 29 descendientes y dos contienen el punto —«Aplicaciones en ejecución» (Pane,
    /// 26.400 px²) y «Vista de tareas» (Button, 3.300 px²)—. El pequeño es el que el usuario
    /// señalaba, y estaba justo debajo.
    ///
    /// El empate se rompe por área y no por profundidad en el árbol: dos elementos anidados pueden
    /// tener la misma caja —un botón y su texto— y entonces da igual cuál se coja; lo que nunca da
    /// igual es coger el panel entero.
    /// </remarks>
    public static Candidato? Elegir(IEnumerable<Candidato> candidatos, System.Windows.Point punto)
        => candidatos
            .Where(c => c.Nombre.Trim().Length > 0 && !c.Caja.IsEmpty && c.Caja.Contains(punto))
            .OrderBy(c => c.Caja.Width * c.Caja.Height)
            .Select(c => (Candidato?)c)
            .FirstOrDefault();

    private readonly Nucleo.Grafo _grafo;
    private readonly Func<string> _donde;

    public LoQueSenalas(Nucleo.Grafo grafo, Func<string> donde)
    {
        _grafo = grafo;
        _donde = donde;
    }

    /// <summary>
    /// CUÁNTO VALE UN SEÑALAMIENTO. Pasado esto, «púlsalo» ya no puede referirse a ello.
    /// </summary>
    /// <remarks>
    /// Señalar hace que lo apuntado sea accionable aunque no esté en el mapa de esta pantalla —así
    /// funciona «¿ves este icono? ábrelo»—. Pero eso abre un riesgo: si lo señalado no caduca, un
    /// «púlsalo» dicho diez minutos después actúa sobre algo que ya no está delante, y encima con
    /// la confianza de haber acertado. Es el mismo fallo que el freno resolvió con las tareas: un
    /// gesto viejo no puede decidir lo que se pida ahora (promesa 22).
    ///
    /// Un minuto es lo que dura una frase con su respuesta: se señala, se pregunta, se contesta y
    /// se pide. Más allá, quien habla ya está en otra cosa.
    /// </remarks>
    public static readonly TimeSpan LoSenaladoCaduca = TimeSpan.FromMinutes(1);

    /// <summary>¿Sigue valiendo lo que se señaló entonces?</summary>
    public static bool SigueValiendo(DateTime cuandoSeSenalo, DateTime ahora)
        => ahora - cuandoSeSenalo <= LoSenaladoCaduca;
    /// <summary>
    /// ¿SE REFIERE A LO QUE SEÑALASTE? Sin exigir que lo diga clavado.
    /// </summary>
    /// <remarks>
    /// Windows llama a las cosas como le da la gana: el icono de la barra es «Copilot anclado» y
    /// una ventana abierta es «Claude- 2 ventanas de ejecución». Nadie dice eso. Se dice «Copilot».
    ///
    /// Exigir la igualdad exacta hacía que «¿ves este icono? ábrelo» fallara SIEMPRE en cuanto el
    /// nombre real llevara una palabra de más — que es casi siempre (2026-08-23, probado por el
    /// usuario). Basta con que uno contenga al otro: quien señala y quien habla están mirando lo
    /// mismo, y el riesgo de confusión ya lo acota que caduque en un minuto.
    /// </remarks>
    public static bool SeRefiereA(string pedido, string loSenalado)
        => Nombres.HablanDeLoMismo(pedido, loSenalado);

    /// <summary>Cuando bajo el cursor no hay nada con nombre. Se pide mover, no se adivina.</summary>
    public const string NadaDebajo =
        "bajo el cursor no hay nada con nombre. Muévelo un poco y vuelve a preguntar.";

    /// <summary>Qué contestar sobre lo que hay bajo el cursor. Null = no había nada con nombre.</summary>
    public string Con(Senalado? visto)
    {
        if (visto is not { } s || s.Nombre.Length == 0) return NadaDebajo;

        string aqui = _donde() ?? "";
        var alcanzable = aqui.Length == 0 ? null
            : _grafo.DesdeAqui(aqui).FirstOrDefault(a =>
                a.Que.Etiqueta.Equals(s.Nombre, StringComparison.OrdinalIgnoreCase));

        // LAS TRES RESPUESTAS SON DISTINTAS Y HAY QUE DISTINGUIRLAS. «Puedo pulsarlo» invita a
        // pedirlo; «lo recuerdo pero no lo veo» avisa de que hay que llegar antes; «no lo conozco»
        // dice que hay que mirarlo primero. Fundirlas en un «no puedo» las tres haría que quien
        // pregunta se rinda en los dos casos en los que sí había salida.
        string puedo = alcanzable is null ? "no lo tengo en el mapa de esta pantalla"
                     : alcanzable.Vivo       ? "puedo pulsarlo ahora"
                                             : "lo recuerdo aquí, pero ahora mismo no lo veo";

        return $"señalas «{s.Nombre}» ({s.Tipo}) · {puedo}"
             + (s.SePudoIluminar ? " · lo estoy iluminando" : " · no he podido iluminarlo");
    }
}
