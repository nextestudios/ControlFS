# Desempenho

**Nenhuma medição de desempenho foi feita ainda.** Metas de projeto (não resultados): p95 < 50 ms de retorno visual à
navegação; rolagem próxima de 60 fps; pastas de 10 mil itens sem materializar milhares de controles; memória da extração
não proporcional ao tamanho descompactado.

## Decisões já tomadas com impacto em desempenho

- Listagem e extração fora da thread de UI (`Task.Run`); navegação cancela a listagem anterior e descarta respostas antigas.
- `ListView` virtualizado com modelo de linha sem bindings, preenchido em `ContainerContentChanging`.
- Extração em fluxo com buffer de 80 KB do `ArrayPool` (memória constante por entrada).
- SDL bombeado a 8 ms com a janela ativa e 120 ms em segundo plano (sem polling ocupado).

## Riscos conhecidos

- `MainWindow.Render` reconstrói rodapé e camada modal a cada mudança de estado; aceitável para Etapa 1, a medir.
- Trocar a seleção recria `ItemsSource` (para repintar marcas); em pastas grandes isso pode custar — otimizar com
  atualização de contêineres visíveis.
- A listagem ordena a pasta inteira antes de exibir (sem listagem incremental ainda).

## Como medir (pendente)

Registrar hardware, Windows, disco, tamanho da pasta e estado de cache; medir inicialização, primeira página,
navegação p95 (carimbo no `InputRouter` → `Render`), consumo em repouso e durante extração.
