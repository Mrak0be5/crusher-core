#!/usr/bin/env python3
"""
Procedural Industrial ASMR Audio Synthesizer for CRUSHER CORE V1.
Zero Asset Store: 100% mathematical DSP synthesis via numpy + scipy.signal.
Generates all 14 SfxId groups (21 .wav files, 44.1 kHz 16-bit PCM) + self-check QA report.
"""

from __future__ import annotations
import json
import math
import sys
from pathlib import Path
import numpy as np
from scipy.io import wavfile
from scipy.signal import butter, sosfilt

SR = 44100


def bandpass(sig: np.ndarray, low_hz: float, high_hz: float, order: int = 4) -> np.ndarray:
    nyq = 0.5 * SR
    sos = butter(order, [max(20.0, low_hz) / nyq, min(nyq * 0.96, high_hz) / nyq], btype="bandpass", output="sos")
    return sosfilt(sos, sig)


def lowpass(sig: np.ndarray, cutoff_hz: float, order: int = 4) -> np.ndarray:
    nyq = 0.5 * SR
    sos = butter(order, min(nyq * 0.96, cutoff_hz) / nyq, btype="lowpass", output="sos")
    return sosfilt(sos, sig)


def highpass(sig: np.ndarray, cutoff_hz: float, order: int = 3) -> np.ndarray:
    nyq = 0.5 * SR
    sos = butter(order, max(20.0, cutoff_hz) / nyq, btype="highpass", output="sos")
    return sosfilt(sos, sig)


def apply_fade(sig: np.ndarray, fade_in_ms: float = 2.5, fade_out_ms: float = 8.0) -> np.ndarray:
    out = sig.copy()
    n_in = max(2, int(SR * fade_in_ms * 0.001))
    n_out = max(4, int(SR * fade_out_ms * 0.001))
    out[:n_in] *= np.linspace(0.0, 1.0, n_in)
    out[-n_out:] *= np.linspace(1.0, 0.0, n_out)
    out[0] = 0.0
    out[-1] = 0.0
    return out


def normalize_peak(sig: np.ndarray, peak_target: float = 0.86) -> np.ndarray:
    mx = float(np.max(np.abs(sig)))
    if mx < 1e-8:
        return sig
    return sig * (peak_target / mx)


def pitch_sweep_sine(t: np.ndarray, f_start: float, f_end: float, curve: float = 3.0) -> np.ndarray:
    dur = max(float(t[-1]), 1e-5)
    u = t / dur
    freq = f_end + (f_start - f_end) * np.exp(-curve * u)
    phase = 2.0 * math.pi * np.cumsum(freq) / SR
    return np.sin(phase)


def gen_crunch(variant: int, rng: np.random.Generator) -> np.ndarray:
    dur = 0.17 + 0.015 * variant
    n = int(SR * dur)
    t = np.arange(n, dtype=np.float64) / SR

    # 1. Sub-bass kinetic anvil thump
    sub = pitch_sweep_sine(t, 92.0 - 6.0 * variant, 36.0, curve=14.0) * np.exp(-t * 28.0)

    # 2. Poisson micro-fracture grain cluster (crushing plastic/alloy shell)
    grains = np.zeros(n, dtype=np.float64)
    n_impacts = 8 + variant * 2
    offsets = np.sort(rng.uniform(0.001, dur * 0.62, size=n_impacts))
    for idx, t0 in enumerate(offsets):
        i0 = int(t0 * SR)
        rem = n - i0
        if rem <= 32:
            continue
        tg = np.arange(rem, dtype=np.float64) / SR
        amp = (0.95 - 0.45 * (idx / n_impacts)) * rng.uniform(0.7, 1.15)
        burst_noise = rng.standard_normal(rem) * np.exp(-tg * rng.uniform(85.0, 150.0))
        grains[i0:] += amp * burst_noise

    crisp_grit = bandpass(grains, 1650.0 + 140.0 * variant, 7200.0)

    # 3. Inharmonic carbide steel plate modal ring
    f0 = 390.0 + 48.0 * variant
    modes = [1.0, 1.57, 2.29, 3.12]
    decays = [34.0, 46.0, 62.0, 84.0]
    modal = np.zeros(n, dtype=np.float64)
    for m, d in zip(modes, decays):
        modal += np.sin(2.0 * math.pi * (f0 * m) * t + rng.uniform(0, 2 * math.pi)) * np.exp(-t * d)
    modal = np.tanh(modal * 1.9) * 0.42

    mix = sub * 0.85 + crisp_grit * 1.15 + modal
    mix = np.tanh(mix * 1.45)
    return normalize_peak(apply_fade(mix, 1.5, 14.0), 0.86)


def gen_boss_shatter(rng: np.random.Generator) -> np.ndarray:
    dur = 0.72
    n = int(SR * dur)
    t = np.arange(n, dtype=np.float64) / SR

    # Heavy hydraulic breach + sub drop
    sub = pitch_sweep_sine(t, 125.0, 25.0, curve=6.5) * np.exp(-t * 5.2)
    punch = pitch_sweep_sine(t, 240.0, 48.0, curve=16.0) * np.exp(-t * 18.0)

    # Tearing titanium hull modal cluster
    modes_hz = [178.0, 289.0, 462.0, 738.0, 1190.0, 1940.0]
    hull = np.zeros(n, dtype=np.float64)
    for k, hz in enumerate(modes_hz):
        wobble = 1.0 - 0.06 * np.exp(-t * 9.0)
        hull += np.sin(2.0 * math.pi * hz * wobble * t) * np.exp(-t * (6.5 + k * 3.8)) * (1.0 / (1.0 + 0.35 * k))
    hull = np.tanh(hull * 3.2) * 0.55

    # Cascading debris & cassette scatter tail
    debris = np.zeros(n, dtype=np.float64)
    for t0 in np.linspace(0.03, 0.54, 22):
        jt = min(dur - 0.05, max(0.005, t0 + rng.uniform(-0.015, 0.02)))
        i0 = int(jt * SR)
        rem = n - i0
        tg = np.arange(rem, dtype=np.float64) / SR
        ping_f = rng.uniform(950.0, 3400.0)
        ping = np.sin(2.0 * math.pi * ping_f * tg) * np.exp(-tg * rng.uniform(35.0, 75.0))
        burst = bandpass(rng.standard_normal(rem), 1800.0, 8000.0) * np.exp(-tg * 95.0)
        debris[i0:] += (ping * 0.45 + burst * 0.55) * rng.uniform(0.25, 0.75)

    mix = np.tanh((sub * 0.95 + punch * 0.65 + hull + debris * 0.70) * 1.35)
    return normalize_peak(apply_fade(mix, 2.0, 35.0), 0.90)


def periodic_band_noise(n_samples: int, low_hz: float, high_hz: float, rng: np.random.Generator) -> np.ndarray:
    """Exact circularly-periodic filtered noise via iRFFT -> 0.0 loop seam click guaranteed!"""
    freqs = np.fft.rfftfreq(n_samples, d=1.0 / SR)
    spec = np.zeros(len(freqs), dtype=np.complex128)
    mask = (freqs >= low_hz) & (freqs <= high_hz)
    phases = rng.uniform(0.0, 2.0 * math.pi, size=int(np.sum(mask)))
    # Smooth bell envelope across passband
    f_band = freqs[mask]
    center = 0.5 * (low_hz + high_hz)
    half_w = max(1.0, 0.5 * (high_hz - low_hz))
    env = np.cos(0.5 * math.pi * np.clip((f_band - center) / half_w, -1.0, 1.0)) ** 2
    spec[mask] = env * np.exp(1j * phases)
    return np.fft.irfft(spec, n=n_samples)


def gen_rotor_hum_loop(rng: np.random.Generator) -> np.ndarray:
    # Exact 3.00s seamless loop: every harmonic is an integer multiple of 1/3 Hz!
    dur = 3.0
    n = int(SR * dur)
    t = np.arange(n, dtype=np.float64) / SR
    w0 = 2.0 * math.pi / dur  # fundamental angular unit -> exact 2*pi periodicity at n

    lfo1 = 0.85 + 0.15 * np.sin(3.0 * w0 * t)       # 1.0 Hz gentle torque breathe
    lfo2 = 0.90 + 0.10 * np.cos(6.0 * w0 * t)       # 2.0 Hz counter-drum phase

    # 3-phase industrial stator + gear mesh harmonics (all integer multiples of 1/3 Hz)
    stator = (
        0.52 * np.sin(180.0 * w0 * t)               # 60 Hz motor hum
        + 0.34 * np.sin(360.0 * w0 * t + 0.6)       # 120 Hz magnetic torque
        + 0.18 * np.sin(540.0 * w0 * t + 1.2)       # 180 Hz 3rd harmonic
        + 0.09 * np.sin(1080.0 * w0 * t) * lfo2     # 360 Hz gear tooth mesh
    ) * lfo1

    # Circularly periodic bearing & roller friction texture
    bearing = normalize_peak(periodic_band_noise(n, 140.0, 720.0, rng), 1.0) * 0.16 * lfo2
    mix = np.tanh((stator + bearing) * 1.15)
    return normalize_peak(mix, 0.68)


def gen_blackhole_loop(rng: np.random.Generator) -> np.ndarray:
    # Exact 2.50s seamless loop: every component is an integer multiple of 0.4 Hz!
    dur = 2.5
    n = int(SR * dur)
    t = np.arange(n, dtype=np.float64) / SR
    w0 = 2.0 * math.pi / dur  # 0.4 Hz fundamental

    pulse = 0.72 + 0.28 * np.sin(10.0 * w0 * t)  # 4.0 Hz electromagnetic throb
    # Beating Lorentz sub-drones (48.0 Hz + 48.8 Hz -> 2 exact beats per loop)
    sub_drone = 0.58 * np.sin(120.0 * w0 * t) + 0.42 * np.sin(122.0 * w0 * t)
    coil_buzz = (
        0.24 * np.sin(240.0 * w0 * t + 0.4 * np.sin(5.0 * w0 * t))
        + 0.14 * np.sin(360.0 * w0 * t)
        + 0.08 * np.sin(480.0 * w0 * t)
    ) * pulse

    swirl = normalize_peak(periodic_band_noise(n, 180.0, 980.0, rng), 1.0) * 0.22 * (0.65 + 0.35 * np.cos(5.0 * w0 * t))
    mix = np.tanh((sub_drone + coil_buzz + swirl) * 1.25)
    return normalize_peak(mix, 0.78)


def gen_orb_saw(variant: int, rng: np.random.Generator) -> np.ndarray:
    dur = 0.165
    n = int(SR * dur)
    t = np.arange(n, dtype=np.float64) / SR
    u = t / dur

    # Doppler circular plasma-saw slice
    f_base = 1380.0 + 160.0 * variant
    f_curve = f_base + 920.0 * np.sin(math.pi * (u ** 0.75)) - 260.0 * u
    phase = 2.0 * math.pi * np.cumsum(f_curve) / SR
    mod = np.sin(2.0 * math.pi * (310.0 + 35.0 * variant) * t) * 2.2
    saw_fm = np.sin(phase + mod) * np.sin(math.pi * u) ** 0.65

    sparks = bandpass(rng.standard_normal(n), 3400.0, 8800.0) * np.exp(-((u - 0.35) ** 2) / 0.08) * 0.55
    mix = np.tanh((saw_fm * 0.80 + sparks) * 1.3)
    return normalize_peak(apply_fade(mix, 2.0, 14.0), 0.78)


def gen_pneumo_shot(rng: np.random.Generator) -> np.ndarray:
    dur = 0.095
    n = int(SR * dur)
    t = np.arange(n, dtype=np.float64) / SR

    piston = pitch_sweep_sine(t, 205.0, 58.0, curve=22.0) * np.exp(-t * 36.0)
    air_burst = highpass(rng.standard_normal(n), 1500.0) * np.exp(-t * 95.0) * 0.48
    click = np.sin(2.0 * math.pi * 1150.0 * t) * np.exp(-t * 140.0) * 0.30
    mix = np.tanh((piston + air_burst + click) * 1.25)
    # Deliberately softer peak (-4.5 dBFS) so rapid fire stays pleasant for 60 minutes
    return normalize_peak(apply_fade(mix, 1.0, 12.0), 0.60)


def gen_golden_start(rng: np.random.Generator) -> np.ndarray:
    dur = 0.88
    n = int(SR * dur)
    t = np.arange(n, dtype=np.float64) / SR

    # Rising induction smelter horn (90 -> 220 Hz)
    f_rise = 90.0 + 130.0 * (1.0 - np.exp(-t * 4.5))
    horn_phase = 2.0 * math.pi * np.cumsum(f_rise) / SR
    horn = (np.sin(horn_phase) + 0.45 * np.sin(2.0 * horn_phase)) * np.exp(-t * 2.2) * 0.48

    # Furnace flame whoosh
    whoosh = bandpass(rng.standard_normal(n), 500.0, 2600.0) * np.sin(math.pi * np.clip(t / 0.55, 0.0, 1.0)) * 0.35

    # Radiant golden anvil chord (D5, A5, D6, F#6, A6)
    chord_freqs = [587.33, 880.00, 1174.66, 1479.98, 1760.00]
    shimmer = np.zeros(n, dtype=np.float64)
    for idx, f in enumerate(chord_freqs):
        t_delay = max(0.0, idx * 0.035)
        i0 = int(t_delay * SR)
        rem = n - i0
        tc = np.arange(rem, dtype=np.float64) / SR
        bell = (np.sin(2.0 * math.pi * f * tc) + 0.35 * np.sin(2.0 * math.pi * f * 2.756 * tc) * np.exp(-tc * 12.0))
        shimmer[i0:] += bell * np.exp(-tc * (3.2 + idx * 0.5)) * 0.28

    mix = np.tanh((horn + whoosh + shimmer) * 1.25)
    return normalize_peak(apply_fade(mix, 3.0, 45.0), 0.88)


def gen_ingot_ding(step: int) -> np.ndarray:
    dur = 0.21
    n = int(SR * dur)
    t = np.arange(n, dtype=np.float64) / SR

    # Pentatonic pure gold bar chimes: C6, D6, E6, G6, A6
    penta_hz = [1046.50, 1174.66, 1318.51, 1567.98, 1760.00]
    f0 = penta_hz[step % len(penta_hz)]

    # Solid gold bullion modal ratios
    bar = (
        0.68 * np.sin(2.0 * math.pi * f0 * t) * np.exp(-t * 14.0)
        + 0.28 * np.sin(2.0 * math.pi * (f0 * 2.0) * t) * np.exp(-t * 22.0)
        + 0.22 * np.sin(2.0 * math.pi * (f0 * 2.758) * t) * np.exp(-t * 32.0)
        + 0.12 * np.sin(2.0 * math.pi * (f0 * 4.15) * t) * np.exp(-t * 48.0)
    )
    # Crisp bullion clink transient
    clink = np.sin(2.0 * math.pi * (f0 * 5.4) * t) * np.exp(-t * 160.0) * 0.25
    return normalize_peak(apply_fade(bar + clink, 1.0, 18.0), 0.80)


def gen_chain_zap(rng: np.random.Generator) -> np.ndarray:
    dur = 0.22
    n = int(SR * dur)
    out = np.zeros(n, dtype=np.float64)

    # 3 cascading high-voltage arc hops across targets
    for hop, t0 in enumerate([0.0, 0.052, 0.108]):
        i0 = int(t0 * SR)
        rem = n - i0
        th = np.arange(rem, dtype=np.float64) / SR
        amp = 1.0 - 0.22 * hop
        zap_f = 420.0 + (3600.0 - 500.0 * hop) * np.exp(-th * 45.0)
        ph = 2.0 * math.pi * np.cumsum(zap_f) / SR
        fm = np.sin(ph + 3.2 * np.sin(2.0 * math.pi * 680.0 * th))
        arc = np.tanh(fm * 3.5) * np.exp(-th * 28.0)
        sizzle = bandpass(rng.standard_normal(rem), 2600.0, 9500.0) * np.exp(-th * 42.0) * 0.65
        out[i0:] += amp * (arc * 0.72 + sizzle)

    return normalize_peak(apply_fade(np.tanh(out * 1.2), 1.0, 16.0), 0.84)


def gen_plasma_hit(rng: np.random.Generator) -> np.ndarray:
    dur = 0.28
    n = int(SR * dur)
    t = np.arange(n, dtype=np.float64) / SR

    rail_thump = pitch_sweep_sine(t, 180.0, 38.0, curve=12.0) * np.exp(-t * 14.0)
    womp = np.sin(2.0 * math.pi * np.cumsum(140.0 + 620.0 * np.exp(-t * 18.0)) / SR + 2.0 * np.sin(2.0 * math.pi * 110.0 * t)) * np.exp(-t * 12.0)
    fizz = bandpass(rng.standard_normal(n), 1800.0, 7500.0) * np.exp(-t * 24.0) * 0.45
    mix = np.tanh((rail_thump * 0.85 + womp * 0.65 + fizz) * 1.35)
    return normalize_peak(apply_fade(mix, 1.5, 22.0), 0.86)


def gen_ui_tap() -> np.ndarray:
    dur = 0.045
    n = int(SR * dur)
    t = np.arange(n, dtype=np.float64) / SR
    body = pitch_sweep_sine(t, 1250.0, 480.0, curve=28.0) * np.exp(-t * 85.0)
    snap = np.sin(2.0 * math.pi * 2650.0 * t) * np.exp(-t * 210.0) * 0.45
    return normalize_peak(apply_fade(body + snap, 0.8, 8.0), 0.68)


def gen_ui_upgrade() -> np.ndarray:
    dur = 0.15
    n = int(SR * dur)
    t = np.arange(n, dtype=np.float64) / SR

    # Pneumatic latch + ascending fifth (D5 -> A5)
    latch = pitch_sweep_sine(t, 320.0, 95.0, curve=25.0) * np.exp(-t * 45.0) * 0.55
    n1 = np.sin(2.0 * math.pi * 587.33 * t) * np.exp(-t * 26.0)
    i2 = int(0.052 * SR)
    t2 = np.arange(n - i2, dtype=np.float64) / SR
    n2 = np.zeros(n, dtype=np.float64)
    n2[i2:] = (np.sin(2.0 * math.pi * 880.0 * t2) + 0.35 * np.sin(2.0 * math.pi * 1760.0 * t2)) * np.exp(-t2 * 20.0)
    return normalize_peak(apply_fade(latch + n1 * 0.55 + n2 * 0.75, 1.0, 14.0), 0.78)


def gen_ui_unlock() -> np.ndarray:
    dur = 0.58
    n = int(SR * dur)
    t = np.arange(n, dtype=np.float64) / SR

    bass = np.sin(2.0 * math.pi * 146.83 * t) * np.exp(-t * 4.5) * 0.45
    notes = [(0.00, 587.33), (0.07, 739.99), (0.14, 880.00), (0.22, 1174.66)]
    arp = np.zeros(n, dtype=np.float64)
    for t0, hz in notes:
        i0 = int(t0 * SR)
        tn = np.arange(n - i0, dtype=np.float64) / SR
        voice = np.sin(2.0 * math.pi * hz * tn) + 0.30 * np.sin(2.0 * math.pi * hz * 2.0 * tn) + 0.18 * np.sin(2.0 * math.pi * hz * 3.0 * tn)
        arp[i0:] += voice * np.exp(-tn * 5.5) * 0.38

    return normalize_peak(apply_fade(np.tanh((bass + arp) * 1.15), 2.0, 35.0), 0.85)


def gen_overload_death(rng: np.random.Generator) -> np.ndarray:
    dur = 0.95
    n = int(SR * dur)
    t = np.arange(n, dtype=np.float64) / SR

    # 3 violent gear-jamming clanks + turbine power-down whine + steam vent
    clanks = np.zeros(n, dtype=np.float64)
    for t0 in [0.0, 0.105, 0.205]:
        i0 = int(t0 * SR)
        tc = np.arange(n - i0, dtype=np.float64) / SR
        c = (
            np.sin(2.0 * math.pi * 215.0 * tc)
            + 0.7 * np.sin(2.0 * math.pi * 342.0 * tc)
            + 0.5 * np.sin(2.0 * math.pi * 588.0 * tc)
        ) * np.exp(-tc * 24.0)
        clanks[i0:] += np.tanh(c * 2.8) * 0.55

    f_down = 420.0 * np.exp(-t * 2.6) + 38.0
    whine = np.sin(2.0 * math.pi * np.cumsum(f_down) / SR) * np.exp(-t * 2.1) * 0.55
    steam = lowpass(highpass(rng.standard_normal(n), 600.0), 4200.0) * np.exp(-np.maximum(0.0, t - 0.18) * 4.2) * (t >= 0.18) * 0.42
    boom = pitch_sweep_sine(t, 98.0, 28.0, curve=5.0) * np.exp(-t * 4.0) * 0.70

    mix = np.tanh((clanks + whine + steam + boom) * 1.25)
    return normalize_peak(apply_fade(mix, 2.0, 45.0), 0.90)


def validate_and_write(out_dir: Path, filename: str, samples_f64: np.ndarray, is_loop: bool) -> dict:
    assert samples_f64.ndim == 1 and len(samples_f64) > 256, f"{filename}: invalid length"
    assert np.all(np.isfinite(samples_f64)), f"{filename}: contains NaN or Inf"

    peak = float(np.max(np.abs(samples_f64)))
    rms = float(np.sqrt(np.mean(samples_f64 ** 2)))
    assert 0.05 <= peak <= 0.96, f"{filename}: peak out of safe range ({peak:.4f})"
    assert rms >= 0.015, f"{filename}: signal too quiet (rms={rms:.5f})"

    if is_loop:
        # Check circular continuity across loop boundary: predicted next sample from slope vs sample[0]
        predicted_0 = float(samples_f64[-1] + (samples_f64[-1] - samples_f64[-2]))
        seam_step = abs(float(samples_f64[0]) - predicted_0)
        raw_jump = abs(float(samples_f64[0]) - float(samples_f64[-1]))
        assert seam_step < 0.015 and raw_jump < 0.08, (
            f"{filename}: loop seam discontinuity (step_err={seam_step:.5f}, jump={raw_jump:.5f})"
        )
    else:
        seam_step = 0.0
        assert abs(float(samples_f64[0])) < 1e-4 and abs(float(samples_f64[-1])) < 1e-4, (
            f"{filename}: non-zero endpoints could click"
        )

    pcm16 = np.clip(np.round(samples_f64 * 32767.0), -32767, 32767).astype(np.int16)
    clipped = int(np.sum(np.abs(pcm16) >= 32767))
    assert clipped == 0, f"{filename}: PCM16 clipped samples = {clipped}"

    path = out_dir / filename
    wavfile.write(str(path), SR, pcm16)
    return {
        "file": filename,
        "duration_s": round(len(samples_f64) / SR, 4),
        "peak": round(peak, 4),
        "rms": round(rms, 4),
        "is_loop": is_loop,
        "loop_seam_slope_err": round(seam_step, 6),
        "bytes": path.stat().st_size,
    }


def main() -> int:
    root = Path(__file__).resolve().parents[2]
    out_dir = root / "staging" / "shaders_audio" / "Audio"
    out_dir.mkdir(parents=True, exist_ok=True)

    rng = np.random.default_rng(20261111)
    specs: list[tuple[str, np.ndarray, bool]] = []

    for i in range(4):
        specs.append((f"sfx_crunch_0{i+1}.wav", gen_crunch(i, rng), False))
    specs.append(("sfx_boss_shatter.wav", gen_boss_shatter(rng), False))
    specs.append(("sfx_rotor_hum_loop.wav", gen_rotor_hum_loop(rng), True))
    for i in range(2):
        specs.append((f"sfx_orb_saw_0{i+1}.wav", gen_orb_saw(i, rng), False))
    specs.append(("sfx_pneumo_shot.wav", gen_pneumo_shot(rng), False))
    specs.append(("sfx_blackhole_loop.wav", gen_blackhole_loop(rng), True))
    specs.append(("sfx_golden_start.wav", gen_golden_start(rng), False))
    for i in range(5):
        specs.append((f"sfx_ingot_ding_0{i+1}.wav", gen_ingot_ding(i), False))
    specs.append(("sfx_chain_zap.wav", gen_chain_zap(rng), False))
    specs.append(("sfx_plasma_hit.wav", gen_plasma_hit(rng), False))
    specs.append(("sfx_ui_tap.wav", gen_ui_tap(), False))
    specs.append(("sfx_ui_upgrade.wav", gen_ui_upgrade(), False))
    specs.append(("sfx_ui_unlock.wav", gen_ui_unlock(), False))
    specs.append(("sfx_overload_death.wav", gen_overload_death(rng), False))

    results = [validate_and_write(out_dir, name, sig, is_loop) for name, sig, is_loop in specs]
    report = {
        "status": "PASS",
        "sample_rate": SR,
        "count": len(results),
        "files": results,
    }
    (out_dir / "audio_qa_report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    return 0


if __name__ == "__main__":
    sys.exit(main())
