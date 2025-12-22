# Objects Folder

The **Objects Folder** type controls GameObject active states globally. Each button toggles a GameObject on or off, making it visible or invisible in the world.

## Overview

Objects Folder is one of the most straightforward and commonly used Folder Types. It provides simple on/off control for any GameObject in your scene, ideal for:

- Room furniture and props
- Doors and gates
- Lighting fixtures
- Particle systems
- Alternative layouts
- Seasonal decorations
- Interactive elements

## Configuration

### Basic Setup

1. **Create an Objects Folder**:
   - In the Launchpad custom editor, add a new folder
   - Set Folder Type to **"Objects Folder"**
   - Name your folder (e.g., "Furniture", "Lights", "Props")

2. **Add GameObjects**:
   - Drag GameObjects from your scene hierarchy into the folder's object list
   - Each GameObject becomes a button
   - The order in the list determines button order

3. **Set Button Names**:
   - Each button automatically uses the GameObject's name
   - Override with custom names in the "Button Names" section
   - Keep names concise for display clarity

4. **Configure Exclusivity** (optional):
   - **Exclusive**: Only one GameObject active at a time
   - **Non-Exclusive**: Multiple GameObjects can be active simultaneously
   - Toggle the "Exclusive" checkbox in folder settings

### Advanced Options

- **Default State**: Set which GameObjects are active when the world loads
- **Animation Integration**: GameObjects can have animators that trigger on activation
- **Child Objects**: Toggling a parent GameObject affects all children
- **Multiple Instances**: The same GameObject can be referenced in multiple folders

## Use Cases

### Furniture Layouts

Create multiple room layouts with different furniture arrangements:

```
Objects Folder: "Living Room Layouts"
- Cozy Setup (couch, coffee table, rug)
- Party Setup (chairs around perimeter, open center)
- Theater Setup (rows of seating, facing screen)
```

Set to **Exclusive** so only one layout is active at a time.

### Lighting Configurations

Toggle different lighting setups:

```
Objects Folder: "Lighting"
- Ambient Lights
- Spotlight
- Disco Lights
- Blacklight
```

Can be **Non-Exclusive** to allow combining lights.

### Seasonal Decorations

Switch between holiday themes:

```
Objects Folder: "Decorations"
- Halloween Props
- Christmas Decorations
- New Year Decorations
- Default Setup
```

Set to **Exclusive** for clean theme switching.

### Doors and Access Points

Control access to different areas:

```
Objects Folder: "Doors"
- Main Entrance (door GameObject)
- VIP Area (barrier GameObject)
- Backstage (gate GameObject)
```

**Non-Exclusive** allows independent door control.

### Particle Effects

Toggle various particle systems:

```
Objects Folder: "Effects"
- Snow Particles
- Confetti Particles
- Fog Effect
- Fireflies
```

## Behavior Details

### Activation
- Button press: Toggles GameObject active state
- Active state: SetActive(true) - GameObject is visible and active
- Inactive state: SetActive(false) - GameObject is invisible and inactive

### Networking
- State syncs across all players
- Late joiners see current active states
- Whitelist restrictions apply (if enabled)

### Performance
- Toggling objects has minimal performance impact
- Inactive GameObjects don't render or update
- Use for performance optimization (disable expensive objects when not needed)

## Tips and Best Practices

### Organization
- **Group Related Objects**: Keep similar objects in the same folder
- **Use Parent GameObjects**: Create empty parent objects to group multiple objects under one toggle
- **Clear Naming**: Use descriptive names so players understand what each button does

### Performance Optimization
- **Disable Unused Objects**: Keep rarely-used objects disabled by default
- **Combine Objects**: Group small objects under parent GameObjects to reduce button count
- **Mesh Batching**: Consider Unity's static/dynamic batching when designing toggleable objects

### Scene Setup
- **Test Defaults**: Set appropriate default states for world load
- **Consider Late Joiners**: Default state should make sense for new players
- **Animator Interaction**: If objects have animators, ensure they handle activation correctly

### Exclusivity Decisions

**Use Exclusive When**:
- Only one option makes sense (room layouts, themes)
- Objects conflict with each other (overlapping geometry)
- Memory or performance requires limiting active objects

**Use Non-Exclusive When**:
- Options are independent (individual lights, props)
- Players may want combinations (multiple decorations)
- Flexibility is desired (mix-and-match setups)

## Common Issues

### GameObject Not Toggling
- Verify GameObject reference is assigned correctly
- Check that GameObject isn't controlled by another script
- Ensure GameObject isn't part of a network-synced prefab that conflicts

### Multiple Objects Toggle Together
- Check if objects are parented to the same root
- Verify each button has the correct GameObject assigned
- Ensure you're not using the same GameObject in multiple buttons unintentionally

### State Not Syncing
- Verify network sync settings
- Check that Launchpad has proper ownership
- Ensure world has UdonSharp properly configured

### Performance Issues
- Limit number of complex objects (high poly, many materials)
- Consider LOD (Level of Detail) for toggled objects
- Disable physics on inactive objects if possible

## Integration with Other Systems

### With Fader System
- Toggle objects, then use dynamic faders to control their properties
- Example: Toggle lights on, use fader to control brightness

### With Properties Folder
- Combine with Properties Folder for complete control
- Example: Objects Folder toggles visibility, Properties Folder changes colors

### With Presets Folder
- Save object toggle states as presets
- Load entire room configurations instantly
- Share layouts between players

## Examples

### Club DJ Booth Setup

```
Objects Folder: "DJ Booth Elements" (Non-Exclusive)
- Turntables
- CDJs
- Mixer Stand
- Monitor Speakers
- LED Panel
```

### Gallery Wall

```
Objects Folder: "Artwork" (Exclusive)
- Painting Set A
- Painting Set B
- Sculpture Collection
- Photography Set
```

### Game Modes

```
Objects Folder: "Game Areas" (Exclusive)
- Battle Arena
- Racing Track
- Puzzle Room
- Social Lounge
```

## Next Steps

- [Materials Folder](materials-folder.md) - Swap materials instead of toggling visibility
- [Properties Folder](properties-folder.md) - Control shader properties on objects
- [Presets Folder](presets-folder.md) - Save object toggle configurations

---

**Navigation**: [← Folder Types](folder-types.md) | [Materials Folder →](materials-folder.md)

[Back to Home](index.md) | [View on GitHub](https://github.com/Cozen-Official/Enigma-Launchpad-OS)
