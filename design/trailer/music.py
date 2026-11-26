# Спокійна електроніка для трейлера (60 с, 100 BPM, Am–F–C–G). Усе синтезовано тут.
import numpy as np, wave, sys
SR = 44100; D = 60.0; N = int(SR * D); t = np.arange(N) / SR
L = np.zeros(N); R = np.zeros(N)
BPM = 100; BEAT = 60 / BPM; BAR = 4 * BEAT
def hz(n): return 440 * 2 ** ((n - 69) / 12)
CH = [(45, [57, 60, 64, 69]), (41, [53, 57, 60, 65]), (48, [55, 60, 64, 67]), (43, [55, 59, 62, 67])]  # Am F C G
def chord_at(x): return CH[int(x // (2 * BAR)) % 4]
def place(sig, start, ch=(1, 1)):
    s = int(start * SR); e = min(N, s + len(sig))
    if s >= N or e <= 0: return
    L[s:e] += sig[:e - s] * ch[0]; R[s:e] += sig[:e - s] * ch[1]
def adsr(n, a, d, sus, r, total):
    tt = np.arange(n) / SR; env = np.minimum(1, tt / max(a, 1e-4))
    env = np.where(tt > a, sus + (1 - sus) * np.exp(-(tt - a) / max(d, 1e-4)), env)
    rel = np.clip((total - tt) / r, 0, 1); return env * rel
# Інтенсивність секцій
def level(x):
    if x < 9.5: return .55
    if x < 13.5: return .7
    if x < 49: return 1.0
    if x < 54: return .75
    return 1.0
# Пед: м'які «пилки» з 4 гармонік, легкий розлад L/R, повільна атака
for k in range(int(D / (2 * BAR)) + 1):
    st = k * 2 * BAR
    if st >= D: break
    root, notes = chord_at(st); dur = 2 * BAR + .4; n = int(dur * SR); tt = np.arange(n) / SR
    env = adsr(n, 1.2, 2, .85, 1.0, dur) * level(st) * (0.6 if st >= 54 else 1)
    for note in notes:
        f = hz(note); sl = sr = 0
        for h, a in ((1, 1), (2, .28), (3, .1), (4, .04)):
            sl = sl + a * np.sin(2 * np.pi * f * h * 0.997 * tt); sr = sr + a * np.sin(2 * np.pi * f * h * 1.003 * tt + .7)
        place(.045 * env * sl, st, (1, 0)); place(.045 * env * sr, st, (0, 1))
# Бас: на кожну долю 13.5–49, корінь акорду
b = 13.5
while b < 49:
    root, _ = chord_at(b); n = int(.55 * SR); tt = np.arange(n) / SR
    sig = (np.sin(2 * np.pi * hz(root) * tt) + .25 * np.sin(4 * np.pi * hz(root) * tt)) * np.exp(-tt * 5) * np.minimum(1, tt / .01)
    place(.16 * sig, b); b += BEAT
# Бочка: 13.5–49 на кожну долю, і удари на логотипі й даті
def kick(at, amp=.5):
    n = int(.45 * SR); tt = np.arange(n) / SR; f = 45 + 75 * np.exp(-tt * 28)
    place(amp * np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-tt * 9), at)
b = 13.5
while b < 49: kick(b, .42); b += BEAT
kick(9.6, .55); kick(54.0, .7)
# Хет: між долями 19–49 (шум, лише високі)
rng = np.random.default_rng(7)
b = 19 + BEAT / 2
while b < 49:
    n = int(.06 * SR); nz = rng.standard_normal(n); nz = np.diff(np.concatenate([[0], nz]))
    sig = nz * np.exp(-np.arange(n) / SR * 70); place(.035 * sig, b, (.7, 1)); b += BEAT
# Арпеджіо: шістнадцяті, 24.5–53.5, ноти акорду на октаву вище, «пінг-понг»
b = 24.5; i = 0
while b < 53.5:
    _, notes = chord_at(b); note = notes[[0, 1, 2, 3, 2, 1][i % 6]] + 12; n = int(.3 * SR); tt = np.arange(n) / SR
    sig = (np.sin(2 * np.pi * hz(note) * tt) + .2 * np.sin(6 * np.pi * hz(note) * tt)) * np.exp(-tt * 14) * np.minimum(1, tt / .003)
    place(.05 * sig * level(b), b, (1, .55) if i % 2 else (.55, 1)); b += BEAT / 4; i += 1
# Шум-підйом перед логотипом і датою
def riser(a, b2, amp=.06):
    n = int((b2 - a) * SR); nz = rng.standard_normal(n); x = np.linspace(0, 1, n)
    k = np.maximum(2, (2 + 30 * x).astype(int))
    sm = np.convolve(nz, np.ones(32) / 32, mode='same'); sig = (nz - sm) * x ** 2
    place(amp * sig, a, (1, 1))
riser(7.8, 9.6, .05); riser(51.5, 54.0, .07)
# Пінги подій (тихо)
def ping(at, amp=.07):
    for f, d in ((1318.5, 0), (1975.5, .11)):
        n = int(.6 * SR); tt = np.arange(n) / SR; place(amp * np.sin(2 * np.pi * f * tt) * np.exp(-tt * 7), at + d, (1, 1))
for at in (21.4, 44.4, 45.6, 46.8): ping(at)
# Майстер: м'яке обмеження, згасання 57–60
mix = np.stack([L, R], 1)
fade = np.clip((D - t) / 3.0, 0, 1)[:, None] * np.clip(t / .5, 0, 1)[:, None]
mix = np.tanh(mix * 1.6) * fade
mix /= np.max(np.abs(mix)) / .9
w = wave.open(sys.argv[1], 'wb'); w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR)
w.writeframes((mix * 32767).astype('<i2').tobytes()); w.close(); print('ok')
