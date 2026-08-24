using System.Windows;
using System.Windows.Automation;
using System.Runtime.InteropServices;
using U.WindowsClient.Diagnostics;
using U.WindowsClient.Navigation;

namespace U.WindowsClient.Ui;

/// <summary>
/// Por dónde ha pasado el ratón últimamente, y sobre qué.
///
/// Señalar UNA cosa con el cursor ya funcionaba bien, y la forma natural de enseñar VARIAS es la
/// misma repetida: pasar el ratón por encima de ellas seguidas. «Ilumina todo esto que te estoy
/// mostrando» no se responde adivinando una zona de la pantalla —se intentó por bandas de
/// coordenadas y devolvió la barra de título cuando se pedía la columna izquierda (2026-08-05)—,
/// se responde recordando por dónde acaba de pasar la mano de quien habla.
///
/// Se muestrea solo cuando el cursor SE MUEVE: un ratón parado sobre algo no está enseñando nada, y
/// dejarlo entrar llenaría el rastro de repeticiones de lo último que se tocó.
/// </summary>
public static class RastroDelCursor
{
    /// <summary>Algo por lo que pasó el cursor: qué era, dónde estaba y cuándo.</summary>
    public sealed record Marca(string Nombre, string Tipo, Rect Caja, DateTime Cuando);

    /// <summary>Cuánto se guarda. Más allá de esto ya no es «lo que te estoy mostrando».</summary>
    private static readonly TimeSpan Memoria = TimeSpan.FromSeconds(30);

    /// <summary>Cada cuánto se mira. Barrer seis iconos lleva unos tres segundos: a este paso caben
    /// todos sin que ninguno se escape entre dos muestras.</summary>
    private static readonly TimeSpan Paso = TimeSpan.FromMilliseconds(180);

    private static readonly object _llave = new();
    private static readonly List<Marca> _marcas = new();
    private static System.Threading.Timer? _reloj;
    private static POINT _ultimoPunto;

    public static void Arrancar()
    {
        if (_reloj != null) return;
        _reloj = new System.Threading.Timer(_ => Mirar(), null, Paso, Paso);
    }

    /// <summary>Lo que se ha señalado en los últimos <paramref name="segundos"/>, en orden de paso.</summary>
    public static IReadOnlyList<Marca> Ultimas(double segundos)
    {
        var desde = DateTime.UtcNow - TimeSpan.FromSeconds(Math.Max(0.5, segundos));
        lock (_llave) return _marcas.Where(m => m.Cuando >= desde).ToList();
    }

    /// <summary>Se vacía cuando lo mostrado ya se usó, para que la siguiente pregunta empiece limpia.</summary>
    public static void Olvidar()
    {
        lock (_llave) _marcas.Clear();
    }

    private static void Mirar()
    {
        try
        {
            if (!GetCursorPos(out var p)) return;

            // Quieto no es enseñar. Y un temblor de un píxel tampoco.
            if (Math.Abs(p.X - _ultimoPunto.X) < 3 && Math.Abs(p.Y - _ultimoPunto.Y) < 3) return;
            _ultimoPunto = p;

            var el = AutomationElement.FromPoint(new Point(p.X, p.Y));
            if (el == null) return;

            // Lo NUESTRO no cuenta: al ir a hablar, el cursor cruza por encima de la carita, y
            // apuntarla haría que el asistente creyera que se le está enseñando a sí mismo.
            try
            {
                int pid = el.Current.ProcessId;
                if (pid == Environment.ProcessId) return;
            }
            catch { }

            string nombre = "";
            string tipo = "";
            Rect caja = Rect.Empty;
            try
            {
                nombre = (el.Current.Name ?? "").Trim();
                tipo = el.Current.ControlType.ProgrammaticName.Replace("ControlType.", "");
                caja = el.Current.BoundingRectangle;
            }
            catch { return; }

            // Sin nombre se sube, igual que hace map_pointing_at: un contenedor anónimo no es lo que
            // alguien está enseñando, pero lo que lo contiene suele serlo.
            var origen = el;
            for (int i = 0; i < 4 && nombre.Length == 0; i++)
            {
                try
                {
                    var arriba = TreeWalker.ControlViewWalker.GetParent(el);
                    if (arriba == null) break;
                    el = arriba;
                    nombre = (el.Current.Name ?? "").Trim();
                    tipo = el.Current.ControlType.ProgrammaticName.Replace("ControlType.", "");
                    caja = el.Current.BoundingRectangle;
                }
                catch { break; }
            }

            // Y SI ARRIBA NO HAY NOMBRE, SE MIRA ABAJO — la misma razón por la que map_pointing_at
            // (LoQueSenala, en SurfaceMapTools) lo necesita: la barra de tareas de Windows 11 es
            // XAML, `FromPoint` cae en un Pane sin nombre cuyo padre TAMPOCO lo tiene, y el nombre
            // real vive en un DESCENDIENTE. Sin este paso, pasar el ratón por la barra de tareas no
            // dejaba NINGUNA marca — «no has pasado el ratón por encima de nada» sobre una barra
            // llena de iconos (2026-08-24, reproducido con map_pointed_trail). Se parte del elemento
            // ORIGINAL bajo el cursor, no del que quedó tras subir: subir ya fracasó, bajar desde ahí
            // solo repetiría el mismo callejón sin salida.
            if (nombre.Length == 0)
            {
                var candidatos = new List<LoQueSenalas.Candidato>();
                try
                {
                    foreach (AutomationElement d in origen.FindAll(TreeScope.Descendants,
                        System.Windows.Automation.Condition.TrueCondition))
                    {
                        try
                        {
                            candidatos.Add(new LoQueSenalas.Candidato(
                                (d.Current.Name ?? "").Trim(),
                                d.Current.ControlType.ProgrammaticName.Replace("ControlType.", ""),
                                d.Current.BoundingRectangle));
                        }
                        catch { }
                    }
                }
                catch { }

                if (LoQueSenalas.Elegir(candidatos, new Point(p.X, p.Y)) is { } elegido)
                {
                    nombre = elegido.Nombre; tipo = elegido.Tipo; caja = elegido.Caja;
                }
            }

            if (nombre.Length == 0 || caja.IsEmpty || caja.Width < 1 || caja.Height < 1) return;

            // Una ventana entera no es «un elemento»: al mover el ratón entre dos iconos se pasa por
            // el fondo, y apuntar el fondo convertiría el rastro en «toda la pantalla».
            if (caja.Width > 1200 && caja.Height > 700) return;

            lock (_llave)
            {
                var ahora = DateTime.UtcNow;
                _marcas.RemoveAll(m => ahora - m.Cuando > Memoria);

                // Lo mismo dos veces seguidas es un solo gesto: se refresca la hora y no se duplica.
                var yaEsta = _marcas.FindIndex(m =>
                    m.Nombre.Equals(nombre, StringComparison.OrdinalIgnoreCase) && m.Caja == caja);
                if (yaEsta >= 0) _marcas[yaEsta] = _marcas[yaEsta] with { Cuando = ahora };
                else
                {
                    _marcas.Add(new Marca(nombre, tipo, caja, ahora));
                    LogBus.Log("rastro", $"marca: «{nombre}» ({tipo})");
                }
            }
        }
        catch { /* el árbol de UI se mueve bajo los pies: se mira otra vez dentro de 180 ms */ }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT p);
}
