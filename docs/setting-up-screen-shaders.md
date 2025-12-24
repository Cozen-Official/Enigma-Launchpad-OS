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

<img width="2318" height="1724" alt="Folder Navigation (3)" src="https://github.com/user-attachments/assets/06508e5b-8ab0-4592-9a65-3d267e6c6273" />
<img width="2318" height="1724" alt="Folder Navigation (2)" src="https://github.com/user-attachments/assets/3fb69a03-8d85-4a15-996a-16b840831b08" />

3. **Add Shader Materials**:
   - Drag shader materials into the folder's material list
   - Each material becomes a toggle button
   - Materials should have your desired screen shader applied

   <img width="2318" height="1724" alt="Folder Navigation (4)" src="https://github.com/user-attachments/assets/11ff9527-fe23-468f-8f84-064310621a1f" />

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
   - Make a cube gameobject and size it around the area you want the shaders to be visible in.
   - Remove the collider from this object, and place it under a parent called "Shaders" (or whatever name you prefer).
   - Assign this cube to the "Target Renderer" slot in the Shaders Folder. Do not use the same target renderer as a June Folder or Shaders Folder. Each should be on their own renderer.

   <img width="460" height="642" alt="image" src="https://github.com/user-attachments/assets/6bdfee15-a1a4-4e5c-aa4e-2b7396a50660" />
   <img width="463" height="328" alt="image" src="https://github.com/user-attachments/assets/34bd0eb0-5d4c-4ad0-a08b-84fe1eb58e7c" />

3. **Configure Presets**:
   - The folder provides six pages of preset controls
   - Customize default values for each effect in the editor
   - Provide as many outline colors as you'd like. You can cycle through them to choose which one you want.
   - Provide up to three images to use for scan and overlay effects. These will show on page 5 of the layout. You can preview the Mochie layout in the Preview foldout.
   
   <img width="454" height="297" alt="image" src="https://github.com/user-attachments/assets/eace8111-4065-446d-9798-2b5849be9d3f" />


4. **Using The Mochie Folder**:
   - Some controls have +/- buttons that adjust the property in steps. Clicking the middle button resets the property back to default. The middle button will light up green when positive or red when negative, and will be the inactive color when default.
   - On the first page, the color selector works by clicking the "Next Color" button which changes the "Set Color" to the "Next Color". Once the "Set Color" matches the desired color, click the "Set Color" button to change the "Current Color" to the "Set Color". To reiterate, the left button is the applied color, the right button cycles the colors on the selector, and the middle button applies the chosen color.
   
   <img width="260" height="82" alt="image" src="https://github.com/user-attachments/assets/3947aecf-6557-424d-8794-368a38a9c16b" />
   
   - The audiolink page lets you choose audiolink strengths for the different effects. These all start at full strength, with the sxception of Fog. When in doubt, hit reset.
   
   <img width="254" height="239" alt="image" src="https://github.com/user-attachments/assets/6e69a898-1b6f-4c8c-8be9-8b5a6abeb4e4" />


See [Mochie Folder](mochie-folder.md) for complete details.

## Setting up June Folder

For June Shaders integration:

### Prerequisites
- June Shaders installed and configured

### Configuration Steps

1. **Create a June Folder**:
   - Set Folder Type to **"June Folder"**
   - Make sure you have the June Depth Light prefab in your scene.
   
2. **Assign Target Renderer**:
   - Make a cube gameobject and size it around the area you want the shaders to be visible in
   - Remove the collider from this object, and place it under a parent called "Shaders" (or whatever name you prefer).
   - Assign this cube to the "Target Renderer" slot in the Shaders Folder. Do not use the same target renderer as a Mochie Folder or Shaders Folder. Each should be on their own renderer.   

3. **Select Modules**:
   - Choose which June modules to expose as toggles
   - Each module gets its own button
   - The first time you use June Folder, you will need to click "Generate Mapping" to scan the shader files and build the module UI.

   <img width="451" height="351" alt="image" src="https://github.com/user-attachments/assets/cbeec9b5-31c5-456b-a7d2-29d108352886" />

4. **Configure Properties**:
   - All module properties are exposed in the editor
   - Set default values, ranges, and behaviors
   - Configure per-module or folder-wide exclusivity
   - After clicking Generate Mapping, set the values for the desired effect for that toggle.
   <img width="445" height="495" alt="image" src="https://github.com/user-attachments/assets/4263f664-5d2b-4377-b3ab-5c692d77097d" />
   - If you need to change the render queue from the default (3998), search for "June Paid" material in your project or click it under "Internal References". You will need to make a new branch with the desired render queue and make sure the material is set to that branch (it should do so automatically after creation).

5. **Shader Locking**:
   - The system handles shader locking automatically
   - Prevents conflicts between modules
   - After setting the values for every destired toggle, lock the June Folder Material by clicking the Lock button at the button of the Folder. This will disable editing, so unlock if you need to make changes. If you forget to lock the folder, the editor will do so for you upon build.
   <img width="458" height="572" alt="image" src="https://github.com/user-attachments/assets/6372fd94-7ea7-4f54-87a4-4325fbfb7446" />

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
