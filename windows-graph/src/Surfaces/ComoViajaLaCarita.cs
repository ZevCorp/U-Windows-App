namespace U.Graph.Surfaces;

/// <summary>
/// CÓMO VIAJA LA CARITA HASTA EL CLIC: qué avisa, cuándo merece la pena moverse, cuánto dura el viaje
/// y con qué curva. Promesa 240 (spec 022). Pura.
/// </summary>
/// <remarks>
/// LA CARITA DEJÓ DE ACOMPAÑAR A LA MANO JUSTO CUANDO LA MANO MEJORÓ. Hasta la spec 020 casi todos los
/// clics movían el ratón, y la carita seguía al cursor (<c>UiaSurface.CursorMoved</c>); desde que se
/// pulsa por patrón y por mensaje, el ratón no se mueve y la carita se queda plantada mientras las
/// cosas se abren solas. El dueño, el 2026-09-14: «la carita ya es muy buena haciendo clics sin el
/// mouse, y quiero que siempre que se haga un clic la carita se mueva hasta allá».
///
/// LA CURVA ES LA MITAD DEL PEDIDO, y está dicha con esas palabras: «un movimiento fluido, no rápido y
/// brusco, sino fluido rápidamente, con una curva de aceleración suave pero rápida». Eso es un muelle
/// crítico: arranca parado y va acelerando —no da el tirón de una ease-out—, hace el grueso del camino
/// enseguida y se posa sin pasarse. Sin rebote a propósito: el rebote del lanzamiento se lee como peso
/// una vez, y en cada clic se leería como gelatina.
/// </remarks>
public static class ComoViajaLaCarita
{
    /// <summary>¿Esta acción de la mano es un clic? Escribir o elegir no mueve a nadie.</summary>
    public static bool EsClic(string actionType) => (actionType ?? "").Trim().ToLowerInvariant() switch
    {
        "click" or "doubleclick" or "rightclick" => true,
        _ => false,
    };

    /// <summary>Un salto corto se resuelve apareciendo: animar 12 px es un parpadeo, no un movimiento.</summary>
    public static bool MereceViaje(double distancia) => distancia >= 24;

    /// <summary>
    /// Cuánto dura el viaje. Crece con la distancia y se acota arriba: cruzar una pantalla 4K entera
    /// tiene que seguir cabiendo entre dos pasos de un plan, que van a menos de un segundo.
    /// </summary>
    public static TimeSpan Cuanto(double distancia)
        => TimeSpan.FromMilliseconds(Math.Clamp(150 + Math.Abs(distancia) * 0.18, 150, 430));

    /// <summary>Cuánta rigidez tiene el muelle. Más alto = llega antes y frena más seco.</summary>
    private const double Tiron = 9.0;

    /// <summary>
    /// La curva, de 0 a 1. Muelle CRÍTICO —llega y para, sin rebotar—: <c>1-(1+w·t)·e^(-w·t)</c>,
    /// normalizado para que en t=1 esté exactamente en el destino y no al 99,88%.
    /// </summary>
    public static double Curva(double t)
    {
        double final = Muelle(1);
        return Math.Abs(final) < 1e-9 ? t : Muelle(t) / final;
    }

    private static double Muelle(double t) => 1 - (1 + Tiron * t) * Math.Exp(-Tiron * t);
}
