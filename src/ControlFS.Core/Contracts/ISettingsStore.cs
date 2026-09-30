using ControlFS.Core.Actions;

namespace ControlFS.Core.Contracts;

/// <summary>Densidade da lista: confortável (duas linhas, para TV) ou compacta (uma linha com colunas).</summary>
public enum ListDensity
{
    Comfortable,
    Compact,
}

/// <summary>Forma de exibir pastas e locais: lista (linhas) ou grade (blocos com ícone grande, navegação em 2D).</summary>
public enum ViewMode
{
    List,
    Grid,
}

public sealed record AppSettings
{
    /// <summary>2: <see cref="ButtonLabelStyle.Automatic"/> passou a ser o padrão (antes era Generic).</summary>
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public ConfirmBackConvention Convention { get; init; } = ConfirmBackConvention.SouthConfirms;
    public ButtonLabelStyle LabelStyle { get; init; } = ButtonLabelStyle.Automatic;
    public bool ShowHidden { get; init; }
    public bool ReducedMotion { get; init; }
    public ListDensity Density { get; init; } = ListDensity.Comfortable;

    /// <summary>Lista ou grade, para todas as pastas (a densidade vale para as duas: grade compacta tem blocos menores).</summary>
    public ViewMode View { get; init; } = ViewMode.List;

    /// <summary>Tema (#37): automático segue o modo de apps do Windows; claro ou escuro fixos.</summary>
    public Appearance.ThemeMode Theme { get; init; } = Appearance.ThemeMode.System;

    /// <summary>Cor de destaque (#37): foco, cursor e símbolos em destaque. Só predefinições conferidas em contraste.</summary>
    public Appearance.AccentColor Accent { get; init; } = Appearance.AccentColor.Cyan;

    /// <summary>
    /// Painel de detalhes na lista e na grade, cada exibição com a sua escolha (Configurações → Painel de
    /// detalhes). Null = automático: à mostra onde cabe sem apertar o conteúdo, escondido em portáteis e janelas estreitas.
    /// </summary>
    public bool? ListDetails { get; init; }

    /// <inheritdoc cref="ListDetails"/>
    public bool? GridDetails { get; init; }
    public string? LastLocation { get; init; }
    public IReadOnlyList<string> Favorites { get; init; } = [];

    /// <summary>Lembra pastas e arquivos abertos recentemente (somente neste computador). Desligar apaga as listas.</summary>
    public bool RememberRecents { get; init; } = true;

    /// <summary>Pastas visitadas recentemente, a mais recente primeiro (lista limitada).</summary>
    public IReadOnlyList<string> RecentFolders { get; init; } = [];

    /// <summary>Arquivos e compactados abertos recentemente, o mais recente primeiro (lista limitada).</summary>
    public IReadOnlyList<string> RecentFiles { get; init; } = [];

    /// <summary>Reabre, ao iniciar, as abas deixadas abertas (2 ou mais). Desligar apaga <see cref="OpenTabs"/>.</summary>
    public bool RestoreTabs { get; init; } = true;

    /// <summary>Pasta do disco de cada aba aberta, na ordem da faixa (vazio com uma aba só).</summary>
    public IReadOnlyList<string> OpenTabs { get; init; } = [];

    /// <summary>Índice, em <see cref="OpenTabs"/>, da aba que estava ativa.</summary>
    public int ActiveOpenTab { get; init; }

    /// <summary>
    /// Tela cheia (#230): F11, Menu → Tela cheia ou o botão ao lado de minimizar. Fica salva e vale na próxima abertura.
    /// O vídeo põe a janela em tela cheia enquanto toca sem mudar esta escolha.
    /// </summary>
    public bool FullScreen { get; init; }

    /// <summary>Dois painéis lado a lado (#56) onde a tela comporta; portáteis e janelas estreitas mostram um.</summary>
    public bool DualPane { get; init; }

    /// <summary>
    /// Fluidez máxima: com a janela ativa, o controle é lido a cada 8 ms (~125 vezes por segundo, o bastante para telas de
    /// 120 Hz), com o relógio do Windows em 1 ms. Desligado (economia): o temporizador comum (~64 vezes por segundo), que
    /// gasta menos bateria em portáteis. O desenho segue a taxa da tela nos dois casos.
    /// </summary>
    public bool SyncInputToDisplay { get; init; } = true;

    /// <summary>
    /// Leve em segundo plano (docs/performance.md): com a janela inativa ou minimizada (o usuário foi jogar), o ControlFS
    /// cede CPU (prioridade abaixo do normal e modo de eficiência do Windows), lê os controles só para notar conexões, para
    /// de consultar unidades e devolve a memória livre depois de alguns segundos. Cópias e extrações continuam, mais
    /// devagar enquanto a janela estiver em segundo plano. Mídia tocando mantém a prioridade normal.
    /// </summary>
    public bool LightInBackground { get; init; } = true;

    /// <summary>
    /// Experimental (#77): no teclado virtual, girar/inclinar um controle com giroscópio (DualSense e outros que o SDL expõe)
    /// aponta as teclas. Desligado por padrão; o sensor só é ligado com isto ativo. O direcional continua funcionando.
    /// </summary>
    public bool GyroKeyboard { get; init; }

    /// <summary>Volume padrão dos sons do controle (#286): ligados de fábrica, em volume médio.</summary>
    public const int DefaultControllerSoundVolume = 50;

    /// <summary>
    /// Sons do controle (#276, #286): volume de 0 a 100 escolhido pelo usuário; 0 = desligados. Null = nunca escolheu: vale o
    /// padrão (ligado, <see cref="DefaultControllerSoundVolume"/>). Os sons só tocam nas ações vindas do controle.
    /// </summary>
    public int? ControllerSoundLevel { get; init; }

    /// <summary>
    /// Só para ler preferências da 0.13 (campo antigo, padrão 0): um valor acima de 0 foi escolha do usuário e vale; 0 foi o
    /// padrão de então (indistinguível de "desligei"), então vale o padrão novo. Quem desliga agora grava
    /// <see cref="ControllerSoundLevel"/> = 0 e nunca mais é ligado de novo.
    /// </summary>
    public int ControllerSoundVolume { get; init; }

    /// <summary>O volume que vale agora (0 = desligados).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int EffectiveControllerSoundVolume =>
        Math.Clamp(ControllerSoundLevel ?? (ControllerSoundVolume > 0 ? ControllerSoundVolume : DefaultControllerSoundVolume), 0, 100);

    /// <summary>Status do Git (#75): ramo e marcas de modificado/novo em pastas de repositórios. Desligado por padrão.</summary>
    public bool ShowGitStatus { get; init; }

    /// <summary>Sugestões locais no teclado virtual (nunca em senhas). Desligar apaga <see cref="TypedTexts"/>.</summary>
    public bool KeyboardSuggestions { get; init; } = true;

    /// <summary>Nomes e buscas concluídos no teclado virtual, o mais recente primeiro (lista limitada; nunca senhas).</summary>
    public IReadOnlyList<string> TypedTexts { get; init; } = [];

    /// <summary>Verifica novas versões a cada abertura do app. Desligável no menu.</summary>
    public bool AutoCheckUpdates { get; init; } = true;

    /// <summary>
    /// Atualização automática ao abrir (versão instalada): se a verificação da abertura achar uma versão nova e verificada, e
    /// o usuário ainda não começou a usar o app, instala e reabre sozinho. Desligado, só oferece "Instalar e reiniciar".
    /// </summary>
    public bool AutoInstallUpdates { get; init; } = true;

    /// <summary>
    /// Última versão que a atualização automática tentou instalar. Se a mesma versão aparecer de novo na abertura seguinte
    /// (a instalação não pegou), não tenta de novo sozinha: cai no aviso de sempre. Evita um laço de reinícios.
    /// </summary>
    public string? LastAutoInstallAttempt { get; init; }

    /// <summary>Instala em silêncio, ao sair, uma atualização já baixada e verificada.</summary>
    public bool InstallUpdatesOnExit { get; init; } = true;

    /// <summary>Recebe versões de pré-lançamento. Null = automático (sim se a versão atual for pré-lançamento).</summary>
    public bool? IncludePrereleases { get; init; }

    public DateTimeOffset? LastUpdateCheck { get; init; }

    /// <summary>
    /// Boas-vindas (#231). True: vistas ou puladas. False: instalação nova, ainda não vistas (o armazenamento grava false ao
    /// criar as preferências). Null: preferências de antes das boas-vindas (quem reinstala ou atualiza; a pasta de dados
    /// sobrevive à reinstalação): também ainda não vistas, então aparecem uma vez. Campo opcional: versões anteriores
    /// ignoram a propriedade, então o esquema não muda.
    /// </summary>
    public bool? OnboardingCompleted { get; init; }

    /// <summary>
    /// "Mais da equipe": mostrada uma única vez, depois das boas-vindas e do tutorial. True assim que abre (mesmo que o app
    /// seja fechado com ela aberta); nunca volta sozinha. Rever: Menu → Ajuda e tutorial → Mais da equipe.
    /// </summary>
    public bool? PromoSeen { get; init; }
}

/// <param name="FirstRun">Nenhuma preferência salva ainda (primeira abertura): padrões que dependem do aparelho podem valer.</param>
public sealed record SettingsLoadResult(AppSettings Settings, bool RecoveredFromCorruption, string? Notice, bool FirstRun = false);

public interface ISettingsStore
{
    SettingsLoadResult Load();

    void Save(AppSettings settings);
}
