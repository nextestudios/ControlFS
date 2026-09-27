namespace ControlFS.Core.Actions;

/// <summary>
/// Ícone semântico de uma opção de menu, de um botão de diálogo ou do cabeçalho de um modal (#172). O AppController
/// escolhe o significado; a tela só desenha o símbolo de <see cref="ActionIcons.Glyph"/>. O ícone reforça o texto, nunca
/// o substitui.
/// </summary>
public enum ActionIcon
{
    None,

    // Abrir e navegar
    Open,
    OpenFolder,
    OpenExternal,
    OpenWith,
    Reveal,
    NewTab,
    CloseTab,
    Home,
    FolderUp,
    GoToPath,
    Back,
    Refresh,
    Choose,

    // Itens e locais (menus dinâmicos: locais, caminhos, recentes)
    Folder,
    File,
    Drive,
    Archive,
    Image,
    Text,
    Game,
    Favorite,
    Unfavorite,
    Recent,
    RecycleBin,
    ThisPc,

    // Operações de arquivo
    Rename,
    Copy,
    Cut,
    Paste,
    CopyTo,
    MoveTo,
    NewFolder,
    Compress,
    Extract,
    Delete,
    DeleteForever,
    Restore,
    Run,
    Test,
    Properties,
    Info,
    SelectAll,
    ClearSelection,
    MoveUp,
    MoveDown,

    // Busca e exibição
    Search,
    Filter,
    ClearFilter,
    Sort,
    SortOrder,
    Hidden,
    View,
    Density,
    DetailsPane,
    Subfolders,

    // Operações, histórico e desfazer
    Operations,
    Undo,
    Redo,
    Pause,
    Resume,
    Retry,
    Cancel,
    Close,
    Accept,
    Skip,
    KeepBoth,
    Replace,
    Merge,

    /// <summary>Apaga uma lista do ControlFS (recentes, histórico): destrutivo, mas não mexe em arquivos.</summary>
    Erase,

    /// <summary>Esvazia algo temporário (área de transferência): não destrói dados.</summary>
    Clear,

    // Controles e aplicativo
    Controller,
    ControllerTest,
    ControllerSetup,
    Labels,
    Import,
    Export,
    Update,
    Settings,
    Keyboard,
    Password,
    About,
    Exit,
    Menu,

    // Tons do cabeçalho dos diálogos
    Warning,
    Error,
    Success,
}

public static class ActionIcons
{
    /// <summary>
    /// Opções que apagam dados ou listas: aparecem em vermelho, com o símbolo de alerta, e nunca recebem o foco inicial
    /// de um menu ou diálogo (<see cref="IsDestructive"/> é a única fonte dessa regra).
    /// </summary>
    public static bool IsDestructive(ActionIcon icon) => icon is ActionIcon.Delete or ActionIcon.DeleteForever or ActionIcon.Erase;

    /// <summary>
    /// Símbolo na fonte de ícones do Windows (Segoe Fluent Icons, com Segoe MDL2 Assets como reserva): nunca emoji. Um
    /// único lugar para trocar o desenho de uma ação em todos os modais.
    /// </summary>
    public static string Glyph(ActionIcon icon) => icon switch
    {
        ActionIcon.None => string.Empty,
        ActionIcon.Open => "\uE8E5",
        ActionIcon.OpenFolder => "\uE838",
        ActionIcon.OpenExternal => "\uE8DA",
        ActionIcon.OpenWith => "\uE7AC",
        ActionIcon.Reveal => "\uED25",
        ActionIcon.NewTab => "\uE8A7",
        ActionIcon.CloseTab => "\uE711",
        ActionIcon.Home => "\uE80F",
        ActionIcon.FolderUp => "\uE74A",
        ActionIcon.GoToPath => "\uE8AD",
        ActionIcon.Back => "\uE72B",
        ActionIcon.Refresh => "\uE72C",
        ActionIcon.Choose => "\uE73E",
        ActionIcon.Folder => "\uE8B7",
        ActionIcon.File => "\uE8A5",
        ActionIcon.Drive => "\uEDA2",
        ActionIcon.Archive => "\uE7B8",
        ActionIcon.Image => "\uE8B9",
        ActionIcon.Text => "\uE8D2",
        ActionIcon.Game => "\uE7FC",
        ActionIcon.Favorite => "\uE734",
        ActionIcon.Unfavorite => "\uE8D9",
        ActionIcon.Recent => "\uE81C",
        ActionIcon.RecycleBin => "\uE74D",
        ActionIcon.ThisPc => "\uE977",
        ActionIcon.Rename => "\uE8AC",
        ActionIcon.Copy => "\uE8C8",
        ActionIcon.Cut => "\uE8C6",
        ActionIcon.Paste => "\uE77F",
        ActionIcon.CopyTo => "\uE8C8",
        ActionIcon.MoveTo => "\uE8DE",
        ActionIcon.NewFolder => "\uE8F4",
        ActionIcon.Compress => "\uE7B8",
        ActionIcon.Extract => "\uE896",
        ActionIcon.Delete => "\uE74D",
        ActionIcon.DeleteForever => "\uE74D",
        ActionIcon.Restore => "\uE7A7",
        ActionIcon.Run => "\uE768",
        ActionIcon.Test => "\uEA18",
        ActionIcon.Properties => "\uE946",
        ActionIcon.Info => "\uE946",
        ActionIcon.SelectAll => "\uE8B3",
        ActionIcon.ClearSelection => "\uE8E6",
        ActionIcon.MoveUp => "\uE70E",
        ActionIcon.MoveDown => "\uE70D",
        ActionIcon.Search => "\uE721",
        ActionIcon.Filter => "\uE71C",
        ActionIcon.ClearFilter => "\uE894",
        ActionIcon.Sort => "\uE8CB",
        ActionIcon.SortOrder => "\uE8AB",
        ActionIcon.Hidden => "\uE890",
        ActionIcon.View => "\uECA5",
        ActionIcon.Density => "\uE8FD",
        ActionIcon.DetailsPane => "\uE90D",
        ActionIcon.Subfolders => "\uE8B7",
        ActionIcon.Operations => "\uE895",
        ActionIcon.Undo => "\uE7A7",
        ActionIcon.Redo => "\uE7A6",
        ActionIcon.Pause => "\uE769",
        ActionIcon.Resume => "\uE768",
        ActionIcon.Retry => "\uE72C",
        ActionIcon.Cancel => "\uE711",
        ActionIcon.Close => "\uE711",
        ActionIcon.Accept => "\uE8FB",
        ActionIcon.Skip => "\uE893",
        ActionIcon.KeepBoth => "\uE8C8",
        ActionIcon.Replace => "\uE8AB",
        ActionIcon.Merge => "\uE8B7",
        ActionIcon.Erase => "\uE75C",
        ActionIcon.Clear => "\uE894",
        ActionIcon.Controller => "\uE7FC",
        ActionIcon.ControllerTest => "\uE9D9",
        ActionIcon.ControllerSetup => "\uE713",
        ActionIcon.Labels => "\uE8EC",
        ActionIcon.Import => "\uE8B5",
        ActionIcon.Export => "\uEDE1",
        ActionIcon.Update => "\uE777",
        ActionIcon.Settings => "\uE713",
        ActionIcon.Keyboard => "\uE765",
        ActionIcon.Password => "\uE72E",
        ActionIcon.About => "\uE946",
        ActionIcon.Exit => "\uE7E8",
        ActionIcon.Menu => "\uE700",
        ActionIcon.Warning => "\uE7BA",
        ActionIcon.Error => "\uE783",
        ActionIcon.Success => "\uE930",
        _ => throw new ArgumentOutOfRangeException(nameof(icon), icon, null),
    };
}
