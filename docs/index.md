# Enigma Launchpad OS Documentation

Welcome to the **Enigma Launchpad OS** documentation! Enigma Launchpad OS is a modular control system designed for VRChat worlds. It provides a unified interface for toggling objects and materials, modifying shader properties, controlling screen shaders, displaying analytics, creating persistent presets, changing skyboxes, and more.

## What is Enigma Launchpad OS?

Enigma Launchpad OS is a powerful UdonSharp-based control system that simplifies world UI creation in VRChat. The system offers two prefab versions:

- **Enigma Launchpad**: A 3x3 grid of toggle buttons with displays, folder navigation, and page management
- **Enigma Mixer**: All Launchpad features plus a fader system, screen panel, AudioLink integration, and video player controls

## Key Features

- **Custom Editor**: Significantly simplifies UI setup with an intuitive interface
- **Folder-Based System**: Organize controls into folders with different Folder Types
- **Modular Design**: Each Folder Type acts as a module defining specific behaviors
- **Whitelist System**: Control access with optional third-party integrations
- **Fader System**: Dynamic and static faders for real-time shader property control
- **Preset System**: Save and load toggle configurations, with persistent PlayerData support
- **Screen Shader Support**: Built-in support for Mochie Screen FX and June Shaders
- **Wide Integration**: Works with AudioLink, AutoLink, ProTV, VideoTXL, and more

## Use Cases

While tailored for VRChat club environments, Enigma Launchpad OS is useful for any creator who needs runtime control:

- **Materials Folder**: Swap pictures on walls or change room themes
- **Objects Folder**: Switch between furniture layouts or toggle props
- **Properties Folder**: Modify light colors or shader-driven effects
- **Skybox Folder**: Change environment skyboxes with auto-cycling
- **Shaders/Mochie/June Folders**: Control post-processing effects and screen shaders
- **Presets Folder**: Save and share complex multi-toggle configurations with persistence

## Getting Started

To get started with Enigma Launchpad OS, follow these steps:

1. [Install Dependencies](dependencies.md) - Install required packages before importing
2. [Setting up Prefab](setting-up-prefab.md) - Add the Launchpad or Mixer prefab to your world
3. [Setting up Screen Shaders](setting-up-screen-shaders.md) - Configure screen shader support
4. [Setting up Fader System](setting-up-fader-system.md) - Set up static and dynamic faders
5. [Setting up Whitelist](setting-up-whitelist.md) - Configure access control

## Folder Types

Enigma Launchpad OS uses different **Folder Types** to implement various behaviors:

- [Objects Folder](objects-folder.md) - Toggle GameObject active states
- [Materials Folder](materials-folder.md) - Swap materials on renderers
- [Properties Folder](properties-folder.md) - Set shader property values
- [Shaders Folder](shaders-folder.md) - Launch screen shaders
- [Skybox Folder](skybox-folder.md) - Switch world skyboxes
- [Stats Folder](stats-folder.md) - Display world/instance statistics
- [Mochie Folder](mochie-folder.md) - Control Mochie Screen FX
- [June Folder](june-folder.md) - Control June Shaders
- [Presets Folder](presets-folder.md) - Save and load user created presets

Learn more about [Folder Types](folder-types.md) and how to configure each type.

## Navigation

- **Main Pages**: [Intro](index.md) | [Dependencies](dependencies.md) | [Prefab Setup](setting-up-prefab.md) | [Screen Shaders](setting-up-screen-shaders.md) | [Fader System](setting-up-fader-system.md) | [Whitelist](setting-up-whitelist.md)
- **Folder Types**: [Overview](folder-types.md) | [Objects](objects-folder.md) | [Materials](materials-folder.md) | [Properties](properties-folder.md) | [Skybox](skybox-folder.md) | [Stats](stats-folder.md) | [Shaders](shaders-folder.md) | [Mochie](mochie-folder.md) | [June](june-folder.md) | [Presets](presets-folder.md)

---

[View on GitHub](https://github.com/Cozen-Official/Enigma-Launchpad-OS)
