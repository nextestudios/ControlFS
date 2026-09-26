# 0004 — Motor de extração e políticas próprias de segurança

- **Data:** 2026-09-26
- **Estado:** aceita

## Decisão

SharpCompress 1.0.0 é usado **apenas** para listar entradas e fornecer fluxos descompactados. Toda decisão de destino,
nome, link, colisão, limite, staging, conflito e integridade é do produto (`SafeExtractor`, `ArchivePathPolicy`,
`DestinationGuard`). Nunca usamos `WriteToDirectory` do motor.

## Comportamentos do motor observados nesta sessão (fixtures reais)

| Caso | Observado |
|---|---|
| ZipCrypto sem senha | `CryptographicException: No password supplied for encrypted zip.` |
| ZipCrypto senha errada | `CryptographicException: The password did not match.` |
| ZIP com symlink (Info-ZIP `-y`) | `LinkTarget` **nulo**; o tipo só aparece em `Attrib >> 16` (`0xA1ED`). Detecção própria implementada. |
| Nomes `../x`, `C:\x`, `x:Zone.Identifier`, `CON.txt` | Entregues crus em `Key`. Contenção é responsabilidade nossa. |
| Byte de conteúdo alterado | Nenhum erro do motor ao ler. **CRC32 próprio** implementado e testado. |
| Arquivo truncado | `ArchiveException: Failed to locate the Zip Header`. |

Documentação do motor no commit do pacote (`FORMATS.md`, `b6cc95af`): ZIP suporta PKWARE e WinZip AES (LZMA criptografado
não); RAR sólido apenas via `RarReader` (sequencial); 7z apenas via Archive API. Por isso `IArchiveEngine` não impõe uma
estratégia única de leitura.

## Alternativas

Escrever descompactador próprio: proibido pela especificação e desnecessário. Outro motor só será avaliado diante de uma
limitação demonstrada (ex.: variante RAR/7z exigida pela matriz 1.0 e não suportada).
