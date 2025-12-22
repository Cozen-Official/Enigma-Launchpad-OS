# Folder Types

Folder Types are the modular components that define how each folder in the Enigma Launchpad OS behaves. Each Folder Type implements specific functionality, from toggling GameObjects to controlling complex shader systems.

## What are Folder Types?

When you create a folder in the Launchpad custom editor, you assign it a **Folder Type**. This type determines:
- What the folder controls (objects, materials, shaders, etc.)
- How buttons behave when pressed
- What configuration options are available
- Whether exclusivity is supported
- How the folder interacts with other systems

The folder system provides all navigation and paging logic automatically, allowing you to focus on configuring the specific behavior you need.

## Available Folder Types

### Core Folder Types

#### [Objects Folder](objects-folder.md)
Toggle GameObject active states globally. Each button controls a GameObject's visibility.

**Use for**: Room elements, furniture, props, doors, layout variants

#### [Materials Folder](materials-folder.md)
Swap materials on renderers. Each button assigns a different material to configured renderers.

**Use for**: Poster/art swapping, room theme changes, texture variants

#### [Properties Folder](properties-folder.md)
Set shader property values. Each button sets a specific value for a shader property across assigned renderers.

**Use for**: Light colors, effect intensities, shader-driven state changes

### Screen Shader Folder Types

#### [Shaders Folder](shaders-folder.md)
Launch screen shaders on duplicated renderers. Supports any screen shader with minimal setup.

**Use for**: Custom screen effects, general shader launching, flexible shader setups

#### [Mochie Folder](mochie-folder.md)
Control Mochie Screen FX with a six-page preset layout. Includes +/- controls, color selectors, and AudioLink integration.

**Use for**: Mochie Screen FX control, audio-reactive effects, complex post-processing

#### [June Folder](june-folder.md)
Control June Shader modules with individual toggles and exposed properties. Supports flexible exclusivity.

**Use for**: June Shaders integration, modular post-processing, per-module control

### World State Folder Types

#### [Skybox Folder](skybox-folder.md)
Switch world skyboxes. Includes auto-change functionality for cycling through skyboxes.

**Use for**: Environment changes, day/night cycles, atmosphere variations

#### [Stats Folder](stats-folder.md)
Display world and instance statistics on button displays. Integrates with World Stats asset.

**Use for**: Analytics, player count, instance info, world metrics

#### [Presets Folder](presets-folder.md)
Save and load toggle state configurations. Supports persistent PlayerData for sharing and transferring presets.

**Use for**: Scene presets, event configurations, saved states, sharing setups

## Exclusivity

Many Folder Types support **exclusivity mode**:

- **Exclusive**: Only one button can be active at a time
- **Non-Exclusive**: Multiple buttons can be active simultaneously

When a folder is set to exclusive:
- Pressing a button deactivates the previously active button
- Useful for mutually exclusive states (e.g., only one skybox active)
- Prevents conflicting configurations

## Pages and Navigation

Folders support multiple pages:
- **Automatic pagination**: Add more buttons, pages create automatically
- **Page navigation**: Forward/back buttons on the Launchpad interface
- **Page indicator**: Shows current page number and total pages

## Working with Multiple Folders

### Folder Interaction
- Folders operate independently by default
- Preset system can capture state across multiple folders
- Whitelist applies globally to all folders
- Fader system can link to toggles in any folder


## Next Steps

Explore the specific Folder Type you need to configure:

- [Objects Folder](objects-folder.md) - Toggle GameObjects
- [Materials Folder](materials-folder.md) - Swap materials
- [Properties Folder](properties-folder.md) - Set shader properties
- [Skybox Folder](skybox-folder.md) - Change skyboxes
- [Stats Folder](stats-folder.md) - Display analytics
- [Shaders Folder](shaders-folder.md) - Launch screen shaders
- [Mochie Folder](mochie-folder.md) - Control Mochie Screen FX
- [June Folder](june-folder.md) - Control June Shaders
- [Presets Folder](presets-folder.md) - Save/load configurations

---

**Navigation**: [← Whitelist](setting-up-whitelist.md)

[Back to Home](index.md) | [View on GitHub](https://github.com/Cozen-Official/Enigma-Launchpad-OS)
