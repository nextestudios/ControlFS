# 0003 — Interface WinUI construída em C#, sem XAML compilado (Etapa 1)

- **Data:** 2026-09-26
- **Estado:** aceita para a Etapa 1; revisável

## Bloqueio real

O ambiente de desenvolvimento desta sessão é macOS (arm64). Experimento controlado:

| Variante | Resultado no macOS |
|---|---|
| Projeto WinUI com `App.xaml` | Falha: `XamlCompiler output file ... was not created. The XAML compiler may have crashed.` (o compilador XAML é executável Windows). |
| Projeto WinUI só com C# | Compila; falham apenas ferramentas Windows `makepri.exe` e `mt.exe` (PRI e manifesto self-contained). |
| Só C#, com `AppxGeneratePriEnabled=false` e self-contained restrito ao Windows | **Compila** (verificação completa de tipos do código de UI). |

## Decisão

Views são construídas em C# (`src/ControlFS.App/Views`). O único trecho de XAML é um `DataTemplate` mínimo, sem
bindings, carregado por `XamlReader.Load` e preenchido em `ContainerContentChanging` (padrão da Microsoft para listas
virtualizadas). O app implementa `IXamlMetadataProvider` delegando a `XamlControlsXamlMetaDataProvider` e adiciona
`XamlControlsResources` em código.

No Windows, PRI e self-contained seguem o fluxo normal (condição `'$(OS)' == 'Windows_NT'`).

## Consequências

- (+) Todo o código de UI é verificado pelo compilador em qualquer SO; a CI Windows faz o build completo.
- (+) Estado de apresentação vive em `ControlFS.Application` (AppController), testado sem WinUI.
- (−) Menos idiomático que XAML; designers não editam marcação. Migrar views para XAML é possível sem tocar na lógica.
- (−) **Risco:** o carregamento de `XamlControlsResources`/metadados sem XAML compilado e o `XamlReader.Load` do modelo
  de linha só serão comprovados executando no Windows.
