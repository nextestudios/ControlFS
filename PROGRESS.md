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
