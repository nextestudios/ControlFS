# Modelo de segurança da extração

## Ameaças consideradas

Compactados maliciosos (travessia, caminhos absolutos/UNC/dispositivo, ADS, nomes reservados, colisões por caixa ou
normalização, links), metadados falsos (tamanhos, CRC), conteúdo corrompido/truncado, bombas de descompressão e
estruturas de diretório que redirecionem gravações (links/junctions já existentes no destino).

## Defesas implementadas (e onde estão testadas)

| Defesa | Implementação | Teste |
|---|---|---|
| Contenção por construção: nome vira lista de componentes validados; rejeita `..`, raiz, `C:`, `C:x`, `\\`, `\\?\`, `\\.\`, NUL, `//`, `:` (ADS), reservados, ponto/espaço final, caracteres inválidos. Nada é "corrigido" em silêncio. | `ArchivePathPolicy`, `WindowsNameRules` | `ArchivePathPolicyTests`, `SafeExtractorTests.Malicious_paths_never_write_outside_destination` (prova por snapshot do disco) |
| Defesa em profundidade: caminho final normalizado precisa estar abaixo da raiz. | `DestinationGuard.AssertContained` | idem |
| Links e tipos especiais do compactado bloqueados (inclui symlink Unix em ZIP, que o motor não expõe como link). | `SharpCompressZipEngine.IsLinkOrSpecial` | `ZipEngineTests`, `Symlink_entries_are_blocked` |
| Diretórios existentes no destino que sejam link/junction/reparse point não são atravessados. No Windows, a cadeia raiz → pasta de destino é aberta **por handle** (`FILE_FLAG_OPEN_REPARSE_POINT`, sem `FILE_SHARE_DELETE`) e conferida pelo handle (atributos sem reparse point; caminho final igual ao esperado) imediatamente antes de cada gravação final; enquanto presa, nenhuma pasta da cadeia pode ser renomeada, apagada ou trocada por junction, e a movimentação final é feita **relativa ao handle** da pasta (`SetFileInformationByHandle(FileRenameInfo)`). A identidade da raiz é fixada no início: raiz trocada no meio recusa as gravações seguintes. Vale para extração e para copiar/mover. | `PinnedDirectory`, `DestinationGuard`, `FileOperationService` | `Existing_link_in_destination_is_not_followed` (symlink, macOS); junction: `WindowsBehaviorTests`; corrida adversarial (troca contínua por junction durante extração, cópia e movimentação) e pasta presa que não pode ser renomeada: `JunctionRaceTests` (Windows) |
| Colisões por caixa, normalização Unicode (NFC) e arquivo×pasta são rejeitadas com motivo. | `SafeExtractor.BuildPlan` | `Case_and_type_collisions_are_rejected_not_merged` |
| Nunca sobrescreve em silêncio; escolha inicial preserva o existente; "substituir" exige segunda confirmação; "aplicar aos demais" vale só para a operação. | `SafeExtractor.PlaceAsync`, `AppController.ShowConflictDialog` | `Conflicts_*`, `Apply_to_remaining_*`, jornadas de conflito |
| Staging privado no mesmo volume (`.controlfs-staging-<128 bits aleatórios>` com manifesto), arquivo `CreateNew`, move sem sobrescrita. Temporários removidos em sucesso, falha e cancelamento. Depois de uma queda: cada temporário (staging, `.controlfs-new-*`, `.controlfs-copy-*.part`) é registrado **antes** de ser criado (`TemporaryJournal`, na pasta de dados do app, com o processo dono); na próxima inicialização só é apagado o que o registro prova ser nosso (staging com manifesto e token do registro; parcial/pasta vazia com o nome aleatório exato), sem seguir links e nunca de uma instância que ainda está rodando. | `SafeExtractor`, `TemporaryJournal` | vários (verificam ausência de `.controlfs-`); `LeftoverCleanupTests` |
| Pasta dedicada nunca reutiliza existente (cria temporária + `Directory.Move`, que falha se o alvo existir). | `CreateDedicatedFolder` | `Dedicated_folder_never_reuses_an_existing_one` |
| Limites finitos sobre bytes **efetivamente escritos**: por entrada, total, quantidade de entradas, profundidade, comprimento, tempo; relação de expansão como sinal adicional; dados além do tamanho declarado = corrupção. | `ExtractionLimits`, `SafeExtractor` | `Size_limits_count_actual_bytes`, `Expansion_ratio_*`, `Entry_count_limit_*` |
| CRC32 próprio; falha não deixa arquivo parcial no destino. | `Crc32` | `Corrupted_payload_fails_crc_*` |
| Nada é executado; o original nunca é apagado; permissões/atributos do compactado não são restaurados (apenas data de modificação plausível). | — | asserções nos testes |
| Mark of the Web: `Zone.Identifier` do compactado é copiado para cada arquivo extraído (NTFS). | `MarkOfTheWeb` | `WindowsIntegrationTests` (**passou** na CI `windows-latest` (2026-09-26)) |
| Senhas: nunca em logs/arquivos/argumentos; `ExtractionRequest.ToString()` omite a senha; o buffer do teclado virtual é zerado em `TakeSecret`/cancelar. | `VirtualKeyboard`, `ExtractionRequest` | `Password_is_masked_*`, jornada de senha |

## Limites honestos da proteção

- **Corrida (TOCTOU), o que ainda resta:** as pastas **abaixo** da raiz do destino ficam presas por handle durante cada
  gravação final, então não podem ser trocadas por junction nesse intervalo (`JunctionRaceTests`). Continuam fora dessa garantia:
  - a própria raiz escolhida pelo usuário e as pastas **acima** dela (ex.: `C:\Users\voce`): podem ser links legítimos e são
    confiadas como escolha do usuário; a raiz é presa e sua identidade conferida a cada gravação, mas quem controla uma pasta
    acima dela pode redirecionar o caminho entre duas gravações (a gravação seguinte é recusada);
  - arquivos temporários são criados **por caminho** dentro da pasta presa (o Win32 não cria arquivo relativo a um handle);
    a cadeia presa impede o redirecionamento, mas não a leitura do temporário por outro processo com os mesmos privilégios;
  - o que acontece **depois** da gravação (outro processo pode mover a pasta assim que ela é solta) e metadados aplicados por
    caminho (data de modificação, Mark of the Web) logo após a colocação;
  - pastas presas não podem ser renomeadas/apagadas pelo próprio usuário durante a gravação de cada arquivo (efeito colateral
    intencional);
  - fora do Windows (apenas desenvolvimento/testes) a verificação é por caminho, sem proteção contra corrida;
  - sistemas de arquivos de rede que não aceitam renomear relativo a um handle usam o caminho completo da pasta presa.
- A extração roda **no mesmo processo** (thread de trabalho). Não há worker isolado nem sandbox; um decodificador que não
  respeite cancelamento só é interrompido pelo limite de tempo ao término do bloco atual.
- A string da senha exigida pelo motor (`ReaderOptions.Password`) é imutável e fica na memória gerenciada até ser coletada.
- A checagem de espaço usa o total declarado (metadado não confiável) como sinal inicial; a contagem real continua durante a gravação.
- "Extraiu sem erro" não significa conteúdo seguro. Não há verificação antivírus.

## Compactar e abrir com o Windows

- **Compactar** não segue links nem junctions da origem (listados como ignorados), grava num temporário na pasta de
  destino e só renomeia para o nome final ao concluir, sem sobrescrever; recusa criar o compactado dentro de uma pasta que
  está sendo compactada; cancelamento remove o temporário (`ArchiveCreatorTests`).
- **Abrir com o Windows** usa APIs do Shell com o caminho como parâmetro próprio (sem montar linha de comando). Tipos que
  executam código (`ExecutableFiles`: .exe, .msi, .bat, .ps1, .lnk, .url, .hta, .reg…) exigem confirmação que começa em
  "Cancelar"; SmartScreen/Mark of the Web do Windows continuam valendo. Nada é aberto automaticamente após extrair.
  Testado de verdade no runner Windows (`ShellIntegrationTests`: abre o Bloco de Notas, mostra no Explorador).

## Visualizações internas

- A visualização de imagens nunca executa nada: só o decodificador de imagens do Windows (WIC) lê os pixels, fora da thread de UI.
- Antes de decodificar, `ImagePreviewPolicy` (Core) confere o formato real pelo conteúdo (PNG, JPEG, GIF, BMP, WebP), o
  tamanho do arquivo (100 MB) e a resolução declarada (80 megapixels): uma "bomba" de poucos bytes que declara
  100 000 × 100 000 é recusada sem chegar ao decodificador (`ImagePreviewPolicyTests`, `ImagePreviewJourneyTests`).
- A imagem é reduzida na decodificação para no máximo 4096 px no lado maior (~64 MB por imagem em memória).
- A visualização de texto é somente leitura e lê no máximo 2 MB / 10.000 linhas (`TextPreview`, Core); arquivos binários
  são recusados; scripts podem ser lidos pelo menu sem nunca serem executados (`TextPreviewTests`, `TextPreviewJourneyTests`).
- Arquivos dentro de compactados não são visualizados.

## Atualizações automáticas

Manifesto de release assinado (ECDSA P-256/SHA-256) com chave que existe só no secret `UPDATE_SIGNING_KEY` do GitHub;
chave pública embutida no app (`UpdateTrust`). O app exige assinatura válida, produto/repositório corretos, versão do
manifesto = versão da tag e > versão atual (sem downgrade/replay), HTTPS para hosts fixos com redirecionamentos
conferidos, tamanho e SHA-256 exatos, e reconfere o arquivo aberto sem escrita de terceiros antes de executar.
Testes: `UpdateServiceTests` (adulteração, outra chave, replay, sem assinatura, host fora da lista, hash errado,
download maior que o declarado, offline) e `UpdateFlowTests`. Riscos e limites: `decisions/0005`.

## Relato de vulnerabilidades

Ver `SECURITY.md`.
