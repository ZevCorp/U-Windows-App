using U.WindowsClient.Cuenta;
using U.WindowsClient.Diagnostics;

namespace U.WindowsClient.Clinical;

/// <summary>En qué punto va la consulta. Los nombres calcan los estados del backend.</summary>
public enum EstadoDeConsulta
{
    /// <summary>No ha empezado, o terminó y se archivó.</summary>
    SinEmpezar,

    /// <summary>El encounter existe y el micrófono está abierto.</summary>
    Grabando,

    /// <summary>Se paró de grabar; la transcripción viaja o viajó al backend.</summary>
    Transcrita,

    /// <summary>El backend está organizando la nota.</summary>
    GenerandoNota,

    /// <summary>La nota está organizada y disponible en <see cref="Consulta.Nota"/>.</summary>
    NotaLista,

    /// <summary>Algo falló. El motivo está en <see cref="Consulta.Motivo"/> y se puede nombrar.</summary>
    Fallida,
}

/// <summary>
/// UNA CONSULTA MÉDICO-PACIENTE, de empezar a grabar a tener la nota organizada. Es la máquina de
/// estados que une la sesión del médico, el dictado y el backend clínico.
/// </summary>
/// <remarks>
/// SIN MÉDICO NO EMPIEZA NADA, y es la promesa 84. Se comprueba ANTES de abrir el micrófono y antes
/// de crear el encounter, en ese orden y a propósito: grabar a un paciente sin saber de quién es la
/// consulta es lo único de este flujo que no tiene vuelta atrás, y un encounter creado sin dueño
/// sería una fila huérfana en la base de todos.
///
/// FALLAR NO ES TERMINAR, y es la promesa 91. «Terminé» no es un veredicto (este repo ya vio al
/// puente consciente declarar éxito habiendo pulsado el botón equivocado): si `generate-note`
/// falla, el estado queda en <see cref="EstadoDeConsulta.Fallida"/> con el motivo NOMBRADO —el
/// código del backend decide si se reintenta o se avisa al equipo— y el <c>encounter_id</c>
/// sobrevive: el contrato permite repetir generate-note mientras haya transcript, así que
/// reintentar sobre el mismo encounter no duplica nada. Volver a crearlo, sí.
///
/// LA NOTA ES UN RESULTADO, NO UNA PANTALLA. <see cref="Nota"/> expone las secciones tipadas
/// (clave → texto) porque el destino final de esto no es pintarse: es ser la materia prima que los
/// RECUERDOS guían y los BATCHES escriben en SAP (la experiencia completa: enseñas el app con
/// recuerdos → activas la nota clínica → los campos se llenan solos). Esta clase no sabe de SAP —
/// igual que el portal no sabe de SAP— y esa frontera es deliberada: el acoplamiento campo↔dato
/// vive en los recuerdos, no aquí.
///
/// EL MICRÓFONO Y EL VERBATIM SE INYECTAN como funciones para que el contrato pueda juzgar esta
/// máquina sin audio ni pantalla: el arnés pasa un «abrir micrófono» que anota si fue llamado, y
/// eso es lo que permite afirmar «no se abrió» en vez de suponerlo.
/// </remarks>
public sealed class Consulta
{
    private readonly SesionMiracle _sesion;
    private readonly ClinicaClient _clinica;
    private readonly Func<CancellationToken, Task<bool>> _abrirMicrofono;
    private readonly Func<Task<string>> _pararYRecogerLoDicho;

    /// <summary>
    /// Escribe el espejo en `consultations` para que la consulta se VEA en el portal (promesa 93).
    /// Se inyecta como funcion para que esta clase no sepa de Supabase — y para que el contrato
    /// pueda juzgar la maquina de estados sin red.
    /// </summary>
    /// <remarks>
    /// EL CUARTO ARGUMENTO ES «la fila ya existe», y decide si el espejo manda `estado` y `firma`
    /// (promesa 193, spec 015). Corregir una nota vuelve a escribir el espejo, y mandarlos otra vez
    /// devolveria la consulta a borrador y le quitaria la firma.
    /// </remarks>
    private readonly Func<string, NotaClinica, string, bool, Task<bool>>? _espejar;

    public Consulta(SesionMiracle sesion, ClinicaClient clinica,
        Func<CancellationToken, Task<bool>> abrirMicrofono,
        Func<Task<string>> pararYRecogerLoDicho,
        Func<string, NotaClinica, string, bool, Task<bool>>? espejar = null)
    {
        _sesion = sesion;
        _clinica = clinica;
        _abrirMicrofono = abrirMicrofono;
        _pararYRecogerLoDicho = pararYRecogerLoDicho;
        _espejar = espejar;
    }

    /// <summary>Lo ultimo que se dijo en esta consulta. Es lo que viaja al espejo.</summary>
    public string Verbatim { get; private set; } = "";

    /// <summary>La consulta quedo visible en el portal. Falso si el espejo no se pudo escribir.</summary>
    public bool VisibleEnElPortal { get; private set; }

    public EstadoDeConsulta Estado { get; private set; } = EstadoDeConsulta.SinEmpezar;

    /// <summary>Por qué está donde está, cuando falló. Vacío mientras todo va bien.</summary>
    public string Motivo { get; private set; } = "";

    /// <summary>
    /// El código del backend del último fallo (<c>NOTE_GENERATION_FAILED</c>…), para decidir por
    /// máquina si reintentar. Vacío si no falló o si el fallo no vino del backend.
    /// </summary>
    public string CodigoDeFallo { get; private set; } = "";

    /// <summary>El id del encounter en la base — el MISMO que verá el portal. Sobrevive a los fallos.</summary>
    public string EncounterId { get; private set; } = "";

    /// <summary>La nota organizada, cuando <see cref="Estado"/> es <see cref="EstadoDeConsulta.NotaLista"/>.</summary>
    public NotaClinica? Nota { get; private set; }

    /// <summary>
    /// ¿Se puede cambiar de cuenta ahora? Solo mientras no se está grabando (promesa 99).
    /// </summary>
    /// <remarks>
    /// Cambiar de cuenta empieza por cerrar la sesión actual, y eso es justo lo que no se puede
    /// hacer a media consulta: con el micrófono abierto, cerrar sesión dejaría un dictado huérfano
    /// —nadie lo para, nadie lo guarda— y el médico se quedaría sin saber que perdió lo grabado.
    /// </remarks>
    public bool PuedeCambiarDeUsuario => Estado != EstadoDeConsulta.Grabando;

    /// <summary>Cambió el estado. La interfaz se pinta con esto; nadie más decide con esto.</summary>
    public event Action<EstadoDeConsulta>? Cambio;

    // ── el ciclo ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Empieza la consulta: crea el encounter y abre el micrófono. Devuelve si empezó; si no, el
    /// porqué queda en <see cref="Motivo"/>.
    /// </summary>
    public async Task<bool> EmpezarAsync(string plantillaId, CancellationToken ct = default)
    {
        if (Estado is EstadoDeConsulta.Grabando or EstadoDeConsulta.GenerandoNota)
        {
            Motivo = "ya hay una consulta en marcha";
            return false;
        }

        // LA PUERTA. Antes que el micrófono y antes que la red: sin médico no se toca nada.
        if (!_sesion.HayMedico)
        {
            Motivo = "no hay ningún médico con sesión iniciada";
            LogBus.Log("consulta", "se pidió empezar sin sesión: no se abre micrófono ni se crea encounter");
            return false;
        }

        try
        {
            EncounterId = await _clinica.CrearEncounterAsync(plantillaId, ct: ct);
            if (EncounterId.Length == 0)
            {
                Fallar("el backend no devolvió el id del encounter", "");
                return false;
            }

            // SE MIRA SI ABRIÓ, y esta línea es la promesa 95. El 2026-09-01 el stream contestó
            // «Unable to connect to the remote server», esto siguió adelante, y la consulta se
            // declaró GRABANDO: alguien le habló diecisiete segundos a una app que no escuchaba.
            if (!await _abrirMicrofono(ct))
            {
                Fallar("no se pudo abrir el dictado: no hubo conexión con el servicio de "
                     + "transcripción. Comprueba la red y vuelve a intentarlo", "");
                return false;
            }

            Motivo = "";
            CodigoDeFallo = "";
            Nota = null;
            Pasar(EstadoDeConsulta.Grabando);
            LogBus.Log("consulta", $"grabando · encounter {EncounterId}");
            return true;
        }
        catch (ErrorClinico e)
        {
            Fallar(e.Message, e.Codigo);
            return false;
        }
        catch (Exception e)
        {
            Fallar($"no se pudo empezar: {e.Message}", "");
            return false;
        }
    }

    /// <summary>
    /// Para de grabar, guarda lo dicho y pide la nota. Al volver, o hay nota
    /// (<see cref="EstadoDeConsulta.NotaLista"/>) o hay un motivo nombrado.
    /// </summary>
    public async Task TerminarAsync(CancellationToken ct = default)
    {
        if (Estado != EstadoDeConsulta.Grabando)
        {
            Motivo = "no hay ninguna grabación en marcha";
            return;
        }

        string dicho = await _pararYRecogerLoDicho();
        Verbatim = dicho ?? "";

        // VACÍO SE DICE AQUÍ, no se manda. /transcript con texto vacío contesta 400
        // TRANSCRIPT_REQUIRED — un error evitable que además taparía el de verdad: el micrófono
        // que no entregó nada.
        if (string.IsNullOrWhiteSpace(dicho))
        {
            // Llegar aquí significa que el dictado SÍ conectó (si no, no se habría llegado a
            // grabar) y aun así no entregó texto. Ahora sí es del lado del audio, y por eso este
            // mensaje puede nombrar el micrófono sin mandar a nadie al sitio equivocado.
            Fallar("el dictado estaba conectado pero no llegó ni una palabra: revisa que el "
                 + "micrófono correcto esté seleccionado en Windows", "");
            return;
        }

        try
        {
            await _clinica.GuardarTranscripcionAsync(EncounterId, dicho, ct);
            Pasar(EstadoDeConsulta.Transcrita);

            Pasar(EstadoDeConsulta.GenerandoNota);
            Nota = await _clinica.GenerarNotaAsync(EncounterId, ct);
            await EspejarAsync();
            Pasar(EstadoDeConsulta.NotaLista);
            LogBus.Log("consulta", $"nota lista · encounter {EncounterId} · {Nota.Secciones.Count} sección(es)");
        }
        catch (ErrorClinico e)
        {
            // El encounter NO se toca: con la transcripción ya guardada, reintentar la nota sobre
            // el mismo id es exactamente lo que el contrato del backend permite.
            Fallar(e.Message, e.Codigo);
        }
        catch (Exception e)
        {
            Fallar($"se perdió la conexión con el backend: {e.Message}", "");
        }
    }

    /// <summary>
    /// Vuelve a pedir la nota sobre el MISMO encounter. Solo tiene sentido tras un fallo con la
    /// transcripción ya guardada; en cualquier otro punto contesta que no con su porqué.
    /// </summary>
    public async Task<bool> ReintentarNotaAsync(CancellationToken ct = default)
    {
        if (Estado != EstadoDeConsulta.Fallida || EncounterId.Length == 0)
        {
            Motivo = "no hay ningún fallo del que reintentar";
            return false;
        }

        try
        {
            Pasar(EstadoDeConsulta.GenerandoNota);
            Nota = await _clinica.GenerarNotaAsync(EncounterId, ct);
            await EspejarAsync();
            Motivo = "";
            CodigoDeFallo = "";
            Pasar(EstadoDeConsulta.NotaLista);
            return true;
        }
        catch (ErrorClinico e)
        {
            Fallar(e.Message, e.Codigo);
            return false;
        }
        catch (Exception e)
        {
            Fallar($"se perdió la conexión con el backend: {e.Message}", "");
            return false;
        }
    }

    /// <summary>
    /// Corrige el texto de UNA sección de la nota ya generada, y lo guarda donde vive la nota.
    /// </summary>
    /// <remarks>
    /// Promesa 191 (spec 015). El médico lee lo que la IA organizó y arregla lo que haga falta antes
    /// de que eso llegue a la historia clínica.
    ///
    /// SE MANDA LA NOTA ENTERA aunque solo cambie una sección, porque el contrato del backend exige
    /// las claves EXACTAS del snapshot y este objeto es el único sitio donde están todas — la
    /// pantalla pinta solo las que tienen texto. Ver <see cref="ClinicaClient.CuerpoDeNotaEditada"/>.
    ///
    /// EL ORDEN IMPORTA Y NO ES CASUAL: primero el backend, después el espejo. Si se hiciera al
    /// revés, un `PUT` fallido dejaría el portal enseñando un texto que la historia clínica no
    /// tiene. Así, un espejo fallido solo deja el portal desactualizado —y lo DICE, en
    /// <see cref="VisibleEnElPortal"/>— con la nota ya a salvo.
    ///
    /// LA NOTA EN MEMORIA SOLO SE CAMBIA SI EL BACKEND ACEPTÓ, y se reemplaza por la que él
    /// devuelve, no por la que se mandó: es él quien restaura `label` y el orden desde el snapshot.
    /// </remarks>
    public async Task<bool> CorregirSeccionAsync(string clave, string texto,
        CancellationToken ct = default)
    {
        if (Nota == null || EncounterId.Length == 0)
        {
            Motivo = "no hay ninguna nota que corregir";
            return false;
        }

        var corregida = Nota.ConSeccion(clave, texto ?? "");

        try
        {
            Nota = await _clinica.GuardarNotaEditadaAsync(EncounterId, corregida, ct);
            // La fila del portal YA existe —se escribió al generar la nota— así que esto es una
            // corrección: el espejo no puede volver a mandar `estado` ni `firma` (promesa 193).
            await EspejarAsync(yaExiste: true);
            Motivo = "";
            CodigoDeFallo = "";
            LogBus.Log("consulta", $"sección «{clave}» corregida por el médico");
            return true;
        }
        catch (ErrorClinico e)
        {
            // NO se pasa a Fallida: la consulta está entera y su nota también. Lo que falló es un
            // guardado, y decirlo como una avería de la consulta mandaría a mirar el sitio
            // equivocado (aprendizaje nº2).
            Motivo = e.Message;
            CodigoDeFallo = e.Codigo;
            LogBus.Log("consulta", $"no se pudo guardar la corrección · {e.Codigo}");
            return false;
        }
        catch (Exception e)
        {
            Motivo = $"no se pudo guardar la corrección: {e.Message}";
            return false;
        }
    }

    /// <summary>
    /// Corrige el RESUMEN de la nota. Mismo camino que una sección: backend y después espejo.
    /// </summary>
    /// <remarks>
    /// El resumen es lo primero que se lee de una nota —y lo que el portal enseña en la lista— así
    /// que dejarlo de solo lectura mientras las secciones se corrigen era la mitad del trabajo.
    /// </remarks>
    public async Task<bool> CorregirResumenAsync(string texto, CancellationToken ct = default)
    {
        if (Nota == null || EncounterId.Length == 0)
        {
            Motivo = "no hay ninguna nota que corregir";
            return false;
        }

        try
        {
            Nota = await _clinica.GuardarNotaEditadaAsync(EncounterId, Nota.ConResumen(texto ?? ""), ct);
            await EspejarAsync(yaExiste: true);
            Motivo = "";
            CodigoDeFallo = "";
            LogBus.Log("consulta", "resumen corregido por el médico");
            return true;
        }
        catch (ErrorClinico e)
        {
            Motivo = e.Message;
            CodigoDeFallo = e.Codigo;
            return false;
        }
        catch (Exception e)
        {
            Motivo = $"no se pudo guardar la corrección: {e.Message}";
            return false;
        }
    }

    /// <summary>Archiva esta consulta para poder empezar otra. La de la base no se toca.</summary>
    public void Cerrar()
    {
        EncounterId = "";
        Nota = null;
        Motivo = "";
        CodigoDeFallo = "";
        Pasar(EstadoDeConsulta.SinEmpezar);
    }

    // ── menudencias ──────────────────────────────────────────────────────────

    /// <summary>
    /// Espeja la consulta al portal. UN FALLO AQUI NO TUMBA LA CONSULTA: la nota ya esta a salvo en
    /// el backend y perderla por no poder pintarla en una lista seria absurdo. Lo que si pasa es
    /// que se DICE —queda en VisibleEnElPortal y en el log— en vez de suponer que se vio.
    /// </summary>
    private async Task EspejarAsync(bool yaExiste = false)
    {
        VisibleEnElPortal = false;
        if (_espejar == null || Nota == null) return;
        try { VisibleEnElPortal = await _espejar(EncounterId, Nota, Verbatim, yaExiste); }
        catch (Exception e)
        {
            LogBus.Log("consulta", $"la consulta no se pudo espejar al portal: {e.Message}");
        }
    }

    private void Fallar(string motivo, string codigo)
    {
        Motivo = motivo;
        CodigoDeFallo = codigo;
        LogBus.Log("consulta", $"falló · {(codigo.Length > 0 ? codigo + " · " : "")}{motivo}");
        Pasar(EstadoDeConsulta.Fallida);
    }

    private void Pasar(EstadoDeConsulta nuevo)
    {
        Estado = nuevo;
        Cambio?.Invoke(nuevo);
    }
}
