import sys, asyncio, certifi
certifi.where = lambda: "/root/.ccr/ca-bundle.crt"
import edge_tts
async def main(text, out, voice, rate, pitch):
    await edge_tts.Communicate(text, voice, rate=rate, pitch=pitch).save(out)
voice = sys.argv[3] if len(sys.argv) > 3 else "ru-RU-DmitryNeural"
rate = sys.argv[4] if len(sys.argv) > 4 else "+15%"
pitch = sys.argv[5] if len(sys.argv) > 5 else "+0Hz"
asyncio.run(main(sys.argv[1], sys.argv[2], voice, rate, pitch))
