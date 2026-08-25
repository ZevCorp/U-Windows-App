namespace U.WindowsClient.Navigation;

/// <summary>
/// ¿PUEDE PASAR YA AL SIGUIENTE RECUERDO? Lleva la cuenta de si habló entre uno y otro.
/// </summary>
/// <remarks>
/// EXISTE PORQUE PEDIRLO NO BASTÓ, otra vez. El catálogo decía «te dice recuerdo 1 de N, lo ilumina,
/// y tú lo CUENTAS EN VOZ; cuando termines, vuelve a llamarla con cual=2», y aun así el modelo
/// encadenaba las dos llamadas seguidas y narraba los dos al final. Medido el 2026-08-24 en tres
/// respuestas consecutivas del servidor:
///
///   20:10:39  respuesta A → map_recuerdos          · CERO audio
///   20:10:40  respuesta B → map_recuerdos cual=2   · CERO audio
///   20:10:43  respuesta C → la única voz, contando LOS DOS
///
/// El recuadro del primero vivía un segundo antes de saltar al segundo, y para cuando llegaba la
/// frase ya estaba marcado el otro. Eso rompe justo lo que hace útil señalar: que lo marcado sea
/// aquello de lo que se está hablando.
///
/// NO SE FUERZA A HABLAR —no se puede, y fingir un turno de voz sería peor—: se le niega el
/// siguiente hasta que hable. La negativa lleva dentro qué hacer, así que no es un callejón.
///
/// AVANZAR ES LO ÚNICO QUE SE VIGILA. Repetir el que ya se contó, o volver a uno anterior, se deja
/// pasar: no adelanta el recuadro, así que no puede desincronizar nada — y negarlo impediría lo más
/// natural del mundo, que es «espera, ¿cuál era el primero?».
/// </remarks>
public sealed class ElTurnoDeContar
{
    private readonly object _llave = new();
    private int _ultimoContado;
    private bool _habloDesdeEntonces;

    /// <summary>Se acaba de entregar el recuerdo <paramref name="cual"/>: toca hablar de él.</summary>
    public void SeConto(int cual)
    {
        lock (_llave)
        {
            if (cual > _ultimoContado) _ultimoContado = cual;
            _habloDesdeEntonces = false;
        }
    }

    /// <summary>Ü dijo algo. Es lo único que desbloquea el siguiente.</summary>
    public void Hablo()
    {
        lock (_llave) _habloDesdeEntonces = true;
    }

    /// <summary>Se empieza una tanda nueva: la cuenta vuelve a cero.</summary>
    public void Reiniciar()
    {
        lock (_llave) { _ultimoContado = 0; _habloDesdeEntonces = false; }
    }

    /// <summary>
    /// Alguien está corrigiendo un recuerdo a mano ahora mismo. Se inyecta desde la interfaz —es
    /// quien tiene las tarjetas— y para el avance mientras dure.
    /// </summary>
    /// <remarks>
    /// Pasar al siguiente con alguien a media frase le quitaría el foco y le borraría lo escrito.
    /// Y es un caso que se da solo: se está contando lo aprendido, la persona ve que una frase no
    /// es la que quería, y se pone a arreglarla justo mientras la voz sigue (2026-08-24, pedido por
    /// el usuario). Esperar aquí no cuesta nada; atropellarla cuesta su corrección.
    /// </remarks>
    public Func<bool>? EscribiendoAlguien { get; set; }

    /// <summary>
    /// Todavía se está OYENDO lo anterior. Lo contesta el altavoz —queda cola por sonar— y no el
    /// servidor.
    /// </summary>
    /// <remarks>
    /// SONAR NO ES RECIBIR, y es la misma confusión que ya costó un bug en 2026-08-06 (ver
    /// <see cref="U.WindowsClient.Voice.LiveAudio.NivelSalida"/>): el modelo manda el audio mucho
    /// más rápido de lo que se oye, así que cuando el servidor dice «terminé» quedan segundos de voz
    /// en la cola. Medido el 2026-08-24 contando recuerdos:
    ///
    ///   23:02:02  recuadro sobre el primero
    ///   23:02:05  el servidor termina de MANDAR su narración (~40 palabras, ~16 s de habla)
    ///   23:02:05  el recuadro salta al segundo
    ///
    /// Tres segundos de recuadro para dieciséis de voz: se oía bien el primero mientras se señalaba
    /// el segundo. El usuario lo describió exacto — «menciona bien el primer elemento, pero a
    /// destiempo con la señalización».
    /// </remarks>
    public Func<bool>? SigueSonando { get; set; }

    /// <summary>Si se puede entregar ese recuerdo ahora mismo.</summary>
    public bool PuedeContar(int cual)
    {
        // ESCRIBIR MANDA SOBRE TODO LO DEMÁS, incluso sobre repetir: si está corrigiendo la tarjeta
        // que tiene delante, volver a pintarla le tiraría lo escrito.
        if (EscribiendoAlguien?.Invoke() == true) return false;

        lock (_llave)
        {
            if (_ultimoContado == 0) return true;      // el primero de la tanda
            if (cual <= _ultimoContado) return true;   // repetir o volver atrás no adelanta nada
            if (!_habloDesdeEntonces) return false;    // avanzar exige haber hablado
        }

        // Y QUE SE HAYA ACABADO DE OÍR. Haber hablado no basta: la voz llega en un segundo y se oye
        // en dieciséis. Se pregunta FUERA del candado porque quien contesta es el altavoz.
        return SigueSonando?.Invoke() != true;
    }
}
