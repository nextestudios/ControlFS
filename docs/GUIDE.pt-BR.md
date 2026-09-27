# Guia do ControlFS

🇺🇸 [Guide in English](GUIDE.md)

## Controles

Os botões seguem a **posição física** (convenção do SDL3), não as letras impressas.

| Posição | Ação | Teclado virtual |
|---|---|---|
| Direcional / analógico esquerdo | Mover | Mover entre as teclas |
| Sul | Abrir / confirmar | Pressionar tecla |
| Leste | Voltar / fechar | Cancelar sem aplicar |
| Oeste | Marcar item | Apagar |
| Norte | Ações do item | Maiúsculas |
| LB / RB | — | Mover cursor |
| LT / RT | Página anterior / próxima | — |
| Start | Menu do app | Concluir |
| Select | (busca, ainda não) | Símbolos |

**Voltar** fecha primeiro o menu aberto, depois limpa a seleção, depois volta no histórico e por fim vai à tela inicial. Sair do app sempre pede confirmação, começando em "Cancelar".

Só um controle comanda o app por vez: o primeiro a apertar um botão. Apertar um botão em outro controle (com o ativo solto) passa o comando para ele. Com a janela em segundo plano, a entrada é ignorada. O menu tem "Confirmar com: botão inferior/direito" e o estilo das legendas: **automáticas** (padrão: seguem a família do controle em uso: Xbox, PlayStation, Nintendo ou genérico) ou fixas em genérico, Xbox, PlayStation ou Nintendo.

O rodapé mostra os botões do controle em uso (por exemplo `A Abrir` no Xbox, `✕ Abrir` no PlayStation) e troca assim que você usa outro controle. Ao usar o teclado, mostra as teclas (`Enter Abrir`, `Esc Voltar`) até você apertar um botão do controle de novo.

O teclado virtual tem o campo de texto em cima, quatro linhas de caracteres, uma linha de funções (`⇧` Maiúsculas · `ABC` letras · `@#:` símbolos · espaço · `⌫`) e uma linha inferior (cursor `◀ ▶` · `…` mais · Cancelar · **Concluir**). `…` abre os acentos, **Limpar** e a troca PT-BR/EN. Maiúsculas: um toque deixa a próxima letra maiúscula, o segundo trava (`⇪`), o terceiro desliga. Teclas que o campo não aceita (ex.: `\ / : * ? " < > |` em nomes de arquivo) ficam apagadas.

Toda ação essencial está nos menus (Start / Norte), então um controle só com direcional e dois botões continua funcionando.

## Extraindo

Num compactado o rodapé mostra **Sul Explorar** (abre somente leitura), **Oeste Marcar** e **Norte Extrair…**: Norte abre o menu de ações já em **Extrair para "nome"**, então Norte e depois Sul extraem.

- **Extrair para "nome"**: cria uma pasta nova ao lado do arquivo (nunca reaproveita uma existente: "nome (2)").
- **Extrair aqui**: na pasta do arquivo; conflitos de nome perguntam.
- **Extrair para…**: escolha uma pasta dentro do app (dá para criar uma ali).
- Dentro de um compactado aberto, marque entradas com Oeste e use **Extrair seleção**.

Antes de começar você vê origem, destino, entradas e política de conflitos. Conflitos começam em **Pular (manter existente)**; **Substituir** pergunta de novo. O compactado nunca é apagado e nada extraído é aberto ou executado.

Entradas bloqueadas (nomes inseguros como `../`, links, nomes reservados do Windows) aparecem com ⚠ e o motivo.

## Compactando

Marque itens com Oeste (ou foque um) → Norte → **Compactar…**. Escolha o nome (teclado virtual), ZIP ou TAR.GZ e o nível de compressão, e depois **Compactar**. O arquivo é gravado num temporário e só aparece quando termina; um arquivo existente nunca é sobrescrito (o nome ganha "(2)"). Links e junctions dentro das pastas são ignorados e listados no resultado. RAR não pode ser criado (formato proprietário).

## Central de operações

Menu → **Operações** lista cada cópia, movimentação, exclusão, extração e compactação da sessão. Selecione uma para ver o progresso ou o resultado, ou para cancelá-la enquanto roda. Uma operação que **falhou**, **terminou com avisos** ou foi **cancelada** oferece **Tentar de novo**: o ControlFS planeja o pedido original do zero (itens que não existem mais na origem ficam de fora; o que já chegou ao destino passa pelas perguntas de conflito de sempre). Compactados com senha pedem a senha outra vez.

## Abrindo arquivos com o Windows

**Sul** num arquivo que não é compactado abre no programa padrão do Windows. Norte num arquivo oferece também **Abrir com…** e **Mostrar no Explorador de Arquivos**. O outro programa pode não funcionar com o controle: volte com Alt+Tab ou o botão do sistema. Programas e scripts (.exe, .msi, .bat, .ps1, .lnk…) perguntam antes, começando em **Cancelar**. Nada é aberto automaticamente depois de extrair.

## Atualizações

A versão **instalada** se atualiza sozinha:

1. No máximo uma vez por dia, pergunta ao GitHub qual é a última release de `nextestudios/ControlFS` (só estáveis, ou também pré-lançamentos se você estiver num).
2. Baixa o instalador em segundo plano e só o aceita se o **manifesto da release estiver assinado com a chave do projeto** e o **SHA-256 e o tamanho** do arquivo conferirem. Versões iguais ou anteriores são recusadas.
3. Oferece **Instalar e reiniciar**. Se você adiar, instala em silêncio ao sair. Nada acontece enquanto uma cópia ou extração estiver em andamento.

Menu → **Atualizações**: verificar agora, verificação automática sim/não, instalar ao sair sim/não, pré-lançamentos (automático / sim / não).
A versão **portátil** só avisa que existe versão nova; baixe-a na página da release.

## Privacidade

Tudo fica no seu PC. Preferências e logs ficam em `%LOCALAPPDATA%\ControlFS` (instalado) ou em `ControlFS_Data` ao lado do `ControlFS-Portable-x64.exe` (portátil). Senhas nunca são gravadas nem registradas. As funções centrais nunca usam a rede. O único acesso à rede é a verificação de atualizações, que envia ao GitHub apenas o User-Agent `ControlFS/<versão>` e pode ser desligada.

## Solução de problemas

- **O app não abre:** envie `logs\startup.log` e `logs\crash.log` da pasta de dados acima (não contêm senhas nem conteúdo de arquivos).
- **Controle não detectado:** conecte e aperte um botão; o cabeçalho mostra o controle ativo. Teclado e mouse sempre funcionam.
- **Aviso do SmartScreen:** o build ainda não tem assinatura de código ([política](CODE_SIGNING.md)).
- **"Formato reconhecido, mas não suportado":** por enquanto só ZIP.
- **Falha na atualização / "assinatura inválida":** o app recusou um arquivo que não conseguiu verificar. Tente mais tarde ou baixe o instalador na página da release.

## Compilando

Requer o .NET SDK indicado em `global.json`.

```bash
dotnet build ControlFS.slnx
dotnet test ControlFS.slnx
```

O app roda só no Windows 11 x64. Instalador + portátil (requer Inno Setup 6): `.\build\Publish-ControlFS.ps1 -Version 0.1.0-alpha.3`.
Mais em [build-and-release.md](build-and-release.md).
