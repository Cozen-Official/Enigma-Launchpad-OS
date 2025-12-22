# Skybox Folder

The **Skybox Folder** type switches the world's skybox material. Each button assigns a different skybox, and the folder includes auto-change functionality for cycling through skyboxes automatically.

## Configuration

### Basic Setup

1. **Create a Skybox Folder**:
   - In the Launchpad custom editor, add a new folder
   - Set Folder Type to **"Skybox Folder"**

2. **Add Skybox Materials**:
   - Drag skybox materials from your project into the folder's skybox list
   - Each skybox becomes a button
   - Materials must be skybox shaders (6-sided, cubemap, or procedural)

3. **Set Button Names**:
   - Each button automatically uses the material name

4. **Configure Auto-Change** (optional):
   - Enable auto-change to cycle through skyboxes automatically. If using a Skybox Folder, the page number display becomes an auto change button. When enabled, the color indicator hue shifts.
   - Set interval duration (seconds between changes)

## Behavior Details

### Skybox Switching
- Button press: Changes RenderSettings.skybox to selected material
- Immediate effect: Skybox updates instantly
- Lighting impact: May affect ambient lighting if configured
- Reflection probes: May need to update for accurate reflections

### Auto-Change Behavior
- **Enabled**: Cycles through skyboxes at set interval
- **Timer**: Countdown resets after each change
- **Manual Override**: Manual button press resets timer
- **Toggle Control**: Auto-change can be toggled on/off anytime

### Networking
- Skybox state syncs across all players
- Late joiners see current skybox
- Auto-change timer syncs (all players see changes together)

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
