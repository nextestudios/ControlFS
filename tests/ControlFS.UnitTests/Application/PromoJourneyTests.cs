using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Infrastructure.Archives;
using ControlFS.Infrastructure.Windows.Settings;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

/// <summary>"Mais da equipe": uma vez, depois das boas-vindas e do tutorial, sem internet e sem repetir.</summary>
public class PromoJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();
    private readonly TempDir _data = new();

    public void Dispose()
    {
        _tmp.Dispose();
        _data.Dispose();
    }

    private sealed class RecordingShell : IShellService
    {
        public List<string> Links { get; } = [];
        public void Open(string path) { }
        public void OpenWith(string path) { }
        public void RevealInExplorer(string path) { }
        public void OpenLink(Uri url) => Links.Add(url.AbsoluteUri);
    }

    private AppController Launch(string data, RecordingShell shell, bool offerPromo = true, bool offerOnboarding = true)
    {
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService(), new JsonSettingsStore(data), shell: shell)
        {
            OfferOnboarding = offerOnboarding,
            OfferPromo = offerPromo,
        };
        app.Start();
        return app;
    }

    [Fact]
    public void Shows_once_after_the_welcome_and_the_tutorial_opens_links_only_on_confirm_and_never_returns() => UiContext.Run(async () =>
    {
        var shell = new RecordingShell();
        var app = Launch(_data.Path, shell);
        var d = new Driver(app);

        // Primeiro as boas-vindas; a tela da equipe não aparece por cima delas.
        Assert.IsType<OnboardingModal>(app.TopModal);
        for (var i = 0; i < 4; i++) d.Press(InputAction.NextRegion);
        Assert.Equal(OnboardingStep.Tutorial, ((OnboardingModal)app.TopModal!).Step);
        d.Press(InputAction.Confirm); // "Começar tutorial": a tela da equipe espera o tutorial terminar
        Assert.Null(app.TopModal);
        Assert.NotNull(app.Tutorial);
        Assert.NotEqual(true, app.Settings.PromoSeen);

        app.SkipTutorial();
        var promo = Assert.IsType<PromoModal>(app.TopModal);
        Assert.True(app.Settings.PromoSeen); // vista já ao abrir
        Assert.Equal(["NextBoost PRO", "Console Mode"], promo.Cards.Select(c => c.Name));
        Assert.Empty(shell.Links); // nada abre sozinho

        d.Press(InputAction.Confirm);
        Assert.Equal(["https://nextboost.pro/"], shell.Links);
        Assert.Same(promo, app.TopModal); // continua aberta: o navegador abre por cima
        d.Press(InputAction.NavigateRight);
        d.Press(InputAction.Confirm);
        Assert.Equal("https://github.com/lippdev/consolemode", shell.Links[^1]);
        d.Press(InputAction.NavigateRight); // no segundo cartão, a direita não passa dele
        Assert.Equal(1, promo.FocusIndex);
        d.Press(InputAction.NavigateDown);
        Assert.True(promo.CloseFocused);
        Assert.Contains(app.Hints, h => h.Action == InputAction.Confirm && h.Label == "Fechar");
        d.Press(InputAction.Confirm);
        Assert.Null(app.TopModal);
        await d.Idle();

        // Nunca mais sozinha: nem ao reabrir o app, nem depois de outros diálogos.
        var again = Launch(_data.Path, shell);
        Assert.Null(again.TopModal);

        // Rever pelo menu Ajuda continua possível; Voltar fecha.
        var d2 = new Driver(again);
        d2.Press(InputAction.OpenAppMenu);
        await d2.ChooseMenu("Ajuda e tutorial");
        await d2.ChooseMenu("Mais da equipe");
        Assert.IsType<PromoModal>(again.TopModal);
        d2.Press(InputAction.Back);
        Assert.Null(again.TopModal);
    });

    [Fact]
    public void Appears_right_away_for_someone_who_already_saw_the_welcome_and_never_for_the_harness() => UiContext.Run(async () =>
    {
        var seenWelcome = _data.MakeDir("seen");
        await File.WriteAllTextAsync(Path.Join(seenWelcome, "settings.json"), "{ \"OnboardingCompleted\": true }");
        var shell = new RecordingShell();

        // Testes, gerador de capturas e --no-onboarding não a oferecem.
        Assert.Null(Launch(seenWelcome, shell, offerPromo: false).TopModal);
        Assert.Null(Launch(_data.MakeDir("fresh"), shell, offerPromo: false, offerOnboarding: false).TopModal);

        // Quem já viu as boas-vindas (atualizou de uma versão sem ela): logo ao abrir, uma vez.
        var app = Launch(seenWelcome, shell);
        Assert.IsType<PromoModal>(app.TopModal);
        new Driver(app).Press(InputAction.Back);
        Assert.Null(app.TopModal);
        Assert.Null(Launch(seenWelcome, shell).TopModal);
    });

    [Fact]
    public void Main_menu_opens_the_catalog_which_grows_in_rows_and_handles_being_empty() => UiContext.Run(async () =>
    {
        var shell = new RecordingShell();
        var app = Launch(_data.MakeDir("menu"), shell, offerPromo: false, offerOnboarding: false);
        var d = new Driver(app);

        // Menu principal → Mais da equipe (entrada própria, não só em Ajuda), sempre a pedido.
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Mais da equipe");
        var promo = Assert.IsType<PromoModal>(app.TopModal);
        Assert.All(promo.Cards, c => Assert.Equal("Windows 10 e 11", c.Platform));
        d.Press(InputAction.Back);

        // Catálogo com três apps: linhas de duas colunas, Baixo/Cima andam entre elas e chegam a Fechar.
        PromoCard Make(string n) => new(n, "frase", "descrição", "Abrir " + n, "https://exemplo.com/" + n, "promo-x.png", "Windows");
        app.TeamCatalog = [Make("a"), Make("b"), Make("c")];
        app.ShowPromo();
        promo = (PromoModal)app.TopModal!;
        d.Press(InputAction.NavigateRight);
        Assert.Equal(1, promo.FocusIndex);
        d.Press(InputAction.NavigateDown); // b não tem nada abaixo: vai a Fechar
        Assert.True(promo.CloseFocused);
        d.Press(InputAction.NavigateUp);
        Assert.Equal(2, promo.FocusIndex); // volta ao último cartão (c, segunda linha)
        d.Press(InputAction.NavigateUp);
        Assert.Equal(0, promo.FocusIndex); // c está sob a
        d.Press(InputAction.NavigateDown);
        Assert.Equal(2, promo.FocusIndex);
        d.Press(InputAction.Confirm);
        Assert.Equal("https://exemplo.com/c", shell.Links[^1]);
        d.Press(InputAction.Back);

        // Catálogo vazio: a tela abre, só oferece Fechar e não quebra.
        app.TeamCatalog = [];
        app.ShowPromo();
        promo = (PromoModal)app.TopModal!;
        Assert.True(promo.CloseFocused);
        d.Press(InputAction.NavigateRight);
        d.Press(InputAction.NavigateDown);
        d.Press(InputAction.Confirm);
        Assert.Null(app.TopModal);
        Assert.Empty(shell.Links.Where(l => l.Contains("nextboost", StringComparison.Ordinal)));
    });
}

