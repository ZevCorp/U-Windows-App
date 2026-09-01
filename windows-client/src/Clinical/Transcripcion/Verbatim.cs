using System.Text;

namespace U.WindowsClient.Clinical.Transcripcion;

/// <summary>
/// TODO LO QUE SE DIJO, tal como se dijo. Es lo que se manda a
/// <c>POST /api/clinical/encounters/:id/transcript</c>.
/// </summary>
/// <remarks>
/// LA FRASE SIN CERRAR CUENTA, y esa es la promesa 88.
///
/// Los proveedores marcan el fin de frase con un token de control —<c>&lt;end&gt;</c> en Soniox,
/// <c>speech_final</c> en Deepgram—, así que un acumulador que solo guarde lo CERRADO pierde
/// exactamente lo último que se dijo: el médico deja de hablar, pulsa parar, y la frase en la que
/// estaba —que suele ser la conclusión— se queda dentro. Es el patrón nº10 (*un paso no ejecutado
/// deja rastro*) aplicado al texto: no fallaría nada, no habría error, y el backend organizaría una
/// nota a la que le falta el final.
///
/// Por eso <see cref="Todo"/> concatena lo cerrado MÁS lo que va en curso, y no hay ninguna forma de
/// pedir «solo lo cerrado»: si la hubiera, alguien la usaría por descuido.
///
/// NO ES SEGURO PARA VARIOS HILOS a propósito: lo alimenta el único hilo que lee el socket. Meter un
/// candado aquí sugeriría que se puede escribir desde donde sea, y no se puede.
/// </remarks>
public sealed class Verbatim
{
    private readonly StringBuilder _cerrado = new();
    private readonly StringBuilder _enCurso = new();

    /// <summary>Texto confirmado por el proveedor. Lo provisional NO entra aquí.</summary>
    public void Confirmar(string texto)
    {
        if (string.IsNullOrEmpty(texto)) return;
        _enCurso.Append(texto);
    }

    /// <summary>
    /// El proveedor cerró la frase. Devuelve la frase cerrada (vacía si no había nada), que es lo
    /// que se entrega a quien la esté esperando en vivo.
    /// </summary>
    public string CerrarFrase()
    {
        string frase = _enCurso.ToString().Trim();
        _enCurso.Clear();
        if (frase.Length == 0) return "";

        if (_cerrado.Length > 0) _cerrado.Append(' ');
        _cerrado.Append(frase);
        return frase;
    }

    /// <summary>Todo lo dicho, incluida la frase que quedó a medias. Es lo que viaja al backend.</summary>
    public string Todo
    {
        get
        {
            string cola = _enCurso.ToString().Trim();
            if (cola.Length == 0) return _cerrado.ToString();
            return _cerrado.Length == 0 ? cola : _cerrado + " " + cola;
        }
    }

    /// <summary>
    /// No se oyó nada. Se pregunta antes de llamar a <c>/transcript</c>, que con texto vacío
    /// contesta <c>400 TRANSCRIPT_REQUIRED</c> — un error evitable que además tapa el de verdad
    /// (el micrófono mudo).
    /// </summary>
    public bool Vacio => Todo.Length == 0;

    /// <summary>Cuántos caracteres se llevan. Para el log, que no puede llevar el texto.</summary>
    public int Largo => Todo.Length;

    public void Limpiar()
    {
        _cerrado.Clear();
        _enCurso.Clear();
    }
}
