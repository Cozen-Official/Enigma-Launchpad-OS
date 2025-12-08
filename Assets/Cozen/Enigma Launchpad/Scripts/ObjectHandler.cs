using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Cozen
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ObjectHandler : UdonSharpBehaviour
    {
        // Per-handler state (replaces global objectStates and syncedObjectPages)
        [UdonSynced] private bool[] entryStates;
        [UdonSynced] private int currentPage;
        
        private bool[] initialEntryStates;
        
        [Tooltip("Folder index used to map page changes and selections.")]
        public int folderIndex;
        
        [Tooltip("Parent launchpad that owns folder selection and UI updates.")]
        public EnigmaLaunchpad launchpad;
        
        [Tooltip("Entries for THIS folder only (GameObjects or Materials).")]
        public UnityEngine.Object[] folderEntries;
        
        
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
            Debug.Log($"[ObjectHandler] Start() called on {gameObject.name}");
            Debug.Log($"[ObjectHandler] folderEntries is {(folderEntries != null ? $"NOT NULL with {folderEntries.Length} entries" : "NULL")}");
            Debug.Log($"[ObjectHandler] launchpad reference is {(launchpad != null ? "NOT NULL" : "NULL")}");
            
            if (launchpad == null)
            {
                Debug.Log("[ObjectHandler] launchpad is null, calling Awake() to find it");
                Awake();
                Debug.Log($"[ObjectHandler] After Awake(), launchpad is {(launchpad != null ? "NOT NULL" : "still NULL")}");
            }
        }
        
        public void SetLaunchpad(EnigmaLaunchpad pad)
        {
            Debug.Log($"[ObjectHandler] SetLaunchpad called on {gameObject.name}, pad is {(pad != null ? "NOT NULL" : "NULL")}");
            Debug.Log($"[ObjectHandler] folderEntries at SetLaunchpad time: {(folderEntries != null ? $"NOT NULL with {folderEntries.Length} entries" : "NULL")}");
            launchpad = pad;
        }
        
        public void InitializeObjectRuntime()
        {
            Debug.Log($"[ObjectHandler] InitializeObjectRuntime called on {gameObject.name}");
            Debug.Log($"[ObjectHandler] folderEntries: {(folderEntries != null ? $"NOT NULL with {folderEntries.Length} entries" : "NULL")}");
            
            if (launchpad == null)
            {
                Debug.LogWarning("[ObjectHandler] InitializeObjectRuntime skipped - launchpad is null");
                return;
            }
            
            InitializeHandler();
            CaptureInitialState();
            Debug.Log($"[ObjectHandler] InitializeObjectRuntime completed, entryStates has {(entryStates != null ? entryStates.Length : 0)} entries");
        }
        
        private void InitializeHandler()
        {
            Debug.Log($"[ObjectHandler] InitializeHandler called, folderEntries is {(folderEntries != null ? $"NOT NULL with {folderEntries.Length} entries" : "NULL")}");
            
            if (folderEntries == null)
            {
                Debug.LogWarning("[ObjectHandler] folderEntries is NULL - this indicates a serialization issue. Creating empty array.");
                folderEntries = new UnityEngine.Object[0];
            }
            else if (folderEntries.Length == 0)
            {
                Debug.LogWarning("[ObjectHandler] folderEntries is EMPTY - no objects to toggle. Check if entries were lost during build.");
            }
            
            int count = folderEntries.Length;
            if (entryStates == null || entryStates.Length != count)
            {
                entryStates = new bool[count];
                
                // Initialize states based on current GameObject active state
                if (launchpad != null && launchpad.GetFolderTypeForIndex(folderIndex) == ToggleFolderType.Objects)
                {
                    for (int i = 0; i < count; i++)
                    {
                        GameObject go = GetGameObjectAtLocalIndex(i);
                        entryStates[i] = (go != null && go.activeSelf);
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
            if (launchpad == null || folderEntries == null || entryStates == null)
            {
                return;
            }
            
            ToggleFolderType folderType = launchpad.GetFolderTypeForIndex(folderIndex);
            if (folderType != ToggleFolderType.Objects)
            {
                return;
            }
            
            for (int i = 0; i < folderEntries.Length; i++)
            {
                if (i >= entryStates.Length)
                {
                    break;
                }
                
                bool desired = entryStates[i];
                ApplyObjectActivation(i, desired);
            }
        }
        
        private GameObject GetGameObjectAtLocalIndex(int localIndex)
        {
            if (folderEntries == null || localIndex < 0 || localIndex >= folderEntries.Length)
            {
                return null;
            }
            
            UnityEngine.Object entry = folderEntries[localIndex];
            if (entry == null)
            {
                return null;
            }
            
            return entry.GetType() == typeof(GameObject) ? (GameObject)entry : null;
        }
        
        private Material GetMaterialAtLocalIndex(int localIndex)
        {
            if (folderEntries == null || localIndex < 0 || localIndex >= folderEntries.Length)
            {
                return null;
            }
            
            UnityEngine.Object entry = folderEntries[localIndex];
            if (entry == null)
            {
                return null;
            }
            
            return entry.GetType() == typeof(Material) ? (Material)entry : null;
        }
        
        private void ApplyObjectActivation(int localIndex, bool desiredState)
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
            folderEntries != null &&
            folderEntries.Length > 0;
        }
        
        private string GetButtonLabel(int buttonIndex)
        {
            if (launchpad == null)
            {
                return string.Empty;
            }
            
            if (launchpad.GetFolderTypeForIndex(folderIndex) != ToggleFolderType.Objects)
            {
                return string.Empty;
            }
            
            if (!IsHandlerConfigured())
            {
                return string.Empty;
            }
            
            int localIndex = currentPage * launchpad.GetItemsPerPage() + buttonIndex;
            if (localIndex < 0 || localIndex >= folderEntries.Length)
            {
                return string.Empty;
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
            
            if (launchpad.GetFolderTypeForIndex(folderIndex) != ToggleFolderType.Objects)
            {
                return false;
            }
            
            if (folderEntries == null || folderEntries.Length <= 0)
            {
                return false;
            }
            
            int localIndex = currentPage * launchpad.GetItemsPerPage() + buttonIndex;
            if (localIndex < 0 || localIndex >= folderEntries.Length)
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
            if (launchpad == null || folderEntries == null)
            {
                return 1;
            }
            
            int count = folderEntries.Length;
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
            
            if (launchpad.GetFolderTypeForIndex(folderIndex) != ToggleFolderType.Objects)
            {
                return false;
            }
            
            if (!IsHandlerConfigured())
            {
                return false;
            }
            
            int localIndex = currentPage * launchpad.GetItemsPerPage() + buttonIndex;
            if (localIndex < 0 || localIndex >= folderEntries.Length)
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
                        ApplyObjectActivation(i, false);
                    }
                }
            }
            
            entryStates[localIndex] = newState;
            ApplyObjectActivation(localIndex, newState);
            return true;
        }
        
        public void OnPageChange(int direction)
        {
            if (launchpad == null)
            {
                return;
            }
            
            if (launchpad.GetFolderTypeForIndex(folderIndex) != ToggleFolderType.Objects)
            {
                return;
            }

            EnsureLocalOwnership();
            UpdatePage(direction);
        }
        
        private void UpdatePage(int direction)
        {
            if (folderEntries == null)
            {
                return;
            }

            int count = folderEntries.Length;
            int totalPages = Mathf.Max(1, Mathf.CeilToInt((float)count / launchpad.GetItemsPerPage()));
            currentPage = (currentPage + direction + totalPages) % totalPages;

            RequestSerialization();
        }

        private void EnsureLocalOwnership()
        {
            if (!Networking.IsOwner(gameObject))
            {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            }
        }

        // Compatibility methods for editor and migration
        public int GetEntryCount()
        {
            return folderEntries != null ? folderEntries.Length : 0;
        }
        
        /// <summary>
        /// Gets the activation state of a specific entry by local index.
        /// Used by FaderSystemHandler to check dynamic fader conditions.
        /// </summary>
        public bool GetEntryState(int localIndex)
        {
            if (entryStates == null || localIndex < 0 || localIndex >= entryStates.Length)
            {
                return false;
            }
            return entryStates[localIndex];
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
        }
        
        /// <summary>
        /// Applies all entry states to their corresponding GameObjects.
        /// Used by PresetHandler after applying preset snapshots.
        /// </summary>
        public void ApplyStates()
        {
            if (entryStates == null)
            {
                return;
            }
            
            for (int i = 0; i < entryStates.Length; i++)
            {
                ApplyObjectActivation(i, entryStates[i]);
            }
        }
        
        public GameObject GetGameObjectEntry(int index)
        {
            return GetGameObjectAtLocalIndex(index);
        }
        
        public Material GetMaterialEntry(int index)
        {
            return GetMaterialAtLocalIndex(index);
        }
        
        // Static helper methods for UdonSharp compatibility
        public static int GetFolderIndex(ObjectHandler handler)
        {
            return handler != null ? handler.folderIndex : -1;
        }
        
        public static UnityEngine.Object[] GetFolderEntries(ObjectHandler handler)
        {
            return handler != null ? handler.folderEntries : null;
        }
    }
}
