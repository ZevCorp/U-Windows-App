using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using U.WindowsClient.Diagnostics;
using Velopack;

namespace U.WindowsClient;

public partial class App : Application
{
    /// <summary>
    /// Entry point manual (ver StartupObject en WindowsClient.csproj). Existe por Velopack: cuando el
    /// updater instala o desinstala una versión relanza este mismo .exe con argumentos de hook y espera
    /// que el proceso los atienda y termine. <c>VelopackApp.Run()</c> hace eso y NO retorna en ese caso
    /// — por eso corre antes de levantar WPF, o cada actualización abriría una carita fantasma.
    /// </summary>
    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            VelopackApp.Build().Run();

            var app = new App();
            app.InitializeComponent();
            app.Run();
        }
        catch (Exception ex)
        {
            LogBus.Log("fatal", ex.ToString());
            MessageBox.Show($"Ü no pudo arrancar: {ex.Message}", "Ü", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Nunca dejar caer la carita por una excepción no capturada: es un overlay permanente.
        DispatcherUnhandledException += (_, ex) =>
        {
            LogBus.Log("fatal", ex.Exception.ToString());
            MessageBox.Show($"Ü tropezó: {ex.Exception.Message}", "Ü", MessageBoxButton.OK, MessageBoxImage.Warning);
            ex.Handled = true;
        };

        // Una tarea "fire-and-forget" (p.ej. `_ = algo.EmpezarAsync()`) que revienta NO pasa por
        // DispatcherUnhandledException: se pierde en silencio salvo que se observe aquí. Esto fue
        // exactamente lo que ocultó el primer error real de la enseñanza por video.
        TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            LogBus.Log("unobserved-task", ex.Exception.ToString());
            ex.SetObserved();
        };

        // EL ICONO DE LA CONSULTA (spec 004, fase 8). `U.exe --consulta` abre la ventana de la
        // consulta clínica delante; la carita arranca igual por StartupUri — mismo proceso, no se
        // le quita nada. La sesión se RESTAURA antes de pedir login: cerrar el portátil una noche
        // no puede costar la contraseña otra vez.
        if (e.Args.Any(a => string.Equals(a, "--consulta", StringComparison.OrdinalIgnoreCase)))
        {
            AbrirLaConsulta();
        }
    }

    /// <summary>
    /// El camino de <c>--consulta</c>, con la aplicación sostenida mientras dura.
    /// </summary>
    /// <remarks>
    /// EL <c>ShutdownMode</c> NO ES UN DETALLE: es lo que mató a esta app en silencio el
    /// 2026-09-01. `OnStartup` corre ANTES de que `StartupUri` cree la carita, así que mientras el
    /// login está abierto es la ÚNICA ventana del proceso. Al cerrarse —con el login YA
    /// resuelto— `ShutdownMode.OnLastWindowClose`, que es el de fábrica, dio la aplicación por
    /// terminada: `ConsultaWindow.Show()` no pintó nada, `app.Run()` retornó y U.exe se cerró.
    ///
    /// El síntoma fue perfecto en su inutilidad: ni excepción, ni ventana, ni mensaje. El log lo
    /// dijo todo con un silencio — «cuenta: médico dentro · 67530d77…» y ni una línea más.
    ///
    /// `OnExplicitShutdown` mientras dura el arranque, y se devuelve el modo anterior en cuanto hay
    /// ventana. No se deja puesto: con él, cerrar todas las ventanas dejaría el proceso vivo e
    /// invisible, que es la avería opuesta y peor.
    /// </remarks>
    private void AbrirLaConsulta()
    {
        var modoPrevio = ShutdownMode;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            var sesion = new U.WindowsClient.Cuenta.SesionMiracle(
                U.WindowsClient.Cuenta.Nube.SupabaseUrl, U.WindowsClient.Cuenta.Nube.ClavePublicable);

            var arranque = new U.WindowsClient.Clinical.ArranqueDeConsulta(
                restaurarSesion: sesion.Restaurar,
                pedirCredenciales: () => new Ui.LoginWindow(sesion).ShowDialog() == true,
                abrirLaVentana: () => new Ui.ConsultaWindow(sesion, U.Graph.GraphConfig.Load()).Show(),
                avisarDelFallo: Ui.Aviso.Fallo);

            arranque.Correr();
        }
        finally { ShutdownMode = modoPrevio; }
    }
}
