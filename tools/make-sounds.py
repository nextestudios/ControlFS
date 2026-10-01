#!/usr/bin/env python3
"""
Gera assets/sounds/*.wav (os sons do controle, #276/#287) a partir do pacote "Interface Sounds" de Kenney (CC0).

Só é preciso rodar de novo para trocar um som; o app usa os WAV já prontos. Requer `pip install soundfile numpy`.
Uso: python tools/make-sounds.py <pasta Audio do pacote> assets/sounds

Cada som: mono, aparado no silêncio, fade de 2 ms (sem estalo), mesma energia (RMS) para todos e pico limitado, PCM 16 bits
a 44,1 kHz. O volume do usuário só abaixa a partir daí (src/ControlFS.Core/Audio/SoundBank.cs).
"""
import sys, os
import numpy as np
import soundfile as sf

CUES = {          # cue do app -> arquivo do pacote de Kenney
    "move": "select_002.ogg",
    "select": "glass_006.ogg",
    "confirm": "confirmation_001.ogg",
    "back": "back_004.ogg",
}
TARGET_RMS = 0.10
PEAK_CAP = 0.80
RATE = 44100

def process(samples, rate):
    mono = samples.mean(axis=1)
    assert rate == RATE, f"taxa inesperada {rate}"
    loud = np.flatnonzero(np.abs(mono) > 0.01)
    mono = mono[loud[0]:loud[-1] + 1]
    fade = int(RATE * 0.002)
    mono[:fade] *= np.linspace(0, 1, fade)
    mono[-fade:] *= np.linspace(1, 0, fade)
    gain = TARGET_RMS / np.sqrt((mono ** 2).mean())
    gain = min(gain, PEAK_CAP / np.abs(mono).max())
    return mono * gain

def main(src, dst):
    os.makedirs(dst, exist_ok=True)
    for cue, name in CUES.items():
        data, rate = sf.read(os.path.join(src, name), always_2d=True)
        out = process(data, rate)
        sf.write(os.path.join(dst, cue + ".wav"), out, RATE, subtype="PCM_16")
        print(f"{cue}.wav <- {name}: {len(out) / RATE * 1000:.0f} ms, rms={np.sqrt((out ** 2).mean()):.3f}, peak={np.abs(out).max():.2f}")

if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
