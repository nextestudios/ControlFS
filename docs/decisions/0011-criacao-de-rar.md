# 0011 — Criação de RAR pelo Rar.exe que o usuário já tem instalado

- **Data:** 2026-09-29
- **Estado:** aceita (a validação com um WinRAR real está descrita em "Verificação")
- **Issue:** #258

## Contexto e restrições de licença

- O **formato de compressão RAR é proprietário** da RARLAB (win.rar GmbH). Ler RAR é livre para todos (por isso o
  ControlFS extrai RAR4 e RAR5 com o SharpCompress); **criar** RAR só é permitido com o software da RARLAB.
- A licença do UnRAR proíbe usar o código-fonte do UnRAR para recriar o algoritmo de compressão RAR. Fazer engenharia
  reversa do compressor também é vedado.
- Não existe gravador de RAR livre e mantido compatível com a AGPL-3.0-only. SharpCompress e libarchive **só leem** RAR.
- Portanto o ControlFS **não** pode embutir `Rar.exe`/`WinRAR.exe`/DLLs da RARLAB nem escrever o próprio codificador.

## Alternativas avaliadas

| Opção | Veredito | Por quê |
|---|---|---|
| Embutir `Rar.exe` (ou `rar` de Linux) no pacote | **Recusada** | Programa proprietário, redistribuição sem licença; incompatível com distribuir sob AGPL. |
| Codificador RAR próprio (a partir do UnRAR, de documentação ou por engenharia reversa) | **Recusada** | A licença do UnRAR proíbe recriar a compressão; o formato de escrita não é documentado com direito de uso. |
| Contêiner "compatível com RAR" sem compressão (só armazenar) | **Recusada** | Continua sendo escrever o formato RAR sem gravador licenciado; qualquer variação futura de compressão cairia na regra acima. |
| 7-Zip / SharpCompress / libarchive | **Impossível** | Leem RAR, não gravam. |
| Biblioteca .NET de terceiros que grava RAR | **Não existe** livre e mantida | Nenhuma opção compatível com a AGPL foi encontrada. |
| **Chamar o `Rar.exe` do WinRAR que o usuário instalou** | **Escolhida** | O usuário tem a licença do WinRAR; o ControlFS é só um cliente da linha de comando, como um terminal. Nada é distribuído, baixado ou instalado por nós. |

## Decisão

O formato **RAR** aparece no fluxo Compactar **somente quando o WinRAR está instalado**. Sem ele, a entrada fica
desabilitada com o motivo "Instale o WinRAR para criar RAR." O ControlFS nunca baixa nem instala o WinRAR.

### Como o Rar.exe é encontrado (`RarLocator`)

- Só os lugares que o instalador do WinRAR usa: chaves `HKLM\SOFTWARE\WinRAR` (64 e 32 bits) e `HKCU\SOFTWARE\WinRAR`
  (`exe64`/`ExePath`, usando a **pasta** desse caminho) e `%ProgramFiles%\WinRAR` / `%ProgramFiles(x86)%\WinRAR`.
  Nunca varre o disco nem o PATH.
- O arquivo tem de se chamar exatamente `Rar.exe` (o `WinRAR.exe` é a interface gráfica), com caminho completo e local
  (não UNC), e ter **assinatura Authenticode válida** (`WinVerifyTrust`, sem consulta de rede) de um assinante
  "win.rar GmbH". Sem isso, o RAR fica indisponível.

### Como é executado (`RarWriter`)

- `ProcessStartInfo.ArgumentList`, sem shell e sem linha de comando concatenada; entrada padrão fechada.
- Opções fixas: `a -ma5 -m1|-m3|-m5 -r- -y -idq -cfg- -scul <temporário> @<lista>`: RAR5, nível mapeado de
  rápida/normal/máxima, sem recursão própria, silencioso, **ignora `Rar.ini` e a variável `RAR` do usuário**.
  **Nunca `-p-`**: no WinRAR 7.23 ele não significa "sem senha", criptografa com a senha `-` (observado na CI; foi o que o teste com o WinRAR real apanhou). Sem senha, sem registro de recuperação, sem volumes, sem sólido (fora do escopo da issue).
- Os nomes vão numa lista UTF-16 numa pasta temporária privada do usuário (sem limite de linha de comando; um nome nunca é lido
  como opção: nomes que começam com `-` ou `@` recebem o prefixo `.\`). A lista traz cada arquivo e só as pastas realmente
  vazias, a partir da pasta de origem; links e junctions continuam ignorados pelo plano do ControlFS e nunca chegam ao Rar.exe.
  Todos os itens precisam estar na mesma pasta (é o que o fluxo Compactar já faz).
- O Rar.exe grava num temporário `.controlfs-new-*.part` **ao lado do destino** (registrado no diário de sobras, #82);
  ao terminar com sucesso, o resultado precisa começar pela assinatura RAR5 e só então é renomeado para o nome final, sem
  sobrescrever (nome existente: falha; a interface numera "(2)" como nos demais formatos). As origens nunca são alteradas.
- Cancelar mata a **árvore de processos** e apaga o temporário. Um processo que não cresce o arquivo nem imprime nada por
  10 minutos é considerado travado, encerrado e reportado.
- Códigos de saída do WinRAR: 0 = ok; **1 = aviso** (o arquivo é mantido e o resultado é "concluído com avisos", pedindo
  para conferir, porque não sabemos qual item ficou de fora); 2 e maiores = erro (mensagem em pt-BR por código, com a última
  linha da saída do WinRAR sem caracteres de controle), temporário apagado; 255 = interrompido.

## Consequências

- Requer o WinRAR instalado. Sem ele, só ZIP, TAR.GZ e 7z. Essa limitação é dita na interface e na documentação.
- O ControlFS não distribui nada da RARLAB: `THIRD_PARTY_NOTICES.md` registra que o WinRAR é do usuário.
- O progresso é indeterminado (o modo silencioso não informa por arquivo); o resultado traz a contagem no fim.
- Versões do WinRAR que recusem alguma opção falham com o código 7 e a mensagem "esta versão pode não ser compatível".

## Verificação

- Testes de unidade com um `Rar.exe` **falso** (`tests/ControlFS.FakeRar`, imita só a linha de comando): argumentos, lista,
  códigos de saída, cancelamento, travamento, sem sobrescrever e sem sobras.
- Integração no Windows (`RarCreationInteropTests`): cria com o WinRAR real e extrai com o leitor do ControlFS; pulado quando
  o WinRAR não está instalado. O resultado observado na CI está em `docs/archive-support.md`.
- Não validado: versões do WinRAR diferentes da que a CI instala; o teste manual está em `docs/TESTING.md`.
