using Nucleo;

namespace Nucleo.Pruebas;

/// <summary>
/// EL CONTRATO DEL NÚCLEO NUEVO: lo que el grafo promete, escrito como pruebas que llaman al código
/// real y corren sin abrir una ventana.
///
/// Cada promesa de aquí existe porque su ausencia costó un diagnóstico en el núcleo anterior. No se
/// añade ninguna «por si acaso»: una promesa que nadie ha visto fallar es una que nadie sabe leer.
/// </summary>
internal static class Contrato
{
    private static int _fallos;

    private static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("CONTRATO DEL NÚCLEO (el grafo, aislado)\n");

        Prueba("1. lo que se ve queda alcanzable desde donde se vio", LoVistoQueda);
        Prueba("2. observar NO borra: lo de antes sigue, marcado como no vivo", ObservarNoBorra);
        Prueba("3. cruzar guarda a dónde llevó, por ubicación Y selector", CruzarGuardaDestino);
        Prueba("4. el mismo selector puede llevar a sitios distintos según desde dónde", MismoSelectorDosDestinos);
        Prueba("5. el orden de las observaciones no cambia el grafo", ElOrdenNoImporta);
        Prueba("6. lo vivo y lo recordado nunca se confunden", VivoNoEsRecordado);
        Prueba("7. no se inventa nada: sin cruzar, no hay destino", SinCruzarNoHayDestino);

        Console.WriteLine();
        Console.WriteLine(_fallos == 0
            ? "NÚCLEO ÍNTEGRO: el grafo promete lo que dice prometer."
            : $"NÚCLEO ROTO: {_fallos} promesa(s) incumplida(s).");
        return _fallos;
    }

    // ── Las promesas ─────────────────────────────────────────────────────────

    private static void LoVistoQueda(Grafo g)
    {
        g.Observar("app://inicio", new[] { new Elemento("s:catalogo", "Catálogo", "Button") });
        var d = g.DesdeAqui("app://inicio");
        Debe(d.Count == 1 && d[0].Que.Etiqueta == "Catálogo", "lo observado se puede volver a preguntar");
        Debe(d[0].Vivo, "y está vivo, porque se acaba de ver");
        Debe(g.Aqui == "app://inicio", "el grafo sabe dónde está");
    }

    private static void ObservarNoBorra(Grafo g)
    {
        // Un menú abierto y luego cerrado: sus opciones dejan de estar en pantalla, y el núcleo
        // anterior las habría perdido. Olvidar en cuanto algo se oculta vacía el grafo solo.
        g.Observar("app://informes", new[]
        {
            new Elemento("s:exportar", "Exportar", "Button"),
            new Elemento("s:programar", "Programar envío", "MenuItem"),
        });
        g.Observar("app://informes", new[] { new Elemento("s:exportar", "Exportar", "Button") });

        var d = g.DesdeAqui("app://informes");
        Debe(d.Count == 2, "las dos siguen en el grafo: observar no borra");
        Debe(d.Single(x => x.Que.Selector == "s:exportar").Vivo, "la que está en pantalla, viva");
        Debe(!d.Single(x => x.Que.Selector == "s:programar").Vivo,
            "la que ya no está, RECORDADA pero no viva — memoria, no promesa");
    }

    private static void CruzarGuardaDestino(Grafo g)
    {
        g.Observar("app://inicio", new[] { new Elemento("s:catalogo", "Catálogo", "Button") });
        g.Cruzar("app://inicio", "s:catalogo", "app://catalogo");
        Debe(g.DesdeAqui("app://inicio")[0].Destino == "app://catalogo",
            "el destino cruzado queda guardado");
    }

    private static void MismoSelectorDosDestinos(Grafo g)
    {
        // El caso que rompía el núcleo anterior: «Atrás» lleva a un sitio distinto según de dónde
        // vengas. Guardarlo solo por selector obligaba a inventar el concepto de «relativa».
        var atras = new Elemento("s:atras", "Atrás", "Button");
        g.Observar("app://a", new[] { atras });
        g.Observar("app://b", new[] { atras });
        g.Cruzar("app://a", "s:atras", "app://inicio");
        g.Cruzar("app://b", "s:atras", "app://catalogo");

        Debe(g.DesdeAqui("app://a")[0].Destino == "app://inicio", "desde A lleva a inicio");
        Debe(g.DesdeAqui("app://b")[0].Destino == "app://catalogo",
            "…y desde B a catálogo, sin que uno pise al otro");
    }

    private static void ElOrdenNoImporta(Grafo g)
    {
        var uno = new Elemento("s:1", "Uno", "Button");
        var dos = new Elemento("s:2", "Dos", "Button");

        g.Observar("app://x", new[] { uno, dos });
        g.Cruzar("app://x", "s:1", "app://y");
        string primero = Retrato(g);

        var otro = new Grafo();
        otro.Observar("app://x", new[] { dos, uno });   // el mismo mundo, visto al revés
        otro.Cruzar("app://x", "s:1", "app://y");

        Debe(Retrato(otro) == primero, "el mismo mundo da el mismo grafo aunque se recorra en otro orden");
    }

    private static void VivoNoEsRecordado(Grafo g)
    {
        var e = new Elemento("s:menu", "Más opciones", "MenuItem");
        g.Observar("app://p", new[] { e });
        Debe(g.DesdeAqui("app://p")[0].Vivo, "recién visto: vivo");

        g.Observar("app://p", Array.Empty<Elemento>());
        Debe(!g.DesdeAqui("app://p")[0].Vivo, "ya no está en pantalla: deja de estar vivo…");
        Debe(g.DesdeAqui("app://p").Count == 1, "…pero no desaparece del grafo");
    }

    private static void SinCruzarNoHayDestino(Grafo g)
    {
        g.Observar("app://z", new[] { new Elemento("s:misterio", "Misterio", "Button") });
        Debe(g.DesdeAqui("app://z")[0].Destino.Length == 0,
            "una puerta sin cruzar no tiene destino, y decirlo vacío es la respuesta honesta");
        // Y no se acepta un destino inventado: cruzar a donde ya estás no es cruzar.
        g.Cruzar("app://z", "s:misterio", "app://z");
        Debe(g.DesdeAqui("app://z")[0].Destino.Length == 0, "cruzar a uno mismo no acuña nada");
    }

    // ── El arnés ─────────────────────────────────────────────────────────────

    /// <summary>Una huella del grafo entero, estable, para comparar dos grafos.</summary>
    private static string Retrato(Grafo g) =>
        string.Join("\n", g.Ubicaciones().Select(u =>
            u + " => " + string.Join(", ", g.DesdeAqui(u)
                .OrderBy(a => a.Que.Selector, StringComparer.Ordinal)
                .Select(a => $"{a.Que.Selector}[{(a.Vivo ? "vivo" : "memoria")}]->{a.Destino}"))));

    private static void Prueba(string nombre, Action<Grafo> cuerpo)
    {
        int antes = _fallos;
        try { cuerpo(new Grafo()); }
        catch (Exception e)
        {
            _fallos++;
            for (var x = e; x != null; x = x.InnerException)
                Console.WriteLine($"   ✘ {x.GetType().Name}: {x.Message}");
        }
        Console.WriteLine($"{(_fallos == antes ? "✔" : "✘")} {nombre}");
    }

    private static void Debe(bool condicion, string promesa)
    {
        if (condicion) return;
        _fallos++;
        Console.WriteLine($"   ✘ {promesa}");
    }
}
