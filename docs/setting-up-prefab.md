# Setting up Prefab

This guide covers how to add and configure the Enigma Launchpad OS prefabs in your VRChat world.
<img width="539" height="127" alt="image" src="https://github.com/user-attachments/assets/ed73e071-5a6e-4907-8ed5-95aab3862f59" />

## Choosing a Prefab

Enigma Launchpad OS includes two prefab options:

### Enigma Launchpad
- 3x3 grid of toggle buttons with displays
- Folder name and page number displays
- Directional navigation buttons
- Auto-change toggle for cycling through skyboxes (if used).
- Supports all Folder Types

**Use when**: You need button-based controls without fader functionality, or space is limited

### Enigma Mixer
- All Launchpad features
- Fader system (static and dynamic faders)
- Fader displays with visual feedback
- Screen panel with multiple display modes
- Custom AudioLink tablet with AutoLink integration
- Video player controls for ProTV and VideoTXL
- Extended UI options

**Use when**: You need fader controls for real-time shader property adjustment

Note: The Mixer prefab already has the full launchpad built into it, there is no benefit to using both unless you need a separate launcher for a different area of your world or for a separate purpose.

## Adding the Prefab

1. **Locate the Prefab**: In your Unity Project window, navigate to `Assets/Cozen/Enigma Launchpad`
   - Look for `Enigma Launchpad.prefab` or `Enigma Mixer.prefab`

2. **Drag into Scene**: Drag your chosen prefab into the scene hierarchy

3. **Position the Prefab**: Move and rotate the prefab to your desired location in the world. There are no handles or pickup scripts on either prefab unless you add them yourself.

4. **Scale if Needed**: Adjust the scale if necessary, though the default scale is already scaled for most VRChat worlds / Booth avatar size.

## Initial Configuration

### Opening the Custom Editor

1. Select the prefab instance in your hierarchy
2. The custom editor interface will display automatically. All configuration happens here, no digging through buttons and TMP objects required.

<img width="1316" height="605" alt="image" src="https://github.com/user-attachments/assets/70264331-84a0-4ffd-bc1d-68a819a30fb1" />


### Understanding the Editor Interface

The custom editor includes several sections:
- **Preview Foldout**: Shows a preview of your set folders/pages to verify layout in the editor
<img width="458" height="319" alt="image" src="https://github.com/user-attachments/assets/85ad7639-d223-417e-b9e4-3e09f70ed481" />

- **Settings**: Configure toggle colors, defailt folder, and additionally video player controls and audiolink controls for the Mixer Prefab.
<img width="454" height="167" alt="image" src="https://github.com/user-attachments/assets/e020fe15-8017-42d2-b85a-74002b5858c9" />

- **Folders Section**: Configure all folders and their Folder Types
<img width="458" height="512" alt="image" src="https://github.com/user-attachments/assets/64f2c780-c3c2-4a47-a7e1-280e4f55033d" />

- **Whitelist Section**: Set up access control (optional)
<img width="452" height="155" alt="image" src="https://github.com/user-attachments/assets/2dc24172-7832-42e6-8fc2-ba00f16038cc" />

- **Fader Settings** (Mixer only): Configure static and dynamic faders
<img width="463" height="385" alt="image" src="https://github.com/user-attachments/assets/b0a72578-4015-457e-91cb-f7a68f0a8a7d" />


### Settings
If you're using the Mixer prefab, it's important to set the references to AudioLink and Video Player in the Settings foldout. Otherwise, the video screen options including AudioLink will not work.

## Next Steps

After setting up the prefab:
- [Setting up Screen Shaders](setting-up-screen-shaders.md) - Configure shader launching
- [Setting up Fader System](setting-up-fader-system.md) - Set up faders (Mixer only)
- [Setting up Whitelist](setting-up-whitelist.md) - Configure access control
- [Folder Types Overview](folder-types.md) - Learn about each Folder Type

---

**Navigation**: [← Dependencies](dependencies.md) | [Screen Shaders →](setting-up-screen-shaders.md)

[Back to Home](index.md) | [View on GitHub](https://github.com/Cozen-Official/Enigma-Launchpad-OS)
