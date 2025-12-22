# Dependencies

Before importing the Enigma Launchpad OS package, you must install the required dependencies. The dependencies vary depending on which prefab you plan to use.

## Required Dependencies

### For Launchpad Prefab

- **AudioLink**: Install from VRChat Creator Companion
  - AudioLink is required for audio-reactive features and is a core dependency
  - Open VRChat Creator Companion, select your project, and add AudioLink from the official packages

### For Mixer Prefab

The Mixer prefab includes all Launchpad features plus additional functionality, requiring these dependencies:

- **AudioLink**: Install from VRChat Creator Companion
- **AutoLink**: Install from VCC after adding the repository
  - Repository: [https://github.com/lackofbindings/AutoLink](https://github.com/lackofbindings/AutoLink)
  - Add the VCC repository URL, then install from Creator Companion

## Optional Dependencies

These dependencies enable specific Folder Types or features. Install only what you need for your world:

### Shader Systems

- **Mochie Screen FX**: Enables the Mochie Folder type
  - Free version: [https://github.com/MochiesCode/Mochies-Unity-Shaders](https://github.com/MochiesCode/Mochies-Unity-Shaders)
  - Extended Patreon version: [https://www.patreon.com/c/mochieshaders/posts](https://www.patreon.com/c/mochieshaders/posts)
  - The extended version unlocks additional features in the Mochie Folder layout

- **June Shaders**: Enables the June Folder type
  - Purchase from: [https://kleineluka.gumroad.com/l/june](https://kleineluka.gumroad.com/l/june)
  - Complete the full installation process as per June Shaders documentation
  - Note: Support for the free version is coming soon. For now, use the Shaders Folder with a material for each effect.

### Video Players

- **ProTV**: Video player integration
  - Website: [https://protv.dev/](https://protv.dev/)
  - Add the VCC repository and install from Creator Companion

- **VideoTXL**: Alternative video player integration
  - Repository: [https://github.com/vrctxl/VideoTXL](https://github.com/vrctxl/VideoTXL)
  - Add the VCC repository and install from Creator Companion

### Access Control

- **OhGeezCmon Access Control**: Runtime whitelist management
  - Repository: [https://github.com/OhGeezCmon/VRC-AccessControl](https://github.com/OhGeezCmon/VRC-AccessControl)
  - Allows adding users to the whitelist during runtime
  - Install the package from the repository

- **Flatline Open Decks Manager**: Additional whitelist integration and event support
  - Purchase from: [https://lavysworlds.gumroad.com/l/flatline](https://lavysworlds.gumroad.com/l/flatline)
  - Provides whitelist integration and event hosting features

## Installation Order

1. **First**: Install VRChat SDK3 (Worlds) if not already installed
2. **Second**: Install required dependencies (AudioLink, and AutoLink if using Mixer)
3. **Third**: Install optional dependencies based on your needs
4. **Finally**: Import the Enigma Launchpad OS package

## Troubleshooting

### Missing Dependencies Error

If you receive errors about missing dependencies after import:
- Check that all required packages are installed in Creator Companion
- Verify that the latest versions are installed
- Restart Unity Editor after installing dependencies

You may see a missing script error if don't have ProTV or VideoTXL installed. This is normal as the prefab ships linked to video player controls. You can delete the unused controls from under the "Video Player Controls" transform to remove the error.

### AutoLink Issues

- You should see an AutoLink UI canvas on the Mixer if imported correctly. If you do not see the UI buttons to toggle autolink, search for "AutoLink" in the hierarchy and make sure it's enabled and not clipping. Resizing the model may cause it to clip, so drag it up until it's visible.

## Next Steps

Once all dependencies are installed, proceed to:
- [Setting up Prefab](setting-up-prefab.md) - Add and configure the Launchpad or Mixer prefab

---

**Navigation**: [← Intro](index.md) | [Prefab Setup →](setting-up-prefab.md)

[Back to Home](index.md) | [View on GitHub](https://github.com/Cozen-Official/Enigma-Launchpad-OS)
