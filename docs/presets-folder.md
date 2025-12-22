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


## Next Steps

- [Objects Folder](objects-folder.md) - Include object states in presets
- [Shaders Folder](shaders-folder.md) - Include shader configurations
- [Fader System](setting-up-fader-system.md) - Optionally include fader states
- Test presets thoroughly in uploaded world

---

**Navigation**: [← June Folder](june-folder.md)

[Back to Home](index.md) | [View on GitHub](https://github.com/Cozen-Official/Enigma-Launchpad-OS)
