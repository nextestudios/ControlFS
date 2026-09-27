# Roadmap

> **Fonte atual do roadmap: GitHub Issues.** Acompanhamento geral e ordem recomendada:
> [#95](https://github.com/nextestudios/ControlFS/issues/95). Milestones 1–9 com épicos #87–#94 e issues #10–#86
> (prioridade, área e dependências nativas "blocked by"). As etapas abaixo são o histórico do plano inicial.

Etapas com critérios de saída; sem datas.

## Etapa 0 — Viabilidade e decisões ✅ (com pendências de Windows)

Feito: ferramentas e versões fixadas; licenças verificadas; binding SDL3 validado por reflexão e execução (macOS);
prova de entrada (`InputProbe`); fixture ZIP aberta e extraída; WinUI compila fora do Windows (C# sem XAML).
Pendente para sair de verdade: build + execução no Windows 11 x64; probe com controle real.

## Etapa 1 — Primeira jornada vertical 🟡 (lógica completa e testada; UI Windows não executada)

Feito: navegação em diretórios reais; foco previsível; menu do app e de ações; teclado virtual; criar pasta; abrir ZIP em
modo somente leitura; extrair tudo/seleção/aqui/para pasta dedicada/para… (seletor interno); senha; conflitos;
progresso na central de operações; resultado por item; proteções de caminho, links, colisões, limites e temporários.
Critério restante: executar a jornada no Windows com controle real e registrar evidência.

## Etapa 2 — Gerenciador e extrator completos

Seleção múltipla em operações; copiar/recortar/colar/mover/renomear (preservando extensão)/excluir (Lixeira primeiro);
favoritos e recentes; busca incremental; breadcrumbs; edição de caminho; dois painéis com "copiar/mover/extrair para o outro
painel"; propriedades com tamanho recursivo cancelável; visualização de imagem/texto; abrir externamente com aviso;
modo grade. Extrator: ZIP64, AES, 7z, RAR4/5, TAR, TAR.GZ, GZ, verificar integridade, vários compactados; testes de UI.

## Etapa 3 — Robustez

Assistente para controles sem perfil, perfis versionados com importação/exportação, remapeamento, SDL_GameControllerDB
versionado, dispositivo ativo por ação explícita, duplicidade físico/virtual, suspensão/retomada, tema claro e cor de
destaque, movimento reduzido, acessibilidade (narrador), cache de miniaturas, verificação por handles contra junction
(TOCTOU), worker de extração isolado, limpeza de staging por manifesto, logs com rotação e pacote de diagnóstico.

## Etapa 4 — 1.0

Critérios de aceite (jornadas A–F) com evidência, matriz real de controles e formatos, pacote validado em máquina limpa,
licenças e avisos, notas de limitações.
