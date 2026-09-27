using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Input;
using ControlFS.Core.Input.Mapping;
using ControlFS.Infrastructure.Archives;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

/// <summary>
/// Tela "Teste de controles" (#78) com o roteador real: cada pressão vira uma linha "controle → ação", Confirmar e
/// Voltar só agem quando mantidos, e o relatório não leva caminhos nem GUIDs. Hardware real fica no docs/TESTING.md.
/// </summary>
public class ControllerTestJourneyTests : IDisposable
{
    private static readonly InputDeviceInfo Xbox = new("sdl:3", "Xbox Wireless Controller", "0300a1b2c3d4e5f60000000000000000", 0x045E, 0x0B13,
        IsGamepad: true, IsVirtual: false, TypeName: "xboxone", Path: @"\\?\HID#VID_045E&PID_0B13#serial-1234", Family: ControllerFamily.Xbox);

    private static readonly InputDeviceInfo Pad = new("sdl:7", "Generic USB Joystick", "03000000790000000600000000000000", 0x0079, 0x0006,
        IsGamepad: false, IsVirtual: false, TypeName: "joystick sem perfil", Path: @"\\?\HID#VID_0079&PID_0006#serial-5678");

    private readonly TempDir _tmp = new();
    private TimeSpan _now;

    public void Dispose() => _tmp.Dispose();

    private sealed class FakeDiagnostics : IControllerDiagnostics
    {
        public IReadOnlyCollection<InputDeviceInfo> Devices { get; } = [Xbox, Pad];
        public string? ActiveDeviceKey { get; set; }
        public string BackendDescription => "SDL 3.2.0";
    }

    [Fact]
    public void Test_screen_shows_each_press_and_its_action_and_leaves_only_on_hold() => UiContext.Run(async () =>
    {
        var app = new AppController(new TestFileSystem(_tmp.MakeDir("files")), new ArchiveService()) { Clock = () => _now };
        var diagnostics = new FakeDiagnostics();
        app.AttachControllerDiagnostics(diagnostics);
        string? copied = null;
        app.CopyText = text => { copied = text; return true; };
        app.Start();
        var router = new InputRouter(new ActionMap(app.Settings.Convention), InputSettings.Default, app.Handle);
        router.ActiveDeviceChanged += key => diagnostics.ActiveDeviceKey = key;
        void Control(InputDeviceInfo device, PhysicalControl control, bool pressed)
        {
            app.BeginTestInput(device, control, pressed); // como o InputHost faz
            router.OnControl(device.SessionKey, control, pressed, _now);
            app.EndTestInput();
        }
        void Tap(PhysicalControl control)
        {
            Control(Xbox, control, true);
            Control(Xbox, control, false);
        }

        var d = new Driver(app);
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Teste de controles");
        var modal = Assert.IsType<ControllerTestModal>(app.TopModal);

        // Pressões curtas só registram: Confirmar e Voltar também estão sendo testados.
        Tap(PhysicalControl.South);
        Tap(PhysicalControl.East);
        Tap(PhysicalControl.Start);
        Assert.Same(modal, app.TopModal);
        Assert.Equal(
            [(PhysicalControl.South, InputAction.Confirm), (PhysicalControl.East, InputAction.Back), (PhysicalControl.Start, InputAction.OpenAppMenu)],
            modal.Lines.Select(l => (l.Control!.Value, l.Action!.Value)));
        Assert.Null(copied);

        // Joystick sem perfil: a entrada crua aparece, sem ação.
        app.RecordTestRaw(Pad, RawInputEvent.Button(2, true));
        Assert.Null(modal.Lines[^1].Action);

        // Segurar Confirmar copia o relatório; segurar Voltar sai.
        Control(Xbox, PhysicalControl.South, true);
        _now += AppController.HoldInControllerTest;
        app.TickControllers();
        Control(Xbox, PhysicalControl.South, false);
        Assert.NotNull(copied);
        Assert.Contains("#1 South -> Confirm", copied, StringComparison.Ordinal);
        Assert.Contains("VID:PID 045E:0B13", copied, StringComparison.Ordinal);
        Assert.Contains("#2 button 3 -> (no action)", copied, StringComparison.Ordinal);
        Assert.DoesNotContain("serial", copied, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Xbox.StableId, copied, StringComparison.OrdinalIgnoreCase);
        Assert.Same(modal, app.TopModal);

        Control(Xbox, PhysicalControl.East, true);
        _now += TimeSpan.FromSeconds(0.5);
        app.TickControllers();
        Assert.Same(modal, app.TopModal); // ainda não
        _now += TimeSpan.FromSeconds(0.6);
        app.TickControllers();
        Assert.Null(app.TopModal);
    });
}
