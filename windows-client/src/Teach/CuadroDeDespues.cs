namespace U.WindowsClient.Teach;

/// <summary>
/// QUÉ CUADRO ES EL DE DESPUÉS DE UN CLIC. Promesa 169 (spec 013).
/// </summary>
/// <remarks>
/// EL DE DESPUÉS NO ES «EL SIGUIENTE»: es el primero en que la pantalla YA LLEGÓ. Entre el clic y la
/// pantalla nueva SAP hace un viaje al servidor —cientos de milisegundos, a veces segundos— y en
/// medio hay cuadros de transición: la pantalla vieja todavía, un reloj de arena, media lista
/// pintada. Enseñarle al modelo uno de esos como «lo que pasó al hacer clic» es enseñarle ruido.
///
/// «LLEGÓ» SE MIDE, NO SE SUPONE: dos cuadros seguidos con la misma huella. Es el mismo criterio
/// que usan los ojos: la pantalla dejó de moverse. Y se mide a partir de una ESPERA MÍNIMA, porque
/// la pantalla vieja también se repite consigo misma en los primeros milisegundos, antes de que
/// SAP haya contestado nada.
///
/// SI NO SE ASIENTA, SE DICE. Un techo, y pasado el techo se entrega el último cuadro de dentro
/// con <c>Asentado = false</c>. Callarse aquí —devolver el último como si fuera bueno— es el patrón
/// nº10 del repo: parecer que funcionó.
/// </remarks>
public static class CuadroDeDespues
{
    /// <summary>Lo elegido, y si de verdad se asentó o se entregó por agotar el techo.</summary>
    public readonly record struct Eleccion(Cuadro Cuadro, bool Asentado);

    /// <summary>Antes de esto no se mira: la pantalla vieja aún no ha tenido tiempo de irse.</summary>
    public const int EsperaMinimaMs = 400;

    /// <summary>Después de esto se deja de esperar y se dice que no se asentó.</summary>
    /// <remarks>Dos segundos: un viaje normal de SAP en QAS tarda 300–900 ms (medido en `⏱ TIEMPOS`).
    /// Un viaje que pasa de dos segundos es noticia, y la noticia es «asentado=false».</remarks>
    public const int TechoMs = 2000;

    /// <summary>
    /// El primer cuadro, pasado el clic más la espera, que se repite en el siguiente; si ninguno
    /// dentro del techo, el último de dentro con <c>Asentado=false</c>; si no hay ninguno, null.
    /// </summary>
    public static Eleccion? Elegir(IReadOnlyList<Cuadro> cuadros, long horaDelClicMs,
        int esperaMinimaMs = EsperaMinimaMs, int techoMs = TechoMs)
    {
        if (cuadros == null || cuadros.Count == 0) return null;

        long desde = horaDelClicMs + Math.Max(0, esperaMinimaMs);
        long hasta = horaDelClicMs + Math.Max(esperaMinimaMs, techoMs);

        var enOrden = cuadros.OrderBy(c => c.HoraMs).ToList();
        Cuadro? ultimoDentro = null;
        for (int i = 0; i < enOrden.Count; i++)
        {
            var c = enOrden[i];
            if (c.HoraMs < desde) continue;
            if (c.HoraMs > hasta) break;
            ultimoDentro = c;

            // ASENTADO = el siguiente es igual. El siguiente puede caer fuera del techo y aun así
            // confirmar a este: lo que se juzga es que ESTE ya era la pantalla final.
            if (i + 1 < enOrden.Count && enOrden[i + 1].Huella == c.Huella)
                return new Eleccion(c, true);
        }

        return ultimoDentro == null ? null : new Eleccion(ultimoDentro, false);
    }
}
