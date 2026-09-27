using System.Text.Json;
using System.Text.Json.Serialization;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;

namespace ControlFS.Infrastructure.Windows.Settings;

/// <summary>
/// Configurações em JSON versionado. Gravação segura: arquivo temporário + flush + troca atômica.
/// Arquivo corrompido é preservado com sufixo e substituído pelos padrões, com aviso.
/// </summary>
public sealed class JsonSettingsStore(string directory) : ISettingsStore
{
    private const long MaxSettingsBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string FilePath => Path.Join(directory, "settings.json");

    /// <summary>Pasta padrão do modo instalado: %LOCALAPPDATA%\ControlFS.</summary>
    public static string DefaultDirectory() =>
        Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ControlFS");

    public SettingsLoadResult Load()
    {
        if (!File.Exists(FilePath)) return new(new AppSettings(), false, null);
        try
        {
            var info = new FileInfo(FilePath);
            if (info.Length > MaxSettingsBytes) throw new InvalidDataException("Arquivo de configuração grande demais.");
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? throw new InvalidDataException("Vazio.");
            if (settings.SchemaVersion > AppSettings.CurrentSchemaVersion)
                return new(new AppSettings(), false, "Configuração criada por versão mais nova; usando padrões sem sobrescrever o arquivo.");
            return new(Migrate(settings), false, null);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or NotSupportedException)
        {
            var backup = FilePath + $".corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}";
            try { File.Move(FilePath, backup); } catch (IOException) { }
            return new(new AppSettings(), true, $"Configuração corrompida foi preservada em {Path.GetFileName(backup)} e os padrões foram restaurados.");
        }
    }

    private static AppSettings Migrate(AppSettings settings)
    {
        // v1 não tinha "automático": Generic era só o padrão, não uma escolha. Passa a seguir o controle ativo.
        if (settings.SchemaVersion < 2 && settings.LabelStyle == ButtonLabelStyle.Generic)
            settings = settings with { LabelStyle = ButtonLabelStyle.Automatic };
        return settings with { SchemaVersion = AppSettings.CurrentSchemaVersion };
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(directory);
        var temp = FilePath + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, settings, Options);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temp, FilePath, overwrite: true);
    }
}
