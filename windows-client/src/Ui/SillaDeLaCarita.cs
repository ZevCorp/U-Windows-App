namespace U.WindowsClient.Ui;

/// <summary>
/// DÓNDE ESTÁ SENTADA LA CARITA, si lo está. Promesa 272 (spec 031). Puro.
/// </summary>
/// <remarks>
/// UN SOLO VALOR Y NO UN BOOL POR ANFITRIÓN, y no es estilo: con un bool por sitio nada impide que
/// dos digan «la tengo» a la vez, que es exactamente lo que pasó el 2026-09-05 con la carita
/// flotando y la del panel a la vez. Aquí «sentada en X» y «sentada en Y» son el mismo campo, así
/// que sentarla en uno la saca del otro por construcción.
/// </remarks>
public sealed class SillaDeLaCarita
{
    /// <summary>El nombre del anfitrión que la tiene, o <c>null</c> si flota.</summary>
    public string? Donde { get; private set; }

    public bool Ocupada => Donde != null;

    public void Sentar(string anfitrion)
    {
        if (string.IsNullOrWhiteSpace(anfitrion)) throw new ArgumentException("un anfitrión tiene nombre", nameof(anfitrion));
        Donde = anfitrion.Trim();
    }

    public void Levantar() => Donde = null;
}
