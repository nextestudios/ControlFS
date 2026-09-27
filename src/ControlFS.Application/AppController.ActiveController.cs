using System.Globalization;
using ControlFS.Application.State;
using ControlFS.Core.Contracts;
using ControlFS.Core.Input;

namespace ControlFS.Application;

/// <summary>
/// Menu → Controle ativo (#80): escolher explicitamente o controle que comanda a UI e avisar quando um remapeador
/// (Steam Input, DS4Windows) expõe o controle físico e uma cópia virtual ao mesmo tempo. A escolha vale para a sessão.
/// </summary>
public sealed partial class AppController
{
    private readonly HashSet<string> _duplicateNoticeShown = new(StringComparer.Ordinal);
    private bool _activeLocked;

    /// <summary>Rótulo do item no menu principal.</summary>
    private string ActiveControllerMenuLabel =>
        _diagnostics is { IsActiveDeviceLocked: true } d && d.Devices.FirstOrDefault(x => x.SessionKey == d.ActiveDeviceKey) is { } active
            ? $"Controle ativo: {active.Name}…"
            : "Controle ativo: automático…";

    internal void ShowActiveControllerMenu()
    {
        if (_diagnostics is not { } diagnostics) return;
        var connected = diagnostics.Devices.ToList();
        var duplicates = ControllerDuplicates.Find(connected);
        var items = new List<MenuItem>
        {
            new("Automático" + (diagnostics.IsActiveDeviceLocked ? string.Empty : " (atual)"), () => SelectActiveController(null),
                Detail: "Qualquer controle assume ao apertar um botão (exceto em confirmações sensíveis)."),
        };
        var focus = 0;
        foreach (var device in connected)
        {
            var isActive = string.Equals(device.SessionKey, diagnostics.ActiveDeviceKey, StringComparison.Ordinal);
            if (isActive && diagnostics.IsActiveDeviceLocked) focus = items.Count;
            var canDrive = device.IsGamepad || ProfileFor(device) is not null;
            items.Add(new MenuItem(device.Name + (isActive ? (diagnostics.IsActiveDeviceLocked ? " (escolhido)" : " (em uso)") : string.Empty),
                () => SelectActiveController(device),
                canDrive ? null : "Joystick sem perfil: configure-o em Menu → Controles sem perfil primeiro.",
                Detail: DescribeForMenu(device, connected, duplicates)));
        }
        if (connected.Count == 0)
            items.Add(new MenuItem("Nenhum controle conectado", null, "Conecte um controle; o teclado continua funcionando."));
        PushModal(new MenuModal("Controle ativo", items) { FocusIndex = focus });
    }

    private void SelectActiveController(InputDeviceInfo? device)
    {
        if (_diagnostics is null) return;
        _diagnostics.SelectActiveDevice(device?.SessionKey);
        _activeLocked = _diagnostics.IsActiveDeviceLocked;
        if (device is not null && !string.Equals(_diagnostics.ActiveDeviceKey, device.SessionKey, StringComparison.Ordinal))
        {
            SetStatus("O controle escolhido foi desconectado.");
            return;
        }
        if (device is not null) SetActiveController(device.IsGamepad ? device.Family : ControllerFamily.Generic);
        SetStatus(device is null
            ? "Controle ativo automático: qualquer controle assume ao apertar um botão."
            : $"Controle ativo: {device.Name}. Só ele comanda o ControlFS até você voltar ao automático.");
    }

    /// <summary>A camada de entrada avisa quando um controle conecta ou desconecta.</summary>
    public void OnControllersChanged()
    {
        if (_diagnostics is null) return;
        if (_activeLocked && !_diagnostics.IsActiveDeviceLocked)
        {
            _activeLocked = false;
            SetStatus("O controle escolhido foi desconectado; qualquer controle volta a assumir.");
            return;
        }
        if (_diagnostics.IsActiveDeviceLocked) return; // já escolheu: sem aviso
        foreach (var duplicate in ControllerDuplicates.Find(_diagnostics.Devices.ToList()))
        {
            if (!_duplicateNoticeShown.Add(duplicate.Virtual.SessionKey)) continue;
            SetStatus($"\"{duplicate.Virtual.Name}\" parece uma cópia virtual ({SourceName(duplicate.Source)}) de outro controle: "
                + "cada botão pode agir duas vezes. Escolha um em Menu → Controle ativo.");
            return;
        }
    }

    private static string DescribeForMenu(InputDeviceInfo device, IReadOnlyCollection<InputDeviceInfo> connected, IReadOnlyList<ControllerDuplicate> duplicates)
    {
        var parts = new List<string>
        {
            device.IsGamepad ? $"{FamilyName(device.Family)} · {device.TypeName}" : "joystick cru",
            string.Create(CultureInfo.InvariantCulture, $"VID:PID {device.VendorId:X4}:{device.ProductId:X4}"),
        };
        var source = ControllerDuplicates.SourceOf(device, connected);
        parts.Add(source == VirtualSource.None ? "físico" : $"virtual ({SourceName(source)})");
        var detail = string.Join(" · ", parts);
        if (duplicates.FirstOrDefault(d => ReferenceEquals(d.Virtual, device)) is { } duplicate)
            detail += $". Pode repetir {string.Join(", ", duplicate.Others.Select(o => $"\"{o.Name}\""))}: escolha só um.";
        return detail;
    }

    internal static string SourceName(VirtualSource source) => source switch
    {
        VirtualSource.Sdl => "SDL",
        VirtualSource.SteamInput => "Steam Input",
        VirtualSource.LikelyEmulatedXbox360 => "provável DS4Windows/ViGEm",
        _ => "física",
    };
}
