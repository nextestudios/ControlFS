using ControlFS.Core.Policies;

namespace ControlFS.UnitTests.Core;

/// <summary>Tradutor único de erros do sistema: categoria legível em pt-BR, texto cru só nos detalhes técnicos e no log.</summary>
public class UserErrorsTests
{
    public static TheoryData<Exception, UserErrorKind> Cases() => new()
    {
        { new FileNotFoundException("Could not find file 'C:\\x.txt'."), UserErrorKind.NotFound },
        { new DirectoryNotFoundException("Could not find a part of the path."), UserErrorKind.NotFound },
        { new UnauthorizedAccessException("Access to the path is denied."), UserErrorKind.AccessDenied },
        { new IOException("The process cannot access the file because it is being used by another process.", unchecked((int)0x80070020)), UserErrorKind.InUse },
        { new IOException("There is not enough space on the disk.", unchecked((int)0x80070070)), UserErrorKind.DiskFull },
        { new PathTooLongException("The specified path is too long."), UserErrorKind.PathTooLong },
        { new IOException("The device is not ready.", unchecked((int)0x80070015)), UserErrorKind.DeviceNotReady },
        { new System.ComponentModel.Win32Exception(2), UserErrorKind.NotFound }, // processo/shell: código em NativeErrorCode
        { new InvalidOperationException("Operation is not valid due to the current state of the object."), UserErrorKind.Generic },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void System_errors_map_to_a_readable_category_with_a_suggestion_and_keep_the_raw_text_only_as_technical_detail(Exception ex, UserErrorKind kind)
    {
        var logged = new List<Exception>();
        UserErrors.Log = (e, _) => { lock (logged) logged.Add(e); };
        try
        {
            var error = UserErrors.Describe(ex, "teste");
            Assert.Equal(kind, error.Kind);
            Assert.DoesNotContain(ex.Message, error.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(ex.GetType().Name, error.Text, StringComparison.Ordinal);
            Assert.NotEmpty(error.Suggestion);
            Assert.Contains(ex.Message, error.Technical, StringComparison.Ordinal);
            lock (logged) Assert.Contains(ex, logged);
        }
        finally
        {
            UserErrors.Log = null;
        }
    }
}
