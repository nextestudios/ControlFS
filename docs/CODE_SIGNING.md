# Code signing policy / Política de assinatura de código

**English.** ControlFS releases are **not code-signed yet**. Windows SmartScreen may warn when you run `ControlFS.exe`.
Every release is built by the public GitHub Actions workflow (`.github/workflows/release.yml`) from the tagged commit and
ships a `SHA256SUMS.txt` so you can check the download. We will never ask you to disable Windows protections or to
install a root certificate. When signing is added (e.g. an open-source signing service), only tag builds will be signed,
from CI, with the certificate never stored in this repository.

**Português.** As releases do ControlFS **ainda não têm assinatura de código**. O SmartScreen do Windows pode avisar ao
abrir o `ControlFS.exe`. Toda release é gerada pelo workflow público do GitHub Actions a partir do commit da tag e inclui
`SHA256SUMS.txt` para conferir o download. Nunca pediremos para desativar proteções do Windows nem instalar certificados
raiz. Quando houver assinatura, só builds de tag serão assinados, pela CI, e o certificado nunca ficará neste repositório.
