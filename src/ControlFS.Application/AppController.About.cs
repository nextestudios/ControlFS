using System.Reflection;
using ControlFS.Application.State;
using ControlFS.Core.Actions;

namespace ControlFS.Application;

public sealed partial class AppController
{
    public const string SourceUrl = "https://github.com/nextestudios/ControlFS";

    /// <summary>Versão em execução (a mesma que o atualizador usa).</summary>
    public string AppVersion =>
        _updates?.CurrentVersion.ToString()
        ?? (Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "?");

    internal void ShowAbout()
    {
        var channel = Settings.IncludePrereleases ?? (_updates?.CurrentVersion.IsPrerelease ?? true) ? "inclui pré-lançamentos" : "somente estáveis";
        PushModal(new AboutModal(AppVersion,
        [
            ("Versão", AppVersion),
            ("Atualizações", _updates is null ? "indisponíveis nesta compilação" : $"{(_updates.IsInstalled ? "automáticas (versão instalada)" : "manuais (versão portátil)")} · {channel}"),
            ("Licença", "GNU Affero General Public License v3.0 only (AGPL-3.0-only)"),
            ("Código-fonte", SourceUrl),
            ("Avisos de terceiros", "THIRD_PARTY_NOTICES.md, no código-fonte e na pasta de instalação"),
            ("Privacidade", "sem login, sem telemetria; tudo fica no seu computador"),
        ]));
    }

    private void HandleAbout(AboutModal modal, InputAction action)
    {
        if (action is InputAction.Back or InputAction.Confirm or InputAction.OpenAppMenu) CloseModal(modal);
    }
}
