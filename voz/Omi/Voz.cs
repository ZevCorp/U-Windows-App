namespace Omi;

/// <summary>
/// El ancla del ensamblado, y de momento nada más.
///
/// Existe para que el contrato pueda alcanzar este ensamblado por reflexión mientras las capacidades
/// de verdad —<c>Codec</c>, <c>Trama</c>, <c>Reposicion</c>, <c>Fuente</c>, <c>Relevo</c>— todavía no
/// están escritas. Las promesas se escriben ANTES que su código (regla del flujo dirigido por
/// especificación), así que en la fase 0 este proyecto compila y no promete nada: las cinco promesas
/// salen rojas diciendo qué capacidad falta y en qué fase llega.
///
/// Cuando las cinco estén verdes, esta clase se puede borrar si ya hay otro tipo público que sirva
/// de ancla. Hasta entonces, borrarla deja al contrato sin poder cargar el ensamblado — y un juez
/// que no puede correr no dice «no sé», dice «culpable» (aprendizaje nº17).
/// </summary>
public static class Voz
{
}
