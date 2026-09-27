using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Input;
using ControlFS.Infrastructure.Archives;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

/// <summary>
/// Menu → Controle ativo (#80) com o roteador real: o Steam Input expõe o DualSense físico e um "Steam Virtual
/// Gamepad"; o app avisa da duplicata, e escolher um faz só ele comandar a UI. Hardware real fica no docs/TESTING.md.
/// </summary>
public class ActiveControllerJourneyTests : IDisposable
{
    private static readonly InputDeviceInfo DualSense = new("sdl:1", "DualSense Wireless Controller", "physical", 0x054C, 0x0CE6,
        IsGamepad: true, IsVirtual: false, TypeName: "ps5", Path: null, Family: ControllerFamily.PlayStation);

    private static readonly InputDeviceInfo SteamPad = new("sdl:2", "Steam Virtual Gamepad", "virtual", 0x28DE, 0x11FF,
        IsGamepad: true, IsVirtual: false, TypeName: "xbox360", Path: null, Family: ControllerFamily.Xbox);

    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private sealed class RouterDiagnostics(InputRouter router) : IControllerDiagnostics
    {
        public IReadOnlyCollection<InputDeviceInfo> Devices { get; } = [DualSense, SteamPad];
        public string? ActiveDeviceKey => router.ActiveDeviceKey;
        public string BackendDescription => "SDL 3.2.0";
        public bool IsActiveDeviceLocked => router.IsActiveDeviceLocked;
        public void SelectActiveDevice(string? deviceKey) => router.SelectActiveDevice(deviceKey);
    }

    [Fact]
    public void Duplicate_is_diagnosed_and_the_chosen_controller_is_the_only_one_routed() => UiContext.Run(async () =>
    {
        var app = new AppController(new TestFileSystem(_tmp.MakeDir("files")), new ArchiveService());
        var router = new InputRouter(new ActionMap(app.Settings.Convention), InputSettings.Default, app.Handle);
        app.AttachControllerDiagnostics(new RouterDiagnostics(router));
        app.Start();
        void Tap(InputDeviceInfo device, PhysicalControl control)
        {
            router.OnControl(device.SessionKey, control, true, TimeSpan.Zero);
            router.OnControl(device.SessionKey, control, false, TimeSpan.Zero);
        }

        app.OnControllersChanged(); // o InputHost chama ao conectar
        Assert.Contains("Steam Input", app.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("Controle ativo", app.StatusMessage, StringComparison.Ordinal);

        var d = new Driver(app);
        Tap(DualSense, PhysicalControl.Start);
        await d.ChooseMenu("Controle ativo: automático");
        var menu = await d.WaitMenu();
        Assert.Equal("Controle ativo", menu.Title);
        var steam = menu.Items.Single(i => i.Label.StartsWith("Steam Virtual Gamepad", StringComparison.Ordinal));
        Assert.Contains("virtual (Steam Input)", steam.Detail, StringComparison.Ordinal);
        Assert.Contains("DualSense", steam.Detail, StringComparison.Ordinal);
        await d.ChooseMenu("Steam Virtual Gamepad");

        Assert.True(router.IsActiveDeviceLocked);
        Assert.Equal(SteamPad.SessionKey, router.ActiveDeviceKey);
        Assert.Equal(ControllerFamily.Xbox, app.ActiveController);

        // O físico não comanda mais, nem ocioso; o escolhido sim.
        Tap(DualSense, PhysicalControl.Start);
        Assert.Null(app.TopModal);
        Tap(SteamPad, PhysicalControl.Start);
        await d.ChooseMenu("Controle ativo: Steam Virtual Gamepad");
        await d.ChooseMenu("Automático");
        Assert.False(router.IsActiveDeviceLocked);
        Tap(DualSense, PhysicalControl.Start);
        Assert.IsType<MenuModal>(app.TopModal);
    });
}
