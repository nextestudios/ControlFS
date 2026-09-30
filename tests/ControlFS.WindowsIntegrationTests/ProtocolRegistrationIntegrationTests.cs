using ControlFS.Infrastructure.Windows.Shell;
using Microsoft.Win32;
using Xunit;

namespace ControlFS.WindowsIntegrationTests;

public class ProtocolRegistrationIntegrationTests
{
    private const string ProtocolKey = @"Software\Classes\controlfs";

    [Fact]
    public void EnsureRegistered_registers_scheme_in_current_user_registry()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Requer Windows.");

        var messages = new List<string>();
        ProtocolRegistration.EnsureRegistered(messages.Add);

        using var key = Registry.CurrentUser.OpenSubKey(ProtocolKey);
        Assert.NotNull(key);
        Assert.Equal("URL:ControlFS", key.GetValue(null));
        Assert.Equal(string.Empty, key.GetValue("URL Protocol"));

        using var cmdKey = Registry.CurrentUser.OpenSubKey(ProtocolKey + @"\shell\open\command");
        Assert.NotNull(cmdKey);
        var command = cmdKey.GetValue(null) as string;
        Assert.NotNull(command);
        Assert.Contains(Environment.ProcessPath!, command, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("\"%1\"", command, StringComparison.OrdinalIgnoreCase);

        Assert.True(ProtocolRegistration.IsRegistered());
    }
}
