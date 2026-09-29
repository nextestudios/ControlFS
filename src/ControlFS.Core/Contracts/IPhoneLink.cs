using ControlFS.Core.Remote;

namespace ControlFS.Core.Contracts;

/// <summary>
/// Pareamento aberto (#223). <see cref="Url"/> vai no QR Code: a chave da sessão fica no fragmento (#k=…), que o
/// navegador nunca envia pela rede. <see cref="AddressCount"/> &gt; 1: o PC tem outras redes locais para escolher.
/// </summary>
public sealed record PhonePairing(int Session, string Url, string Address, int AddressIndex, int AddressCount, TimeSpan Lifetime);

/// <summary>Eventos do canal com o celular. Chegam em qualquer thread; <see cref="Session"/> descarta os de uma sessão velha.</summary>
public abstract record PhoneLinkEvent(int Session);

/// <summary>Um celular provou ter a chave; o usuário precisa permitir no PC conferindo IP e código.</summary>
public sealed record PhoneAwaitingConfirmation(int Session, string PhoneAddress, string VerificationCode) : PhoneLinkEvent(Session);

public sealed record PhoneConnected(int Session, string PhoneAddress) : PhoneLinkEvent(Session);

/// <summary>Mensagem autenticada de um celular permitido, dentro do limite de taxa.</summary>
public sealed record PhoneCommand(int Session, PhoneMessage Message) : PhoneLinkEvent(Session);

public sealed record PhoneEnded(int Session, PhoneEndReason Reason) : PhoneLinkEvent(Session);

public enum PhoneAttemptStage
{
    /// <summary>Um celular abriu a página do QR Code (ainda falta o canal seguro).</summary>
    PageOpened,

    /// <summary>Um celular pediu a página ou o canal e foi recusado (endereço, origem, cabeçalho, QR Code vencido).</summary>
    Refused,

    /// <summary>O celular abriu o canal mas não provou ter a chave a tempo (ou mandou algo inválido). Pode tentar de novo.</summary>
    HandshakeFailed,
}

/// <summary>
/// Andamento sem consequência para a sessão (ela continua esperando): mostra no PC que um celular chegou até onde e por
/// que falhou. <see cref="Code"/> é um código curto de diagnóstico (nunca chave, id de sessão ou conteúdo).
/// </summary>
public sealed record PhoneAttempt(int Session, PhoneAttemptStage Stage, string Code) : PhoneLinkEvent(Session);

/// <summary>
/// Canal local e cifrado com um celular (sem nuvem, sem conta, sem retransmissor). Nada escuta na rede fora de uma
/// sessão iniciada pelo usuário; um celular por vez.
/// </summary>
public interface IPhoneLink : IDisposable
{
    /// <summary>Encerra a sessão anterior (se houver) e começa outra na rede local de índice <paramref name="addressIndex"/>.</summary>
    /// <exception cref="PhoneLinkException">Sem rede local privada ou a porta não pôde ser aberta.</exception>
    PhonePairing Start(int addressIndex = 0);

    /// <summary>O usuário permitiu o celular que aguarda. Falso: a sessão não está aguardando (acabou, expirou).</summary>
    bool Confirm(int session);

    /// <summary>Encerra a sessão (para de escutar, fecha a conexão, descarta a chave). Idempotente.</summary>
    void Stop(int session, PhoneEndReason reason);

    /// <summary>Estado da tela para a página do celular (campo de texto em foco, bloqueio por confirmação sensível).</summary>
    void PublishUi(bool keyboard, bool locked);

    event Action<PhoneLinkEvent>? Event;
}

public sealed class PhoneLinkException : Exception
{
    public PhoneLinkException() { }

    public PhoneLinkException(string message) : base(message) { }

    public PhoneLinkException(string message, Exception inner) : base(message, inner) { }
}
