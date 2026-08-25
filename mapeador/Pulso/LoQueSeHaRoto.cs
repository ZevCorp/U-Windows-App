namespace Mapeador;

/// <summary>
/// LO QUE SE HA ROTO, CONTADO. Recoge los errores del terreno y sabe decir cuál falla más.
/// </summary>
/// <remarks>
/// SEPARADO DE QUIEN LOS DETECTA a propósito. Los siete sitios que descubren estos fallos están
/// repartidos por el mapeador, el navegador y las herramientas, y cada uno sabe solo de lo suyo;
/// juntarlos aquí es lo que permite contestar «¿qué falla más, y en qué app?», que es la pregunta
/// por la que se instala esto en los equipos de los médicos.
///
/// NO ESCRIBE EN DISCO NI EN NINGÚN SITIO. Guarda en memoria y avisa a quien quiera escuchar. Quien
/// lo mande al log, al backend o a un recorte de vídeo se engancha a <see cref="Ocurrio"/> — y así
/// esta clase se sigue pudiendo juzgar sin pantalla, sin red y sin base de datos.
///
/// SE OLVIDA LO VIEJO. Un turno de urgencias son doce horas y esto no puede crecer sin fin: se
/// queda con los últimos, que son los que se van a mirar. Lo que importa a largo plazo ya viajó por
/// el aviso a quien lo persista.
/// </remarks>
public sealed class LoQueSeHaRoto
{
    /// <summary>Cuántos se guardan. Suficiente para una sesión larga de pruebas sin ocupar memoria.</summary>
    private const int Caben = 500;

    private readonly object _llave = new();
    private readonly List<ErrorDelTerreno> _errores = new();

    /// <summary>Acaba de romperse algo. Quien escriba o grabe se engancha aquí.</summary>
    public event Action<ErrorDelTerreno>? Ocurrio;

    /// <summary>Se anota un error. Devuelve el error para poder encadenarlo.</summary>
    public ErrorDelTerreno Anotar(QueSeRompio que, string donde, string detalle)
    {
        var e = new ErrorDelTerreno(que, donde, detalle, DateTime.UtcNow);
        lock (_llave)
        {
            _errores.Add(e);
            if (_errores.Count > Caben) _errores.RemoveRange(0, _errores.Count - Caben);
        }
        Ocurrio?.Invoke(e);
        return e;
    }

    /// <summary>Todo lo roto, en el orden en que pasó.</summary>
    public IReadOnlyList<ErrorDelTerreno> Todos
    {
        get { lock (_llave) return _errores.ToList(); }
    }

    /// <summary>
    /// El recuento por tipo, de lo que más falla a lo que menos. Es la tabla que se viene a buscar.
    /// </summary>
    public IReadOnlyList<(QueSeRompio Que, Gravedad Cuanto, int Veces)> PorTipo()
    {
        lock (_llave)
            return _errores
                .GroupBy(e => e.Que)
                .Select(g => (Que: g.Key, Cuanto: g.First().Cuanto, Veces: g.Count()))
                // Lo que ROMPE el grafo va primero aunque pase menos veces: un camino perdido no se
                // arregla solo, y treinta lecturas descartadas sí. Ordenar solo por frecuencia
                // enterraría lo urgente debajo de lo que se cura en la siguiente vuelta.
                .OrderBy(x => x.Cuanto)
                .ThenByDescending(x => x.Veces)
                .ToList();
    }

    /// <summary>Y por app, para saber dónde duele. Solo lo que rompe el grafo.</summary>
    public IReadOnlyList<(string App, int Veces)> PorApp()
    {
        lock (_llave)
            return _errores
                .Where(e => e.Cuanto == Gravedad.RompeElGrafo && e.App.Length > 0)
                .GroupBy(e => e.App, StringComparer.OrdinalIgnoreCase)
                .Select(g => (App: g.Key, Veces: g.Count()))
                .OrderByDescending(x => x.Veces)
                .ToList();
    }

    /// <summary>Se vacía. Para empezar una prueba desde cero sin reiniciar la app.</summary>
    public void Olvidar()
    {
        lock (_llave) _errores.Clear();
    }
}
