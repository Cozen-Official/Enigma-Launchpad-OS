# Installation


How to Install:

1.  You must have AudioLink imported into your project. Open VRChat
    Creator Companion, select your project, and add AudioLink from the
    official packages.

2.  Download the .unitypackage and import Enigma OS into your world
    project. You should see the files under the “Cozen” folder.

3.  At the root of the Enigma OS folder are two prefabs, a Launchpad
    variant and a Mixer variant. Drag the one you prefer to use in your
    scene. Both prefabs use the same Enigma OS. The Launchpad variant
    has only buttons, while the Mixer adds faders and a screen.

4.  If you do not have AutoLink installed, you will be prompted to
    import it. This is required for the Mixer. If you only plan to use
    the Launchpad, you can choose to ignore this message.

## Updating from a previous version

If you already have Enigma OS (or the older Enigma Launchpad) in your project, update from the editor instead of importing the package by hand. Select your Launchpad or Mixer in the scene, and in the Inspector use the **Download** button shown under “Update Available.” The in-editor updater removes your old Enigma folder before installing the new version, so nothing is left behind from the previous release.

If you do import a new version manually, delete your existing Enigma folder first — importing on top of an old install can leave stale files mixed between versions.

## Troubleshooting

**ProTV media controls stop showing the track title or time.** Importing any package can occasionally disturb ProTV’s scene wiring — this is a Unity/UdonSharp import quirk, not specific to Enigma OS. If ProTV’s media controls stop displaying the current title or time (playback still works), run **Tools → ProTV → Update Scene** to rebuild ProTV’s connections. Enigma OS 2.0.10 and later repairs this automatically when you open the scene or enter play mode, but **Update Scene** is ProTV’s own built-in fix and works on any version.

Supported Third Party Packages:

Shader Systems

- Mochie Screen FX:

  - Free version: <https://github.com/MochiesCode/Mochies-Unity-Shaders>

  - Extended Patreon
    version: <https://www.patreon.com/c/mochieshaders/posts>

  - There are templates included for both versions.

- June Shaders:

  - Purchase from: <https://kleineluka.gumroad.com/l/june>

  - Complete the full installation process as per June Shaders
    documentation

- Bean FX:

  - Download from: <https://booth.pm/ja/items/8178293>

- Taco FX:

  - Download from: <https://booth.pm/ja/items/8043371>

Access Control and Video Player

- OhGeezCmon Access Control: Runtime whitelist management

  - Repository: <https://github.com/OhGeezCmon/VRC-AccessControl>

  - Allows adding users to the whitelist during runtime

  - Install the package from the repository

- ProTV: Video player with whitelist support

  - Website: <https://protv.dev/>

  - Add the VCC repository and install from Creator Companion

- Flatline Open Decks Manager:

  - Download from: <https://lavysworlds.gumroad.com/l/flatline>


[← Introduction](index.md) | [Using Enigma OS →](using-enigma-os.md)
