"""Gera assets/controlfs.ico e assets/controlfs-icon-512.png a partir de logos/controlfs-icon.png.

Mantém a transparência (sem fundo preto) e o brilho neon da arte: recorta pelo alfa, centraliza num quadrado com
uma pequena margem e reduz com Lanczos para cada tamanho que o Windows usa (16–256 px).
Uso: python3 build/make-icons.py  (requer Pillow)
"""
from PIL import Image

SIZES = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]

src = Image.open("logos/controlfs-icon.png").convert("RGBA")
box = src.split()[3].point(lambda v: 255 if v > 8 else 0).getbbox()  # ignora ruído quase invisível na borda
art = src.crop(box)
w, h = art.size
side = int(max(w, h) * 1.06)
square = Image.new("RGBA", (side, side), (0, 0, 0, 0))
square.paste(art, ((side - w) // 2, (side - h) // 2), art)

square.resize((512, 512), Image.LANCZOS).save("assets/controlfs-icon-512.png")
frames = [square.resize((s, s), Image.LANCZOS) for s in SIZES]
frames[-1].save("assets/controlfs.ico", format="ICO", sizes=[(s, s) for s in SIZES], append_images=frames[:-1])
