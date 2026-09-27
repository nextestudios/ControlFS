using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Input.Mapping;
using ControlFS.Infrastructure.Archives;
using ControlFS.Infrastructure.Windows.Settings;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

/// <summary>
/// Joystick genérico (hat + botões, sem perfil de gamepad) mapeado pelo assistente, com o perfil salvo em disco
/// temporário. O hardware real não roda na CI: aqui entram só eventos crus simulados.
/// </summary>
public class ControllerMappingJourneyTests : IDisposable
{
    private static readonly InputDeviceInfo Pad = new("sdl:7", "Generic USB Joystick", "03000000790000000600000000000000", 0x0079, 0x0006,
        IsGamepad: false, IsVirtual: false, TypeName: "joystick sem perfil", Path: null);

    private readonly TempDir _tmp = new();
    private TimeSpan _now;

    public void Dispose() => _tmp.Dispose();

    private sealed class FakeRawSource : IRawControllerSource
    {
        public IReadOnlyList<InputDeviceInfo> RawDevices { get; set; } = [Pad];

        public RawJoystickState? GetState(string deviceKey) => new([], [false, false, false], [0]);
    }

    private AppController NewApp(JsonControllerProfileStore store)
    {
        var app = new AppController(new TestFileSystem(_tmp.MakeDir("files")), new ArchiveService(), controllerProfiles: store) { Clock = () => _now };
        app.AttachRawControllers(new FakeRawSource());
        app.Start();
        return app;
    }

    private void Wait(AppController app, double seconds)
    {
        _now += TimeSpan.FromSeconds(seconds);
        app.TickControllers();
    }

    private static void Button(AppController app, int index)
    {
        app.OnRawInput(Pad, RawInputEvent.Button(index, true));
        app.OnRawInput(Pad, RawInputEvent.Button(index, false));
    }

    private static void Hat(AppController app, int mask)
    {
        app.OnRawInput(Pad, RawInputEvent.Hat(0, mask));
        app.OnRawInput(Pad, RawInputEvent.Hat(0, HatDirections.Centered));
    }

    /// <summary>Direções no hat, confirmar no botão 0, voltar no botão 1 (que também pula os opcionais).</summary>
    private void MapRequiredAndSkipOptionals(AppController app)
    {
        Wait(app, 1.1); // neutro
        Hat(app, HatDirections.Up);
        Hat(app, HatDirections.Down);
        Hat(app, HatDirections.Left);
        Hat(app, HatDirections.Right);
        Button(app, 0);
        Button(app, 1);
        while (app.MappingWizard!.Wizard.Phase != MappingPhase.Review) Button(app, 1);
    }

    [Fact]
    public void Generic_joystick_is_mapped_by_itself_saved_and_used_after_restart() => UiContext.Run(() =>
    {
        var store = new JsonControllerProfileStore(_tmp.MakeDir("data"));
        var app = NewApp(store);

        // Sem perfil, o joystick não navega; segurar um botão dele abre o assistente.
        Assert.True(app.OnRawInput(Pad, RawInputEvent.Button(2, true)));
        Assert.Contains("segure qualquer botão", app.StatusMessage, StringComparison.Ordinal);
        Wait(app, 2.1);
        Assert.IsType<MappingWizardModal>(app.TopModal);
        app.OnRawInput(Pad, RawInputEvent.Button(2, false));

        MapRequiredAndSkipOptionals(app);
        Assert.Empty(store.Load().Profiles); // teste antes de salvar: nada no disco ainda

        // No teste, o próprio joystick escolhe "Salvar perfil" com o mapeamento novo.
        Hat(app, HatDirections.Down);
        Hat(app, HatDirections.Up);
        Assert.Equal(0, app.MappingWizard!.ReviewFocus);
        Button(app, 0);
        Assert.Null(app.TopModal);
        Assert.Contains("Perfil salvo", app.StatusMessage, StringComparison.Ordinal);

        // Reabrir o ControlFS: o perfil vale para o mesmo joystick e a camada de entrada o aplica.
        var restarted = NewApp(new JsonControllerProfileStore(_tmp.Sub("data")));
        var profile = restarted.ProfileFor(Pad);
        Assert.NotNull(profile);
        Assert.False(restarted.OnRawInput(Pad, RawInputEvent.Hat(0, HatDirections.Down)));
        var controls = new List<PhysicalControl>();
        new ControllerProfileTranslator(profile).Apply(RawInputEvent.Hat(0, HatDirections.Down), (c, pressed) => { if (pressed) controls.Add(c); });
        Assert.Equal(InputAction.NavigateDown, new ActionMap(restarted.Settings.Convention).Resolve(Assert.Single(controls)));
        return Task.CompletedTask;
    });

    [Fact]
    public void Existing_profile_is_only_replaced_after_confirmation_and_cancelling_never_touches_it() => UiContext.Run(async () =>
    {
        var store = new JsonControllerProfileStore(_tmp.MakeDir("data"));
        var app = NewApp(store);
        var d = new Driver(app);
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Controles sem perfil");
        await d.ChooseMenu("Configurar Generic USB Joystick");
        MapRequiredAndSkipOptionals(app);
        d.Press(InputAction.Confirm); // teclado: "Salvar perfil" (primeiro perfil: sem pergunta)
        var file = Assert.Single(Directory.GetFiles(store.Directory, "*.json"));
        var saved = File.ReadAllBytes(file);

        // Esc no meio do assistente: cancela sem tocar no perfil existente, que continua valendo.
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Controles sem perfil");
        await d.ChooseMenu("Configurar Generic USB Joystick");
        Wait(app, 1.1);
        Hat(app, HatDirections.Left); // "Cima" num lugar diferente
        d.Press(InputAction.Back);
        Assert.Null(app.TopModal);
        Assert.Equal(saved, File.ReadAllBytes(file));
        Assert.NotNull(app.ProfileFor(Pad));

        // Salvar por cima pede confirmação com foco em Cancelar; cancelar mantém o arquivo.
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Controles sem perfil");
        await d.ChooseMenu("Configurar Generic USB Joystick");
        Wait(app, 1.1);
        Hat(app, HatDirections.Down); // cima e baixo trocados
        Hat(app, HatDirections.Up);
        Hat(app, HatDirections.Left);
        Hat(app, HatDirections.Right);
        Button(app, 0);
        Button(app, 1);
        while (app.MappingWizard!.Wizard.Phase != MappingPhase.Review) Button(app, 1);
        d.Press(InputAction.Confirm);
        var confirm = await d.WaitDialog("Substituir o perfil salvo?");
        Assert.Equal("Cancelar", confirm.Options[confirm.FocusIndex].Label);
        d.Press(InputAction.Confirm);
        Assert.IsType<MappingWizardModal>(app.TopModal);
        Assert.Equal(saved, File.ReadAllBytes(file));

        d.Press(InputAction.Confirm);
        d.ChooseOption(await d.WaitDialog("Substituir o perfil salvo?"), "Substituir");
        Assert.Null(app.TopModal);
        Assert.NotEqual(saved, File.ReadAllBytes(file));
        Assert.Equal(saved, File.ReadAllBytes(file + ".bak"));
        Assert.Equal(new RawBinding(RawInputKind.Hat, 0, HatDirections.Down), app.ProfileFor(Pad)!.Bindings[PhysicalControl.DPadUp]);
    });

    [Fact]
    public void Importing_a_tampered_profile_is_refused_and_a_valid_export_imports_back() => UiContext.Run(async () =>
    {
        var store = new JsonControllerProfileStore(_tmp.MakeDir("data"));
        var app = NewApp(store);
        var d = new Driver(app);
        app.StartMapping(Pad);
        MapRequiredAndSkipOptionals(app);
        d.Press(InputAction.Confirm);
        var exported = store.Export(app.ProfileFor(Pad)!, _tmp.MakeDir("export"));

        var tampered = Path.Join(_tmp.Path, "export", "tampered.json");
        File.WriteAllText(tampered, File.ReadAllText(exported).Replace("\"name\"", "\"run\": \"cmd.exe /c calc\",\n  \"name\"", StringComparison.Ordinal));
        app.ImportProfile(tampered);
        var refused = await d.WaitDialog("Perfil não importado");
        Assert.Contains("campo desconhecido", refused.Message, StringComparison.Ordinal);
        d.Press(InputAction.Back);

        // O perfil exportado (válido) entra em outra instalação e passa a valer para o mesmo controle.
        var other = new JsonControllerProfileStore(_tmp.MakeDir("other"));
        var fresh = NewApp(other);
        fresh.ImportProfile(exported);
        var fd = new Driver(fresh);
        fd.ChooseOption(await fd.WaitDialog("Importar perfil de controle?"), "Importar");
        Assert.Null(fresh.TopModal);
        Assert.Equal(app.ProfileFor(Pad)!.Bindings.OrderBy(b => b.Key), fresh.ProfileFor(Pad)!.Bindings.OrderBy(b => b.Key));
        Assert.Single(other.Load().Profiles);
    });
}
