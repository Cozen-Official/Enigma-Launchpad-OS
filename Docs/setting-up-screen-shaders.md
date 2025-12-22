# Setting up Screen Shaders

Screen shaders are visual effects applied to cameras or screen spaces in VRChat worlds. Enigma Launchpad OS includes a streamlined workflow for launching and managing screen shaders without manual scene setup.

## What are Screen Shaders?

Screen shaders are post-processing effects that modify the rendered image, including:
- Color grading and filters
- Blur and distortion effects
- Edge detection and outlines
- Kaleidoscope and mirror effects
- Audio-reactive visual effects
- And many more creative effects

## Supported Shader Systems

Enigma Launchpad OS has built-in support for:

### Mochie Screen FX
- Free version and Patreon SFX X version
- Six-page preset layout with global controls
- +/- adjustment controls
- Color selector for outline colors
- AudioLink band and strength toggles
- Major effect toggles
- See [Mochie Folder](mochie-folder.md) for detailed configuration

### June Shaders
- Modular shader system with individual toggles
- Per-module property exposure
- Flexible exclusivity options
- Automatic shader locking and setup
- See [June Folder](june-folder.md) for detailed configuration

### Custom Shaders via Shaders Folder
- Works with any screen shader
- Manual configuration required
- Duplicated mesh renderer approach
- See [Shaders Folder](shaders-folder.md) for setup

## Setting up Shaders Folder

The **Shaders Folder** type enables you to launch screen shaders with minimal setup:

### Basic Setup

1. **Create a Shaders Folder**:
   - In the Launchpad custom editor, add a new folder
   - Set Folder Type to **"Shaders Folder"**

2. **Configure Renderer Target**:
   - Assign the target renderer that will display the shader
   - The system duplicates this renderer for each shader toggle
   - Typically this is a screen mesh or plane in your world

3. **Add Shader Materials**:
   - Drag shader materials into the folder's material list
   - Each material becomes a toggle button
   - Materials should have your desired screen shader applied

4. **Set Display Names**:
   - Name each toggle for the in-game display
   - Keep names concise (e.g., "Kaleidoscope", "RGB Shift", "Blur")

### Advanced Options

- **Exclusivity**: Enable to ensure only one shader is active at a time
- **Default State**: Set which shader (if any) is active on world load
- **Button Layout**: Organize shaders across multiple pages if needed

## Setting up Mochie Folder

For Mochie Screen FX integration:

### Prerequisites
- Mochie Screen FX installed (free or Patreon version)
- Screen shader renderer configured in scene

### Configuration Steps

1. **Create a Mochie Folder**:
   - Set Folder Type to **"Mochie Folder"**

2. **Assign Target Renderers**:
   - Select all renderers that should receive Mochie effects
   - Can be single or multiple renderers

3. **Configure Presets**:
   - The folder provides six pages of preset controls
   - Customize default values for each effect in the editor
   - Set color presets, intensity values, and AudioLink bands

4. **Set Up Controls**:
   - +/- buttons for value adjustments
   - Color selector toggles
   - AudioLink band assignments
   - Effect enable/disable toggles

See [Mochie Folder](mochie-folder.md) for complete details.

## Setting up June Folder

For June Shaders integration:

### Prerequisites
- June Shaders installed and configured
- June shader system added to scene

### Configuration Steps

1. **Create a June Folder**:
   - Set Folder Type to **"June Folder"**

2. **Select Modules**:
   - Choose which June modules to expose as toggles
   - Each module gets its own button

3. **Configure Properties**:
   - All module properties are exposed in the editor
   - Set default values, ranges, and behaviors
   - Configure per-module or folder-wide exclusivity

4. **Shader Locking**:
   - The system handles shader locking automatically
   - Prevents conflicts between modules

See [June Folder](june-folder.md) for complete details.

## Workflow Tips

### Organizing Shaders
- **Group by Type**: Create folders for different effect categories (Color, Distortion, Audio-Reactive)
- **Use Pages**: Spread many shaders across pages for better organization
- **Name Clearly**: Use descriptive button names for easy identification

### Performance Optimization
- **Limit Active Shaders**: Use exclusivity to prevent multiple expensive shaders running simultaneously
- **Test on Target Hardware**: Screen shaders can be performance-intensive
- **Use LOD**: Consider disabling shaders for distant players if possible

### Testing Shaders
1. Enter Play Mode in Unity
2. Enable shaders via the Launchpad interface
3. Check visual output on target screen
4. Verify AudioLink reactivity (if applicable)
5. Test toggle and exclusivity behavior

## Common Issues

### Shader Not Appearing
- Verify renderer assignment is correct
- Check that material is properly assigned
- Ensure shader is compatible with VRChat

### Multiple Shaders Active
- Enable exclusivity if only one should be active
- Check folder configuration settings

### AudioLink Not Working
- Verify AudioLink is in scene and initialized
- Check AudioLink component references
- Ensure AudioLink bands are properly assigned

### Performance Issues
- Reduce number of active shaders
- Optimize shader complexity
- Use exclusivity to limit simultaneous effects

## Next Steps

After setting up screen shaders:
- [Setting up Fader System](setting-up-fader-system.md) - Add real-time shader property control
- [Mochie Folder](mochie-folder.md) - Detailed Mochie configuration
- [June Folder](june-folder.md) - Detailed June configuration
- [Shaders Folder](shaders-folder.md) - Custom shader setup

---

**Navigation**: [← Prefab Setup](setting-up-prefab.md) | [Fader System →](setting-up-fader-system.md)

[Back to Home](index.md) | [View on GitHub](https://github.com/Cozen-Official/Enigma-Launchpad-OS)
