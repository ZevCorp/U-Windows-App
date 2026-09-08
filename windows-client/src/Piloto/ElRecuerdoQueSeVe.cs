namespace U.WindowsClient.Piloto;

/// <summary>
/// LA COREOGRAFÍA DE CADA PASO DEL PLAN: lo que la persona VE mientras Ü comprueba. Promesa 180 (spec 014).
/// </summary>
/// <remarks>
/// LO QUE PIDIÓ EL DUEÑO (2026-09-07, tras la quinta prueba): «que la carita se mueva al lado del
/// elemento señalando, ilumine el elemento, escriba el aprendizaje, deje de señalarlo, se vea el
/// recuerdo y continúe». Hasta hoy el plan colgaba el recuerdo y daba el paso sin que nadie viera
/// nada: la comprobación era correcta y era invisible, y una enseñanza que no se ve no se puede
/// corregir a tiempo.
///
/// EL ORDEN ES LA PROMESA: primero se señala (la carita va al lado y el elemento se enciende),
/// después se dice y se escribe el recuerdo, después la tarjeta se queda a la vista un tiempo que
/// dé para leerla, y SOLO ENTONCES se toca. Al tocar, la tarjeta se cierra y la señal se suelta.
/// Tocar antes de mostrar sería enseñar el recuerdo de una pantalla que ya no está.
///
/// SIN ELEMENTO EN PANTALLA NO HAY ESPECTÁCULO: si el elemento no se pudo señalar, no se muestra
/// tarjeta sobre la nada; se dice y se pasa al paso, que el ejecutor de tanda juzgará por su
/// cuenta. Una tarjeta flotando en una esquina es la caja que miente (patrón nº8).
///
/// PURO: solo decide el orden y el tiempo. Los efectos —carita, recuadro, tarjeta, voz, paso— los
/// ejecuta la ventana, en ese orden.
/// </remarks>
public static class ElRecuerdoQueSeVe
{
    public enum Gesto { Senalar, Decir, Escribir, Mostrar, Esperar, Actuar, Cerrar, Soltar }

    /// <summary>Lo que se hace, en orden, con un paso del plan.</summary>
    public static IReadOnlyList<Gesto> Coreografia(bool hayElemento, bool hayRecuerdo, bool hayDecir)
    {
        var g = new List<Gesto>();
        if (hayElemento) g.Add(Gesto.Senalar);
        if (hayDecir) g.Add(Gesto.Decir);
        if (hayRecuerdo)
        {
            g.Add(Gesto.Escribir);
            if (hayElemento) { g.Add(Gesto.Mostrar); g.Add(Gesto.Esperar); }
        }
        g.Add(Gesto.Actuar);
        if (hayRecuerdo && hayElemento) g.Add(Gesto.Cerrar);
        if (hayElemento) g.Add(Gesto.Soltar);
        return g;
    }

    /// <summary>Cuánto se deja la tarjeta a la vista: lo que tarda leerla, con suelo y techo.</summary>
    /// <remarks>Suelo de 900 ms para que ni un recuerdo de tres palabras parpadee; 25 ms por carácter
    /// (unas 40 palabras por 4 s a ritmo de lectura); techo de 4 s para que un párrafo largo no
    /// detenga la comprobación entera.</remarks>
    public const int SueloMs = 900, TechoMs = 4000, PorCaracterMs = 25;
    public static int TiempoDeLectura(string recuerdo)
        => Math.Clamp(SueloMs + PorCaracterMs * (recuerdo ?? "").Trim().Length, SueloMs, TechoMs);
}
