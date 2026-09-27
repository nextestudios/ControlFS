using System.Globalization;

namespace ControlFS.Core.Text;

/// <summary>
/// Concordância de número em pt-BR para textos da interface: 1 é singular; 0 e 2+ são plural ("0 itens", "1 item", "3 itens").
/// </summary>
public static class Plural
{
    /// <summary>"1 item", "3 itens", "1.234 itens" (número com separador de milhar da cultura atual).</summary>
    public static string Of(long count, string singular, string plural) =>
        $"{count.ToString("N0", CultureInfo.CurrentCulture)} {Word(count, singular, plural)}";

    /// <summary>Só a palavra, para concordar adjetivos e verbos: "marcado"/"marcados", "foi omitido"/"foram omitidos".</summary>
    public static string Word(long count, string singular, string plural) => count == 1 ? singular : plural;
}
