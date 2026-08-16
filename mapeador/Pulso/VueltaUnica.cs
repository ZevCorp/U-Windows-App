namespace Mapeador;

/// <summary>
/// UNA VUELTA A LA VEZ. Deja pasar a la primera y descarta —no encola— a la que llegue mientras la
/// anterior sigue corriendo.
/// </summary>
/// <remarks>
/// LO QUE SE PROTEGE NO ES UNA VELOCIDAD, ES UN INVARIANTE: nada se apila. Un umbral de tiempo
/// —«menos de 500 ms»— sería una mala promesa: falla en una máquina cargada, con una página enorme
/// delante, o un martes. Un juez que da rojos por motivos ajenos al código enseña a desconfiar del
/// juez, y eso ya nos costó caro.
///
/// EL FALLO QUE EXISTE PARA IMPEDIR: un `Timer` de .NET dispara la siguiente vuelta aunque la
/// anterior no haya vuelto. El 2026-08-12 el intervalo bajó a 120 ms para algo que costaba 200, las
/// llamadas se encolaron, la cola creció sola, y leer la Maqueta pasó de 0,4 s a 2,2 —cinco veces
/// más lento sin que nadie tocara el código de leer—.
///
/// SE DESCARTA, NO SE ENCOLA, y esa es la decisión de fondo. Si el estado volvió a cambiar, el
/// siguiente latido lo recogerá igual; una cola solo serviría para pintar con retraso una foto que
/// ya caducó. Descartar es la respuesta correcta a «llego tarde», no una pérdida.
///
/// ESTO ERA DOS `Interlocked` SUELTOS dentro de una clase de WPF. Ahí el invariante no tenía nombre
/// —no se podía nombrar en una promesa— y no se podía probar sin levantar la app entera. Aquí es
/// una pieza pura que el contrato del mapeador juzga en milisegundos.
/// </remarks>
public sealed class VueltaUnica(string nombre)
{
    private int _dentro;
    private int _descartadas;

    /// <summary>Para qué es esta vuelta. Solo sirve para poder decir cuál se saturó.</summary>
    public string Nombre { get; } = nombre;

    /// <summary>Cuántas llegaron con otra en curso y se tiraron, desde siempre.</summary>
    public int Descartadas => _descartadas;

    /// <summary>
    /// ¿Me toca? `true` si no había nadie dentro —y entonces QUEDA OBLIGADO a llamar a
    /// <see cref="Termine"/>, siempre, desde un `finally`—. `false` si esta vuelta llegó tarde y se
    /// descarta; en ese caso no hay nada que soltar.
    /// </summary>
    public bool MeToca()
    {
        if (Interlocked.Exchange(ref _dentro, 1) == 0) return true;
        Interlocked.Increment(ref _descartadas);
        return false;
    }

    /// <summary>Se acabó: la siguiente ya puede entrar. Va en un `finally` o no va.</summary>
    public void Termine() => Interlocked.Exchange(ref _dentro, 0);

    /// <summary>
    /// La forma sin trampa: corre el trabajo si toca y suelta solo, pase lo que pase. Devuelve si
    /// llegó a correr. Se prefiere a <see cref="MeToca"/> siempre que el trabajo quepa en una
    /// llamada — olvidarse de <see cref="Termine"/> deja la vuelta cerrada PARA SIEMPRE, y eso no
    /// se ve como un fallo sino como «se quedó parado».
    /// </summary>
    public bool Corre(Action trabajo)
    {
        if (!MeToca()) return false;
        try { trabajo(); return true; }
        finally { Termine(); }
    }
}
