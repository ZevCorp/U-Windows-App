using Mapeador;

namespace Mapeador.Pruebas;

/// <summary>
/// EL CONTRATO DEL MAPEADOR: lo que promete el PROCESO que alimenta al grafo, escrito como pruebas
/// que llaman al código real y corren sin abrir una ventana.
///
/// Hermano del contrato del núcleo, y separado a propósito: aquel juzga QUÉ SE SABE, este juzga CÓMO
/// SE SABE. La razón de que exista es que el 2026-08-12 el mapeo pasó de 0,4 s a 2,2 y no había
/// ningún sitio donde esa clase de promesa cupiera — el invariante estaba escrito como dos
/// `Interlocked` sueltos dentro de una clase de WPF, sin nombre y sin forma de probarse.
///
/// Ninguna promesa mide milisegundos. Un umbral de tiempo falla en una máquina cargada o un martes,
/// y un juez que da rojos por motivos ajenos al código enseña a desconfiar del juez.
/// </summary>
internal static class Contrato
{
    private static int _fallos;

    private static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("CONTRATO DEL MAPEADOR (el proceso, aislado)\n");

        Prueba("1. una vuelta a la vez: la que llega tarde se DESCARTA, no espera", UnaALaVez);
        Prueba("2. descartar deja rastro, y se sabe de qué reloj", DescartarSeCuenta);
        Prueba("3. al terminar, la siguiente entra: el candado se suelta", ElCandadoSeSuelta);
        Prueba("4. si la vuelta revienta, el candado se suelta igual", RevantarNoDejaCerrado);
        Prueba("5. los dos relojes no se estorban: el caro no bloquea al barato", DosRelojesIndependientes);
        Prueba("6. con hilos de verdad, nunca hay dos dentro a la vez", NuncaDosDentro);
        Prueba("7. todo rechazo lleva motivo", RechazoConMotivo);
        Prueba("8. de cada coste se guarda también LA PEOR, no solo la media", SeGuardaLaPeor);
        Prueba("9. el pulso no opina: no hay veredictos, solo cuentas", ElPulsoNoOpina);

        Console.WriteLine();
        Console.WriteLine(_fallos == 0
            ? "MAPEADOR ÍNTEGRO: el proceso promete lo que dice prometer."
            : $"MAPEADOR ROTO: {_fallos} promesa(s) incumplida(s).");

        Apuntar();
        return _fallos;
    }

    /// <summary>
    /// Deja el veredicto en disco, junto al del núcleo, para poder mirarlo sin correr nada.
    /// </summary>
    /// <remarks>
    /// EN LA MISMA CARPETA Y EN LA MISMA PESTAÑA que el del núcleo, y no en un panel aparte. Las dos
    /// contestan la misma pregunta —«¿qué está garantizado?»— y partirla en dos sitios obliga a
    /// acordarse de mirar los dos; el que se olvida es siempre el que está en rojo. Lo que sí es
    /// otra pregunta es el pulso —«¿qué está pasando AHORA?»—, y por eso esa sí tiene su pestaña.
    ///
    /// Y lo que se enseña es el RESULTADO de correrlas, con su hora, no una lista que alguien
    /// tecleó: una descripción envejece en silencio en cuanto alguien toca el código.
    /// </remarks>
    private static void Apuntar()
    {
        try
        {
            // Se sube desde el binario buscando el repo —el que tenga `nucleo/visor` dentro—. Si no
            // aparece, se calla: no poder dejar la nota no puede tumbar al juez.
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "nucleo", "visor"))) dir = dir.Parent;
            if (dir == null) return;

            var filas = _veredicto.Select(v =>
                $"{{\"regla\":{Cita(v.Nombre)},\"cumple\":{(v.Cumple ? "true" : "false")}}}");
            File.WriteAllText(Path.Combine(dir.FullName, "nucleo", "visor", "reglas-mapeador.json"),
                "{\"cuando\":" + Cita(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                + ",\"integro\":" + (_fallos == 0 ? "true" : "false")
                + ",\"reglas\":[" + string.Join(",", filas) + "]}",
                System.Text.Encoding.UTF8);
        }
        catch { }
    }

    private static string Cita(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static readonly List<(string Nombre, bool Cumple)> _veredicto = new();

    // ── Las promesas ─────────────────────────────────────────────────────────

    /// <remarks>
    /// EL FALLO QUE ESTO IMPIDE: un `Timer` de .NET dispara la siguiente vuelta aunque la anterior no
    /// haya vuelto. El intervalo bajó a 120 ms para algo que costaba 200, las llamadas se encolaron,
    /// la cola creció sola, y leer la Maqueta pasó de 0,4 s a 2,2 (2026-08-12).
    ///
    /// Se descarta y NO se encola: si el estado volvió a cambiar, el siguiente latido lo recogerá
    /// igual, y una cola solo serviría para pintar con retraso una foto ya caducada.
    /// </remarks>
    private static void UnaALaVez()
    {
        var v = new VueltaUnica("prueba");
        Debe(v.MeToca(), "la primera entra");
        Debe(!v.MeToca(), "la segunda, con la primera dentro, NO entra");
        Debe(!v.MeToca(), "ni la tercera: no se hace cola");
        v.Termine();
    }

    private static void DescartarSeCuenta()
    {
        var v = new VueltaUnica("pantalla");
        Debe(v.Descartadas == 0, "recién hecha, no ha descartado nada");
        v.MeToca();
        v.MeToca(); v.MeToca();
        Debe(v.Descartadas == 2, "se cuentan las dos que llegaron tarde");
        Debe(v.Nombre == "pantalla", "y se sabe de qué reloj se está hablando");
        v.Termine();

        // Y el candado es el ÚNICO que lleva esa cuenta: tenerla también en el pulso era tener dos
        // opiniones sobre el mismo hecho, que es la avería que este panel existe para no repetir.
        Debe(PulsoDelMapeador.Actual.DescartadasPantalla == PulsoDelMapeador.Actual.Pantalla.Descartadas,
            "el pulso no lleva su propia cuenta: lee la del candado");
    }

    private static void ElCandadoSeSuelta()
    {
        var v = new VueltaUnica("prueba");
        v.MeToca();
        v.Termine();
        Debe(v.MeToca(), "terminada la anterior, la siguiente entra");
        v.Termine();
        Debe(v.Descartadas == 0, "y ninguna de las dos se descartó: no se pisaron");
    }

    /// <remarks>
    /// OLVIDARSE DE SOLTAR NO SE VE COMO UN FALLO, SE VE COMO «SE QUEDÓ PARADO». El candado queda
    /// cerrado para siempre, todas las vueltas siguientes se descartan, y el mapa deja de moverse sin
    /// un solo error en el log. Por eso existe <see cref="VueltaUnica.Corre"/>: suelta en un
    /// `finally` que no se puede omitir.
    /// </remarks>
    private static void RevantarNoDejaCerrado()
    {
        var v = new VueltaUnica("prueba");
        try { v.Corre(() => throw new InvalidOperationException("algo se rompió dentro")); }
        catch (InvalidOperationException) { }
        Debe(v.MeToca(), "tras reventar la vuelta, la siguiente PUEDE entrar");
        v.Termine();
    }

    /// <remarks>
    /// Saber dónde estoy es barato (~45 ms) y va cada 250 ms; leer la pantalla es caro (~1 s) y va
    /// cada 900. Con un candado compartido, la vuelta cara bloquearía a la barata y el sitio actual
    /// se quedaría atrasado — que es exactamente el síntoma de «vaya donde vaya, se queda en
    /// claude.exe» por el otro camino.
    /// </remarks>
    private static void DosRelojesIndependientes()
    {
        var p = PulsoDelMapeador.Actual;
        Debe(!ReferenceEquals(p.Ubicacion, p.Pantalla), "son dos candados, no uno");
        Debe(p.Pantalla.MeToca(), "la vuelta cara entra");
        Debe(p.Ubicacion.MeToca(), "…y la barata entra IGUAL, sin esperarla");
        p.Ubicacion.Termine();
        p.Pantalla.Termine();
    }

    /// <remarks>
    /// Las cinco promesas de arriba pasarían con un `bool` sin `Interlocked`: con un solo hilo nunca
    /// se ve la carrera. Esta la ve. Sin ella, el contrato certificaría un candado que no cierra.
    /// </remarks>
    private static void NuncaDosDentro()
    {
        var v = new VueltaUnica("prueba");
        int dentro = 0, maximo = 0, entraron = 0;
        var hilos = new Thread[8];
        for (int i = 0; i < hilos.Length; i++)
        {
            hilos[i] = new Thread(() =>
            {
                for (int k = 0; k < 4000; k++)
                {
                    if (!v.MeToca()) continue;
                    Interlocked.Increment(ref entraron);
                    int ahora = Interlocked.Increment(ref dentro);
                    // Se apunta el máximo visto. Si el candado no cerrara, aquí se vería un 2.
                    int visto;
                    while (ahora > (visto = Volatile.Read(ref maximo)))
                        Interlocked.CompareExchange(ref maximo, ahora, visto);
                    Interlocked.Decrement(ref dentro);
                    v.Termine();
                }
            });
            hilos[i].Start();
        }
        foreach (var h in hilos) h.Join();

        Debe(maximo == 1, $"nunca hubo más de uno dentro (el máximo visto fue {maximo})");
        Debe(entraron > 0, "y alguien entró: la prueba no pasó por vacía");
        Debe(entraron + v.Descartadas == 8 * 4000, "cada intento acabó o dentro o descartado, ninguno se perdió");
    }

    /// <remarks>
    /// «No se atribuyó» sin causa obliga a volver al log, que es justo de donde el panel viene a
    /// sacarnos. El motivo ES el dato.
    /// </remarks>
    private static void RechazoConMotivo()
    {
        var p = PulsoDelMapeador.Actual;
        int antes = p.Saltos;
        p.Rechazada("el clic ya explicó otro salto");
        Debe(p.Saltos == antes + 1, "un rechazo cuenta como salto: el salto ocurrió igual");
        Debe(p.Rechazos.ContainsKey("el clic ya explicó otro salto"), "y queda apuntado con su causa");
        Debe(p.Rechazos.Keys.All(m => !string.IsNullOrWhiteSpace(m)), "ningún motivo está en blanco");
    }

    /// <remarks>
    /// Una media buena con una peor terrible es justo el perfil que se SIENTE lento y no lo parece en
    /// la media. La media de proyectar tapó durante horas unos picos que chocaban con el corte de
    /// 5 s del cliente (2026-08-13).
    /// </remarks>
    private static void SeGuardaLaPeor()
    {
        var p = PulsoDelMapeador.Actual;
        p.Costo("prueba-coste", 10);
        p.Costo("prueba-coste", 900);
        p.Costo("prueba-coste", 10);
        var t = p.Tiempos["prueba-coste"];
        Debe(t.Veces == 3, "se cuentan las veces");
        Debe(t.PeorMs == 900, "y se guarda LA PEOR, que la media esconde");
        Debe(t.TotalMs / t.Veces < 400, "…mientras la media sigue pareciendo buena");
    }

    /// <remarks>
    /// El panel no calcula nada: proyecta estos contadores tal cual. Si el pulso emitiera además un
    /// juicio —«saturado», «lento»— tendríamos dos opiniones sobre el mismo hecho, y esa es
    /// exactamente la avería que llevamos la semana entera pagando. Esta promesa lo vigila leyendo el
    /// propio tipo: si alguien añade un `bool EstaSaturado`, sale en rojo.
    /// </remarks>
    private static void ElPulsoNoOpina()
    {
        var juicios = typeof(PulsoDelMapeador).GetProperties()
            .Where(x => x.PropertyType == typeof(bool))
            .Select(x => x.Name)
            .ToList();
        Debe(juicios.Count == 0,
            "el pulso solo cuenta, no dictamina" + (juicios.Count == 0 ? "" : ": sobra " + string.Join(", ", juicios)));
    }

    // ── El arnés ─────────────────────────────────────────────────────────────

    private static void Prueba(string nombre, Action cuerpo)
    {
        int antes = _fallos;
        try { cuerpo(); }
        catch (Exception e)
        {
            _fallos++;
            for (var x = e; x != null; x = x.InnerException)
                Console.WriteLine($"   ✘ {x.GetType().Name}: {x.Message}");
        }
        bool cumple = _fallos == antes;
        _veredicto.Add((nombre, cumple));
        Console.WriteLine($"{(cumple ? "✔" : "✘")} {nombre}");
    }

    private static void Debe(bool condicion, string promesa)
    {
        if (condicion) return;
        _fallos++;
        Console.WriteLine($"   ✘ {promesa}");
    }
}
