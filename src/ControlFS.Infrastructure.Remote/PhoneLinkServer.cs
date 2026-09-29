using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using ControlFS.Core.Contracts;
using ControlFS.Core.Remote;

namespace ControlFS.Infrastructure.Remote;

/// <summary>
/// Servidor do celular como controle (#223). Só existe durante uma sessão iniciada pelo usuário: escuta num endereço
/// IPv4 privado e numa porta alta aleatória, serve a página do celular (GET /sessão) e abre um WebSocket
/// (GET /sessão/ws) cujas mensagens são quadros AES-256-GCM (<see cref="PhoneCrypto"/>). A primeira conexão que prova
/// ter a chave fica com a sessão e a porta deixa de escutar; o usuário ainda precisa permitir no PC. Desconectar,
/// expirar, perder a conexão ou fechar o app encerram tudo e descartam a chave.
/// </summary>
public sealed class PhoneLinkServer : IPhoneLink
{
    /// <summary>Conexões TCP abertas ao mesmo tempo durante o pareamento (página + canal; o resto é recusado).</summary>
    public const int MaxConnections = 4;

    private static readonly TimeSpan HeadTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(2);
    private const int MaxFrameBytes = PhoneProtocol.MaxMessageBytes + PhoneCrypto.Overhead;

    private readonly Func<IReadOnlyList<IPAddress>> _addresses;
    private readonly Action<string>? _trace;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Lock _gate = new();
    private ActiveSession? _active;
    private int _lastSession;
    private (bool Keyboard, bool Locked) _ui;

    /// <param name="trace">
    /// Diagnóstico para o log local (#259): uma linha por recusa ou falha, só com código curto, IP privado do celular e
    /// número da sessão; nunca chave, id de sessão, URL ou conteúdo.
    /// </param>
    public PhoneLinkServer(Action<string>? trace = null) : this(LanAddresses.Discover, trace)
    {
    }

    /// <summary>Testes: escuta em endereços escolhidos (ex.: 127.0.0.1) em vez das redes locais do PC.</summary>
    internal PhoneLinkServer(Func<IReadOnlyList<IPAddress>> addresses, Action<string>? trace = null)
    {
        _addresses = addresses;
        _trace = trace;
    }

    public event Action<PhoneLinkEvent>? Event;

    /// <summary>Porta em escuta da sessão atual (null: nada escutando). Para testes e diagnóstico.</summary>
    internal int? ListeningPort
    {
        get
        {
            lock (_gate) return _active is { Listening: true } active ? active.Port : null;
        }
    }

    private TimeSpan Now => _clock.Elapsed;

    public PhonePairing Start(int addressIndex = 0)
    {
        ActiveSession? previous;
        lock (_gate) previous = _active;
        if (previous is not null) End(previous, PhoneEndReason.Disconnected);

        var addresses = _addresses();
        if (addresses.Count == 0)
            throw new PhoneLinkException("Nenhuma rede local encontrada. Conecte o PC ao mesmo Wi-Fi ou roteador do celular.");
        var index = ((addressIndex % addresses.Count) + addresses.Count) % addresses.Count;
        var address = addresses[index];
        var listener = Listen(address, out var port);
        var key = RandomNumberGenerator.GetBytes(PhoneCrypto.KeyLength);
        var sessionId = RandomNumberGenerator.GetBytes(PhoneCrypto.SessionIdLength);
        var (phoneToPc, pcToPhone) = PhoneCrypto.DeriveKeys(key, sessionId);
        var host = $"{address}:{port}";
        var active = new ActiveSession(Interlocked.Increment(ref _lastSession), new PhoneSession(Now), listener, host, port, key, phoneToPc, pcToPhone,
            "/" + Base64Url.Encode(sessionId));
        lock (_gate) _active = active;
        Trace(active.Id, $"session started address={address} port={port}");
        _ = AcceptLoopAsync(active);
        _ = WatchAsync(active);
        return new PhonePairing(active.Id, $"http://{host}{active.Path}#k={Base64Url.Encode(key)}", address.ToString(), index, addresses.Count, PhoneSession.PairingLifetime);
    }

    /// <summary>Porta alta aleatória (49152–65535), exclusiva do ControlFS (outro programa não pode dividi-la).</summary>
    private static TcpListener Listen(IPAddress address, out int port)
    {
        for (var attempt = 0; ; attempt++)
        {
            port = RandomNumberGenerator.GetInt32(49152, 65536);
            var listener = new TcpListener(address, port);
            try
            {
                if (OperatingSystem.IsWindows()) listener.ExclusiveAddressUse = true;
                listener.Start(MaxConnections);
                return listener;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse && attempt < 16)
            {
                listener.Dispose();
            }
            catch (SocketException ex)
            {
                listener.Dispose();
                throw new PhoneLinkException($"Não foi possível abrir a conexão local ({ex.SocketErrorCode}). Verifique o Firewall do Windows e tente de novo.", ex);
            }
        }
    }

    public bool Confirm(int session)
    {
        ActiveSession? active;
        lock (_gate) active = _active;
        if (active is null || active.Id != session || !active.Session.Confirm(Now)) return false;
        Raise(new PhoneConnected(active.Id, active.Session.PhoneAddress!));
        _ = ConfirmedAsync(active);
        return true;
    }

    private async Task ConfirmedAsync(ActiveSession active)
    {
        await SendAsync(active, PhoneProtocol.State("ready")).ConfigureAwait(false);
        await PublishUiAsync(active, force: true).ConfigureAwait(false);
    }

    public void Stop(int session, PhoneEndReason reason)
    {
        ActiveSession? active;
        lock (_gate) active = _active;
        if (active is not null && active.Id == session) End(active, reason);
    }

    public void PublishUi(bool keyboard, bool locked)
    {
        ActiveSession? active;
        lock (_gate)
        {
            _ui = (keyboard, locked);
            active = _active;
        }
        if (active is not null && active.Session.State == PhoneSessionState.Connected && active.SentUi != (keyboard, locked)) _ = PublishUiAsync(active, force: false);
    }

    private async Task PublishUiAsync(ActiveSession active, bool force)
    {
        (bool Keyboard, bool Locked) ui;
        lock (_gate) ui = _ui;
        if (!force && active.SentUi == ui) return;
        active.SentUi = ui;
        await SendAsync(active, PhoneProtocol.Ui(ui.Keyboard, ui.Locked)).ConfigureAwait(false);
    }

    public void Dispose()
    {
        ActiveSession? active;
        lock (_gate) active = _active;
        if (active is not null) End(active, PhoneEndReason.AppClosing);
    }

    private void Raise(PhoneLinkEvent e)
    {
        if (e is PhoneEnded ended) Trace(ended.Session, $"session ended reason={ended.Reason}");
        Event?.Invoke(e);
    }

    private void Trace(int session, string message) => _trace?.Invoke($"phone: #{session} {message}");

    /// <summary>Uma conexão foi recusada ou falhou: vai para o log local e para o diálogo do PC (a sessão segue esperando).</summary>
    private void Attempt(ActiveSession active, PhoneAttemptStage stage, string code, IPAddress? peer = null)
    {
        Trace(active.Id, $"{stage} code={code}" + (peer is null ? string.Empty : $" peer={(peer.IsIPv4MappedToIPv6 ? peer.MapToIPv4() : peer)}"));
        Raise(new PhoneAttempt(active.Id, stage, code));
    }

    /// <summary>
    /// Encerra a sessão uma vez: para de escutar na hora (antes de avisar a interface), avisa o celular do motivo, fecha
    /// a conexão e apaga as chaves.
    /// </summary>
    private void End(ActiveSession active, PhoneEndReason reason)
    {
        var first = active.Session.End(reason);
        lock (_gate)
            if (ReferenceEquals(_active, active)) _active = null;
        active.StopListening();
        if (!first) return;
        Raise(new PhoneEnded(active.Id, reason));
        _ = CloseAsync(active, reason);
    }

    private async Task CloseAsync(ActiveSession active, PhoneEndReason reason)
    {
        try
        {
            if (active.Socket is { State: WebSocketState.Open } socket)
            {
                await SendAsync(active, PhoneProtocol.End(reason.ToString())).ConfigureAwait(false);
                using var timeout = new CancellationTokenSource(SendTimeout);
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or IOException or OperationCanceledException or ObjectDisposedException)
        {
            // O celular já sumiu: nada a avisar.
        }
        finally
        {
            await active.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task WatchAsync(ActiveSession active)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        try
        {
            while (await timer.WaitForNextTickAsync(active.Cancellation).ConfigureAwait(false))
            {
                if (active.Session.State == PhoneSessionState.Ended) return;
                if (active.Session.Tick(Now) is { } reason)
                {
                    CleanUpAfterTimeout(active, reason);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Sessão encerrada.
        }
    }

    /// <summary>O prazo encerrou a sessão dentro do <see cref="PhoneSession.Tick"/>: limpa e avisa como <see cref="End"/>.</summary>
    private void CleanUpAfterTimeout(ActiveSession active, PhoneEndReason reason)
    {
        lock (_gate)
            if (ReferenceEquals(_active, active)) _active = null;
        active.StopListening();
        Raise(new PhoneEnded(active.Id, reason));
        _ = CloseAsync(active, reason);
    }

    private async Task AcceptLoopAsync(ActiveSession active)
    {
        while (!active.Cancellation.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await active.Listener.AcceptTcpClientAsync(active.Cancellation).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException or InvalidOperationException)
            {
                return; // parou de escutar
            }
            if (Interlocked.Increment(ref active.Connections) > MaxConnections)
            {
                Interlocked.Decrement(ref active.Connections);
                client.Dispose();
                continue;
            }
            _ = ServeAsync(active, client);
        }
    }

    private async Task ServeAsync(ActiveSession active, TcpClient client)
    {
        try
        {
            using (client)
            {
                client.NoDelay = true;
                if (client.Client.RemoteEndPoint is not IPEndPoint remote || !IsAllowedPeer(remote.Address, active))
                {
                    Trace(active.Id, "Refused code=peer-not-local");
                    return;
                }
                var stream = client.GetStream();
                HttpRequestHead? head;
                var headRejected = HttpParseStatus.Incomplete;
                using (var headTimeout = CancellationTokenSource.CreateLinkedTokenSource(active.Cancellation))
                {
                    headTimeout.CancelAfter(HeadTimeout);
                    head = await HttpRequestParser.ReadAsync(stream, headTimeout.Token, status => headRejected = status).ConfigureAwait(false);
                }
                if (head is null)
                {
                    // Conexão vazia ou fechada sem pedido (pré-conexão do navegador) não é falha; pedido que o leitor recusou é.
                    if (headRejected != HttpParseStatus.Incomplete)
                    {
                        Attempt(active, PhoneAttemptStage.Refused, headRejected == HttpParseStatus.TooLarge ? "head-too-large" : "head-invalid", remote.Address);
                        await RespondAsync(stream, "400 Bad Request", active.Cancellation).ConfigureAwait(false);
                    }
                    return;
                }
                // Host exato (IP:porta): recusa nomes de DNS apontados para o PC (DNS rebinding).
                if (!string.Equals(head.Header("Host"), active.Host, StringComparison.Ordinal))
                {
                    Attempt(active, PhoneAttemptStage.Refused, "host-mismatch", remote.Address);
                    await RespondAsync(stream, "421 Misdirected Request", active.Cancellation).ConfigureAwait(false);
                    return;
                }
                if (head.Target == active.Path)
                {
                    if (active.Session.CanServePage(Now))
                    {
                        Attempt(active, PhoneAttemptStage.PageOpened, "page", remote.Address);
                        await WritePageAsync(stream, active).ConfigureAwait(false);
                    }
                    else
                    {
                        Attempt(active, PhoneAttemptStage.Refused, "page-gone", remote.Address);
                        await RespondAsync(stream, "410 Gone", active.Cancellation, GoneText).ConfigureAwait(false);
                    }
                    return;
                }
                // Sonda da página: o celular pergunta se o QR Code ainda vale para separar "PC fora do alcance" de "QR vencido".
                if (head.Target == active.Path + "/ping")
                {
                    if (active.Session.CanServePage(Now)) await RespondAsync(stream, "204 No Content", active.Cancellation, empty: true).ConfigureAwait(false);
                    else await RespondAsync(stream, "410 Gone", active.Cancellation, GoneText).ConfigureAwait(false);
                    return;
                }
                if (head.Target == active.Path + "/ws" && HttpRequestParser.IsWebSocketUpgrade(head))
                {
                    await RunChannelAsync(active, stream, head, remote.Address).ConfigureAwait(false);
                    return;
                }
                Attempt(active, PhoneAttemptStage.Refused, head.Target == active.Path + "/ws" ? "not-websocket-upgrade" : "unknown-route", remote.Address);
                await RespondAsync(stream, "404 Not Found", active.Cancellation).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException or WebSocketException)
        {
            // Conexão caiu ou a sessão acabou.
        }
        finally
        {
            Interlocked.Decrement(ref active.Connections);
        }
    }

    /// <summary>Só aparelhos da rede local (ou a própria máquina quando o servidor escuta nela, nos testes).</summary>
    private static bool IsAllowedPeer(IPAddress peer, ActiveSession active)
    {
        if (peer.IsIPv4MappedToIPv6) peer = peer.MapToIPv4();
        return LanAddresses.IsPrivate(peer) || (IPAddress.IsLoopback(peer) && IPAddress.IsLoopback(((IPEndPoint)active.Listener.LocalEndpoint).Address));
    }

    private const string GoneText = "Este QR Code não vale mais. Gere outro no ControlFS (Menu → Conectar celular).";

    private async Task RunChannelAsync(ActiveSession active, NetworkStream stream, HttpRequestHead head, IPAddress peer)
    {
        if (!string.Equals(head.Header("Origin"), "http://" + active.Host, StringComparison.Ordinal))
        {
            Attempt(active, PhoneAttemptStage.Refused, "origin-mismatch", peer);
            await RespondAsync(stream, "403 Forbidden", active.Cancellation).ConfigureAwait(false);
            return;
        }
        if (HttpRequestParser.WebSocketAccept(head.Header("Sec-WebSocket-Key")) is not { } accept)
        {
            Attempt(active, PhoneAttemptStage.Refused, "websocket-key-invalid", peer);
            await RespondAsync(stream, "400 Bad Request", active.Cancellation).ConfigureAwait(false);
            return;
        }
        if (!active.Session.TryBeginHandshake(Now))
        {
            Attempt(active, PhoneAttemptStage.Refused, "channel-busy-or-expired", peer);
            await RespondAsync(stream, "409 Conflict", active.Cancellation, "Outro celular já usou este QR Code, ou ele expirou.").ConfigureAwait(false);
            return;
        }

        var authenticated = false;
        using var opener = new FrameOpener(active.PhoneToPc);
        try
        {
            var upgrade = $"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(upgrade), active.Cancellation).ConfigureAwait(false);
            using var socket = WebSocket.CreateFromStream(stream, new WebSocketCreationOptions
            {
                IsServer = true,
                KeepAliveInterval = TimeSpan.FromSeconds(5),
                KeepAliveTimeout = TimeSpan.FromSeconds(15), // celular sumiu da rede (tela bloqueada, Wi-Fi caiu): a sessão acaba
            });
            var buffer = new byte[MaxFrameBytes + 1];

            byte[]? hello;
            using (var handshakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(active.Cancellation))
            {
                handshakeTimeout.CancelAfter(HandshakeTimeout);
                hello = await ReceiveFrameAsync(socket, buffer, handshakeTimeout.Token).ConfigureAwait(false);
            }
            if (hello is null || !opener.TryOpen(hello, out var plaintext) || !PhoneProtocol.TryParse(plaintext, out var message) || message.Kind != PhoneMessageKind.Hello)
            {
                Attempt(active, PhoneAttemptStage.HandshakeFailed, hello is null ? "hello-missing" : "hello-invalid", peer);
                FailHandshake(active);
                return;
            }
            var code = PhoneCrypto.VerificationCode(active.Key, message.Nonce);
            var peerText = (peer.IsIPv4MappedToIPv6 ? peer.MapToIPv4() : peer).ToString();
            if (!active.Session.TryAuthenticate(peerText, code, Now)) return;
            authenticated = true;
            active.StopListening(); // a chave agora é deste celular: ninguém mais conecta
            if (!active.Attach(socket, new FrameSealer(active.PcToPhone)) || active.Session.State == PhoneSessionState.Ended) return; // encerrada no meio
            Raise(new PhoneAwaitingConfirmation(active.Id, peerText, code));
            await SendAsync(active, PhoneProtocol.State("confirm")).ConfigureAwait(false);

            while (true)
            {
                var frame = await ReceiveFrameAsync(socket, buffer, active.Cancellation).ConfigureAwait(false);
                if (frame is null)
                {
                    End(active, PhoneEndReason.PhoneLeft);
                    return;
                }
                if (!opener.TryOpen(frame, out plaintext))
                {
                    End(active, PhoneEndReason.AuthenticationFailed);
                    return;
                }
                if (!PhoneProtocol.TryParse(plaintext, out message) || message.Kind == PhoneMessageKind.Hello) continue; // descartada
                if (message.Kind == PhoneMessageKind.Bye)
                {
                    End(active, PhoneEndReason.PhoneLeft);
                    return;
                }
                if (active.Session.TryAcceptInput(Now)) Raise(new PhoneCommand(active.Id, message));
            }
        }
        catch (Exception ex) when (ex is WebSocketException or IOException or OperationCanceledException or ObjectDisposedException)
        {
            if (authenticated) End(active, PhoneEndReason.PhoneLeft);
            else
            {
                Attempt(active, PhoneAttemptStage.HandshakeFailed, ex is OperationCanceledException ? "hello-timeout" : "channel-dropped", peer);
                FailHandshake(active);
            }
        }
    }

    private void FailHandshake(ActiveSession active)
    {
        if (active.Session.HandshakeFailed()) CleanUpAfterTimeout(active, PhoneEndReason.TooManyAttempts);
    }

    /// <summary>Uma mensagem binária inteira de até <see cref="MaxFrameBytes"/>. Null: fechou, texto, grande demais.</summary>
    private static async Task<byte[]?> ReceiveFrameAsync(WebSocket socket, byte[] buffer, CancellationToken cancellation)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var result = await socket.ReceiveAsync(buffer.AsMemory(total), cancellation).ConfigureAwait(false);
            if (result.MessageType != WebSocketMessageType.Binary) return null;
            total += result.Count;
            if (result.EndOfMessage) return total <= MaxFrameBytes ? buffer.AsSpan(0, total).ToArray() : null;
        }
        return null;
    }

    private async Task SendAsync(ActiveSession active, byte[] plaintext)
    {
        await active.SendLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (active.Socket is not { State: WebSocketState.Open } socket || active.Sealer is not { } sealer) return;
            using var timeout = new CancellationTokenSource(SendTimeout);
            await socket.SendAsync(sealer.Seal(plaintext), WebSocketMessageType.Binary, true, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is WebSocketException or IOException or OperationCanceledException or ObjectDisposedException)
        {
            // A queda é percebida pelo laço de recepção.
        }
        finally
        {
            active.SendLock.Release();
        }
    }

    private static async Task WritePageAsync(NetworkStream stream, ActiveSession active)
    {
        var page = CompanionPage.Html;
        var head = "HTTP/1.1 200 OK\r\n"
            + "Content-Type: text/html; charset=utf-8\r\n"
            + $"Content-Length: {page.Length}\r\n"
            + "Cache-Control: no-store\r\n"
            + $"Content-Security-Policy: default-src 'none'; script-src '{CompanionPage.ScriptHash}'; style-src 'unsafe-inline'; connect-src ws://{active.Host} http://{active.Host}; img-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'\r\n"
            + "Referrer-Policy: no-referrer\r\n"
            + "X-Content-Type-Options: nosniff\r\n"
            + "X-Frame-Options: DENY\r\n"
            + "Connection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), active.Cancellation).ConfigureAwait(false);
        await stream.WriteAsync(page, active.Cancellation).ConfigureAwait(false);
    }

    private static async Task RespondAsync(NetworkStream stream, string status, CancellationToken cancellation, string? text = null, bool empty = false)
    {
        var body = empty ? [] : Encoding.UTF8.GetBytes(text ?? status);
        var head = $"HTTP/1.1 {status}\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), cancellation).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellation).ConfigureAwait(false);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA1001", Justification = "O CancellationTokenSource não tem temporizador e o token é lido por tarefas que podem terminar depois de DisposeAsync; cancelar basta.")]
    private sealed class ActiveSession(int id, PhoneSession session, TcpListener listener, string host, int port, byte[] key, byte[] phoneToPc, byte[] pcToPhone, string path)
    {
        private readonly CancellationTokenSource _cancellation = new();
        private readonly Lock _attach = new();
        private bool _closed;
        private int _listening = 1;

        public int Id { get; } = id;
        public PhoneSession Session { get; } = session;
        public TcpListener Listener { get; } = listener;
        public string Host { get; } = host;
        public int Port { get; } = port;
        public byte[] Key { get; } = key;
        public byte[] PhoneToPc { get; } = phoneToPc;
        public byte[] PcToPhone { get; } = pcToPhone;

        /// <summary>"/" + id da sessão em base64url (a página; o canal é Path + "/ws").</summary>
        public string Path { get; } = path;

        public CancellationToken Cancellation => _cancellation.Token;
        public SemaphoreSlim SendLock { get; } = new(1, 1);
        public WebSocket? Socket { get; private set; }
        public FrameSealer? Sealer { get; private set; }
        public (bool Keyboard, bool Locked)? SentUi { get; set; }
        public bool Listening => Volatile.Read(ref _listening) == 1;
        public int Connections;

        /// <summary>O canal autenticado passa a ser o da sessão. Falso: a sessão já foi fechada (o cifrador é descartado).</summary>
        public bool Attach(WebSocket socket, FrameSealer sealer)
        {
            lock (_attach)
            {
                if (_closed)
                {
                    sealer.Dispose();
                    return false;
                }
                Socket = socket;
                Sealer = sealer;
                return true;
            }
        }

        public void StopListening()
        {
            if (Interlocked.Exchange(ref _listening, 0) == 1) Listener.Stop();
        }

        /// <summary>Fecha tudo e apaga as chaves da memória (a sessão não pode ser retomada).</summary>
        public async Task DisposeAsync()
        {
            StopListening();
            lock (_attach) _closed = true;
            await _cancellation.CancelAsync().ConfigureAwait(false);
            Socket?.Abort();
            await SendLock.WaitAsync().ConfigureAwait(false);
            try
            {
                Sealer?.Dispose();
                Sealer = null;
            }
            finally
            {
                SendLock.Release();
            }
            CryptographicOperations.ZeroMemory(Key);
            CryptographicOperations.ZeroMemory(PhoneToPc);
            CryptographicOperations.ZeroMemory(PcToPhone);
        }
    }
}
