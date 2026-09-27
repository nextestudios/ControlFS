using System.Text;
using ControlFS.Core.Actions;
using ControlFS.Core.Input.Mapping;

namespace ControlFS.UnitTests.Input;

/// <summary>Perfis importados são uma fronteira de segurança: só dados conhecidos, com tamanho e faixas limitados.</summary>
public class ControllerProfileSerializerTests
{
    private static readonly string Valid = ValidText.ReplaceLineEndings("\n");

    private const string ValidText = """
        {
          "format": "controlfs-controller-profile",
          "schemaVersion": 1,
          "name": "Generic USB Joystick",
          "match": { "guid": "03000000790000000600000000000000", "vendorId": 121, "productId": 6 },
          "axes": [ { "index": 1, "neutral": -1, "deadzone": 0.25 } ],
          "bindings": [
            { "control": "DPadUp", "kind": "Hat", "index": 0, "direction": 1 },
            { "control": "DPadDown", "kind": "Hat", "index": 0, "direction": 4 },
            { "control": "DPadLeft", "kind": "Hat", "index": 0, "direction": 8 },
            { "control": "DPadRight", "kind": "Hat", "index": 0, "direction": 2 },
            { "control": "South", "kind": "Button", "index": 0, "direction": 0 },
            { "control": "East", "kind": "Button", "index": 1, "direction": 0 },
            { "control": "RightTrigger", "kind": "Axis", "index": 1, "direction": 1 }
          ]
        }
        """;

    [Fact]
    public void Round_trips_a_valid_profile()
    {
        var profile = ControllerProfileSerializer.Parse(Encoding.UTF8.GetBytes(Valid));
        var again = ControllerProfileSerializer.Parse(ControllerProfileSerializer.Serialize(profile));

        Assert.Equal("Generic USB Joystick", again.Name);
        Assert.Equal(profile.Match, again.Match);
        Assert.Equal(profile.Bindings.OrderBy(b => b.Key), again.Bindings.OrderBy(b => b.Key));
        Assert.Equal(new AxisCalibration(1, -1, 0.25), Assert.Single(again.Axes));
    }

    [Theory]
    [InlineData("{ \"format\": ", "malformado")]
    [InlineData("[1, 2, 3]", "perfil fora")]
    [InlineData("\"command\": \"calc.exe\",", "campo desconhecido")] // nada executável ou extra entra
    [InlineData("\"name\": \"Outro\",", "campo repetido")]
    [InlineData("SCHEMA:2", "versão mais nova")]
    [InlineData("FORMAT:other-app", "Não é um perfil")]
    [InlineData("DROP:East", "incompleto")]
    [InlineData("SWAP:\"control\": \"DPadRight\", \"kind\": \"Hat\", \"index\": 0, \"direction\": 2|\"control\": \"DPadRight\", \"kind\": \"Hat\", \"index\": 0, \"direction\": 1", "mesma entrada")]
    [InlineData("SWAP:\"control\": \"RightTrigger\"|\"control\": \"8\"", "controle")] // enum numérico não passa
    [InlineData("SWAP:\"direction\": 8 }|\"direction\": 9 }", "direção")] // hat diagonal
    [InlineData("SWAP:\"index\": 1, \"direction\": 0|\"index\": 999, \"direction\": 0", "índice")]
    [InlineData("SWAP:\"deadzone\": 0.25|\"deadzone\": 5", "zona morta")]
    [InlineData("SWAP:\"guid\": \"03000000790000000600000000000000\"|\"guid\": \"..\\\\..\\\\x\"", "GUID")]
    [InlineData("SWAP:\"axes\": [ { \"index\": 1, \"neutral\": -1, \"deadzone\": 0.25 } ]|\"axes\": [[[[[[1]]]]]]", "malformado")] // profundidade limitada
    public void Rejects_malformed_or_unexpected_profiles(string change, string expected)
    {
        var json = Apply(change);
        var ex = Assert.Throws<ControllerProfileException>(() => ControllerProfileSerializer.Parse(Encoding.UTF8.GetBytes(json)));
        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Stops_reading_an_oversized_stream_at_the_limit()
    {
        using var endless = new EndlessStream();
        var ex = Assert.Throws<ControllerProfileException>(() => ControllerProfileSerializer.Read(endless));
        Assert.Contains("grande demais", ex.Message, StringComparison.Ordinal);
        Assert.True(endless.BytesRead <= ControllerProfileSerializer.MaxBytes + 1);
    }

    [Fact]
    public void Serialize_refuses_a_profile_it_could_not_read_back()
    {
        var incomplete = new ControllerProfile("Pad", new ControllerMatch("", 0x0079, 0x0006),
            new Dictionary<PhysicalControl, RawBinding> { [PhysicalControl.South] = new(RawInputKind.Button, 0, 0) }, []);
        Assert.Throws<ControllerProfileException>(() => ControllerProfileSerializer.Serialize(incomplete));
    }

    private static string Apply(string change)
    {
        if (change.StartsWith("SCHEMA:", StringComparison.Ordinal)) return Valid.Replace("\"schemaVersion\": 1", $"\"schemaVersion\": {change[7..]}", StringComparison.Ordinal);
        if (change.StartsWith("FORMAT:", StringComparison.Ordinal)) return Valid.Replace("controlfs-controller-profile", change[7..], StringComparison.Ordinal);
        if (change.StartsWith("DROP:", StringComparison.Ordinal))
        {
            var lines = Valid.Split('\n').Where(l => !l.Contains($"\"control\": \"{change[5..]}\"", StringComparison.Ordinal));
            return string.Join('\n', lines);
        }
        if (change.StartsWith("SWAP:", StringComparison.Ordinal))
        {
            var parts = change[5..].Split('|');
            Assert.Contains(parts[0], Valid, StringComparison.Ordinal);
            return Valid.Replace(parts[0], parts[1], StringComparison.Ordinal);
        }
        if (change.StartsWith('{') || change.StartsWith('[')) return change;
        return Valid.Replace("{\n  \"format\"", "{\n  " + change + "\n  \"format\"", StringComparison.Ordinal);
    }

    private sealed class EndlessStream : Stream
    {
        public long BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => BytesRead; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            buffer.AsSpan(offset, count).Fill((byte)' ');
            BytesRead += count;
            return count;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
