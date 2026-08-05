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
    /// <summary>Se está señalando algo: su caja en pantalla y cómo se llama.</summary>
    public static event Action<Rect, string>? Senala;

    /// <summary>Se dejó de señalar.</summary>
    public static event Action? Suelta;

    /// <summary>Lo último señalado, para quien se enganche tarde.</summary>
    public static (Rect Caja, string Que)? Actual { get; private set; }

    public static void Senalar(Rect caja, string que)
    {
        Actual = (caja, que);
        Senala?.Invoke(caja, que);
    }

    public static void Soltar()
    {
        Actual = null;
        Suelta?.Invoke();
    }
}
