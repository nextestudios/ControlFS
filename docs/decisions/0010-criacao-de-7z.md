# 0010 — Criação de 7z com o codificador LZMA do SharpCompress

- **Data:** 2026-09-27
- **Estado:** aceita
- **Issue:** #67

## Decisão

O ControlFS cria `.7z` com código próprio (`Creation/SevenZipWriter`) que grava o contêiner 7z descrito no `7zFormat.txt`
do SDK LZMA (domínio público) e usa, para comprimir, o codificador LZMA que já vem no SharpCompress 1.0.0
(`SharpCompress.Compressors.LZMA.LzmaStream`, porte em C# do SDK LZMA; SharpCompress é MIT e já é dependência).
Nenhuma dependência nova, nenhum binário nativo, nenhum processo externo.

Formato gravado: um bloco sólido LZMA (id `03 01 01`) com todos os arquivos não vazios; cabeçalho final sem compressão
com tamanhos, CRC-32 de cada arquivo, nomes UTF-16 com `/`, data de modificação e atributos (pasta; somente leitura,
oculto e arquivo). Pastas e arquivos vazios ficam como entradas sem fluxo. Sem criptografia, sem filtros (BCJ), sem
LZMA2 (o SharpCompress não codifica LZMA2) e sem volumes.

Dicionário por nível: rápida 1 MiB, normal 8 MiB, máxima 16 MiB (e 64 fast bytes), reduzido para o tamanho da entrada.
O codificador usa cerca de 11× o dicionário em memória (≈ 190 MiB no máximo).

As regras do criador existente continuam: links e junctions não são seguidos, grava num temporário e só renomeia ao
concluir, nunca sobrescreve, cancelar não deixa nada.

## Alternativas avaliadas

| Opção | Licença | Tamanho | Por que não |
|---|---|---|---|
| Embutir `7za.exe`/`7zr.exe` do 7-Zip | LGPL-2.1 (+ BSD) — compatível com AGPL-3.0 | ~0,6–1,3 MB por arquitetura (x64 e arm64) | Processo externo a executar a partir do gerenciador, binários de terceiros sem a nossa assinatura, atualização de segurança separada, avisos de licença e cópia do código-fonte a acompanhar cada release. |
| `7z.dll` via wrapper (SevenZipSharp e derivados) | LGPL | ~1,5 MB por arquitetura | Mesmos custos do nativo, wrapper sem manutenção ativa, P/Invoke em DLL carregada no processo do app. |
| Codificador LZMA do SharpCompress + contêiner próprio | MIT (já dependência) + especificação em domínio público | 0 | **Escolhida.** Mais lenta que o 7-Zip nativo e sem LZMA2 multithread, aceitável com progresso e cancelamento. |

## Verificação

`ArchiveCreatorTests` faz ida e volta pelo extrator do próprio app (SharpCompress) conferindo byte a byte. O teste de
integração no Windows abre o compactado criado com o 7-Zip instalado no runner (`7z t`) e falha se ele não estiver lá.
