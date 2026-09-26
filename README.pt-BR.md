# ControlFS

Um **gerenciador de arquivos para Windows feito para o controle**, com **extrator integrado**: navegue, organize e descompacte arquivos do sofá, num PC ligado à TV ou num portátil Windows. Código aberto, local, sem login, sem telemetria.

🇺🇸 [Read in English](README.md)

![Windows](https://img.shields.io/badge/Windows-11%20x64-blue)
![Versão](https://img.shields.io/github/v/release/nextestudios/ControlFS?include_prereleases&label=vers%C3%A3o&color=brightgreen)
![Licença](https://img.shields.io/github/license/nextestudios/ControlFS)
![CI](https://github.com/nextestudios/ControlFS/actions/workflows/ci.yml/badge.svg)

> **Pré-alfa.** A primeira jornada funciona e é coberta por testes automatizados sobre arquivos reais, mas o app **ainda não foi validado em Windows com controles físicos**. Veja o [PROGRESS.md](PROGRESS.md).

## Download

Baixe a **[0.1.0-alpha.1](https://github.com/nextestudios/ControlFS/releases/tag/v0.1.0-alpha.1)** (pré-lançamento):

- **`ControlFS-Portable-x64.zip`**: descompacte em qualquer pasta e rode o `ControlFS.exe`. Windows 11 x64. Ainda sem assinatura de código ([política](docs/CODE_SIGNING.md)).

## Como funciona

1. Abra o app: a tela inicial lista suas pastas (Downloads, Documentos…) e unidades.
2. Navegue com o direcional ou o analógico; **Sul** abre, **Leste** volta, **Norte** mostra as ações.
3. Num `.zip`, **Sul** abre em modo somente leitura; **Norte → Extrair** descompacta numa pasta dedicada, aqui, ou numa pasta escolhida dentro do app.

| Controle (posição) | Faz | Teclado |
|---|---|---|
| Direcional / analógico esquerdo | Mover | Setas |
| Sul (A / ✕) | Abrir / confirmar | Enter |
| Leste (B / ○) | Voltar / fechar | Esc |
| Oeste (X / □) | Marcar item | Espaço |
| Norte (Y / △) | Ações do item | F2 |
| LT / RT | Página anterior / próxima | PgUp / PgDn |
| Start | Menu do app | F10 |
| — | Tela cheia | F11 |

Os botões seguem a **posição física**, então um controle Nintendo não inverte confirmar e voltar. Dá para trocar para "confirmar com o botão direito" no menu.

## Recursos

- Pastas e unidades reais, histórico, ordenação, itens ocultos, marcação, propriedades
- **Teclado virtual** próprio (português/inglês, acentos, símbolos, cursor, senha mascarada) usável só com direções + confirmar + voltar
- **Criar pasta** com as regras de nomes do Windows
- **ZIP:** navegar sem extrair; extrair tudo ou uma seleção; senha (ZipCrypto); conflitos (pular / manter ambos / substituir com confirmação); progresso e resultado por item
- **Extração segura:** nada é gravado fora do destino, links são bloqueados, colisões de nome são recusadas, limites de tamanho, staging temporário, verificação CRC ([modelo de segurança](docs/security-model.md))

**Formatos hoje: só ZIP.** AES, ZIP64, 7z, RAR, TAR e GZ são detectados e o app avisa que ainda não os suporta ([matriz](docs/archive-support.md)).

## Roadmap

**Próximos:** rodar no Windows com controles reais, ZIP64 e AES, renomear/excluir (Lixeira), copiar/mover · **Depois:** dois painéis, busca, favoritos, 7z/RAR/TAR/GZ, assistente para controles desconhecidos, tema claro. Detalhes em [docs/roadmap.md](docs/roadmap.md).

## Mais

- [Guia](docs/GUIDE.pt-BR.md): controles, extração, privacidade, solução de problemas, compilar
- **Feedback:** [este formulário](https://github.com/nextestudios/ControlFS/issues/new?template=feedback.yml)
- Histórico: [português](CHANGELOG.md) · [English](CHANGELOG.en-US.md)

Licença [MIT](LICENSE) · [Avisos de terceiros](THIRD_PARTY_NOTICES.md) · [Política de assinatura de código](docs/CODE_SIGNING.md) · [Segurança](SECURITY.md)
