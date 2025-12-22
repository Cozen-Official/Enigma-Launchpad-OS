# Mochie Folder

The **Mochie Folder** type provides global control of **Mochie Screen FX** shaders with a comprehensive six-page preset layout. It includes +/- controls, color selectors, AudioLink integration, and major effect toggles.

## Overview

Mochie Folder is specifically designed for the Mochie Screen FX shader system (both free and Patreon SFX X versions). It offers:

- Six-page preset layout for organized controls
- Global control across multiple screens
- +/- adjustment controls for precise values
- Color selector for outline colors
- AudioLink band toggles
- AudioLink strength controls
- Major effect toggles
- Configurable default values

## Prerequisites

### Required
- **Mochie Screen FX** shader package installed
  - Free version: [https://github.com/MochiesCode/Mochies-Unity-Shaders](https://github.com/MochiesCode/Mochies-Unity-Shaders)
  - Patreon SFX X version: [https://www.patreon.com/c/mochieshaders/posts](https://www.patreon.com/c/mochieshaders/posts)
- **AudioLink** in scene (for AudioLink features)
- Target renderers with Mochie Screen FX material

### Shader Version Support
- **Free SFX**: Supports most features
- **SFX X (Patreon)**: Full feature support including extended effects
- The folder automatically adapts to available features

## Configuration

### Basic Setup

1. **Install Mochie Screen FX**:
   - Download and import Mochie shaders package
   - Follow Mochie installation instructions
   - Verify shaders compile without errors

2. **Set Up Screen Renderer**:
   - Create or identify screen mesh(es) in your scene
   - Apply Mochie Screen FX material to renderer(s)
   - Configure material base settings if desired

3. **Create a Mochie Folder**:
   - In the Launchpad custom editor, add a new folder
   - Set Folder Type to **"Mochie Folder"**
   - Name your folder (e.g., "Screen FX", "Mochie Controls")

4. **Assign Target Renderers**:
   - Select all renderers that should receive Mochie control
   - Can be single or multiple screen renderers
   - All assigned renderers update simultaneously

5. **Configure Default Values**:
   - Set default values for each effect in the editor
   - Values populate the six preset pages
   - Customize intensity, colors, bands, etc.

### Six-Page Layout

The Mochie Folder provides six pages of organized controls:

#### Page 1: Primary Effects
- Main effect toggles (Blur, Zoom, etc.)
- Intensity controls (+/-)
- Basic parameter adjustments

#### Page 2: Color Controls
- Color selector toggles
- Outline color options
- Color intensity adjustments
- Hue shift controls

#### Page 3: AudioLink Bands
- Bass band toggle
- Low-mid band toggle
- High-mid band toggle
- Treble band toggle
- Multi-band toggles

#### Page 4: AudioLink Strength
- Strength adjustments per band
- Global AudioLink intensity
- Band weighting controls
- Response sensitivity

#### Page 5: Advanced Effects
- Secondary effect toggles
- Modifier controls
- Blend mode options
- Special features (SFX X)

#### Page 6: Presets/Misc
- Quick preset buttons
- Reset controls
- Utility functions
- Custom configurations

### Control Types

#### Toggle Buttons
- Enable/disable specific effects
- AudioLink band activation
- Feature switches

#### +/- Controls
- Increment/decrement values
- Fine-tune parameters
- Step-based adjustments
- Configurable step size

#### Color Selectors
- Predefined color options
- Outline color choices
- Tint selections
- Quick color switching

## Mochie Effects Configuration

### Common Effects

#### Blur
- Configurable blur amount
- Direction controls
- Sample count
- Blur type selection

#### Zoom
- Zoom intensity
- Zoom center point
- Animated zoom
- Direction controls

#### Distortion
- Distortion strength
- Pattern selection
- Animation speed
- Directional distortion

#### Color Effects
- Color shift/rotation
- Saturation adjustment
- Brightness/contrast
- Hue displacement

#### Outline
- Outline width
- Outline color (via color selector)
- Outline intensity
- Glow effect

### AudioLink Integration

#### Band Control
- **Bass**: Low frequency response
- **Low-Mid**: Lower mid-range
- **High-Mid**: Upper mid-range  
- **Treble**: High frequency response

Each band can:
- Toggle on/off independently
- Adjust strength/intensity
- Control effect response
- Modify color reactivity

#### AudioLink Features
- Audio-reactive intensity
- Audio-driven colors
- Frequency-based effects
- Synchronized visuals

## Use Cases

### DJ/VJ Performance

Live performance controls:

```
Mochie Folder: "Live FX"
Targets: StageScreen_01, StageScreen_02, StageScreen_03
Configuration:
- Page 1: Quick effect toggles (Blur, Zoom, Distortion)
- Page 2: Color choices for different moods
- Page 3: AudioLink band activation for music sync
- Page 4: Fine-tune AudioLink response
- Page 5: Advanced effects for transitions
- Page 6: Preset buttons for song changes
```

### Club Environment

Pre-configured club visuals:

```
Mochie Folder: "Club Screens"
Targets: DanceFloor_Screens (multiple)
Configuration:
- Default AudioLink bands: Bass + Treble
- Color selector: Neon palette
- Effects: Kaleidoscope + Audio-reactive intensity
- Presets: Different moods (Chill, Energetic, Intense)
```

### Event Stage

Coordinated stage displays:

```
Mochie Folder: "Stage Display"
Targets: Backdrop_Screen, Side_Screens
Configuration:
- Synchronized effects across all screens
- Color-coordinated for event branding
- AudioLink for live music reactivity
- Presets for different event segments
```

### Ambient Visuals

Background atmospheric effects:

```
Mochie Folder: "Ambiance"
Targets: Background_Screens
Configuration:
- Subtle blur and color shift
- Slow animation speeds
- Gentle AudioLink response
- Calming color palette
```

## Behavior Details

### Effect Application
- Changes apply to all target renderers simultaneously
- Global control ensures synchronized visuals
- Material instances created for independent control
- Property changes network-synced

### Button Functions
- **Toggle**: Enable/disable effects
- **+/-**: Increment/decrement by configured step
- **Color**: Set outline/tint color
- **Preset**: Recall saved configuration

### Value Ranges
- All parameters respect shader property ranges
- +/- controls have configurable min/max
- Values clamp to valid ranges
- Default values configurable in editor

### Networking
- All Mochie control states sync across players
- Late joiners see current effect configuration
- AudioLink integration syncs globally
- Whitelist restrictions apply (if enabled)

## Tips and Best Practices

### Configuration Strategy
- **Start Simple**: Enable basic effects first
- **Test Incrementally**: Add complexity gradually
- **Performance Check**: Monitor FPS with effects enabled
- **Save Presets**: Document working configurations

### AudioLink Setup
- **Verify AudioLink**: Ensure AudioLink in scene and working
- **Band Selection**: Choose appropriate bands for music type
- **Strength Tuning**: Adjust response for desired intensity
- **Testing**: Test with actual music during setup

### Performance Optimization
- **Effect Count**: Limit simultaneous complex effects
- **Screen Count**: Consider GPU load with multiple screens
- **Resolution**: Lower render resolution if needed
- **Effect Complexity**: Some effects more expensive than others

### Color Configuration
- **Color Palette**: Choose cohesive color selections
- **Contrast**: Ensure colors visible against backgrounds
- **Branding**: Match event/venue branding colors
- **Variety**: Provide diverse options for different moods

## Common Issues

### Effects Not Appearing
- Verify Mochie Screen FX properly installed
- Check renderer material uses Mochie shader
- Ensure target renderers assigned correctly
- Confirm no shader compilation errors

### AudioLink Not Working
- Check AudioLink component in scene
- Verify AudioLink initialized properly
- Ensure AudioLink bands configured
- Check music/audio source connected to AudioLink

### Performance Problems
- Too many effects enabled simultaneously
- Multiple high-resolution screens
- Complex effect combinations
- Reduce active effects or screen count

### Controls Not Responding
- Verify folder type set to Mochie Folder
- Check renderer references
- Ensure material instances created
- Look for script errors in console

### Wrong Shader Version
- Free vs. Patreon version differences
- Some features only in SFX X
- Check folder configuration matches shader
- Verify correct Mochie package imported

## Integration with Other Systems

### With Fader System
- Link dynamic faders to Mochie effect toggles
- Real-time parameter adjustment
- Example: Enable blur, use fader for blur amount

### With Presets Folder
- Save complete Mochie configurations
- Recall effect setups for different events
- Share configurations between users
- Event segment presets

### With AudioLink
- Essential for audio-reactive features
- Synchronized music visuals
- Band-based effect control
- Automatic intensity adjustment

### With Objects Folder
- Show/hide Mochie screens based on context
- Example: Toggle screen visibility, Mochie controls effects

## Advanced Techniques

### Multi-Screen Coordination
- Control multiple screens simultaneously
- Create synchronized visual experiences
- Different effects on different screen groups
- Use multiple Mochie Folders for independent control

### Effect Layering
- Combine multiple effects for complex visuals
- Order effects for desired appearance
- Balance performance vs. visual complexity
- Create signature looks

### Dynamic Presets
- Create presets for different song sections
- Quick switching during performances
- Mood-based configurations
- Genre-specific setups

### Custom Effect Combinations
- Experiment with parameter combinations
- Document successful configurations
- Create personal effect library
- Share discoveries with community

## Mochie Folder vs. Shaders Folder

**Use Mochie Folder when**:
- Using Mochie Screen FX specifically
- Need advanced Mochie-specific controls
- Want AudioLink integration
- Require +/- adjustment controls

**Use Shaders Folder when**:
- Using non-Mochie shaders
- Need simple on/off control
- Want generic shader launching
- Don't need Mochie-specific features

## Next Steps

- [June Folder](june-folder.md) - Alternative shader system control
- [Shaders Folder](shaders-folder.md) - Generic shader launching
- [Fader System](setting-up-fader-system.md) - Add real-time control
- [Presets Folder](presets-folder.md) - Save Mochie configurations

## Resources

- **Mochie GitHub**: [https://github.com/MochiesCode/Mochies-Unity-Shaders](https://github.com/MochiesCode/Mochies-Unity-Shaders)
- **Mochie Patreon**: [https://www.patreon.com/c/mochieshaders/posts](https://www.patreon.com/c/mochieshaders/posts)
- **Mochie Discord**: Community support and documentation
- **Tutorial Videos**: Available on YouTube and Mochie's channels

---

**Navigation**: [← Shaders Folder](shaders-folder.md) | [June Folder →](june-folder.md)

[Back to Home](index.md) | [View on GitHub](https://github.com/Cozen-Official/Enigma-Launchpad-OS)
