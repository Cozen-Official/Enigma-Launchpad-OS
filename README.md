# Enigma OS

> **Heads up — this is the new asset.** The original Enigma Launchpad OS (v1.x) was a different architecture and is no longer maintained on this branch. It is preserved on the [`legacy/launchpad`](https://github.com/Cozen-Official/Enigma-Launchpad-OS/tree/legacy/launchpad) branch and as the [`archive-launchpad-final`](https://github.com/Cozen-Official/Enigma-Launchpad-OS/releases/tag/archive-launchpad-final) release. Enigma OS is a complete rewrite and is **not** source-compatible with v1.x.

Enigma OS is a fully-configurable in-world control surface for VRChat worlds. It pairs the look and feel of a hardware MIDI launchpad or DJ mixer with a flexible action system that lets you wire up nearly any in-world behaviour — material swaps, screen shader effects, skybox changes, AudioLink modulation, GameObject toggles, Udon variable writes, transform changes, teleports, and more — all without writing a single line of code. Configuration happens entirely through the Unity inspector: you build folders of buttons, drop actions onto each one from a categorized picker, and Enigma OS bakes everything down to runtime arrays consumed by a single Udon executor at play time.

Out of the box, Enigma OS ships with two ready-to-use prefabs (a button-only Launchpad and a fader-equipped Mixer with a built-in AudioLink controller and screen-shader display), a library of templates covering common patterns (object toggles, material swaps, skybox switchers, world-stat displays, persistent presets, and turnkey folders for popular shader sets), and integrated support for popular third-party systems (OhGeezCmon Access Control, ProTV, Flatline). Advanced features include exclusivity tags, time-based auto-changing, conditional and stepped actions, color palettes, persistent per-user preset storage via VRChat PlayerData, dynamic faders that re-bind based on which buttons are active, and multi-controller support with room boundaries for spatially-separated installs.

The Mixer prefab's design is inspired by the Roland VR-50HD AV Mixer.

## Installation

1. Import [AudioLink](https://github.com/llealloo/audiolink) via VRChat Creator Companion.
2. Download the latest `.unitypackage` from the [Releases](https://github.com/Cozen-Official/Enigma-Launchpad-OS/releases) page and import it into your world project. Files land under `Assets/Cozen/Enigma OS/`.
3. Drag the **Enigma Launchpad** prefab (buttons only) or the **Enigma Mixer** prefab (buttons + faders + screen) from `Assets/Cozen/Enigma OS/` into your scene.

## Documentation

The full documentation ships inside the package at `Assets/Cozen/Enigma OS/Enigma OS Documentation.pdf`, covering installation, every action type, the template system, faders (static + dynamic), presets, advanced features, custom controllers, and supported third-party integrations.

For help and community: [Discord](https://discord.gg/DQw3r9VJjZ).

## Third-Party Integrations

Enigma OS detects supported packages at edit time and lights up matching features automatically:

- **Shader systems:** Mochie Screen FX (free + Patreon), BeanFX, custom shader templates
- **Video players:** ProTV, VideoTXL
- **Access control:** OhGeezCmon Access Control, Flatline
- **Audio:** AudioLink (required)
- **Lighting:** VR Stage Lighting (VRSL) and VRSL GI

## License

MIT. See [`LICENSE.md`](LICENSE.md).
