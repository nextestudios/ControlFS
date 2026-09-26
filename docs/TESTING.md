# Testes manuais

Checklist para o que a CI não consegue provar (Windows real, controles, telas). Marque só o que foi **executado**, com
versão, Windows, controle e conexão. Resultados de controles vão para `controller-compatibility.md`.

## Antes de cada release

- [ ] Baixar `ControlFS-Portable-x64.zip` da release, conferir o SHA-256 com `SHA256SUMS.txt`, descompactar e abrir `ControlFS.exe` numa conta padrão.
- [ ] Tela inicial mostra Downloads/Documentos reais e as unidades.
- [ ] Teclado físico: setas, Enter, Esc, Espaço, F2, F10, F11 (tela cheia).
- [ ] Conectar um controle **depois** de abrir o app: o cabeçalho mostra o controle ao apertar um botão.
- [ ] Segurar Sul ao abrir um diálogo: o diálogo **não** é aceito sozinho.
- [ ] Criar pasta "Relatórios ação" só com o teclado virtual e o controle.
- [ ] Abrir um ZIP, entrar numa subpasta, voltar; foco retorna ao arquivo de origem.
- [ ] Extrair para pasta dedicada, extrair aqui duas vezes (conflito → "Manter ambos"), extrair para… com o seletor interno.
- [ ] ZIP com senha: senha errada pede de novo; senha certa extrai.
- [ ] Desconectar o controle durante uma extração grande: a extração continua; reconectar e seguir navegando.
- [ ] Alt+Tab para outro app: botões do controle não fazem nada no ControlFS até voltar.
- [ ] 1280×720, 1280×800, 1920×1080 e 4K com escala 100–200%: nenhuma ação essencial fica inacessível.
- [ ] Narrador lê o nome dos itens e das teclas do teclado virtual.

## Controles da matriz

Xbox (USB/BT), DualShock 4, DualSense, Switch Pro, 8BitDo, um genérico. Para cada um: navegação, confirmar/voltar na
posição certa, gatilhos (página), Start (menu), reconexão.
