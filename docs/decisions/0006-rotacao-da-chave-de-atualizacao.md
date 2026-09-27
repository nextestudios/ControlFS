# 0006 — Rotação da chave de assinatura das atualizações

- **Data:** 2026-09-27
- **Estado:** aceita (complementa a `0005`)

## Decisão

O app confia num **conjunto** de chaves públicas (`UpdateTrust.Official.PublicKeysPem`, no máximo 4): um manifesto é
aceito se a assinatura conferir com **qualquer** uma delas. Chaves que não são ECDSA P-256 nunca são usadas. O resto
da verificação da `0005` não muda (produto, repositório, versão = tag e > atual, hosts fixos, tamanho e SHA-256).

Para quem ainda não conhece a chave nova, o serviço tenta um **degrau**: se nenhuma chave desta cópia confere a
assinatura da release mais nova, ele confere as anteriores (até 3, sempre mais novas que a versão instalada) e oferece
a mais nova que conseguir verificar — a release de transição, assinada com a chave antiga e que já traz a nova. Só a
falta de chave conhecida pula uma release; manifesto adulterado ou versão trocada recusam a verificação inteira.
Nunca há downgrade.

A CI (`build/New-ReleaseManifest.ps1`) confere a assinatura contra todas as chaves de `UpdateTrust.cs` e imprime a
impressão digital da que assinou; falha se nenhuma conferir.

Testes: `UpdateServiceTests::Manifest_signed_by_either_trusted_key_is_accepted_and_any_other_key_is_refused`,
`::After_a_key_rotation_an_old_copy_takes_the_newest_release_it_can_verify_and_never_an_older_one` e
`::Official_keys_are_distinct_p256_keys_and_still_include_the_one_every_installed_copy_trusts` (trava: a chave original
não sai da lista por engano).

## Procedimento do mantenedor

Nunca gere, copie ou guarde a chave privada dentro do repositório, em issues, logs ou artefatos. Use uma máquina
confiável e apague os arquivos temporários ao final.

### Gerar uma chave (PowerShell 7)

```powershell
$key = [Security.Cryptography.ECDsa]::Create([Security.Cryptography.ECCurve+NamedCurves]::nistP256)
$key.ExportPkcs8PrivateKeyPem() | Set-Content -NoNewline "$HOME\controlfs-update-key-NOVA.pem"   # privada: fora do repo
$key.ExportSubjectPublicKeyInfoPem()                                                              # pública: vai para UpdateTrust.cs
[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($key.ExportSubjectPublicKeyInfo())).ToLowerInvariant()  # impressão digital
```

Guarde a privada num cofre de senhas **e** numa cópia offline. Ela só entra no GitHub como secret:
`gh secret set UPDATE_SIGNING_KEY -R nextestudios/ControlFS < "$HOME\controlfs-update-key-NOVA.pem"`.

### Recomendado já: chave reserva

Gere uma segunda chave agora e publique **só a pública** em `UpdateTrust.cs` (com a impressão digital no comentário),
guardando a privada offline. Se a chave em uso for **perdida**, a reserva assina a próxima release e toda cópia que já
tinha a reserva continua atualizando sozinha. Sem reserva, perder a chave obriga uma reinstalação manual.

### Rotação planejada (chave não comprometida)

1. PR: acrescentar a pública nova em `UpdateTrust.Official` (depois da atual) e a impressão digital no comentário.
2. Publicar a release **de transição** N com o secret ainda com a chave antiga. Ela confia nas duas.
3. Esperar a adoção (pelo menos um ciclo de releases; quem abre o app 1×/dia atualiza em horas).
4. Trocar o secret `UPDATE_SIGNING_KEY` pela privada nova. A release N+1 sai assinada com ela: quem está em N ou depois
   aceita; quem tem este mecanismo mas ainda está antes de N recebe N (degrau) e depois N+1. Cópias anteriores a este
   mecanismo (0.8.x e antes) só olham a mais nova: se ainda existirem, precisam de instalação manual — por isso não
   pule o passo 3.
5. Depois de algumas releases, um PR remove a pública antiga e atualiza a impressão digital fixada no teste.

### Chave vazada

Quem tem a chave antiga consegue assinar atualizações para toda cópia que confia nela; não há como revogar à distância.

1. Gerar a chave nova. Publicar imediatamente a release N assinada com a chave **antiga** (é a única que as cópias
   instaladas aceitam) trazendo **só** a pública nova (remover a antiga; atualizar a impressão digital fixada no teste).
2. Trocar o secret pela privada nova; toda release seguinte usa só ela.
3. Avisar no README/release que versões anteriores a N devem atualizar já (ou reinstalar pelo instalador baixado do
   GitHub). Com uma **chave reserva** publicada antes do vazamento, a release N pode ser assinada pela reserva.

### Chave perdida

Sem reserva: gerar chave nova, publicar uma release com ela e pedir instalação manual dessa versão. Com reserva: assinar
com a reserva (vira a chave em uso) e publicar uma nova reserva na mesma release.

## Riscos

- O degrau depende de a release de transição continuar entre as 20 mais recentes listadas pelo GitHub e entre as 3
  primeiras conferidas; rotações devem ser raras e espaçadas.
- A lista de chaves vem no binário; um processo que já roda como o usuário e altera o executável está fora do modelo
  (ver `0005`).
