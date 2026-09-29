using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Remote;

namespace ControlFS.Application;

public enum PhoneLinkState
{
    None,

    /// <summary>QR Code na tela, esperando o celular.</summary>
    Waiting,

    /// <summary>Um celular conectou; o usuário precisa permitir no PC.</summary>
    Confirming,

    Connected,
}

/// <summary>
/// Celular como controle (#223): Menu → Conectar celular mostra um QR Code de uma sessão local e temporária; o celular
/// que conecta aparece com IP e código para o usuário permitir aqui. Permitido, cada toque dele vira a mesma ação
/// semântica de um botão, e o texto digitado nele vai para o campo do teclado na tela. Enquanto uma confirmação
/// importante está aberta (excluir, substituir, desfazer, mapear controle), o celular só consegue Voltar.
/// </summary>
public sealed partial class AppController
{
    private IPhoneLink? _phoneLink;
    private PhonePairing? _phonePairing;
    private DialogModal? _phoneDialog;
    private IReadOnlyList<(string Label, string Value)> _phoneLines = [];

    public PhoneLinkState PhoneState { get; private set; }

    /// <summary>IP do celular conectado ou aguardando permissão.</summary>
    public string? PhoneAddress { get; private set; }

    /// <summary>Linha de estado para o cabeçalho (null: nenhuma sessão).</summary>
    public string? PhoneStatusText => PhoneState switch
    {
        PhoneLinkState.Waiting => "Celular: aguardando o QR Code",
        PhoneLinkState.Confirming => "Celular: aguardando permissão",
        PhoneLinkState.Connected => $"Celular conectado ({PhoneAddress})",
        _ => null,
    };

    /// <summary>Liga o canal com o celular (a janela cria o servidor; os eventos chegam em outra thread e voltam para a UI).</summary>
    public void AttachPhoneLink(IPhoneLink link)
    {
        ArgumentNullException.ThrowIfNull(link);
        _phoneLink = link;
        link.Event += e => _ui.Post(_ => OnPhoneEvent(e), null);
    }

    private MenuItem PhoneMenuItem() => PhoneState == PhoneLinkState.Connected
        ? new MenuItem("Desconectar celular", () => StopPhone(PhoneEndReason.Disconnected),
            Detail: $"Conectado: {PhoneAddress}. O celular para de controlar o ControlFS na hora.", Icon: ActionIcon.Phone, Section: "ControlFS")
        : new MenuItem("Conectar celular…", () => BeginPhonePairing(), _phoneLink is null ? "Indisponível nesta compilação." : null,
            Detail: "Use o celular para navegar e digitar, pela rede local (sem nuvem e sem instalar app).", Icon: ActionIcon.Phone, Section: "ControlFS");

    /// <summary>Abre uma sessão nova e mostra o QR Code. <paramref name="addressIndex"/>: qual rede local do PC usar.</summary>
    public void BeginPhonePairing(int addressIndex = 0)
    {
        if (_phoneLink is null) return;
        ClosePhoneDialog();
        PhonePairing pairing;
        try
        {
            pairing = _phoneLink.Start(addressIndex);
        }
        catch (PhoneLinkException ex)
        {
            ResetPhone();
            ShowMessage("Não foi possível conectar o celular", [], ex.Message, ActionIcon.Error);
            return;
        }
        _phonePairing = pairing;
        PhoneState = PhoneLinkState.Waiting;
        PhoneAddress = null;

        var network = pairing.AddressCount > 1 ? $"{pairing.Address} ({pairing.AddressIndex + 1} de {pairing.AddressCount})" : pairing.Address;
        var dialog = new DialogModal("Conectar celular", [("Endereço", pairing.Url), ("Rede do PC", network)])
        {
            Icon = ActionIcon.Phone,
            QrModules = QrCode.Encode(pairing.Url),
            Message = $"Celular na mesma rede do PC (não use a rede de convidados). Vale {pairing.Lifetime.TotalMinutes:0} min, para uma conexão. Se a página não abrir, permita o ControlFS em redes privadas no Firewall do Windows.",
        };
        _phoneLines = dialog.Lines;
        var cancel = new DialogOption("Cancelar", DialogOptionKind.Safe, () => StopPhone(PhoneEndReason.Disconnected), ActionIcon.Close);
        dialog.Options.Add(cancel);
        if (pairing.AddressCount > 1)
            dialog.Options.Add(new DialogOption("Usar outra rede do PC", DialogOptionKind.Primary, () => BeginPhonePairing(pairing.AddressIndex + 1), ActionIcon.Refresh));
        dialog.BackOption = cancel;
        _phoneDialog = dialog;
        PushModal(dialog);
    }

    /// <summary>Encerra a sessão pelo PC (cancelar, recusar, desconectar): para de escutar e descarta a chave na hora.</summary>
    internal void StopPhone(PhoneEndReason reason)
    {
        if (_phonePairing is { } pairing) _phoneLink?.Stop(pairing.Session, reason);
        ResetPhone();
        StatusMessage = PhoneEndMessage(reason);
        RaiseChanged();
    }

    private void ResetPhone()
    {
        ClosePhoneDialog();
        _phonePairing = null;
        PhoneState = PhoneLinkState.None;
        PhoneAddress = null;
    }

    private void ClosePhoneDialog()
    {
        if (_phoneDialog is { } dialog) CloseModal(dialog);
        _phoneDialog = null;
    }

    private void OnPhoneEvent(PhoneLinkEvent e)
    {
        if (_phonePairing is not { } pairing || e.Session != pairing.Session) return; // sessão antiga
        switch (e)
        {
            case PhoneAwaitingConfirmation waiting:
                AskToAllowPhone(pairing.Session, waiting);
                break;
            case PhoneAttempt attempt:
                ShowPhoneAttempt(attempt);
                break;
            case PhoneConnected connected:
                PhoneState = PhoneLinkState.Connected;
                PhoneAddress = connected.PhoneAddress;
                StatusMessage = $"Celular conectado ({connected.PhoneAddress}). Para encerrar: Menu → Desconectar celular.";
                PublishPhoneUi();
                break;
            case PhoneCommand command:
                HandlePhone(command.Message);
                return; // Handle/Type* já redesenham
            case PhoneEnded ended:
                ResetPhone();
                StatusMessage = PhoneEndMessage(ended.Reason);
                break;
        }
        RaiseChanged();
    }

    /// <summary>
    /// Mostra no diálogo do QR Code até onde o celular chegou e por que falhou (#259), para o PC e o celular não ficarem
    /// em "conectando" sem explicação. A sessão continua esperando: o celular pode tentar de novo.
    /// </summary>
    private void ShowPhoneAttempt(PhoneAttempt attempt)
    {
        if (PhoneState != PhoneLinkState.Waiting || _phoneDialog is not { } dialog) return;
        var text = attempt.Stage switch
        {
            PhoneAttemptStage.PageOpened => "Um celular abriu a página; conectando…",
            PhoneAttemptStage.HandshakeFailed => $"Um celular chegou, mas a verificação segura falhou ({attempt.Code}). Ele pode tentar de novo.",
            _ => $"Um celular tentou conectar e foi recusado ({attempt.Code}). Se persistir, gere um QR Code novo.",
        };
        dialog.Lines = [.. _phoneLines, ("Estado", text)];
    }

    /// <summary>
    /// Confirmação no PC: IP e código do celular que conectou. O foco começa em Recusar (um toque perdido não permite) e
    /// o diálogo é sensível (outro controle não assume; o celular ainda não comanda nada).
    /// </summary>
    private void AskToAllowPhone(int session, PhoneAwaitingConfirmation waiting)
    {
        ClosePhoneDialog();
        PhoneState = PhoneLinkState.Confirming;
        PhoneAddress = waiting.PhoneAddress;
        var dialog = new DialogModal("Permitir este celular?", [("Celular (IP)", waiting.PhoneAddress), ("Código", waiting.VerificationCode)], sensitive: true)
        {
            Icon = ActionIcon.Phone,
            Message = "Permita só se o celular na sua mão mostra este mesmo código. Permitido, ele navega e digita no ControlFS até você desconectar; "
                + "confirmações importantes (excluir, substituir) continuam só no PC.",
        };
        var allow = new DialogOption("Permitir", DialogOptionKind.Primary, () =>
        {
            ClosePhoneDialog();
            if (_phoneLink?.Confirm(session) != true)
            {
                ResetPhone();
                StatusMessage = "O celular desconectou antes de ser permitido.";
            }
        }, ActionIcon.Accept);
        var reject = new DialogOption("Recusar", DialogOptionKind.Safe, () => StopPhone(PhoneEndReason.Rejected), ActionIcon.Close);
        dialog.Options.Add(allow);
        dialog.Options.Add(reject);
        dialog.BackOption = reject;
        dialog.FocusIndex = 1;
        _phoneDialog = dialog;
        PushModal(dialog);
    }

    /// <summary>
    /// Uma mensagem do celular permitido. Com uma confirmação sensível aberta, só Voltar passa (a opção segura); texto e
    /// Enter só valem com o teclado na tela aberto.
    /// </summary>
    internal void HandlePhone(PhoneMessage message)
    {
        if (PhoneState != PhoneLinkState.Connected) return;
        var locked = TopModal?.IsSensitive == true;
        switch (message.Kind)
        {
            case PhoneMessageKind.Action when locked && message.Action != InputAction.Back:
                StatusMessage = "Esta confirmação é só no PC: o celular pode apenas voltar.";
                RaiseChanged();
                break;
            case PhoneMessageKind.Action:
                Handle(message.Action);
                break;
            case PhoneMessageKind.Text when !locked && message.Text is { } text:
                TypeText(text);
                break;
            case PhoneMessageKind.Backspace when !locked:
                TypeBackspace();
                break;
            case PhoneMessageKind.Enter when !locked && TopModal is KeyboardModal:
                Handle(InputAction.OpenAppMenu); // OK/Concluir do teclado na tela, como Enter no teclado físico
                break;
        }
    }

    /// <summary>A página do celular mostra o campo de texto e o bloqueio conforme a tela (só envia quando muda).</summary>
    private void PublishPhoneUi()
    {
        if (PhoneState == PhoneLinkState.Connected) _phoneLink?.PublishUi(TopModal is KeyboardModal { IsBusy: false }, TopModal?.IsSensitive == true);
    }

    private string PhoneEndMessage(PhoneEndReason reason) => reason switch
    {
        PhoneEndReason.Disconnected => "Celular desconectado. Para conectar de novo: Menu → Conectar celular.",
        PhoneEndReason.PhoneLeft => "O celular desconectou (página fechada ou rede perdida). Para conectar de novo: Menu → Conectar celular.",
        PhoneEndReason.Expired => "O QR Code expirou sem uso. Para tentar de novo: Menu → Conectar celular.",
        PhoneEndReason.ConfirmationTimedOut => "Ninguém permitiu o celular a tempo; a conexão foi encerrada.",
        PhoneEndReason.Rejected => "Celular recusado; a conexão foi encerrada.",
        PhoneEndReason.AuthenticationFailed => "Mensagem inválida do celular; a conexão foi encerrada por segurança.",
        PhoneEndReason.TooManyAttempts => "Tentativas demais de conectar sem o código certo; o pareamento foi encerrado.",
        _ => StatusMessage ?? string.Empty,
    };
}
