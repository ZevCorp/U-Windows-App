using System;
using System.Collections.Generic;

namespace U.WindowsClient.Teach;

/// <summary>Cada cosa que hay que hacer al cerrar una demostración, como paso con nombre.</summary>
public enum Cierre
{
    /// <summary>Publicar lo que la superficie tenga observado y sin emitir (lo tecleado que no viajó).</summary>
    DescargarLoPendiente,

    /// <summary>Soltar el oyente de pasos, la cámara y el vigía.</summary>
    SoltarLaSuperficie,

    /// <summary>Cerrar el mp4 en disco.</summary>
    PararElVideo,

    /// <summary>Escribir la lección: eventos, cuadros, frases.</summary>
    ArmarLaLeccion,

    /// <summary>Cerrar la sesión del grabador contra Graph.</summary>
    PararElGrabador,
}

/// <summary>
/// EL ORDEN EN QUE SE CIERRA UNA DEMOSTRACIÓN. Promesa 187.
/// </summary>
/// <remarks>
/// POR QUÉ ESTO EXISTE COMO PIEZA APARTE (2026-09-07, octava demo). Con eventos COM, SAP publica lo
/// tecleado solo cuando la pantalla VIAJA; una demo que acaba dentro de un formulario no viaja y deja
/// todo sin publicar. Se añadió la descarga… dentro de <c>StopObserving</c>, que corre al parar el
/// grabador, treinta líneas DESPUÉS de que la lección ya esté escrita en disco. La descarga
/// funcionaba y la lección salía vacía igual, que es la peor forma de fallar: parece que funcionó.
///
/// El orden es lo que se puede equivocar en silencio, y por eso vive aquí, en una función que el
/// contrato puede juzgar sin pantalla — el mismo motivo por el que existe
/// <see cref="Piloto.ElRecuerdoQueSeVe.Coreografia"/>. Las dos precedencias que importan:
/// descargar ANTES de soltar la superficie (soltada, el oyente ya no recibe lo descargado) y
/// descargar ANTES de armar la lección (armada, ya no admite nada).
/// </remarks>
public static class ElCierreDeLaDemo
{
    public static IReadOnlyList<Cierre> Orden(bool hayVideo, bool haySuperficieObservada)
    {
        var pasos = new List<Cierre>();
        if (haySuperficieObservada)
        {
            pasos.Add(Cierre.DescargarLoPendiente);
            pasos.Add(Cierre.SoltarLaSuperficie);
        }
        if (hayVideo) pasos.Add(Cierre.PararElVideo);
        pasos.Add(Cierre.ArmarLaLeccion);
        pasos.Add(Cierre.PararElGrabador);
        return pasos;
    }
}
