# Plano de testes

## Camadas

| Camada | Projeto | Onde roda | Estado |
|---|---|---|---|
| Unidade (políticas, entrada, teclado, lista, estados) | `tests/ControlFS.UnitTests` | Windows (CI) | 214 testes passando na CI `windows-latest` |
| Integração do extrator com arquivos reais | idem (`Archives/`) | qualquer SO | incluídos acima |
| Jornadas ponta a ponta por ações semânticas | idem (`Application/JourneyTests`) | qualquer SO | incluídos acima; **não** substituem UI nem hardware |
| Integração Windows (pastas conhecidas, junction, MOTW, nomes reservados, caminhos longos) | `tests/ControlFS.WindowsIntegrationTests` | Windows | 6 testes, **passaram** na CI `windows-latest`; pulados fora do Windows |
| UI WinUI (foco, diálogos, teclado virtual na tela) | `build/Test-UiAutomation.ps1` (UI Automation) | Windows (workflow Smoke, manual) | 16 verificações no app real: anel de foco no menu, confirmação com foco na opção segura, escopo do modal, teclado virtual desenhado e recebendo texto (ver `TESTING.md`) |
| Hardware real | manual | Windows | matriz em `controller-compatibility.md`, tudo "não testado" |

Comandos: `dotnet test ControlFS.slnx`.

## Matriz obrigatória (seção 20 da especificação)

| Área | Cenário | Estado |
|---|---|---|
| Entrada | Conectar controle após abrir | implementado (eventos SDL `*_ADDED`); **não testado em hardware** |
| Entrada | Desconectar durante extração | router libera dispositivo sem tocar na fila; **não testado em hardware** |
| Entrada | Segurar confirmar ao abrir diálogo | **testado** (unidade: `Held_button_is_latched_*`) |
| Entrada | Drift e direções mantidas | **testado** (unidade: zona morta, histerese, repetição só de navegação) |
| Entrada | Físico + virtual simultâneos | parcial: um dispositivo ativo; diagnóstico/escolha explícita pendentes |
| Entrada | Controle sem sticks/gatilhos | todas as ações essenciais têm caminho por menu (Start/North → itens); **sem hardware**; joystick sem perfil comanda após o assistente (#79; testado com eventos simulados) |
| Foco | Modais, renomear, excluir | modais e criação de pasta **testados**; renomear/excluir não implementados |
| Texto | Acentos e extensão | acentos **testados**; edição de extensão depende de "renomear" (Etapa 2) |
| Arquivos | Falta de espaço, bloqueio, permissão | mapeamento de erros implementado; **sem teste** |
| Arquivos | Movimento entre volumes interrompido | não implementado (Etapa 2) |
| Arquivos | Cancelar após parte do lote | **testado** para extração (arquivos concluídos mantidos, restantes "não processados") |
| ZIP | simples, ZIP64, senha certa/errada | simples e ZipCrypto **testados**; ZIP64 e AES pendentes |
| RAR/7z | variantes | não implementado |
| Volumes | parte ausente | não implementado |
| Integridade | truncado / checksum inválido | **testados** |
| Segurança | `../`, absoluto, dispositivo | **testados** (prova por snapshot do disco) |
| Segurança | link no compactado / junction no destino | symlink **testado**; junction **testado** na CI Windows |
| Segurança | colisão, ADS, reservados | **testados** |
| Recursos | expansão e metadados enormes | limites de bytes, razão e quantidade **testados**; UI responsiva não medida |
| Destino | remover unidade durante extração | mapeado para `DestinationUnavailable`; **sem teste** |
| Recuperação | encerrar processo durante operação | staging identificável por manifesto; limpeza em sessões seguintes **não implementada** |
| Interface | DPI, tela pequena, movimento reduzido | **não testado** (sem Windows) |
