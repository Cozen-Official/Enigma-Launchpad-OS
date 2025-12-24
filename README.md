<img width="974" height="571" alt="image" src="https://github.com/user-attachments/assets/18bcf20e-bb02-4ba7-8d41-5608a7c81c42" />

# **Enigma Launchpad OS**

Enigma Launchpad OS is a modular control system designed for VRChat worlds. It aims to be an all-in-one solution for runtime world modification for VR event hosts. It provides a unified interface for toggling objects and materials, modifying shader properties, controlling screen shaders, displaying analytics, creating persistent presets, changing skyboxes, and more, all inside a cohesive, synced UdonSharp controller. The Launchpad is built to handle complex world and shader setups and substantially simplifies world UI creation across a wide variety of tasks. Two prefab versions are included: **Enigma Launchpad** (buttons only) and **Enigma Mixer** (buttons plus a fader system).

While several features are tailored to VRChat club environments, the system is not limited to shader workflows. It is useful for any creator who needs to control or automate elements of their world at runtime. For example, you can create a **Materials Folder** to swap pictures on a wall, use an **Objects Folder** to switch between furniture layouts, use a **Properties Folder** to modify light colors, add a **Mochie Folder** or **June Folder** for post-processing controls, or configure a **Skybox Folder** to change the environment. This removes the need for half-baked Udon scripts, difficult-to-maintain UI canvases, or tedious manual reordering of toggles.

The Launchpad prefab consists of a **3x3 grid of buttons** with displays, a **folder display**, a **page display** with an **auto-change toggle**, and directional navigation buttons. The Mixer prefab adds **faders** with a custom fader system, fader displays, a **screen panel** with multiple display modes including a custom AudioLink tablet with AutoLink, video-player controls for popular video prefabs, and more. The in-game UI is driven by **folders**, each assigned a **Folder Type**. Folder Types act as modules that define how each folder behaves. All folder navigation and paging logic are handled by the Launchpad OS. Because this asset includes many distinct features, refer to the documentation for the specific **Folder Type** you are configuring if questions arise.

---

## **Feature Overview**

• **Custom editor** that significantly simplifies UI setup.  
• **Launchpad prefab** with toggle buttons and displays using folder/page logic.  
• **Mixer prefab** adding faders and extended controls.  
• Easy shader-launching workflow designed to support diverse world setups.  
• **Folder Types** implementing different behaviors, including optional **exclusivity** (only one toggle active per folder).  
• **Whitelist system** with optional third-party integrations. Supports manual names, **OhGeezCmon Access Control** (not included).  
• **Editor-side preview foldout** that simulates the Launchpad UI to visualize folder/page layout.  
• **Fader system** for modifying shader properties using hand-collider-driven faders with display feedback. Supports dynamic fader assignment based on enabled toggles in folders.  
• Built-in support for popular shader sets like **Mochie Screen FX** and **June Shaders** (must be imported separetely).  
• Integrates with popular tools like ProTV, VideoTXL, Access Control, AutoLink, etc.  

---

## **Folder Types**

### **Objects Folder**
Each toggle changes the active state of a GameObject globally. Drag GameObjects into the list, and buttons populate automatically. Useful for furniture toggles, door states, props, and layout switching.

### **Materials Folder**
Each toggle globally assigns a material to a configured renderer. Drag materials in and each button will set a different material. Useful for swapping posters, changing the theme of a room, etc.

### **Properties Folder**
Each toggle sets a specific shader property to a defined value across assigned renderers. Useful for color changes, shader-driven effects, or any runtime-adjustable property.

### **Shaders Folder**
Each toggle launches a screen-shader on a duplicated mesh renderer, avoiding manual scene setup.

### **Skybox Folder**
Each toggle switches the world’s skybox. Drag in skybox materials and buttons populate automatically. The **auto-change button** cycles skyboxes at a configurable interval and can be toggled on/off from any folder.

### **Mochie Folder**
A six-page preset layout that provides global control of **Mochie screen shaders** (not included). Supports most Mochie features for both standard SFX and paid SFX X versions, with **+/- controls**, a **color selector** for outline colors, **AudioLink band toggles**, **AudioLink strength toggles**, and major effect toggles. Values are configurable in the editor.

### **June Folder**
Provides global toggles for **June screen-shader modules** (not included). Each toggle corresponds to a specific June module, and all module properties are exposed in the editor. **Exclusivity** can be handled per module or per folder depending on setup. The Launchpad handles all shader locking and setup.

### **Presets Folder**
Allows users to save **presets** of toggle states in-game, including options to include or exclude specific folders from the preset. Presets can be saved to and loaded from persistent **PlayerData**, enabling sharing, transferring between users, or reusing personal presets across sessions. This allows coordinated multi-toggle configurations, useful for lighting/shader presets during live events.

### **Stats Folder**
Displays world and instance statistics on button displays. Integrates with the author’s **World Stats** asset to query metrics from the VRChat API.

## **Fader System**

### **Static Faders**
Set each fader to a property and globally modify values at runtime. Supports int/float/range/color properties. Colors support setting the degree of hue rotation, and all numeric values support default/min/max values that are auto-populated from the targeted material/renderer.
### **Dynamic Faders**
Create an unlimited amount of dynamic faders, which populate on empty fader slots when a linked toggle from another folder is set. This allows you to individually control the properties for each active effect without needing to make hundreds of individual sliders.

## **Documentation**

For complete documentation, visit our [Documentation Site](https://cozen-official.github.io/Enigma-Launchpad-OS/).

**Quick Links:**
- [Getting Started Guide](https://cozen-official.github.io/Enigma-Launchpad-OS/)
- [Dependencies](https://cozen-official.github.io/Enigma-Launchpad-OS/dependencies.html)
- [Setting up Prefab](https://cozen-official.github.io/Enigma-Launchpad-OS/setting-up-prefab.html)
- [Setting up Screen Shaders](https://cozen-official.github.io/Enigma-Launchpad-OS/setting-up-screen-shaders.html)
- [Setting up Fader System](https://cozen-official.github.io/Enigma-Launchpad-OS/setting-up-fader-system.html)
- [Setting up Whitelist](https://cozen-official.github.io/Enigma-Launchpad-OS/setting-up-whitelist.html)

**Folder Types:**
- [Folder Types Overview](https://cozen-official.github.io/Enigma-Launchpad-OS/folder-types.html)
- [Objects Folder](https://cozen-official.github.io/Enigma-Launchpad-OS/objects-folder.html)
- [Materials Folder](https://cozen-official.github.io/Enigma-Launchpad-OS/materials-folder.html)
- [Properties Folder](https://cozen-official.github.io/Enigma-Launchpad-OS/properties-folder.html)
- [Shaders Folder](https://cozen-official.github.io/Enigma-Launchpad-OS/shaders-folder.html)
- [Skybox Folder](https://cozen-official.github.io/Enigma-Launchpad-OS/skybox-folder.html)
- [Mochie Folder](https://cozen-official.github.io/Enigma-Launchpad-OS/mochie-folder.html)
- [June Folder](https://cozen-official.github.io/Enigma-Launchpad-OS/june-folder.html)
- [Presets Folder](https://cozen-official.github.io/Enigma-Launchpad-OS/presets-folder.html)
- [Stats Folder](https://cozen-official.github.io/Enigma-Launchpad-OS/stats-folder.html)

---

## **DEPENDENCIES**
Install these dependencies before importing the Enigma Launchpad package.

Launchpad Prefab:  
• AudioLink (Install from Creator Companion)

Mixer Prefab:  
• AudioLink (Install from Creator Companion)  
• AutoLink (go to repo https://github.com/lackofbindings/AutoLink and add the VCC, then install from Creator Companion)  

Optional Dependencies(for a sepecifc folder or feature):  
• Mochie Screen FX (Enables basic Mochie layout using free Screen FX https://github.com/MochiesCode/Mochies-Unity-Shaders, or extended layout using Patreon version at https://www.patreon.com/c/mochieshaders/posts)  
• June Shaders (Enables June Folder type, purchase from https://kleineluka.gumroad.com/l/june and compelete the install process). Support for the free version will be added soon, if you would like to use the free version, use the Shaders folder instead.  
• ProTV (From https://protv.dev/, add the VCC and import from creator companion.)  
• VideoTXL (From https://github.com/vrctxl/VideoTXL, add the VCC and import from creator companion.)  
• OhGeezCmon Access Control (for adding users to the whitelist at runtime, install the package from https://github.com/OhGeezCmon/VRC-AccessControl)  
• Flatine Open Decks Manager (https://lavysworlds.gumroad.com/l/flatline) for whitelist integration and event hosting.

## **SUPPORT**
For support message "cozen." on Discord or join the [Enigma Discord](https://discord.gg/fvCdcpFedP).

## **SPECIAL THANKS**
Special thanks to the many world creators that beta tested this asset for me over the course of a year, especially:
- Bean from District One (https://discord.gg/DQw3r9VJjZ)
- Biochemicals from Club Chemistry (https://discord.gg/DQw3r9VJjZ)
- TootyFrooty and Rondogbot from Psychosis (https://discord.gg/DQw3r9VJjZ)  
whose feedback and suggestions made this all this possible.

Also special thanks to Zoey/Luka, developer of June Shaders, for her detailed help that made the optimized June Folder integration possible.
