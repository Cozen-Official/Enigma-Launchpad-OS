# Setting up Screen Shaders

Screen shaders are visual effects applied to cameras or screen spaces in VRChat worlds. Enigma Launchpad OS includes a streamlined workflow for launching and managing screen shaders without manual scene setup.

## What are Screen Shaders?

Screen shaders are post-processing effects that modify the rendered image, including:
- Color grading and filters
- Blur and distortion effects
- Edge detection and outlines
- Audio-reactive visual effects

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
- See [Shaders Folder](shaders-folder.md) for technical info

## Setting up Shaders Folder

The **Shaders Folder** type enables you to launch screen shaders with minimal setup:

### Basic Setup

1. **Create a Shaders Folder**:
   - In the Launchpad custom editor, add a new folder
   - Set Folder Type to **"Shaders Folder"**

2. **Configure Renderer Target**:
   - Make a cube gameobject and size it around the area you want the shaders to be visible in
   - Remove the mesh collider from this object, and place it under a parent called "Shaders" (or whatever name you prefer).
   - Assign this cube to the "Target Renderer" slot in the Shaders Folder.
   - The system duplicates this renderer as a template, duplicating for each shader toggle, and sets the template to Editor Only.
   - The duplicates appear at the same level as the template. Link your user-facing local toggle to the Shaders folder to allow users to individually turn off shaders. You will want to do this for photosensitivity or user preference if using intense shaders. 

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

## Setting up Mochie Folder

For Mochie Screen FX integration:

### Prerequisites
- Mochie Screen FX installed (free or Patreon version)

### Configuration Steps

1. **Create a Mochie Folder**:
   - Set Folder Type to **"Mochie Folder"**

2. **Assign Target Renderer**:
   - Make a cube gameobject and size it around the area you want the shaders to be visible in
   - Remove the collider from this object, and place it under a parent called "Shaders" (or whatever name you prefer).
   - Assign this cube to the "Target Renderer" slot in the Shaders Folder. Do not use the same target renderer as a June Folder or Shaders Folder. Each should be on their own renderer.

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

### Configuration Steps

1. **Create a June Folder**:
   - Set Folder Type to **"June Folder"**
   
2. **Assign Target Renderer**:
   - Make a cube gameobject and size it around the area you want the shaders to be visible in
   - Remove the collider from this object, and place it under a parent called "Shaders" (or whatever name you prefer).
   - Assign this cube to the "Target Renderer" slot in the Shaders Folder. Do not use the same target renderer as a Mochie Folder or Shaders Folder. Each should be on their own renderer.   

3. **Select Modules**:
   - Choose which June modules to expose as toggles
   - Each module gets its own button

4. **Configure Properties**:
   - All module properties are exposed in the editor
   - Set default values, ranges, and behaviors
   - Configure per-module or folder-wide exclusivity

5. **Shader Locking**:
   - The system handles shader locking automatically
   - Prevents conflicts between modules

See [June Folder](june-folder.md) for complete details.

### Notes
Some shaders/skyboxes may not look right in the editor, always test shaders in play mode or using Build & Test.

### Shaders Not Appearing
- Verify you have the Depth Light prefab for whatever shader set you are using in your scene.
- Verify renderer assignment is correct
- Test your shaders in play mode and adjust until visible. Shaders/Skyboxes may not render correctly in the editor, always test in play mode or a built world.
- Check the min/max distance in your shaders if applicable.
- Check the render queue of the shaders you are using. Make sure it is HIGHER than the environment.

### Multiple Shaders Active
- Enable exclusivity if only one should be active
- If one or the other are not displaying when both are toggle on, then check your render queues for both materials. It may take some testing to see which needs a higher queue.
- If using Mochie or June folder, all effects use the same render queue.

### AudioLink Not Working
- Verify AudioLink is in scene and initialized
- Check AudioLink component references

## Next Steps

After setting up screen shaders:
- [Setting up Fader System](setting-up-fader-system.md) - Add real-time shader property control
- [Mochie Folder](mochie-folder.md) - Detailed Mochie configuration
- [June Folder](june-folder.md) - Detailed June configuration
- [Shaders Folder](shaders-folder.md) - Custom shader setup

---

**Navigation**: [← Prefab Setup](setting-up-prefab.md) | [Fader System →](setting-up-fader-system.md)

[Back to Home](index.md) | [View on GitHub](https://github.com/Cozen-Official/Enigma-Launchpad-OS)
