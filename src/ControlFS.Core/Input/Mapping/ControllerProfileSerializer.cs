using System.Globalization;
using System.Text;
using System.Text.Json;
using ControlFS.Core.Actions;

namespace ControlFS.Core.Input.Mapping;

/// <summary>Perfil de controle rejeitado (arquivo importado ou salvo). A mensagem é para o usuário.</summary>
public sealed class ControllerProfileException : Exception
{
    public ControllerProfileException() : base("Perfil de controle inválido.")
    {
    }

    public ControllerProfileException(string message) : base(message)
    {
    }

    public ControllerProfileException(string message, Exception inner) : base(message, inner)
    {
    }
}

/// <summary>
/// Formato JSON versionado dos perfis de controle. É uma fronteira de segurança (perfis podem vir de outra pessoa):
/// tamanho e profundidade limitados, somente as propriedades conhecidas (nada de campos extras, caminhos ou comandos),
/// tipos e faixas conferidos, controles obrigatórios presentes e nenhuma entrada ligada a dois controles.
/// </summary>
public static class ControllerProfileSerializer
{
    public const string Format = "controlfs-controller-profile";
    public const int MaxBytes = 64 * 1024;
    public const int MaxNameLength = 64;
    public const int MaxRawIndex = 127;
    private const int MaxItems = 32;

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        MaxDepth = 4,
        CommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
    };

    private static readonly Dictionary<string, PhysicalControl> ControlNames =
        MappingTargets.Allowed.ToDictionary(c => c.ToString(), c => c, StringComparer.Ordinal);

    private static readonly Dictionary<string, RawInputKind> KindNames =
        Enum.GetValues<RawInputKind>().ToDictionary(k => k.ToString(), k => k, StringComparer.Ordinal);

    /// <summary>Lê no máximo <see cref="MaxBytes"/> do fluxo; maior que isso é rejeitado sem ler o resto.</summary>
    public static ControllerProfile Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var buffer = new byte[MaxBytes + 1];
        var total = 0;
        int read;
        while (total < buffer.Length && (read = stream.Read(buffer, total, buffer.Length - total)) > 0) total += read;
        return Parse(buffer.AsSpan(0, total));
    }

    public static ControllerProfile Parse(ReadOnlySpan<byte> utf8)
    {
        if (utf8.Length > MaxBytes) throw new ControllerProfileException($"Arquivo grande demais para um perfil (máximo {MaxBytes / 1024} KB).");
        if (utf8.Length == 0) throw new ControllerProfileException("Arquivo vazio.");
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(utf8.ToArray(), DocumentOptions);
        }
        catch (JsonException ex)
        {
            throw new ControllerProfileException("Não é um perfil de controle válido (JSON malformado).", ex);
        }
        using (document)
        {
            try
            {
                return ReadProfile(document.RootElement);
            }
            catch (InvalidOperationException ex)
            {
                throw new ControllerProfileException("Perfil de controle com tipos inválidos.", ex);
            }
        }
    }

    public static byte[] Serialize(ControllerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Validate(profile);
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("format", Format);
            w.WriteNumber("schemaVersion", ControllerProfile.CurrentSchemaVersion);
            w.WriteString("name", profile.Name);
            w.WriteStartObject("match");
            w.WriteString("guid", profile.Match.DeviceGuid);
            w.WriteNumber("vendorId", profile.Match.VendorId);
            w.WriteNumber("productId", profile.Match.ProductId);
            w.WriteEndObject();
            w.WriteStartArray("axes");
            foreach (var axis in profile.Axes.OrderBy(a => a.Index))
            {
                w.WriteStartObject();
                w.WriteNumber("index", axis.Index);
                w.WriteNumber("neutral", axis.Neutral);
                w.WriteNumber("deadzone", axis.Deadzone);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("bindings");
            foreach (var (control, binding) in profile.Bindings.OrderBy(b => b.Key))
            {
                w.WriteStartObject();
                w.WriteString("control", control.ToString());
                w.WriteString("kind", binding.Kind.ToString());
                w.WriteNumber("index", binding.Index);
                w.WriteNumber("direction", binding.Direction);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return stream.ToArray();
    }

    /// <summary>Nome exibível e seguro: sem caracteres de controle, aparado, até <see cref="MaxNameLength"/>.</summary>
    public static string CleanName(string? name)
    {
        var builder = new StringBuilder();
        foreach (var c in name ?? string.Empty)
            if (!IsHidden(c)) builder.Append(c);
        var clean = builder.ToString().Trim();
        if (clean.Length > MaxNameLength) clean = clean[..MaxNameLength].TrimEnd();
        return clean.Length == 0 ? "Joystick" : clean;
    }

    /// <summary>Caracteres de controle e de formatação (ex.: inversão bidirecional) não entram no nome exibido.</summary>
    private static bool IsHidden(char c) => char.IsControl(c) || char.GetUnicodeCategory(c) == UnicodeCategory.Format;

    /// <summary>Mesmas regras do arquivo, para perfis montados em memória (o assistente nunca grava algo que não leria).</summary>
    public static void Validate(ControllerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Name.Length is 0 or > MaxNameLength || profile.Name.Any(IsHidden)) throw Invalid("nome");
        ValidateMatch(profile.Match);
        if (profile.Axes.Count > MaxItems || profile.Bindings.Count > MaxItems) throw Invalid("itens demais");
        var axisIndexes = new HashSet<int>();
        foreach (var axis in profile.Axes)
        {
            if (axis.Index is < 0 or > MaxRawIndex || !axisIndexes.Add(axis.Index)) throw Invalid("eixo");
            if (!double.IsFinite(axis.Neutral) || axis.Neutral is < -1 or > 1) throw Invalid("neutro do eixo");
            if (!double.IsFinite(axis.Deadzone) || axis.Deadzone is < 0.05 or > 0.9) throw Invalid("zona morta");
        }
        var inputs = new HashSet<RawBinding>();
        foreach (var (control, binding) in profile.Bindings)
        {
            if (!MappingTargets.Allowed.Contains(control)) throw Invalid("controle");
            if (binding.Index is < 0 or > MaxRawIndex) throw Invalid("índice");
            var directionOk = binding.Kind switch
            {
                RawInputKind.Button => binding.Direction == 0,
                RawInputKind.Hat => HatDirections.IsSingle(binding.Direction),
                RawInputKind.Axis => binding.Direction is -1 or 1,
                _ => false,
            };
            if (!directionOk) throw Invalid("direção");
            if (!inputs.Add(binding)) throw new ControllerProfileException("Perfil inválido: a mesma entrada está ligada a dois controles.");
        }
        foreach (var required in MappingTargets.Required)
            if (!profile.Bindings.ContainsKey(required))
                throw new ControllerProfileException("Perfil incompleto: faltam direções, confirmar ou voltar.");
    }

    private static ControllerProfile ReadProfile(JsonElement root)
    {
        var props = Object(root, "perfil", ["format", "schemaVersion", "name", "match", "axes", "bindings"], ["format", "schemaVersion", "name", "match", "bindings"]);
        if (props["format"].GetString() != Format) throw new ControllerProfileException("Não é um perfil de controle do ControlFS.");
        var version = Int(props["schemaVersion"], "versão", 1, int.MaxValue);
        if (version > ControllerProfile.CurrentSchemaVersion) throw new ControllerProfileException("Perfil criado por uma versão mais nova do ControlFS.");
        var name = props["name"].GetString() ?? throw Invalid("nome");

        var m = Object(props["match"], "identificação", ["guid", "vendorId", "productId"], ["guid", "vendorId", "productId"]);
        var match = new ControllerMatch(m["guid"].GetString() ?? throw Invalid("GUID"), (ushort)Int(m["vendorId"], "vendor", 0, ushort.MaxValue), (ushort)Int(m["productId"], "product", 0, ushort.MaxValue));

        var axes = new List<AxisCalibration>();
        if (props.TryGetValue("axes", out var axesElement))
            foreach (var item in Items(axesElement, "eixos"))
            {
                var a = Object(item, "eixo", ["index", "neutral", "deadzone"], ["index", "neutral", "deadzone"]);
                axes.Add(new AxisCalibration(Int(a["index"], "eixo", 0, MaxRawIndex), Number(a["neutral"], "neutro"), Number(a["deadzone"], "zona morta")));
            }

        var bindings = new Dictionary<PhysicalControl, RawBinding>();
        foreach (var item in Items(props["bindings"], "ligações"))
        {
            var b = Object(item, "ligação", ["control", "kind", "index", "direction"], ["control", "kind", "index", "direction"]);
            if (!ControlNames.TryGetValue(b["control"].GetString() ?? string.Empty, out var control)) throw Invalid("controle");
            if (!KindNames.TryGetValue(b["kind"].GetString() ?? string.Empty, out var kind)) throw Invalid("tipo de entrada");
            if (!bindings.TryAdd(control, new RawBinding(kind, Int(b["index"], "índice", 0, MaxRawIndex), Int(b["direction"], "direção", -8, 8))))
                throw new ControllerProfileException("Perfil inválido: um controle aparece duas vezes.");
        }

        var profile = new ControllerProfile(name, match, bindings, axes) { SchemaVersion = version };
        Validate(profile);
        return profile with { SchemaVersion = ControllerProfile.CurrentSchemaVersion };
    }

    private static void ValidateMatch(ControllerMatch match)
    {
        if (match.DeviceGuid.Length != 0 && (match.DeviceGuid.Length != 32 || !match.DeviceGuid.All(char.IsAsciiHexDigit))) throw Invalid("GUID");
        if (match.DeviceGuid.Length == 0 && (match.VendorId == 0 || match.ProductId == 0))
            throw new ControllerProfileException("Perfil sem identificação do controle (GUID ou vendor/product).");
    }

    private static Dictionary<string, JsonElement> Object(JsonElement element, string what, string[] allowed, string[] required)
    {
        if (element.ValueKind != JsonValueKind.Object) throw Invalid(what);
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name, StringComparer.Ordinal))
                throw new ControllerProfileException($"Perfil inválido: campo desconhecido \"{Short(property.Name)}\".");
            if (!result.TryAdd(property.Name, property.Value))
                throw new ControllerProfileException($"Perfil inválido: campo repetido \"{Short(property.Name)}\".");
        }
        foreach (var name in required)
            if (!result.ContainsKey(name)) throw new ControllerProfileException($"Perfil inválido: falta \"{name}\".");
        return result;
    }

    private static JsonElement.ArrayEnumerator Items(JsonElement element, string what)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() > MaxItems) throw Invalid(what);
        return element.EnumerateArray();
    }

    private static int Int(JsonElement element, string what, int min, int max)
    {
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var value) || value < min || value > max) throw Invalid(what);
        return value;
    }

    private static double Number(JsonElement element, string what)
    {
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out var value) || !double.IsFinite(value)) throw Invalid(what);
        return value;
    }

    private static string Short(string text) => text.Length <= 24 ? text : text[..24] + "…";

    private static ControllerProfileException Invalid(string what) =>
        new(string.Create(CultureInfo.InvariantCulture, $"Perfil inválido: {what} fora do formato esperado."));
}
