using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Appearance;
using ControlFS.Infrastructure.Archives;
using ControlFS.Infrastructure.Windows.Settings;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

/// <summary>Boas-vindas (#231): só na primeira execução, guiadas por ações semânticas, com efeito imediato e conclusão salva.</summary>
public class OnboardingJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();
    private readonly TempDir _data = new();

    public void Dispose()
    {
        _tmp.Dispose();
        _data.Dispose();
    }

    private AppController Launch(string dataDirectory, bool offer = true)
    {
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService(), new JsonSettingsStore(dataDirectory)) { OfferOnboarding = offer };
        app.Start();
        return app;
    }

    [Fact]
    public void First_run_walks_the_steps_with_the_controller_applies_settings_at_once_and_is_not_shown_again() => UiContext.Run(async () =>
    {
        var app = Launch(_data.Path);
        var themes = new List<ThemeMode>();
        app.SettingsChanged += s => themes.Add(s.Theme);
        var d = new Driver(app);
        var onboarding = Assert.IsType<OnboardingModal>(app.TopModal);
        Assert.Equal(OnboardingStep.Welcome, onboarding.Step);
        Assert.Contains(app.Hints, h => h is { Action: InputAction.OpenAppMenu, Label: "Pular" });
        Assert.StartsWith("Boas-vindas, passo 1 de 5", app.DescribeFocus().Context, StringComparison.Ordinal); // Narrador

        d.Press(InputAction.Confirm); // Começar
        Assert.Equal(OnboardingStep.Controls, onboarding.Step);
        Assert.Contains(onboarding.ControlLegend, h => h is { Action: InputAction.OpenContextMenu, Label: "Ações" });
        d.Press(InputAction.Back); // Voltar volta um passo
        Assert.Equal(OnboardingStep.Welcome, onboarding.Step);
        d.Press(InputAction.NextRegion);
        d.Press(InputAction.Confirm); // Continuar
        Assert.Equal(OnboardingStep.Basics, onboarding.Step);

        // Ajuste com efeito imediato: o tema troca na hora, a janela recebe e o foco continua no ajuste.
        d.Press(InputAction.NavigateDown);
        d.Press(InputAction.NavigateDown);
        Assert.StartsWith("Tema:", onboarding.FocusedOption!.Label, StringComparison.Ordinal);
        d.Press(InputAction.Confirm);
        Assert.Equal(ThemeMode.Dark, app.Settings.Theme);
        Assert.Equal(ThemeMode.Dark, themes[^1]);
        Assert.Equal("Tema: escuro", onboarding.FocusedOption!.Label);
        Assert.Same(onboarding, app.TopModal);

        d.Press(InputAction.NextRegion); // privacidade
        d.Press(InputAction.NextRegion); // convite para o tutorial
        Assert.Equal(OnboardingStep.Tutorial, onboarding.Step);
        d.Press(InputAction.NavigateDown);
        Assert.Equal("Agora não", onboarding.FocusedOption!.Label);
        d.Press(InputAction.Confirm);
        Assert.Null(app.TopModal);
        Assert.Null(app.Tutorial);
        Assert.True(app.Settings.OnboardingCompleted);
        await d.Idle();

        var again = Launch(_data.Path);
        Assert.Null(again.TopModal); // concluídas: nunca mais sozinhas
        Assert.Equal(ThemeMode.Dark, again.Settings.Theme);
    });

    [Fact]
    public void Reinstalls_see_it_once_the_harness_never_unless_asked_and_menu_reopens_it_with_start_skipping() => UiContext.Run(async () =>
    {
        // Preferências de antes das boas-vindas (quem reinstala ou atualiza): ainda não viram, então aparecem uma vez...
        var upgraded = _data.MakeDir("upgraded");
        await File.WriteAllTextAsync(Path.Join(upgraded, "settings.json"), "{ \"AutoCheckUpdates\": false }");
        var reinstalled = Launch(upgraded);
        Assert.IsType<OnboardingModal>(reinstalled.TopModal);
        reinstalled.SkipOnboarding();
        Assert.Null(Launch(upgraded).TopModal); // ...e, vistas ou puladas, nunca mais sozinhas
        // Quem já as viu (o script de UI Automation grava isso) não as vê.
        var seen = _data.MakeDir("seen");
        await File.WriteAllTextAsync(Path.Join(seen, "settings.json"), "{ \"OnboardingCompleted\": true }");
        Assert.Null(Launch(seen).TopModal);
        // Primeira execução sem a janela real (testes, gerador de capturas, --no-onboarding): não aparecem.
        var app = Launch(_data.MakeDir("fresh"), offer: false);
        Assert.Null(app.TopModal);

        var d = new Driver(app);
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Ajuda e tutorial");
        await d.ChooseMenu("Rever boas-vindas");
        Assert.IsType<OnboardingModal>(app.TopModal);
        d.Press(InputAction.OpenAppMenu); // Start pula tudo, sem confirmação
        Assert.Null(app.TopModal);
        Assert.Contains("Menu → Ajuda", app.StatusMessage, StringComparison.Ordinal);
        Assert.True(app.Settings.OnboardingCompleted);
    });
}
