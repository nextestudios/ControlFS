using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Input;
using ControlFS.Core.Input.Mapping;

namespace ControlFS.Application;

/// <summary>
/// Tela "Teste de controles" (#78): o mantenedor roda a matriz de hardware no Windows sem o SDK do .NET. Mostra os
/// dispositivos que a camada de entrada vê e, a cada pressão, o controle físico e a ação semântica produzida; o
/// relatório copiado leva só dados do controle e do sistema (nada de caminhos, GUIDs, números de série ou nomes de usuário).
/// </summary>
public sealed partial class AppController
{
    /// <summary>Segurar Confirmar (copiar relatório) ou Voltar (sair) por este tempo age na tela de teste.</summary>
    public static readonly TimeSpan HoldInControllerTest = TimeSpan.FromSeconds(1);

    private IControllerDiagnostics? _diagnostics;

    public void AttachControllerDiagnostics(IControllerDiagnostics diagnostics) => _diagnostics = diagnostics;

    /// <summary>Copia texto para a área de transferência do sistema (fornecido pela janela; false se falhou).</summary>
    public Func<string, bool>? CopyText { get; set; }

    public ControllerTestModal? ControllerTest => TopModal as ControllerTestModal;

    public bool IsTestingControllers => TopModal is ControllerTestModal;

    internal void ShowControllerTest() => PushModal(new ControllerTestModal());

    /// <summary>Dispositivos conectados e os que já passaram pela tela nesta sessão (numeração estável).</summary>
    public IReadOnlyList<ControllerTestDevice> ControllerTestDevices(ControllerTestModal modal)
    {
        var connected = _diagnostics?.Devices ?? [];
        foreach (var device in connected) Number(modal, device);
        var keys = connected.Select(d => d.SessionKey).ToHashSet(StringComparer.Ordinal);
        return [.. modal.Seen.Values.OrderBy(s => s.Number).Select(s => new ControllerTestDevice(
            s.Number, s.Info, keys.Contains(s.Info.SessionKey), string.Equals(_diagnostics?.ActiveDeviceKey, s.Info.SessionKey, StringComparison.Ordinal),
            s.Info.IsGamepad ? null : ProfileFor(s.Info)?.Name, ControllerDuplicates.SourceOf(s.Info, connected)))];
    }

    private static int Number(ControllerTestModal modal, InputDeviceInfo device)
    {
        if (modal.Seen.TryGetValue(device.SessionKey, out var seen)) return seen.Number;
        var number = modal.Seen.Count + 1;
        modal.Seen[device.SessionKey] = (number, device);
        return number;
    }

    /// <summary>
    /// Chamado pela camada de entrada antes de rotear um controle físico na tela de teste; <see cref="EndTestInput"/>
    /// depois. A ação que o roteador emitir entre os dois é associada a esta pressão.
    /// </summary>
    public void BeginTestInput(InputDeviceInfo device, PhysicalControl control, bool pressed, RawInputEvent? raw = null)
    {
        if (ControllerTest is not { } modal) return;
        if (!pressed)
        {
            if (modal.Hold is { } hold && hold.Device == device.SessionKey && hold.Control == control) modal.Hold = null;
            modal.Pending = null;
            return;
        }
        modal.Pending = new PendingTestInput(device.SessionKey, Number(modal, device), control, raw, device.Family);
    }

    public void EndTestInput()
    {
        if (ControllerTest is not { } modal || modal.Pending is not { } pending) return;
        modal.Pending = null;
        if (pending.Handled) return;
        AddTestLine(modal, new(pending.Device, pending.Control, pending.Raw, null, pending.Family)); // nenhuma ação (ex.: outro controle ativo)
        RaiseChanged();
    }

    /// <summary>Joystick cru sem perfil na tela de teste: registra botões, hats e eixos (sem ruído de eixo).</summary>
    public void RecordTestRaw(InputDeviceInfo device, RawInputEvent input)
    {
        if (ControllerTest is not { } modal) return;
        switch (input.Kind)
        {
            case RawInputKind.Button when input.Value < 0.5:
                return;
            case RawInputKind.Hat when (int)input.Value == HatDirections.Centered:
                return;
            case RawInputKind.Axis:
                var bucket = input.Value >= 0.6 ? 1 : input.Value <= -0.6 ? -1 : 0;
                var key = (device.SessionKey, input.Index);
                var previous = modal.AxisBuckets.GetValueOrDefault(key);
                modal.AxisBuckets[key] = bucket;
                if (bucket == 0 || bucket == previous) return;
                break;
        }
        AddTestLine(modal, new(Number(modal, device), null, input, null, ControllerFamily.Generic));
        RaiseChanged();
    }

    private void HandleControllerTest(ControllerTestModal modal, InputAction action)
    {
        if (modal.Pending is { Handled: false } pending)
        {
            pending.Handled = true;
            AddTestLine(modal, new(pending.Device, pending.Control, pending.Raw, action, pending.Family));
            if (action is InputAction.Confirm or InputAction.Back) modal.Hold = (pending.DeviceKey, pending.Control, action, Clock());
            return;
        }
        // Teclado ou mouse: agem na hora.
        if (action == InputAction.Back) CloseModal(modal);
        else if (action == InputAction.Confirm) CopyControllerReport();
    }

    private static void AddTestLine(ControllerTestModal modal, ControllerTestLine line)
    {
        modal.Notice = null;
        if (modal.Lines.Count >= ControllerTestModal.MaxLines) modal.Lines.RemoveAt(0);
        modal.Lines.Add(line);
    }

    /// <summary>Laço de entrada: Confirmar/Voltar mantidos por <see cref="HoldInControllerTest"/>.</summary>
    private bool TickControllerTest()
    {
        if (ControllerTest is not { Hold: { } hold } modal || Clock() - hold.Since < HoldInControllerTest) return false;
        modal.Hold = null;
        if (hold.Action == InputAction.Back)
        {
            CloseModal(modal);
            StatusMessage = "Teste de controles encerrado.";
        }
        else
        {
            CopyControllerReport();
        }
        RaiseChanged();
        return true;
    }

    public void CopyControllerReport()
    {
        if (ControllerTest is not { } modal) return;
        var text = BuildControllerReport(modal);
        modal.Notice = CopyText?.Invoke(text) == true
            ? "Relatório copiado. Cole num comentário da issue #78 no GitHub."
            : "Não foi possível copiar o relatório para a área de transferência.";
        RaiseChanged();
    }

    /// <summary>Texto em inglês (vai para a issue). Só dados do controle e do sistema: sem caminhos, GUIDs nem nomes de usuário.</summary>
    public string BuildControllerReport(ControllerTestModal modal)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("ControlFS controller test report");
        sb.AppendLine(inv, $"App: {AppVersion}");
        sb.AppendLine(inv, $"Windows: {RuntimeInformation.OSDescription.Trim()} ({RuntimeInformation.OSArchitecture})");
        sb.AppendLine(inv, $"Input: {_diagnostics?.BackendDescription ?? "unavailable"}");
        sb.AppendLine(inv, $"Confirm button: {(Settings.Convention == ConfirmBackConvention.SouthConfirms ? "South" : "East")} · Labels: {Settings.LabelStyle}");
        sb.AppendLine();
        var devices = ControllerTestDevices(modal);
        sb.AppendLine(inv, $"Devices ({devices.Count}):");
        if (devices.Count == 0) sb.AppendLine("  none detected");
        foreach (var d in devices) sb.AppendLine(inv, $"  {d.Number}. {DescribeDevice(d)}");
        sb.AppendLine();
        sb.AppendLine(inv, $"Results ({modal.Lines.Count} presses):");
        foreach (var line in modal.Lines)
            sb.AppendLine(inv, $"  #{line.Device} {DescribeInput(line, english: true)} -> {line.Action?.ToString() ?? "(no action)"}");
        return sb.ToString();
    }

    /// <summary>Linha do dispositivo (tela e relatório): nome, tipo do SDL, família, VID:PID, gamepad ou joystick cru.</summary>
    public static string DescribeDevice(ControllerTestDevice d)
    {
        var info = d.Info;
        var kind = info.IsGamepad ? "gamepad" : d.Profile is { } profile ? $"raw joystick, profile \"{profile}\"" : "raw joystick, no profile";
        return string.Create(CultureInfo.InvariantCulture,
            $"{info.Name} | SDL type: {(info.IsGamepad ? info.TypeName : "-")} | family: {info.Family} | VID:PID {info.VendorId:X4}:{info.ProductId:X4} | {kind}")
            + (d.Source switch
            {
                VirtualSource.None => string.Empty,
                VirtualSource.SteamInput => " | virtual (Steam Input)",
                VirtualSource.LikelyEmulatedXbox360 => " | likely virtual (DS4Windows/ViGEm)",
                _ => " | virtual",
            })
            + (d.IsActive ? " | ACTIVE" : string.Empty)
            + (d.IsConnected ? string.Empty : " | disconnected");
    }

    /// <summary>Entrada da linha: controle físico (com o nome na família) e/ou a entrada crua que o perfil traduziu.</summary>
    public static string DescribeInput(ControllerTestLine line, bool english)
    {
        var parts = new List<string>(2);
        if (line.Raw is { } raw) parts.Add(DescribeRaw(raw, english));
        if (line.Control is { } control)
            parts.Add(english ? control.ToString() : $"{control} ({ControllerButtons.SpokenName(ControllerButtons.From(control), line.Family)})");
        return string.Join(" = ", parts);
    }

    private static string DescribeRaw(RawInputEvent raw, bool english)
    {
        var n = raw.Index + 1;
        return raw.Kind switch
        {
            RawInputKind.Button => english ? $"button {n}" : $"botão {n}",
            RawInputKind.Hat => (english ? $"hat {n} " : $"direcional {n} ") + ((int)raw.Value switch
            {
                HatDirections.Up => "↑",
                HatDirections.Down => "↓",
                HatDirections.Left => "←",
                HatDirections.Right => "→",
                var mask => string.Create(CultureInfo.InvariantCulture, $"0x{mask:X}"),
            }),
            _ => (english ? $"axis {n} " : $"eixo {n} ") + (raw.Value < 0 ? "−" : "+"),
        };
    }
}
