using System.Windows.Media;

namespace U.WindowsClient.Ui;

/// <summary>
/// La tabla ÚNICA de colores con significado de la interfaz. Existe porque dos cosas empezaron a
/// pintar estado a la vez —el punto de conexión con Graph y el humor de la carita— y si cada una
/// elige su verde, acabamos con dos verdes que no significan lo mismo.
///
/// La regla la aprendimos caro con el inspector y está escrita en <c>windows-client/CLAUDE.md</c>:
/// <b>ningún color puede parecerse a otro con distinto significado</b>. El amarillo del destello era
/// indistinguible del ámbar de «sin mapear», y durante una tarde pareció que el mapeo se caía en
/// cada clic.
///
/// Esta paleta es de la carita y del panel. La del inspector (cian/violeta/verde de shell) es otra
/// y vive en <see cref="UiInspector"/>: son superficies distintas y no comparten vocabulario.
/// </summary>
public static class UiPalette
{
    /// <summary>Vivo, correcto, respondió. (Graph OK · escuchando)</summary>
    public static readonly Color Vivo = Color.FromRgb(0x2F, 0xB4, 0x57);

    /// <summary>Trabajando. Es el mismo azul que ya usaba la pastilla de actualización.</summary>
    public static readonly Color Trabajando = Color.FromRgb(0x3B, 0x82, 0xF6);

    /// <summary>Atención: algo espera a alguien. (key rechazada · nunca se habló · esperando respuesta)</summary>
    public static readonly Color Atencion = Color.FromRgb(0xFF, 0xA5, 0x1F);

    /// <summary>
    /// Fallo — y también grabando. Es el mismo rojo que ya llevan ⏹ y 🎓 mientras enseñas.
    ///
    /// COLISIÓN DELIBERADA, y por eso está escrita: rojo significa «algo se rompió» y «te estoy
    /// grabando» en la MISMA superficie (la carita). Se distinguen por lo que las acompaña —grabando
    /// va con pose atenta y la frase «Grabando»; el fallo va con ceño, un pulso de entrada y «Se
    /// detuvo»—, nunca por el color a secas. Si algún día hay que separarlos, el que se mueve es
    /// «grabando»: el rojo de peligro es el que no se puede tocar.
    /// </summary>
    public static readonly Color Fallo = Color.FromRgb(0xFF, 0x3B, 0x30);

    /// <summary>Inactivo, sin configurar, sin nada que decir.</summary>
    public static readonly Color Inactivo = Color.FromRgb(0x8A, 0x8A, 0x8E);

    /// <summary>
    /// La lengua, cuando la carita abre la boca al hablar. No es un color de estado: es el único
    /// detalle de la cara que no es tinta, y por eso vive aquí y no suelto en el dibujo — el día que
    /// haya un tema nuevo, los colores de la carita se cambian en un sitio.
    /// </summary>
    public static readonly Color Lengua = Color.FromRgb(0xFF, 0x9A, 0xA5);

    /// <summary>Pinceles congelados, para no crear uno por repintado.</summary>
    public static readonly Brush PincelLengua = Frozen(Lengua);

    public static readonly Brush VivoBrush = Frozen(Vivo);
    public static readonly Brush TrabajandoBrush = Frozen(Trabajando);
    public static readonly Brush AtencionBrush = Frozen(Atencion);
    public static readonly Brush FalloBrush = Frozen(Fallo);
    public static readonly Brush InactivoBrush = Frozen(Inactivo);

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>
    /// Mezcla <paramref name="a"/> hacia <paramref name="b"/> en la proporción dada. La usa la carita
    /// para teñir el relleno: a 42 px un punto de acento no se ve, así que el acento tiene que
    /// cambiar el bloque entero.
    /// </summary>
    public static Color Blend(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));
}
