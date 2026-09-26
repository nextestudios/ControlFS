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
| Start | Menu do app | OK |
| Select | (busca, ainda não) | Símbolos |

**Voltar** fecha primeiro o menu aberto, depois limpa a seleção, depois volta no histórico e por fim vai à tela inicial. Sair do app sempre pede confirmação, começando em "Cancelar".

Só um controle comanda o app por vez: o primeiro a apertar um botão. Com a janela em segundo plano, a entrada é ignorada. O menu tem "Confirmar com: botão inferior/direito" e o estilo das legendas (genérico, Xbox, PlayStation, Nintendo).

Toda ação essencial está nos menus (Start / Norte), então um controle só com direcional e dois botões continua funcionando.

## Extraindo

- **Extrair para "nome"**: cria uma pasta nova ao lado do arquivo (nunca reaproveita uma existente: "nome (2)").
- **Extrair aqui**: na pasta do arquivo; conflitos de nome perguntam.
- **Extrair para…**: escolha uma pasta dentro do app (dá para criar uma ali).
- Dentro de um compactado aberto, marque entradas com Oeste e use **Extrair seleção**.

Antes de começar você vê origem, destino, entradas e política de conflitos. Conflitos começam em **Pular (manter existente)**; **Substituir** pergunta de novo. O compactado nunca é apagado e nada extraído é aberto ou executado.

Entradas bloqueadas (nomes inseguros como `../`, links, nomes reservados do Windows) aparecem com ⚠ e o motivo.

## Atualizações

A versão **instalada** se atualiza sozinha:

1. No máximo uma vez por dia, pergunta ao GitHub qual é a última release de `nextestudios/ControlFS` (só estáveis, ou também pré-lançamentos se você estiver num).
2. Baixa o instalador em segundo plano e só o aceita se o **manifesto da release estiver assinado com a chave do projeto** e o **SHA-256 e o tamanho** do arquivo conferirem. Versões iguais ou anteriores são recusadas.
3. Oferece **Instalar e reiniciar**. Se você adiar, instala em silêncio ao sair. Nada acontece enquanto uma cópia ou extração estiver em andamento.

Menu → **Atualizações**: verificar agora, verificação automática sim/não, instalar ao sair sim/não, pré-lançamentos (automático / sim / não).
A versão **portátil** só avisa que existe versão nova; baixe-a na página da release.

## Privacidade

Tudo fica no seu PC. As preferências ficam em `%LOCALAPPDATA%\ControlFS\settings.json`. Senhas nunca são gravadas nem registradas. As funções centrais nunca usam a rede. O único acesso à rede é a verificação de atualizações, que envia ao GitHub apenas o User-Agent `ControlFS/<versão>` e pode ser desligada.

## Solução de problemas

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

O app roda só no Windows 11 x64. Instalador + portátil (requer Inno Setup 6): `.\build\Publish-ControlFS.ps1 -Version 0.1.0-alpha.2`.
Mais em [build-and-release.md](build-and-release.md).
