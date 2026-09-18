using System;
using System.Collections.Generic;
using System.Diagnostics;
using U.WindowsClient.Decision;

// LA SONDA DEL DECISOR (spec 035): el interruptor Luna/Jev, funcionando, sin SAP.
//
//   sonda-del-decisor            los cinco casos en seco, sin tocar la red
//   sonda-del-decisor --deverdad ademas llama a TypeSafe, SOLO si hay TYPESAFE_API_KEY
//
// POR QUE EXISTE. El aprendizaje n13 de este repo: preguntale a la API antes de creerle al codigo.
// El contrato ya juzga las nueve promesas sin red; lo que el contrato NO puede hacer —y esto si— es
// hablar con TypeSafe de verdad cuando haya clave, que es lo unico que dira si Jev elige bien sobre
// pantallas reales.
//
// SIN --deverdad NO SALE UN SOLO PAQUETE de esta maquina.

internal static class Programa
{
    // Las puertas del fallo real que cuenta CLAUDE.md: el puente consciente pulso «Buscar pacientes»
    // en vez de «Crear Triage Administrativo».
    private static readonly string[] Puertas =
        { "Crear Triage Administrativo", "Buscar pacientes", "Salir" };
    private const string Objetivo = "crear el triage administrativo del paciente";
    private const string Pantalla = "SAP/NWP1";

    private static int _llamadas;

    private static int Main(string[] args)
    {
        bool deVerdad = Array.Exists(args, a => a.Equals("--deverdad", StringComparison.OrdinalIgnoreCase));

        // Un transporte que ANOTA. Es lo unico que distingue «no se llamo» de «se llamo y se descarto».
        Func<string, string> contador = _ =>
        {
            _llamadas++;
            return Respuesta("Crear Triage Administrativo", 0.91);
        };

        Titulo("1. SIN CONFIGURAR — lo que hay hoy en la maquina del hospital");
        Caso(Entorno(null, null), contador);

        Titulo("2. U_DECISOR=jev PERO SIN CLAVE");
        Caso(Entorno("jev", null), contador);

        Titulo("3. U_DECISOR=simulado — la cadena entera, sin red");
        Caso(Entorno("simulado", null), contador);

        Titulo("4. U_DECISOR=jev CON CLAVE, transporte de mentira");
        Caso(Entorno("jev", "sk-de-mentira"), contador);

        Titulo("5. UNA PUERTA QUE NO ESTA EN PANTALLA — el pendiente n2 de CLAUDE.md");
        _llamadas = 0;
        Muestra(ElDecisor.Elegir("jev", Pantalla, Objetivo, Puertas, 0.7, _ => Respuesta("Grabar", 0.99)), _llamadas);

        Titulo("6. JEV DUDA — dos puertas casi iguales");
        _llamadas = 0;
        Muestra(ElDecisor.Elegir("jev", Pantalla, Objetivo, Puertas, 0.7, _ => Respuesta("Crear Triage Administrativo", 0.51)), _llamadas);

        Titulo("EL CUERPO QUE SE LE MANDARIA A TYPESAFE");
        string estado = PeticionASystemOne.EstadoDeLaPantalla(Pantalla, Objetivo, Puertas);
        string cuerpo = PeticionASystemOne.CuerpoDeEleccion(
            "jev-latest", estado, PeticionASystemOne.IdDeLaPuerta,
            PeticionASystemOne.InstruccionesDeLaPuerta(Objetivo), Puertas);
        Console.WriteLine(cuerpo);
        bool filtrada = cuerpo.Contains("Bearer") || cuerpo.Contains("TYPESAFE_API_KEY") || cuerpo.Contains("api_key");
        Color(filtrada ? ConsoleColor.Red : ConsoleColor.Green,
            filtrada ? "  !! LA CLAVE APARECE EN EL CUERPO" : "  la clave no aparece en el cuerpo: va en la cabecera.");

        if (!deVerdad) { Console.WriteLine(); Console.WriteLine("listo (en seco; con --deverdad y clave, se llama a TypeSafe)."); return 0; }

        string? clave = Environment.GetEnvironmentVariable(PeticionASystemOne.VariableDeLaClave);
        if (string.IsNullOrWhiteSpace(clave))
        {
            Titulo("--deverdad PEDIDO, PERO NO HAY TYPESAFE_API_KEY");
            Color(ConsoleColor.Red, "  no se llama a nadie. Pon la clave y vuelve a intentarlo.");
            return 2;
        }

        Titulo("7. LLAMADA REAL A TYPESAFE");
        var cfg = ConfiguracionDelDecisor.DelSistema();
        using var cliente = new ClienteTypeSafe(clave, Math.Max(cfg.TiempoMaximoMs, 5000), m => Console.WriteLine("    " + m));
        var reloj = Stopwatch.StartNew();
        var d = ElDecisor.Elegir("jev", Pantalla, Objetivo, Puertas, cfg.Confianza, cliente.Pregunta);
        reloj.Stop();
        Console.WriteLine($"    tardo        : {reloj.ElapsedMilliseconds} ms");
        Muestra(d, null);
        return d.Actuar ? 0 : 1;
    }

    private static Func<string, string?> Entorno(string? decisor, string? clave) => n => n switch
    {
        "U_DECISOR" => decisor,
        "TYPESAFE_API_KEY" => clave,
        _ => null,
    };

    private static void Caso(Func<string, string?> entorno, Func<string, string> transporte)
    {
        var cfg = ConfiguracionDelDecisor.Leer(entorno);
        Console.WriteLine($"    quien decide : {cfg.Quien}");
        Console.WriteLine($"    porque       : {cfg.Porque}");
        _llamadas = 0;
        Muestra(ElDecisor.Elegir(cfg.Quien, Pantalla, Objetivo, Puertas, cfg.Confianza, transporte), _llamadas);
    }

    private static void Muestra(DecisionDeUnPaso d, int? llamadas)
    {
        Color(d.Actuar ? ConsoleColor.Green : ConsoleColor.DarkGray, $"    actuar       : {d.Actuar}");
        if (d.Puerta.Length > 0) Console.WriteLine($"    puerta       : {d.Puerta}");
        Console.WriteLine($"    confianza    : {d.Confianza:0.00}");
        Console.WriteLine($"    porque       : {d.Porque}");
        if (llamadas.HasValue)
            Color(llamadas.Value == 0 ? ConsoleColor.Cyan : ConsoleColor.Magenta,
                  $"    llamadas a TypeSafe: {llamadas.Value}");
    }

    private static string Respuesta(string elegida, double confianza) =>
        "{\"model\":\"jev-1.13.0\",\"answers\":{\"" + PeticionASystemOne.IdDeLaPuerta
      + "\":{\"type\":\"choice\",\"choice\":\"" + elegida + "\",\"probabilities\":{\"" + elegida + "\":"
      + confianza.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
      + "},\"confidence\":" + confianza.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
      + "}},\"usage\":{\"input_tokens\":312,\"output_tokens\":0}}";

    private static void Titulo(string t)
    {
        Console.WriteLine();
        Color(ConsoleColor.Yellow, "=== " + t + " ===");
    }

    private static void Color(ConsoleColor c, string t)
    {
        var antes = Console.ForegroundColor;
        Console.ForegroundColor = c;
        Console.WriteLine(t);
        Console.ForegroundColor = antes;
    }
}
