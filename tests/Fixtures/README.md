# Fixtures

Arquivos pequenos, controlados e com origem documentada. Nenhum contém dados reais de usuários.

| Arquivo | Como foi gerado | Uso |
|---|---|---|
| `zip/zipcrypto-senha-certa.zip` | `zip -q -r -X -P certa` (Info-ZIP 3.0 do macOS) sobre `segredo.txt` ("conteúdo protegido\n") e `docs/nota.txt` ("nota\n"). Senha: `certa`. Criptografia ZipCrypto (tradicional). | Senha ausente, incorreta e correta. |

Demais fixtures (ZIP simples, nomes maliciosos, symlink Unix, colisões, bombas de expansão, CRC corrompido,
truncamento) são **geradas durante os testes** em diretórios temporários por `ZipFixtures` (tests/ControlFS.UnitTests),
usando `System.IO.Compression` do BCL, para manter o repositório pequeno e o conteúdo auditável.

Pendente: fixture ZIP AES (AE-2), ZIP64, 7z, RAR4/RAR5, TAR, TAR.GZ, GZ — ver `docs/archive-support.md`.
