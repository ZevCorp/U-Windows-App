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

    /// <summary>Si se puede entregar ese recuerdo ahora mismo.</summary>
    public bool PuedeContar(int cual)
    {
        lock (_llave)
        {
            if (_ultimoContado == 0) return true;      // el primero de la tanda
            if (cual <= _ultimoContado) return true;   // repetir o volver atrás no adelanta nada
            return _habloDesdeEntonces;                // avanzar, solo tras haber hablado
        }
    }
}
