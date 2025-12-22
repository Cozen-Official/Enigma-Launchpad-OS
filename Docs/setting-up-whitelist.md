# Setting up Whitelist

The Whitelist system controls who can interact with the Enigma Launchpad OS in your VRChat world. This guide covers configuration options and third-party integrations.

## What is the Whitelist System?

The Whitelist system restricts Launchpad/Mixer interaction to authorized users. When enabled:
- Only whitelisted players can use buttons and faders
- Non-whitelisted players see the interface but cannot interact
- Whitelist can be managed manually or via third-party systems
- Supports runtime additions with compatible integrations

## Whitelist Configuration

### Enabling the Whitelist

1. **Open Whitelist Settings**:
   - Select the Enigma Launchpad/Mixer prefab
   - Navigate to the Whitelist section in the custom editor

2. **Enable Whitelist**:
   - Check the "Enable Whitelist" option
   - The system now restricts access

3. **Choose Mode**:
   - **Manual Mode**: Manually enter player names
   - **Integration Mode**: Use third-party access control systems
   - **Hybrid Mode**: Combine manual and integration methods

### Manual Whitelist

Add players manually by VRChat display name:

1. **Add Players**:
   - In the Whitelist section, find the "Manual Entries" list
   - Click "Add Player"
   - Enter the player's exact VRChat display name
   - Repeat for each player

2. **Managing Entries**:
   - **Remove**: Click the X next to a player's name
   - **Edit**: Modify the name in the text field
   - **Reorder**: Drag entries to organize (optional)

3. **Name Matching**:
   - Names must match **exactly** (case-sensitive)
   - Include special characters and spaces
   - Verify names in VRChat before adding

### Always Allowed Roles

Configure which VRChat roles bypass the whitelist:

- **World Creator**: Always allow the world creator (you)
- **Instance Master**: Allow the instance master
- **Friends**: Allow your VRChat friends
- **None**: No automatic allowances (strict manual whitelist only)

Configure in the "Auto-Allow" section of whitelist settings.

## Third-Party Integrations

### OhGeezCmon Access Control

Allows runtime whitelist management through an integrated access control system.

#### Prerequisites
- OhGeezCmon Access Control package installed
- Access Control prefab added to scene

#### Setup Steps

1. **Install Access Control**:
   - Download from: [https://github.com/OhGeezCmon/VRC-AccessControl](https://github.com/OhGeezCmon/VRC-AccessControl)
   - Import the package into your project

2. **Add to Scene**:
   - Drag the Access Control prefab into your scene
   - Configure Access Control settings as needed

3. **Link to Launchpad**:
   - In Launchpad Whitelist settings, enable "OhGeezCmon Integration"
   - Assign the Access Control component reference
   - The systems now sync automatically

4. **Runtime Behavior**:
   - Authorized users can add/remove players via Access Control UI
   - Changes sync to Launchpad whitelist in real-time
   - Persistent across world instances

### Flatline Open Decks Manager

Advanced whitelist integration for event hosting and DJ setups.

#### Prerequisites
- Flatline Open Decks Manager purchased and installed
- Flatline prefab configured in scene

#### Setup Steps

1. **Purchase and Install**:
   - Purchase from: [https://lavysworlds.gumroad.com/l/flatline](https://lavysworlds.gumroad.com/l/flatline)
   - Follow Flatline installation instructions

2. **Configure Flatline**:
   - Set up Flatline Open Decks Manager in your scene
   - Configure decks, timeslots, and event settings

3. **Link to Launchpad**:
   - In Launchpad Whitelist settings, enable "Flatline Integration"
   - Assign the Flatline Manager component reference

4. **Event Mode**:
   - Flatline manages whitelist based on scheduled events
   - Current performer gets Launchpad access
   - Automatic access rotation based on timeslots
   - Supports multiple performers per event

5. **Runtime Behavior**:
   - Active DJ/performer automatically whitelisted
   - Access revoked when timeslot ends
   - Seamless transitions between performers
   - Override controls for event organizers

## Whitelist Behavior

### Access Denied Feedback

When non-whitelisted players try to interact:
- Visual feedback (button highlight, shake, or color change)
- Optional audio cue (configurable)
- Display message (optional)

Configure feedback in the Whitelist Settings section.

### Networking and Syncing

- **Whitelist State**: Syncs across all players in instance
- **Access Changes**: Update in real-time for all clients
- **Late Joiners**: Receive current whitelist state on join
- **Ownership**: Whitelist modifications require ownership transfer

### Override Controls

World creator and authorized users can:
- Temporarily override whitelist for specific players
- Disable whitelist for the current instance
- Transfer ownership to other players
- Reset whitelist to default state

## Use Cases

### Private Worlds
- Restrict access to world creator and friends only
- Prevent random players from changing settings
- Create exclusive experiences

### Event Hosting
- Use Flatline integration for scheduled DJ sets
- Automatic access rotation between performers
- Organizer override controls

### Club Environments
- Manual whitelist for regular DJs and staff
- Access Control for runtime additions
- Temporary access for guest performers

### Testing and Development
- Whitelist test users during development
- Easily add/remove testers
- Quick toggle for open testing sessions

## Common Issues

### Player Name Not Working
- Verify exact spelling and capitalization
- Check for extra spaces or special characters
- Confirm player name hasn't changed

### Integration Not Syncing
- Verify third-party component reference is assigned
- Check that integration package is properly installed
- Ensure both systems are initialized

### Everyone Has Access
- Check if "Instance Master" or "Friends" auto-allow is enabled
- Verify whitelist is actually enabled
- Confirm no override is active

### Late Joiners Can't Access
- Check network sync settings
- Verify whitelist syncs properly
- Test with multiple players in instance

## Security Considerations

### Best Practices
- **Keep Manual List Updated**: Remove players who no longer need access
- **Use Role Auto-Allow Carefully**: Understand implications of auto-allowing friends/masters
- **Monitor Access Control Users**: Limit who can modify the whitelist at runtime
- **Test Thoroughly**: Verify whitelist works correctly before public use

### Limitations
- Display names can be changed by users
- No built-in authentication beyond VRChat display name
- Third-party integrations depend on their own security
- World creator always has access (cannot be restricted)

## Advanced Configuration

### Custom Whitelist Logic
- Extend whitelist system with custom UdonSharp scripts
- Add time-based access (certain hours only)
- Implement point/currency systems for access
- Create group-based permissions

### Multiple Whitelist Levels
- Different whitelists for different folder types
- Separate button and fader whitelists
- Tiered access (basic vs. advanced controls)

### Event Logging
- Track who uses the Launchpad and when
- Log whitelist changes
- Analytics for world creators

## Next Steps

After setting up the whitelist:
- [Folder Types Overview](folder-types.md) - Learn about each Folder Type
- [Presets Folder](presets-folder.md) - Save configurations with whitelist support
- Test whitelist in VRChat with multiple users

---

**Navigation**: [← Fader System](setting-up-fader-system.md) | [Folder Types →](folder-types.md)

[Back to Home](index.md) | [View on GitHub](https://github.com/Cozen-Official/Enigma-Launchpad-OS)
