# Properties Folder

The **Properties Folder** type sets specific shader property values across assigned renderers. Each button sets a property to a defined value, enabling precise control over shader parameters.

## Overview

Properties Folder provides fine-grained control over shader properties without swapping entire materials. It's ideal for:

- Light color changes
- Emission intensity adjustments
- Effect strength toggles
- Shader parameter presets
- Property-driven animations
- Color scheme variations
- Runtime shader customization

## Configuration

### Basic Setup

1. **Create a Properties Folder**:
   - In the Launchpad custom editor, add a new folder
   - Set Folder Type to **"Properties Folder"**
   - Name your folder (e.g., "Light Colors", "Effects", "Intensity")

2. **Select Target Renderers**:
   - Choose which renderers will have properties modified
   - Can target multiple renderers simultaneously
   - All targets receive the same property change

3. **Choose Property Name**:
   - Enter the shader property name (e.g., "_Color", "_EmissionColor", "_Intensity")
   - Property names are case-sensitive and must match exactly
   - Tip: Check your shader code or material inspector for property names

4. **Configure Buttons**:
   - Each button represents a specific value for the property
   - Add as many buttons as needed for different values
   - Set button names and values

5. **Set Property Type**:
   - **Color**: RGBA color values
   - **Float**: Single decimal value
   - **Vector**: 4-component vector (x, y, z, w)
   - **Int**: Integer value

6. **Define Values**:
   - For each button, set the value the property should become
   - Color picker for color properties
   - Numeric input for float/int/vector properties

### Advanced Options

- **Exclusivity**: Only one value active at a time (recommended for most cases)
- **Default Value**: Which button's value is active on world load
- **Property Range**: Some properties have min/max ranges (auto-detected)
- **Multiple Properties**: Create multiple folders to control different properties

## Use Cases

### Light Color Control

Change lighting colors:

```
Properties Folder: "Room Lights Color" (Exclusive)
Targets: Light_Renderer_01, Light_Renderer_02
Property: _EmissionColor (Color)
Buttons:
- "White" → (1, 1, 1, 1)
- "Red" → (1, 0, 0, 1)
- "Blue" → (0, 0, 1, 1)
- "Purple" → (0.5, 0, 0.5, 1)
```

### Effect Intensity

Toggle between intensity presets:

```
Properties Folder: "Glow Intensity" (Exclusive)
Targets: GlowObject_Renderer
Property: _Intensity (Float)
Buttons:
- "Off" → 0.0
- "Low" → 0.3
- "Medium" → 0.7
- "High" → 1.5
```

### Shader Effect States

Enable/disable shader features:

```
Properties Folder: "Distortion Effect" (Non-Exclusive)
Targets: Screen_Renderer
Property: _DistortionEnabled (Int)
Buttons:
- "Enable" → 1
- "Disable" → 0
```

### Color Schemes

Predefined color palettes:

```
Properties Folder: "Color Scheme" (Exclusive)
Targets: Accent_Renderer_01, Accent_Renderer_02, Accent_Renderer_03
Property: _Color (Color)
Buttons:
- "Cool Blue" → (0.2, 0.4, 0.8, 1)
- "Warm Orange" → (0.9, 0.5, 0.2, 1)
- "Nature Green" → (0.3, 0.7, 0.3, 1)
```

## Behavior Details

### Property Setting
- Button press: Sets property value on all target renderers
- Material instances: Creates instances to prevent affecting other objects
- Immediate effect: Changes apply instantly
- Type conversion: Values converted to appropriate property type

### Property Types

#### Color Properties
- **Format**: RGBA (0-1 range for each component)
- **Common Names**: `_Color`, `_EmissionColor`, `_TintColor`
- **HDR Support**: Values can exceed 1.0 for HDR colors
- **Alpha Channel**: Controls transparency if shader supports it

#### Float Properties
- **Format**: Single decimal number
- **Common Names**: `_Intensity`, `_Strength`, `_Alpha`, `_Metallic`
- **Range**: Often 0-1, but can be any value depending on shader
- **Precision**: Standard float precision

#### Integer Properties
- **Format**: Whole numbers
- **Common Names**: `_Mode`, `_Enabled`, `_Steps`
- **Use Case**: Toggle flags, enum values, step counts

#### Vector Properties
- **Format**: (x, y, z, w) four-component
- **Common Names**: `_ScrollSpeed`, `_Offset`, `_Tiling`
- **Use Case**: UV manipulation, multi-value parameters

### Networking
- Property changes sync across all players
- Late joiners see current property values
- Material instances network automatically
- Whitelist restrictions apply (if enabled)

### Performance
- Property changes are efficient
- Material instances created per renderer
- GPU property updates are fast
- Minimal CPU overhead

## Tips and Best Practices

### Finding Property Names
1. **Material Inspector**: Select material, switch to Debug mode
2. **Shader Code**: Look for Properties block in shader file
3. **Common Patterns**: Properties usually start with underscore (e.g., `_Color`)
4. **Documentation**: Check shader documentation for property lists

### Property Configuration
- **Test Values**: Experiment with values in material inspector first
- **Value Ranges**: Respect shader property ranges
- **HDR Colors**: Use HDR picker for emission colors
- **Naming**: Use clear button names describing the effect

### Performance Optimization
- **Material Instances**: System creates instances automatically
- **Property Count**: Limit simultaneous property changes
- **Update Frequency**: Properties Folder is for discrete changes, not continuous
- **Fader Integration**: Use faders for continuous property adjustment

### Organization
- **One Property Per Folder**: Each folder controls a single property
- **Related Properties**: Create multiple folders for related properties
- **Logical Grouping**: Group property folders by system (Lights, Effects, Colors)

## Common Issues

### Property Not Changing
- Verify property name spelling (case-sensitive)
- Check that shader has the property
- Ensure renderers are assigned correctly
- Confirm property type matches (color vs. float, etc.)

### Wrong Property Changed
- Check property name for typos
- Verify target renderers are correct
- Some shaders have similar property names

### Value Out of Range
- Check shader property range
- Some properties clamp values
- HDR colors can exceed 1.0 (this is correct for emission)

### Changes Not Syncing
- Verify network sync settings
- Check material instance creation
- Ensure Launchpad ownership is correct

## Integration with Other Systems

### With Materials Folder
- Materials Folder assigns material, Properties Folder adjusts its properties
- Example: Assign material with Materials Folder, set color with Properties Folder

### With Fader System
- Properties Folder for discrete presets, Faders for continuous adjustment
- Example: Properties Folder sets intensity preset, fader fine-tunes
- Can link dynamic faders to property buttons

### With Objects Folder
- Objects Folder toggles visibility, Properties Folder adjusts appearance
- Example: Show object, set its emission color

### With Presets Folder
- Save property values as part of presets
- Recall complete property configurations
- Create "scenes" with multiple property states

## Examples

### Stage Lighting Control

```
Properties Folder: "Stage Light Color" (Exclusive)
Targets: SpotLight_01, SpotLight_02, SpotLight_03, SpotLight_04
Property: _EmissionColor (Color)
Buttons:
- "Warm White" → (1, 0.95, 0.8, 1)
- "Cool White" → (0.8, 0.9, 1, 1)
- "Red" → (1, 0, 0, 1)
- "Blue" → (0, 0, 1, 1)
- "Green" → (0, 1, 0, 1)
- "Purple" → (0.5, 0, 0.5, 1)
```

### Hologram Effect

```
Properties Folder: "Hologram Intensity" (Exclusive)
Targets: Hologram_Renderer
Property: _HologramStrength (Float)
Buttons:
- "Off" → 0.0
- "Subtle" → 0.3
- "Normal" → 0.7
- "Strong" → 1.0
- "Maximum" → 1.5
```

### Water Shader Control

```
Properties Folder: "Water Color" (Exclusive)
Targets: WaterPlane_Renderer
Property: _WaterColor (Color)
Buttons:
- "Clear Blue" → (0.1, 0.3, 0.8, 0.7)
- "Tropical" → (0, 0.7, 0.8, 0.6)
- "Murky" → (0.4, 0.5, 0.3, 0.8)
- "Dark" → (0.1, 0.1, 0.2, 0.9)
```

### Material Metallic/Smoothness

```
Properties Folder: "Surface Metallic" (Exclusive)
Targets: Metal_Object_Renderer
Property: _Metallic (Float)
Buttons:
- "Matte" → 0.0
- "Brushed" → 0.3
- "Polished" → 0.7
- "Mirror" → 1.0
```

## Advanced Techniques

### Multi-Property Control
- Create multiple Properties Folders for the same renderers
- Control different properties independently
- Build complex shader state systems

### Conditional Properties
- Use with Objects Folder to enable/disable before changing properties
- Property changes only visible when object is active
- Optimize by not changing properties on inactive objects

### Animation Integration
- Set properties that animators then blend
- Create keyframe-like control
- Combine with Unity timeline for complex sequences

### Custom Shader Integration
- Design shaders with specific properties for Launchpad control
- Expose important parameters as properties
- Document property names and ranges

## Next Steps

- [Fader System](setting-up-fader-system.md) - Use faders for continuous property control
- [Materials Folder](materials-folder.md) - Swap materials instead of adjusting properties
- [Presets Folder](presets-folder.md) - Save property configurations

---

**Navigation**: [← Materials Folder](materials-folder.md) | [Skybox Folder →](skybox-folder.md)

[Back to Home](index.md) | [View on GitHub](https://github.com/Cozen-Official/Enigma-Launchpad-OS)
