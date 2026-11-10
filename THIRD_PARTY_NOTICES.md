# Third-party notices

Reactor Sim's own code and assets are MIT licensed (see [LICENSE](LICENSE)). The components below are third-party.

## Liberation Sans (font)

`ReactorSim.Unity/Assets/TextMesh Pro/Fonts/LiberationSans.ttf`, bundled with the TextMesh Pro essential resources.
Licensed under the SIL Open Font License 1.1. The full license text and copyright notices are kept beside the font:
`ReactorSim.Unity/Assets/TextMesh Pro/Fonts/LiberationSans - OFL.txt`.

## Unity packages and TextMesh Pro

The `ReactorSim.Unity` project depends on Unity Technologies packages (see `ReactorSim.Unity/Packages/manifest.json`)
and includes the TextMesh Pro essential resources (shaders, settings and sprite assets under
`ReactorSim.Unity/Assets/TextMesh Pro/`). These are distributed under the Unity Companion License / Unity package
license terms and the Unity Software terms of the Unity version used to build the project; they are not covered by
this repository's MIT license. Building or running the Unity front-end requires accepting Unity's terms.

## NuGet packages

The test and runner projects restore xUnit and related packages from NuGet at build time. They are not
redistributed in this repository; each is under its own license (xUnit: Apache-2.0).
