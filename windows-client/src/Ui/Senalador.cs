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
    /// Señalar CADUCA. Un señalamiento sin final deja el recuadro encendido y los ojos torcidos para
    /// siempre —que fue justo lo que pasó (2026-08-05)— y entonces deja de significar nada: si
    /// siempre está señalando, no está señalando.
    ///
    /// Cinco segundos: lo que dura mirar algo que te acaban de indicar. Y se suelta antes si el
    /// asistente hace cualquier otra cosa, porque ya no está mirando eso.
    /// </summary>
    private static readonly TimeSpan Duracion = TimeSpan.FromSeconds(5);
    private static System.Threading.Timer? _caducidad;

    public static void Senalar(Rect caja, string que) => SenalarVarias(new[] { caja }, que);

    /// <summary>Señala varias cajas. La primera manda: es donde se pone la carita.</summary>
    public static void SenalarVarias(IReadOnlyList<Rect> cajas, string que)
    {
        if (cajas.Count == 0) { Soltar(); return; }
        Actual = (cajas[0], que);
        SenalaVarias?.Invoke(cajas);
        Senala?.Invoke(cajas[0], que);

        _caducidad?.Dispose();
        _caducidad = new System.Threading.Timer(_ => Soltar(), null,
            (int)Duracion.TotalMilliseconds, System.Threading.Timeout.Infinite);
    }

    public static void Soltar()
    {
        _caducidad?.Dispose();
        _caducidad = null;
        if (Actual == null) return;   // ya estaba suelto: no se avisa dos veces
        Actual = null;
        Suelta?.Invoke();
    }
}
