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
| Diretórios existentes no destino que sejam link/junction/reparse point não são atravessados; cadeia **revalidada** imediatamente antes de cada gravação final. | `DestinationGuard` | `Existing_link_in_destination_is_not_followed` (symlink, macOS); junction: `WindowsIntegrationTests` (**passou** na CI `windows-latest` (2026-09-26)) |
| Colisões por caixa, normalização Unicode (NFC) e arquivo×pasta são rejeitadas com motivo. | `SafeExtractor.BuildPlan` | `Case_and_type_collisions_are_rejected_not_merged` |
| Nunca sobrescreve em silêncio; escolha inicial preserva o existente; "substituir" exige segunda confirmação; "aplicar aos demais" vale só para a operação. | `SafeExtractor.PlaceAsync`, `AppController.ShowConflictDialog` | `Conflicts_*`, `Apply_to_remaining_*`, jornadas de conflito |
| Staging privado no mesmo volume (`.controlfs-staging-<128 bits aleatórios>` com manifesto), arquivo `CreateNew`, move sem sobrescrita. Temporários removidos em sucesso, falha e cancelamento. | `SafeExtractor` | vários (verificam ausência de `.controlfs-`) |
| Pasta dedicada nunca reutiliza existente (cria temporária + `Directory.Move`, que falha se o alvo existir). | `CreateDedicatedFolder` | `Dedicated_folder_never_reuses_an_existing_one` |
| Limites finitos sobre bytes **efetivamente escritos**: por entrada, total, quantidade de entradas, profundidade, comprimento, tempo; relação de expansão como sinal adicional; dados além do tamanho declarado = corrupção. | `ExtractionLimits`, `SafeExtractor` | `Size_limits_count_actual_bytes`, `Expansion_ratio_*`, `Entry_count_limit_*` |
| CRC32 próprio; falha não deixa arquivo parcial no destino. | `Crc32` | `Corrupted_payload_fails_crc_*` |
| Nada é executado; o original nunca é apagado; permissões/atributos do compactado não são restaurados (apenas data de modificação plausível). | — | asserções nos testes |
| Mark of the Web: `Zone.Identifier` do compactado é copiado para cada arquivo extraído (NTFS). | `MarkOfTheWeb` | `WindowsIntegrationTests` (**passou** na CI `windows-latest` (2026-09-26)) |
| Senhas: nunca em logs/arquivos/argumentos; `ExtractionRequest.ToString()` omite a senha; o buffer do teclado virtual é zerado em `TakeSecret`/cancelar. | `VirtualKeyboard`, `ExtractionRequest` | `Password_is_masked_*`, jornada de senha |

## Limites honestos da proteção

- **Corrida (TOCTOU):** a verificação usa atributos por caminho, não handles abertos com `FILE_FLAG_OPEN_REPARSE_POINT`.
  Um processo malicioso **com os mesmos privilégios** pode trocar uma pasta por junction entre a revalidação e o `File.Move`.
  Mitigação por handles está planejada (Etapa 3). Não anunciamos proteção contra corrida.
- A extração roda **no mesmo processo** (thread de trabalho). Não há worker isolado nem sandbox; um decodificador que não
  respeite cancelamento só é interrompido pelo limite de tempo ao término do bloco atual.
- A string da senha exigida pelo motor (`ReaderOptions.Password`) é imutável e fica na memória gerenciada até ser coletada.
- A checagem de espaço usa o total declarado (metadado não confiável) como sinal inicial; a contagem real continua durante a gravação.
- "Extraiu sem erro" não significa conteúdo seguro. Não há verificação antivírus.

## Relato de vulnerabilidades

Ver `SECURITY.md`.
