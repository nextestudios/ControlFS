# 0005 — Instalador e atualizações automáticas

- **Data:** 2026-09-26
- **Estado:** aceita

## Decisão

- **Instalador:** Inno Setup 6, por usuário (`PrivilegesRequired=lowest`, pasta `%LOCALAPPDATA%\Programs\ControlFS`),
  PT-BR/EN, atalho no menu Iniciar, atalho na Área de Trabalho opcional. `AppId` fixo
  `{29C18AD1-61FD-4D75-A2F9-1EBA66E7192F}` — nunca mudar. Grava o marcador `ControlFS.installed`, que habilita a
  atualização automática. O pacote portátil continua existindo e só avisa sobre novas versões.
- **Atualização:** o app consulta as releases do GitHub (no máximo 1×/dia, desligável), baixa o instalador em segundo
  plano, confere e oferece "Instalar e reiniciar"; se adiado, instala em silêncio ao sair (desligável). Nunca durante
  operações de disco.

## Autenticidade, integridade e origem (exigência da especificação)

Os binários ainda não têm assinatura de código, então a autenticidade vem de um **manifesto de release assinado**:

1. A CI gera `release-manifest.json` (produto, repositório, versão, nome/tamanho/SHA-256 do instalador e do portátil) e
   o assina com ECDSA P-256/SHA-256 usando a chave do secret `UPDATE_SIGNING_KEY`. O script confere a assinatura contra
   a chave pública embutida no app e falha se não bater.
2. O app aceita a atualização somente se: a assinatura confere com a chave pública embutida (impressão digital
   `8b841b19…c64f3e8`); produto e repositório conferem; a versão do manifesto é igual à da tag **e** maior que a atual
   (sem downgrade nem replay); o arquivo baixado tem exatamente o tamanho e o SHA-256 do manifesto.
3. Origem: HTTPS apenas, hosts fixos (`api.github.com`, `github.com`, `objects.githubusercontent.com`,
   `release-assets.githubusercontent.com`), redirecionamentos seguidos manualmente e conferidos um a um, limites de bytes.
4. Antes de executar, o instalador é conferido de novo com o arquivo aberto sem permissão de escrita para terceiros, e é
   iniciado com `ArgumentList` (sem string de shell).

## Por que não "baixar e executar" nem só SHA256SUMS

Um `SHA256SUMS.txt` servido pelo mesmo lugar que o binário não prova origem: quem trocasse um trocaria o outro. A
assinatura com chave fora do repositório resolve isso sem depender de certificado pago.

## Riscos e pendências

- **Perda da chave privada:** ela existe apenas no secret do GitHub (não há cópia local). Se for perdida ou vazar, é
  preciso gerar outra, publicar uma versão com a nova chave pública e pedir aos usuários uma instalação manual dessa
  versão. Rotação com duas chaves aceitas ainda não implementada.
- **Mesmos privilégios:** um processo malicioso já rodando como o usuário pode alterar o instalador em
  `%LOCALAPPDATA%\ControlFS\updates`; a reconferência antes de executar reduz, mas não elimina, essa janela.
- **Congelamento:** quem bloquear o acesso ao GitHub impede atualizações (não há como forçar).
- **SmartScreen:** sem assinatura de código, o Windows pode alertar na primeira instalação manual.
- Validado por testes automatizados e por instalação/desinstalação silenciosa na CI; o ciclo completo
  "versão antiga instalada → atualiza → reabre" ainda precisa ser executado numa máquina Windows real.
