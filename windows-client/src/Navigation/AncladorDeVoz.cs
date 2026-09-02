namespace U.WindowsClient.Navigation;

/// <summary>Una frase transcrita con la hora en que se dijo (ms del mismo reloj que los pasos).</summary>
public sealed record FraseDicha(string Texto, long HoraMs);

/// <summary>Las frases repartidas: cada paso con lo que sonaba, y el resto como contexto.</summary>
public sealed record VozAnclada(IReadOnlyList<string> DichoPorPaso, string Contexto);

/// <summary>
/// EL ANCLADOR: qué frase estaba sonando con qué paso. Promesa 105 (spec 005).
/// </summary>
/// <remarks>
/// «Aquí va el NIT» solo sirve colgado del campo que sonaba; suelto es una frase perdida en un log
/// — que es exactamente donde vivía hasta hoy (ConversacionEnVivo:1203, medido el 2026-09-01).
///
/// La regla es POR CERCANÍA EN EL TIEMPO, con una ventana: una frase pertenece al paso más cercano
/// si le queda a menos de la ventana; si ningún paso le queda cerca, va al CONTEXTO general de la
/// skill — inventarle un ancla sería colgar «aquí va el NIT» de un botón cualquiera, y una frase
/// mal colgada es peor que una suelta (la caja que miente del aprendizaje nº4, en versión de voz).
///
/// La ventana son 15 s a cada lado: el humano narra ANTES o DESPUÉS de tocar («ahora voy a…» /
/// «eso que hice fue…»), nunca exactamente durante, y las demos reales respiran. No es un número
/// sagrado: si el nivel 4 enseña otra cosa, se cambia AQUÍ y el contrato sigue juzgando la regla.
/// </remarks>
public static class AncladorDeVoz
{
    public const long VentanaMs = 15_000;

    public static VozAnclada Ancla(IReadOnlyList<FraseDicha> frases, IReadOnlyList<long> horaDeCadaPasoMs)
    {
        var porPaso = new string[horaDeCadaPasoMs.Count];
        Array.Fill(porPaso, "");
        var contexto = new List<string>();

        foreach (var f in frases)
        {
            int mejor = -1;
            long mejorDist = long.MaxValue;
            for (int i = 0; i < horaDeCadaPasoMs.Count; i++)
            {
                long d = Math.Abs(f.HoraMs - horaDeCadaPasoMs[i]);
                if (d < mejorDist) { mejorDist = d; mejor = i; }
            }

            if (mejor >= 0 && mejorDist <= VentanaMs)
                porPaso[mejor] = porPaso[mejor].Length == 0 ? f.Texto : porPaso[mejor] + " " + f.Texto;
            else if (f.Texto.Length > 0)
                contexto.Add(f.Texto);
        }

        return new(porPaso, string.Join(" ", contexto));
    }
}
