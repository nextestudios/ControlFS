<h1 align="center"><img src="logos/controlfs-logo-text.png" alt="ControlFS" width="560"></h1>

Um **gerenciador de arquivos para Windows feito para o controle**, com **extrator integrado**: navegue, organize e descompacte arquivos do sofá, num PC ligado à TV ou num portátil Windows. Código aberto (AGPL-3.0-only), local, sem login, sem telemetria.

🇺🇸 [Read in English](README.md)

![Windows](https://img.shields.io/badge/Windows-11%20x64-blue)
![Versão](https://img.shields.io/github/v/release/nextestudios/ControlFS?include_prereleases&label=vers%C3%A3o&color=brightgreen)
![Licença](https://img.shields.io/badge/licen%C3%A7a-AGPL--3.0--only-blue)
![CI](https://github.com/nextestudios/ControlFS/actions/workflows/ci.yml/badge.svg)

> **Pré-alfa.** A primeira jornada funciona e é coberta por testes automatizados sobre arquivos reais, mas o app **ainda não foi validado em Windows com controles físicos**. Veja o [PROGRESS.md](PROGRESS.md).

## Download

Baixe a **[0.4.0-alpha.1](https://github.com/nextestudios/ControlFS/releases/tag/v0.4.0-alpha.1)** (pré-lançamento):

- **`ControlFS-Setup-x64.exe`** (recomendado): instala por usuário, sem admin, e **se atualiza sozinho** (atualizações assinadas e verificadas).
- **`ControlFS-Portable-x64.exe`**: um único executável que guarda os dados na pasta `ControlFS_Data` ao lado dele; avisa de novas versões, a troca é manual.

Windows 11 x64. Ainda sem assinatura de código, então o SmartScreen pode avisar ([política](docs/CODE_SIGNING.md)). Está na 0.1.0-alpha.1 ou alpha.2? Elas não abriam: rode o instalador uma vez; daí em diante as atualizações são automáticas.

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

- Pastas e unidades reais, histórico, ordenação, itens ocultos, marcação (marcar todos / limpar), propriedades, **pastas favoritas** no Início e **barra de caminho navegável**
- **Legendas do controle** de acordo com o que está na sua mão (Xbox, PlayStation, Nintendo, genérico), glifos vetoriais originais, rodapé por contexto e **assistente de mapeamento** para joysticks sem perfil
- **Layout responsivo** para portáteis 720p/800p, desktop e TVs 1080p/4K
- **Tentar de novo** uma operação que falhou ou só os itens que falharam; restos de operações interrompidas são limpos na próxima abertura
- **Ícones nativos do Windows** para arquivos, pastas, pastas especiais e unidades, carregados em segundo plano e no tamanho da escala da tela
- **Operações de arquivo:** renomear, copiar, recortar, colar, mover e excluir para a Lixeira, com conflitos (pular / manter ambos / substituir / mesclar pastas) e resultado por item
- **Teclado virtual** próprio (português/inglês, acentos, símbolos, cursor visível, segurar para repetir, senha mascarada) usável só com direções + confirmar + voltar
- **Busca por nome** na pasta atual, com ou sem subpastas: resultados aparecem enquanto são encontrados, dá para cancelar e abrir na pasta; sem índice, links nunca seguidos
- **Criar pasta** com as regras de nomes do Windows
- **Compactados:** navegar em ZIP, 7z, RAR, TAR, TAR.GZ e GZ sem extrair; extrair tudo ou uma seleção; senhas; conflitos (pular / manter ambos / substituir com confirmação); progresso e resultado por item
- **Compactar** em ZIP ou TAR.GZ a partir dos itens marcados, com o nome digitado no teclado virtual
- **Abrir com o Windows:** programa padrão, "Abrir com…", "Mostrar no Explorador de Arquivos"; programas e scripts pedem confirmação
- **Atualizações automáticas e verificadas** (versão instalada): verificação diária, download em segundo plano, "Instalar e reiniciar" ou instalar ao sair; manifesto assinado + SHA-256; desligável ([como funciona](docs/GUIDE.pt-BR.md#atualizações))
- **Extração segura:** nada é gravado fora do destino, links são bloqueados, colisões de nome são recusadas, limites de tamanho, staging temporário, verificação CRC ([modelo de segurança](docs/security-model.md))

**Extrair:** ZIP, 7z, RAR4/RAR5, TAR, TAR.GZ, GZ. **Criar:** ZIP, TAR.GZ. RAR não pode ser criado (formato proprietário); criar 7z, volumes divididos, ZIP AES e ZIP64 ainda precisam de validação ([matriz](docs/archive-support.md)).

## Roadmap

**Próximos:** validação com controles reais (#78) e depois os itens *Should*: pastas recentes, abas, histórico e desfazer, grade, pré-visualização, ZIP64/AES ([roadmap por prioridade MoSCoW](https://github.com/nextestudios/ControlFS/issues/95)) · **Depois:** dois painéis, volumes divididos, tema claro. Detalhes em [docs/roadmap.md](docs/roadmap.md).

## Mais

- [Guia](docs/GUIDE.pt-BR.md): controles, extração, privacidade, solução de problemas, compilar
- **Feedback:** [este formulário](https://github.com/nextestudios/ControlFS/issues/new?template=feedback.yml)
- Histórico: [português](CHANGELOG.md) · [English](CHANGELOG.en-US.md)

Licença [GNU Affero General Public License v3.0 only](LICENSE) ([notas sobre a licença](docs/LICENSING.md)) · [Avisos de terceiros](THIRD_PARTY_NOTICES.md) · [Política de assinatura de código](docs/CODE_SIGNING.md) · [Segurança](SECURITY.md)
