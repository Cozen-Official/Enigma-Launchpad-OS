# Shaders Folder

The **Shaders Folder** type launches screen shaders on duplicated mesh renderers. It provides a streamlined workflow for any screen shader without manual scene setup.

## Overview

Shaders Folder enables dynamic screen shader launching with minimal configuration. The system automatically handles renderer duplication and shader management. Perfect for:

- Custom screen effects
- Post-processing effects
- Visual effects on screens
- Screen-space shaders
- Camera effects
- Audio-reactive visuals
- Dynamic screen content

Unlike the specialized [Mochie Folder](mochie-folder.md) and [June Folder](june-folder.md) which are tailored for specific shader systems, Shaders Folder works with any screen shader.

## Configuration

### Basic Setup

1. **Create a Shaders Folder**:
   - In the Launchpad custom editor, add a new folder
   - Set Folder Type to **"Shaders Folder"**
   - Name your folder (e.g., "Screen Effects", "Visuals", "FX")

2. **Assign Target Renderer**:
   - Select the renderer that will display shaders
   - Typically a screen mesh or plane in your world
   - The system duplicates this renderer for each shader

3. **Add Shader Materials**:
   - Drag shader materials from your project into the folder
   - Each material becomes a toggle button
   - Materials should have screen shaders applied

4. **Set Button Names**:
   - Name each button descriptively
   - Examples: "Kaleidoscope", "RGB Shift", "Blur", "Distortion"
   - Keep names concise for display

5. **Configure Exclusivity**:
   - **Exclusive**: Only one shader active at a time (recommended)
   - **Non-Exclusive**: Multiple shaders can layer (advanced)

### Advanced Options

- **Renderer Layer**: Choose which layer duplicated renderers use
- **Sort Order**: Control rendering order for layered shaders
- **Default State**: Set which shader (if any) is active on load
- **Material Instance**: Configure per-instance material properties

## How It Works

### Renderer Duplication
1. System duplicates the target renderer for each shader material
2. Each duplicate is assigned its corresponding shader material
3. Duplicates are disabled by default
4. Button press enables the corresponding duplicate renderer

### Shader Lifecycle
- **Enable**: Activates the shader's renderer duplicate
- **Disable**: Deactivates the renderer duplicate
- **Exclusive**: Disabling others when enabling one
- **Material**: Each shader maintains its own material instance

## Use Cases

### Visual Effect Library

Collection of screen effects:

```
Shaders Folder: "Screen FX" (Exclusive)
Target: MainScreen_Renderer
Shaders:
- "Kaleidoscope" (kaleidoscope effect material)
- "RGB Shift" (chromatic aberration material)
- "Blur" (gaussian blur material)
- "Glitch" (digital glitch material)
- "Mirror" (mirror effect material)
- "Wave Distortion" (wave distortion material)
```

### Audio-Reactive Effects

AudioLink-driven shaders:

```
Shaders Folder: "Audio Visuals" (Exclusive)
Target: VisualizerScreen_Renderer
Shaders:
- "Spectrum Bars" (frequency bars)
- "Waveform" (oscilloscope display)
- "Particles" (audio-reactive particles)
- "Geometric" (audio-driven geometry)
- "Color Pulse" (color reactive to bass)
```

### Post-Processing Suite

Camera-like post effects:

```
Shaders Folder: "Post-Processing" (Non-Exclusive for layering)
Target: CameraOutput_Renderer
Shaders:
- "Bloom" (bloom effect)
- "Vignette" (edge darkening)
- "Color Grading" (color adjustment)
- "Film Grain" (noise overlay)
```

### Artistic Filters

Stylized rendering effects:

```
Shaders Folder: "Art Filters" (Exclusive)
Target: ArtScreen_Renderer
Shaders:
- "Oil Painting" (painterly effect)
- "Pixelate" (pixel art style)
- "Edge Detect" (outline style)
- "Halftone" (comic book style)
- "Posterize" (reduced color palette)
```

## Behavior Details

### Shader Activation
- **Button Press**: Enables corresponding shader renderer
- **Exclusive Mode**: Disables other shaders first
- **Non-Exclusive**: Shaders layer on top of each other
- **Material Properties**: Maintain set values when toggled

### Rendering Order
- Duplicated renderers render in configured order
- Layer settings affect render queue
- Transparent shaders respect transparency order
- Z-fighting avoided through proper configuration

### Networking
- Shader states sync across all players
- Late joiners see current shader configuration
- Material property changes sync if networked
- Whitelist restrictions apply (if enabled)

### Performance
- Each active shader has rendering cost
- Multiple shaders increase GPU load
- Use exclusivity to limit simultaneous shaders
- Optimize shader code for performance

## Tips and Best Practices

### Shader Material Preparation
- **Optimize Shaders**: Keep shader complexity reasonable for VRChat
- **Test Performance**: Test with target hardware (Quest, PC)
- **Mobile Compatibility**: Consider Quest compatibility if needed
- **Texture Usage**: Minimize texture samples in shaders

### Renderer Setup
- **Screen Mesh**: Use dedicated mesh for screen shaders
- **UV Mapping**: Ensure UVs are correct for screen space
- **Transform**: Position and scale screen appropriately
- **Material Slots**: Verify material slot configuration

### Shader Organization
- **Categorize**: Group similar effects together
- **Pages**: Use multiple pages for large shader libraries
- **Naming**: Clear names help users identify effects
- **Testing**: Test each shader individually first

### Performance Optimization
- **Exclusive Mode**: Use to prevent multiple expensive shaders
- **Shader LOD**: Implement LOD if possible
- **Resolution**: Consider render resolution for screens
- **Update Rate**: Optimize shader update frequency

## Common Issues

### Shader Not Appearing
- Verify renderer assignment is correct
- Check material is assigned to button
- Ensure renderer is visible in scene
- Confirm shader compiles without errors

### Renderer Not Duplicating
- Check Shaders Folder configuration
- Verify target renderer exists
- Ensure renderer has mesh filter and mesh
- Check for script errors in console

### Multiple Shaders Fighting
- Verify exclusivity setting
- Check rendering layers and order
- Ensure proper material sorting
- Avoid z-fighting with proper setup

### Performance Issues
- Too many active shaders
- Shader complexity too high
- Texture resolution too high
- Reduce active shader count or optimize shaders

## Shader Compatibility

### Supported Shader Types
- **Screen Space**: Shaders designed for screen/quad rendering
- **Post-Processing**: Camera-style post effects
- **Procedural**: Mathematically generated effects
- **Texture-Based**: Shaders sampling textures
- **Audio-Reactive**: AudioLink-integrated shaders

### Shader Requirements
- Must be VRChat compatible
- Should work with standard mesh renderers
- Respect VRChat shader limitations
- Test in VRChat before deployment

## Integration with Other Systems

### With Fader System
- Link dynamic faders to shader toggles
- Control shader properties in real-time
- Example: Enable shader, adjust intensity with fader

### With Properties Folder
- Use Properties Folder for discrete shader parameter changes
- Combine shader launching with property adjustment
- Create shader preset variations

### With AudioLink
- Many screen shaders integrate with AudioLink
- Audio-reactive visuals synchronized across players
- Ensure AudioLink is in scene and configured

### With Presets Folder
- Save shader configurations as presets
- Recall shader setups for different events
- Share shader configurations between users

## Examples

### DJ Booth Screens

```
Shaders Folder: "DJ Screens" (Exclusive)
Target: DJScreen_Renderer
Shaders:
- "AudioLink Spectrum"
- "Waveform Display"
- "Logo Animation"
- "Color Pulse"
- "Geometric Shapes"
- "Particle Field"
```

### Gallery Interactive Display

```
Shaders Folder: "Interactive Art" (Non-Exclusive)
Target: ArtDisplay_Renderer
Shaders:
- "Base Canvas"
- "Overlay Pattern"
- "Color Filter"
- "Motion Blur"
```

### Event Visuals

```
Shaders Folder: "Stage Visuals" (Exclusive)
Target: BackdropScreen_Renderer
Shaders:
- "Opening Animation"
- "Energetic Pattern"
- "Calm Waves"
- "Intense Strobe"
- "Closing Credits"
```

### Experimental Effects

```
Shaders Folder: "Experimental" (Exclusive)
Target: TestScreen_Renderer
Shaders:
- "Fractal Zoom"
- "Mandelbrot Set"
- "Fluid Simulation"
- "Ray Marching Demo"
- "Voronoi Cells"
```

## Advanced Techniques

### Layered Shader Effects
- Use non-exclusive mode carefully
- Layer complementary effects
- Control render order precisely
- Optimize for multiple active shaders

### Dynamic Shader Properties
- Combine with fader system for real-time control
- Create complex parameter spaces
- Link multiple faders to single shader
- Save configurations as presets

### Custom Screen Shader Development
- Design shaders specifically for Launchpad use
- Expose important parameters for fader control
- Optimize for VRChat performance requirements
- Test across platforms (PC/Quest)

### Render Texture Integration
- Use render textures as shader input
- Create camera-based effects
- Chain multiple shader passes
- Advanced visual pipelines

## Differences from Specialized Folders

### vs. Mochie Folder
- Mochie Folder: Specialized for Mochie Screen FX, preset layouts, advanced controls
- Shaders Folder: Generic, works with any shader, simpler configuration

### vs. June Folder
- June Folder: Specialized for June Shaders, per-module control, tight integration
- Shaders Folder: Generic, works with any shader, manual material assignment

**Use Shaders Folder when**:
- Using custom or third-party shaders
- Need simple on/off control
- Want flexibility in shader choice
- Don't need specialized controls

## Next Steps

- [Mochie Folder](mochie-folder.md) - Specialized Mochie Screen FX control
- [June Folder](june-folder.md) - Specialized June Shaders control
- [Fader System](setting-up-fader-system.md) - Add real-time shader control
- [Properties Folder](properties-folder.md) - Adjust shader properties

---

**Navigation**: [← Stats Folder](stats-folder.md) | [Mochie Folder →](mochie-folder.md)

[Back to Home](index.md) | [View on GitHub](https://github.com/Cozen-Official/Enigma-Launchpad-OS)
