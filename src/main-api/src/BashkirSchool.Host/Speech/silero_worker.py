"""Private stdin/stdout protocol. No HTTP server, user scripts or shell commands."""
import base64
import contextlib
import io
import json
import struct
import sys
import wave

# Keep third-party diagnostics away from the JSON protocol.
with contextlib.redirect_stdout(sys.stderr):
    import torch
    torch.set_num_threads(int(sys.argv[2]))
    model = torch.package.PackageImporter(sys.argv[1]).load_pickle("tts_models", "model")
    model.to(torch.device("cpu"))

for line in sys.stdin:
    try:
        request = json.loads(line)
        with contextlib.redirect_stdout(sys.stderr), torch.inference_mode():
            audio = model.apply_tts(text=request["text"], speaker=request["speaker"], sample_rate=request["sampleRate"])
            samples = audio.detach().cpu().clamp(-1, 1).mul(32767).to(torch.int16).tolist()
        output = io.BytesIO()
        with wave.open(output, "wb") as wav:
            wav.setnchannels(1)
            wav.setsampwidth(2)
            wav.setframerate(request["sampleRate"])
            wav.writeframes(struct.pack("<" + "h" * len(samples), *samples))
        result = {"wav": base64.b64encode(output.getvalue()).decode("ascii")}
    except Exception as error:
        result = {"error": str(error)}
    print(json.dumps(result), flush=True)
