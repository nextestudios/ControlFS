# PROGRESS

Última atualização: 2026-09-26 · Versão: 0.1.0 (pré-alfa) · Ambiente desta sessão: **macOS (arm64)**, .NET SDK 10.0.401.

## Resumo honesto

A Etapa 0 e a lógica completa da primeira jornada vertical (Etapa 1) estão implementadas e **testadas automaticamente
sobre arquivos reais em diretórios temporários**. O aplicativo WinUI compila no macOS e na CI Windows, e os testes de integração Windows passam na CI, mas o **app ainda
não foi aberto numa sessão Windows interativa**. Nenhum controle físico foi testado.

## Implementado

- **Entrada:** ações semânticas; mapeamento por posição física com convenção confirmar/voltar alternável (comportamento e
  legendas); analógico com zona morta, limiar, histerese e dominância; repetição progressiva só para navegação; dispositivo
  ativo único; trava de botões mantidos ao trocar de contexto; suspensão em segundo plano; backend SDL3 sem janela SDL,
  bombeado na thread de UI; teclado físico e mouse pelo mesmo modelo.
- **Navegação:** tela inicial de locais (pastas conhecidas via `SHGetKnownFolderPath` + unidades), listagem real,
  histórico, subir nível, ordenação (nome/tipo/tamanho/data) preservando foco por identidade, ocultos, marcação,
  propriedades, proteção contra respostas atrasadas (geração), `Back` com semântica definida.
- **Teclado virtual:** PT-BR/EN, maiúsculas (uma vez/travado), números, símbolos, acentos, espaço, apagar, limpar,
  cursor, OK/cancelar; campos Nome/Caminho/Senha com regras distintas; senha mascarada, revelação explícita, buffer zerado.
- **Criar pasta** com validação do Windows e sem reaproveitar pasta existente.
- **Extrator ZIP:** inspeção sem extrair (navegador virtual somente leitura), extrair tudo/seleção, pasta dedicada, aqui,
  "para…" com seletor de pasta interno (criar pasta no fluxo), resumo antes de iniciar, senha com nova tentativa,
  conflitos (pular/manter ambos/substituir com segunda confirmação/aplicar aos demais/cancelar), central de operações com
  progresso e cancelamento, resultado por item, "abrir pasta extraída".
- **Segurança:** contenção de caminhos, links/especiais bloqueados, junction/symlink no destino não seguidos, colisões
  rejeitadas, limites efetivos, staging com manifesto, CRC32 próprio, MOTW (código Windows), nada executado, original
  preservado. Detalhes e limites em `docs/security-model.md`.
- **Configuração:** JSON versionado com gravação atômica e recuperação de arquivo corrompido.
- **Documentação e CI:** todos os documentos exigidos; ADRs 0001–0004; workflow de CI (não executado).

## Itens Should do roadmap (0.5.0-alpha.1)

- Resolvidas: #20, #21, #22, #25, #26, #29, #40, #44, #47, #49, #50, #54, #55, #57, #58, #63, #64, #66, #68, #69, #80, #83,
  #85, #126 (PRs #131–#155). #84 (assinatura de código) tem o workflow pronto e espera o certificado do mantenedor.
- Evidência (CI Windows, 2026-09-27): 367 testes unitários/jornadas e 28 de integração Windows (Lixeira real, ZIP64 acima
  de 4 GiB, junctions no tamanho de pasta), 16 verificações de UI Automation no Smoke; pacotes ~26% menores.

## Controle, teclado, navegação e segurança (0.4.0-alpha.1)

- Issues *Must* do roadmap (MoSCoW) resolvidas: #18, #19, #23, #24, #28, #30, #31, #32, #33, #34, #35, #36, #41, #42, #43,
  #46, #48, #79, #81, #82 (PRs #107–#129). #78 (matriz de controles físicos) segue aberta: depende de hardware real, com a
  tela "Teste de controles" (#127) pronta para isso.
- Evidência (CI Windows, 2026-09-27): testes unitários/jornadas e de integração Windows (incluindo corrida de junction
  durante extração/cópia/movimentação e busca sem seguir junction); capturas renderizadas em 1280×720, 1280×800,
  1920×1080 e 3840×2160 e galeria de glifos conferidas no workflow Smoke.

## Operações de arquivo (0.3.0-alpha.1)

- Todas as issues críticas do roadmap resolvidas: motor de operações #10, copiar #13, mover #14, conflitos #17 (PR #101),
  recortar #15 e colar #16 (PR #102), renomear #11 (PR #103), Lixeira #12 (PR #104).
- Evidência (CI Windows, 2026-09-27): 228 testes unitários/jornadas + 14 de integração Windows, incluindo mover entre
  volumes reais (D: → C:), arquivo bloqueado, renomear só maiúsculas/minúsculas, envio real à Lixeira e exclusão
  permanente sem seguir junction. Licença AGPL-3.0-only a partir desta versão (#9); ícone oficial (#100).

## Formatos, compactar e abrir com o Windows (0.2.0-alpha.1)

- Extrair 7z, RAR4/RAR5 (inclusive sólidos), TAR, TAR.GZ, GZ; compactar em ZIP e TAR.GZ; abrir com o programa padrão,
  "Abrir com…" e "Mostrar no Explorador" (#5). Detalhes: `docs/archive-support.md`.
- Achados na CI durante o desenvolvimento: `Entry.Attrib` do SharpCompress lança `NotImplementedException` em TAR/GZ;
  o leitor TAR do SharpCompress não entende PAX (trocado por `System.Formats.Tar`); RAR5 criptografado guarda CRC
  transformado; o `тест.txt` das fixtures está em CRLF (hash conferido pela versão arquivada).
- Evidência (CI 2026-09-26): 214 testes (Windows e Linux) + 9 de integração Windows, incluindo abrir o Bloco de Notas
  pelo serviço de shell; smoke: portátil e instalado abrem.

## Portátil com o nome publicado (0.1.0-alpha.4)

- O smoke da release 0.1.0-alpha.3 **com os arquivos baixados da página** mostrou: instalador OK, mas o portátil falhava
  sob o nome `ControlFS-Portable-x64.exe` (procurava `ControlFS-Portable-x64.pri`). Os testes renomeavam o arquivo e
  esconderam o erro. Corrigido com `resources.pri`; os testes agora usam o nome publicado (#4).

## Correção de inicialização (0.1.0-alpha.3)

- **As releases 0.1.0-alpha.1 e 0.1.0-alpha.2 não abriam** (instalado e portátil): o processo terminava em ~1 s com
  `0xC000027B` em `Microsoft.UI.Xaml.dll`. Reproduzido pelo novo workflow `smoke.yml` num Windows do GitHub; o log
  novo mostrou `Cannot locate resource from 'ms-appx:///Microsoft.UI.Xaml/Themes/themeresources.xaml'`. Causa: app sem
  pacote MSIX precisa de `EnableMsixTooling=true` para gerar `ControlFS.pri`. Os testes anteriores cobriam a lógica,
  mas nunca abriam o app — lacuna fechada: CI e release agora abrem o portátil e o instalado antes de publicar.
- Evidência pós-correção (runner `windows-latest`, Windows Server 2025 26100): portátil `.exe` único e app instalado
  abrem, mantêm a janela "ControlFS" (40 s e 25 s), log até "Janela ativada", SDL 3.5.0 carregado; print mostra a
  interface desenhada e o aviso de nova versão vindo do GitHub real. Instalar → abrir → desinstalar: OK.
- Portátil agora é `ControlFS-Portable-x64.exe` (arquivo único) com dados em `ControlFS_Data` ao lado.

## Instalador e atualizações (0.1.0-alpha.2)

- Instalador Inno Setup por usuário, sem administrador (`build/ControlFS.iss`), com marcador que habilita atualização.
- Atualização automática: verificação diária desligável, download em segundo plano, "Instalar e reiniciar" ou instalar
  ao sair, nunca durante operações; manifesto assinado (ECDSA P-256) + SHA-256 + tamanho + hosts fixos
  (`src/ControlFS.Infrastructure.Updates`, ADR 0005). Portátil só avisa.
- Testes: 34 novos (versões, verificação, servidor falso com ataques, fluxo no controlador).
- Release 0.1.0-alpha.2 publicada pela CI: instalador compilado, **instalação e desinstalação silenciosas sem admin
  aprovadas no runner Windows**, manifesto assinado e conferido contra a chave pública do app.
- Teste ponta a ponta contra o GitHub real (2026-09-26), com o código de produção do atualizador e a chave oficial:
  simulando 0.1.0-alpha.1 → encontrou 0.1.0-alpha.2, assinatura válida, instalador baixado (64.416.800 bytes) com
  SHA-256 conferido; simulando 0.1.0-alpha.2 → "atualizado". `shasum -c SHA256SUMS.txt` → OK.
- **Ainda não validado:** execução do instalador pelo app numa sessão Windows interativa (Restart Manager fechando o
  app, reabertura com `/RELAUNCH=1`, SmartScreen). Checklist em `docs/TESTING.md`.

## Executado nesta sessão (resultados observados)

| Comando | Resultado |
|---|---|
| `dotnet-install.sh --version 10.0.401` | SDK instalado |
| Reflexão sobre SharpCompress 1.0.0 e ppy.SDL3-CS 2026.722.0 | APIs confirmadas (ver ADR 0002/0004) |
| Sondagem de SharpCompress com ZIPs forjados | comportamentos registrados no ADR 0004 |
| Experimento WinUI no macOS (XAML vs. só C#) | XAML falha; só C# compila (ADR 0003) |
| `dotnet run --project tools/ControlFS.InputProbe -- 3` | `Backend: SDL 3.5.0`; 0 dispositivos; encerrou sem erro |
| `dotnet build ControlFS.slnx -c Release` | sucesso, 0 avisos (8 projetos, incluindo o app WinUI) |
| `dotnet test ControlFS.slnx -c Release` | **133 aprovados**, 0 falhas; 6 pulados (exclusivos do Windows) |
| Suíte executada 5× seguidas (antes das últimas correções) | 5/5 verdes (sem instabilidade observada) |
| Testes de regressão com correção revertida | falharam (2) como esperado; com a correção, passaram |
| CI GitHub (`windows-latest`), 2026-09-26 | build da solução ok; **133 + 6 testes Windows aprovados**; sem pacotes vulneráveis |
| CI GitHub (Linux), 2026-09-26 | 133 aprovados |

## Falhas encontradas e corrigidas

- Cancelamento antes do início lançava exceção em vez de resultado `Cancelled` (achado por teste).
- `InputRouter.Tick` reativava um controle travado se a própria repetição trocasse o contexto (revisão; teste adicionado).
- Extração com falha podia deixar árvore de pastas vazias na pasta dedicada (revisão; teste adicionado).
- "Tentar outra senha" ignorava a escolha de pasta dedicada (revisão).

## Ainda não validado

- **App aberto no Windows:** renderização WinUI sem XAML compilado (`XamlControlsResources`, `XamlReader.Load` do modelo
  de linha), foco de teclado na raiz, pacote portátil em máquina limpa, DPI/4K/720p. (Pastas conhecidas, unidades,
  junctions, MOTW e caminhos longos já passaram nos testes de integração da CI Windows.)
- **Hardware:** qualquer controle; conexão/desconexão/reconexão; suspensão; dispositivos virtuais duplicados.
- **Formatos:** AES, ZIP64, 7z, RAR, TAR, GZ.
- Núcleo em Linux (job da CI existe, não executado).

## Não implementado (e por isso não aparece na UI)

Copiar/recortar/colar/mover/renomear/excluir, busca, dois painéis, favoritos/recentes, breadcrumbs navegáveis, edição de
caminho, modo grade, visualizador, abrir externamente, tamanho recursivo, tema claro, cor de destaque, assistente e perfis
de controle, logs/diagnóstico, limpeza de staging entre sessões, worker isolado. Ver `docs/roadmap.md`.

## Próxima entrega concreta

1. Baixar a release portátil num Windows 11 x64, abrir o app e seguir `docs/TESTING.md`; corrigir o que aparecer na
   renderização e no foco; registrar evidências aqui.
2. Rodar `InputProbe` e a jornada B (extração) com ao menos um controle Xbox e um DualSense; preencher
   `docs/controller-compatibility.md`.
3. Fixtures e suporte a ZIP64 e AES (requisito 1.0) e início da Etapa 2 (renomear/excluir com Lixeira).
