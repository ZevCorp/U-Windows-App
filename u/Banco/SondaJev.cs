using U.Ciclo;

// LA SONDA DEL HISTORIAL (2026-09-25, 06:10): en la Calculadora científica Jev pulsa «42) Cuatro» cuando toca «Uno»,
// siempre justo después de un historial que empieza por «pulsé «42) Cuatro»». Hipótesis: el número del historial le
// empuja a repetir el id. Se pregunta a Jev con el MISMO estado, con y sin el número en lo ya hecho, N veces cada uno.
// Solo lectura: Jev contesta, nadie pulsa nada.
internal static class SondaJev
{
    public static int Correr(ClienteJev jev, int veces)
    {
        string[] nombres =
        {
            "Minimizar Calculadora", "Maximizar Calculadora", "Cerrar Calculadora", "Abrir navegación", "Alternar grados", "Notación científica",
            "Sumar memoria", "Restar memoria", "Almacén de memoria", "Trigonometría", "Trigonometría", "Funciones", "Funciones", "Función inversa",
            "Pi", "Número de Euler", "Borrar", "Retroceso", "Cuadrado", "Raíz cuadrada", "'X' elevado al exponente", "Diez elevado al exponente",
            "Logaritmo", "Logaritmo natural", "Recíproco", "Valor absoluto", "Exponencial", "Módulo", "Paréntesis de apertura", "Paréntesis de cierre",
            "Factorial", "Dividir por", "Multiplicar por", "Menos", "Más", "Es igual a", "Positivo negativo", "Cero", "Uno", "Dos", "Tres", "Cuatro",
            "Cinco", "Seis", "Siete", "Ocho", "Nueve", "Separador decimal",
        };
        var lista = Accionables.Numerar(nombres.Select((n, i) => new Crudo(n, i is 9 or 11 ? "ListItem" : "Button", new Caja(i * 10, 10, 40, 20), true, false)));
        string Id(string n) => lista.First(a => a.Nombre == n).Id;
        var conNumero = new[] { $"pulsé «{Id("Cuatro")}»", $"pulsé «{Id("Cinco")}»", $"pulsé «{Id("Multiplicar por")}»" };
        var sinNumero = new[] { "pulsé «Cuatro»", "pulsé «Cinco»", "pulsé «Multiplicar por»" };
        var textos = new[] { "Calculadora", "La pantalla muestra 45 ×", "Modo calculadora Científica" };

        foreach (var (nombre, hecho) in new[] { ("con número", conNumero), ("sin número", sinNumero) })
        {
            var elegidas = new Dictionary<string, int>();
            for (int i = 0; i < veces; i++)
            {
                var e = jev.Decidir(new Contexto("ApplicationFrameHost · Calculadora", "calcular 45 por 12", lista, textos, hecho));
                string k = e.Numero > 0 ? lista.First(a => a.Numero == e.Numero).Nombre : "(no pulsa: " + e.Porque + ")";
                elegidas[k] = elegidas.GetValueOrDefault(k) + 1;
            }
            Console.WriteLine($"{nombre}: " + string.Join(" · ", elegidas.OrderByDescending(x => x.Value).Select(x => $"{x.Key} ×{x.Value}")));
        }
        return 0;
    }
}
