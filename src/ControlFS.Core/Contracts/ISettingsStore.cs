using ControlFS.Core.Actions;

namespace ControlFS.Core.Contracts;

/// <summary>Densidade da lista: confortável (duas linhas, para TV) ou compacta (uma linha com colunas).</summary>
public enum ListDensity
{
    Comfortable,
    Compact,
}

public sealed record AppSettings
{
    /// <summary>2: <see cref="ButtonLabelStyle.Automatic"/> passou a ser o padrão (antes era Generic).</summary>
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public ConfirmBackConvention Convention { get; init; } = ConfirmBackConvention.SouthConfirms;
    public ButtonLabelStyle LabelStyle { get; init; } = ButtonLabelStyle.Automatic;
    public bool ShowHidden { get; init; }
    public bool ReducedMotion { get; init; }
    public ListDensity Density { get; init; } = ListDensity.Comfortable;
    public string? LastLocation { get; init; }
    public IReadOnlyList<string> Favorites { get; init; } = [];

    /// <summary>Lembra pastas e arquivos abertos recentemente (somente neste computador). Desligar apaga as listas.</summary>
    public bool RememberRecents { get; init; } = true;

    /// <summary>Pastas visitadas recentemente, a mais recente primeiro (lista limitada).</summary>
    public IReadOnlyList<string> RecentFolders { get; init; } = [];

    /// <summary>Arquivos e compactados abertos recentemente, o mais recente primeiro (lista limitada).</summary>
    public IReadOnlyList<string> RecentFiles { get; init; } = [];

    /// <summary>Verifica novas versões ao abrir (no máximo uma vez por dia). Desligável no menu.</summary>
    public bool AutoCheckUpdates { get; init; } = true;

    /// <summary>Instala em silêncio, ao sair, uma atualização já baixada e verificada.</summary>
    public bool InstallUpdatesOnExit { get; init; } = true;

    /// <summary>Recebe versões de pré-lançamento. Null = automático (sim se a versão atual for pré-lançamento).</summary>
    public bool? IncludePrereleases { get; init; }

    public DateTimeOffset? LastUpdateCheck { get; init; }
}

public sealed record SettingsLoadResult(AppSettings Settings, bool RecoveredFromCorruption, string? Notice);

public interface ISettingsStore
{
    SettingsLoadResult Load();

    void Save(AppSettings settings);
}
