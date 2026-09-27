# Suporte a compactados — matriz real

"Validado" = teste automatizado que passou na CI (`windows-latest` e Linux). Motores: SharpCompress **1.0.0**
(`b6cc95af`) para ZIP, 7z, RAR e GZ; `System.Formats.Tar` (.NET 10) para TAR e TAR.GZ — o leitor de TAR do
SharpCompress expunha cabeçalhos PAX como arquivos falsos.

## Extrair

| Formato | Variante | Senha | Fixture | Resultado | Limitação conhecida |
|---|---|---|---|---|---|
| ZIP | Stored, Deflate | — | gerada no teste | **validado** (tudo, seleção, conflitos, CRC, truncado, maliciosos) | — |
| ZIP | Deflate | ZipCrypto | `zip/zipcrypto-senha-certa.zip` | **validado** (sem/errada/certa) | ZipCrypto aceita senha errada ~1/256; aí o CRC acusa "senha ou dados". |
| ZIP | AES (AE-2) | — | nenhuma | não testado | Obrigatório na 1.0. |
| ZIP64 | entrada > 4 GiB (Deflate) e entrada seguinte | — | gerada no teste (≈4,0 GiB descompactados) | **validado** (tamanho exato, CRC-32 conferido, amostras em volta da fronteira de 4 GiB) | Não testado: arquivo compactado com mais de 4 GiB em disco (deslocamentos ZIP64). |
| ZIP64 | 70.000 entradas (registro final ZIP64) | — | gerada no teste | **validado** (lista todas; extrai entradas além da 65.535ª; limite de entradas continua valendo) | Extrair tudo não foi medido (70 mil arquivos no runner). |
| 7z | LZMA2 | — | `7z/7Zip.LZMA2.7z` | **validado** (SHA-256 de cada arquivo) | — |
| 7z | sólido | — | `7z/7Zip.solid.7z` | **validado** | Extrair seleção de um sólido pode ser lento. |
| 7z | LZMA2 + AES | `testpassword` | `7z/7Zip.LZMA2.Aes.7z` | **validado** (sem senha → pede; com senha → conteúdo correto) | — |
| RAR | RAR4, RAR5 | — | `rar/Rar4.rar`, `rar/Rar5.rar` | **validado** | — |
| RAR | sólido (RAR4 e RAR5) | — | `rar/Rar.solid.rar`, `rar/Rar5.solid.rar` | **validado** | — |
| RAR | RAR5, arquivos criptografados | `test` | `rar/Rar5.encrypted_filesOnly.rar` | **validado** (sem → pede; errada → recusa; certa → conteúdo correto) | RAR5 criptografado guarda o CRC transformado pela chave: a verificação CRC própria não se aplica. |
| RAR/7z | lista protegida (cabeçalhos criptografados) | — | nenhuma | implementado (o app pede a senha para listar), **não validado por fixture** | — |
| TAR | ustar/PAX/GNU | — | gerada no teste | **validado** (links bloqueados pelo tipo, `../` recusado, nomes acentuados) | Sem CRC por entrada. |
| TAR.GZ | PAX | — | gerada no teste | **validado** (inclui extração de seleção em uma passada) | Leitura sequencial: listar descomprime o arquivo inteiro. |
| GZ | arquivo único | — | gerada no teste | **validado** (vira um arquivo, não uma pasta) | Tamanho declarado não é usado (módulo 2³²). |
| Volumes divididos (.001, .part1.rar, .z01) | — | — | — | **não suportado** | — |

## Criar

| Formato | Resultado | Observações |
|---|---|---|
| ZIP (Deflate, nomes UTF-8) | **validado** (ida e volta, byte a byte) | Rápida/normal/máxima. |
| TAR.GZ (PAX) | **validado** (ida e volta) | — |
| 7z | não disponível | Exigiria embutir o 7-Zip; decisão futura. |
| RAR | **nunca** | Formato proprietário: só o WinRAR pode criar. |

Links e junctions na origem não são seguidos (listados como ignorados); o compactado é gravado num temporário e só
recebe o nome final ao concluir; um arquivo existente nunca é sobrescrito; cancelar não deixa nada.
