import json, os, subprocess, sys

S = "/tmp/claude-0/-home-user-LABA6/c1812580-6106-549a-9c4e-d11767a67866/scratchpad"
SRC = {"A": f"{S}/src/a_641289134.mp4", "B": f"{S}/src/a_641292714.mp4"}
OUT = f"{S}/build"
os.makedirs(OUT, exist_ok=True)
FPS = 30
W, H = 1080, 1920
CARD_W, CARD_H, CARD_Y = 1020, 874, 480  # foreground "card" in the 9:16 frame


def run(cmd):
    r = subprocess.run(cmd, capture_output=True, text=True)
    if r.returncode:
        print(" ".join(cmd)); print(r.stderr[-3000:]); sys.exit(1)


# piece: (src, start, end, speed, slowmo_interp)
SEGS = [
    dict(name="hook", pieces=[("A", 25.2, 28.1, 1)], punch=[0.8], flash=[0.8], shake=[(0.8, 0.45)]),
    dict(name="worlds", pieces=[("A", 0.3, 12.0, 2.2)], punch=[0.05, 3.5], flash=[3.5], shake=[]),
    dict(name="steal", pieces=[("A", 13.9, 17.3, 1)], punch=[2.6], flash=[2.6], shake=[(2.6, 0.5)]),
    dict(name="flyby", pieces=[("A", 20.0, 20.5, 1), ("A", 20.5, 21.1, 0.3), ("A", 21.1, 22.6, 1)],
         punch=[0.5, 2.5], flash=[2.5], shake=[(0.45, 0.3), (2.5, 0.4)], slowzoom=(0.5, 2.5)),
    dict(name="home", pieces=[("A", 72.6, 76.3, 1.3), ("A", 76.3, 78.6, 1)], punch=[3.45], flash=[3.45],
         shake=[(3.45, 0.45)]),
    dict(name="beforeafter", pieces=[("B", 2.3, 5.0, 1), ("B", 22.9, 25.6, 1.35), ("B", 29.6, 32.1, 1)],
         punch=[2.7, 3.144, 3.737, 4.256, 4.7], flash=[3.144, 3.737, 4.256, 4.7], shake=[(4.7, 0.6)]),
    dict(name="split", split=(("B", 3.0, 4.6), ("B", 32.1, 33.7)), dur=1.6),
    dict(name="outro", pieces=[("A", 83.4, 90.0, 1)], punch=[0.6], flash=[0.0], shake=[]),
]


def seg_dur(sg):
    if "split" in sg: return sg["dur"]
    return sum((e - s) / sp for _, s, e, sp in sg["pieces"])


def zexpr(sg, tv):
    z = "1.06"
    for p in sg.get("punch", []):
        z += f"+0.22*exp(-({tv}-{p})*7)*gte({tv},{p})"
    if "slowzoom" in sg:
        a, b = sg["slowzoom"]
        z += f"+0.18*clip(({tv}-{a})/({b}-{a}),0,1)*lt({tv},{b})"
    return z


def shake(sg, tv, seed):
    s = "0"
    for (t0, d) in sg.get("shake", []):
        s += f"+28*exp(-({tv}-{t0})*5)*between({tv},{t0},{t0 + d})*sin({tv}*{seed})"
    return s


def render_segment(i, sg):
    out = f"{OUT}/seg{i}.mp4"
    args = ["ffmpeg", "-y", "-v", "error"]
    fc = []
    if "split" in sg:
        for k, (src, s, e) in enumerate(sg["split"]):
            args += ["-ss", str(s), "-t", str(e - s), "-i", SRC[src]]
            fc.append(f"[{k}:v]setpts=PTS-STARTPTS,fps={FPS},crop=810:720:235:0,scale=1080:960,"
                      f"eq=saturation=1.25:contrast=1.06[h{k}]")
        fc.append("[h0][h1]vstack=2,drawbox=x=0:y=954:w=1080:h=12:color=white:t=fill,"
                  "eq=brightness='0.5*exp(-t*9)':eval=frame,format=yuv420p[v]")
    else:
        labels = []
        for k, (src, s, e, sp) in enumerate(sg["pieces"]):
            args += ["-ss", str(s), "-t", str(e - s), "-i", SRC[src]]
            f = f"[{k}:v]setpts=(PTS-STARTPTS)/{sp},"
            if sp < 1:
                f += f"minterpolate=fps={FPS}:mi_mode=mci:mc_mode=aobmc:vsbmc=1,"
            f += f"fps={FPS},scale=1280:720,setsar=1[p{k}]"
            fc.append(f); labels.append(f"[p{k}]")
        n = len(labels)
        fc.append(f"{''.join(labels)}concat=n={n}:v=1:a=0,eq=saturation=1.3:contrast=1.08,split[c1][c2]")
        # blurred background
        fc.append("[c1]scale=270:480:force_original_aspect_ratio=increase,crop=270:480,boxblur=8:2,"
                  "scale=1080:1920,eq=brightness=-0.18:saturation=1.4[bg]")
        z = zexpr(sg, "it")
        sx, sy = shake(sg, "it", 61), shake(sg, "it", 47)
        fc.append(f"[c2]crop=840:720:220:0,scale=2040:1748,"
                  f"zoompan=z='{z}':x='iw/2-iw/zoom/2+({sx})':y='ih/2-ih/zoom/2+({sy})':d=1:"
                  f"s={CARD_W}x{CARD_H}:fps={FPS}[fg]")
        br = "0"
        for t0 in sg.get("flash", []):
            br += f"+0.55*exp(-(t-{t0})*9)*gte(t,{t0})"
        fc.append(f"[bg]drawbox=x={(W - CARD_W) // 2 - 7}:y={CARD_Y - 7}:w={CARD_W + 14}:h={CARD_H + 14}:"
                  f"color=white:t=fill[bgb];[bgb][fg]overlay={(W - CARD_W) // 2}:{CARD_Y}:shortest=1,"
                  f"vignette=PI/5,eq=brightness='{br}':eval=frame,format=yuv420p[v]")
    args += ["-filter_complex", ";".join(fc), "-map", "[v]", "-t", f"{seg_dur(sg):.3f}",
             "-c:v", "libx264", "-preset", "medium", "-crf", "16", "-r", str(FPS), out]
    run(args)
    return out


# ---------------- timeline ----------------
starts, t = [], 0.0
for sg in SEGS:
    starts.append(t); t += seg_dur(sg)
TOTAL = t
st = {sg["name"]: s for sg, s in zip(SEGS, starts)}
DROP = st["beforeafter"] + 4.7

words = json.load(open(f"{S}/tts/words.json"))
VO = {"v1": 0.1, "v2": st["worlds"] + 0.1, "v3": st["steal"] + 0.1, "v4": st["flyby"] + 0.1,
      "v5": st["home"] + 0.1, "v6": DROP - words["v6"][-1]["t"], "v7": st["outro"] + 0.2}
DISPLAY = {"v7": {0: "STEAL", 1: "AN", 2: "EGG", 3: "ON", 4: "PLANE", 7: "ROBLOX"}}


def ts(x):
    x = max(0, x); h = int(x // 3600); m = int(x % 3600 // 60); s = x % 60
    return f"{h}:{m:02d}:{s:05.2f}"


def build_ass():
    L = ["[Script Info]", "ScriptType: v4.00+", f"PlayResX: {W}", f"PlayResY: {H}", "WrapStyle: 2", "",
         "[V4+ Styles]",
         "Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic,"
         " Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL,"
         " MarginR, MarginV, Encoding",
         "Style: Cap,Montserrat Black,104,&H00FFFFFF,&H00FFFFFF,&H00000000,&H96000000,0,0,0,0,100,100,0,0,1,9,5,5,40,40,0,1",
         "Style: Title,Russo One,104,&H0000E5FF,&H00FFFFFF,&H00000000,&H96000000,0,0,0,0,100,100,2,0,1,10,6,5,30,30,0,1",
         "Style: Big,Russo One,190,&H00FFFFFF,&H00FFFFFF,&H00000000,&H96000000,0,0,0,0,100,100,4,0,1,14,8,5,30,30,0,1",
         "", "[Events]",
         "Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text"]

    def ev(a, b, style, text, layer=0):
        L.append(f"Dialogue: {layer},{ts(a)},{ts(b)},{style},,0,0,0,,{text}")

    pop = r"\fscx150\fscy150\t(0,120,\fscx100\fscy100)"
    # top titles (yellow), per segment
    titles = [
        ("hook", 0.0, r"УКРАЛ ЯЙЦО\NНА САМОЛЁТЕ", -4),
        ("worlds", 0.0, r"ВСЕ МИРЫ\NЗА 5 СЕКУНД", 3),
        ("steal", 0.0, r"КРАЖА ЯЙЦА", -3),
        ("flyby", 0.0, r"{\c&H2A2AFF&}ЖЁСТКИЙ\NПРОЛЁТ", 4),
        ("home", 0.0, r"ЯЙЦО ДОМА {\c&H55FF55&}+$$$", -3),
    ]
    for name, off, text, rot in titles:
        i = [s["name"] for s in SEGS].index(name)
        a = starts[i] + off; b = starts[i] + seg_dur(SEGS[i])
        ev(a, b, "Title", rf"{{\pos(540,270)\frz{rot}{pop if name != 'hook' else ''}}}{text}")
    # slow-mo tag
    fb = st["flyby"]
    ev(fb + 0.5, fb + 2.5, "Cap", r"{\pos(870,520)\fs56\c&H00E5FF&\frz8\fad(80,80)}SLOW-MO", 2)
    # +$1 pop
    hm = st["home"] + 3.45
    ev(hm, hm + 1.4, "Big", r"{\pos(540,860)\c&H55FF55&\fs170" + pop + r"\t(700,1400,\alpha&HFF&)}+$1", 2)
    # before / after
    ba = st["beforeafter"]
    ev(ba, ba + 2.7, "Big", r"{\pos(540,270)\c&H3030FF&" + pop + r"}ДО")
    ev(ba + 2.7, ba + 4.7, "Title", r"{\pos(540,270)" + pop + r"\t(0,2000,\fscx120\fscy120)}ПРОКАЧКА...")
    ev(ba + 4.7, ba + 7.2, "Big", r"{\pos(540,270)\c&H55FF55&\fs170" + pop + r"}ПОСЛЕ")
    ev(ba + 4.7, ba + 7.2, "Title", r"{\pos(540,1440)\c&H00E5FF&\frz-4" + pop + r"}МАКС ЛЕВЕЛ!", 2)
    sp = st["split"]
    ev(sp, sp + 1.6, "Big", r"{\pos(200,140)\fs130\c&H3030FF&" + pop + r"}ДО", 3)
    ev(sp, sp + 1.6, "Big", r"{\pos(330,1100)\fs130\c&H55FF55&" + pop + r"}ПОСЛЕ", 3)
    # outro card
    oc = st["outro"]
    ev(oc, TOTAL, "Title", r"{\pos(540,250)\fs110" + pop + r"}STEAL AN EGG\N{\c&HFFFFFF&}ON PLANE")
    ev(oc + 2.3, TOTAL, "Cap", r"{\pos(540,1440)\c&H55FF55&\fs76" + pop + r"}ИГРАЙ В ROBLOX!", 2)
    ev(TOTAL - 2.6, TOTAL, "Cap", r"{\pos(540,1580)\fs64" + pop + r"}ПОДПИШИСЬ!", 2)
    # word-by-word captions from VO timings
    for k, t0 in VO.items():
        if k == "v7":  # outro uses its own captions
            continue
        ws = words[k]
        disp = [DISPLAY.get(k, {}).get(j, w["w"]).upper() for j, w in enumerate(ws)]
        chunks, cur = [], []
        for j in range(len(ws)):
            cur.append(j)
            if len(cur) == 3 or sum(len(disp[x]) for x in cur) > 14 or j == len(ws) - 1:
                chunks.append(cur); cur = []
        for ci, ch in enumerate(chunks):
            for pos, j in enumerate(ch):
                a = t0 + ws[j]["t"]
                if pos + 1 < len(ch):
                    b = t0 + ws[ch[pos + 1]]["t"]
                elif ci + 1 < len(chunks):
                    b = t0 + ws[chunks[ci + 1][0]]["t"]
                else:
                    b = t0 + ws[j]["t"] + ws[j]["d"] + 0.35
                parts = []
                for x in ch:
                    if x == j:
                        parts.append(r"{\c&H00E5FF&\fscx112\fscy112}" + disp[x] + r"{\c&HFFFFFF&\fscx100\fscy100}")
                    else:
                        parts.append(disp[x])
                intro = r"\fscx120\fscy120\t(0,90,\fscx100\fscy100)" if pos == 0 else ""
                ev(a, b, "Cap", rf"{{\pos(540,1560){intro}}}" + " ".join(parts), 1)
    with open(f"{OUT}/subs.ass", "w") as f:
        f.write("\n".join(L) + "\n")


def build_audio():
    sfx = [("boom", 0.8, 0.9), ("boom", st["steal"] + 2.6, 0.8), ("boom", st["home"] + 3.45, 0.7),
           ("ding", st["home"] + 3.5, 0.5), ("boom", DROP, 1.0), ("riser", DROP - 2.0, 0.45),
           ("slow", st["flyby"] + 0.5, 0.6), ("scratch", st["beforeafter"] - 0.05, 0.5),
           ("boom", st["flyby"] + 2.5, 0.5), ("boom", st["outro"], 0.6)]
    for nm in ("worlds", "steal", "flyby", "home", "split", "outro"):
        sfx.append(("whoosh", st[nm] - 0.3, 0.55))
    args = ["ffmpeg", "-y", "-v", "error", "-i", f"{S}/audio/music.wav"]
    fc, vlabels, slabels = [], [], []
    idx = 1
    for k, t0 in VO.items():
        args += ["-i", f"{S}/tts/{k}.mp3"]
        fc.append(f"[{idx}:a]aresample=44100,aformat=channel_layouts=stereo,"
                  f"adelay={int(t0 * 1000)}|{int(t0 * 1000)},volume=1.6,apad=whole_dur={TOTAL + 0.5:.3f}[vo{idx}]")
        vlabels.append(f"[vo{idx}]"); idx += 1
    for nm, t0, g in sfx:
        args += ["-i", f"{S}/audio/{nm}.wav"]
        fc.append(f"[{idx}:a]adelay={int(t0 * 1000)}|{int(t0 * 1000)},volume={g}[s{idx}]")
        slabels.append(f"[s{idx}]"); idx += 1
    fc.append(f"{''.join(vlabels)}amix=inputs={len(vlabels)}:normalize=0,"
              f"acompressor=threshold=0.1:ratio=4:attack=5:release=80,asplit[vo][key]")
    fc.append(f"[0:a]volume=0.32[mus];[mus][key]sidechaincompress=threshold=0.04:ratio=8:attack=20:release=300[duck]")
    fc.append(f"{''.join(slabels)}amix=inputs={len(slabels)}:normalize=0,volume=0.55[fx]")
    fc.append(f"[duck][vo][fx]amix=inputs=3:normalize=0,alimiter=limit=0.95,"
              f"atrim=0:{TOTAL:.3f},afade=t=out:st={TOTAL - 0.6:.3f}:d=0.6[a]")
    args += ["-filter_complex", ";".join(fc), "-map", "[a]", "-ar", "44100", f"{OUT}/mix.wav"]
    run(args)


if __name__ == "__main__":
    print(f"total {TOTAL:.3f}s, drop {DROP:.3f}s", {k: round(v, 2) for k, v in st.items()})
    only = sys.argv[1:]
    if "post" not in only:
        segs = []
        for i, sg in enumerate(SEGS):
            segs.append(render_segment(i, sg)); print("rendered", sg["name"], flush=True)
        with open(f"{OUT}/list.txt", "w") as f:
            f.writelines(f"file '{p}'\n" for p in segs)
        run(["ffmpeg", "-y", "-v", "error", "-f", "concat", "-safe", "0", "-i", f"{OUT}/list.txt",
             "-c", "copy", f"{OUT}/joined.mp4"])
    build_ass()
    build_audio()
    run(["ffmpeg", "-y", "-v", "error", "-i", f"{OUT}/joined.mp4", "-i", f"{OUT}/mix.wav",
         "-vf", f"subtitles={OUT}/subs.ass:fontsdir={S}/fonts",
         "-map", "0:v", "-map", "1:a", "-c:v", "libx264", "-preset", "slow", "-crf", "19",
         "-profile:v", "high", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k",
         "-movflags", "+faststart", "-shortest", f"{OUT}/final.mp4"])
    print("done")
