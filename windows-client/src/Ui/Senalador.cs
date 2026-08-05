using System.Windows;

namespace U.WindowsClient.Ui;

/// <summary>
/// «Estoy mirando ESTO». Un solo sitio donde se dice, y quien quiera reaccionar se apunta.
///
/// Cuando el asistente dice que ve un elemento, hay dos cosas que tienen que pasar a la vez: el
/// recuadro se enciende sobre él y la carita se pone a su lado. Son dos ventanas distintas y sin
/// esto cada una necesitaría una referencia a la otra —o a quien las coordina— y acabaríamos con la
/// misma orden dada dos veces, que es como empiezan a desincronizarse las cosas (2026-08-05).
///
/// La caja va en píxeles FÍSICOS, que es como los da UIA: convertir es cosa de quien pinta, porque
/// solo él sabe en qué monitor y con qué escalado está.
/// </summary>
public static class Senalador
{
    /// <summary>Se está señalando algo: su caja en pantalla y cómo se llama. La primera caja manda
    /// —es donde se pone la carita— y las demás acompañan.</summary>
    public static event Action<Rect, string>? Senala;

    /// <summary>Todas las cajas señaladas, cuando son varias.</summary>
    public static event Action<IReadOnlyList<Rect>>? SenalaVarias;

    /// <summary>Se dejó de señalar.</summary>
    public static event Action? Suelta;

    /// <summary>Lo último señalado, para quien se enganche tarde.</summary>
    public static (Rect Caja, string Que)? Actual { get; private set; }

    /// <summary>
    /// LO QUE ESTÁ MARCADO AHORA, uno por uno y con su nombre.
    ///
    /// Sin esto, señalar era un gesto sin memoria: cada llamada empezaba de cero, así que «excepto
    /// este» no podía significar «quita ese de los que ya tienes» —no había «los que ya tienes»— y
    /// acababa iluminando justo el que se quería excluir (2026-08-05). Una selección que no se
    /// puede retocar no es una selección, es un parpadeo.
    /// </summary>
    public static IReadOnlyList<(Rect Caja, string Que)> Marcadas { get; private set; }
        = Array.Empty<(Rect, string)>();

    /// <summary>
    /// Lo señalado SE QUEDA hasta que el asistente pase a otra cosa.
    ///
    /// Hubo un reloj de cinco segundos y estorbaba: si señala «estos cuatro son del menú principal»
    /// y el usuario se pone a mirarlos, la marca se apaga justo cuando hace falta. Lo que la
    /// convertía en ruido no era que durase, era que no se apagaba NUNCA — y para eso el final
    /// natural no es el tiempo, es que el asistente haga otra llamada: si se puso a otra cosa, ya no
    /// está mirando eso (2026-08-05, corregido por el usuario).
    /// </summary>
    public static void Senalar(Rect caja, string que) => SenalarVarias(new[] { caja }, que);

    /// <summary>Señala varias cajas. La primera manda: es donde se pone la carita.</summary>
    public static void SenalarVarias(IReadOnlyList<Rect> cajas, string que)
        => SenalarVarias(cajas.Select(c => (c, que)).ToList());

    /// <summary>
    /// Señala varias, cada una con SU nombre. La primera manda: es donde se pone la carita.
    ///
    /// Guardar el nombre de cada una es lo que permite decir después «quita la de Música» o
    /// «excepto esta»: para quitar una hay que saber cuál es cuál.
    /// </summary>
    public static void SenalarVarias(IReadOnlyList<(Rect Caja, string Que)> elementos)
    {
        if (elementos.Count == 0) { Soltar(); return; }
        Marcadas = elementos.ToList();
        Actual = (elementos[0].Caja, elementos[0].Que);
        SenalaVarias?.Invoke(elementos.Select(e => e.Caja).ToList());
        Senala?.Invoke(elementos[0].Caja, elementos[0].Que);
    }

    public static void Soltar()
    {
        Marcadas = Array.Empty<(Rect, string)>();
        if (Actual == null) return;   // ya estaba suelto: no se avisa dos veces
        Actual = null;
        Suelta?.Invoke();
    }
}
