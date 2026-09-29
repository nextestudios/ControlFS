using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Core.Remote;
using ControlFS.Infrastructure.Archives;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

/// <summary>
/// Celular como controle (#223) do lado da interface: Menu → Conectar celular, QR Code, permissão no PC, ações e texto
/// do celular e o bloqueio em confirmações sensíveis. A rede real está em WindowsIntegrationTests (PhoneLinkIntegrationTests).
/// </summary>
public class PhoneJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private sealed class FakeLink : IPhoneLink
    {
        public int Session { get; private set; }
        public List<PhoneEndReason> Stopped { get; } = [];
        public bool Confirmed { get; private set; }
        public (bool Keyboard, bool Locked) Ui { get; private set; }

        public PhonePairing Start(int addressIndex = 0) =>
            new(++Session, $"http://192.168.0.10:50123/abcdefghijklmnopqrstuv#k={new string('K', 43)}", "192.168.0.10", 0, 1, PhoneSession.PairingLifetime);

        public bool Confirm(int session)
        {
            Confirmed = session == Session;
            if (Confirmed) Raise(new PhoneConnected(session, "192.168.0.23"));
            return Confirmed;
        }

        public void Stop(int session, PhoneEndReason reason) => Stopped.Add(reason);
        public void PublishUi(bool keyboard, bool locked) => Ui = (keyboard, locked);
        public void Raise(PhoneLinkEvent e) => Event?.Invoke(e);
        public event Action<PhoneLinkEvent>? Event;
        public void Dispose() { }
    }

    /// <summary>Com Lixeira, para abrir a confirmação sensível de exclusão; nada é executado.</summary>
    private sealed class NoOpFileOperations : IFileOperationService
    {
        public bool CanRecycle(string path) => true;
        public FileEntry Rename(string path, string newName) => throw new InvalidOperationException("não usado");
        public Task<OperationResult> RunAsync(FileOperationRequest request, IConflictInteraction conflicts, IProgress<OperationProgress>? progress, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("não usado");
    }

    [Fact]
    public void Phone_pairs_after_confirmation_on_the_pc_then_navigates_types_and_is_blocked_in_sensitive_dialogs() => UiContext.Run(async () =>
    {
        var files = _tmp.Path;
        File.WriteAllText(Path.Join(files, "a.txt"), "a");
        var app = new AppController(new TestFileSystem(files), new ArchiveService(), fileOperations: new NoOpFileOperations());
        var link = new FakeLink();
        app.AttachPhoneLink(link);
        app.Start();
        var d = new Driver(app);

        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Conectar celular");
        var pairing = await d.WaitDialog("Conectar celular");
        Assert.NotNull(pairing.QrModules);
        Assert.Contains(pairing.Lines, l => l.Value.StartsWith("http://192.168.0.10:50123/", StringComparison.Ordinal));
        Assert.Equal(PhoneLinkState.Waiting, app.PhoneState);

        // Antes da permissão, nada do celular vira entrada.
        link.Raise(new PhoneCommand(link.Session, new PhoneMessage(PhoneMessageKind.Action, InputAction.Back)));
        link.Raise(new PhoneAwaitingConfirmation(link.Session, "192.168.0.23", "123 456"));
        var allow = await d.WaitDialog("Permitir este celular?");
        Assert.True(allow.IsSensitive);
        Assert.Contains(("Código", "123 456"), allow.Lines);
        Assert.Equal("Recusar", allow.Options[allow.FocusIndex].Label); // um toque perdido não permite
        d.ChooseOption(allow, "Permitir");
        await UiContext.WaitUntil(() => app.PhoneState == PhoneLinkState.Connected, "celular conectado");
        Assert.Null(app.TopModal);
        Assert.Contains("192.168.0.23", app.PhoneStatusText, StringComparison.Ordinal);

        // Ações semânticas e texto no campo do teclado na tela.
        link.Raise(new PhoneCommand(link.Session, new PhoneMessage(PhoneMessageKind.Action, InputAction.OpenAppMenu)));
        await d.ChooseMenu("Ir para caminho");
        var keyboard = await d.WaitKeyboard();
        await UiContext.WaitUntil(() => link.Ui == (true, false), "página avisada do campo de texto");
        link.Raise(new PhoneCommand(link.Session, new PhoneMessage(PhoneMessageKind.Text, Text: "abc")));
        link.Raise(new PhoneCommand(link.Session, new PhoneMessage(PhoneMessageKind.Backspace)));
        await UiContext.WaitUntil(() => keyboard.Keyboard.Text.EndsWith("ab", StringComparison.Ordinal), "texto do celular no campo");
        link.Raise(new PhoneCommand(link.Session, new PhoneMessage(PhoneMessageKind.Action, InputAction.Back)));
        await UiContext.WaitUntil(() => app.TopModal is null, "teclado fechado pelo celular");

        // Confirmação sensível: o celular não confirma, só volta.
        d.Press(InputAction.Confirm); // abre o primeiro local do início (a pasta de teste)
        await d.FocusItem("a.txt");
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Excluir");
        var delete = await d.WaitDialog("Mover 1 item para a Lixeira?");
        await UiContext.WaitUntil(() => link.Ui.Locked, "página avisada do bloqueio");
        var focus = delete.FocusIndex;
        link.Raise(new PhoneCommand(link.Session, new PhoneMessage(PhoneMessageKind.Action, InputAction.NavigateRight)));
        link.Raise(new PhoneCommand(link.Session, new PhoneMessage(PhoneMessageKind.Action, InputAction.Confirm)));
        await UiContext.WaitUntil(() => app.StatusMessage?.Contains("só no PC", StringComparison.Ordinal) == true, "aviso de bloqueio");
        Assert.Same(delete, app.TopModal);
        Assert.Equal(focus, delete.FocusIndex);
        link.Raise(new PhoneCommand(link.Session, new PhoneMessage(PhoneMessageKind.Action, InputAction.Back)));
        await UiContext.WaitUntil(() => app.TopModal is null, "diálogo cancelado pelo celular");
        Assert.True(File.Exists(Path.Join(files, "a.txt")));

        // Menu → Desconectar celular encerra a sessão pelo PC.
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Desconectar celular");
        Assert.Equal([PhoneEndReason.Disconnected], link.Stopped);
        Assert.Equal(PhoneLinkState.None, app.PhoneState);
        Assert.Null(app.PhoneStatusText);
    });

    [Fact]
    public void The_pairing_dialog_says_how_far_a_phone_got_and_why_it_failed() => UiContext.Run(async () =>
    {
        var app = new AppController(new TestFileSystem(_tmp.MakeDir("files")), new ArchiveService());
        var link = new FakeLink();
        app.AttachPhoneLink(link);
        app.Start();
        var d = new Driver(app);

        app.BeginPhonePairing();
        var pairing = await d.WaitDialog("Conectar celular");
        var lines = pairing.Lines.Count;
        link.Raise(new PhoneAttempt(link.Session, PhoneAttemptStage.PageOpened, "page"));
        await UiContext.WaitUntil(() => pairing.Lines.Any(l => l.Label == "Estado" && l.Value.Contains("abriu a página", StringComparison.Ordinal)), "página aberta");
        link.Raise(new PhoneAttempt(link.Session, PhoneAttemptStage.HandshakeFailed, "hello-timeout"));
        await UiContext.WaitUntil(() => pairing.Lines.Any(l => l.Label == "Estado" && l.Value.Contains("hello-timeout", StringComparison.Ordinal)), "motivo da falha");
        Assert.Equal(lines + 1, pairing.Lines.Count); // uma linha "Estado", trocada a cada aviso
        Assert.Equal(PhoneLinkState.Waiting, app.PhoneState); // a sessão segue esperando: o celular pode tentar de novo
    });

    [Fact]
    public void Losing_the_phone_closes_its_dialogs_and_stale_sessions_are_ignored() => UiContext.Run(async () =>
    {
        var app = new AppController(new TestFileSystem(_tmp.MakeDir("files")), new ArchiveService());
        var link = new FakeLink();
        app.AttachPhoneLink(link);
        app.Start();
        var d = new Driver(app);

        app.BeginPhonePairing();
        var old = link.Session;
        await d.WaitDialog("Conectar celular");
        app.BeginPhonePairing(); // outra rede: sessão nova
        link.Raise(new PhoneAwaitingConfirmation(old, "192.168.0.66", "000 000"));
        link.Raise(new PhoneAwaitingConfirmation(link.Session, "192.168.0.23", "123 456"));
        var allow = await d.WaitDialog("Permitir este celular?");
        Assert.Contains(("Celular (IP)", "192.168.0.23"), allow.Lines);

        link.Raise(new PhoneEnded(link.Session, PhoneEndReason.PhoneLeft));
        await UiContext.WaitUntil(() => app.PhoneState == PhoneLinkState.None, "sessão encerrada");
        Assert.Null(app.TopModal);
        Assert.Contains("desconectou", app.StatusMessage, StringComparison.Ordinal);
        Assert.False(link.Confirmed);
    });
}
