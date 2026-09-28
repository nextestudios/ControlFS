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

## Atalhos (.url, .lnk) e seus ícones (#168)

Um atalho é um arquivo comum que qualquer programa (ou download) pode deixar na Área de trabalho, então o conteúdo dele
não é confiável.

- **Leitura:** `.url` só é lido para exibir (`InternetShortcut.Parse`, Core): limite de 64 KB, leitura tolerante (primeira
  ocorrência de cada chave, linhas estranhas ignoradas), valores com caractere de controle ou acima de 2048 caracteres
  descartados. Não é lido se for link/ponto de nova análise ou arquivo só na nuvem (ler baixaria o conteúdo). O `.lnk`
  é lido pelo `IShellLink` sem resolver o destino (sem procura, sem rede).
- **Jogo da Steam:** decidido só pelo esquema `steam://` da URL; qualquer outro esquema continua um atalho da Internet
  comum. O título mostrado é o nome do arquivo sem `.url` (nunca um texto de dentro do atalho).
- **Ícones:** o Shell nunca é chamado sobre o atalho (o manipulador de `.url`/`.lnk` do Windows leria o ícone declarado,
  inclusive de `\\servidor\…`, o que enviaria as credenciais NTLM do usuário a esse servidor só por mostrar a pasta). O
  caminho declarado (`IconFile`, IconLocation ou o destino do `.lnk`) passa por `IconLocationPolicy` (Core): só
  `X:\…` absoluto, sem UNC, sem `\\?\`/`\\.\`/`\??\`, sem URL (`file:`, `http:`), sem relativo, sem `..`, sem fluxo
  alternativo, sem nome de dispositivo, sem variável por expandir, e (para ícones) só .ico/.exe/.dll/.icl/.cpl. Depois,
  `ShortcutFiles.ResolveLocal` (Infrastructure) exige unidade fixa (unidade mapeada de rede é recusada) e confere cada
  pasta do caminho da raiz para dentro sem seguir nenhuma: um link/junção no caminho recusa o ícone. Só então o ícone é
  extraído do arquivo local (`SHDefExtractIcon`, na thread dedicada de ícones, .ico até 16 MB). Documento de destino de
  um `.lnk` usa só o ícone do tipo (pela extensão), para que um `.lnk` apontando para outro `.url` não seja seguido.
  Reserva da Steam: o mesmo nome `<hash>.ico` em `steam\games` da instalação achada em `HKCU\Software\Valve\Steam\SteamPath`,
  com as mesmas regras; nada é baixado. Testes: `ShortcutTests` (política e leitura), `ShortcutIconIntegrationTests`
  (ícones reais, caminho remoto e junção recusados).
- **Abrir:** o próprio arquivo do atalho vai para o Shell (`ShellExecute`), como qualquer arquivo; nunca se monta uma linha
  de comando com o conteúdo. `.url` e `.lnk` continuam em `ExecutableFiles`, então pedem confirmação começando em
  "Cancelar" — **também para jogos da Steam** (decisão: um `steam://` pode levar argumentos a um jogo, e o arquivo pode ter
  mudado desde a listagem; o diálogo mostra o jogo, o arquivo e o que o atalho abre). Sem programa registrado para o
  esquema da URL (ex.: Steam não instalada), o ControlFS mostra um erro legível em vez de chamar o Windows.

## Visualizações internas

- A visualização de imagens nunca executa nada: só o decodificador de imagens do Windows (WIC) lê os pixels, fora da thread de UI.
- Antes de decodificar, `ImagePreviewPolicy` (Core) confere o formato real pelo conteúdo (PNG, JPEG, GIF, BMP, WebP), o
  tamanho do arquivo (100 MB) e a resolução declarada (80 megapixels): uma "bomba" de poucos bytes que declara
  100 000 × 100 000 é recusada sem chegar ao decodificador (`ImagePreviewPolicyTests`, `ImagePreviewJourneyTests`).
- A imagem é reduzida na decodificação para no máximo 4096 px no lado maior (~64 MB por imagem em memória).
- A visualização de texto é somente leitura e lê no máximo 2 MB / 10.000 linhas (`TextPreview`, Core); arquivos binários
  são recusados; scripts podem ser lidos pelo menu sem nunca serem executados (`TextPreviewTests`, `TextPreviewJourneyTests`).
- A visualização de PDF (#59) só desenha pixels com o `Windows.Data.Pdf`, uma página por vez, fora da thread de UI:
  links, anexos, formulários e JavaScript do PDF nunca são abertos nem executados, e nada é entregue a outro programa.
  Antes de abrir, `PdfPreviewPolicy` (Core) exige a assinatura `%PDF-` no primeiro 1 KB (um executável renomeado para
  `.pdf` nunca chega ao renderizador) e o limite de 200 MB. Cada página é desenhada com no máximo 3072 px no lado maior
  (~36 MB), só as primeiras 5.000 páginas são navegáveis e abrir ou desenhar tem prazo de 20 s: um PDF feito para
  travar o renderizador vira erro na tela (`PdfPreviewJourneyTests`, `PdfRendererIntegrationTests`). A senha de um PDF
  protegido é digitada no campo mascarado, sem sugestões, usada só para abrir e nunca guardada. O arquivo fica aberto
  compartilhado (leitura, escrita, exclusão) enquanto a visualização durar e é fechado ao sair.
- A reprodução de áudio (#60) usa o `MediaPlayer` do Windows (Media Foundation) só com os codecs instalados. O arquivo é
  entregue como fluxo local (`MediaSource.CreateFromStream`), nunca como URL: nada vai para a rede e o reprodutor não
  resolve caminhos por conta própria. Antes, `MediaPreviewPolicy` (Core) recusa arquivos vazios e executáveis
  disfarçados (cabeçalho `MZ`), fora da thread de UI. A decodificação acontece nas threads do próprio Media Foundation;
  a aplicação só lê um retrato do estado a cada quadro. Os controles de mídia do sistema ficam desligados, fechar para o
  som e libera o arquivo na hora (`AudioPreviewJourneyTests`, `MediaPlayerIntegrationTests`).
- O reprodutor de vídeo (#61, #170) segue as mesmas regras do áudio (fluxo local, executáveis disfarçados recusados,
  só codecs do Windows, fechar para e libera o arquivo). Legendas externas só são usadas quando são arquivos comuns
  (sem link/junção), com o mesmo nome do vídeo, na mesma pasta e com até 5 MB, lidas como fluxo local pelo próprio
  Windows. "Continuar de onde parou" guarda em `playback.json` só um resumo SHA-256 (caminho + tamanho + data) e os
  segundos, no máximo 500 entradas; arquivo ilegível recomeça vazio (`VideoPlayerJourneyTests`).
- A edição leve de texto (#62) só abre arquivos que `TextEditDocument` (Core) consegue regravar byte a byte iguais
  (mesma codificação, BOM e quebras; linhas não tocadas nunca mudam), até 1 MB e 10.000 linhas; binários e arquivos
  somente leitura são recusados. Salvar exige confirmação; `AtomicFileWriter` grava num temporário `.controlfs-edit-*.part`
  na mesma pasta (registrado no diário de temporários antes de existir, para limpeza após queda), com `WriteThrough` e
  flush até o disco, e troca com `File.Replace` (atributos, datas e permissões do original preservados), deixando o
  original em `nome.controlfs.bak`. Se o tamanho ou a data mudaram desde a abertura, salvar pede confirmação explícita.
  Uma linha editada nunca ganha quebras (`TextEditDocumentTests`, `TextEditJourneyTests`).
- Arquivos dentro de compactados não são visualizados.

## Atualizações automáticas

Manifesto de release assinado (ECDSA P-256/SHA-256) com chave que existe só no secret `UPDATE_SIGNING_KEY` do GitHub;
chaves públicas embutidas no app (`UpdateTrust`; vale qualquer uma do conjunto, para rotação e chave reserva — `decisions/0006`). O app exige assinatura válida, produto/repositório corretos, versão do
manifesto = versão da tag e > versão atual (sem downgrade/replay), HTTPS para hosts fixos com redirecionamentos
conferidos, tamanho e SHA-256 exatos, e reconfere o arquivo aberto sem escrita de terceiros antes de executar.
Testes: `UpdateServiceTests` (adulteração, outra chave, qualquer chave do conjunto, degrau depois da rotação, replay, sem assinatura, host fora da lista, hash errado,
download maior que o declarado, offline) e `UpdateFlowTests`. Riscos e limites: `decisions/0005`.

## Relato de vulnerabilidades

Ver `SECURITY.md`.

## Celular como controle (#223)

O ControlFS abre um canal de rede que **controla o PC**, então ele só existe quando o usuário pede (Menu → Conectar
celular) e é fechado em toda saída. Código: `src/ControlFS.Infrastructure.Remote` (servidor, HTTP, quadros),
`src/ControlFS.Core/Remote` (sessão, mensagens, QR Code) e `AppController.Phone.cs` (permissão e entrada).

| Defesa | Implementação | Teste |
|---|---|---|
| Nada escuta fora de uma sessão. O `TcpListener` nasce em Conectar celular, só no endereço IPv4 **privado** escolhido (10/8, 172.16/12, 192.168/16; nunca `0.0.0.0`, loopback, link-local ou IP público), numa porta alta aleatória com uso exclusivo. Para de escutar assim que um celular se autentica, e tudo fecha em Cancelar, Recusar, Desconectar (PC ou celular), 2 min sem uso, 2 min sem permissão, queda da conexão (keep-alive de 5 s, 15 s sem resposta) e ao fechar o app. | `PhoneLinkServer`, `LanAddresses`, `PhoneSession` | `PhoneLinkIntegrationTests` (porta fechada depois de Desconectar e depois de autenticar), `PhoneChannelTests.Session_*` |
| Só aparelhos da rede local conectam (endereço de origem privado); no máximo 4 conexões TCP ao mesmo tempo durante o pareamento. | `PhoneLinkServer.IsAllowedPeer` | — |
| HTTP mínimo: duas rotas (`GET /<sessão>` a página; `GET /<sessão>/ws` o WebSocket), só GET/HTTP/1.1, sem corpo, só ASCII e CRLF, cabeçalho ≤ 4 KB e ≤ 32 campos, 5 s para chegar, campo repetido recusado. `Host` tem de ser exatamente `IP:porta` (contra DNS rebinding) e `Origin` do WebSocket, `http://IP:porta`. A sessão é um id aleatório de 128 bits no caminho. | `HttpRequestParser` | `PhoneChannelTests.Http_parser_*`, `WebSocket_accept_*`, Origin estranha recusada em `PhoneLinkIntegrationTests` |
| A página é um HTML único servido pelo app (sem CDN, fonte ou imagem de fora), com `Content-Security-Policy` que só permite o script com o hash SHA-256 embutido e conexões `ws://IP:porta`; `no-store`, `no-referrer`, `nosniff`, sem frames. | `CompanionPage`, `Companion/phone.html` | `PhoneLinkIntegrationTests` (cabeçalho CSP) |
| Chave de 32 bytes aleatória **por pareamento**, só no **fragmento** do QR Code (`#k=…`: o navegador nunca o envia pela rede; a página o tira da barra de endereço). HKDF-SHA256(chave, sal = id da sessão) gera uma chave por sentido. Cada mensagem WebSocket é **AES-256-GCM** com nonce = contador de 64 bits que precisa ser exatamente o próximo; repetida, fora de ordem, adulterada ou do outro sentido **derruba a conexão** na primeira falha. As chaves são apagadas da memória ao encerrar. | `PhoneCrypto`, `FrameSealer`, `FrameOpener`; página: @noble/ciphers + @noble/hashes | `PhoneChannelTests.Keys_code_and_frames_match_the_phone_page_libraries` (vetores gerados pela biblioteca da página), `Frames_reject_*`, `PhoneLinkIntegrationTests.Tampered_frame_ends_the_session` |
| Uso único: a primeira conexão que prova ter a chave fica com a sessão; outra é recusada (a porta já não escuta). Até 2 aberturas simultâneas e 8 falhas de autenticação antes de encerrar. Sem reconexão automática: cair a conexão mata a chave, e voltar exige um QR Code novo. | `PhoneSession` | `PhoneChannelTests.Session_*`, segundo celular recusado em `PhoneLinkIntegrationTests` |
| **Permissão no PC**: o celular autenticado aparece com o IP e um código de 6 dígitos (HKDF da chave com um número aleatório que o próprio celular mandou), que a página também mostra. O diálogo é sensível, começa em Recusar e nada do celular vira entrada antes de Permitir. | `AppController.AskToAllowPhone`, `PhoneSession.TryAcceptInput` | `PhoneJourneyTests`, `PhoneLinkIntegrationTests` |
| O celular só manda **ações semânticas** (as mesmas de um botão: direções, página, rolagem, abrir, voltar, marcar, ações, menu, buscar, lista/grade, regiões) e **texto** (≤ 256 caracteres, sem caracteres de controle) para o campo do teclado na tela; nunca caminhos, comandos ou dados arbitrários. JSON com campo desconhecido ou repetido é descartado; mensagens acima de 1 KB fecham a conexão; mais de 30/s (rajada de 60) são descartadas. | `PhoneProtocol`, `PhoneSession` | `PhoneChannelTests.Phone_messages_*`, `Session_limits_*` |
| Com uma confirmação sensível aberta (excluir, excluir permanentemente, substituir, desfazer, mapear controle), o celular **só consegue Voltar** (a opção segura); texto também é ignorado. Mensagens são pressões avulsas: perder a conexão não deixa botão preso nem repetição no PC. | `AppController.HandlePhone` | `PhoneJourneyTests` |

**Risco residual, dito às claras:** a página em si é servida por **HTTP simples** na rede local (navegadores de celular
bloqueiam `crypto.subtle` em `http://` e um HTTPS autoassinado mostra alertas), então um atacante **ativo** na mesma rede
(ARP spoofing, Wi-Fi comprometido) que intercepte o pedido da página pode trocá-la por outra e, com isso, ler a chave do
fragmento e comandar o ControlFS pelo celular da vítima ou conectar primeiro. O que reduz esse risco: a sessão dura 2
minutos e serve uma conexão só; o PC mostra o **IP** de quem conectou e pede **Permitir** (um IP inesperado é o sinal);
o que o celular consegue fazer é limitado a ações de navegação e texto, e confirmações importantes continuam só no PC.
Um atacante **passivo** (que só escuta a rede) vê o id da sessão mas não a chave, então não lê nem forja mensagens.
Não use em redes em que você não confia (Wi-Fi público).

**Criptografia escolhida:** AES-256-GCM, e não ChaCha20-Poly1305, porque o `ChaCha20Poly1305` do .NET só existe no
Windows 10 build 20142+ / Windows 11, e o ControlFS roda no Windows 10 2004 (19041); o `AesGcm` usa o CNG em todas as
versões suportadas. O formato do quadro (nonce de 96 bits com contador, etiqueta de 128 bits, chaves separadas por
sentido) é o mesmo.
