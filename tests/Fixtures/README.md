# Fixtures

Arquivos pequenos, controlados e com origem documentada. Nenhum contém dados reais de usuários.

| Arquivo | Como foi gerado | Uso |
|---|---|---|
| `rar/Rar4.rar`, `rar/Rar5.rar`, `rar/Rar.solid.rar`, `rar/Rar5.solid.rar`, `rar/Rar5.encrypted_filesOnly.rar` (senha `test`), `7z/7Zip.LZMA2.7z`, `7z/7Zip.solid.7z`, `7z/7Zip.LZMA2.Aes.7z` (senha `testpassword`) | Copiados sem alteração de `tests/TestArchives/Archives/` do SharpCompress, commit `b6cc95af73950c914e0c6ce7ea3511528ede1121`, licença MIT (Copyright (c) 2014 Adam Hathcock). Contêm os mesmos três arquivos de `tests/TestArchives/Original` (`exe/test.exe`, `jpg/test.jpg`, `тест.txt`); os testes conferem o SHA-256 de cada arquivo extraído contra esses originais. Nada extraído é executado. | RAR4/RAR5, sólidos, 7z LZMA2/sólido, senhas. |
| `zip/zipcrypto-senha-certa.zip` | `zip -q -r -X -P certa` (Info-ZIP 3.0 do macOS) sobre `segredo.txt` ("conteúdo protegido\n") e `docs/nota.txt` ("nota\n"). Senha: `certa`. Criptografia ZipCrypto (tradicional). | Senha ausente, incorreta e correta. |

Demais fixtures (ZIP simples, nomes maliciosos, symlink Unix, colisões, bombas de expansão, CRC corrompido,
truncamento) são **geradas durante os testes** em diretórios temporários por `ZipFixtures` (tests/ControlFS.UnitTests),
usando `System.IO.Compression` do BCL, para manter o repositório pequeno e o conteúdo auditável.

TAR, TAR.GZ e GZ também são gerados durante os testes (`System.Formats.Tar`/`GZipStream`). ZIP64 (entrada acima de 4 GiB e
70.000 entradas) é gerado em `Zip64Tests` com o BCL, em diretório temporário apagado ao final: nada grande vai para o
repositório. Pendente: ZIP AES (AE-2) e volumes divididos — ver `docs/archive-support.md`.
