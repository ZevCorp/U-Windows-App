using System.Windows;
using System.Windows.Controls;

namespace U.WindowsClient.Ui;

/// <summary>
/// ALGO DONDE LA CARITA SE PUEDE SENTAR. Promesa 272 (spec 031).
/// </summary>
/// <remarks>
/// HASTA EL 2026-09-17 HABÍA UN SOLO ESCONDITE, el muelle, y <see cref="FaceWindow"/> hablaba con él
/// por su nombre: <c>_muelle.Caja</c>, <c>_muelle.Guardando</c>, y la silla cableada dentro de su
/// panel. El dueño pidió sentarla también en el centro de la consulta —que pasa a ser el centro de
/// operaciones—, y copiar el código del muelle habría dejado dos sitios que deciden lo mismo de
/// formas que se separan con el tiempo (aprendizaje nº16). Esto es lo que un anfitrión tiene que
/// dar, y nada más: dónde está, una señal de «la tengo», y un hueco donde sentarla.
///
/// LA SILLA ES UNA Y SE MUDA. El <c>FaceControl</c> que hace de carita sentada sale del hueco de un
/// anfitrión y entra en el del otro, como ya se muda <c>RootPanel</c> entero al muelle. No hay una
/// segunda copia que sincronizar: dos caras a la vez ya mintieron una vez (2026-09-05).
/// </remarks>
public interface AnfitrionDeLaCarita
{
    /// <summary>Cómo se llama para el log y para la silla: «muelle», «consulta».</summary>
    string Nombre { get; }

    /// <summary>La caja que ocupa AHORA en coordenadas de pantalla, o <see cref="Rect.Empty"/> si no se ve.</summary>
    Rect Caja { get; }

    /// <summary>Tiene la carita dentro. Cada anfitrión lo enseña a su manera.</summary>
    bool Guardando { get; set; }

    /// <summary>Donde se sienta la carita: el anfitrión lo tiene vacío hasta que ella llega.</summary>
    Decorator Hueco { get; }

    /// <summary>La ventana que lo aloja: si se cierra con la carita dentro, la carita vuelve a flotar.</summary>
    Window Ventana { get; }
}
