# Code signing policy / Política de assinatura de código

**English.** ControlFS releases are **not code-signed yet**. Windows SmartScreen may warn when you run `ControlFS.exe`.
Every release is built by the public GitHub Actions workflow (`.github/workflows/release.yml`) from the tagged commit and
ships a `release-manifest.json` with the SHA-256 of both downloads, signed (ECDSA P-256) with the project's update key, which lives only in a GitHub Actions secret. The installed app only accepts an update whose manifest signature and SHA-256 match. We will never ask you to disable Windows protections or to
install a root certificate. The release workflow is ready to sign
(Authenticode, with a timestamp) the installer, the portable exe and the app, and to verify the signatures before
publishing, as soon as the maintainer configures a signing certificate (Microsoft Artifact Signing or a `.pfx`) in a
GitHub Actions environment that requires manual approval. Only tag builds are signed, from CI, and the certificate is
never stored in this repository. Setup: `docs/build-and-release.md` → "Assinatura de código".

**Português.** As releases do ControlFS **ainda não têm assinatura de código**. O SmartScreen do Windows pode avisar ao
abrir o `ControlFS.exe`. Toda release é gerada pelo workflow público do GitHub Actions a partir do commit da tag e inclui
um `release-manifest.json` com o SHA-256 dos dois downloads, assinado (ECDSA P-256) com a chave de atualização do projeto, que existe apenas num secret do GitHub Actions. O app instalado só aceita uma atualização cuja assinatura e SHA-256 confiram. Nunca pediremos para desativar proteções do Windows nem instalar certificados
raiz. O workflow de release já está pronto para assinar (Authenticode, com carimbo de tempo) o instalador, o
portátil e o app, e para conferir as assinaturas antes de publicar, assim que o mantenedor configurar um certificado
(Microsoft Artifact Signing ou `.pfx`) num ambiente do GitHub Actions com aprovação manual. Só builds de tag são
assinados, pela CI, e o certificado nunca fica neste repositório. Configuração: `docs/build-and-release.md` →
"Assinatura de código".

## SignPath Foundation

**English.** ControlFS is applying for free open-source code signing from [SignPath Foundation](https://signpath.org)
(certificate issued to SignPath Foundation, keys kept in SignPath.io's HSM). Roles, as required by their
[conditions](https://signpath.org/terms): committers and reviewers — @nextestudios; approvers — @nextestudios, who
approves every signing request manually. All team members use multi-factor authentication on GitHub and SignPath.
Signed binaries carry `ControlFS` as product name and the release version as product version. Privacy:
[docs/PRIVACY.md](PRIVACY.md). Once the application is accepted, `release.yml` gains a SignPath signing step
(the existing `.pfx`/Artifact Signing paths stay available).

**Português.** O ControlFS está pedindo a assinatura de código gratuita para código aberto da
[SignPath Foundation](https://signpath.org) (certificado emitido para a SignPath Foundation, chaves no HSM da SignPath.io).
Papéis, como exigem as [condições](https://signpath.org/terms): autores e revisores — @nextestudios; aprovadores —
@nextestudios, que aprova manualmente cada pedido de assinatura. Todos no time usam autenticação em dois fatores no GitHub
e na SignPath. Os binários assinados levam `ControlFS` como nome do produto e a versão da release como versão do produto.
Privacidade: [docs/PRIVACY.md](PRIVACY.md). Quando o pedido for aceito, o `release.yml` ganha uma etapa de assinatura pela
SignPath (as opções `.pfx`/Artifact Signing continuam disponíveis).
