using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Cozen
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ScreenHandler : UdonSharpBehaviour
    {
        [Tooltip("Reference to the master Enigma Launchpad script.")]
        public EnigmaLaunchpad launchpad;
        
        [Tooltip("Array of screen GameObjects to toggle.")]
        public GameObject[] screens;
        
        [HideInInspector]
        public int defaultScreenIndex = 0;
        
        [UdonSynced] private int activeScreenIndex = -1;
        
        private int initialDefaultScreenIndex;
        
        public void Start()
        {
            if (launchpad == null)
            {
                launchpad = GetComponent<EnigmaLaunchpad>();
                if (launchpad == null)
                {
                    launchpad = GetComponentInParent<EnigmaLaunchpad>();
                }
            }
            
            initialDefaultScreenIndex = defaultScreenIndex;
            
            // Enable the default screen on start
            // Only initialize on master client to avoid conflicts with network sync
            if (screens != null)
            {
                if (Networking.IsMaster)
                {
                    // defaultScreenIndex == -1 means "AudioLink" mode (disable all screens)
                    // Named "AudioLink" per user requirement to represent the no-screen state
                    bool isAudioLinkMode = defaultScreenIndex == -1;
                    bool isValidScreenIndex = defaultScreenIndex >= 0 && defaultScreenIndex < screens.Length;
                    
                    if (isAudioLinkMode || isValidScreenIndex)
                    {
                        activeScreenIndex = defaultScreenIndex;
                        ApplyScreenState();
                    }
                }
                // Non-master clients will receive the correct state via OnDeserialization
            }
        }
        
        public void SetLaunchpad(EnigmaLaunchpad pad)
        {
            launchpad = pad;
        }
        
        /// <summary>
        /// Toggle to the specified screen by index, disabling all others.
        /// </summary>
        public void ToggleScreen(int index)
        {
            if (screens == null || index < 0 || index >= screens.Length)
            {
                Debug.LogWarning($"[ScreenHandler] Invalid screen index: {index}");
                return;
            }

            SetActiveScreenIndex(index);
        }

        public void DisableAllScreens()
        {
            if (screens == null || screens.Length == 0)
            {
                Debug.LogWarning("[ScreenHandler] No screens available to disable.");
                return;
            }

            SetActiveScreenIndex(-1);
        }
        
        private void ApplyScreenState()
        {
            if (screens == null)
            {
                return;
            }

            for (int i = 0; i < screens.Length; i++)
            {
                if (screens[i] != null)
                {
                    screens[i].SetActive(i == activeScreenIndex);
                }
            }
        }

        private void SetActiveScreenIndex(int index)
        {
            // Take ownership for syncing
            if (!Networking.IsOwner(gameObject))
            {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            }

            activeScreenIndex = index;
            ApplyScreenState();

            RequestSerialization();
        }
        
        public override void OnDeserialization()
        {
            base.OnDeserialization();
            ApplyScreenState();
        }
        
        public void RestoreInitialState()
        {
            // Take ownership for syncing
            if (!Networking.IsOwner(gameObject))
            {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            }
            
            activeScreenIndex = initialDefaultScreenIndex;
            ApplyScreenState();
            
            RequestSerialization();
        }
        
        public int GetDefaultScreenIndex()
        {
            return defaultScreenIndex;
        }
        
        public void SetDefaultScreenIndex(int index)
        {
            defaultScreenIndex = index;
        }
        
        public string[] GetScreenNames()
        {
            // Always return "AudioLink" as first option, even with no screens.
            // "AudioLink" represents the "disable all screens" state, which is valid
            // regardless of whether screens exist. Named per user requirement.
            if (screens == null || screens.Length == 0)
            {
                return new string[] { "AudioLink" };
            }
            
            // Prepend "AudioLink" to the screen names
            string[] names = new string[screens.Length + 1];
            names[0] = "AudioLink";
            for (int i = 0; i < screens.Length; i++)
            {
                names[i + 1] = screens[i] != null ? screens[i].name : $"Screen {i}";
            }
            return names;
        }
    }
}
