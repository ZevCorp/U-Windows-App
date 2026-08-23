namespace U.WindowsClient.Navigation;

/// <summary>
/// IR: quién lleva la navegación a una pantalla, el núcleo o el mapa viejo.
/// </summary>
/// <remarks>
/// La quinta y última de las cinco capacidades que la voz usa de verdad: 44 veces en 26 días.
///
/// El CÓMO ya estaba escrito —<see cref="PasoDelNucleo"/>, 181 líneas que hacen lo que el mapa viejo
/// hace en un laberinto— y hasta hoy solo se usaba para destinos que no fueran <c>uia://</c>. Lo que
/// faltaba no era código: era decidir CUÁNDO dejarle el volante.
///
/// Y la decisión no es «núcleo sí o no», porque el núcleo distingue DOS formas de no saber llegar y
/// solo una de ellas es ignorancia:
///
///   · «no hay ningún camino aprendido hasta ahí» — el núcleo no conoce el terreno. El mapa viejo
///     lleva meses recorriéndolo, así que puede saberlo: se le deja intentarlo.
///   · «sé llegar, pero la puerta que hace falta no está en pantalla ahora» — el núcleo SÍ sabe, y
///     lo que falta es destapar algo: desplegar un panel, hacer scroll, volver atrás. Aquí caer al
///     mapa viejo sería tirar información buena y ponerse a buscar a ciegas, cuando la respuesta
///     útil —«haz scroll y vuelve a pedírmelo»— ya la teníamos.
///
/// Fundir las dos en «no pude» es lo que convierte una instrucción accionable en un callejón.
/// </remarks>
public static class IrSegunElNucleo
{
    public enum Quien
    {
        /// <summary>El núcleo sabe el camino y lo recorre él.</summary>
        LoLlevaElNucleo,

        /// <summary>El núcleo no conoce el terreno; que lo intente el mapa viejo.</summary>
        QueLoIntenteElMapaViejo,

        /// <summary>El núcleo sabe llegar pero ahora no se puede, y sabe decir por qué.</summary>
        SeSabeYNoSePuede,
    }

    /// <summary>
    /// Quién debe llevar esto, a partir de lo que contesta <see cref="Nucleo.Grafo.ComoLlego"/>.
    /// </summary>
    public static Quien Decide(bool hayPasoVivo, bool conocidoEnMemoria)
        => hayPasoVivo ? Quien.LoLlevaElNucleo
         : conocidoEnMemoria ? Quien.SeSabeYNoSePuede
         : Quien.QueLoIntenteElMapaViejo;
}
