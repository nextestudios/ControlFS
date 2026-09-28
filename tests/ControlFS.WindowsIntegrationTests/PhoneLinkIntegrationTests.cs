using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Remote;
using ControlFS.Infrastructure.Archives;
using ControlFS.Infrastructure.Remote;
using ControlFS.Infrastructure.Windows.FileSystem;
using ControlFS.UnitTests.Support;
using Xunit;

namespace ControlFS.WindowsIntegrationTests;

/// <summary>
/// Celular como controle (#223) com a pilha real do Windows: o servidor escuta em 127.0.0.1, a página é servida, um
/// ClientWebSocket faz o papel do celular (chaves derivadas como a página faz) e o AppController recebe navegação e
/// texto só depois da permissão no PC. Depois de Desconectar, nada escuta na porta.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class PhoneLinkIntegrationTests : IDisposable
{
    private readonly string _root = Path.Join(Path.GetTempPath(), "controlfs-phone-tests", Guid.NewGuid().ToString("N"));

    public PhoneLinkIntegrationTests()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Requer Windows.");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>O "celular": mesmo protocolo da página (hello com número aleatório, quadros AES-GCM com contador).</summary>
    private sealed class Phone(ClientWebSocket socket, byte[] key, byte[] sessionId) : IDisposable
    {
        private readonly FrameSealer _out = new(PhoneCrypto.DeriveKeys(key, sessionId).PhoneToPc);
        private readonly FrameOpener _in = new(PhoneCrypto.DeriveKeys(key, sessionId).PcToPhone);

        public ClientWebSocket Socket => socket;

        public Task Send(string json) => socket.SendAsync(_out.Seal(Encoding.UTF8.GetBytes(json)), WebSocketMessageType.Binary, true, CancellationToken.None);

        public Task SendRaw(byte[] frame) => socket.SendAsync(frame, WebSocketMessageType.Binary, true, CancellationToken.None);

        public byte[] Seal(string json) => _out.Seal(Encoding.UTF8.GetBytes(json));

        public async Task<string?> Receive()
        {
            var buffer = new byte[4096];
            var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            Assert.True(_in.TryOpen(buffer.AsSpan(0, result.Count), out var plaintext));
            return Encoding.UTF8.GetString(plaintext);
        }

        public void Dispose()
        {
            _out.Dispose();
            _in.Dispose();
            socket.Dispose();
        }
    }

    private static (Uri Page, Uri Channel, string Origin, byte[] Key, byte[] SessionId) ParseUrl(string url)
    {
        var fragment = url.IndexOf("#k=", StringComparison.Ordinal);
        var page = new Uri(url[..fragment]);
        var key = Base64Url.TryDecode(url[(fragment + 3)..])!;
        var sessionId = Base64Url.TryDecode(page.AbsolutePath[1..])!;
        return (page, new Uri($"ws://{page.Authority}{page.AbsolutePath}/ws"), $"http://{page.Authority}", key, sessionId);
    }

    private static async Task<Phone> ConnectPhone(Uri channel, string origin, byte[] key, byte[] sessionId)
    {
        var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Origin", origin);
        await socket.ConnectAsync(channel, CancellationToken.None);
        var phone = new Phone(socket, key, sessionId);
        await phone.Send($"{{\"t\":\"hello\",\"n\":\"{Base64Url.Encode(new byte[16])}\"}}");
        return phone;
    }

    [Fact]
    public void Phone_pairs_over_the_real_listener_controls_the_app_and_the_port_closes_on_disconnect() => UiContext.Run(async () =>
    {
        File.WriteAllText(Path.Join(_root, "a.txt"), "a");
        File.WriteAllText(Path.Join(_root, "b.txt"), "b");
        using var server = new PhoneLinkServer(() => [IPAddress.Loopback]);
        var app = new AppController(new LocalFileSystemProvider(), new ArchiveService());
        app.AttachPhoneLink(server);
        app.Start();

        app.BeginPhonePairing();
        var pairing = Assert.IsType<DialogModal>(app.TopModal);
        var url = pairing.Lines.Single(l => l.Label == "Endereço").Value;
        var (page, channel, origin, key, sessionId) = ParseUrl(url);
        Assert.Equal(page.Port, server.ListeningPort);

        using (var http = new HttpClient())
        {
            var response = await http.GetAsync(page);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("script-src 'sha256-", string.Join(" ", response.Headers.GetValues("Content-Security-Policy")), StringComparison.Ordinal);
            Assert.Contains("nobleControlFS", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        // Origin de outro site: o canal é recusado antes do WebSocket.
        using (var foreign = new ClientWebSocket())
        {
            foreign.Options.SetRequestHeader("Origin", "http://evil.example");
            await Assert.ThrowsAnyAsync<WebSocketException>(() => foreign.ConnectAsync(channel, CancellationToken.None));
        }

        using var phone = await ConnectPhone(channel, origin, key, sessionId);
        Assert.Equal("{\"t\":\"state\",\"s\":\"confirm\"}", await phone.Receive());
        var allow = await WaitDialog(app, "Permitir este celular?");
        Assert.Contains(("Código", PhoneCrypto.VerificationCode(key, new byte[16])), allow.Lines);
        Assert.Null(server.ListeningPort); // a chave agora é deste celular: a porta parou de escutar

        // Um segundo celular com o mesmo QR Code não consegue nem conectar.
        using (var second = new ClientWebSocket())
        {
            second.Options.SetRequestHeader("Origin", origin);
            await Assert.ThrowsAnyAsync<WebSocketException>(() => second.ConnectAsync(channel, CancellationToken.None));
        }

        // Sem a permissão no PC, nada vira entrada.
        await phone.Send("{\"t\":\"a\",\"a\":\"back\"}");
        await Task.Delay(200);
        Assert.Same(allow, app.TopModal);

        app.PointerChooseModalOption(allow.Options.FindIndex(o => o.Label == "Permitir"));
        Assert.Equal("{\"t\":\"state\",\"s\":\"ready\"}", await phone.Receive());
        await UiContext.WaitUntil(() => app.PhoneState == PhoneLinkState.Connected, "celular conectado");

        await phone.Send("{\"t\":\"a\",\"a\":\"menu\"}");
        await UiContext.WaitUntil(() => app.TopModal is MenuModal { Title: "Menu" }, "menu aberto pelo celular");
        await phone.Send("{\"t\":\"a\",\"a\":\"back\"}");
        await UiContext.WaitUntil(() => app.TopModal is null, "menu fechado pelo celular");

        app.Handle(InputAction.OpenAppMenu);
        app.PointerChooseModalOption(((MenuModal)app.TopModal!).Items.ToList().FindIndex(i => i.Label == "Ir para caminho…"));
        var keyboard = Assert.IsType<KeyboardModal>(app.TopModal);
        keyboard.Keyboard.SelectAll();
        await phone.Send($"{{\"t\":\"text\",\"v\":\"{_root.Replace("\\", "\\\\", StringComparison.Ordinal)}x\"}}");
        await phone.Send("{\"t\":\"key\",\"k\":\"backspace\"}");
        await UiContext.WaitUntil(() => keyboard.Keyboard.Text == _root, "texto do celular no campo");
        await phone.Send("{\"t\":\"key\",\"k\":\"enter\"}");
        await UiContext.WaitUntil(() => app.Screen == Screen.Browser && app.Browser.List.Items.Any(i => i.Name == "a.txt"), "pasta aberta pelo Enter do celular");
        var focus = app.Browser.List.FocusIndex;
        await phone.Send("{\"t\":\"a\",\"a\":\"down\"}");
        await UiContext.WaitUntil(() => app.Browser.List.FocusIndex == focus + 1, "foco movido pelo celular");

        // Menu → Desconectar celular: o celular é avisado e nada escuta mais na porta.
        app.Handle(InputAction.OpenAppMenu);
        var menu = Assert.IsType<MenuModal>(app.TopModal);
        app.PointerChooseModalOption(menu.Items.ToList().FindIndex(i => i.Label == "Desconectar celular"));
        Assert.Equal(PhoneLinkState.None, app.PhoneState);
        string? message;
        do message = await phone.Receive();
        while (message is not null && !message.StartsWith("{\"t\":\"end\"", StringComparison.Ordinal));
        Assert.Equal("{\"t\":\"end\",\"r\":\"Disconnected\"}", message);
        using var probe = new TcpClient();
        await Assert.ThrowsAnyAsync<SocketException>(() => probe.ConnectAsync(IPAddress.Loopback, page.Port));
    });

    [Fact]
    public void Tampered_frame_ends_the_session() => UiContext.Run(async () =>
    {
        using var server = new PhoneLinkServer(() => [IPAddress.Loopback]);
        var app = new AppController(new LocalFileSystemProvider(), new ArchiveService());
        app.AttachPhoneLink(server);
        app.Start();
        app.BeginPhonePairing();
        var (_, channel, origin, key, sessionId) = ParseUrl(((DialogModal)app.TopModal!).Lines.Single(l => l.Label == "Endereço").Value);
        using var phone = await ConnectPhone(channel, origin, key, sessionId);
        await phone.Receive();
        var allow = await WaitDialog(app, "Permitir este celular?");
        app.PointerChooseModalOption(allow.Options.FindIndex(o => o.Label == "Permitir"));
        await UiContext.WaitUntil(() => app.PhoneState == PhoneLinkState.Connected, "celular conectado");

        var frame = phone.Seal("{\"t\":\"a\",\"a\":\"menu\"}");
        frame[^1] ^= 1;
        await phone.SendRaw(frame);
        await UiContext.WaitUntil(() => app.PhoneState == PhoneLinkState.None, "sessão encerrada");
        Assert.Contains("segurança", app.StatusMessage, StringComparison.Ordinal);
        Assert.Null(app.TopModal); // a mensagem adulterada não abriu o menu
    });

    private static async Task<DialogModal> WaitDialog(AppController app, string title)
    {
        await UiContext.WaitUntil(() => app.TopModal is DialogModal d && d.Title == title, title);
        return (DialogModal)app.TopModal!;
    }
}
