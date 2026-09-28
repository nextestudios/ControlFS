GAMEPAD EXPLORER — AVISOS DE TERCEIROS

O código original deste projeto é licenciado sob a GNU Affero General Public License v3.0 only
(AGPL-3.0-only, arquivo LICENSE; versões até 0.2.0-alpha.1 foram distribuídas sob MIT — ver docs/LICENSING.md).
Os componentes abaixo mantêm suas próprias licenças; incluí-los na distribuição NÃO os torna AGPL.

Componentes distribuídos com o aplicativo
------------------------------------------------------------------------------

SharpCompress 1.0.0
  Origem: https://github.com/adamhathcock/sharpcompress (commit b6cc95af73950c914e0c6ce7ea3511528ede1121)
  Licença: MIT — Copyright (c) 2014 Adam Hathcock
  Texto: https://github.com/adamhathcock/sharpcompress/blob/master/LICENSE.txt
  Inclui o porte em C# do LZMA SDK de Igor Pavlov (domínio público), usado para ler e, desde #67, criar 7z.

ppy.SDL3-CS 2026.722.0 (binding C#)
  Origem: https://github.com/ppy/SDL3-CS (commit 7f836c9f21dad8ee68e70432e5b7d38ceae47eaa)
  Licença: MIT — Copyright (c) ppy Pty Ltd

SDL 3.5.0 (biblioteca nativa SDL3.dll incluída no pacote acima)
  Origem: https://github.com/libsdl-org/SDL
  Licença: zlib — Copyright (C) 1997-2026 Sam Lantinga <slouken@libsdl.org>
  PENDENTE: confirmar quais componentes opcionais (ex.: hidapi) estão compilados no binário
  distribuído e reproduzir aqui os avisos correspondentes.

Microsoft Windows App SDK 2.5.1 — componentes WinUI, Foundation, InteractiveExperiences e Base (e Microsoft.Web.WebView2,
  dependência do WinUI)
  Origem: https://www.nuget.org/packages/Microsoft.WindowsAppSDK.WinUI (e pacotes Microsoft.WindowsAppSDK.* listados)
  Licença: Microsoft Software License Terms — Microsoft Windows App SDK (license.txt no pacote).
  A seção "Distributable Code" permite redistribuir arquivos colocados junto ao aplicativo pelo pacote,
  inclusive em implantação self-contained. Contém avisos de terceiros próprios; ver o pacote.

LibGit2Sharp 0.32.0 (status do Git somente leitura, #75)
  Origem: https://github.com/libgit2/libgit2sharp
  Licença: MIT — Copyright (c) LibGit2Sharp contributors

libgit2 (git2-5853918.dll, do pacote LibGit2Sharp.NativeBinaries 2.0.324)
  Origem: https://github.com/libgit2/libgit2
  Licença: GNU GPL v2 com exceção de linking ("LINKING EXCEPTION"): permite distribuir a biblioteca compilada ligada a
  outros programas sem que a GPL se aplique a eles; modificações da própria libgit2 seguem a GPL v2. Inclui código de
  terceiros com licenças próprias (zlib, PCRE, http-parser, ntlmclient e outros), listadas em libgit2/libgit2.license.txt
  no pacote. O ControlFS não modifica a libgit2.

@noble/ciphers 2.4.0 e @noble/hashes 2.4.0 (página do celular, #223)
  Origem: https://github.com/paulmillr/noble-ciphers e https://github.com/paulmillr/noble-hashes (npm)
  Licença: MIT — Copyright (c) 2022 Paul Miller (https://paulmillr.com); noble-ciphers também
  Copyright (c) 2016 Thomas Pornin <pornin@bolet.org>.
  Só AES-GCM, HKDF e SHA-256, empacotados e minificados com esbuild em
  src/ControlFS.Infrastructure.Remote/Companion/noble-ciphers.min.js, com o texto das duas licenças no topo do arquivo.
  O arquivo vai embutido no ControlFS e é servido ao celular dentro da página; nada é baixado de fora. Para refazer:
  `npm install @noble/ciphers@2.4.0 @noble/hashes@2.4.0 esbuild`, um entry que exporta { gcm } de
  @noble/ciphers/aes.js, { hkdf } de @noble/hashes/hkdf.js e { sha256 } de @noble/hashes/sha2.js em
  self.nobleControlFS, e `esbuild entry.js --bundle --minify --format=iife --target=safari15,chrome100 --legal-comments=none`.

Codificador de QR Code (src/ControlFS.Core/Remote/QrCode.cs, #223)
  Código próprio do ControlFS, escrito seguindo a estrutura do gerador de referência "QR Code generator library" de
  Project Nayuki (https://www.nayuki.io/page/qr-code-generator-library, licença MIT — Copyright (c) Project Nayuki).

.NET Runtime 10 (quando publicado como self-contained)
  Origem: https://github.com/dotnet/runtime
  Licença: MIT — Copyright (c) .NET Foundation and Contributors
  Avisos de terceiros: THIRD-PARTY-NOTICES.TXT distribuído com o runtime.

Inno Setup 6 (gerador do instalador ControlFS-Setup-x64.exe)
  Origem: https://jrsoftware.org/isinfo.php
  Licença: Inno Setup License — Copyright (C) 1997-2026 Jordan Russell, Martijn Laan.
  O executável do instalador contém o código de instalação do Inno Setup, distribuído sob essa licença.

Componentes usados apenas no desenvolvimento (não distribuídos)
------------------------------------------------------------------------------

xunit.v3 3.2.2 / xunit.runner.visualstudio 3.1.5 — Apache-2.0
Microsoft.NET.Test.Sdk 18.10.1 — MIT
Microsoft.Windows.SDK.BuildTools 10.0.28000.2705 — Microsoft Windows SDK License

Glifos dos botões de controle
------------------------------------------------------------------------------

Os glifos das legendas (src/ControlFS.App/Resources/ControllerGlyphs.cs) são originais deste projeto: formas
geométricas e letras desenhadas em código. Nenhuma imagem, fonte de ícones, logotipo ou conjunto de glifos de
terceiros (Xbox, PlayStation, Nintendo ou outros) é incluído. Xbox, PlayStation e Nintendo são marcas de seus
respectivos donos e aparecem aqui só para identificar compatibilidade.

O Windows e seus serviços não fazem parte deste código aberto.
