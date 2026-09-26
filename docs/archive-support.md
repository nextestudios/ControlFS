# Suporte a compactados — matriz real

Motor: SharpCompress **1.0.0** (commit `b6cc95af`). "Validado" = coberto por teste automatizado que passou nesta sessão
(macOS arm64, .NET 10.0.401). Validação em Windows está pendente para todos os itens.

| Formato | Método | Criptografia | Volumes | Fixture | Resultado | Limitação conhecida |
|---|---|---|---|---|---|---|
| ZIP | Stored, Deflate | — | único | gerada no teste (BCL) | **validado**: listar, extrair tudo, extrair seleção, pasta dedicada, conflitos | — |
| ZIP | Deflate | ZipCrypto (PKWARE) | único | `tests/Fixtures/zip/zipcrypto-senha-certa.zip` | **validado**: sem senha → `PasswordRequired`; errada → `WrongPassword`; certa → conteúdo correto | ZipCrypto pode aceitar senha errada em ~1/256 dos casos; aí o CRC falha e o erro é "senha incorreta **ou** dados corrompidos". |
| ZIP | — | WinZip AES | único | **nenhuma** | não testado | Obrigatório na 1.0. Entradas AES (AE-2) declaram CRC 0; nossa verificação CRC não se aplica a elas. |
| ZIP64 | — | — | único | **nenhuma** | não testado | Obrigatório na 1.0. |
| ZIP | — | — | multivolume | nenhuma | não suportado | `CanReadMultiVolume = false`. |
| ZIP | integridade | — | — | payload com byte alterado | **validado**: CRC32 próprio detecta; nenhum arquivo parcial fica no destino | O motor não detectou a alteração sozinho. |
| ZIP | truncado | — | — | gerada no teste | **validado**: `Corrupt` | — |
| 7z | LZMA/LZMA2, sólido | — | — | nenhuma | detectado pelo conteúdo, **não suportado** (mensagem explícita) | Etapa 2. Motor: somente Archive API. |
| RAR 4 / RAR 5 | — | — | — | nenhuma | detectado, **não suportado** | Etapa 2. RAR sólido só via `RarReader` (sequencial). |
| TAR, TAR.GZ/TGZ | — | — | — | nenhuma | TAR detectado por `ustar`; **não suportado** | Etapa 2. |
| GZ | — | — | — | nenhuma | detectado, **não suportado** | Etapa 2; não será exibido como pasta. |

## Capacidades expostas por arquivo (ZIP, após inspeção)

`CanList`, `CanExtractAll`, `CanExtractSelection`, `CanReadEncryptedPayload`, `CanVerifyIntegrity` (CRC durante a
extração), `CanCancelCooperatively` = verdadeiro. `CanReadEncryptedHeaders`, `CanReadMultiVolume`, `CanPauseInSession`,
`CanResumeAfterRestart`, `CanCreate` = falso. "Pausar" não é oferecido na UI.

## Ainda não implementado

"Verificar integridade" como ação separada, extrair para o outro painel (depende do modo de dois painéis), vários
compactados de uma vez (uma pasta por arquivo), worker de extração em processo separado.
