using ControlFS.Core.Actions;
using ControlFS.Core.Input;

namespace ControlFS.Core.Models;

/// <summary>
/// Preferências por dispositivo, identificadas por StableId (GUID SDL, vendor, product) e nunca
/// por identificadores efêmeros da sessão. Perfis contêm apenas dados: nenhum código ou caminho.
/// </summary>
public sealed record DeviceProfile(
    int SchemaVersion,
    string StableId,
    string DisplayName,
    ConfirmBackConvention Convention,
    ButtonLabelStyle LabelStyle,
    InputSettings Input);
