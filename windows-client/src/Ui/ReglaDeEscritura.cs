namespace U.WindowsClient.Ui;

/// <summary>
/// LA REGLA de «escribirle sin botón»: con el ratón sobre la carita, la primera letra abre el globo
/// con esa letra puesta. Separada del gancho de teclado para que el contrato la juzgue sin teclado
/// (promesa 114, spec 008). El gancho (<see cref="AtajoPorGolpes"/>) no decide nada: pregunta aquí,
/// y solo si esto dice que sí se queda con la tecla.
/// </summary>
/// <remarks>
/// Lo importante no es qué teclas ABREN sino cuáles NO SE ROBAN. El cursor puede estar apoyado en
/// la carita mientras alguien trabaja en SAP, y quitarle un F5, un Ctrl+S o un Enter a esa persona
/// sería peor que no tener el gesto. Así que solo cuenta lo que ESCRIBE —letras, dígitos, signos,
/// el teclado numérico— y sin modificador alguno. Un espacio o un retroceso sueltos tampoco: abrir
/// un globo vacío no dice nada. Y nada de esto vale sin el ratón encima: fuera de la carita, ninguna
/// tecla es para Ü.
///
/// Se juzga por tecla virtual y no por carácter a propósito: el carácter depende de la distribución
/// (es-LA, en-US…) y del Shift, y eso lo traduce Windows al reinyectar la tecla en el globo; aquí
/// solo hay que saber si la tecla es de las que escriben.
/// </remarks>
public static class ReglaDeEscritura
{
    /// <param name="vk">La tecla virtual (VK_*), tal como la ve el gancho de bajo nivel.</param>
    /// <param name="ratonEncima">Si el cursor lleva un momento sobre la carita (o sobre la línea
    /// «Escríbele…» que asoma a su lado).</param>
    /// <param name="ctrl">Ctrl (o Win) pulsado: es un atajo de la app de debajo, no una letra.</param>
    /// <param name="alt">Alt pulsado: ídem.</param>
    public static bool Abre(uint vk, bool ratonEncima, bool ctrl, bool alt)
    {
        if (!ratonEncima) return false;
        if (ctrl || alt) return false;
        return Escribe(vk);
    }

    /// <summary>Las teclas que ponen un carácter: letras, dígitos, el teclado numérico y las OEM
    /// (signos, tildes, la ñ, el «&lt;» de los teclados ISO). Espacio y retroceso quedan fuera a
    /// propósito: no abren un globo vacío.</summary>
    public static bool Escribe(uint vk) =>
        vk is >= 0x30 and <= 0x39     // 0-9
        || vk is >= 0x41 and <= 0x5A  // A-Z
        || vk is >= 0x60 and <= 0x6F  // numérico: dígitos y + - * / . ,
        || vk is >= 0xBA and <= 0xC0  // OEM_1 .. OEM_3 (; = , - . / `)
        || vk is >= 0xDB and <= 0xDF  // OEM_4 .. OEM_8 ([ \ ] ' y la ñ/¡ según distribución)
        || vk == 0xE2;                // OEM_102: el «<>» de los teclados ISO (es-LA, es-ES)
}
