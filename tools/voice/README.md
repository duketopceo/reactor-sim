# Assistant voice lines

`ReactorSim.Unity/Assets/Audio/Voice/*.wav` are the control-room assistant and autopilot lines: startup steps,
autopilot on/off, advice and alarms. Each file is named after its line key (for example `scram.wav`).

They are 24 kHz mono WAVs, synthesized with the Kokoro-82M model (voice `am_onyx`, Apache-2.0) through the
[Voicebox](https://voicebox.sh) app's local API, with a robot effects chain: pitch -2 semitones, chorus, 180 Hz
high-pass, 5 kHz low-pass, compressor.

## Regenerating or adding lines

1. Open Voicebox on a machine that has it (the API listens on `127.0.0.1:17493`).
2. Edit the `LINES` table in `voice_lines.py` (key = file name, value = text).
3. Run `python3 -I voice_lines.py`. It creates or reuses a "Reactor Assistant" profile and writes the WAVs to
   `~/Downloads/reactor-voice/`.
4. Copy the WAVs into `ReactorSim.Unity/Assets/Audio/Voice/`.

The lines are not wired into the game yet; that is part of the assistant and autopilot work in `ROADMAP.md`.
