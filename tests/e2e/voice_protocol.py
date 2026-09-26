"""Run inside the deployed voice worker to check real Codex OAuth audio output."""
import asyncio
import base64
import json
from array import array
from voice_worker import CodexRealtimeSession


async def check():
    session = None
    try:
        session = await CodexRealtimeSession.start()
        print('Realtime transport started', flush=True)
        await session.speak('Voice protocol verification complete.')
        async with asyncio.timeout(45):
            while True:
                event = await session.notifications.get()
                method = event.get('method')
                if method == 'thread/realtime/error':
                    raise RuntimeError(event.get('params', {}).get('message', 'Realtime output failed'))
                if method == 'thread/realtime/outputAudio/delta':
                    encoded = event.get('params', {}).get('audio', {}).get('data', '')
                    samples = array('h', base64.b64decode(encoded))
                    peak = max((abs(sample) for sample in samples), default=0)
                    if peak < 1000:  # Ignore the continuous silent media frames.
                        continue
                    print(json.dumps({'audioOutput': True, 'transport': 'Codex CLI WebRTC + ChatGPT OAuth',
                                      'encodedAudioCharacters': len(encoded), 'peakPcmAmplitude': peak}), flush=True)
                    return
    finally:
        if session is not None:
            await session.close()


asyncio.run(check())
