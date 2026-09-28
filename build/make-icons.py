"""Gera assets/controlfs.ico e assets/controlfs-icon-512.png a partir de logos/controlfs-icon.png.

Ícone limpo (#187): fundo transparente, sem brilho neon nem névoa em volta. A arte original tem o corpo um pouco
translúcido e um halo fraco com pontinhos soltos; aqui o corpo fica opaco, só a região do logo (mais uma borda de 2 px
para a suavização) é mantida e o resto vira transparente. Depois recorta, centraliza num quadrado com uma pequena
margem e reduz com Lanczos para cada tamanho que o Windows usa (16–256 px).
Uso: python3 build/make-icons.py  (requer Pillow)
"""
from PIL import Image, ImageFilter

SIZES = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]

src = Image.open("logos/controlfs-icon.png").convert("RGBA")
alpha = src.split()[3]
body = alpha.point(lambda v: 255 if v >= 100 else 0)  # o logo em si (o halo e os pontinhos têm alfa baixo)
near_body = body.filter(ImageFilter.MaxFilter(5))  # o logo + 2 px: só aí fica a borda suavizada
clean = alpha.point(lambda v: 0 if v < 60 else 255 if v >= 200 else round((v - 60) * 255 / 140))  # corpo opaco
clean = Image.composite(clean, Image.new("L", src.size, 0), near_body)
src.putalpha(clean)
box = clean.getbbox()
art = src.crop(box)
w, h = art.size
side = int(max(w, h) * 1.06)
square = Image.new("RGBA", (side, side), (0, 0, 0, 0))
square.paste(art, ((side - w) // 2, (side - h) // 2), art)

square.resize((512, 512), Image.LANCZOS).save("assets/controlfs-icon-512.png")
frames = [square.resize((s, s), Image.LANCZOS) for s in SIZES]
frames[-1].save("assets/controlfs.ico", format="ICO", sizes=[(s, s) for s in SIZES], append_images=frames[:-1])
