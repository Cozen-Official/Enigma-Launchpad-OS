# Setting up Fader System

The Fader System is exclusive to the **Enigma Mixer** prefab and provides real-time control of shader properties through hand-collider-driven faders with visual feedback. This guide covers both static and dynamic faders.

## What are Faders?

Faders are interactive sliders that allow players to adjust shader properties in real-time, including:
- Float values (intensity, strength, range)
- Integer values (steps, counts)
- Color properties (with hue rotation)
- Range properties (min/max values)

Faders provide immediate visual feedback and smooth property transitions, making them ideal for live adjustments during events or performances.

## Fader Types

### Static Faders
- **Permanently assigned** to specific properties
- Always visible and accessible
- Ideal for frequently-adjusted properties
- Examples: Master intensity, color rotation, blur strength

### Dynamic Faders
- **Populate on-demand** when linked toggles are enabled
- Appear in empty fader slots
- Allow individual control per active effect
- Automatically clear when toggle is disabled
- Unlimited dynamic faders can be defined (limited by available slots)

## Setting up Static Faders

Static faders are configured directly in the Mixer custom editor:

### Basic Configuration

1. **Open Fader Settings**:
   - Select the Enigma Mixer prefab
   - Navigate to the Fader Settings section in the custom editor

2. **Assign a Fader Slot**:
   - Choose an available fader slot (physical fader on the prefab)
   - Click to configure the fader

3. **Select Property Type**:
   - Choose from: Float, Int, Range, or Color
   - The type determines how the fader behaves

4. **Assign Target**:
   - **Material Property**: Select material and property name
   - **Renderer Property**: Select renderer and property name
   - Property names are auto-populated from the selected material/renderer

5. **Set Value Range**:
   - **Min Value**: Minimum fader position value
   - **Max Value**: Maximum fader position value
   - **Default Value**: Initial fader position
   - These values auto-populate from the shader's property definition

6. **Configure Display**:
   - Set display name (shown above fader)
   - Choose display format (decimal places, units)

### Property Type Details

#### Float Properties
- Smooth continuous values
- Examples: Intensity (0.0 - 1.0), Blur strength (0 - 10)
- Display shows current value with decimal precision

#### Integer Properties
- Discrete whole number values
- Examples: Step count, iteration count
- Fader snaps to integer positions

#### Range Properties
- Bounded values with shader-defined min/max
- Automatically inherits shader property limits
- Examples: Property ranges defined in shader

#### Color Properties
- Controls hue rotation degree (0° - 360°)
- Rotates color around the color wheel
- Display shows current hue angle
- Visual color preview available

### Multiple Target Support

Static faders can affect multiple materials/renderers:
1. Add additional targets in the target list
2. All targets update simultaneously
3. Useful for globally adjusting multiple screens or objects

## Setting up Dynamic Faders

Dynamic faders provide per-toggle control without cluttering the fader bank:

### Basic Configuration

1. **Create a Dynamic Fader Definition**:
   - In the Fader Settings section, navigate to Dynamic Faders
   - Click "Add Dynamic Fader"

2. **Link to Toggle**:
   - Select which folder and button the fader links to
   - The fader only appears when that toggle is active

3. **Configure Property**:
   - Follow the same steps as static faders
   - Property target is specific to the linked toggle's object

4. **Set Priority** (optional):
   - Determines which dynamic faders appear first if slots are limited
   - Higher priority faders take precedence

### Dynamic Fader Behavior

- When toggle **enabled**: Fader appears in the next available empty slot
- When toggle **disabled**: Fader clears from the slot
- If all slots full: Lower priority faders may not appear
- Multiple dynamic faders per toggle: All appear when toggle is active

### Use Cases for Dynamic Faders

#### Per-Shader Controls
- Link faders to shader toggles in a Shaders Folder
- Each shader gets its own intensity, color, or effect controls
- Example: Each kaleidoscope shader has its own segment count fader

#### Per-Effect Properties
- Link to Mochie or June toggle controls
- Adjust specific effect properties individually
- Example: Different blur strengths for different blur types

#### Conditional Properties
- Link to state toggles in Objects or Properties folders
- Properties only adjustable when certain objects are active
- Example: Light color only adjustable when light is on

## Fader Display System

The fader display shows:
- **Fader Name**: Property being controlled
- **Current Value**: Real-time value display
- **Visual Indicator**: Fader position representation
- **Units** (optional): Display units (%, degrees, etc.)

### Customizing Display

1. Set display name in fader configuration
2. Choose decimal precision for float values
3. Add unit suffix (optional)
4. Configure color for color property displays

## Testing Faders

### In-Editor Testing
1. Enter Play Mode
2. Use VR controls or mouse to interact with faders
3. Observe property changes in real-time
4. Verify display updates correctly

### In-World Testing
1. Upload world to VRChat
2. Test with actual VR hand colliders
3. Verify smooth transitions and accurate values
4. Check networking/syncing behavior

## Performance Considerations

### Optimization Tips
- **Update Rate**: Faders update every frame; limit total fader count if needed
- **Property Complexity**: Complex shader properties may impact performance
- **Material Instances**: Each fader creates material instances; be mindful of count
- **Network Traffic**: Fader movements sync across network; avoid excessive changes

### Recommended Limits
- **Static Faders**: 8-16 depending on complexity
- **Dynamic Faders**: 20-30 definitions (not all active simultaneously)
- **Total Active Faders**: Keep under 16 for optimal performance

## Common Issues

### Fader Not Responding
- Check that material/renderer is assigned correctly
- Verify property name matches shader property
- Ensure fader slot is properly configured

### Value Not Changing
- Check min/max range is correct
- Verify property type matches shader property type
- Ensure material instance is being created

### Dynamic Fader Not Appearing
- Verify toggle link is correct
- Check that fader slots are available
- Confirm toggle is actually enabled
- Check fader priority if slots are full

### Display Shows Wrong Value
- Verify property type is correct
- Check decimal precision settings
- Ensure display format matches property range

## Advanced Techniques

### Linked Fader Groups
- Create multiple faders controlling related properties
- Example: RGB color components as separate faders
- Synchronized control for complex effects

### Fader Presets via Preset System
- Save fader positions in preset folders
- Recall entire fader configurations
- Useful for scene transitions or event segments

### Conditional Fader Visibility
- Use dynamic faders with complex toggle logic
- Show/hide faders based on world state
- Create context-sensitive control layouts

## Next Steps

After setting up the fader system:
- [Setting up Whitelist](setting-up-whitelist.md) - Control who can use faders
- [Properties Folder](properties-folder.md) - Use faders with property toggles
- [Presets Folder](presets-folder.md) - Save fader configurations

---

**Navigation**: [← Screen Shaders](setting-up-screen-shaders.md) | [Whitelist →](setting-up-whitelist.md)

[Back to Home](index.md) | [View on GitHub](https://github.com/Cozen-Official/Enigma-Launchpad-OS)
