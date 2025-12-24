# June Folder

The **June Folder** type provides global toggles for **June Shader** modules with exposed properties. It enables per-module control with flexible exclusivity options and automatic shader locking.

## Overview

June Folder is specifically designed for the June Shader system, a modular post-processing shader suite. It offers:

- Individual toggle per June module
- All module properties exposed in editor
- Per-module or folder-wide exclusivity
- Automatic shader locking for optimization
- Complete setup handled by Launchpad OS
- Configurable values using the same UI layout as June editor
- Integration with Faders and Presets systems

## Prerequisites

### Required
- **June Shaders** purchased and installed
  - Purchase: [https://kleineluka.gumroad.com/l/june](https://kleineluka.gumroad.com/l/june)
  - Complete June Shaders installation process
  - Follow June documentation for setup
- Target renderer(s) with June-compatible materials

### Version Support
- **Full Version**: Complete module support
- **Free Version**: Support coming soon (use Shaders Folder for now)

## Technical Details
- June Folder utilizes a single material that is assigned under Internal References. This material is applied by the editor to the set target renderer on play mode entry and upon build if not already applied.
- The Enigma Launchpad OS is agnostic about June shader properties and values, an included Roslyn parser scans the June editor for shader property and module organization, and scans the shader files for min/max/default values. A June Mapping file is created with the needed information to build the UI for each module within the Enigma Launchpad editor.
- Locking the June Folder automatically sets the locking flags for only the modules needed for the toggles set in the June Folder, then locks the material, which creates a new Shader file "branch".
- The June material should be using the AudioLink branch of June.
- Render queue should be changed within this material if necessary.

## Next Steps

- [Presets Folder](presets-folder.md) - Save June configurations
- [Fader System](setting-up-fader-system.md) - Add real-time control
- [Mochie Folder](mochie-folder.md) - Alternative shader system
- [Shaders Folder](shaders-folder.md) - Generic shader launching

## Resources

- **June Shaders**: [https://kleineluka.gumroad.com/l/june](https://kleineluka.gumroad.com/l/june)
---

**Navigation**: [← Mochie Folder](mochie-folder.md) | [Presets Folder →](presets-folder.md)

[Back to Home](index.md) | [View on GitHub](https://github.com/Cozen-Official/Enigma-Launchpad-OS)
