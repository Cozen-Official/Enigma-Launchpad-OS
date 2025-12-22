# June Folder

The **June Folder** type provides global toggles for **June Shader** modules with exposed properties. It enables per-module control with flexible exclusivity options and automatic shader locking.

## Overview

June Folder is specifically designed for the June Shader system, a modular post-processing shader suite. It offers:

- Individual toggle per June module
- All module properties exposed in editor
- Per-module or folder-wide exclusivity
- Automatic shader locking to prevent conflicts
- Complete setup handled by Launchpad OS
- Configurable default values
- Module combination support

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

## Configuration

### Basic Setup

1. **Install June Shaders**:
   - Purchase from Gumroad
   - Download and import package
   - Follow June installation instructions
   - Verify shaders compile correctly

2. **Set Up June in Scene**:
   - Add June shader system to scene
   - Configure base June setup as per documentation
   - Prepare target renderer(s)

3. **Create a June Folder**:
   - In the Launchpad custom editor, add a new folder
   - Set Folder Type to **"June Folder"**
   - Name your folder (e.g., "Post-Processing", "June FX")

4. **Select June Modules**:
   - Choose which June modules to expose as toggles
   - Each selected module gets a button
   - Modules appear in selected order

5. **Configure Module Properties**:
   - For each module, all properties are exposed
   - Set default values in editor
   - Configure ranges where applicable
   - Set initial enabled/disabled state

6. **Set Exclusivity Mode**:
   - **Per-Module Exclusivity**: Each module independently toggleable
   - **Folder Exclusivity**: Only one module active at a time
   - **Hybrid**: Some modules exclusive, others independent

### Available June Modules

Common June Shader modules (depends on June version):

#### Visual Effect Modules
- **Blur**: Various blur types and intensities
- **Bloom**: HDR bloom with threshold control
- **Chromatic Aberration**: Color channel separation
- **Vignette**: Edge darkening/lightening
- **Distortion**: Wave, bulge, twist distortions
- **Kaleidoscope**: Geometric pattern effects
- **Pixelation**: Pixel art style rendering
- **Edge Detection**: Outline and edge highlighting

#### Color Effect Modules
- **Color Grading**: LUT-based color correction
- **HSV Adjustment**: Hue, saturation, value control
- **Contrast/Brightness**: Tone adjustment
- **Color Tint**: Overlay color tinting
- **Invert Colors**: Color inversion effect
- **Posterize**: Reduced color palette
- **Black & White**: Desaturation effect

#### Special Effect Modules
- **Glitch**: Digital glitch artifacts
- **Screen Shake**: Camera shake simulation
- **Film Grain**: Noise and grain overlay
- **Scanlines**: CRT monitor effect
- **RGB Split**: Color channel offset
- **Feedback**: Recursive visual feedback
- **Halftone**: Comic book/print style

#### Audio-Reactive Modules
- **AudioLink Integration**: Audio-driven effects
- **Spectrum Visualization**: Frequency display
- **Reactive Colors**: Audio-driven color changes
- **Pulse Effects**: Beat-synchronized effects

## Module Configuration

### Per-Module Settings

For each module, configure:

#### Basic Parameters
- **Enable/Disable**: Default state
- **Intensity**: Effect strength (0-1 typically)
- **Mix Amount**: Blend with original image

#### Module-Specific Properties
Each module has unique properties:

**Blur Module Example**:
- Blur type (Gaussian, Box, Radial)
- Blur radius
- Sample count
- Direction (if applicable)

**Color Grading Example**:
- LUT texture
- LUT strength
- Temperature
- Tint

**Distortion Example**:
- Distortion type
- Amplitude
- Frequency
- Center point

### Exclusivity Configuration

#### Folder-Wide Exclusivity
- Only one module active at a time
- Simpler management
- Prevents effect conflicts
- Good for mutually exclusive effects

#### Per-Module Exclusivity
- Modules toggle independently
- Allows effect layering
- More flexibility
- Requires understanding of module interactions

#### Hybrid Approach
- Some modules exclusive with each other
- Other modules independent
- Example: Color effects independent, distortion effects exclusive
- Advanced configuration option

## Use Cases

### Post-Processing Suite

Comprehensive visual control:

```
June Folder: "Post-Processing" (Per-Module)
Modules:
- Bloom (always compatible)
- Vignette (always compatible)
- Color Grading (always compatible)
- Chromatic Aberration (use sparingly)
- Film Grain (subtle texture)
```

### VJ Performance Effects

Performance-focused effects:

```
June Folder: "Performance FX" (Folder Exclusive)
Modules:
- Kaleidoscope (geometric visuals)
- RGB Split (high energy)
- Glitch (transition effect)
- Distortion (dynamic shapes)
- Feedback (psychedelic effect)
```

### Atmosphere Control

Mood-setting effects:

```
June Folder: "Ambiance" (Per-Module)
Modules:
- Color Grading (mood setting)
- Vignette (focus control)
- Blur (depth simulation)
- Black & White (dramatic moments)
```

### Camera Effects

Photography-style processing:

```
June Folder: "Camera FX" (Per-Module)
Modules:
- Film Grain (analog feel)
- Chromatic Aberration (lens simulation)
- Vignette (focus)
- Contrast/Brightness (exposure)
```

## Behavior Details

### Module Activation
- **Button Press**: Toggles module on/off
- **Exclusive Mode**: Disables others if needed
- **Property Application**: Module properties apply immediately
- **Shader Locking**: System prevents conflicts automatically

### Shader Management
- **Automatic Locking**: Prevents conflicting module combinations
- **Safe Toggling**: System validates safe module states
- **Error Prevention**: Blocks invalid configurations
- **Material Instances**: Created for independent control

### Networking
- Module states sync across all players
- Late joiners see current configuration
- Property values network-synced
- Whitelist restrictions apply (if enabled)

### Performance
- Each module has performance cost
- Multiple modules compound cost
- Use exclusivity for performance management
- Test on target hardware

## Tips and Best Practices

### Module Selection
- **Start Essential**: Enable core modules first
- **Test Individually**: Verify each module works
- **Performance Check**: Monitor FPS with combinations
- **Compatibility**: Some modules work better together

### Property Configuration
- **Conservative Defaults**: Start with subtle values
- **Test Ranges**: Experiment with property ranges
- **Documentation**: Refer to June docs for properties
- **Save Configs**: Document working setups

### Performance Optimization
- **Module Count**: Limit active modules
- **Exclusive Mode**: Use to reduce load
- **Effect Intensity**: Lower intensity = better performance
- **Resolution**: Consider render resolution

### Exclusivity Strategy
- **Visual Effects**: Often compatible (blur, bloom, vignette)
- **Distortion Effects**: May conflict with each other
- **Color Effects**: Usually stackable
- **Heavy Effects**: Consider making exclusive

## Common Issues

### Module Not Appearing
- Verify June Shaders properly installed
- Check module is selected in folder configuration
- Ensure June system initialized in scene
- Confirm no shader compilation errors

### Module Conflicts
- Some modules incompatible with others
- Check June documentation for conflicts
- Use shader locking features
- Consider exclusivity settings

### Performance Problems
- Too many modules active
- Complex module combinations
- High render resolution
- Reduce active modules or optimize settings

### Properties Not Working
- Verify property name spelling
- Check property type matches
- Ensure June version supports property
- Review June shader documentation

### Shader Locking Issues
- Automatic locking prevents certain combinations
- Intentional safety feature
- Reconfigure modules for compatibility
- Check June docs for valid combinations

## Integration with Other Systems

### With Fader System
- Link dynamic faders to June module toggles
- Real-time property adjustment
- Example: Enable blur, fader controls blur amount

### With Presets Folder
- Save complete June configurations
- Recall module setups for events
- Share configurations between users
- Scene-based presets

### With Objects Folder
- Control June renderer visibility
- Context-based post-processing
- Example: Show screens with June effects

### With Properties Folder
- Fine-tune June properties discretely
- Complement module toggles
- Preset property values

## Advanced Techniques

### Effect Layering
- Combine compatible modules
- Order matters for some effects
- Create signature visual styles
- Document successful combinations

### Dynamic Configurations
- Switch between module sets during events
- Performance mode vs. quality mode
- Genre-specific configurations
- Time-of-day variations

### Custom Module Settings
- Create property presets per module
- Save multiple configurations
- Quick-switch between looks
- A/B testing different settings

### Integration with Other Shaders
- Use June alongside other shader systems
- Combine with Mochie Folder if separate renderers
- Layer June with custom shaders
- Complex visual pipelines

## June Folder vs. Other Shader Folders

### vs. Mochie Folder
- Mochie: Mochie Screen FX specific, six-page layout
- June: June Shaders specific, module-based
- Different shader systems entirely

### vs. Shaders Folder
- Shaders: Generic, any shader, simple on/off
- June: June-specific, exposed properties, advanced control

**Use June Folder when**:
- Using June Shaders system
- Need per-module control
- Want property exposure
- Require shader locking

**Use Shaders Folder when**:
- Using non-June shaders
- Need simple shader launching
- Don't need June-specific features

## Next Steps

- [Presets Folder](presets-folder.md) - Save June configurations
- [Fader System](setting-up-fader-system.md) - Add real-time control
- [Mochie Folder](mochie-folder.md) - Alternative shader system
- [Shaders Folder](shaders-folder.md) - Generic shader launching

## Resources

- **June Shaders**: [https://kleineluka.gumroad.com/l/june](https://kleineluka.gumroad.com/l/june)
- **June Documentation**: Included with purchase
- **June Discord**: Community support (if available)
- **Tutorial Videos**: Check June creator's resources

---

**Navigation**: [← Mochie Folder](mochie-folder.md) | [Presets Folder →](presets-folder.md)

[Back to Home](index.md) | [View on GitHub](https://github.com/Cozen-Official/Enigma-Launchpad-OS)
