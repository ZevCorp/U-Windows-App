using System;
using System.Globalization;

namespace U.WindowsClient.Decision;

/// <summary>
/// QUIÉN DECIDE QUÉ PUERTA SE TOMA, y cómo se cambia sin recompilar. Promesas 275, 276 y 277 (spec 035).
/// </summary>
/// <remarks>
/// LUNA POR DEFECTO, Y NO ES TIMIDEZ. Esta pieza vive en el camino que usa un hospital: el asistente
/// opera SAP GUI IS-H con pacientes de verdad delante. Lo que no se ha medido sobre ese terreno no
/// manda sobre él, así que un entorno sin configurar se comporta EXACTAMENTE como el día antes de
/// que esto existiera — y encender Jev es un acto explícito de alguien que sabe lo que hace.
///
/// POR QUÉ HAY INTERRUPTOR Y NO UN CAMBIO LIMPIO. Jev no puede sustituir a Luna: no genera texto, no
/// llama herramientas y no mantiene conversación (documentación de TypeSafe leída el 2026-09-17).
/// Luna oye, habla y elige; de esas tres, Jev solo puede hacer la tercera. El interruptor cambia
/// quién elige la puerta, y nada más.
///
/// UN VALOR QUE NO SE ENTIENDE CAE EN LUNA. Alguien escribirá «jeff» —el modelo se llama Jev— o
/// «typesafe», y la única salida que no cambia el comportamiento de una máquina en producción es
/// seguir como hasta ahora, diciendo en voz alta qué se leyó para que se pueda corregir.
/// </remarks>
public sealed class ConfiguracionDelDecisor
{
    /// <summary>Nombre de la variable que enciende o apaga el decisor.</summary>
    public const string Interruptor = "U_DECISOR";

    /// <summary>El umbral por debajo del cual no se actúa. TypeSafe no publica uno recomendado.</summary>
    public const double ConfianzaPorDefecto = 0.70;

    /// <summary>
    /// Cuánto se espera a TypeSafe antes de devolverle el paso a Luna.
    ///
    /// SON 2.000 ms POR UNA MEDIDA, no por gusto: <c>map_where_am_i</c> costaba 2.771 ms de mediana
    /// (spec 025, medido el 2026-09-15) y eso ya se considera inaceptable en este repo. Un decisor
    /// que tardara más estaría empeorando justo lo que se viene arreglando.
    /// </summary>
    public const int TiempoMaximoPorDefectoMs = 2000;

    /// <summary>El alias de TypeSafe. Hoy apunta a <c>jev-1.13.0</c>.</summary>
    public const string ModeloPorDefecto = "jev-latest";

    /// <summary>«luna», «jev» o «simulado».</summary>
    public string Quien { get; }

    /// <summary>Por qué se quedó así. Describe lo que se leyó; nunca concluye (patrón nº2).</summary>
    public string Porque { get; }

    /// <summary>Mínimo de confianza exigido para actuar.</summary>
    public double Confianza { get; }

    /// <summary>Plazo máximo de la llamada, en milisegundos.</summary>
    public int TiempoMaximoMs { get; }

    /// <summary>El modelo que se le pide a TypeSafe.</summary>
    public string Modelo { get; }

    private ConfiguracionDelDecisor(string quien, string porque, double confianza, int tiempoMaximoMs, string modelo)
    {
        Quien = quien;
        Porque = porque;
        Confianza = confianza;
        TiempoMaximoMs = tiempoMaximoMs;
        Modelo = modelo;
    }

    /// <summary>
    /// Lee el entorno y decide quién decide.
    /// </summary>
    /// <param name="entorno">
    /// De dónde salen las variables. Se pasa, no se deduce: el contrato la necesita de mentira para
    /// poder juzgar los cuatro casos sin ensuciar el entorno de la máquina que lo corre.
    /// </param>
    public static ConfiguracionDelDecisor Leer(Func<string, string?> entorno)
    {
        if (entorno == null) throw new ArgumentNullException(nameof(entorno));

        double confianza = Numero(entorno(Interruptor + "_CONFIANZA") ?? entorno("U_TYPESAFE_CONFIANZA"), ConfianzaPorDefecto);
        if (confianza <= 0 || confianza > 1) confianza = ConfianzaPorDefecto;
        int plazo = Entero(entorno("U_TYPESAFE_TIMEOUT_MS"), TiempoMaximoPorDefectoMs);
        if (plazo < 100) plazo = TiempoMaximoPorDefectoMs;
        string modelo = Texto(entorno("U_TYPESAFE_MODELO")) ?? ModeloPorDefecto;

        ConfiguracionDelDecisor Con(string quien, string porque) =>
            new ConfiguracionDelDecisor(quien, porque, confianza, plazo, modelo);

        string? pedido = Texto(entorno(Interruptor));
        if (pedido == null)
            return Con("luna", $"sin {Interruptor}: decide Luna, como hasta hoy.");

        switch (pedido.ToLowerInvariant())
        {
            case "luna":
                return Con("luna", $"{Interruptor}=luna: decide Luna.");

            case "simulado":
                // NO PIDE CLAVE A PROPÓSITO: el modo simulado existe justamente para poder probar
                // sin credenciales ni red.
                return Con("simulado", $"{Interruptor}=simulado: se decide con la regla fija, sin llamar a TypeSafe.");

            case "jev":
                // VACÍO NO ES AUSENTE (patrón nº9): una variable puesta a cadena vacía —que es lo que
                // deja un `set TYPESAFE_API_KEY=` en un .bat— no es una clave.
                string? clave = Texto(entorno(PeticionASystemOne.VariableDeLaClave));
                if (clave == null)
                    return Con("luna",
                        $"{Interruptor}=jev pero {PeticionASystemOne.VariableDeLaClave} está vacía o no existe: "
                      + "se decide con Luna. No se llama a TypeSafe sin credencial, porque eso serían 401 "
                      + "gastando el cupo de peticiones por minuto para no decidir nada.");
                return Con("jev", $"{Interruptor}=jev con clave presente: elige Jev ({modelo}).");

            default:
                return Con("luna",
                    $"{Interruptor}=«{pedido}» no se entiende (se esperaba luna, jev o simulado): se decide con Luna. "
                  + "El modelo de TypeSafe se llama Jev.");
        }
    }

    /// <summary>Lee el entorno de verdad. El atajo para producción.</summary>
    /// <remarks>
    /// LA CLAVE DE TYPESAFE PUEDE VENIR DEL BACKEND (promesa 300): una copia distribuida no la lleva
    /// dentro del .exe. <c>DeLaApp</c> mira primero el entorno —así la máquina de quien desarrolla se
    /// comporta igual que siempre— y solo después lo que Graph haya dado. Para las demás variables
    /// (<c>U_DECISOR</c> y sus ajustes) no hay nada en el backend y devuelve el entorno tal cual.
    /// </remarks>
    public static ConfiguracionDelDecisor DelSistema() =>
        Leer(Credenciales.ClavesDelBackend.DeLaApp);

    private static string? Texto(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private static double Numero(string? v, double porDefecto) =>
        double.TryParse(Texto(v), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : porDefecto;

    private static int Entero(string? v, int porDefecto) =>
        int.TryParse(Texto(v), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : porDefecto;
}
