import sys, json, asyncio, certifi
certifi.where = lambda: "/root/.ccr/ca-bundle.crt"
import edge_tts
LINES = {
 "v1": "Я ворую яйца... на самолёте!",
 "v2": "Пролетаю все миры на бешеной скорости.",
 "v3": "Залетаю прямо на замок и забираю яйцо!",
 "v4": "А теперь — жёсткий пролёт. Впритык!",
 "v5": "Между домами, и... яйцо дома! Деньги капают.",
 "v6": "Было: деревянное колесо. Прокачка... и стало — МАКСИМАЛКА!",
 "v7": "Стил эн Эгг он Плейн. Заходи в Роблокс и укради своё первое яйцо!",
}
async def one(k, text, out):
    c = edge_tts.Communicate(text, "ru-RU-DmitryNeural", rate="+18%", pitch="+4Hz", boundary="WordBoundary")
    words = []
    with open(f"{out}/{k}.mp3", "wb") as f:
        async for ch in c.stream():
            if ch["type"] == "audio": f.write(ch["data"])
            elif ch["type"] == "WordBoundary":
                words.append({"t": ch["offset"]/1e7, "d": ch["duration"]/1e7, "w": ch["text"]})
    return words
async def main(out):
    res = {}
    for k, t in LINES.items():
        res[k] = await one(k, t, out)
    json.dump(res, open(f"{out}/words.json", "w"), ensure_ascii=False, indent=1)
asyncio.run(main(sys.argv[1]))
