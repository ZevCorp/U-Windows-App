namespace U.WindowsClient.Ui;

/// <summary>
/// CUÁNTO ESPERA UN TOQUE ANTES DE CONTAR COMO SIMPLE.
/// </summary>
/// <remarks>
/// Vive separada del gesto por la misma razón que <see cref="ReglaDelAura"/> vive separada del
/// dibujo: el contrato juzga sin pantalla, y <see cref="FaceGestures"/> pregunta a ESTA función en
/// vez de llevar su propia constante. Dos copias del mismo número acaban discrepando, y la forma de
/// fallo está contada en el aprendizaje nº16 del CLAUDE.md.
///
/// POR QUÉ EL CERO IMPORTA (2026-09-05, spec 010). La espera existía siempre, valiera para algo o
/// no: 250 ms parado por si llegaba un segundo toque. Mientras el doble toque abría el micrófono
/// era el precio justo. Al pasar el micrófono AL CLIC SIMPLE, ese mismo cuarto de segundo se
/// convierte en el retardo del gesto más usado de la aplicación — y un botón de encender que tarda
/// en encender no se lee como lento, se lee como roto.
///
/// La espera no se borró: se condicionó. Donde alguien vuelva a cablear un doble toque, vuelve sola.
/// </remarks>
public static class ReglaDelToque
{
    /// <summary>
    /// La ventana para distinguir un toque de dos.
    ///
    /// 250 ms es lo que había y se conserva a propósito: por debajo de ~200 el doble clic deja de
    /// poderse hacer a voluntad, y el número que se ajusta a ojo es el que rompe a quien tiene el
    /// pulso menos firme.
    /// </summary>
    public const int VentanaDelDobleToqueMs = 250;

    /// <summary>
    /// Cuánto hay que esperar tras levantar el dedo antes de dar el toque por simple.
    /// </summary>
    /// <param name="hayDobleToque">Si este gesto tiene cableado un doble toque que distinguir.</param>
    public static int EsperaMs(bool hayDobleToque) => hayDobleToque ? VentanaDelDobleToqueMs : 0;
}
