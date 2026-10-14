import numpy as np, wave, sys
SR = 44100
rng = np.random.default_rng(7)

def save(path, x):
    x = np.asarray(x, dtype=np.float64)
    if x.ndim == 1: x = np.stack([x, x], 1)
    peak = np.max(np.abs(x)) or 1
    x = x / peak * 0.95
    with wave.open(path, "wb") as w:
        w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes((x * 32767).astype("<i2").tobytes())

def t_(d): return np.arange(int(d * SR)) / SR

def lowpass(x, a):  # one-pole
    y = np.empty_like(x); acc = 0.0
    for i, v in enumerate(x):
        acc += a * (v - acc); y[i] = acc
    return y

def hp(x, a): return x - lowpass(x, a)

# ---------- drums ----------
def kick(d=0.45):
    t = t_(d); f = 45 + 160 * np.exp(-t * 28)
    ph = 2 * np.pi * np.cumsum(f) / SR
    return np.tanh(2.2 * np.sin(ph) * np.exp(-t * 7))

def b808(freq, d):
    t = t_(d); f = freq * (1 + 0.6 * np.exp(-t * 30))
    ph = 2 * np.pi * np.cumsum(f) / SR
    env = np.minimum(1, t * 200) * np.exp(-t * 1.6)
    return np.tanh(3.0 * np.sin(ph) * env) * 0.8

def clap():
    t = t_(0.25); n = rng.uniform(-1, 1, len(t))
    env = np.exp(-t * 22) + 0.6 * np.exp(-np.abs(t - 0.012) * 300) + 0.5 * np.exp(-np.abs(t - 0.024) * 300)
    return hp(n, 0.25) * env * 0.7

def hat(d=0.05, open_=False):
    t = t_(0.18 if open_ else d); n = rng.uniform(-1, 1, len(t))
    return hp(hp(n, 0.6), 0.6) * np.exp(-t * (14 if open_ else 70)) * 0.35

def cowbell(freq, d=0.28):
    t = t_(d)
    s = np.sign(np.sin(2 * np.pi * freq * t)) + 0.8 * np.sign(np.sin(2 * np.pi * freq * 1.48 * t))
    s = hp(s, 0.08)
    env = np.exp(-t * 9) * np.minimum(1, t * 400)
    return np.tanh(1.5 * s * env) * 0.45

def place(buf, x, at, g=1.0):
    i = int(at * SR)
    if i >= len(buf): return
    n = min(len(x), len(buf) - i)
    buf[i:i + n] += g * x[:n]

def note(n):  # midi -> Hz
    return 440 * 2 ** ((n - 69) / 12)

def music(total, drop_at, intro_quiet=0.0):
    bpm = 130; beat = 60 / bpm; step = beat / 4
    L = np.zeros(int((total + 1) * SR)); R = np.zeros_like(L)
    # F# minor phonk cowbell riff (16 steps x 2 bars)
    riff = [66, None, 69, None, 66, 73, None, 71, 69, None, 66, None, 64, None, 66, None,
            66, None, 69, None, 66, 73, None, 76, 73, None, 71, None, 69, None, 68, None]
    bass = [42, 42, 45, 40]  # per bar roots
    nsteps = int(total / step) + 1
    for s in range(nsteps):
        at = s * step
        bar = s // 16; pos = s % 16
        after = at >= drop_at
        half = at < drop_at and at > drop_at - 2.2  # build before drop
        # kick
        kpat = (0, 6, 10) if not after else (0, 3, 6, 10, 14)
        if pos in kpat and not half: place(L, kick(), at, 0.9)
        # 808
        if pos in (0, 6, 10) and not half:
            place(L, b808(note(bass[bar % 4] - 12 + 12), step * (6 if pos == 0 else 4)), at, 0.55)
        # clap on 2 & 4
        if pos in (4, 12) and not half: place(L, clap(), at, 0.6)
        # hats, rolls every other bar end
        if pos % 2 == 0 or (bar % 2 == 1 and pos >= 12): place(L, hat(), at, 0.5 if pos % 4 == 0 else 0.35)
        # cowbell
        r = riff[s % 32]
        if r is not None and (s >= 0):
            g = 0.55 if after else 0.42
            place(R, cowbell(note(r)), at, g); place(L, cowbell(note(r)), at + 0.008, g * 0.6)
    # build-up snare roll into drop
    for k in range(16):
        at = drop_at - 2.0 + k * 2.0 / 16
        place(L, clap(), at, 0.15 + 0.5 * k / 16)
    mix = np.stack([L + 0.7 * R, R + 0.7 * L], 1)
    return mix[: int(total * SR)]

# ---------- sfx ----------
def whoosh(d=0.6, up=True):
    t = t_(d); n = rng.uniform(-1, 1, len(t))
    env = np.sin(np.pi * t / d) ** 2
    cut = np.linspace(0.02, 0.35, len(t)) if up else np.linspace(0.35, 0.02, len(t))
    y = np.empty_like(n); acc = 0.0
    for i, v in enumerate(n):
        acc += cut[i] * (v - acc); y[i] = acc
    return y * env * 1.4

def boom(d=1.4):
    t = t_(d); f = 38 + 90 * np.exp(-t * 10)
    ph = 2 * np.pi * np.cumsum(f) / SR
    s = np.sin(ph) * np.exp(-t * 2.2)
    n = lowpass(rng.uniform(-1, 1, len(t)), 0.05) * np.exp(-t * 6) * 2
    return np.tanh(2.5 * (s + n))

def ding():
    t = t_(1.0)
    s = sum(a * np.sin(2 * np.pi * f * t) * np.exp(-t * k) for f, a, k in
            [(1568, 0.6, 4), (2093, 0.5, 5), (3136, 0.25, 7), (4186, 0.15, 9)])
    s2 = np.concatenate([np.zeros(int(0.09 * SR)), s])[: len(t)]
    return s + 0.8 * s2

def riser(d=2.0):
    t = t_(d); f = 200 * (8 ** (t / d))
    ph = 2 * np.pi * np.cumsum(f) / SR
    n = hp(rng.uniform(-1, 1, len(t)), 0.3)
    return (0.4 * np.sign(np.sin(ph)) * 0.5 + 0.5 * n) * (t / d) ** 2

def slowdown(d=1.2):
    t = t_(d); f = 600 * (0.1 ** (t / d))
    ph = 2 * np.pi * np.cumsum(f) / SR
    return np.sin(ph) * np.exp(-t * 1.5) * 0.8 + 0.6 * whoosh(d, up=False)

def scratch():
    t = t_(0.35); f = 300 + 900 * np.abs(np.sin(2 * np.pi * 4.3 * t))
    ph = 2 * np.pi * np.cumsum(f) / SR
    n = rng.uniform(-1, 1, len(t))
    return (np.sign(np.sin(ph)) * 0.4 + 0.3 * n) * np.exp(-t * 4)

if __name__ == "__main__":
    out = sys.argv[1]; total = float(sys.argv[2]); drop = float(sys.argv[3])
    save(f"{out}/music.wav", music(total, drop))
    save(f"{out}/whoosh.wav", whoosh())
    save(f"{out}/boom.wav", boom())
    save(f"{out}/ding.wav", ding())
    save(f"{out}/riser.wav", riser())
    save(f"{out}/slow.wav", slowdown())
    save(f"{out}/scratch.wav", scratch())
