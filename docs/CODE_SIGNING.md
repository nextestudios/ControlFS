# Code signing policy / Política de assinatura de código

**English.** ControlFS releases are **not code-signed yet**. Windows SmartScreen may warn when you run `ControlFS.exe`.
Every release is built by the public GitHub Actions workflow (`.github/workflows/release.yml`) from the tagged commit and
ships a `release-manifest.json` with the SHA-256 of both downloads, signed (ECDSA P-256) with the project's update key, which lives only in a GitHub Actions secret. The installed app only accepts an update whose manifest signature and SHA-256 match. We will never ask you to disable Windows protections or to
install a root certificate. When signing is added (e.g. an open-source signing service), only tag builds will be signed,
from CI, with the certificate never stored in this repository.

**Português.** As releases do ControlFS **ainda não têm assinatura de código**. O SmartScreen do Windows pode avisar ao
abrir o `ControlFS.exe`. Toda release é gerada pelo workflow público do GitHub Actions a partir do commit da tag e inclui
um `release-manifest.json` com o SHA-256 dos dois downloads, assinado (ECDSA P-256) com a chave de atualização do projeto, que existe apenas num secret do GitHub Actions. O app instalado só aceita uma atualização cuja assinatura e SHA-256 confiram. Nunca pediremos para desativar proteções do Windows nem instalar certificados
raiz. Quando houver assinatura, só builds de tag serão assinados, pela CI, e o certificado nunca ficará neste repositório.
