namespace ControlFS.Infrastructure.Archives.Creation;

/// <summary>
/// O executável de linha de comando <c>Rar.exe</c> que o próprio usuário instalou com o WinRAR. O ControlFS nunca traz,
/// baixa nem reimplementa um gravador de RAR (ver docs/decisions/0011). <paramref name="LeadingArguments"/> existe só para
/// os testes, que usam um executável falso lançado por <c>dotnet</c>; o localizador de verdade nunca preenche.
/// </summary>
public sealed record RarTool(string Path, IReadOnlyList<string>? LeadingArguments = null);
