# Setting up Whitelist

The Whitelist system controls who can interact with the Enigma Launchpad OS in your VRChat world. It can also drive and sync third party whitelists. This guide covers configuration options and third-party integrations.

## What is the Whitelist System?

The Whitelist system restricts Launchpad/Mixer interaction to authorized users. When enabled:
- Only whitelisted players can use buttons and faders
- Non-whitelisted players see the interface but cannot interact
- Whitelist can be managed manually or via third-party systems
- Supports runtime additions with compatible integrations

## Whitelist Configuration

1. **Open Whitelist Settings**:
   - Select the Enigma Launchpad/Mixer prefab
   - Navigate to the Whitelist section in the custom editor

2. **Enable Whitelist**:
   - Check the "Enable Whitelist" option
   - The system now restricts access

3. **Supply Names**
   - Supply names in the manual whitelist, or reference an external whitelist.
   - Access Control, ProTV, and Flatline whitelists can be used.
   - Names must match exactly, including special characters.
   - Set if instance master should always have access (if the world is open for others).

## Whitelist Behavior

The Editor lists which whitelist acts as a source of truth. If using Access Control, that is always the master whitelist and names should be added there, not using the ProTV whitelist UI or others. If used in conjuction with ProTV or Flatline, it will push the whitelists to keep all of them synced.

## Next Steps

After setting up the whitelist:
- [Folder Types Overview](folder-types.md) - Learn about each Folder Type
- [Presets Folder](presets-folder.md) - Save configurations with whitelist support
- Test whitelist in VRChat with multiple users

---

**Navigation**: [← Fader System](setting-up-fader-system.md) | [Folder Types →](folder-types.md)

[Back to Home](index.md) | [View on GitHub](https://github.com/Cozen-Official/Enigma-Launchpad-OS)
