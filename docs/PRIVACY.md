# Privacy policy / Política de privacidade

**English.** This program will not transfer any information to other networked systems unless specifically requested by
the user or the person installing or operating it.

- **No telemetry, no analytics, no accounts.** Files, folder names, search terms, passwords and settings never leave the PC.
- **Local data only:** settings (including the names and searches you typed, kept for keyboard suggestions until you turn them off, and the folders of your open tabs, kept to restore them until you turn that off; never passwords), history, where each video stopped (only a SHA-256 digest of the file's path, size and date plus the seconds — never the name; Configurações → Apagar onde os vídeos pararam erases them), controller profiles and logs live in `%LOCALAPPDATA%\ControlFS` (installed) or
  in `ControlFS_Data` next to `ControlFS-Portable-x64.exe` (portable). Passwords are never saved or logged.
- **Update check (optional):** the app asks GitHub's servers (the GitHub API and release downloads) whether a newer release exists, at most
  once a day, and downloads it only from the project's GitHub releases. The request carries only a `ControlFS/<version>`
  User-Agent; GitHub sees the IP address like for any web request ([GitHub privacy statement](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement)).
  Turn it off in Menu → Atualizações → "Verificar automaticamente"; a manual "Verificar agora" still works on request.
- **Network locations:** ControlFS lists the mapped drives and network locations Windows already knows (read locally) and
  only contacts a server when you open that place. It never stores network credentials.
- **Phone as a controller (optional, #223):** only when you choose Menu → Conectar celular, ControlFS listens on one
  private address of your local network (never the internet) for up to 2 minutes, for one phone. The phone and the PC
  talk directly over your Wi-Fi/wired network, encrypted with a key that is created for that pairing and never leaves the
  QR code; no cloud service, account or relay is involved and nothing is stored. The phone only sends navigation
  actions and the text you type; the PC only sends back whether a text field is open. It stops listening when you
  disconnect, when the phone disconnects or when ControlFS closes. Windows Firewall may ask for permission the first time.
- **"Mais da equipe" (More from the team):** one screen, shown once, with two of the team's apps (NextBoost PRO, Console Mode).
  Their logos ship inside the package and nothing is downloaded. A link (`https://nextboost.pro/`,
  `https://github.com/lippdev/consolemode`) opens in your default browser only when you choose it; those sites then see your
  visit like any web request. Nothing is sent by ControlFS itself.
- **Opening files with Windows** ("Abrir com…", default programs, File Explorer) hands the file to programs the user
  chooses; what they do is governed by their own policies.

**Português.** Este programa não transfere nenhuma informação para outros sistemas em rede, a menos que seja pedido
especificamente pelo usuário ou por quem o instala ou opera.

- **Sem telemetria, sem estatísticas de uso, sem contas.** Arquivos, nomes de pastas, termos de busca, senhas e
  preferências nunca saem do PC.
- **Só dados locais:** preferências (inclusive nomes e buscas digitados, guardados para as sugestões do teclado até você desligá-las, e as pastas das abas abertas, guardadas para restaurá-las até você desligar; nunca senhas), histórico, onde cada vídeo parou (só um resumo SHA-256 do caminho, tamanho e data do arquivo e os segundos — nunca o nome; Configurações → Apagar onde os vídeos pararam apaga), perfis de controle e logs ficam em `%LOCALAPPDATA%\ControlFS` (instalado)
  ou em `ControlFS_Data` ao lado do `ControlFS-Portable-x64.exe` (portátil). Senhas nunca são salvas nem registradas.
- **Verificação de atualizações (opcional):** o app pergunta aos servidores do GitHub (API e downloads das releases) se há versão nova,
  no máximo uma vez por dia, e só baixa das releases do projeto no GitHub. O pedido leva apenas o User-Agent
  `ControlFS/<versão>`; o GitHub vê o endereço IP como em qualquer acesso à web. Desligue em Menu → Atualizações → "Verificar automaticamente"; "Verificar agora" continua funcionando quando pedido.
- **Locais de rede:** o ControlFS lista as unidades mapeadas e os locais de rede que o Windows já conhece (lidos
  localmente) e só contata um servidor quando você abre aquele local. Não guarda credenciais de rede.
- **Celular como controle (opcional, #223):** só quando você escolhe Menu → Conectar celular, o ControlFS escuta num
  endereço privado da sua rede local (nunca a internet) por até 2 minutos, para um celular. Celular e PC conversam direto
  pela sua rede Wi-Fi/cabo, cifrados com uma chave criada para aquele pareamento que só existe no QR Code; não há serviço
  na nuvem, conta nem retransmissor, e nada fica guardado. O celular só manda ações de navegação e o texto que você digita;
  o PC só responde se há um campo de texto aberto. Para de escutar ao desconectar, quando o celular desconecta ou ao
  fechar o ControlFS. Na primeira vez o Firewall do Windows pode pedir permissão.
- **"Mais da equipe":** uma tela, mostrada uma vez, com dois aplicativos da equipe (NextBoost PRO e Console Mode). Os logos
  vêm dentro do pacote e nada é baixado. Um link (`https://nextboost.pro/`, `https://github.com/lippdev/consolemode`) só abre
  no navegador padrão quando você escolhe; esses sites então veem a sua visita como em qualquer acesso à web. O ControlFS
  não envia nada.
- **Abrir arquivos com o Windows** ("Abrir com…", programa padrão, Explorador de Arquivos) entrega o arquivo a programas
  escolhidos pelo usuário, que seguem as próprias políticas.
