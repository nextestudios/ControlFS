namespace ControlFS.Core.Contracts;

/// <summary>
/// Abrir arquivos com os programas do Windows. Isso sai da experiência controlada pelo app: o outro programa pode não
/// funcionar com o controle. Toda chamada corresponde a uma ação explícita do usuário.
/// </summary>
public interface IShellService
{
    /// <summary>Abre com o programa padrão (associação do Windows).</summary>
    /// <exception cref="ShellException"/>
    void Open(string path);

    /// <summary>Mostra a caixa "Abrir com" do Windows.</summary>
    /// <exception cref="ShellException"/>
    void OpenWith(string path);

    /// <summary>Abre o Explorador de Arquivos com o item selecionado.</summary>
    /// <exception cref="ShellException"/>
    void RevealInExplorer(string path);

    /// <summary>
    /// Abre um endereço https no navegador padrão. Só https, sem usuário/senha; quem chama passa endereços fixos do app,
    /// nunca texto de arquivos ou da rede.
    /// </summary>
    /// <exception cref="ShellException"/>
    void OpenLink(Uri url) => throw new ShellException("Não disponível nesta compilação.");
}

public sealed class ShellException(string message, Exception? inner = null) : Exception(message, inner);
