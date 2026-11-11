# Third-party notices

Reactor Sim's own code and assets are MIT licensed (see [LICENSE](LICENSE)). The components below are third-party.

## Liberation Sans (font)

`ReactorSim.Unity/Assets/TextMesh Pro/Fonts/LiberationSans.ttf`, bundled with the TextMesh Pro essential resources.
Licensed under the SIL Open Font License 1.1. The full license text and copyright notices are kept beside the font:
`ReactorSim.Unity/Assets/TextMesh Pro/Fonts/LiberationSans - OFL.txt`.

## Inter (font)

`ReactorSim.Unity/Assets/Fonts/Inter-Regular.ttf` and `Inter-Bold.ttf`, from Inter 4.1
(https://github.com/rsms/inter), the proportional UI face. Copyright (c) 2016 The Inter Project Authors.
Licensed under the SIL Open Font License 1.1. The license text is kept beside the fonts:
`ReactorSim.Unity/Assets/Fonts/Inter-LICENSE.txt`. The SDF font assets generated from them by
`TmpBootstrap.GenerateFonts` are derived from the same fonts and carry the same license.

## IBM Plex Mono (font)

`ReactorSim.Unity/Assets/Fonts/IBMPlexMono-Regular.ttf`, from IBM Plex Mono 1.1.0
(https://github.com/IBM/plex), the monospace face for screens and numeric readouts. Copyright (c) 2017 IBM Corp.
with Reserved Font Name "Plex". Licensed under the SIL Open Font License 1.1. The license text is kept beside the
font: `ReactorSim.Unity/Assets/Fonts/IBMPlexMono-LICENSE.txt`. The generated SDF font asset carries the same license.

## Kokoro voice model (assistant voice lines)

The WAV files in `ReactorSim.Unity/Assets/Audio/Voice/` were synthesized with the Kokoro-82M text-to-speech model
(voice `am_onyx`), run locally through the Voicebox app, then processed with Voicebox's built-in audio effects
(pitch shift, chorus, band-pass, compressor). Kokoro-82M is released under the Apache License 2.0. The line texts
are this project's own. The generator script is `tools/voice/voice_lines.py`.

## Unity packages and TextMesh Pro

The `ReactorSim.Unity` project depends on Unity Technologies packages (see `ReactorSim.Unity/Packages/manifest.json`)
and includes the TextMesh Pro essential resources (shaders, settings and sprite assets under
`ReactorSim.Unity/Assets/TextMesh Pro/`). These are distributed under the Unity Companion License / Unity package
license terms and the Unity Software terms of the Unity version used to build the project; they are not covered by
this repository's MIT license. Building or running the Unity front-end requires accepting Unity's terms.

## NuGet packages

The test and runner projects restore xUnit and related packages from NuGet at build time. They are not
redistributed in this repository; each is under its own license (xUnit: Apache-2.0).
