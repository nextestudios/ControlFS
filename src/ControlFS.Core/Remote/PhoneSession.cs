namespace ControlFS.Core.Remote;

public enum PhoneSessionState
{
    /// <summary>QR Code na tela: a página pode ser aberta e o celular pode se autenticar.</summary>
    Waiting,

    /// <summary>Um celular provou ter a chave; o PC ainda precisa permitir (IP e código na tela).</summary>
    AwaitingConfirmation,

    /// <summary>Permitido no PC: as mensagens do celular viram entrada.</summary>
    Connected,

    /// <summary>Encerrada: a chave morreu; outra conexão exige um QR Code novo.</summary>
    Ended,
}

public enum PhoneEndReason
{
    /// <summary>O usuário cancelou o pareamento ou desconectou no PC.</summary>
    Disconnected,

    /// <summary>O celular desconectou, fechou a página ou a rede caiu.</summary>
    PhoneLeft,

    /// <summary>Ninguém usou o QR Code a tempo.</summary>
    Expired,

    /// <summary>O celular conectou, mas ninguém permitiu no PC a tempo.</summary>
    ConfirmationTimedOut,

    /// <summary>O usuário recusou o celular no PC.</summary>
    Rejected,

    /// <summary>Mensagem que não passou na autenticação (adulterada, repetida ou fora de ordem).</summary>
    AuthenticationFailed,

    /// <summary>Tentativas demais sem a chave certa.</summary>
    TooManyAttempts,

    /// <summary>O ControlFS está fechando.</summary>
    AppClosing,
}

/// <summary>
/// Máquina de estados de uma sessão de celular (#223), sem rede nem relógio próprio (os tempos chegam de fora, o que a
/// torna testável). Uma sessão: um QR Code, uma chave, um celular. A chave vale para a primeira conexão autenticada e
/// morre quando ela termina; não há reconexão automática. Entrada só depois de o usuário permitir no PC.
/// Thread-safe: o servidor chama de várias conexões ao mesmo tempo.
/// </summary>
public sealed class PhoneSession(TimeSpan startedAt)
{
    /// <summary>Tempo para escanear o QR Code e abrir a página.</summary>
    public static readonly TimeSpan PairingLifetime = TimeSpan.FromMinutes(2);

    /// <summary>Tempo para o usuário permitir o celular no PC.</summary>
    public static readonly TimeSpan ConfirmationTimeout = TimeSpan.FromMinutes(2);

    /// <summary>Conexões abrindo o canal ao mesmo tempo (a primeira autenticada vence).</summary>
    public const int MaxPendingHandshakes = 2;

    /// <summary>Falhas de autenticação toleradas antes de encerrar (a chave tem 256 bits; isto só limita abuso).</summary>
    public const int MaxFailedHandshakes = 8;

    /// <summary>Mensagens por segundo, em média, e a rajada permitida (um toque segurado repete a ~12/s).</summary>
    public const double InputRate = 30;
    public const double InputBurst = 60;

    private readonly Lock _gate = new();
    private int _pending;
    private int _failed;
    private TimeSpan _deadline = startedAt + PairingLifetime;
    private double _tokens = InputBurst;
    private TimeSpan _lastRefill = startedAt;

    public PhoneSessionState State { get; private set; } = PhoneSessionState.Waiting;
    public PhoneEndReason? EndReason { get; private set; }

    /// <summary>IP do celular que se autenticou (mostrado para o usuário confirmar).</summary>
    public string? PhoneAddress { get; private set; }

    public string? VerificationCode { get; private set; }

    /// <summary>A página só é servida enquanto o QR Code está valendo e ninguém se autenticou.</summary>
    public bool CanServePage(TimeSpan now)
    {
        lock (_gate) return State == PhoneSessionState.Waiting && now < _deadline;
    }

    /// <summary>Uma conexão pede o canal. Falso: pareamento encerrado, expirado, já autenticado ou conexões demais.</summary>
    public bool TryBeginHandshake(TimeSpan now)
    {
        lock (_gate)
        {
            if (State != PhoneSessionState.Waiting || now >= _deadline || _pending >= MaxPendingHandshakes) return false;
            _pending++;
            return true;
        }
    }

    /// <summary>
    /// A conexão não provou ter a chave (ou demorou): sai sem afetar a sessão, até o limite de falhas. Verdadeiro quando
    /// esta falha encerrou a sessão (<see cref="PhoneEndReason.TooManyAttempts"/>).
    /// </summary>
    public bool HandshakeFailed()
    {
        lock (_gate)
        {
            if (_pending > 0) _pending--;
            if (State != PhoneSessionState.Waiting) return false;
            return ++_failed >= MaxFailedHandshakes && EndLocked(PhoneEndReason.TooManyAttempts);
        }
    }

    /// <summary>
    /// A conexão provou ter a chave: a sessão passa a ser dela (as outras são recusadas). Falso: outra venceu antes ou a
    /// sessão acabou; a conexão deve ser fechada.
    /// </summary>
    public bool TryAuthenticate(string phoneAddress, string verificationCode, TimeSpan now)
    {
        lock (_gate)
        {
            if (_pending > 0) _pending--;
            if (State != PhoneSessionState.Waiting || now >= _deadline) return false;
            State = PhoneSessionState.AwaitingConfirmation;
            PhoneAddress = phoneAddress;
            VerificationCode = verificationCode;
            _deadline = now + ConfirmationTimeout;
            return true;
        }
    }

    /// <summary>O usuário permitiu no PC. Falso: não havia celular aguardando.</summary>
    public bool Confirm(TimeSpan now)
    {
        lock (_gate)
        {
            if (State != PhoneSessionState.AwaitingConfirmation || now >= _deadline) return false;
            State = PhoneSessionState.Connected;
            _lastRefill = now;
            return true;
        }
    }

    /// <summary>Encerra (idempotente). Verdadeiro só na primeira vez: quem encerrou avisa a interface.</summary>
    public bool End(PhoneEndReason reason)
    {
        lock (_gate) return EndLocked(reason);
    }

    private bool EndLocked(PhoneEndReason reason)
    {
        if (State == PhoneSessionState.Ended) return false;
        State = PhoneSessionState.Ended;
        EndReason = reason;
        return true;
    }

    /// <summary>Prazos: QR Code sem uso e celular sem permissão. Devolve o motivo quando esta chamada encerrou a sessão.</summary>
    public PhoneEndReason? Tick(TimeSpan now)
    {
        lock (_gate)
        {
            if (now < _deadline) return null;
            var reason = State switch
            {
                PhoneSessionState.Waiting => PhoneEndReason.Expired,
                PhoneSessionState.AwaitingConfirmation => PhoneEndReason.ConfirmationTimedOut,
                _ => (PhoneEndReason?)null,
            };
            return reason is { } r && EndLocked(r) ? r : null;
        }
    }

    /// <summary>
    /// Uma mensagem de entrada pode ser entregue? Só conectado e dentro do limite de taxa (balde de fichas); o excesso é
    /// descartado, não enfileirado.
    /// </summary>
    public bool TryAcceptInput(TimeSpan now)
    {
        lock (_gate)
        {
            if (State != PhoneSessionState.Connected) return false;
            _tokens = Math.Min(InputBurst, _tokens + ((now - _lastRefill).TotalSeconds * InputRate));
            _lastRefill = now;
            if (_tokens < 1) return false;
            _tokens--;
            return true;
        }
    }
}
