using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Clinical;

/// <summary>
/// EL ARRANQUE DE LA CONSULTA, PASO A PASO Y CON NOMBRE. Restaurar la sesión → pedir la contraseña
/// si hace falta → abrir la ventana. Cada tropiezo se avisa diciendo EN QUÉ PASO fue.
/// </summary>
/// <remarks>
/// NACE DE UNA MUERTE EN SILENCIO (2026-09-01). `U.exe --consulta`: el médico entró bien —el log
/// dice «cuenta: médico dentro · 67530d77…»— y esa fue **la última línea del archivo**. Ni
/// excepción, ni ventana, ni mensaje. La causa era de WPF: `OnStartup` corre antes de que
/// `StartupUri` cree la carita, así que el login era la ÚNICA ventana abierta y al cerrarse
/// `ShutdownMode.OnLastWindowClose` dio la aplicación por terminada.
///
/// ESA CAUSA CONCRETA ES NIVEL 4 y se dice sin adornos: solo se caza ejecutando, y ninguna promesa
/// de este contrato la habría visto. Lo que esta clase promete es lo otro, que es lo que impide que
/// la próxima vez vuelva a ser invisible: **que el arranque nombre el paso en el que se quedó**. Un
/// fallo que no se ve no se arregla — se vuelve a pagar.
///
/// CANCELAR NO ES FALLAR, y es la mitad menos obvia de la promesa 92. Cerrar el login sin entrar
/// devuelve «no se abrió» sin avisar de nada: un aviso que salta cuando no ha pasado nada se
/// aprende a ignorar, y con él se pierde el que sí importaba.
///
/// TODO SE INYECTA COMO FUNCIONES para que el contrato pueda juzgar esta secuencia sin WPF, sin
/// red y sin micrófono. Quien pinta el aviso es <see cref="Ui.Aviso"/>; aquí solo se decide QUÉ
/// se dice.
/// </remarks>
public sealed class ArranqueDeConsulta
{
    private readonly Func<bool> _restaurarSesion;
    private readonly Func<bool> _pedirCredenciales;
    private readonly Action _abrirLaVentana;
    private readonly Action<string, string> _avisarDelFallo;

    public ArranqueDeConsulta(
        Func<bool> restaurarSesion,
        Func<bool> pedirCredenciales,
        Action abrirLaVentana,
        Action<string, string> avisarDelFallo)
    {
        _restaurarSesion = restaurarSesion;
        _pedirCredenciales = pedirCredenciales;
        _abrirLaVentana = abrirLaVentana;
        _avisarDelFallo = avisarDelFallo;
    }

    /// <summary>Corre el arranque. Devuelve si la ventana de consulta quedó abierta.</summary>
    public bool Correr()
    {
        // 1. La sesión guardada. Que falle no corta nada: se pide la contraseña, que es el camino
        //    normal de la primera vez. Por eso este paso no avisa aunque reviente.
        bool hayMedico;
        try
        {
            hayMedico = _restaurarSesion();
            LogBus.Log("arranque", hayMedico
                ? "sesión restaurada del disco: no hace falta la contraseña"
                : "no había sesión guardada: se pide la contraseña");
        }
        catch (Exception e)
        {
            LogBus.Log("arranque", $"la sesión guardada no se pudo leer ({e.Message}); se pide la contraseña");
            hayMedico = false;
        }

        // 2. La contraseña. Decir que no es una DECISIÓN, no una avería.
        if (!hayMedico)
        {
            try
            {
                if (!_pedirCredenciales())
                {
                    LogBus.Log("arranque", "el login se cerró sin entrar: no se abre la consulta");
                    return false;
                }
            }
            catch (Exception e)
            {
                Fallar("pedir la contraseña", e);
                return false;
            }
        }

        // 3. La ventana. Aquí sí: si esto no abre, el usuario se queda mirando la nada — que es
        //    literalmente lo que pasó el 2026-09-01.
        try
        {
            _abrirLaVentana();
            LogBus.Log("arranque", "ventana de consulta abierta");
            return true;
        }
        catch (Exception e)
        {
            Fallar("abrir la ventana de consulta", e);
            return false;
        }
    }

    /// <summary>
    /// El paso y el porqué REAL. La cadena entera de excepciones, porque un
    /// <c>TypeInitializationException</c> dice «el inicializador lanzó una excepción» y se guarda
    /// para sí el motivo, que es lo único que sirve (patrón nº3).
    /// </summary>
    private void Fallar(string paso, Exception e)
    {
        var porque = new System.Text.StringBuilder();
        for (var x = e; x != null; x = x.InnerException)
        {
            if (porque.Length > 0) porque.Append(" ← ");
            porque.Append($"{x.GetType().Name}: {x.Message}");
        }

        LogBus.Log("arranque", $"FALLÓ al {paso}: {porque}");
        try { _avisarDelFallo(paso, porque.ToString()); }
        catch (Exception avisando)
        {
            // Que el aviso falle no puede tapar el fallo del que avisaba.
            LogBus.Log("arranque", $"y encima el aviso no se pudo enseñar: {avisando.Message}");
        }
    }
}
