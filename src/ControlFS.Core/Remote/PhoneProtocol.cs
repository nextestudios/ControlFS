using System.Text;
using System.Text.Json;
using ControlFS.Core.Actions;

namespace ControlFS.Core.Remote;

public enum PhoneMessageKind
{
    /// <summary>Primeira mensagem do celular: um número aleatório que entra no código de verificação.</summary>
    Hello,

    /// <summary>Ação semântica (a mesma de um botão do controle).</summary>
    Action,

    /// <summary>Texto para o campo em foco do teclado na tela.</summary>
    Text,
    Backspace,

    /// <summary>Enter do teclado do celular: conclui o campo (como OK/Concluir do teclado na tela).</summary>
    Enter,

    /// <summary>O celular pediu para desconectar.</summary>
    Bye,
}

public readonly record struct PhoneMessage(PhoneMessageKind Kind, InputAction Action = default, string? Text = null, byte[]? Nonce = null);

/// <summary>
/// Mensagens do celular (#223), já decifradas: JSON pequeno com um tipo e no máximo um valor. O celular só consegue pedir
/// ações semânticas e texto para o campo em foco; nunca caminhos, comandos ou dados arbitrários. Qualquer coisa fora
/// deste formato (tipo desconhecido, campo a mais, texto grande ou com caracteres de controle) é recusada.
/// </summary>
public static class PhoneProtocol
{
    /// <summary>Tamanho máximo de uma mensagem decifrada.</summary>
    public const int MaxMessageBytes = 1024;

    /// <summary>Máximo de caracteres por mensagem de texto (o celular divide colagens maiores).</summary>
    public const int MaxTextLength = 256;

    public const int NonceLength = 16;

    /// <summary>Nome no protocolo → ação. Só o que a página do celular oferece.</summary>
    public static IReadOnlyDictionary<string, InputAction> Actions { get; } = new Dictionary<string, InputAction>(StringComparer.Ordinal)
    {
        ["up"] = InputAction.NavigateUp,
        ["down"] = InputAction.NavigateDown,
        ["left"] = InputAction.NavigateLeft,
        ["right"] = InputAction.NavigateRight,
        ["pageUp"] = InputAction.PageUp,
        ["pageDown"] = InputAction.PageDown,
        ["scrollUp"] = InputAction.ScrollUp,
        ["scrollDown"] = InputAction.ScrollDown,
        ["scrollLeft"] = InputAction.ScrollLeft,
        ["scrollRight"] = InputAction.ScrollRight,
        ["confirm"] = InputAction.Confirm,
        ["back"] = InputAction.Back,
        ["mark"] = InputAction.ToggleSelection,
        ["actions"] = InputAction.OpenContextMenu,
        ["menu"] = InputAction.OpenAppMenu,
        ["search"] = InputAction.Search,
        ["view"] = InputAction.ChangeView,
        ["prev"] = InputAction.PreviousRegion,
        ["next"] = InputAction.NextRegion,
    };

    public static bool TryParse(ReadOnlySpan<byte> json, out PhoneMessage message)
    {
        message = default;
        if (json.Length == 0 || json.Length > MaxMessageBytes) return false;
        string? type = null, value = null, valueName = null;
        try
        {
            var reader = new Utf8JsonReader(json, new JsonReaderOptions { MaxDepth = 1 });
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return false;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject) break;
                if (reader.TokenType != JsonTokenType.PropertyName) return false;
                var name = reader.GetString();
                if (!reader.Read() || reader.TokenType != JsonTokenType.String) return false;
                var text = reader.GetString();
                if (name == "t" && type is null) type = text;
                else if (name is "a" or "v" or "k" or "n" && valueName is null)
                {
                    valueName = name;
                    value = text;
                }
                else return false; // campo desconhecido ou repetido
            }
            if (reader.TokenType != JsonTokenType.EndObject || reader.Read()) return false; // nada depois do objeto
        }
        catch (JsonException)
        {
            return false;
        }

        switch (type)
        {
            case "hello" when valueName == "n" && TryNonce(value, out var nonce):
                message = new PhoneMessage(PhoneMessageKind.Hello, Nonce: nonce);
                return true;
            case "a" when valueName == "a" && value is not null && Actions.TryGetValue(value, out var action):
                message = new PhoneMessage(PhoneMessageKind.Action, action);
                return true;
            case "text" when valueName == "v" && Sanitize(value) is { } text:
                message = new PhoneMessage(PhoneMessageKind.Text, Text: text);
                return true;
            case "key" when valueName == "k" && value == "backspace":
                message = new PhoneMessage(PhoneMessageKind.Backspace);
                return true;
            case "key" when valueName == "k" && value == "enter":
                message = new PhoneMessage(PhoneMessageKind.Enter);
                return true;
            case "bye" when valueName is null:
                message = new PhoneMessage(PhoneMessageKind.Bye);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Texto aceito: 1–<see cref="MaxTextLength"/> caracteres, sem controle (quebras de linha, tab, escape).</summary>
    private static string? Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > MaxTextLength) return null;
        foreach (var c in text)
            if (char.IsControl(c)) return null;
        return text;
    }

    private static bool TryNonce(string? value, out byte[] nonce)
    {
        nonce = [];
        if (value is null || value.Length != 22) return false; // 16 bytes em base64url sem preenchimento
        var bytes = Base64Url.TryDecode(value);
        if (bytes is not { Length: NonceLength }) return false;
        nonce = bytes;
        return true;
    }

    // ---------- PC → celular ----------

    /// <summary>Estado do pareamento para a página: "confirm" (aguardando o PC) ou "ready".</summary>
    public static byte[] State(string state) => Serialize(w => w.WriteString("s", state), "state");

    /// <summary>Se há um campo de texto em foco e se o PC bloqueou o celular (confirmação sensível aberta).</summary>
    public static byte[] Ui(bool keyboard, bool locked) => Serialize(w =>
    {
        w.WriteBoolean("kb", keyboard);
        w.WriteBoolean("lock", locked);
    }, "ui");

    public static byte[] End(string reason) => Serialize(w => w.WriteString("r", reason), "end");

    private static byte[] Serialize(Action<Utf8JsonWriter> body, string type)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("t", type);
            body(writer);
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }
}

/// <summary>Base64 para URL (RFC 4648 §5) sem preenchimento: chave e sessão no endereço do QR Code.</summary>
public static class Base64Url
{
    public static string Encode(ReadOnlySpan<byte> bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[]? TryDecode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        foreach (var c in text)
            if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) return null;
        var padded = new StringBuilder(text.Replace('-', '+').Replace('_', '/'));
        while (padded.Length % 4 != 0) padded.Append('=');
        try
        {
            return Convert.FromBase64String(padded.ToString());
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
