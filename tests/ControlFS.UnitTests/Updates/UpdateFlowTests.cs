using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Archives;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Updates;

public class UpdateFlowTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private sealed class FakeUpdates(bool installed = true, bool available = true) : IUpdateService
    {
        public ReleaseVersion CurrentVersion { get; } = ReleaseVersion.Parse("0.1.0-alpha.1");
        public bool IsInstalled { get; } = installed;
        public int Checks { get; private set; }
        public bool? LaunchedWithRelaunch { get; private set; }
        public bool? IncludedPrereleases { get; private set; }
        private static readonly UpdateManifest Manifest = new(ReleaseVersion.Parse("0.1.0-alpha.2"), "nextestudios/ControlFS", null,
            new ReleaseAsset("ControlFS-Setup-x64.exe", 10, new string('a', 64)), null, "https://github.com/nextestudios/ControlFS/releases/tag/v0.1.0-alpha.2");

        public Task<UpdateCheckResult> CheckAsync(bool includePrereleases, CancellationToken cancellationToken)
        {
            Checks++;
            IncludedPrereleases = includePrereleases;
            return Task.FromResult(available ? new UpdateCheckResult(UpdateCheckOutcome.UpdateAvailable, Manifest) : new UpdateCheckResult(UpdateCheckOutcome.UpToDate));
        }

        public async Task<ReadyUpdate> DownloadAsync(UpdateManifest manifest, IProgress<long>? progress, CancellationToken cancellationToken)
        {
            await Task.Yield();
            return new ReadyUpdate(manifest, "setup.exe");
        }

        public void LaunchInstaller(ReadyUpdate update, bool relaunchAfterInstall) => LaunchedWithRelaunch = relaunchAfterInstall;
    }

    private sealed class MemorySettings(AppSettings initial) : ISettingsStore
    {
        public AppSettings Current { get; private set; } = initial;
        public SettingsLoadResult Load() => new(Current, false, null);
        public void Save(AppSettings settings) => Current = settings;
    }

    private (AppController App, FakeUpdates Updates, MemorySettings Settings) Boot(AppSettings? settings = null, bool installed = true, bool available = true)
    {
        var updates = new FakeUpdates(installed, available);
        var store = new MemorySettings(settings ?? new AppSettings());
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService(), store, updates);
        return (app, updates, store);
    }

    [Fact]
    public void Automatic_check_downloads_and_offers_install_and_restart() => UiContext.Run(async () =>
    {
        var (app, updates, store) = Boot();
        var exited = false;
        app.ExitRequested += () => exited = true;
        app.Start();
        await app.WhenIdleAsync();

        Assert.Equal(1, updates.Checks);
        Assert.True(updates.IncludedPrereleases); // versão atual é pré-lançamento: canal automático inclui pré-lançamentos
        Assert.Equal(UpdateState.Ready, app.UpdateState);
        Assert.NotNull(store.Current.LastUpdateCheck);
        var dialog = Assert.IsType<DialogModal>(app.TopModal);
        Assert.StartsWith("Atualização 0.1.0-alpha.2 pronta", dialog.Title, StringComparison.Ordinal);
        Assert.Equal("Instalar e reiniciar", dialog.Options[dialog.FocusIndex].Label);
        app.Handle(InputAction.Confirm);
        Assert.True(updates.LaunchedWithRelaunch);
        Assert.True(exited);
        app.PrepareShutdown(); // não dispara o instalador duas vezes
        Assert.True(updates.LaunchedWithRelaunch);
    });

    [Fact]
    public void Postponed_update_installs_silently_on_exit_without_relaunch() => UiContext.Run(async () =>
    {
        var (app, updates, _) = Boot();
        app.Start();
        await app.WhenIdleAsync();
        app.Handle(InputAction.Back); // "Depois"
        Assert.Null(updates.LaunchedWithRelaunch);
        app.PrepareShutdown();
        Assert.False(updates.LaunchedWithRelaunch);
    });

    [Fact]
    public void Install_on_exit_can_be_turned_off() => UiContext.Run(async () =>
    {
        var (app, updates, _) = Boot(new AppSettings { InstallUpdatesOnExit = false });
        app.Start();
        await app.WhenIdleAsync();
        app.Handle(InputAction.Back);
        app.PrepareShutdown();
        Assert.Null(updates.LaunchedWithRelaunch);
    });

    [Fact]
    public void Automatic_check_respects_the_setting_and_the_daily_interval() => UiContext.Run(async () =>
    {
        var (off, offUpdates, _) = Boot(new AppSettings { AutoCheckUpdates = false });
        off.Start();
        await off.WhenIdleAsync();
        Assert.Equal(0, offUpdates.Checks);

        var (recent, recentUpdates, _) = Boot(new AppSettings { LastUpdateCheck = DateTimeOffset.UtcNow.AddHours(-2) });
        recent.Start();
        await recent.WhenIdleAsync();
        Assert.Equal(0, recentUpdates.Checks);
    });

    [Fact]
    public void Portable_mode_only_announces_the_new_version() => UiContext.Run(async () =>
    {
        var (app, updates, _) = Boot(installed: false);
        app.Start();
        await app.WhenIdleAsync();
        Assert.Equal(UpdateState.AvailableManual, app.UpdateState);
        var dialog = Assert.IsType<DialogModal>(app.TopModal);
        Assert.Contains("manual", dialog.Message, StringComparison.Ordinal);
        app.Handle(InputAction.Back);
        app.PrepareShutdown();
        Assert.Null(updates.LaunchedWithRelaunch);
    });

    [Fact]
    public void Manual_check_from_the_menu_reports_up_to_date() => UiContext.Run(async () =>
    {
        var (app, updates, _) = Boot(new AppSettings { AutoCheckUpdates = false }, available: false);
        var d = new Driver(app);
        app.Start();
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Atualizações");
        await d.ChooseMenu("Verificar agora");
        var dialog = await d.WaitDialog("Atualizações");
        Assert.Equal("Você já está na versão mais recente.", dialog.Message);
        Assert.Equal(1, updates.Checks);
    });
}
