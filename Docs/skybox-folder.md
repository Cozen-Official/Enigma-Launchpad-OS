# Skybox Folder

The **Skybox Folder** type switches the world's skybox material. Each button assigns a different skybox, and the folder includes auto-change functionality for cycling through skyboxes automatically.

## Overview

Skybox Folder provides dynamic environment control by changing the world's skybox at runtime. Perfect for:

- Day/night cycles
- Weather variations
- Atmosphere changes
- Mood settings
- Event transitions
- Environment themes
- Automated ambiance rotation

## Configuration

### Basic Setup

1. **Create a Skybox Folder**:
   - In the Launchpad custom editor, add a new folder
   - Set Folder Type to **"Skybox Folder"**
   - Name your folder (e.g., "Environment", "Time of Day", "Atmosphere")

2. **Add Skybox Materials**:
   - Drag skybox materials from your project into the folder's skybox list
   - Each skybox becomes a button
   - Materials must be skybox shaders (6-sided, cubemap, or procedural)

3. **Set Button Names**:
   - Each button automatically uses the material name
   - Override with descriptive names
   - Examples: "Sunset", "Starry Night", "Overcast", "Clear Day"

4. **Configure Auto-Change** (optional):
   - Enable auto-change to cycle through skyboxes automatically
   - Set interval duration (seconds between changes)
   - Choose cycle order (sequential or random)

### Auto-Change Feature

The auto-change toggle appears in the Launchpad interface:

- **Toggle Button**: Separate button that enables/disables auto-cycling
- **Global Control**: Auto-change button is accessible from any folder
- **Interval Setting**: Configure time between skybox changes (in seconds)
- **Cycle Mode**: Sequential (in order) or Random

To configure:
1. Enable "Auto-Change" in folder settings
2. Set interval duration (e.g., 300 seconds = 5 minutes)
3. Choose cycle mode
4. Auto-change toggle appears on Launchpad interface

## Use Cases

### Day/Night Cycle

Create time-of-day progression:

```
Skybox Folder: "Time of Day" (Auto-Change: Sequential, 600s)
Skyboxes:
- "Dawn" (sunrise colors)
- "Morning" (bright clear sky)
- "Noon" (intense sunlight)
- "Afternoon" (warm light)
- "Dusk" (sunset colors)
- "Night" (dark with stars)
- "Midnight" (deep night sky)
```

### Weather Variations

Different weather skyboxes:

```
Skybox Folder: "Weather" (Manual switching)
Skyboxes:
- "Clear Sky"
- "Partly Cloudy"
- "Overcast"
- "Rainy"
- "Stormy"
- "Foggy"
```

### Themed Environments

Special atmosphere skyboxes:

```
Skybox Folder: "Atmosphere" (Auto-Change: Random, 180s)
Skyboxes:
- "Default"
- "Purple Nebula"
- "Orange Space"
- "Green Aurora"
- "Red Mars"
- "Underwater"
```

### Event Modes

Different moods for events:

```
Skybox Folder: "Event Atmosphere"
Skyboxes:
- "Chill" (calm blue sky)
- "Energetic" (vibrant colors)
- "Intense" (dramatic clouds)
- "Ambient" (soft gradients)
```

## Behavior Details

### Skybox Switching
- Button press: Changes RenderSettings.skybox to selected material
- Immediate effect: Skybox updates instantly
- Lighting impact: May affect ambient lighting if configured
- Reflection probes: May need to update for accurate reflections

### Auto-Change Behavior
- **Enabled**: Cycles through skyboxes at set interval
- **Timer**: Countdown resets after each change
- **Order**: Sequential (list order) or Random
- **Manual Override**: Manual button press resets timer
- **Toggle Control**: Auto-change can be toggled on/off anytime

### Networking
- Skybox state syncs across all players
- Late joiners see current skybox
- Auto-change timer syncs (all players see changes together)
- Whitelist restrictions apply (if enabled)

### Performance
- Skybox changes are very efficient
- No significant performance impact
- Cubemap resolution affects rendering
- Reflection probes update separately

## Tips and Best Practices

### Skybox Material Preparation
- **Resolution**: Use appropriate cubemap resolution (1024-2048 is common)
- **Compression**: Compress skybox textures for VRChat
- **Format**: Use cubemap or 6-sided skybox format
- **HDR**: Consider HDR skyboxes for better lighting

### Lighting Considerations
- **Ambient Source**: Skybox can be ambient light source
- **Ambient Intensity**: May need to adjust per skybox
- **Reflection Probes**: Update probes after skybox changes if needed
- **Lighting Baking**: Pre-baked lighting doesn't update with skybox

### Auto-Change Configuration
- **Interval Length**: Consider event pacing (shorter for energetic, longer for ambient)
- **Cycle Order**: Sequential for story/progression, random for variety
- **Skybox Count**: More skyboxes = more variety but longer full cycle
- **Manual Control**: Provide manual buttons even with auto-change enabled

### Organization
- **Logical Order**: Arrange skyboxes in meaningful sequence
- **Naming**: Clear names help players choose desired atmosphere
- **Thematic Grouping**: Group similar skyboxes together

## Common Issues

### Skybox Not Changing
- Verify skybox materials are assigned
- Check that materials use skybox shaders
- Ensure RenderSettings are not locked by other scripts
- Confirm folder type is set to Skybox Folder

### Lighting Looks Wrong
- Check ambient lighting source settings
- Verify skybox exposure is appropriate
- May need to adjust ambient intensity
- Consider reflection probe updates

### Auto-Change Not Working
- Verify auto-change is enabled in folder settings
- Check interval duration is set correctly
- Ensure timer is not paused
- Confirm network sync is working

### Skybox Appears Black
- Check skybox material shader is correct
- Verify textures are assigned to material
- Ensure cubemap faces are properly assigned
- Check exposure settings on skybox material

## Integration with Other Systems

### With Objects Folder
- Change skybox, then show/hide related objects
- Example: Night skybox → enable star particle systems

### With Properties Folder
- Adjust lighting properties when skybox changes
- Example: Darker skybox → reduce global light intensity

### With Presets Folder
- Save complete environment configurations
- Include skybox, lighting, and object states
- Create "scene" presets for events

### With Materials Folder
- Coordinate skybox with material themes
- Example: Sunset skybox → warm-colored materials

## Examples

### Concert Venue

```
Skybox Folder: "Stage Atmosphere" (Auto-Change: Random, 120s)
Skyboxes:
- "Default Stage"
- "Purple Space"
- "Neon Grid"
- "Abstract Waves"
- "Kaleidoscope"
- "Starfield"
```

### Virtual Gallery

```
Skybox Folder: "Gallery Ambiance" (Manual)
Skyboxes:
- "Neutral White"
- "Soft Daylight"
- "Museum Interior"
- "Modern Minimalist"
```

### Open World

```
Skybox Folder: "Time & Weather" (Auto-Change: Sequential, 480s)
Skyboxes:
- "Clear Dawn"
- "Sunny Morning"
- "Cloudy Noon"
- "Rainy Afternoon"
- "Clear Sunset"
- "Starry Night"
```

### Space Station

```
Skybox Folder: "Viewport" (Manual)
Skyboxes:
- "Earth Orbit"
- "Mars View"
- "Asteroid Field"
- "Nebula Region"
- "Deep Space"
- "Hyperspace"
```

## Advanced Techniques

### Coordinated Environment Changes
- Create preset that changes skybox + lighting + objects simultaneously
- Use multiple folders to build complete environment states
- Script custom transitions between environments

### Dynamic Lighting Integration
- Use Properties Folder to adjust directional light color/intensity per skybox
- Match lighting to skybox for cohesive atmosphere
- Automate lighting changes with skybox changes

### Reflection Probe Updates
- Trigger reflection probe updates after skybox changes
- Use UdonSharp to refresh probes
- Important for reflective materials in world

### Custom Skybox Shaders
- Create custom procedural skybox shaders
- Expose properties for runtime adjustment
- Use Properties Folder to modify skybox parameters

### Time-Based Auto-Change
- Set longer intervals for slower time progression
- Use sequential mode for realistic day/night cycles
- Calculate real-world time correspondence

## Performance Considerations

### Skybox Optimization
- **Texture Size**: Balance quality and memory (1024-2048 recommended)
- **Compression**: Use VRChat-compatible texture compression
- **Shader Complexity**: Simple skybox shaders perform best
- **Cubemap Format**: Properly formatted cubemaps are most efficient

### Reflection Probes
- Reflection probe updates can be expensive
- Consider not updating probes for every skybox change
- Use lower-resolution probes if possible
- Baked probes don't update with skybox

## Next Steps

- [Objects Folder](objects-folder.md) - Coordinate object visibility with skybox
- [Properties Folder](properties-folder.md) - Adjust lighting properties per skybox
- [Presets Folder](presets-folder.md) - Save complete environment states

---

**Navigation**: [← Properties Folder](properties-folder.md) | [Stats Folder →](stats-folder.md)

[Back to Home](index.md) | [View on GitHub](https://github.com/Cozen-Official/Enigma-Launchpad-OS)
