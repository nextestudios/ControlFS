using System.Globalization;
using System.Text;
using ControlFS.Core.Text;

namespace ControlFS.Application;

/// <summary>
/// Sugestões do teclado virtual (#45): só fontes locais (textos digitados antes, nomes da pasta atual, pastas
/// recentes e favoritas para caminhos). Nada sai do PC; campos de senha nunca mostram nem guardam sugestões.
/// </summary>
public sealed partial class AppController
{
    /// <summary>Quantos textos digitados ficam guardados para sugerir (os mais recentes primeiro).</summary>
    internal const int MaxTypedHistory = 30;

    private void AttachSuggestions(VirtualKeyboard keyboard)
    {
        if (!Settings.KeyboardSuggestions || keyboard.Kind == TextFieldKind.Password) return;
        var kind = keyboard.Kind;
        var names = kind == TextFieldKind.Path ? [] : ActivePane.List.Items.Select(e => e.Name).ToList();
        keyboard.SuggestionSource = text => Suggest(kind, text, names);
    }

    private List<string> Suggest(TextFieldKind kind, string text, IReadOnlyList<string> folderNames)
    {
        IEnumerable<string> candidates = kind == TextFieldKind.Path
            ? Settings.Favorites.Concat(Settings.RecentFolders)
            : Settings.TypedTexts.Concat(folderNames);
        var key = Fold(text);
        return [.. candidates.Where(c => c.Length > 0 && Fold(c).StartsWith(key, StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(VirtualKeyboard.MaxSuggestions + 1)];
    }

    /// <summary>Guarda o texto concluído num campo de nome ou busca (nunca senhas nem caminhos).</summary>
    private void RememberTypedText(VirtualKeyboard keyboard)
    {
        if (!Settings.KeyboardSuggestions || keyboard.Kind is TextFieldKind.Password or TextFieldKind.Path) return;
        var text = keyboard.Text.Trim();
        if (text.Length == 0) return;
        UpdateSettings(s => s with
        {
            TypedTexts = [.. s.TypedTexts.Where(t => !string.Equals(t, text, StringComparison.OrdinalIgnoreCase)).Prepend(text).Take(MaxTypedHistory)],
        });
    }

    internal void ToggleKeyboardSuggestions()
    {
        var on = !Settings.KeyboardSuggestions;
        // Desligar também apaga os textos guardados: nada fica registrado.
        UpdateSettings(s => on ? s with { KeyboardSuggestions = true } : s with { KeyboardSuggestions = false, TypedTexts = [] });
        StatusMessage = on ? "Sugestões do teclado ligadas." : "Sugestões do teclado desligadas e apagadas.";
    }

    /// <summary>Minúsculas sem acentos, para "relat" achar "Relatório".</summary>
    private static string Fold(string text)
    {
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) builder.Append(c);
        return builder.ToString();
    }
}
