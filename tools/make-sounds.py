#!/usr/bin/env python3
"""
Gera assets/sounds/*.wav (os sons do controle, #276/#287) a partir de dois pacotes de Kenney em CC0: "Interface Sounds" e "UI Audio".

Só é preciso rodar de novo para trocar um som; o app usa os WAV já prontos. Requer `pip install soundfile numpy`.
Uso: python tools/make-sounds.py <Audio de Interface Sounds> <Audio de UI Audio> assets/sounds

Cada som: mono, aparado no silêncio e limitado a uma duração, filtro passa-baixa suave (tira o brilho agudo; o objetivo é um
toque redondo e discreto, na linha do que se espera de uma interface de console), fade de entrada de 3 ms e de saída maior
(sem estalo), mesma energia (RMS) para todos e pico limitado; PCM 16 bits a 44,1 kHz. O volume do usuário só abaixa a partir
daí (src/ControlFS.Core/Audio/SoundBank.cs). Os arquivos foram escolhidos por análise (curtos, tonais, de ataque suave,
confirmar subindo de tom), sem tocar nenhum som da Sony: a interface do PS5 é só referência de sensação.
"""
import sys, os
import numpy as np
import soundfile as sf

# cue do app -> (pacote, arquivo, duração máxima em ms, fade de saída em ms)
CUES = {
    "move":    ("ui",   "rollover5.ogg",        80, 25),
    "select":  ("iface", "glass_002.ogg",       95, 30),
    "confirm": ("iface", "maximize_009.ogg",   225, 45),
    "back":    ("iface", "back_002.ogg",        70, 20),
}
TARGET_RMS = 0.10
PEAK_CAP = 0.80
RATE = 44100
LOWPASS_HZ = 6500

def lowpass(x, cutoff):
    # filtro de um polo aplicado duas vezes (12 dB/oitava): suave, sem ressonância
    a = np.exp(-2 * np.pi * cutoff / RATE)
    for _ in range(2):
        y = np.empty_like(x)
        acc = 0.0
        for i, v in enumerate(x):
            acc = (1 - a) * v + a * acc
            y[i] = acc
        x = y
    return x

def process(samples, rate, max_ms, fade_out_ms):
    mono = samples.mean(axis=1)
    assert rate == RATE, f"taxa inesperada {rate}"
    loud = np.flatnonzero(np.abs(mono) > 0.01)
    mono = mono[loud[0]:loud[-1] + 1][: int(RATE * max_ms / 1000)].copy()
    mono = lowpass(mono, LOWPASS_HZ)
    fi, fo = int(RATE * 0.003), min(int(RATE * fade_out_ms / 1000), len(mono) // 2)
    mono[:fi] *= np.linspace(0, 1, fi)
    mono[-fo:] *= np.linspace(1, 0, fo) ** 1.5
    gain = TARGET_RMS / np.sqrt((mono ** 2).mean())
    gain = min(gain, PEAK_CAP / np.abs(mono).max())
    return mono * gain

def main(iface, ui, dst):
    os.makedirs(dst, exist_ok=True)
    folders = {"iface": iface, "ui": ui}
    for cue, (pack, name, max_ms, fade_out) in CUES.items():
        data, rate = sf.read(os.path.join(folders[pack], name), always_2d=True)
        out = process(data, rate, max_ms, fade_out)
        sf.write(os.path.join(dst, cue + ".wav"), out, RATE, subtype="PCM_16")
        print(f"{cue}.wav <- {pack}/{name}: {len(out) / RATE * 1000:.0f} ms, rms={np.sqrt((out ** 2).mean()):.3f}, peak={np.abs(out).max():.2f}")

if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2], sys.argv[3])
