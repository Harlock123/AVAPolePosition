#!/usr/bin/env python3
"""Synthesises AVAPolePosition's sound effects and tunes into ../src/AVAPolePosition/Sounds as 16-bit mono WAV.

Nothing is sampled from any arcade machine: every effect is built here from square, triangle and
noise voices, and the tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAPolePosition", "Sounds")
RNG = random.Random(1982)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The start lights.
    write("Light.wav", tone(0.3, hz("A4"), kind="square", release=0.4), 0.35)
    write("Go.wav", tone(0.7, hz("A5"), kind="square", release=0.6), 0.4)
    # The car.
    write("Shift.wav", cat(tone(0.05, 900, 600, "noise", release=1.0, gain=0.5), tone(0.12, 140, 220, "saw", release=0.6, gain=0.6)), 0.3)
    write("Skid.wav", tone(0.4, 2600, 1900, "noise", attack=0.02, release=0.4, vibrato=0.06, vib_rate=30), 0.3)
    write("Rumble.wav", cat(*[tone(0.04, 160, 120, "noise", release=0.8) for _ in range(5)]), 0.3)
    write("Crash.wav", cat(mix(tone(0.15, 2400, 600, "noise", release=0.6), tone(0.12, 300, 100, "square", release=0.8)),
                           mix(tone(1.1, 300, 25, "noise", release=0.45), tone(0.9, 120, 25, "saw", release=0.6, gain=0.6))), 0.6)
    write("Pass.wav", tone(0.45, 520, 260, "saw", attack=0.05, release=0.5, gain=0.7), 0.3)
    # The race.
    write("Lap.wav", cat(*[tone(0.08, hz(n), kind="pulse", release=0.3) for n in ["C6", "E6", "G6"]]), 0.35)
    write("Extended.wav", cat(*[tone(0.09, hz(n), kind="pulse", release=0.3) for n in ["G5", "C6", "E6", "G6", "C7"]]), 0.4)
    write("Qualified.wav", tune(150, [("C5", 0.5), ("E5", 0.5), ("G5", 0.5), ("C6", 1.5)], [("C3", 1.5), ("G2", 1.5)], kind="pulse"), 0.45)
    write("Goal.wav", tune(150, [("G4", 0.5), ("C5", 0.5), ("E5", 0.5), ("G5", 1), ("E5", 0.5), ("G5", 0.5), ("C6", 2)], [("C3", 2), ("G2", 1), ("C3", 2)], kind="pulse"), 0.45)
    write("TimeUp.wav", cat(*[tone(0.18, hz(n), kind="square", release=0.3) for n in ["E5", "C5", "A4"]], tone(0.6, hz("E4"), kind="square", release=0.5)), 0.4)

    # Racing: a bright, driving major-key march over a pumping bass, with the engine's drone beneath (new). Loops.
    bassline = []
    for root in ["D2", "D2", "G2", "A2", "D2", "B1", "G2", "A2"] * 2:
        bassline += [(root, 0.25), (root, 0.25), ("-", 0.25), (root, 0.25)] * 2
    lead = [("F#4", 0.5), ("A4", 0.5), ("D5", 1), ("C#5", 0.5), ("B4", 0.5), ("A4", 1), ("G4", 0.5), ("B4", 0.5), ("E5", 1), ("D5", 0.5), ("C#5", 0.5), ("A4", 1),
            ("F#4", 0.5), ("A4", 0.5), ("D5", 1), ("E5", 0.5), ("F#5", 0.5), ("G5", 1), ("F#5", 0.5), ("E5", 0.5), ("D5", 0.5), ("C#5", 0.5), ("D5", 2),
            ("A4", 0.5), ("D5", 0.5), ("F#5", 1), ("E5", 0.5), ("D5", 0.5), ("B4", 1), ("G4", 0.5), ("B4", 0.5), ("D5", 1), ("A4", 2),
            ("F#4", 0.5), ("A4", 0.5), ("D5", 1), ("C#5", 0.5), ("E5", 0.5), ("D5", 3)]
    bass = seq(150, bassline, kind="square", gain=0.32, release=0.5)
    top = seq(150, lead, kind="pulse", gain=0.24, release=0.5)
    drone = tone(len(bass) / RATE, 92, 92, "saw", attack=0.05, release=0.0, gain=0.12, vibrato=0.03, vib_rate=11)
    write("Music.wav", mix(bass, top, drone), 0.42)

    write("TitleTune.wav", tune(132,
                                [("D5", 0.5), ("F#5", 0.5), ("A5", 1), ("G5", 0.5), ("F#5", 0.5), ("E5", 1), ("C#5", 0.5), ("E5", 0.5), ("A5", 1), ("G5", 2),
                                 ("F#5", 0.5), ("E5", 0.5), ("D5", 1), ("B4", 0.5), ("C#5", 0.5), ("E5", 1), ("A4", 0.5), ("C#5", 0.5), ("E5", 0.5), ("C#5", 0.5), ("D5", 2)],
                                [("D3", 2), ("B2", 2), ("A2", 2), ("A2", 2), ("D3", 2), ("G2", 2), ("A2", 2), ("D3", 2)], kind="pulse"), 0.45)
    write("StartTune.wav", tune(160, [("A4", 0.5), ("D5", 0.5), ("F#5", 0.5), ("A5", 1.5), ("F#5", 0.5), ("A5", 2)], [("D3", 2), ("A2", 1), ("D3", 2)], kind="pulse"), 0.45)
    write("GameOver.wav", tune(80, [("A5", 1), ("G5", 1), ("F#5", 1), ("E5", 1), ("D5", 3)], [("D4", 2), ("B3", 2), ("A3", 1), ("D3", 3)], kind="tri"), 0.45)


if __name__ == "__main__":
    main()
