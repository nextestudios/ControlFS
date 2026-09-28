using System.Text;
using ControlFS.Core.Actions;
using ControlFS.Core.Remote;
using ControlFS.Infrastructure.Remote;

namespace ControlFS.UnitTests.Remote;

/// <summary>
/// Fronteira de segurança do celular como controle (#223): o leitor de HTTP mínimo, os quadros cifrados, a máquina de
/// estados da sessão e as mensagens aceitas. O caminho completo com rede está em WindowsIntegrationTests.
/// </summary>
public class PhoneChannelTests
{
    private static HttpParseStatus Parse(string head) => HttpRequestParser.TryParse(Encoding.UTF8.GetBytes(head), out _);

    [Fact]
    public void Http_parser_accepts_only_a_bare_get_within_limits()
    {
        const string ok = "GET /abc_-9/ws HTTP/1.1\r\nHost: 10.0.0.2:50000\r\nUpgrade: websocket\r\n\r\n";
        Assert.Equal(HttpParseStatus.Ok, HttpRequestParser.TryParse(Encoding.ASCII.GetBytes(ok), out var head));
        Assert.Equal("/abc_-9/ws", head!.Target);
        Assert.Equal("10.0.0.2:50000", head.Header("host"));

        Assert.Equal(HttpParseStatus.Incomplete, Parse("GET / HTTP/1.1\r\nHost: a\r\n"));
        Assert.Equal(HttpParseStatus.TooLarge, Parse("GET / HTTP/1.1\r\nX: " + new string('a', HttpRequestParser.MaxHeadBytes)));
        Assert.Equal(HttpParseStatus.Invalid, Parse("POST / HTTP/1.1\r\nHost: a\r\n\r\n"));
        Assert.Equal(HttpParseStatus.Invalid, Parse("GET / HTTP/1.0\r\nHost: a\r\n\r\n"));
        Assert.Equal(HttpParseStatus.Invalid, Parse("GET /a?x=1 HTTP/1.1\r\nHost: a\r\n\r\n")); // consulta
        Assert.Equal(HttpParseStatus.Invalid, Parse("GET /../a HTTP/1.1\r\nHost: a\r\n\r\n"));
        Assert.Equal(HttpParseStatus.Invalid, Parse("GET / HTTP/1.1\r\nHost: a\r\nHost: b\r\n\r\n")); // Host repetido
        Assert.Equal(HttpParseStatus.Invalid, Parse("GET / HTTP/1.1\r\nHost: a\nX: b\r\n\r\n")); // LF solto
        Assert.Equal(HttpParseStatus.Invalid, Parse("GET / HTTP/1.1\r\nHost : a\r\n\r\n"));
        Assert.Equal(HttpParseStatus.Invalid, Parse("GET / HTTP/1.1\r\nHost: a\r\n folded\r\n\r\n"));
        Assert.Equal(HttpParseStatus.Invalid, Parse("GET / HTTP/1.1\r\nHost: á\r\n\r\n")); // fora do ASCII
        Assert.Equal(HttpParseStatus.Invalid, Parse("GET / HTTP/1.1\r\nContent-Length: 3\r\n\r\n"));
        Assert.Equal(HttpParseStatus.Invalid, Parse("GET / HTTP/1.1\r\nTransfer-Encoding: chunked\r\n\r\n"));
        Assert.Equal(HttpParseStatus.Invalid, Parse("GET / HTTP/1.1\r\nHost: a\r\n\r\nGET / HTTP/1.1\r\n\r\n")); // nada depois
        var many = "GET / HTTP/1.1\r\n" + string.Concat(Enumerable.Range(0, HttpRequestParser.MaxHeaders + 1).Select(i => $"X{i}: v\r\n")) + "\r\n";
        Assert.Equal(HttpParseStatus.Invalid, Parse(many));
    }

    [Fact]
    public void WebSocket_accept_follows_rfc6455_and_rejects_malformed_keys()
    {
        Assert.Equal("s3pPLMBiTxaQ9kYGzzhZRbK+xOo=", HttpRequestParser.WebSocketAccept("dGhlIHNhbXBsZSBub25jZQ=="));
        Assert.Null(HttpRequestParser.WebSocketAccept(null));
        Assert.Null(HttpRequestParser.WebSocketAccept("not base64 at all!!!!!!!"));
        Assert.Null(HttpRequestParser.WebSocketAccept(Convert.ToBase64String(new byte[15]) + "AA"));
    }

    private static (byte[] Key, byte[] Sid) Fixed() =>
        (Enumerable.Range(0, 32).Select(i => (byte)i).ToArray(), Enumerable.Range(100, 16).Select(i => (byte)i).ToArray());

    [Fact]
    public void Keys_code_and_frames_match_the_phone_page_libraries()
    {
        // Valores produzidos pelo @noble (hkdf/sha256, aes gcm) embutido na página, com as mesmas entradas.
        var (key, sid) = Fixed();
        var (phoneToPc, pcToPhone) = PhoneCrypto.DeriveKeys(key, sid);
        Assert.Equal("efed7363a788bad64aa97ab921be5e9cec1e69a5ffd282e455805e889613dabd", Convert.ToHexStringLower(phoneToPc));
        Assert.Equal("14ab162d9b235be45258d949634c4bfb06503d3a271d8e49d2a4a54060026e82", Convert.ToHexStringLower(pcToPhone));
        Assert.Equal("136 500", PhoneCrypto.VerificationCode(key, Enumerable.Range(200, 16).Select(i => (byte)i).ToArray()));

        using var opener = new FrameOpener(phoneToPc);
        Assert.True(opener.TryOpen(Convert.FromHexString("0000000000000000a47d53638b03b8329baf039e246afa0bdc0fde77ecc909a18680abcb4398ae928aad"), out var plaintext));
        Assert.Equal("{\"t\":\"a\",\"a\":\"up\"}", Encoding.UTF8.GetString(plaintext));
        using var sealer = new FrameSealer(pcToPhone);
        Assert.Equal("0000000000000000090a68cdb71c61dbd55dea26003d09e5508e87a39526af67a6f1", Convert.ToHexStringLower(sealer.Seal("{\"t\":\"ui\"}"u8)));
    }

    [Fact]
    public void Frames_reject_tampering_replay_reordering_and_the_wrong_direction()
    {
        var (key, sid) = Fixed();
        var (phoneToPc, pcToPhone) = PhoneCrypto.DeriveKeys(key, sid);
        using var phone = new FrameSealer(phoneToPc);
        var first = phone.Seal("one"u8);
        var second = phone.Seal("two"u8);

        using (var pc = new FrameOpener(phoneToPc))
        {
            Assert.True(pc.TryOpen(first, out var one));
            Assert.Equal("one", Encoding.UTF8.GetString(one));
            Assert.False(pc.TryOpen(first, out _)); // repetido
            Assert.True(pc.Failed);
            Assert.False(pc.TryOpen(second, out _)); // travado depois da primeira falha
        }
        using (var pc = new FrameOpener(phoneToPc))
            Assert.False(pc.TryOpen(second, out _)); // fora de ordem
        using (var pc = new FrameOpener(phoneToPc))
        {
            var tampered = (byte[])first.Clone();
            tampered[PhoneCrypto.CounterLength] ^= 1;
            Assert.False(pc.TryOpen(tampered, out _));
        }
        using (var pc = new FrameOpener(pcToPhone))
            Assert.False(pc.TryOpen(first, out _)); // chave do outro sentido
        using (var pc = new FrameOpener(phoneToPc))
            Assert.False(pc.TryOpen(first.AsSpan(0, PhoneCrypto.Overhead - 1), out _));
    }

    [Fact]
    public void Session_expires_is_single_use_and_needs_confirmation_before_input()
    {
        var t0 = TimeSpan.FromSeconds(10);
        var expired = new PhoneSession(t0);
        Assert.True(expired.CanServePage(t0));
        Assert.Null(expired.Tick(t0 + TimeSpan.FromSeconds(30)));
        Assert.Equal(PhoneEndReason.Expired, expired.Tick(t0 + PhoneSession.PairingLifetime));
        Assert.False(expired.CanServePage(t0 + PhoneSession.PairingLifetime));
        Assert.False(expired.TryBeginHandshake(t0 + PhoneSession.PairingLifetime));

        var session = new PhoneSession(t0);
        Assert.True(session.TryBeginHandshake(t0));
        Assert.True(session.TryBeginHandshake(t0));
        Assert.False(session.TryBeginHandshake(t0)); // conexões simultâneas demais
        Assert.False(session.HandshakeFailed()); // quem não tinha a chave sai sem derrubar a sessão
        Assert.Equal(PhoneSessionState.Waiting, session.State);
        Assert.True(session.TryAuthenticate("192.168.0.5", "123 456", t0));
        Assert.False(session.TryAuthenticate("192.168.0.9", "999 999", t0)); // a chave já é do primeiro
        Assert.False(session.TryBeginHandshake(t0)); // segundo celular recusado
        Assert.False(session.CanServePage(t0));
        Assert.False(session.TryAcceptInput(t0)); // sem permissão no PC, nada vira entrada
        Assert.True(session.Confirm(t0 + TimeSpan.FromSeconds(1)));
        Assert.True(session.TryAcceptInput(t0 + TimeSpan.FromSeconds(1)));
        Assert.True(session.End(PhoneEndReason.PhoneLeft));
        Assert.False(session.End(PhoneEndReason.Disconnected)); // idempotente, motivo original
        Assert.Equal(PhoneEndReason.PhoneLeft, session.EndReason);
        Assert.False(session.TryAcceptInput(t0 + TimeSpan.FromSeconds(2)));
        Assert.False(session.TryBeginHandshake(t0 + TimeSpan.FromSeconds(2))); // sem reconexão com a mesma chave

        var unconfirmed = new PhoneSession(t0);
        Assert.True(unconfirmed.TryBeginHandshake(t0));
        Assert.True(unconfirmed.TryAuthenticate("10.0.0.3", "000 001", t0));
        Assert.Equal(PhoneEndReason.ConfirmationTimedOut, unconfirmed.Tick(t0 + PhoneSession.ConfirmationTimeout));
        Assert.False(unconfirmed.Confirm(t0 + PhoneSession.ConfirmationTimeout));
    }

    [Fact]
    public void Session_limits_input_rate_and_failed_attempts()
    {
        var t0 = TimeSpan.Zero;
        var session = new PhoneSession(t0);
        session.TryBeginHandshake(t0);
        session.TryAuthenticate("10.0.0.3", "000 001", t0);
        session.Confirm(t0);
        var accepted = Enumerable.Range(0, 200).Count(_ => session.TryAcceptInput(t0));
        Assert.Equal((int)PhoneSession.InputBurst, accepted); // o excesso é descartado
        Assert.True(session.TryAcceptInput(t0 + TimeSpan.FromSeconds(1)));

        var attacked = new PhoneSession(t0);
        var ended = false;
        for (var i = 0; i < PhoneSession.MaxFailedHandshakes; i++)
        {
            Assert.True(attacked.TryBeginHandshake(t0));
            ended = attacked.HandshakeFailed();
        }
        Assert.True(ended);
        Assert.Equal(PhoneEndReason.TooManyAttempts, attacked.EndReason);
    }

    [Fact]
    public void Phone_messages_map_only_to_semantic_actions_and_bounded_text()
    {
        static PhoneMessage? Read(string json) => PhoneProtocol.TryParse(Encoding.UTF8.GetBytes(json), out var m) ? m : null;

        foreach (var (name, action) in PhoneProtocol.Actions)
            Assert.Equal(new PhoneMessage(PhoneMessageKind.Action, action), Read($"{{\"t\":\"a\",\"a\":\"{name}\"}}"));
        Assert.Equal(InputAction.Confirm, Read("{\"t\":\"a\",\"a\":\"confirm\"}")!.Value.Action);
        Assert.Equal("Relatório 2026", Read("{\"t\":\"text\",\"v\":\"Relatório 2026\"}")!.Value.Text);
        Assert.Equal(PhoneMessageKind.Backspace, Read("{\"t\":\"key\",\"k\":\"backspace\"}")!.Value.Kind);
        Assert.Equal(PhoneMessageKind.Enter, Read("{\"t\":\"key\",\"k\":\"enter\"}")!.Value.Kind);
        Assert.Equal(16, Read("{\"t\":\"hello\",\"n\":\"AAECAwQFBgcICQoLDA0ODw\"}")!.Value.Nonce!.Length);

        Assert.Null(Read("{\"t\":\"a\",\"a\":\"delete\"}")); // ação que a página não oferece
        Assert.Null(Read("{\"t\":\"a\",\"a\":\"SwitchPane\"}"));
        Assert.Null(Read("{\"t\":\"run\",\"v\":\"cmd.exe\"}"));
        Assert.Null(Read("{\"t\":\"open\",\"v\":\"C:\\\\Windows\"}"));
        Assert.Null(Read("{\"t\":\"a\",\"a\":\"up\",\"path\":\"C:\\\\\"}")); // campo a mais
        Assert.Null(Read("{\"t\":\"a\",\"t\":\"a\",\"a\":\"up\"}"));
        Assert.Null(Read("{\"t\":\"a\",\"a\":{\"x\":1}}"));
        Assert.Null(Read("{\"t\":\"text\",\"v\":\"linha\\ncomando\"}")); // caractere de controle
        Assert.Null(Read("{\"t\":\"text\",\"v\":\"\"}"));
        Assert.Null(Read("{\"t\":\"text\",\"v\":\"" + new string('a', PhoneProtocol.MaxTextLength + 1) + "\"}"));
        Assert.Null(Read("{\"t\":\"hello\",\"n\":\"curto\"}"));
        Assert.Null(Read("{\"t\":\"a\",\"a\":\"up\"} {}"));
        Assert.False(PhoneProtocol.TryParse(new byte[PhoneProtocol.MaxMessageBytes + 1], out _));
    }
}
