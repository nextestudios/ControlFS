# Fixtures

Arquivos pequenos, controlados e com origem documentada. Nenhum contém dados reais de usuários.

| Arquivo | Como foi gerado | Uso |
|---|---|---|
| `rar/Rar4.rar`, `rar/Rar5.rar`, `rar/Rar.solid.rar`, `rar/Rar5.solid.rar`, `rar/Rar5.encrypted_filesOnly.rar` (senha `test`), `7z/7Zip.LZMA2.7z`, `7z/7Zip.solid.7z`, `7z/7Zip.LZMA2.Aes.7z` (senha `testpassword`) | Copiados sem alteração de `tests/TestArchives/Archives/` do SharpCompress, commit `b6cc95af73950c914e0c6ce7ea3511528ede1121`, licença MIT (Copyright (c) 2014 Adam Hathcock). Contêm os mesmos três arquivos de `tests/TestArchives/Original` (`exe/test.exe`, `jpg/test.jpg`, `тест.txt`); os testes conferem o SHA-256 de cada arquivo extraído contra esses originais. Nada extraído é executado. | RAR4/RAR5, sólidos, 7z LZMA2/sólido, senhas. |
| `rar/Rar5.encrypted_filesAndHeader.rar` (senha `test`) | Copiado sem alteração de `tests/TestArchives/Archives/` do SharpCompress, commit `b6cc95af73950c914e0c6ce7ea3511528ede1121`, licença MIT. RAR5 com cabeçalhos **e** arquivos criptografados, com os mesmos três arquivos originais acima (SHA-256 conferido). | Lista protegida: senha ausente, incorreta e correta. |
| `7z/cabecalho-protegido.7z` (senha `certa`) | `7zz a -t7z -mhe=on -pcerta -mx=9 cabecalho-protegido.7z segredo.txt docs` (7-Zip 25.01 para macOS, da 7-zip.org) sobre `segredo.txt` ("conteúdo protegido\n") e `docs/nota.txt` ("nota\n"). Cabeçalho codificado com LZMA2 + 7zAES (lista protegida) e arquivos com LZMA2 + 7zAES. 286 bytes. | Lista protegida: senha ausente, incorreta e correta. |
| `rar/Rar5.multi.part01.rar`–`part06.rar`, `rar/Rar4.multi.part01.rar`–`part07.rar` | Copiados sem alteração de `tests/TestArchives/Archives/` do SharpCompress, commit `b6cc95af73950c914e0c6ce7ea3511528ede1121`, licença MIT. RAR5 e RAR4 em volumes, com os mesmos três arquivos originais acima (SHA-256 conferido). | Volumes: abrir por qualquer parte, parte ausente. |
| `7z/volumes.7z.001`–`.003` | `7zz a -t7z -mx=9 -v400b volumes.7z volumes.txt docs` (7-Zip 25.01 para macOS) sobre `volumes.txt` (1.600 linhas `linha NNNNN: volumes divididos do ControlFS`, 70.400 bytes) e `docs/leia.txt` ("conteúdo do segundo arquivo\n"). 911 bytes no total. | Volumes 7z. |
| `zip/volumes.z01`, `zip/volumes.zip` | `zip -q -X -0 -s 64k -r volumes.zip volumes.txt docs` (Info-ZIP 3.0 do macOS; 64 KB é o menor volume que ele aceita) sobre os mesmos dois arquivos. | ZIP dividido (`.z01` + `.zip`). |
| `zip/Zip.deflate.WinzipAES.zip` (senha `test`) | Copiado sem alteração de `tests/TestArchives/Archives/` do SharpCompress, commit `b6cc95af73950c914e0c6ce7ea3511528ede1121`, licença MIT. WinZip AES-256 (AE-2, CRC 0), Deflate, com os mesmos três arquivos originais acima (SHA-256 conferido). | ZIP AES: senha ausente, incorreta e correta. |
| `zip/zipcrypto-senha-certa.zip` | `zip -q -r -X -P certa` (Info-ZIP 3.0 do macOS) sobre `segredo.txt` ("conteúdo protegido\n") e `docs/nota.txt` ("nota\n"). Senha: `certa`. Criptografia ZipCrypto (tradicional). | Senha ausente, incorreta e correta. |

Demais fixtures (ZIP simples, nomes maliciosos, symlink Unix, colisões, bombas de expansão, CRC corrompido,
truncamento) são **geradas durante os testes** em diretórios temporários por `ZipFixtures` (tests/ControlFS.UnitTests),
usando `System.IO.Compression` do BCL, para manter o repositório pequeno e o conteúdo auditável.

TAR, TAR.GZ e GZ também são gerados durante os testes (`System.Formats.Tar`/`GZipStream`). ZIP64 (entrada acima de 4 GiB e
70.000 entradas) é gerado em `Zip64Tests` com o BCL, em diretório temporário apagado ao final: nada grande vai para o
repositório. ZIP AES-128 (AE-1) e AES-192 (AE-2) são gerados por `AesZipFixtures` seguindo a especificação pública da
WinZip (PBKDF2-HMAC-SHA1, AES-CTR, HMAC-SHA1). ZIP e TAR.GZ cortados em pedaços (`.zip.001`, `.tar.gz.001`) também são
gerados no teste, e os volumes RAR com nomes antigos (`.rar`, `.r00`…) são cópias renomeadas de `Rar5.multi`.
