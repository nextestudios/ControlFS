using ControlFS.Core.Actions;

namespace ControlFS.Core.Contracts;

public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public ConfirmBackConvention Convention { get; init; } = ConfirmBackConvention.SouthConfirms;
    public ButtonLabelStyle LabelStyle { get; init; } = ButtonLabelStyle.Generic;
    public bool ShowHidden { get; init; }
    public bool ReducedMotion { get; init; }
    public string? LastLocation { get; init; }
    public IReadOnlyList<string> Favorites { get; init; } = [];
}

public sealed record SettingsLoadResult(AppSettings Settings, bool RecoveredFromCorruption, string? Notice);

public interface ISettingsStore
{
    SettingsLoadResult Load();

    void Save(AppSettings settings);
}
