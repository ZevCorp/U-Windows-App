namespace U.WindowsClient.Ui;

/// <summary>Lo que la mano hace sobre la carita. Los umbrales que separan uno de otro (13 px,
/// 250 ms, 750 ms) viven en <see cref="FaceGestures"/> y son los mismos que en Android y en Mac.</summary>
public enum Gesto
{
    /// <summary>Un toque: pulsar y soltar sin moverse, y no llega un segundo en 250 ms.</summary>
    Toque,
    /// <summary>Dos toques seguidos.</summary>
    DobleToque,
    /// <summary>Mantener oprimida 750 ms sin moverse.</summary>
    Mantener,
    /// <summary>Clic con el botón derecho.</summary>
    ClicDerecho,
    /// <summary>Arrastrar más de 13 px.</summary>
    Arrastre,
}

/// <summary>Lo que Ü entiende que se le pide con ese gesto.</summary>
public enum Intencion
{
    /// <summary>Abrir (o colgar) la conversación por voz.</summary>
    Hablar,
    /// <summary>Pasar de la carita suelta a la barra, o al revés.</summary>
    AlternarBarra,
    /// <summary>El anillo de acciones alrededor de la carita.</summary>
    Anillo,
    /// <summary>Mover la carita; al soltar se va a un borde.</summary>
    Mover,
}

/// <summary>
/// EL MAPA DE GESTOS de la carita, separado del cableado de WPF para que el contrato lo juzgue sin
/// ratón (promesa 113, spec 008). <c>WireFaceGestures</c> no decide nada: pregunta aquí y ejecuta.
/// </summary>
/// <remarks>
/// Hasta el 2026-09-02 un toque abría la barra, el doble toque el micrófono y mantener cambiaba
/// de tema; y hablarle —lo que más se hace— estaba detrás de dos gestos que había que saberse o
/// de una pastilla de 4,5 px. El principio de este mapa: **lo más frecuente cuesta un solo gesto,
/// y lo demás tiene siempre una entrada que no hay que saberse.**
///
/// - **Un toque habla.** Es lo primero que se prueba sobre cualquier cosa, y es lo que más se hace.
/// - **Dos toques abren la barra.** Doble clic es «abrir» en todo Windows.
/// - **Mantener y clic derecho abren el MISMO anillo.** Mantener es el gesto de la burbuja de
///   Android; el clic derecho es el «más» de Windows, y existe aquí porque —como dice el
///   interruptor del Mac— *un gesto que hay que saber no es un botón, es un secreto*.
/// - **Arrastrar mueve**, como siempre.
///
/// El tema claro/oscuro, que era lo que hacía mantener, no merece un gesto principal: vive en el
/// panel (🌓).
/// </remarks>
public static class ReglaDeGestos
{
    public static Intencion Decidir(Gesto gesto) => gesto switch
    {
        Gesto.Toque => Intencion.Hablar,
        Gesto.DobleToque => Intencion.AlternarBarra,
        Gesto.Mantener => Intencion.Anillo,
        Gesto.ClicDerecho => Intencion.Anillo,
        Gesto.Arrastre => Intencion.Mover,
        _ => throw new ArgumentOutOfRangeException(nameof(gesto), gesto, "un gesto sin intención"),
    };
}
