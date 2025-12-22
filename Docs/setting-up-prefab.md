# Setting up Prefab

This guide covers how to add and configure the Enigma Launchpad OS prefabs in your VRChat world.

## Choosing a Prefab

Enigma Launchpad OS includes two prefab options:

### Enigma Launchpad
- 9×9 grid of toggle buttons with displays
- Folder display and page navigation
- Directional navigation buttons
- Auto-change toggle for cycling through options
- Supports all Folder Types

**Use when**: You need button-based controls without fader functionality

### Enigma Mixer
- All Launchpad features
- Fader system (static and dynamic faders)
- Fader displays with visual feedback
- Screen panel with multiple display modes
- Custom AudioLink tablet with AutoLink integration
- Video player controls for ProTV and VideoTXL
- Extended UI options

**Use when**: You need fader controls for real-time shader property adjustment

## Adding the Prefab

1. **Locate the Prefab**: In your Unity Project window, navigate to the Enigma Launchpad OS package folder
   - Look for `Enigma Launchpad.prefab` or `Enigma Mixer.prefab`

2. **Drag into Scene**: Drag your chosen prefab into the scene hierarchy

3. **Position the Prefab**: Move and rotate the prefab to your desired location in the world
   - The prefab is designed to be placed on walls or surfaces
   - Ensure it's accessible to players

4. **Scale if Needed**: Adjust the scale if necessary, though the default scale is optimized for VRChat

## Initial Configuration

### Opening the Custom Editor

1. Select the prefab instance in your hierarchy
2. In the Inspector, locate the main Enigma Launchpad/Mixer component
3. The custom editor interface will display automatically

### Understanding the Editor Interface

The custom editor includes several sections:

- **Folders Section**: Configure all folders and their Folder Types
- **Whitelist Section**: Set up access control (optional)
- **Display Settings**: Configure button displays and text
- **Fader Settings** (Mixer only): Configure static and dynamic faders
- **Preview Foldout**: Visual simulation of the in-game UI layout

### Creating Your First Folder

1. In the custom editor, click **"Add Folder"**
2. Name your folder (e.g., "Lighting", "Materials", "Effects")
3. Select a **Folder Type** from the dropdown
4. Configure the folder based on its type (see [Folder Types](folder-types.md) for details)

### Folder Configuration Tips

- **Folder Names**: Keep names short and descriptive (displayed on the folder navigation)
- **Page Organization**: Each folder can have multiple pages (81 buttons per page)
- **Exclusivity**: Some Folder Types support exclusivity (only one toggle active at a time)
- **Empty Buttons**: Leave buttons empty to display placeholder text

## Preview Foldout

The **Editor-side Preview Foldout** simulates the in-game Launchpad UI:

1. Expand the **Preview** section in the custom editor
2. View how folders and pages are organized
3. See button assignments and folder structure
4. Use this to verify your layout before testing in-game

## Testing in Play Mode

1. Enter Unity Play Mode
2. Interact with the Launchpad/Mixer to test functionality
3. Verify that toggles, folders, and pages work as expected
4. Check display text and visual feedback

## Common Setup Patterns

### Basic World Controls
1. Create an **Objects Folder** for room elements
2. Create a **Properties Folder** for lighting
3. Create a **Skybox Folder** for environment changes

### Club/Event Setup
1. Create **Shaders Folder** for screen effects
2. Create **Mochie Folder** or **June Folder** for post-processing
3. Create **Presets Folder** for saved configurations
4. Configure **Fader System** for real-time adjustments (Mixer)

### Content Display
1. Create **Materials Folder** for poster/art swapping
2. Create **Objects Folder** for scene variants
3. Create **Stats Folder** for analytics display

## Networking and Syncing

The Launchpad OS automatically handles:
- **Network Syncing**: All toggle states sync across players
- **Late Joiners**: Players joining late see the current state
- **Ownership**: Whitelist controls who can make changes (if enabled)

## Performance Considerations

- **Button Count**: Each folder page supports 81 buttons; use multiple pages if needed
- **Shader Count**: Limit active screen shaders for performance
- **Fader Update Rate**: Faders update smoothly but consider total fader count
- **Material Instances**: Be mindful of material instance creation

## Next Steps

After setting up the prefab:
- [Setting up Screen Shaders](setting-up-screen-shaders.md) - Configure shader launching
- [Setting up Fader System](setting-up-fader-system.md) - Set up faders (Mixer only)
- [Setting up Whitelist](setting-up-whitelist.md) - Configure access control
- [Folder Types Overview](folder-types.md) - Learn about each Folder Type

---

**Navigation**: [← Dependencies](dependencies.md) | [Screen Shaders →](setting-up-screen-shaders.md)

[Back to Home](index.md) | [View on GitHub](https://github.com/Cozen-Official/Enigma-Launchpad-OS)
