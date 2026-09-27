# Suporte a compactados — matriz real

"Validado" = teste automatizado que passou na CI (`windows-latest` e Linux). Motores: SharpCompress **1.0.0**
(`b6cc95af`) para ZIP, 7z, RAR e GZ; `System.Formats.Tar` (.NET 10) para TAR e TAR.GZ — o leitor de TAR do
SharpCompress expunha cabeçalhos PAX como arquivos falsos.

## Extrair

| Formato | Variante | Senha | Fixture | Resultado | Limitação conhecida |
|---|---|---|---|---|---|
| ZIP | Stored, Deflate | — | gerada no teste | **validado** (tudo, seleção, conflitos, CRC, truncado, maliciosos) | — |
| ZIP | Deflate | ZipCrypto | `zip/zipcrypto-senha-certa.zip` | **validado** (sem/errada/certa) | ZipCrypto aceita senha errada ~1/256; aí o CRC acusa "senha ou dados". |
| ZIP | Deflate, WinZip AES-256 (AE-2) | `test` | `zip/Zip.deflate.WinzipAES.zip` | **validado** (sem → pede; errada → "Senha incorreta" e pede de novo; certa → SHA-256 idêntico aos originais) | AE-2 guarda CRC 0 e o motor não confere o código de autenticação HMAC: dados adulterados só são percebidos se a descompressão falhar. |
| ZIP | Deflate, WinZip AES-128 (AE-1) e AES-192 (AE-2) | gerada | gerada no teste (`AesZipFixtures`) | **validado** (sem/errada/certa; AE-1 adulterado recusado pelo CRC sem gravar nada) | — |
| ZIP64 | entrada > 4 GiB (Deflate) e entrada seguinte | — | gerada no teste (≈4,0 GiB descompactados) | **validado** (tamanho exato, CRC-32 conferido, amostras em volta da fronteira de 4 GiB) | Não testado: arquivo compactado com mais de 4 GiB em disco (deslocamentos ZIP64). |
| ZIP64 | 70.000 entradas (registro final ZIP64) | — | gerada no teste | **validado** (lista todas; extrai entradas além da 65.535ª; limite de entradas continua valendo) | Extrair tudo não foi medido (70 mil arquivos no runner). |
| 7z | LZMA2 | — | `7z/7Zip.LZMA2.7z` | **validado** (SHA-256 de cada arquivo) | — |
| 7z | sólido | — | `7z/7Zip.solid.7z` | **validado** | Extrair seleção de um sólido pode ser lento. |
| 7z | LZMA2 + AES | `testpassword` | `7z/7Zip.LZMA2.Aes.7z` | **validado** (sem senha → pede; com senha → conteúdo correto) | — |
| RAR | RAR4, RAR5 | — | `rar/Rar4.rar`, `rar/Rar5.rar` | **validado** | — |
| RAR | sólido (RAR4 e RAR5) | — | `rar/Rar.solid.rar`, `rar/Rar5.solid.rar` | **validado** | — |
| RAR | RAR5, arquivos criptografados | `test` | `rar/Rar5.encrypted_filesOnly.rar` | **validado** (sem → pede; errada → recusa; certa → conteúdo correto) | RAR5 criptografado guarda o CRC transformado pela chave: a verificação CRC própria não se aplica. |
| RAR | RAR5, lista protegida (cabeçalhos e arquivos criptografados) | `test` | `rar/Rar5.encrypted_filesAndHeader.rar` | **validado** (listar sem senha → pede; errada → "Senha incorreta"; certa → lista e extrai com SHA-256 idêntico aos originais) | RAR4 com cabeçalhos criptografados não tem fixture: não validado. |
| 7z | LZMA2 + AES, lista protegida (`-mhe=on`) | `certa` | `7z/cabecalho-protegido.7z` | **validado** (listar sem senha → pede; errada → "Senha incorreta" e pede de novo; certa → lista e extrai com conteúdo idêntico) | 7z não tem verificador de senha: uma lista criptografada corrompida também aparece como senha incorreta. |
| TAR | ustar/PAX/GNU | — | gerada no teste | **validado** (links bloqueados pelo tipo, `../` recusado, nomes acentuados) | Sem CRC por entrada. |
| TAR.GZ | PAX | — | gerada no teste | **validado** (inclui extração de seleção em uma passada) | Leitura sequencial: listar descomprime o arquivo inteiro. |
| GZ | arquivo único | — | gerada no teste | **validado** (vira um arquivo, não uma pasta) | Tamanho declarado não é usado (módulo 2³²). |
| 7z | dividido (`.7z.001`…) | — | `7z/volumes.7z.001`–`.003` | **validado** (abre por qualquer volume; sem o primeiro → lista o que falta pelo nome; sem o último → percebido pelo tamanho declarado no cabeçalho 7z) | — |
| RAR | RAR5 e RAR4 em volumes (`.partN.rar`) | — | `rar/Rar5.multi.part01–06.rar`, `rar/Rar4.multi.part01–07.rar` | **validado** (abre por qualquer volume, SHA-256 idêntico; sem o último → percebido pelo bloco final do volume anterior, que diz "há mais volumes") | Volumes com cabeçalhos criptografados: o último ausente só é percebido se algum arquivo ficar pela metade. |
| RAR | nomes antigos (`.rar`, `.r00`, `.r01`…) | — | os volumes RAR5 acima renomeados no teste | **validado** | — |
| ZIP | dividido do Info-ZIP/WinZip (`.z01`… + `.zip`) | — | `zip/volumes.z01`, `zip/volumes.zip` | **validado** (abre pelo `.zip` ou `.z01`; sem um deles → falta apontada pelo nome, usando o número do disco no registro final) | Lido como um ZIP comum (diretório central reescrito em memória, nada é gravado): ZIP64 dividido ou conjunto acima de 4 GiB é recusado como não suportado. |
| ZIP | cortado em pedaços (`.zip.001`…) | — | gerada no teste | **validado** (sem o último → erro de volume ausente sem nome exato: só o motor percebe que a lista final sumiu) | — |
| TAR, TAR.GZ | dividido (`.tar.gz.001`…) | — | gerada no teste | **recusado** ("não suportado"): nada é extraído pela metade | — |

## Testar integridade

Norte → **Testar integridade** lê cada entrada até o fim pelo mesmo caminho da extração (limites, tamanho, CRC) e descarta os dados:
nada é gravado. **Validado** em ZIP (entrada com CRC corrompido apontada pelo nome; as demais conferem; cancelamento no meio
marca o restante como não verificado) e TAR (entradas lidas, marcadas como "sem checksum"). GZ e ZIP AES (AE-2) também não
têm CRC por entrada para conferir. Não é antivírus.

## Criar

| Formato | Resultado | Observações |
|---|---|---|
| ZIP (Deflate, nomes UTF-8) | **validado** (ida e volta, byte a byte) | Rápida/normal/máxima. |
| TAR.GZ (PAX) | **validado** (ida e volta) | — |
| 7z (LZMA, sólido) | **validado** (ida e volta pelo próprio extrator; o 7-Zip do runner Windows testa e extrai byte a byte, nos três níveis; pastas e arquivos vazios preservados) | Sem senha, sem LZMA2 e sem filtros; mais lento que o 7-Zip nativo. Ver `docs/decisions/0008-criacao-de-7z.md`. |
| RAR | **nunca** | Formato proprietário: só o WinRAR pode criar. |

Links e junctions na origem não são seguidos (listados como ignorados); o compactado é gravado num temporário e só
recebe o nome final ao concluir; um arquivo existente nunca é sobrescrito; cancelar não deixa nada.
