namespace ControlFS.Core.Input;

/// <summary>Como ler os controles agora: a thread de 8 ms (com o relógio do Windows em 1 ms) ou um temporizador da UI.</summary>
/// <param name="UseTicker">Thread leve a cada 8 ms ("Fluidez máxima" com a janela ativa e um controle conectado).</param>
/// <param name="Interval">Intervalo do temporizador da UI quando <paramref name="UseTicker"/> é falso.</param>
/// <param name="QuietDevices">
/// Só conexões e desconexões chegam do SDL (botões, eixos e sensores ficam desligados): com a janela em segundo plano,
/// um jogo usando o mesmo controle não enche a fila de eventos de um app que não vai usá-los.
/// </param>
public readonly record struct InputPoll(bool UseTicker, TimeSpan Interval, bool QuietDevices);

/// <summary>
/// Cadência da leitura dos controles (docs/performance.md). Teclado e mouse chegam por eventos do WinUI; só controles
/// precisam de leitura periódica. Sem controle conectado, basta notar a chegada de um (250 ms). Em segundo plano o app
/// não roteia nada: "Leve em segundo plano" lê só conexões a cada 1 s; desligado, a cada 120 ms como antes.
/// </summary>
public static class InputCadence
{
    public static readonly TimeSpan Active = TimeSpan.FromMilliseconds(8);
    public static readonly TimeSpan NoController = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan Background = TimeSpan.FromMilliseconds(120);
    public static readonly TimeSpan LightBackground = TimeSpan.FromSeconds(1);

    public static InputPoll Decide(bool windowActive, bool controllerConnected, bool syncInputToDisplay, bool lightInBackground)
    {
        if (!windowActive) return new(false, lightInBackground ? LightBackground : Background, QuietDevices: true);
        if (!controllerConnected) return new(false, NoController, QuietDevices: false);
        return new(syncInputToDisplay, Active, QuietDevices: false);
    }
}
