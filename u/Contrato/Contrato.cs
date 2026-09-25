using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

// EL CONTRATO DE Ü DESDE CERO — spec 052 (docs/specs/052-u-desde-cero.md), promesas 430-441.
//
// Cada enunciado es el de la spec, literal. Tres veredictos por promesa, y no se mezclan:
//   ✔ cumplida · ✘ incumplida · ⧗ PENDIENTE (la pieza todavía no existe: cuenta como incumplida)
// y uno más para el propio juez: ⚠ NO PUDE JUZGARLA (el arnés falló), que también rompe el contrato
// pero dice otra cosa (aprendizaje nº17: un juez que no puede correr no dice «culpable»).

internal static class Contrato
{
    private static Assembly _nucleo = null!;
    private static int _ok, _mal, _pendientes, _arnes;

    private static int Main()
    {
        try { _nucleo = Assembly.Load("U.Ciclo"); }
        catch (Exception e)
        {
            Console.WriteLine("⚠ NO PUDE CARGAR EL NÚCLEO (U.Ciclo): el contrato no juzgó nada.");
            for (var x = e; x != null; x = x.InnerException) Console.WriteLine($"   ✘ {x.GetType().Name}: {x.Message}");
            return 3;
        }

        Promesa(430, "«Dónde estoy» se contesta sin UIA: ventana, proceso y título salen de Win32, y la propia burbuja de Ü nunca es «dónde estoy».", P430);
        Promesa(431, "Un accionable se identifica por su número en la lista de ESTE ciclo; dos accionables con la misma etiqueta son dos números distintos.", P431);
        Promesa(432, "Lo que no se ve no se ofrece: fuera de pantalla, sin tamaño o deshabilitado no entra en la lista.", P432);
        Promesa(433, "El clic cae en el centro del accionable, en coordenadas de pantalla, y es un clic del ratón real (abajo + arriba, botón izquierdo).", P433);
        Promesa(434, "La respuesta de Jev se valida entera: un número que no se ofreció, una confianza bajo el umbral, «cumplido» alto o «peligro» alto = no se pulsa, y se dice cuál.", P434);
        Promesa(435, "El cuerpo que se le manda a Jev lleva la pantalla, el objetivo y las puertas numeradas, y nunca la clave.", P435);
        Promesa(436, "La espera tras el clic termina en cuanto cambia la huella de los accionables, y nunca pasa de su techo (150 ms).", P436);
        Promesa(437, "Cada ciclo deja sus cinco tiempos (dónde, ver, decidir, pulsar, asentar) y su total, y se marca FUERA DE PRESUPUESTO si pasa de 500 ms.", P437);
        Promesa(438, "Un plan de Luna se lee a objetivos ejecutables; un plan vacío o ilegible no ejecuta nada y lo dice.", P438);
        Promesa(439, "Un paso del plan que no se ejecutó deja rastro (Omitido); el denominador del resultado es el plan.", P439);
        Promesa(440, "El ciclo para al primer «cumplido», al tope de pasos, o cuando Jev repite la misma puerta tres veces sin cambio.", P440);
        Promesa(441, "Escape detiene todo en el ciclo siguiente, sin pulsar nada más.", P441);
        Promesa(442, "Jev sabe lo que ya se hizo: el estado lleva, en orden, lo que ya se pulsó para este objetivo.", P442);
        Promesa(443, "La huella ve los textos: si lo único que cambia es lo que dice la pantalla, la huella cambia.", P443);
        Promesa(444, "Solo cuentan como emergentes las ventanas que pertenecen a la de delante: la barra de tareas no es un menú del Explorador.", P444);

        Console.WriteLine();
        int incumplidas = _mal + _pendientes + _arnes;
        Console.WriteLine($"{_ok} cumplida(s) · {_mal} incumplida(s) · {_pendientes} pendiente(s) · {_arnes} sin juzgar");
        Console.WriteLine(incumplidas == 0
            ? "CONTRATO INTACTO: Ü desde cero cumple lo que promete."
            : $"CONTRATO ROTO: {incumplidas} promesa(s) incumplida(s). El cambio no puede entrar así.");
        return incumplidas == 0 ? 0 : 1;
    }

    // ── El arnés ────────────────────────────────────────────────────────────────────────────────

    private sealed class Pendiente : Exception { public Pendiente(string que) : base(que) { } }
    private sealed class Incumplida : Exception { public Incumplida(string que) : base(que) { } }

    private static void Promesa(int n, string enunciado, Action juicio)
    {
        try { juicio(); _ok++; Console.WriteLine($"✔ {n}  {enunciado}"); }
        catch (Pendiente p) { _pendientes++; Console.WriteLine($"⧗ {n}  PENDIENTE: «{p.Message}» todavía no existe.\n       {enunciado}"); }
        catch (Incumplida i) { _mal++; Console.WriteLine($"✘ {n}  {enunciado}\n       {i.Message}"); }
        catch (Exception e)
        {
            var raiz = e is TargetInvocationException { InnerException: { } ie } ? ie : e;
            _mal++;
            Console.WriteLine($"✘ {n}  {enunciado}");
            for (var x = raiz; x != null; x = x.InnerException) Console.WriteLine($"       ✘ {x.GetType().Name}: {x.Message}");
        }
    }

    private static void Exige(bool cierto, string que) { if (!cierto) throw new Incumplida(que); }

    private static Type T(string nombre) =>
        _nucleo.GetType("U.Ciclo." + nombre) ?? throw new Pendiente("U.Ciclo." + nombre);

    private static object? S(string tipo, string metodo, params object?[] args)
    {
        var t = T(tipo);
        var m = t.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(x => x.Name == metodo && x.GetParameters().Length == args.Length)
                ?? throw new Pendiente($"{tipo}.{metodo}/{args.Length}");
        try { return m.Invoke(null, args); }
        catch (TargetInvocationException e) when (e.InnerException != null) { throw e.InnerException; }
    }

    private static object N(string tipo, params object?[] args)
    {
        var t = T(tipo);
        try { return Activator.CreateInstance(t, args) ?? throw new Pendiente($"new {tipo}"); }
        catch (MissingMethodException) { throw new Pendiente($"new {tipo}({args.Length} argumentos)"); }
        catch (TargetInvocationException e) when (e.InnerException != null) { throw e.InnerException; }
    }

    private static object? P(object o, string propiedad) =>
        (o.GetType().GetProperty(propiedad) ?? throw new Pendiente($"{o.GetType().Name}.{propiedad}")).GetValue(o);

    private static object? I(object o, string metodo, params object?[] args)
    {
        var m = o.GetType().GetMethods().FirstOrDefault(x => x.Name == metodo && x.GetParameters().Length == args.Length)
                ?? throw new Pendiente($"{o.GetType().Name}.{metodo}/{args.Length}");
        try { return m.Invoke(o, args); }
        catch (TargetInvocationException e) when (e.InnerException != null) { throw e.InnerException; }
    }

    private static List<object> L(object? lista) => ((System.Collections.IEnumerable)lista!).Cast<object>().ToList();

    private static object Caja(int x, int y, int w, int h) => N("Caja", x, y, w, h);
    private static object Crudo(string nombre, string tipo, object caja, bool habilitado = true, bool fuera = false) =>
        N("Crudo", nombre, tipo, caja, habilitado, fuera);

    /// <summary>Una lista de accionables de verdad, construida por el mismo camino que usa el núcleo.</summary>
    private static object Lista(params (string Nombre, string Tipo)[] cosas)
    {
        var crudoT = T("Crudo");
        var arr = Array.CreateInstance(crudoT, cosas.Length);
        for (int i = 0; i < cosas.Length; i++) arr.SetValue(Crudo(cosas[i].Nombre, cosas[i].Tipo, Caja(10 * i, 10, 40, 20)), i);
        return S("Accionables", "Numerar", arr)!;
    }

    private static object Ubicacion(int pid, string proceso) => N("Ubicacion", new IntPtr(pid * 10), pid, proceso, proceso + " — título");

    // ── Las promesas ────────────────────────────────────────────────────────────────────────────

    private static void P430()
    {
        // SIN UIA: medido, no supuesto. Si «dónde estoy» tocara UIA, el ensamblado de interop se cargaría.
        bool uiaAntes = AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "Interop.UIAutomationClient");
        var tiempos = new List<double>();
        object? ultima = null;
        for (int i = 0; i < 50; i++)
        {
            var r = Stopwatch.StartNew();
            ultima = S("Donde", "Leer");
            tiempos.Add(r.Elapsed.TotalMilliseconds);
        }
        bool uiaDespues = AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "Interop.UIAutomationClient");
        Exige(!uiaAntes && !uiaDespues, "leer «dónde estoy» cargó el interop de UIA");
        double med = tiempos.OrderBy(x => x).ElementAt(tiempos.Count / 2);
        Exige(med < 2.0, $"«dónde estoy» tarda {med:0.00} ms de mediana; una llamada a Win32 cabe en menos de 2 ms");

        // LA PROPIA BURBUJA NUNCA ES «DÓNDE ESTOY»: si delante está Ü, se contesta la última ventana ajena.
        var propia = Ubicacion(42, "U");
        var ajena = Ubicacion(7, "notepad");
        var r1 = S("Donde", "Elegir", propia, 42, ajena);
        Exige(r1 != null && (int)P(r1, "Pid")! == 7, "con Ü delante, «dónde estoy» no contestó la última ventana ajena");
        var r2 = S("Donde", "Elegir", ajena, 42, null);
        Exige(r2 != null && (int)P(r2, "Pid")! == 7, "con otra app delante, «dónde estoy» no la contestó a ella");
        var r3 = S("Donde", "Elegir", propia, 42, null);
        Exige(r3 == null, "con Ü delante y ninguna ajena conocida, «dónde estoy» se contestó a sí mismo");
    }

    private static void P431()
    {
        var l = L(Lista(("Sistema", "Button"), ("Sistema", "ListItem"), ("Sistema", "ListItem")));
        Exige(l.Count == 3, $"se esperaban 3 accionables y hay {l.Count}: los homónimos se fundieron");
        var numeros = l.Select(a => (int)P(a, "Numero")!).ToList();
        Exige(numeros.SequenceEqual(new[] { 1, 2, 3 }), $"los números no son 1,2,3 en orden de lectura: {string.Join(",", numeros)}");
        var ids = l.Select(a => (string)P(a, "Id")!).ToList();
        Exige(ids[0] == "1) Sistema (Button)" && ids[1] == "2) Sistema (ListItem)" && ids[2] == "3) Sistema (ListItem)",
            $"los ids no llevan su número: {string.Join(" | ", ids)}");
        Exige(ids.Distinct().Count() == 3, "dos accionables con la misma etiqueta comparten id");
    }

    private static void P432()
    {
        var crudoT = T("Crudo");
        var cosas = new[]
        {
            Crudo("Visible", "Button", Caja(0, 0, 40, 20)),
            Crudo("Fuera", "Button", Caja(0, 0, 40, 20), fuera: true),
            Crudo("Plano", "Button", Caja(0, 0, 0, 20)),
            Crudo("Apagado", "Button", Caja(0, 0, 40, 20), habilitado: false),
            Crudo("   ", "Button", Caja(0, 0, 40, 20)),
        };
        var arr = Array.CreateInstance(crudoT, cosas.Length);
        for (int i = 0; i < cosas.Length; i++) arr.SetValue(cosas[i], i);
        var l = L(S("Accionables", "Numerar", arr));
        var nombres = l.Select(a => (string)P(a, "Nombre")!).ToList();
        Exige(nombres.SequenceEqual(new[] { "Visible" }), $"se ofreció lo que no se ve: {string.Join(", ", nombres)}");
    }

    private static void P433()
    {
        var c = S("Raton", "Centro", Caja(100, 200, 50, 20))!;
        int x = (int)c.GetType().GetField("Item1")!.GetValue(c)!, y = (int)c.GetType().GetField("Item2")!.GetValue(c)!;
        Exige(x == 125 && y == 210, $"el centro de (100,200,50×20) es (125,210) y salió ({x},{y})");
        var g = L(S("Raton", "Gesto", 125, 210)).Select(o => o.ToString()).ToList();
        Exige(g.SequenceEqual(new[] { "mover 125,210", "izquierdo abajo", "izquierdo arriba" }),
            $"el gesto no es mover + abajo + arriba del botón izquierdo: {string.Join(" · ", g)}");
    }

    private static string Respuesta(string choice, double conf, double cumplido = 0, double peligro = 0) =>
        JsonSerializer.Serialize(new
        {
            answers = new Dictionary<string, object>
            {
                ["puerta"] = new { choice, confidence = conf },
                ["cumplido"] = new { noul = cumplido },
                ["peligro"] = new { noul = peligro },
            },
        });

    private static void P434()
    {
        var ofrecidas = Lista(("Abrir", "Button"), ("Cerrar", "Button"));
        object E(string json) => S("Jev", "Interpretar", json, ofrecidas, 0.70)!;
        bool Pulsa(object e) => (bool)P(e, "Pulsar")!;
        string Porque(object e) => ((string)P(e, "Porque")!).ToLowerInvariant();

        var bien = E(Respuesta("1) Abrir (Button)", 0.93));
        Exige(Pulsa(bien) && (int)P(bien, "Numero")! == 1, "una elección buena y ofrecida no se pulsó, o no con su número");

        var fuera = E(Respuesta("3) Grabar (Button)", 0.99));
        Exige(!Pulsa(fuera) && Porque(fuera).Contains("no se ofreció"), $"un número que no se ofreció: {Porque(fuera)}");

        var tibia = E(Respuesta("2) Cerrar (Button)", 0.50));
        Exige(!Pulsa(tibia) && Porque(tibia).Contains("confianza"), $"confianza bajo el umbral: {Porque(tibia)}");

        var hecho = E(Respuesta("1) Abrir (Button)", 0.95, cumplido: 0.9));
        Exige(!Pulsa(hecho) && Porque(hecho).Contains("cumplido"), $"«cumplido» alto: {Porque(hecho)}");

        var peligro = E(Respuesta("2) Cerrar (Button)", 0.95, peligro: 0.8));
        Exige(!Pulsa(peligro) && Porque(peligro).Contains("peligro"), $"«peligro» alto: {Porque(peligro)}");

        var basura = E("<html>502</html>");
        Exige(!Pulsa(basura) && Porque(basura).Length > 0, "una respuesta ilegible se pulsó o no dijo por qué");
    }

    private static void P435()
    {
        Environment.SetEnvironmentVariable("TYPESAFE_API_KEY", "sk-secreto-del-contrato");
        var ofrecidas = Lista(("Abrir", "Button"), ("Abrir", "MenuItem"));
        string cuerpo = (string)S("Jev", "Cuerpo", "notepad · Sin título", "abrir el menú Archivo", ofrecidas, "jev-latest")!;
        Exige(!cuerpo.Contains("sk-secreto-del-contrato"), "LA CLAVE VA EN EL CUERPO");
        using var doc = JsonDocument.Parse(cuerpo);
        string estado = doc.RootElement.GetProperty("state").GetString() ?? "";
        Exige(estado.Contains("notepad · Sin título") && estado.Contains("abrir el menú Archivo"), "el estado no lleva la pantalla y el objetivo");
        var criterios = doc.RootElement.GetProperty("questions").GetProperty("puerta").GetProperty("criteria")
            .EnumerateObject().Select(p => p.Name).ToList();
        Exige(criterios.SequenceEqual(new[] { "1) Abrir (Button)", "2) Abrir (MenuItem)" }),
            $"las puertas no van numeradas y únicas: {string.Join(" | ", criterios)}");
        Exige(doc.RootElement.GetProperty("questions").TryGetProperty("cumplido", out _), "no se pregunta si ya está cumplido");
    }

    private static void P436()
    {
        long reloj = 0;
        Func<long> Reloj = () => reloj;
        // Cambia a la tercera lectura: se sale ahí, sin esperar al techo.
        int lecturas = 0;
        Func<string> cambiaALaTercera = () => { lecturas++; reloj += 10; return lecturas >= 3 ? "B" : "A"; };
        var a = S("Asentado", "Esperar", cambiaALaTercera, "A", 150, Reloj)!;
        Exige((bool)P(a, "Cambio")! && (int)P(a, "Lecturas")! == 3, $"no salió en cuanto cambió: {a}");

        // Nunca cambia: se rinde en el techo, no después.
        reloj = 0; lecturas = 0;
        Func<string> nuncaCambia = () => { lecturas++; reloj += 10; return "A"; };
        var b = S("Asentado", "Esperar", nuncaCambia, "A", 150, Reloj)!;
        Exige(!(bool)P(b, "Cambio")! && (long)P(b, "Ms")! <= 160, $"pasó del techo de 150 ms: {b}");

        // Ya cambió al llegar: una sola lectura.
        reloj = 0; lecturas = 0;
        var c = S("Asentado", "Esperar", (Func<string>)(() => { lecturas++; return "B"; }), "A", 150, Reloj)!;
        Exige((bool)P(c, "Cambio")! && (int)P(c, "Lecturas")! == 1, $"leyó de más con la pantalla ya cambiada: {c}");
    }

    private static void P437()
    {
        Exige((double)(T("Ciclo").GetField("Presupuesto")?.GetValue(null) ?? throw new Pendiente("Ciclo.Presupuesto")) == 500,
            "el presupuesto no es 500 ms");
        var dentro = N("Tiempos", 0.3, 80.0, 220.0, 0.5, 100.0);
        Exige(Math.Abs((double)P(dentro, "Total")! - 400.8) < 0.01, $"el total no es la suma de las cinco fases: {P(dentro, "Total")}");
        Exige(!(bool)P(dentro, "FueraDePresupuesto")!, "un ciclo de 400 ms se marcó fuera de presupuesto");
        string linea = ((string)I(dentro, "Linea")!).ToLowerInvariant();
        foreach (var f in new[] { "dónde", "ver", "decidir", "pulsar", "asentar", "total" })
            Exige(linea.Contains(f), $"la línea del ciclo no dice «{f}»: {linea}");

        var fuera = N("Tiempos", 0.3, 80.0, 220.0, 0.5, 250.0);
        Exige((bool)P(fuera, "FueraDePresupuesto")!, "un ciclo de 550 ms no se marcó fuera de presupuesto");
        Exige(((string)I(fuera, "Linea")!).Contains("FUERA DE PRESUPUESTO"), "la línea de un ciclo lento no lo dice");
    }

    private static void P438()
    {
        var bueno = S("Plan", "Leer", "{\"pasos\":[\"abrir el Bloc de notas\",\"abrir el menú Archivo\"]}")!;
        var pasos = L(P(bueno, "Pasos")).Select(o => (string)o).ToList();
        Exige(pasos.SequenceEqual(new[] { "abrir el Bloc de notas", "abrir el menú Archivo" }), $"no se leyeron los pasos: {string.Join(" | ", pasos)}");

        var cercado = S("Plan", "Leer", "```json\n{\"pasos\":[\"ir a Sistema\"]}\n```")!;
        Exige(L(P(cercado, "Pasos")).Count == 1, "un plan dentro de ```json no se leyó");

        foreach (var malo in new[] { "no sé", "{\"pasos\":[]}", "{\"pasos\":[\"  \"]}", "" })
        {
            var r = S("Plan", "Leer", malo)!;
            Exige(L(P(r, "Pasos")).Count == 0, $"«{malo}» produjo pasos");
            Exige(((string)P(r, "Porque")!).Trim().Length > 0, $"«{malo}» no ejecutó nada pero no dijo por qué");
        }
    }

    private static void P439()
    {
        var plan = new List<string> { "abrir", "ir a Sistema", "ir a Pantalla" };
        var hechos = new List<bool> { true, false };
        var r = S("Plan", "Resultado", plan, hechos)!;
        var estados = L(P(r, "Pasos")).Select(p => (string)P(p, "Estado")!).ToList();
        Exige(estados.SequenceEqual(new[] { "Hecho", "Fallido", "Omitido" }), $"estados: {string.Join(", ", estados)}");
        string resumen = (string)P(r, "Resumen")!;
        Exige(resumen.Contains("1 de 3"), $"el denominador no es el plan: «{resumen}»");
    }

    // Un motor de mentira: pantallas que cambian (o no) y un decisor guionizado.
    private static (object Motor, List<int> Pulsados) Motor(Func<int, object> decidirEnLaVuelta, bool pantallaCambia, Func<int, bool>? parar = null)
    {
        var pulsados = new List<int>();
        int vuelta = 0;
        var accionablesT = typeof(IReadOnlyList<>).MakeGenericType(T("Accionable"));
        Func<object?> donde = () => Ubicacion(7, "notepad");
        var dondeT = typeof(Func<>).MakeGenericType(T("Ubicacion"));
        var verT = typeof(Func<>).MakeGenericType(accionablesT);
        var decidirT = typeof(Func<,,,>).MakeGenericType(typeof(string), typeof(string), accionablesT, T("Eleccion"));
        var pulsarT = typeof(Action<>).MakeGenericType(T("Accionable"));

        object Ver() => pantallaCambia ? Lista(("Pantalla " + pulsados.Count, "Text"), ("Siguiente", "Button")) : Lista(("Igual", "Text"), ("Siguiente", "Button"));
        object Decidir(string p, string o, object l) { vuelta++; return decidirEnLaVuelta(vuelta); }
        void Pulsar(object a) => pulsados.Add((int)P(a, "Numero")!);

        var dDonde = Delegado(dondeT, () => donde());
        var dVer = Delegado(verT, () => Ver());
        var dDecidir = DelegadoDe3(decidirT, (a, b, c) => Decidir((string)a!, (string)b!, c!));
        var dPulsar = DelegadoAccion(pulsarT, a => Pulsar(a!));
        var dParar = (Func<bool>)(() => parar?.Invoke(pulsados.Count) ?? false);
        return (N("Motor", dDonde, dVer, dDecidir, dPulsar, dParar), pulsados);
    }

    private static object Eleccion(bool pulsar, int numero, double conf = 0.9, double cumplido = 0, string porque = "") =>
        N("Eleccion", pulsar, numero, conf, cumplido, porque);

    private static void P440()
    {
        // Cumplido a la segunda vuelta.
        var (m1, p1) = Motor(v => v >= 2 ? Eleccion(false, 0, cumplido: 0.9, porque: "cumplido") : Eleccion(true, 2), true);
        var r1 = I(m1, "Objetivo", "llegar", 10)!;
        Exige(p1.Count == 1 && ((string)P(r1, "PorQueParo")!).Contains("cumplido"), $"no paró al primer «cumplido»: {p1.Count} pulsos · {P(r1, "PorQueParo")}");

        // Tope de pasos.
        var (m2, p2) = Motor(_ => Eleccion(true, 2), true);
        var r2 = I(m2, "Objetivo", "seguir", 3)!;
        Exige(p2.Count == 3 && ((string)P(r2, "PorQueParo")!).Contains("tope"), $"no paró en el tope: {p2.Count} pulsos · {P(r2, "PorQueParo")}");
        Exige(L(P(r2, "Vueltas")).Count == 3, "las vueltas no dejaron rastro");

        // La misma puerta tres veces sin que nada cambie.
        var (m3, p3) = Motor(_ => Eleccion(true, 2), false);
        var r3 = I(m3, "Objetivo", "insistir", 10)!;
        Exige(p3.Count == 3 && ((string)P(r3, "PorQueParo")!).Contains("repite"), $"no paró al repetir sin cambio: {p3.Count} pulsos · {P(r3, "PorQueParo")}");
    }

    private static void P441()
    {
        var (m, p) = Motor(_ => Eleccion(true, 2), true, parar: pulsos => pulsos >= 1);
        var r = I(m, "Objetivo", "seguir", 10)!;
        Exige(p.Count == 1 && ((string)P(r, "PorQueParo")!).Contains("Escape"), $"Escape no detuvo: {p.Count} pulsos · {P(r, "PorQueParo")}");
    }

    private static void P442()
    {
        // El cuerpo lleva lo ya hecho, en orden, bajo su propio encabezado.
        var ofrecidas = Lista(("Archivo", "MenuItem"), ("Nuevo", "MenuItem"));
        var hecho = new List<string> { "pulsé «4) Archivo (MenuItem)»", "escribí «hola»" };
        var ctx = N("Contexto", "notepad · Sin título", "abrir el menú Archivo", ofrecidas, new List<string> { "Ln 1, Col 1" }, hecho);
        string cuerpo = (string)S("Jev", "Cuerpo", ctx, "jev-latest")!;
        using var doc = JsonDocument.Parse(cuerpo);
        string estado = doc.RootElement.GetProperty("state").GetString() ?? "";
        int a = estado.IndexOf("pulsé «4) Archivo (MenuItem)»", StringComparison.Ordinal), b = estado.IndexOf("escribí «hola»", StringComparison.Ordinal);
        Exige(estado.Contains("Ya hecho") && a >= 0 && b > a, $"el estado no lleva lo ya hecho en orden:\n{estado}");
        Exige(estado.Contains("Ln 1, Col 1"), "el estado no lleva lo que dice la pantalla");

        // Y el motor se lo pasa: en la segunda vuelta, lo pulsado en la primera.
        var vistos = new List<List<string>>();
        var contextoT = T("Contexto");
        var decidirT = typeof(Func<,>).MakeGenericType(contextoT, T("Eleccion"));
        int vuelta = 0;
        var p = System.Linq.Expressions.Expression.Parameter(contextoT, "c");
        var dDecidir = System.Linq.Expressions.Expression.Lambda(decidirT,
            System.Linq.Expressions.Expression.Convert(System.Linq.Expressions.Expression.Invoke(
                System.Linq.Expressions.Expression.Constant((Func<object, object>)(c =>
                {
                    vistos.Add(L(P(c, "Hecho")).Select(o => (string)o).ToList());
                    return ++vuelta >= 2 ? Eleccion(false, 0, cumplido: 0.9) : Eleccion(true, 2);
                })),
                System.Linq.Expressions.Expression.Convert(p, typeof(object))), T("Eleccion")), p).Compile();

        int pantalla = 0;
        var lecturaT = T("Lectura");
        Func<object> leer = () => N("Lectura", Lista(("Pantalla " + pantalla, "Text"), ("Siguiente", "Button")), new List<string> { "texto " + pantalla });
        var dLeer = Delegado(typeof(Func<>).MakeGenericType(lecturaT), () => leer());
        var dDonde = Delegado(typeof(Func<>).MakeGenericType(T("Ubicacion")), () => Ubicacion(7, "notepad"));
        var dPulsar = DelegadoAccion(typeof(Action<>).MakeGenericType(T("Accionable")), _ => pantalla++);
        var motor = N("Motor", dDonde, dLeer, dDecidir, dPulsar, (Func<bool>)(() => false));
        I(motor, "Objetivo", "llegar", 5);
        Exige(vistos.Count == 2 && vistos[0].Count == 0, $"la primera vuelta no empezó sin historia: {vistos.Count} vueltas");
        Exige(vistos[1].Count == 1 && vistos[1][0].Contains("2) Siguiente (Button)"), $"la segunda vuelta no supo lo pulsado: {string.Join(" | ", vistos.ElementAtOrDefault(1) ?? new())}");
    }

    private static void P443()
    {
        var lista = Lista(("Siete", "Button"), ("Ocho", "Button"));
        string h1 = (string)S("Accionables", "Huella", lista, new List<string> { "La pantalla muestra 0" })!;
        string h2 = (string)S("Accionables", "Huella", lista, new List<string> { "La pantalla muestra 7" })!;
        string h3 = (string)S("Accionables", "Huella", lista, new List<string> { "La pantalla muestra 7" })!;
        Exige(h1 != h2, "la huella no cambió cuando solo cambió el texto de la pantalla");
        Exige(h2 == h3, "la huella cambió sin que cambiara nada");
    }

    private static void P444()
    {
        var delante = new IntPtr(100);
        // (ventana, dueño, clase, proceso igual)
        var encima = new List<(IntPtr, IntPtr, string, bool)>
        {
            (new IntPtr(1), IntPtr.Zero, "Shell_TrayWnd", true),        // la barra de tareas: mismo proceso, de nadie
            (new IntPtr(2), delante, "Xaml_WindowedPopupClass", true),   // el menú de la ventana de delante
            (new IntPtr(3), IntPtr.Zero, "#32768", true),               // un menú clásico: es de quien lo abrió
            (new IntPtr(4), new IntPtr(999), "#32770", true),           // un diálogo de OTRA ventana del mismo proceso
            (new IntPtr(5), delante, "Chrome_WidgetWin_1", false),       // de delante pero de otro proceso: tampoco
        };
        var r = L(S("Emergentes", "Elegir", delante, encima)).Select(o => (IntPtr)o).ToList();
        Exige(r.SequenceEqual(new[] { new IntPtr(2), new IntPtr(3) }), $"emergentes elegidas: {string.Join(",", r)} (se esperaban 2 y 3)");
    }

    // ── Delegados tipados sobre tipos que el contrato solo conoce por nombre ───────────────────

    private static Delegate Delegado(Type t, Func<object?> f) =>
        System.Linq.Expressions.Expression.Lambda(t,
            System.Linq.Expressions.Expression.Convert(
                System.Linq.Expressions.Expression.Invoke(System.Linq.Expressions.Expression.Constant(f)),
                t.GetGenericArguments()[0])).Compile();

    private static Delegate DelegadoDe3(Type t, Func<object?, object?, object?, object?> f)
    {
        var g = t.GetGenericArguments();
        var ps = g.Take(3).Select(System.Linq.Expressions.Expression.Parameter).ToArray();
        var cuerpo = System.Linq.Expressions.Expression.Convert(
            System.Linq.Expressions.Expression.Invoke(System.Linq.Expressions.Expression.Constant(f),
                ps.Select(p => System.Linq.Expressions.Expression.Convert(p, typeof(object)))),
            g[3]);
        return System.Linq.Expressions.Expression.Lambda(t, cuerpo, ps).Compile();
    }

    private static Delegate DelegadoAccion(Type t, Action<object?> f)
    {
        var p = System.Linq.Expressions.Expression.Parameter(t.GetGenericArguments()[0]);
        return System.Linq.Expressions.Expression.Lambda(t,
            System.Linq.Expressions.Expression.Invoke(System.Linq.Expressions.Expression.Constant(f),
                System.Linq.Expressions.Expression.Convert(p, typeof(object))), p).Compile();
    }
}
