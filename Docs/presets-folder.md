# Presets Folder

The **Presets Folder** type allows users to save and load toggle state configurations in-game. It supports persistent PlayerData, enabling preset sharing, transferring between users, and reusing across sessions.

## Overview

Presets Folder enables runtime saving and loading of Launchpad configurations. Perfect for:

- Event scene presets
- Lighting and effect combinations
- User-specific setups
- Quick scene transitions
- Shared configurations
- Personal preference storage
- A/B testing configurations

## Key Features

- **In-Game Saving**: Save current toggle states during gameplay
- **Persistent Storage**: Presets saved to PlayerData survive sessions
- **Selective Saving**: Include or exclude specific folders from presets
- **User Transfer**: Share presets between players
- **Multiple Slots**: Save multiple presets for different scenarios
- **Quick Recall**: Instantly load saved configurations
- **Network Sync**: Loaded presets sync to all players

## Configuration

### Basic Setup

1. **Create a Presets Folder**:
   - In the Launchpad custom editor, add a new folder
   - Set Folder Type to **"Presets Folder"**
   - Name your folder (e.g., "Presets", "Saved Configs", "Scenes")

2. **Configure Preset Slots**:
   - Set number of preset slots (buttons)
   - Each slot can store one preset
   - Name slots if desired (e.g., "Preset 1", "Chill Scene", "Event Mode")

3. **Set Folder Inclusion**:
   - Choose which folders can be saved in presets
   - **Include All**: All folder states saved
   - **Exclude List**: Specific folders excluded
   - **Include List**: Only specific folders saved

4. **Configure Save/Load Buttons**:
   - Save button: Stores current state to selected preset slot
   - Load button: Recalls preset from selected slot
   - Clear button: Deletes preset from slot (optional)

### Preset Slot Configuration

#### Slot Settings
- **Slot Name**: Display name for the preset slot
- **Default Preset**: Pre-configured state for this slot
- **Protected**: Prevent overwriting (load-only)
- **Description**: Optional description of preset purpose

#### Folder Selection
Configure which folders participate in presets:

**Include All Mode**:
- All folders' states saved to preset
- Simplest configuration
- Best for complete scene presets

**Selective Mode**:
- Choose specific folders to include
- Example: Save shader states but not object visibility
- More control over what's saved

**Exclusion Mode**:
- All folders saved except excluded ones
- Example: Exclude Stats Folder (doesn't need saving)
- Useful when most folders should save

## How Presets Work

### Saving a Preset

1. **Configure Scene**: Adjust toggles to desired state across folders
2. **Select Slot**: Choose which preset slot to save to
3. **Press Save**: Current state captures to selected slot
4. **Confirmation**: Visual feedback confirms save

**What Gets Saved**:
- Toggle states (on/off) for included folders
- Button selection in exclusive folders
- Active states for non-exclusive folders
- Does not save: Fader positions (unless specifically configured)

### Loading a Preset

1. **Select Slot**: Choose preset slot to load
2. **Press Load**: Preset applies to scene
3. **State Update**: All included toggles update to preset state
4. **Network Sync**: Changes sync to all players

**What Happens**:
- Included folders restore to saved state
- Excluded folders remain unchanged
- Faders update if configured
- Visual update immediate

### PlayerData Persistence

Presets save to VRChat PlayerData:

- **Persistent**: Survives world reloads and re-joins
- **Per-User**: Each player has their own preset storage
- **Shareable**: Presets can be transferred between users (if configured)
- **Cross-Session**: Presets available in future visits

## Use Cases

### Event Scene Presets

Save complete event configurations:

```
Presets Folder: "Event Scenes"
Includes: All folders
Slots:
- "Opening" → Intro lighting, welcome screens, ambient effects
- "Main Event" → Performance lighting, active screens, intense effects
- "Intermission" → Calm lighting, info screens, reduced effects
- "Closing" → Outro lighting, thanks screens, fade effects
```

### DJ Performance Presets

Quick-switch visual setups:

```
Presets Folder: "Performance"
Includes: Shaders, Mochie, Properties folders
Excludes: Objects, Stats folders
Slots:
- "Chill" → Subtle blur, soft colors, low intensity
- "Energetic" → Kaleidoscope, vibrant colors, high intensity
- "Intense" → Distortion, strobe effects, max intensity
- "Ambient" → Slow animations, pastel colors, medium intensity
```

### Room Configuration Presets

Furniture and layout combinations:

```
Presets Folder: "Room Layouts"
Includes: Objects folder only
Slots:
- "Lounge" → Couches, coffee tables, ambient lighting
- "Dance Floor" → Open space, party lights, DJ booth
- "Theater" → Rows of seating, screen, dim lights
- "Default" → Standard layout
```

### Lighting Presets

Color and intensity combinations:

```
Presets Folder: "Lighting Scenes"
Includes: Properties folder (light colors/intensities)
Slots:
- "Day" → Bright white lights, high intensity
- "Sunset" → Warm orange/red lights, medium intensity
- "Night" → Cool blue lights, low intensity
- "Party" → Colorful rotating lights, high intensity
```

## Behavior Details

### Save Operation
- Captures current state of included folders
- Writes to preset slot in memory
- Persists to PlayerData
- Confirmation feedback to user

### Load Operation
- Reads preset from slot
- Applies state to included folders
- Networks changes to all players
- Visual update immediate
- Preserves excluded folder states

### Persistence
- Saved to VRChat PlayerData
- Survives world reload
- Per-player storage
- Accessible in future sessions

### Networking
- Preset loads sync across players
- All players see state changes
- Whitelist restrictions apply to loading (if enabled)
- Ownership transfer handled automatically

## Tips and Best Practices

### Organizing Presets
- **Clear Names**: Descriptive slot names
- **Logical Order**: Organize by usage flow
- **Documentation**: Document complex presets
- **Categories**: Group similar presets together

### Folder Selection Strategy
- **Complete Scenes**: Include all folders
- **Partial Updates**: Include subset for specific changes
- **Performance**: Exclude Stats folder (no need to save)
- **Context**: Consider what makes sense to save together

### User Experience
- **Visual Feedback**: Ensure save/load confirmation clear
- **Testing**: Test all preset slots work correctly
- **Documentation**: Provide guide for preset usage
- **Defaults**: Pre-configure useful default presets

### Performance Considerations
- **Save Frequency**: Don't save excessively often
- **PlayerData Size**: Be mindful of data limits
- **Sync Impact**: Large state changes may take moment to sync
- **Optimization**: Exclude unnecessary folders

## Common Issues

### Preset Not Saving
- Verify PlayerData permissions
- Check folder inclusion settings
- Ensure save button configured correctly
- Look for script errors in console

### Preset Not Loading
- Verify preset was actually saved
- Check if preset data exists in slot
- Ensure folders still exist (not deleted/renamed)
- Verify network sync working

### Partial Preset Load
- Check folder inclusion/exclusion settings
- Some folders may be excluded intentionally
- Verify all expected folders included in preset config

### Presets Not Persisting
- PlayerData may not be saving properly
- Check VRChat PlayerData functionality
- Verify world permissions
- Test in uploaded world (not just local)

### Sync Issues
- Network sync may be delayed
- Check Launchpad ownership
- Verify all players see changes (eventually)
- Large state changes take time to propagate

## Integration with Other Systems

### With All Folder Types
- Presets work with any folder type
- Include/exclude based on needs
- Complete scene control

### With Fader System
- Optionally include fader positions in presets
- Quick recall of fader configurations
- Performance setups with fader states

### With Whitelist
- Control who can save/load presets
- Prevent unauthorized preset changes
- Admin-only preset management

### With Objects Folder
- Save object visibility states
- Room layout presets
- Scene variant presets

### With Shader Folders
- Save visual effect configurations
- Recall shader combinations
- VJ performance setups

## Advanced Techniques

### Preset Hierarchies
- Create base presets, then modify
- Layer presets (load multiple sequentially)
- Progressive refinement approach

### Event Automation
- Preload presets for event segments
- Quick switching during live events
- Seamless transitions between scenes

### User Customization
- Allow users to save personal preferences
- Per-user optimal settings
- Returning user experience enhancement

### Preset Sharing
- Export/import preset data (if configured)
- Share configurations with others
- Community preset library

### A/B Testing
- Save two variations
- Quick comparison between options
- Iterate on designs efficiently

## Examples

### Concert Venue

```
Presets Folder: "Show Presets"
Includes: All folders
Slots:
- "Pre-Show" → House lights, info screens, ambient music
- "Opening Act" → Stage lights, screens active, effects subtle
- "Headliner" → Full lighting, all effects, max impact
- "Encore" → Special lighting, unique effects, finale mode
- "Post-Show" → Exit lights, info screens, calm atmosphere
```

### Virtual Gallery

```
Presets Folder: "Exhibition Modes"
Includes: Objects, Materials, Properties folders
Slots:
- "Exhibit A" → First art collection, appropriate lighting
- "Exhibit B" → Second collection, different theme
- "Exhibit C" → Third collection
- "Gallery Tour" → Overview layout, all visible
```

### Social Space

```
Presets Folder: "Space Configs"
Includes: Objects, Skybox, Properties folders
Slots:
- "Movie Night" → Theater setup, dim lights, large screen
- "Game Night" → Open space, bright lights, game areas
- "Hangout" → Lounge setup, ambient lighting, music space
- "Party" → Dance floor, party lights, energetic
```

### Photography Studio

```
Presets Folder: "Lighting Setups"
Includes: Properties folder (light properties)
Slots:
- "Portrait" → Soft front lighting, minimal shadows
- "Dramatic" → High contrast, angled lights
- "Product" → Even lighting, no harsh shadows
- "Creative" → Colored lights, unique angles
```

## Limitations

### PlayerData Constraints
- Limited storage per player
- Be mindful of data size
- Too many presets may hit limits
- Optimize what's saved

### Preset Scope
- Only saves toggle states
- Doesn't save continuous values (unless configured)
- Fader positions optional
- Some data may not persist

### Network Limitations
- Loading presets requires network sync
- Large state changes take time
- Network ownership affects loading
- Latency may delay updates

## Next Steps

- [Objects Folder](objects-folder.md) - Include object states in presets
- [Shaders Folder](shaders-folder.md) - Include shader configurations
- [Fader System](setting-up-fader-system.md) - Optionally include fader states
- Test presets thoroughly in uploaded world

---

**Navigation**: [← June Folder](june-folder.md)

[Back to Home](index.md) | [View on GitHub](https://github.com/Cozen-Official/Enigma-Launchpad-OS)
