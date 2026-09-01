using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace Medidor.App;

/// <summary>
/// EL INDICADOR PERMANENTE. La spec anterior (medir-el-terreno.md) lo dejó escrito y no es
/// negociable: «un indicador permanente y un atajo para parar no son cortesía: son lo que hace la
/// diferencia entre una herramienta de medición y una cámara oculta». El icono de bandeja está
/// SIEMPRE, dice el estado de un vistazo (midiendo / pausado / sin médico) y da el menú para elegir
/// médico, pausar y ver qué se mide.
///
/// WinForms NotifyIcon porque WPF no tiene bandeja nativa; es lo único de WinForms en todo el
/// medidor.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class Bandeja : IDisposable
{
    public enum EstadoUi { Midiendo, SinMedico, Pausado, Desconectado }

    private readonly NotifyIcon _icono;
    private readonly ToolStripMenuItem _quienSoy;
    private readonly ToolStripMenuItem _pausar;
    private EstadoUi _estado = EstadoUi.Desconectado;

    public event Action? PedirElegirMedico;
    public event Action? PedirPausar;
    public event Action? PedirReanudar;
    public event Action? PedirQueSeMide;

    public Bandeja()
    {
        var menu = new ContextMenuStrip();
        _quienSoy = new ToolStripMenuItem("Elegir mi nombre…", null, (_, _) => PedirElegirMedico?.Invoke());
        _pausar = new ToolStripMenuItem("Pausar la medición", null, (_, _) => AlternarPausa());
        menu.Items.Add(_quienSoy);
        menu.Items.Add(_pausar);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("¿Qué mide esto?", null, (_, _) => PedirQueSeMide?.Invoke()));

        _icono = new NotifyIcon
        {
            Visible = true,
            Text = "Medidor Ü",
            ContextMenuStrip = menu,
            Icon = IconoDe(EstadoUi.Desconectado),
        };
        _icono.DoubleClick += (_, _) => PedirElegirMedico?.Invoke();
    }

    private void AlternarPausa()
    {
        if (_estado == EstadoUi.Pausado) PedirReanudar?.Invoke();
        else PedirPausar?.Invoke();
    }

    public void MostrarEstado(EstadoUi estado, string? medico)
    {
        _estado = estado;
        _icono.Icon = IconoDe(estado);
        _icono.Text = estado switch
        {
            EstadoUi.Midiendo => $"Midiendo — {medico ?? "sin médico"}",
            EstadoUi.SinMedico => "Sin médico: elige tu nombre",
            EstadoUi.Pausado => "Pausado",
            _ => "Sin conexión con el servidor",
        };
        _pausar.Text = estado == EstadoUi.Pausado ? "Reanudar la medición" : "Pausar la medición";
    }

    public void Aviso(string titulo, string texto)
        => _icono.ShowBalloonTip(5000, titulo, texto, ToolTipIcon.Info);

    /// <summary>Un icono dibujado, sin recursos externos: un círculo cuyo color dice el estado.
    /// Verde midiendo, ámbar sin médico, gris pausado/desconectado.</summary>
    private static Icon IconoDe(EstadoUi estado)
    {
        var color = estado switch
        {
            EstadoUi.Midiendo => Color.FromArgb(46, 160, 67),
            EstadoUi.SinMedico => Color.FromArgb(210, 153, 34),
            EstadoUi.Pausado => Color.FromArgb(139, 148, 158),
            _ => Color.FromArgb(80, 80, 80),
        };
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var pincel = new SolidBrush(color);
            g.FillEllipse(pincel, 2, 2, 12, 12);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    public void Dispose()
    {
        _icono.Visible = false;
        _icono.Dispose();
    }
}
