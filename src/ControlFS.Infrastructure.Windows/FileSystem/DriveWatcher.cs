namespace ControlFS.Infrastructure.Windows.FileSystem;

/// <summary>
/// Avisa quando o conjunto de unidades muda (pendrive conectado ou removido, unidade de rede mapeada, disco inserido
/// num leitor). Compara, a cada intervalo, as letras e tipos (GetLogicalDrives/GetDriveType: sem tocar na mídia) e a
/// prontidão só de unidades removíveis e ópticas; unidades de rede nunca são consultadas (poderiam travar).
/// <see cref="Changed"/> é disparado numa thread do pool: quem assina volta para a thread de UI.
/// </summary>
public sealed class DriveWatcher : IDisposable
{
    private readonly Timer _timer;
    private readonly object _gate = new();
    private string _last;
    private bool _disposed;

    public DriveWatcher(TimeSpan? interval = null)
    {
        _last = Snapshot();
        var period = interval ?? TimeSpan.FromSeconds(2);
        _timer = new Timer(_ => Poll(), null, period, period);
    }

    public event Action? Changed;

    private void Poll()
    {
        string current;
        lock (_gate)
        {
            if (_disposed) return;
            current = Snapshot();
            if (current == _last) return;
            _last = current;
        }
        Changed?.Invoke();
    }

    /// <summary>Assinatura do conjunto de unidades: "C:\=Fixed;E:\=Removable+;…".</summary>
    private static string Snapshot()
    {
        var parts = new List<string>();
        foreach (var drive in SafeGetDrives())
        {
            try
            {
                var type = drive.DriveType;
                var ready = type is DriveType.Removable or DriveType.CDRom ? (drive.IsReady ? "+" : "-") : string.Empty;
                parts.Add($"{drive.Name}={type}{ready}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return string.Join(';', parts);
    }

    private static DriveInfo[] SafeGetDrives()
    {
        try { return DriveInfo.GetDrives(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
        _timer.Dispose();
    }
}
