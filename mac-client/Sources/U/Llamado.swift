import Foundation

/// Cómo se llama a Ü.
///
/// Con el micrófono siempre abierto hay dos formas de vivir, y solo una es soportable: o contesta a
/// todo lo que oye —incluida la conversación que tienes con otro, y la tele de fondo—, o **espera a
/// que la llamen por su nombre**. Lo segundo es lo que hace que se pueda dejar encendida todo el día.
///
/// **El nombre se escribe «Ü» y se oye «u», pero el reconocedor casi nunca escribe eso.** Lo que
/// devuelve es «you», «u», «hu», «uh» — depende del acento, del micrófono y de la frase. Comparar
/// contra un solo texto haría que el nombre funcionara unas veces sí y otras no, que es peor que no
/// tener nombre: enseña a desconfiar de él. Por eso se aceptan todas sus formas.
enum Llamado {

    /// Todo lo que el reconocedor escribe cuando alguien dice «Ü» — y «Mira», que es como la llaman
    /// de verdad.
    ///
    /// **«Mira» no es un alias de cortesía: es el nombre que funciona.** Está medido sobre el
    /// registro del 2026-08-18, 822 frases oídas de una sesión real de trabajo:
    ///
    /// | Cómo empezó la frase | Veces |
    /// |---|---|
    /// | alguna forma de «Ü» (`u`, `you`, `hu`…) | **0** |
    /// | «mira» | **6** |
    ///
    /// El nombre «Ü» es una vocal suelta, y una vocal suelta en español hablado se pierde: el
    /// reconocedor la escribe de siete maneras y ninguna cayó nunca al principio de una frase. En
    /// todo el historial despertó tres veces, y las tres por accidente («Hola You me escuchas»,
    /// «You You», «You're broke»). Un nombre que responde 3 de 822 no es un nombre, es una lotería.
    ///
    /// EL PRECIO, dicho por delante: «mira» también es una palabra corriente, y dicha al principio de
    /// una frase que va para otra persona («mira, te cuento…») la va a despertar. Se acepta a
    /// propósito y no se filtra por lo que venga detrás — se probó contra las 6 llamadas reales y un
    /// filtro por la palabra siguiente tiraba dos de ellas («Mira acá pues…», «Mira una de las…»),
    /// o sea que rechazaba llamadas de verdad para evitar interrupciones que duran 30 segundos y se
    /// cortan solas. Anotado en PENDIENTES.md por si en el uso diario cansa.
    private static let nombre: Set<String> = [
        "u", "ú", "you", "yu", "hu", "uh", "uu", "úe", "ue", "hoo", "who",
        "mira", "mire",
    ]

    /// Palabras que pueden ir DELANTE del nombre sin cambiar que es una llamada.
    private static let antesala: Set<String> = ["oye", "oiga", "hey", "ey", "eh", "hola", "ola"]

    /// Si esto es una llamada, devuelve lo que se dijo DESPUÉS del nombre.
    ///
    /// Devolver el resto es lo que permite decir «Ü, abre el correo» de un tirón, en vez de tener que
    /// llamarla, esperar a que despierte, y recién ahí pedirle algo. Nadie habla así con nadie.
    /// Cadena vacía = la llamaron y ya («¿Ü?»); `nil` = no la estaban llamando.
    static func resto(de texto: String) -> String? {
        let palabras = normalizar(texto).split(separator: " ").map(String.init)
        guard !palabras.isEmpty else { return nil }

        var i = 0
        // Se salta un «oye» o un «hola» de cortesía, si lo hay.
        if palabras.count > 1, antesala.contains(palabras[0]) { i = 1 }
        guard i < palabras.count, nombre.contains(palabras[i]) else { return nil }

        let cola = palabras[(i + 1)...].joined(separator: " ")
        // Se devuelve el resto del texto ORIGINAL, no el normalizado: al modelo hay que darle lo que
        // dijiste, con sus tildes y su puntuación, no la versión aplastada que sirve para comparar.
        guard !cola.isEmpty else { return "" }
        return recortarOriginal(texto, saltando: i + 1)
    }

    /// Cómo queda el texto justo antes de compararlo. Se expone para poder VERLO en el registro:
    /// cuando el nombre no responde, la pregunta siempre es «¿contra qué se está comparando?», y sin
    /// esto se contesta adivinando.
    static func comoLoVeo(_ t: String) -> String { normalizar(t) }

    private static func normalizar(_ t: String) -> String {
        t.folding(options: [.diacriticInsensitive, .caseInsensitive], locale: Locale(identifier: "es"))
            .components(separatedBy: CharacterSet.alphanumerics.inverted)
            .filter { !$0.isEmpty }
            .joined(separator: " ")
    }

    /// El texto original a partir de la palabra número `saltando`, conservando tildes y signos.
    private static func recortarOriginal(_ texto: String, saltando: Int) -> String {
        var quedan = saltando
        var i = texto.startIndex
        var enPalabra = false
        while i < texto.endIndex, quedan > 0 {
            let esLetra = texto[i].isLetter || texto[i].isNumber
            if esLetra { enPalabra = true }
            else if enPalabra { enPalabra = false; quedan -= 1 }
            i = texto.index(after: i)
        }
        return String(texto[i...])
            .trimmingCharacters(in: CharacterSet(charactersIn: " ,.;:¿?¡!—-"))
    }
}
