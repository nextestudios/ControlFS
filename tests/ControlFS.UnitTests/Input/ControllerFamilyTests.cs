using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Input;
using ControlFS.Infrastructure.Windows.Settings;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Input;

public class ControllerFamilyTests
{
    [Theory]
    [InlineData(true, "xboxone", 0x045E, ControllerFamily.Xbox)]
    [InlineData(true, "xbox360", 0x0E6F, ControllerFamily.Xbox)] // licenciado de terceiros: vale o tipo do SDL
    [InlineData(true, "ps5", 0x054C, ControllerFamily.PlayStation)] // DualSense
    [InlineData(true, "ps4", 0x054C, ControllerFamily.PlayStation)] // DualShock 4
    [InlineData(true, "switchpro", 0x057E, ControllerFamily.Nintendo)]
    [InlineData(true, "joyconpair", 0x057E, ControllerFamily.Nintendo)]
    [InlineData(true, "standard", 0x054C, ControllerFamily.PlayStation)] // SDL sem tipo: vendor decide
    [InlineData(true, "unknown", 0x1234, ControllerFamily.Generic)]
    [InlineData(false, "joystick sem perfil", 0x045E, ControllerFamily.Generic)] // sem perfil de gamepad
    public void Family_comes_from_sdl_type_then_vendor(bool isGamepad, string type, int vendor, ControllerFamily expected) =>
        Assert.Equal(expected, ControllerFamilies.Detect(isGamepad, type, (ushort)vendor));

    [Fact]
    public void Manual_style_overrides_the_active_family_and_automatic_follows_it()
    {
        Assert.Equal(ControllerFamily.PlayStation, ControllerFamilies.Resolve(ButtonLabelStyle.Automatic, ControllerFamily.PlayStation));
        Assert.Equal(ControllerFamily.Generic, ControllerFamilies.Resolve(ButtonLabelStyle.Automatic, null));
        Assert.Equal(ControllerFamily.Nintendo, ControllerFamilies.Resolve(ButtonLabelStyle.Nintendo, ControllerFamily.Xbox));
    }

    [Fact]
    public void Settings_v1_with_the_old_generic_default_migrate_to_automatic_but_explicit_choices_stay()
    {
        using var tmp = new TempDir();
        var store = new JsonSettingsStore(tmp.Path);
        File.WriteAllText(store.FilePath, """{ "SchemaVersion": 1, "LabelStyle": "Generic", "ShowHidden": true }""");
        var loaded = store.Load().Settings;
        Assert.Equal(ButtonLabelStyle.Automatic, loaded.LabelStyle);
        Assert.True(loaded.ShowHidden);

        File.WriteAllText(store.FilePath, """{ "SchemaVersion": 1, "LabelStyle": "PlayStation" }""");
        Assert.Equal(ButtonLabelStyle.PlayStation, store.Load().Settings.LabelStyle);

        store.Save(new AppSettings { LabelStyle = ButtonLabelStyle.Generic }); // escolha explícita na v2 é mantida
        Assert.Equal(ButtonLabelStyle.Generic, store.Load().Settings.LabelStyle);
    }
}
