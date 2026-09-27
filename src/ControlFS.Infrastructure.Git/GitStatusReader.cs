using ControlFS.Core.Contracts;
using LibGit2Sharp;

namespace ControlFS.Infrastructure.Git;

/// <summary>
/// Status pela libgit2: procura o .git subindo a árvore, lê só a parte da pasta mostrada e resume por item da pasta. A
/// libgit2 não executa nada do repositório (fsmonitor, filtros externos, hooks), então abrir uma pasta de um repositório
/// desconhecido não roda código dele. Repositórios que a libgit2 recusa (dono diferente, corrompidos): sem status.
/// </summary>
public sealed class GitStatusReader : IGitStatusReader
{
    public GitFolderStatus? Read(string folder)
    {
        try
        {
            var full = Path.GetFullPath(folder);
            if (Repository.Discover(full) is not { } gitDir) return null;
            using var repo = new Repository(gitDir);
            if (repo.Info.IsBare || repo.Info.WorkingDirectory is not { } workdir) return null;
            var relative = Path.GetRelativePath(workdir, full).Replace('\\', '/');
            if (relative == ".") relative = string.Empty;
            // Dentro do próprio .git (ou fora da árvore de trabalho): nada a mostrar.
            if (relative.StartsWith("..", StringComparison.Ordinal) || relative == ".git" || relative.StartsWith(".git/", StringComparison.OrdinalIgnoreCase)) return null;
            var prefix = relative.Length == 0 ? string.Empty : relative + "/";
            var options = new StatusOptions
            {
                IncludeUntracked = true,
                RecurseUntrackedDirs = false,
                IncludeIgnored = false,
                ExcludeSubmodules = true,
                DetectRenamesInIndex = false,
                DetectRenamesInWorkDir = false,
                DisablePathSpecMatch = true,
                PathSpec = relative.Length == 0 ? null : [relative],
            };
            var children = new Dictionary<string, GitChange>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in repo.RetrieveStatus(options))
            {
                if (Map(entry.State) is not { } change) continue;
                var path = entry.FilePath.Replace('\\', '/');
                if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                var rest = path[prefix.Length..].TrimEnd('/');
                if (rest.Length == 0) continue;
                var child = rest.Split('/')[0];
                // Numa subpasta, o que há dentro dela aparece como "modificado" (ou conflito), nunca como apagado.
                if (rest.Contains('/') && change is GitChange.Deleted or GitChange.Renamed or GitChange.Added) change = GitChange.Modified;
                if (!children.TryGetValue(child, out var current) || change < current) children[child] = change;
            }
            var branch = repo.Info.IsHeadDetached ? "HEAD desanexado" : repo.Head.FriendlyName;
            return new GitFolderStatus(Path.TrimEndingDirectorySeparator(workdir), branch, children);
        }
        catch (Exception ex) when (ex is LibGit2SharpException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static GitChange? Map(FileStatus state)
    {
        if (state.HasFlag(FileStatus.Conflicted)) return GitChange.Conflicted;
        if (state.HasFlag(FileStatus.Ignored) || state == FileStatus.Unaltered || state == FileStatus.Nonexistent) return null;
        if (state.HasFlag(FileStatus.ModifiedInWorkdir) || state.HasFlag(FileStatus.ModifiedInIndex) ||
            state.HasFlag(FileStatus.TypeChangeInWorkdir) || state.HasFlag(FileStatus.TypeChangeInIndex)) return GitChange.Modified;
        if (state.HasFlag(FileStatus.RenamedInIndex) || state.HasFlag(FileStatus.RenamedInWorkdir)) return GitChange.Renamed;
        if (state.HasFlag(FileStatus.NewInIndex)) return GitChange.Added;
        if (state.HasFlag(FileStatus.DeletedFromIndex) || state.HasFlag(FileStatus.DeletedFromWorkdir)) return GitChange.Deleted;
        if (state.HasFlag(FileStatus.NewInWorkdir)) return GitChange.Untracked;
        return null;
    }
}
