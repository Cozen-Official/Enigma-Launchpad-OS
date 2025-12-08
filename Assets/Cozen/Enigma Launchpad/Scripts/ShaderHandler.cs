using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Cozen
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ShaderHandler : UdonSharpBehaviour
    {
        // Per-handler state (replaces global states)
        [UdonSynced] private bool[] entryStates;
        [UdonSynced] private int currentPage;
        
        private bool[] initialEntryStates;
        
        [Tooltip("Folder index used to map page changes and selections.")]
        public int folderIndex;
        
        [Tooltip("Parent launchpad that owns folder selection and UI updates.")]
        public EnigmaLaunchpad launchpad;
        
        [Tooltip("Shader GameObjects managed by this handler (created in editor).")]
        public GameObject[] shaderGameObjects;
        
        [Tooltip("Names corresponding to each shader entry.")]
        public string[] shaderNames;
        
        [Header("Editor Configuration")]
        [Tooltip("Template GameObject with MeshRenderer. Used in editor to create shader instances.")]
        public GameObject templateGameObject;
        
        [Tooltip("Materials with screen shaders (configured in editor).")]
        public Material[] shaderMaterials;
        
        [Tooltip("Index of shader to enable by default (-1 for none).")]
        public int defaultShaderIndex = -1;
        
        
        public void Awake()
        {
            if (launchpad == null)
            {
                launchpad = GetComponent<EnigmaLaunchpad>();
                if (launchpad == null)
                {
                    launchpad = GetComponentInParent<EnigmaLaunchpad>();
                }
            }
        }
        
        public void Start()
        {
            if (launchpad == null)
            {
                Awake();
            }
        }
        
        public void SetLaunchpad(EnigmaLaunchpad pad)
        {
            launchpad = pad;
        }
        
        public void InitializeShaderRuntime()
        {
            if (launchpad == null)
            {
                return;
            }
            
            InitializeHandler();
            CaptureInitialState();
        }
        
        private void InitializeHandler()
        {
            if (shaderGameObjects == null)
            {
                shaderGameObjects = new GameObject[0];
            }
            
            int count = shaderGameObjects.Length;
            if (entryStates == null || entryStates.Length != count)
            {
                entryStates = new bool[count];
                
                // Initialize states based on defaultShaderIndex
                if (launchpad != null && launchpad.GetFolderTypeForIndex(folderIndex) == ToggleFolderType.Shaders)
                {
                    for (int i = 0; i < count; i++)
                    {
                        // Only the default shader (if valid) should be enabled at start
                        entryStates[i] = (i == defaultShaderIndex && defaultShaderIndex >= 0 && defaultShaderIndex < count);
                        ApplyShaderActivation(i, entryStates[i]);
                    }
                }
            }
        }
        
        private void CaptureInitialState()
        {
            if (entryStates != null)
            {
                initialEntryStates = new bool[entryStates.Length];
                for (int i = 0; i < entryStates.Length; i++)
                {
                    initialEntryStates[i] = entryStates[i];
                }
            }
        }
        
        public void RestoreInitialState()
        {
            if (initialEntryStates != null && entryStates != null && initialEntryStates.Length == entryStates.Length)
            {
                for (int i = 0; i < entryStates.Length; i++)
                {
                    entryStates[i] = initialEntryStates[i];
                }
            }
            
            currentPage = 0;
            RestoreLocalState();
        }
        
        public void OnLaunchpadDeserialized()
        {
            RestoreLocalState();
        }
        
        public override void OnDeserialization()
        {
            base.OnDeserialization();
            
            RestoreLocalState();
            
            if (launchpad != null)
            {
                launchpad.RequestDisplayUpdateFromHandler();
            }
        }
        
        public void RestoreLocalState()
        {
            if (launchpad == null || shaderGameObjects == null || entryStates == null)
            {
                return;
            }
            
            ToggleFolderType folderType = launchpad.GetFolderTypeForIndex(folderIndex);
            if (folderType != ToggleFolderType.Shaders)
            {
                return;
            }
            
            for (int i = 0; i < shaderGameObjects.Length; i++)
            {
                if (i >= entryStates.Length)
                {
                    break;
                }
                
                bool desired = entryStates[i];
                ApplyShaderActivation(i, desired);
            }
        }
        
        private GameObject GetGameObjectAtLocalIndex(int localIndex)
        {
            if (shaderGameObjects == null || localIndex < 0 || localIndex >= shaderGameObjects.Length)
            {
                return null;
            }
            
            return shaderGameObjects[localIndex];
        }
        
        private void ApplyShaderActivation(int localIndex, bool desiredState)
        {
            GameObject target = GetGameObjectAtLocalIndex(localIndex);
            if (target == null)
            {
                return;
            }
            
            if (target.activeSelf != desiredState)
            {
                target.SetActive(desiredState);
            }
        }
        
        // Handler Interface Methods (called by EnigmaLaunchpad)
        
        public string GetLabel(int buttonIndex)
        {
            if (buttonIndex == 10)
            {
                return launchpad != null ? launchpad.GetFolderLabelForIndex(folderIndex, false) : string.Empty;
            }
            
            if (!IsHandlerConfigured())
            {
                return buttonIndex == 9 ? "0/0" : string.Empty;
            }
            
            if (buttonIndex == 9)
            {
                return GetPageLabel();
            }
            
            return GetButtonLabel(buttonIndex);
        }
        
        public bool IsInteractable(int buttonIndex)
        {
            bool configured = IsHandlerConfigured();
            
            if (buttonIndex == 10)
            {
                return configured;
            }
            
            if (buttonIndex == 9)
            {
                return configured && GetTotalPages() > 1;
            }
            
            if (!configured)
            {
                return false;
            }
            
            return !string.IsNullOrEmpty(GetButtonLabel(buttonIndex));
        }
        
        public bool IsActive(int buttonIndex)
        {
            if (buttonIndex >= 9)
            {
                return IsHandlerConfigured();
            }
            
            return TryGetEntryState(buttonIndex, out bool state) && state;
        }
        
        private bool IsHandlerConfigured()
        {
            return launchpad != null && 
                   shaderGameObjects != null &&
                   shaderGameObjects.Length > 0;
        }
        
        private string GetButtonLabel(int buttonIndex)
        {
            if (launchpad == null)
            {
                return string.Empty;
            }
            
            if (launchpad.GetFolderTypeForIndex(folderIndex) != ToggleFolderType.Shaders)
            {
                return string.Empty;
            }
            
            if (!IsHandlerConfigured())
            {
                return string.Empty;
            }
            
            int localIndex = currentPage * launchpad.GetItemsPerPage() + buttonIndex;
            if (localIndex < 0 || localIndex >= shaderGameObjects.Length)
            {
                return string.Empty;
            }
            
            // Use shaderNames array if available, otherwise fall back to GameObject name
            if (shaderNames != null && localIndex < shaderNames.Length && !string.IsNullOrEmpty(shaderNames[localIndex]))
            {
                return shaderNames[localIndex];
            }
            
            GameObject go = GetGameObjectAtLocalIndex(localIndex);
            return go != null ? go.name : string.Empty;
        }
        
        private bool TryGetEntryState(int buttonIndex, out bool state)
        {
            state = false;
            if (launchpad == null)
            {
                return false;
            }
            
            if (launchpad.GetFolderTypeForIndex(folderIndex) != ToggleFolderType.Shaders)
            {
                return false;
            }
            
            if (shaderGameObjects == null || shaderGameObjects.Length <= 0)
            {
                return false;
            }
            
            int localIndex = currentPage * launchpad.GetItemsPerPage() + buttonIndex;
            if (localIndex < 0 || localIndex >= shaderGameObjects.Length)
            {
                return false;
            }
            
            if (entryStates == null || localIndex >= entryStates.Length)
            {
                return false;
            }
            
            state = entryStates[localIndex];
            return true;
        }
        
        private string GetPageLabel()
        {
            if (!IsHandlerConfigured())
            {
                return "0/0";
            }
            
            int totalPages = GetTotalPages();
            int displayPage = Mathf.Clamp(currentPage, 0, Mathf.Max(0, totalPages - 1));
            return $"{displayPage + 1}/{Mathf.Max(1, totalPages)}";
        }
        
        private int GetTotalPages()
        {
            if (launchpad == null || shaderGameObjects == null)
            {
                return 1;
            }
            
            int count = shaderGameObjects.Length;
            return Mathf.Max(1, Mathf.CeilToInt((float)count / launchpad.GetItemsPerPage()));
        }
        
        public void OnSelect(int buttonIndex)
        {
            if (!ToggleLocalEntry(buttonIndex))
            {
                return;
            }

            RequestSerialization();
        }
        
        private bool ToggleLocalEntry(int buttonIndex)
        {
            if (launchpad == null)
            {
                return false;
            }
            
            if (launchpad.GetFolderTypeForIndex(folderIndex) != ToggleFolderType.Shaders)
            {
                return false;
            }
            
            if (!IsHandlerConfigured())
            {
                return false;
            }
            
            int localIndex = currentPage * launchpad.GetItemsPerPage() + buttonIndex;
            if (localIndex < 0 || localIndex >= shaderGameObjects.Length)
            {
                return false;
            }
            
            if (entryStates == null || localIndex >= entryStates.Length)
            {
                return false;
            }
            
            EnsureLocalOwnership();
            
            bool newState = !entryStates[localIndex];
            
            // Handle exclusive folder logic
            bool isExclusive = launchpad.IsFolderExclusive(folderIndex);
            if (isExclusive && newState)
            {
                // Turn off all other entries in this folder
                for (int i = 0; i < entryStates.Length; i++)
                {
                    if (i == localIndex)
                    {
                        continue;
                    }
                    
                    if (entryStates[i])
                    {
                        entryStates[i] = false;
                        ApplyShaderActivation(i, false);
                    }
                }
            }
            
            entryStates[localIndex] = newState;
            ApplyShaderActivation(localIndex, newState);
            return true;
        }
        
        public void OnPageChange(int direction)
        {
            if (launchpad == null)
            {
                return;
            }
            
            if (launchpad.GetFolderTypeForIndex(folderIndex) != ToggleFolderType.Shaders)
            {
                return;
            }

            EnsureLocalOwnership();
            UpdatePage(direction);
        }
        
        private void UpdatePage(int direction)
        {
            if (shaderGameObjects == null)
            {
                return;
            }
            
            int totalPages = GetTotalPages();
            if (totalPages <= 1)
            {
                return;
            }
            
            currentPage = (currentPage + direction + totalPages) % totalPages;
        }
        
        private void EnsureLocalOwnership()
        {
            if (launchpad != null)
            {
                launchpad.EnsureLocalOwnership();
            }
        }
        
        public bool GetEntryState(int localIndex)
        {
            if (entryStates == null || localIndex < 0 || localIndex >= entryStates.Length)
            {
                return false;
            }
            return entryStates[localIndex];
        }
        
        public int GetEntryCount()
        {
            if (shaderGameObjects == null)
            {
                return 0;
            }
            return shaderGameObjects.Length;
        }
        
        /// <summary>
        /// Sets the activation state of a specific entry by local index.
        /// Used by PresetHandler to apply preset snapshots.
        /// </summary>
        public void SetEntryState(int localIndex, bool state)
        {
            if (entryStates == null || localIndex < 0 || localIndex >= entryStates.Length)
            {
                return;
            }
            
            entryStates[localIndex] = state;
            ApplyShaderActivation(localIndex, state);
        }
    }
}
