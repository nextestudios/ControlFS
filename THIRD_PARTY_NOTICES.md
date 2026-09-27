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
