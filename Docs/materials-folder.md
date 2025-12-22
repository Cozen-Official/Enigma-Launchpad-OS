# Materials Folder

The **Materials Folder** type globally assigns materials to configured renderers. Each button swaps the material on one or more renderers, allowing dynamic visual changes.

## Overview

Materials Folder enables runtime material swapping without toggling GameObject visibility. It's perfect for:

- Swapping posters and artwork
- Changing room themes and color schemes
- Texture variations on objects
- Surface material changes
- Display screen content
- Signage and banners
- Dynamic branding

## Configuration

### Basic Setup

1. **Create a Materials Folder**:
   - In the Launchpad custom editor, add a new folder
   - Set Folder Type to **"Materials Folder"**
   - Name your folder (e.g., "Wall Art", "Theme", "Screens")

2. **Assign Target Renderers**:
   - Select the renderer(s) that will receive material changes
   - Can be MeshRenderer, SkinnedMeshRenderer, etc.
   - Multiple renderers can share the same material assignment

3. **Add Materials**:
   - Drag materials from your project into the folder's material list
   - Each material becomes a button
   - Order determines button position

4. **Set Button Names**:
   - Each button automatically uses the material's name
   - Override with custom names for clarity
   - Examples: "Sunset Poster", "Neon Theme", "Wood Texture"

5. **Configure Exclusivity**:
   - **Exclusive**: Only one material active at a time (recommended)
   - **Non-Exclusive**: Multiple materials can be assigned (rarely used)

### Advanced Options

- **Material Slot Selection**: Choose which material slot on the renderer to modify
- **Multiple Renderers**: Apply the same material to multiple objects simultaneously
- **Default Material**: Set which material is active on world load
- **Material Properties**: Materials can have animated properties

## Use Cases

### Poster/Art Gallery

Swap artwork on walls:

```
Materials Folder: "Gallery Wall" (Exclusive)
Target: Gallery_Plane renderer
Materials:
- Abstract_Art_01
- Portrait_02
- Landscape_03
- Photography_04
```

Each button displays different artwork on the same plane.

### Room Theme Switcher

Change room aesthetics:

```
Materials Folder: "Room Theme" (Exclusive)
Targets: Floor_Renderer, Wall_Renderer, Ceiling_Renderer
Materials:
- Modern_Theme (white walls, light wood floor)
- Industrial_Theme (concrete, metal)
- Cozy_Theme (warm colors, carpet)
```

All renderers update simultaneously to match the theme.

### Display Screens

Change content on screens:

```
Materials Folder: "Main Screen" (Exclusive)
Target: Screen_Renderer
Materials:
- Logo_Animation
- Event_Schedule
- Welcome_Message
- Visuals_A
```

### Surface Materials

Change surface properties:

```
Materials Folder: "Floor Material" (Exclusive)
Target: Floor_Renderer
Materials:
- Marble
- Wood_Oak
- Carpet_Red
- Tile_Checkered
```

## Behavior Details

### Material Assignment
- Button press: Assigns material to all target renderers
- Material slot: Specific material slot on renderer (default: slot 0)
- Material instance: Creates material instance for each renderer
- Shader properties: Retain from assigned material

### Networking
- Material assignments sync across all players
- Late joiners see current material state
- Material property changes sync if networked
- Whitelist restrictions apply (if enabled)

### Performance
- Material swapping is lightweight
- Material instances created per renderer
- Consider material count for memory usage
- Shader complexity affects performance more than swapping

## Tips and Best Practices

### Material Preparation
- **Optimize Textures**: Use appropriate texture sizes
- **Shader Selection**: Choose performant shaders
- **Texture Compression**: Use compressed formats for VRChat
- **Material Variants**: Create material variants instead of duplicating

### Renderer Configuration
- **Material Slots**: Know which slot you're targeting
- **Shared Materials**: Multiple renderers can share materials
- **UV Mapping**: Ensure UVs are correct for all materials
- **Lightmap Considerations**: Be aware of lightmap UVs

### Organization
- **Naming Convention**: Clear material names for easy identification
- **Folder Structure**: Organize materials in project folders
- **Preview Icons**: Use material preview for visual reference
- **Categories**: Group similar materials (art, themes, surfaces)

### Performance Optimization
- **Texture Atlases**: Combine textures when possible
- **Material Count**: Limit total unique materials
- **Shader Variants**: Minimize shader variant count
- **Draw Calls**: Consider batching implications

## Common Issues

### Material Not Changing
- Verify renderer reference is assigned
- Check material slot index (0-based)
- Ensure material is assigned in the list
- Confirm renderer has material slots

### Wrong Material Slot Modified
- Check material slot index in configuration
- Renderers can have multiple material slots
- Default is slot 0 (first material)

### Material Looks Different
- Check shader on material
- Verify texture assignments
- Review material properties
- Consider lighting in scene

### Performance Drop
- Too many high-resolution textures
- Complex shader on materials
- Too many material instances
- Consider texture compression and LOD

## Integration with Other Systems

### With Objects Folder
- Objects Folder shows/hides, Materials Folder changes appearance
- Example: Show table (Objects), change tablecloth (Materials)

### With Properties Folder
- Materials Folder assigns material, Properties Folder adjusts properties
- Example: Assign material, adjust color/intensity with Properties Folder

### With Fader System
- Assign material, use faders to adjust properties in real-time
- Example: Swap material, fade intensity/color with fader

### With Presets Folder
- Save material assignments as part of preset
- Load complete visual configurations
- Share themed setups between players

## Examples

### Virtual Museum

```
Materials Folder: "Exhibit A" (Exclusive)
Target: ExhibitFrame_Renderer
Materials:
- Ancient_Artifact_Info
- Renaissance_Painting
- Modern_Sculpture_Info
- Interactive_Display
```

### Nightclub Lighting

```
Materials Folder: "Wall Screens" (Exclusive)
Targets: LeftWall_Screen, RightWall_Screen, BackWall_Screen
Materials:
- Visualizer_Red
- Visualizer_Blue
- Visualizer_Rainbow
- Logo_Display
```

### Product Showcase

```
Materials Folder: "Product Variants" (Exclusive)
Target: Product_Renderer
Materials:
- Color_Red
- Color_Blue
- Color_Green
- Limited_Edition
```

### Seasonal Themes

```
Materials Folder: "Seasonal Decorations" (Exclusive)
Targets: Banner_Left, Banner_Right, Floor_Mat
Materials:
- Halloween_Theme
- Christmas_Theme
- Summer_Theme
- Default_Theme
```

## Advanced Techniques

### Multi-Slot Control
- Configure multiple Materials Folders for different material slots
- Independent control of different material slots
- Example: Folder 1 controls slot 0 (base), Folder 2 controls slot 1 (detail)

### Animated Materials
- Use materials with animated shaders
- Combine with timeline animations
- Create dynamic visual effects

### Material Property Blocks
- Use Properties Folder for per-instance property changes
- Combine material swapping with property modification
- Advanced material control workflows

## Next Steps

- [Properties Folder](properties-folder.md) - Modify material properties instead of swapping
- [Objects Folder](objects-folder.md) - Combine with visibility toggles
- [Presets Folder](presets-folder.md) - Save material configurations

---

**Navigation**: [← Objects Folder](objects-folder.md) | [Properties Folder →](properties-folder.md)

[Back to Home](index.md) | [View on GitHub](https://github.com/Cozen-Official/Enigma-Launchpad-OS)
