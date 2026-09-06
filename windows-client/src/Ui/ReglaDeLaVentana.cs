namespace U.WindowsClient.Ui;

/// <summary>Lo que hace el botón de una ventana según cómo esté esa ventana.</summary>
public enum QueHacerConLaVentana
{
    /// <summary>No existe todavía: crearla.</summary>
    Abrir,

    /// <summary>Existe pero no está delante —minimizada, escondida o detrás—: traerla.</summary>
    TraerAlFrente,

    /// <summary>Existe, se ve y tiene el foco: el botón la quita de en medio.</summary>
    Ocultar,
}

/// <summary>
/// UN BOTÓN QUE ABRE UNA VENTANA TIENE QUE PODER CERRARLA.
/// </summary>
/// <remarks>
/// POR QUÉ EXISTE (2026-09-06, lo pidió el dueño probándolo). El botón del collar abría la ventana
/// de análisis clínico, y con ella MINIMIZADA no hacía nada: la ventana existe y es «visible» para
/// Windows —minimizada no es oculta—, así que <c>Activate()</c> sobre ella no la levanta. El
/// resultado era el peor de los posibles: pulsas y no pasa nada, así que pulsas otra vez.
///
/// Y AL REVÉS TAMBIÉN. Con la ventana delante, el mismo botón la esconde. Un botón que solo sabe
/// abrir obliga a ir a buscar la equis; uno que alterna se aprende de una vez y sirve para las dos
/// direcciones — es lo mismo que ya hace la pastilla de chat de la carita.
///
/// «Enfocada» y «visible» son cosas distintas y aquí las dos hacen falta: una ventana visible pero
/// detrás de SAP no se esconde al pulsar, se TRAE. Esconder lo que el usuario no está viendo sería
/// hacer desaparecer algo a sus espaldas.
/// </remarks>
public static class ReglaDeLaVentana
{
    /// <param name="existe">Ya hay una ventana de ésas abierta.</param>
    /// <param name="alFrente">Se está viendo Y tiene el foco: es con lo que la persona trabaja ahora.</param>
    public static QueHacerConLaVentana AlPulsarSuBoton(bool existe, bool alFrente)
    {
        if (!existe) return QueHacerConLaVentana.Abrir;
        return alFrente ? QueHacerConLaVentana.Ocultar : QueHacerConLaVentana.TraerAlFrente;
    }
}
