using System.Runtime.Versioning;
using ControlFS.Core.Automation;

namespace ControlFS.Infrastructure.Windows.Automation;

/// <summary>
/// Coordena a inicialização única (single-instance) e sinalização entre processos para automação.
/// Permite que links controlfs:// e argumentos de linha de comando acionem a instância já em execução.
/// </summary>
[SupportedOSPlatform("windows")]
public static class SingleInstanceCoordinator
{
    public const string InstanceMutexName = @"Local\ControlFS.Instance";
    public const string ShowSignalName = @"Local\ControlFS.Show";
    public const string CloseSignalName = @"Local\ControlFS.Close";

    /// <summary>
    /// Avalia a linha de comando e determina se o processo deve continuar ou repassar a ação à instância existente.
    /// Retorna <c>true</c> se o processo atual deve ser finalizado imediatamente.
    /// Retorna <c>false</c> se este processo é a primeira instância e deve abrir a aplicação.
    /// </summary>
    public static bool HandleLaunch(string[] rawArgs, out Mutex? instanceMutex, bool bypass = false, Action<string>? log = null)
    {
        instanceMutex = null;
        if (!OperatingSystem.IsWindows() || bypass)
        {
            return false;
        }

        var args = rawArgs.Skip(1).ToList();
        var action = AppProtocol.ParseActionFromArgs(args);

        bool isFirstInstance;
        try
        {
            instanceMutex = new Mutex(true, InstanceMutexName, out isFirstInstance);
        }
        catch (AbandonedMutexException)
        {
            isFirstInstance = true;
        }

        if (!isFirstInstance)
        {
            // Instância já rodando: avisa a instância existente.
            if (action == AppProtocol.StopAction)
            {
                log?.Invoke("Avisando instância em execução para fechar (Close)");
                Signal(CloseSignalName, log);
            }
            else
            {
                log?.Invoke("Avisando instância em execução para exibir (Show)");
                Signal(ShowSignalName, log);
            }

            instanceMutex?.Dispose();
            instanceMutex = null;
            return true;
        }

        // Primeira instância:
        if (action == AppProtocol.StopAction)
        {
            // Pedido de parada quando nada está aberto: não abre a interface.
            log?.Invoke("Comando de parada recebido (--stop ou controlfs://stop), mas nenhuma instância estava aberta.");
            instanceMutex?.Dispose();
            instanceMutex = null;
            return true;
        }

        return false;
    }

    public static void Signal(string signalName, Action<string>? log = null)
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            if (EventWaitHandle.TryOpenExisting(signalName, out var handle))
            {
                using (handle)
                {
                    handle.Set();
                }
            }
            else
            {
                log?.Invoke($"Sinal '{signalName}' não encontrado ao tentar avisar instância existente.");
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"Erro ao enviar sinal '{signalName}': {ex.Message}");
        }
    }
}
