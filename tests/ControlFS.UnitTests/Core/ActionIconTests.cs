using ControlFS.Core.Actions;

namespace ControlFS.UnitTests.Core;

public class ActionIconTests
{
    [Fact]
    public void Each_meaning_has_its_own_glyph_except_the_documented_shared_ones()
    {
        // Auditoria de UX: Copiar = Copiar para = Manter ambos, Configurações = Controles sem perfil, Informações =
        // Propriedades = Sobre, cinco significados com o X… Só os grupos de ActionIcons.SharedGlyphs podem repetir.
        var shared = ActionIcons.SharedGlyphs.SelectMany(g => g.Select(i => (Icon: i, Group: g))).ToDictionary(p => p.Icon, p => p.Group);
        Assert.All(ActionIcons.SharedGlyphs, g => Assert.Single(g.Select(ActionIcons.Glyph).Distinct()));

        var collisions = Enum.GetValues<ActionIcon>().Where(i => i != ActionIcon.None)
            .GroupBy(ActionIcons.Glyph)
            .Where(g => g.Count() > 1)
            .Where(g => !(shared.TryGetValue(g.First(), out var group) && g.All(group.Contains)))
            .Select(g => string.Join(" = ", g))
            .ToList();
        Assert.Empty(collisions);
    }
}
