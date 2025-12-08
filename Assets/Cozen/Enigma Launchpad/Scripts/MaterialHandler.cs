using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Cozen
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class MaterialHandler : UdonSharpBehaviour
    {
        [Header("Materials")]
        [Tooltip("Parent launchpad that owns folder selection and UI updates.")]
        public EnigmaLaunchpad launchpad;
        
        [Tooltip("Folder index used to map page changes and selections.")]
        public int folderIndex;
        
        [Tooltip("Materials for THIS folder only (per-handler architecture).")]
        public Material[] folderEntries;
        
        // Per-handler state (replaces global objectStates)
        [UdonSynced] private bool[] entryStates;
        [UdonSynced] private int currentPage;
        
        [Tooltip("Renderers controlled by this folder's material toggles.")]
        public Renderer[] folderMaterialRenderers;
        
        [Header("Material Routing")]
        [Tooltip("Renderers controlled by material toggles.")]
        public Renderer[] materialRenderers;
        
        [Tooltip("Default materials for each renderer slot.")]
        public Material[][] materialRendererDefaults;
        
        [Tooltip("Current material index per renderer.")]
        public int[] materialRendererIndices;
        
        [Tooltip("Slot counts per renderer for toggle support.")]
        public int[] materialRendererToggleSlotCounts;
        
        [Tooltip("Mapping from local entry to pair index.")]
        public int[] materialEntryPairIndices;
        
        [Tooltip("Renderers referenced by material pairs.")]
        public Renderer[] materialPairRenderers;
        
        [Tooltip("Materials referenced by material pairs.")]
        public Material[] materialPairMaterials;
        
        [Tooltip("Owning folder index for each material pair.")]
        public int[] materialPairOwnerFolders;
        
        [Tooltip("Owning global index for each material pair.")]
        public int[] materialPairOwnerGlobalIndices;
        
        [Tooltip("Tracks whether a material pair has duplicate references.")]
        public bool[] materialPairHasDuplicates;

        [Tooltip("Fallback materials captured from the assigned renderers during initialization.")]
        private Renderer[] fallbackInitialRenderers;

        private Material[][] fallbackInitialRendererMaterials;

        private Renderer[] singleRendererBuffer;

        private Renderer[] emptyRendererArray;
        
        /// <summary>
        /// Get the number of entries for this handler's folder.
        /// </summary>
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
        /// Applies the current material selection to all renderers.
        /// Used by PresetHandler after applying preset snapshots.
        /// </summary>
        public void ApplyMaterial()
        {
            ApplyMaterialRendererStates();
        }
        
        
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
            Debug.Log($"[MaterialHandler] SetLaunchpad called, pad is {(pad != null ? "NOT NULL" : "NULL")}");
            launchpad = pad;
        }
        
        public override void OnDeserialization()
        {
            base.OnDeserialization();
            
            // Apply synced states to folder renderers
            Renderer[] renderers = GetRenderersForFolder();
            if (renderers != null && renderers.Length > 0)
            {
                ApplyRenderers(renderers);
            }
            
            if (launchpad != null)
            {
                launchpad.RequestDisplayUpdateFromHandler();
            }
        }
        
        public bool IsReady()
        {
            return launchpad != null;
        }
        
        private bool IsHandlerConfigured()
        {
            return launchpad != null && 
            folderEntries != null &&
            folderEntries.Length > 0;
        }
        
        private Renderer[] GetRenderersForFolder()
        {
            if (folderMaterialRenderers != null && folderMaterialRenderers.Length > 0)
            {
                return folderMaterialRenderers;
            }

            if (materialRenderers == null)
            {
                return GetEmptyRendererArray();
            }

            int rendererIndex = (materialRendererIndices != null && folderIndex >= 0 && folderIndex < materialRendererIndices.Length)
            ? materialRendererIndices[folderIndex]
            : -1;

            if (rendererIndex < 0 || rendererIndex >= materialRenderers.Length)
            {
                return GetEmptyRendererArray();
            }

            return WrapRenderer(materialRenderers[rendererIndex]);
        }
        
        public void InitializeMaterialRuntime()
        {
            Debug.Log($"[MaterialHandler] InitializeMaterialRuntime called on {gameObject.name}");

            if (launchpad == null)
            {
                Debug.LogWarning("[MaterialHandler] InitializeMaterialRuntime skipped - launchpad is null");
                return;
            }

            InitializeEntryStates();
        }

        private void InitializeEntryStates()
        {
            if (folderEntries == null)
            {
                Debug.LogWarning("[MaterialHandler] folderEntries is NULL - creating empty array");
                folderEntries = new Material[0];
            }

            int count = folderEntries.Length;
            if (entryStates == null || entryStates.Length != count)
            {
                entryStates = new bool[count];
            }

            Renderer[] renderers = GetRenderersForFolder();
            CacheFallbackMaterials(renderers);

            for (int i = 0; i < count; i++)
            {
                Material target = folderEntries[i];
                entryStates[i] = IsMaterialAppliedToAllRenderers(renderers, target);
            }

            // Check if any toggle is active
            bool hasAnyActiveToggle = false;
            for (int i = 0; i < count; i++)
            {
                if (entryStates[i])
                {
                    hasAnyActiveToggle = true;
                    break;
                }
            }

            // Disable renderers that have no materials and no active toggles
            if (renderers != null)
            {
                for (int r = 0; r < renderers.Length; r++)
                {
                    Renderer renderer = renderers[r];
                    if (renderer == null)
                    {
                        continue;
                    }

                    Material[] currentMaterials = renderer.sharedMaterials;
                    bool hasNoMaterials = currentMaterials == null || currentMaterials.Length == 0 || AreAllMaterialsNull(currentMaterials);

                    if (hasNoMaterials && !hasAnyActiveToggle)
                    {
                        renderer.enabled = false;
                    }
                }
            }
        }

        private bool AreAllMaterialsNull(Material[] materials)
        {
            if (materials == null)
            {
                return true;
            }

            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] != null)
                {
                    return false;
                }
            }

            return true;
        }

        private bool MaterialIsApplied(Material[] rendererMaterials, Material target)
        {
            if (rendererMaterials == null || target == null)
            {
                return false;
            }

            for (int i = 0; i < rendererMaterials.Length; i++)
            {
                if (rendererMaterials[i] == target)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsMaterialAppliedToAllRenderers(Renderer[] renderers, Material target)
        {
            if (renderers == null || renderers.Length == 0)
            {
                return false;
            }

            for (int r = 0; r < renderers.Length; r++)
            {
                Renderer renderer = renderers[r];
                Material[] rendererMaterials = renderer != null ? renderer.sharedMaterials : null;
                if (rendererMaterials != null)
                {
                    CacheFallbackMaterials(renderer, rendererMaterials);
                }

                if (!MaterialIsApplied(rendererMaterials, target))
                {
                    return false;
                }
            }

            return true;
        }
        
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
                return configured && GetPageCount() > 1;
            }
            
            if (!configured)
            {
                return false;
            }
            
            return !string.IsNullOrEmpty(GetButtonLabel(buttonIndex));
        }
        
        public bool IsActive(int buttonIndex)
        {
            if (!IsHandlerConfigured())
            {
                return false;
            }
            
            if (buttonIndex >= 9)
            {
                return true;
            }
            
            return TryGetEntryState(buttonIndex, out bool state) && state;
        }
        
        public string GetButtonLabel(int buttonIndex)
        {
            if (launchpad == null)
            {
                return string.Empty;
            }
            
            if (launchpad.GetFolderTypeForIndex(folderIndex) != ToggleFolderType.Materials)
            {
                return string.Empty;
            }
            
            int count = GetEntryCount();
            if (count <= 0)
            {
                return string.Empty;
            }
            
            int localIndex = currentPage * launchpad.GetItemsPerPage() + buttonIndex;
            
            if (localIndex < 0 || localIndex >= count)
            {
                return string.Empty;
            }
            
            if (folderEntries == null || localIndex >= folderEntries.Length)
            {
                return string.Empty;
            }
            
            Material mat = folderEntries[localIndex];
            if (mat == null)
            {
                return string.Empty;
            }
            
            return mat.name;
        }
        
        private bool TryGetEntryState(int buttonIndex, out bool state)
        {
            state = false;
            if (launchpad == null)
            {
                return false;
            }
            
            if (launchpad.GetFolderTypeForIndex(folderIndex) != ToggleFolderType.Materials)
            {
                return false;
            }
            
            int count = GetEntryCount();
            if (count <= 0)
            {
                return false;
            }
            
            int localIndex = currentPage * launchpad.GetItemsPerPage() + buttonIndex;
            
            if (localIndex < 0 || localIndex >= count)
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
        
        public string GetPageLabel()
        {
            if (launchpad == null)
            {
                return "0/0";
            }
            
            if (launchpad.GetFolderTypeForIndex(folderIndex) != ToggleFolderType.Materials)
            {
                return "0/0";
            }
            
            int totalPages = GetTotalPages();
            int clampedPage = Mathf.Clamp(currentPage, 0, Mathf.Max(0, totalPages - 1));
            if (currentPage != clampedPage)
            {
                currentPage = clampedPage;
            }
            
            return $"{currentPage + 1}/{Mathf.Max(1, totalPages)}";
        }
        
        public int GetPageCount()
        {
            return Mathf.Max(1, GetTotalPages());
        }
        
        public void OnSelect(int buttonIndex)
        {
            if (launchpad == null)
            {
                return;
            }
            
            int folderIdx = folderIndex;
            if (launchpad.GetFolderTypeForIndex(folderIdx) != ToggleFolderType.Materials)
            {
                return;
            }
            
            int count = GetEntryCount();
            if (count <= 0)
            {
                return;
            }
            
            int localIndex = currentPage * launchpad.GetItemsPerPage() + buttonIndex;
            
            if (localIndex < 0 || localIndex >= count)
            {
                return;
            }
            
            if (entryStates == null || localIndex >= entryStates.Length)
            {
                return;
            }
            
            EnsureLocalOwnership();

            bool stateChanged = ToggleMaterialEntry(localIndex);
            if (!stateChanged)
            {
                return;
            }

            RequestSerialization();
            // UpdateDisplay is called by EnigmaLaunchpad.HandleItemSelect after OnSelect returns
        }
        
        public void OnPageChange(int direction)
        {
            if (launchpad == null)
            {
                return;
            }
            
            if (launchpad.GetFolderTypeForIndex(folderIndex) != ToggleFolderType.Materials)
            {
                return;
            }

            EnsureLocalOwnership();

            UpdateObjectPage(direction);

            RequestSerialization();
        }
        
        private void UpdateObjectPage(int direction)
        {
            int totalPages = Mathf.Max(1, GetTotalPages());
            int current = Mathf.Clamp(currentPage, 0, totalPages - 1);
            current = (current + direction + totalPages) % totalPages;
            currentPage = current;
        }
        
        private int GetTotalPages()
        {
            if (launchpad == null)
            {
                return 1;
            }
            
            int count = GetEntryCount();
            return Mathf.Max(1, Mathf.CeilToInt((float)count / launchpad.GetItemsPerPage()));
        }

        private void EnsureLocalOwnership()
        {
            if (!Networking.IsOwner(gameObject))
            {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            }
        }
        
        public void ApplyMaterialRendererStates()
        {
        }

        public void RestoreInitialState()
        {
            if (!IsHandlerConfigured())
            {
                return;
            }

            EnsureLocalOwnership();

            Renderer[] renderers = GetRenderersForFolder();
            if (renderers != null)
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer renderer = renderers[i];
                    if (renderer == null)
                    {
                        continue;
                    }

                    Material[] fallback = GetFallbackMaterialsForRenderer(renderer);
                    if (fallback != null)
                    {
                        renderer.sharedMaterials = fallback;
                    }
                }
            }

            InitializeEntryStates();
            currentPage = 0;
            RequestSerialization();
        }

        public void ApplyAllRendererStates()
        {
            if (materialRenderers == null)
            {
                return;
            }
            
            for (int r = 0; r < materialRenderers.Length; r++)
            {
                ApplyRendererForIndex(r);
            }
        }
        
        public void ApplyRendererStateForLocalEntry(int localIndex)
        {
            if (launchpad == null)
            {
                return;
            }
            
            int count = GetEntryCount();
            if (count <= 0)
            {
                return;
            }
            
            if (localIndex < 0 || localIndex >= count)
            {
                return;
            }

            Renderer[] renderers = GetRenderersForFolder();
            if (renderers == null || renderers.Length == 0)
            {
                return;
            }

            ApplyRenderers(renderers);
        }
        
        public bool ToggleMaterialEntry(int localIndex)
        {
            if (launchpad == null)
            {
                return false;
            }
            
            int folderIdx = folderIndex;
            int count = GetEntryCount();
            if (count <= 0)
            {
                return false;
            }
            
            if (localIndex < 0 || localIndex >= count)
            {
                return false;
            }
            
            if (entryStates == null || localIndex >= entryStates.Length)
            {
                return false;
            }
            
            Renderer[] renderers = GetRenderersForFolder();
            if (renderers == null || renderers.Length == 0)
            {
                return false;
            }
            
            if (folderEntries == null || localIndex >= folderEntries.Length)
            {
                return false;
            }
            
            Material targetMaterial = folderEntries[localIndex];
            if (targetMaterial == null)
            {
                return false;
            }
            
            bool previousState = entryStates[localIndex];
            bool newState = !previousState;
            
            bool isExclusiveFolder = launchpad.IsFolderExclusive(folderIdx);
            
            int pairIndex = (materialEntryPairIndices != null && localIndex < materialEntryPairIndices.Length)
            ? materialEntryPairIndices[localIndex]
            : -1;
            int ownerGlobalIndex = (materialPairOwnerGlobalIndices != null && pairIndex >= 0 && pairIndex < materialPairOwnerGlobalIndices.Length)
            ? materialPairOwnerGlobalIndices[pairIndex]
            : -1;
            bool isOwner = (pairIndex >= 0 && ownerGlobalIndex == localIndex);
            
            if (isExclusiveFolder && newState)
            {
                for (int i = 0; i < count; i++)
                {
                    if (i == localIndex || !entryStates[i]) continue;
                    entryStates[i] = false;
                }
            }
            
            bool duplicatesCleared = false;
            if (newState && pairIndex >= 0)
            {
                if (!isOwner)
                {
                    return false;
                }
                duplicatesCleared = ClearMaterialPairDuplicates(pairIndex, localIndex);
            }
            
            entryStates[localIndex] = newState;
            ApplyRendererStateForLocalEntry(localIndex);
            return (previousState != newState) || duplicatesCleared;
        }
        
        public void EnforceOwnership()
        {
            if (launchpad == null || entryStates == null)
            {
                return;
            }
            
            int[] ownerGlobals = materialPairOwnerGlobalIndices;
            int[] entryPairs = materialEntryPairIndices;
            int count = GetEntryCount();
            if (ownerGlobals == null || entryPairs == null || count <= 0)
            {
                return;
            }
            
            int limit = Mathf.Min(count, entryPairs.Length);
            for (int local = 0; local < limit; local++)
            {
                int pairIndex = entryPairs[local];
                if (pairIndex < 0 || pairIndex >= ownerGlobals.Length)
                {
                    continue;
                }
                
                if (local < 0 || local >= entryStates.Length)
                {
                    continue;
                }
                
                int ownerLocal = ownerGlobals[pairIndex];
                if (ownerLocal < 0 || ownerLocal == local)
                {
                    continue;
                }
                
                if (!entryStates[local])
                {
                    continue;
                }
                
                entryStates[local] = false;
            }
        }
        
        private bool ClearMaterialPairDuplicates(int pairIndex, int keepLocalIndex)
        {
            if (launchpad == null || launchpad.GetMaterialHandlers() == null)
            {
                return false;
            }
            
            if (pairIndex < 0 || materialPairHasDuplicates == null || pairIndex >= materialPairHasDuplicates.Length)
            {
                return false;
            }
            
            if (!materialPairHasDuplicates[pairIndex])
            {
                return false;
            }
            
            bool changed = false;
            for (int h = 0; h < launchpad.GetMaterialHandlers().Length; h++)
            {
                MaterialHandler handler = launchpad.GetMaterialHandlers()[h];
                if (handler == null)
                {
                    continue;
                }
                
                int count = handler.GetEntryCount();
                int[] entryPairs = handler.materialEntryPairIndices;
                
                if (count <= 0 || entryPairs == null || entryPairs.Length <= 0 || handler.entryStates == null)
                {
                    continue;
                }
                
                int limit = Mathf.Min(count, entryPairs.Length);
                for (int local = 0; local < limit; local++)
                {
                    if (entryPairs[local] != pairIndex)
                    {
                        continue;
                    }
                    
                    // If this is the same handler and same local index, skip it
                    if (handler == this && local == keepLocalIndex)
                    {
                        continue;
                    }
                    
                    if (local < 0 || local >= handler.entryStates.Length)
                    {
                        continue;
                    }
                    
                    if (!handler.entryStates[local])
                    {
                        continue;
                    }
                    
                    handler.entryStates[local] = false;
                    changed = true;
                }
            }
            
            return changed;
        }

        private void ApplyRenderers(Renderer[] renderers)
        {
            if (renderers == null)
            {
                return;
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                int rendererIndex = GetRendererIndex(renderer);
                if (rendererIndex >= 0)
                {
                    ApplyRendererForIndex(rendererIndex);
                }
                else
                {
                    ApplyRendererForRenderer(renderer);
                }
            }
        }

        private int GetRendererIndex(Renderer renderer)
        {
            if (renderer == null || materialRenderers == null)
            {
                return -1;
            }

            for (int i = 0; i < materialRenderers.Length; i++)
            {
                if (materialRenderers[i] == renderer)
                {
                    return i;
                }
            }

            return -1;
        }
        
        private void ApplyRendererForIndex(int rendererIndex)
        {
            if (launchpad == null)
            {
                return;
            }
            
            if (rendererIndex < 0 || materialRenderers == null || rendererIndex >= materialRenderers.Length)
            {
                return;
            }
            
            Renderer renderer = materialRenderers[rendererIndex];
            if (renderer == null)
            {
                return;
            }
            
            Material[] defaults = (materialRendererDefaults != null && rendererIndex < materialRendererDefaults.Length)
            ? materialRendererDefaults[rendererIndex]
            : null;
            bool hasExclusiveActive = false;

            // Collect active materials for this renderer, tracking if any come from an exclusive folder
            MaterialHandler[] handlers = launchpad.GetMaterialHandlers();
            Material[] activeMaterials = new Material[Mathf.Max(1, launchpad.GetItemsPerPage())];
            int activeCount = 0;

            if (handlers != null)
            {
                for (int h = 0; h < handlers.Length; h++)
                {
                    MaterialHandler handler = handlers[h];
                    if (handler == null || handler.entryStates == null || handler.folderEntries == null)
                    {
                        continue;
                    }

                    int handlerFolderIdx = handler.folderIndex;
                    if (materialRendererIndices == null || handlerFolderIdx >= materialRendererIndices.Length)
                    {
                        continue;
                    }

                    if (materialRendererIndices[handlerFolderIdx] != rendererIndex)
                    {
                        continue;
                    }

                    bool handlerExclusive = launchpad.IsFolderExclusive(handlerFolderIdx);

                    int count = handler.GetEntryCount();
                    for (int i = 0; i < count; i++)
                    {
                        if (i >= handler.entryStates.Length) break;
                        if (!handler.entryStates[i]) continue;
                        if (i >= handler.folderEntries.Length) continue;

                        Material mat = handler.folderEntries[i];
                        if (mat == null) continue;

                        if (!ContainsMaterial(activeMaterials, activeCount, mat))
                        {
                            if (activeCount >= activeMaterials.Length)
                            {
                                activeMaterials = ExpandMaterialArray(activeMaterials, activeCount + 1);
                            }

                            activeMaterials[activeCount++] = mat;
                        }

                        if (handlerExclusive)
                        {
                            hasExclusiveActive = true;
                        }
                    }
                }
            }

            int defaultsCount = (defaults != null) ? defaults.Length : 0;
            int baseCapacity = defaultsCount + activeCount;
            if (materialRendererToggleSlotCounts != null && rendererIndex < materialRendererToggleSlotCounts.Length)
            baseCapacity += materialRendererToggleSlotCounts[rendererIndex];

            Material[] combined = new Material[Mathf.Max(1, baseCapacity)];
            int combinedCount = 0;

            // Only include defaults when no exclusive entry is currently active
            if (!hasExclusiveActive && defaultsCount > 0)
            {
                for (int i = 0; i < defaultsCount; i++)
                {
                    Material defMat = defaults[i];
                    if (defMat == null) continue;
                    if (IsMaterialDisabledForRenderer(rendererIndex, defMat))
                    {
                        continue;
                    }
                    if (combinedCount >= combined.Length)
                    {
                        combined = ExpandMaterialArray(combined, combinedCount + 1);
                    }

                    combined[combinedCount++] = defMat;
                }
            }

            for (int i = 0; i < activeCount; i++)
            {
                Material mat = activeMaterials[i];
                if (mat == null) continue;
                if (ContainsMaterial(combined, combinedCount, mat))
                {
                    continue;
                }

                if (combinedCount >= combined.Length)
                {
                    combined = ExpandMaterialArray(combined, combinedCount + 1);
                }

                combined[combinedCount++] = mat;
            }

            if (combinedCount == 0 && defaultsCount > 0)
            {
                for (int i = 0; i < defaultsCount; i++)
                {
                    Material defMat = defaults[i];
                    if (defMat == null)
                    {
                        continue;
                    }

                    if (IsMaterialDisabledForRenderer(rendererIndex, defMat))
                    {
                        continue;
                    }

                    if (combinedCount >= combined.Length)
                    {
                        combined = ExpandMaterialArray(combined, combinedCount + 1);
                    }

                    combined[combinedCount++] = defMat;
                }
            }

            if (combinedCount == 0)
            {
                renderer.sharedMaterials = new Material[0];
                renderer.enabled = false;
                return;
            }

            renderer.enabled = true;
            Material[] finalMaterials = new Material[combinedCount];
            for (int i = 0; i < combinedCount; i++) finalMaterials[i] = combined[i];
            renderer.sharedMaterials = finalMaterials;
        }

        private Renderer[] GetEmptyRendererArray()
        {
            if (emptyRendererArray == null)
            {
                emptyRendererArray = new Renderer[0];
            }

            return emptyRendererArray;
        }

        private Renderer[] WrapRenderer(Renderer renderer)
        {
            if (singleRendererBuffer == null || singleRendererBuffer.Length != 1)
            {
                singleRendererBuffer = new Renderer[1];
            }

            singleRendererBuffer[0] = renderer;
            return singleRendererBuffer;
        }

        private void CacheFallbackMaterials(Renderer[] renderers)
        {
            if (renderers == null)
            {
                return;
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                CacheFallbackMaterials(renderer, renderer.sharedMaterials);
            }
        }

        private void CacheFallbackMaterials(Renderer renderer, Material[] materials)
        {
            if (renderer == null || materials == null)
            {
                return;
            }

            int index = GetFallbackRendererIndex(renderer);
            if (index >= 0)
            {
                if (fallbackInitialRendererMaterials == null)
                {
                    fallbackInitialRendererMaterials = new Material[fallbackInitialRenderers.Length][];
                }

                if (fallbackInitialRendererMaterials[index] == null)
                {
                    fallbackInitialRendererMaterials[index] = CloneMaterials(materials);
                }
                return;
            }

            int newLength = fallbackInitialRenderers != null ? fallbackInitialRenderers.Length + 1 : 1;
            Renderer[] newRenderers = new Renderer[newLength];
            Material[][] newMaterials = new Material[newLength][];

            int copy = fallbackInitialRenderers != null ? fallbackInitialRenderers.Length : 0;
            Material[][] existingMaterials = fallbackInitialRendererMaterials;
            for (int i = 0; i < copy; i++)
            {
                newRenderers[i] = fallbackInitialRenderers[i];
                newMaterials[i] = existingMaterials != null && i < existingMaterials.Length ? existingMaterials[i] : null;
            }

            newRenderers[newLength - 1] = renderer;
            newMaterials[newLength - 1] = CloneMaterials(materials);

            fallbackInitialRenderers = newRenderers;
            fallbackInitialRendererMaterials = newMaterials;
        }

        private int GetFallbackRendererIndex(Renderer renderer)
        {
            if (fallbackInitialRenderers == null || renderer == null)
            {
                return -1;
            }

            for (int i = 0; i < fallbackInitialRenderers.Length; i++)
            {
                if (fallbackInitialRenderers[i] == renderer)
                {
                    return i;
                }
            }

            return -1;
        }

        private Material[] GetFallbackMaterialsForRenderer(Renderer renderer)
        {
            if (renderer == null)
            {
                return null;
            }

            int index = GetFallbackRendererIndex(renderer);
            if (index >= 0)
            {
                Material[] cached = fallbackInitialRendererMaterials != null ? fallbackInitialRendererMaterials[index] : null;
                return cached != null ? CloneMaterials(cached) : null;
            }

            Material[] materials = CloneMaterials(renderer.sharedMaterials);
            CacheFallbackMaterials(renderer, materials);
            return materials;
        }

        private bool RendererArrayContains(Renderer[] renderers, Renderer target)
        {
            if (renderers == null || target == null)
            {
                return false;
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == target)
                {
                    return true;
                }
            }

            return false;
        }

        private void ApplyRendererForRenderer(Renderer renderer)
        {
            if (launchpad == null || renderer == null)
            {
                return;
            }

            Material[] defaults = GetFallbackMaterialsForRenderer(renderer);

            bool hasExclusiveActive = false;

            MaterialHandler[] handlers = launchpad.GetMaterialHandlers();
            Material[] activeMaterials = new Material[Mathf.Max(1, launchpad.GetItemsPerPage())];
            int activeCount = 0;

            if (handlers != null)
            {
                for (int h = 0; h < handlers.Length; h++)
                {
                    MaterialHandler handler = handlers[h];
                    if (handler == null || handler.entryStates == null || handler.folderEntries == null)
                    {
                        continue;
                    }

                    Renderer[] handlerRenderers = handler.GetRenderersForFolder();
                    if (!RendererArrayContains(handlerRenderers, renderer))
                    {
                        continue;
                    }

                    bool handlerExclusive = launchpad.IsFolderExclusive(handler.folderIndex);

                    int count = handler.GetEntryCount();
                    for (int i = 0; i < count; i++)
                    {
                        if (i >= handler.entryStates.Length) break;
                        if (!handler.entryStates[i]) continue;
                        if (i >= handler.folderEntries.Length) continue;

                        Material mat = handler.folderEntries[i];
                        if (mat == null) continue;

                        if (!ContainsMaterial(activeMaterials, activeCount, mat))
                        {
                            if (activeCount >= activeMaterials.Length)
                            {
                                activeMaterials = ExpandMaterialArray(activeMaterials, activeCount + 1);
                            }

                            activeMaterials[activeCount++] = mat;
                        }

                        if (handlerExclusive)
                        {
                            hasExclusiveActive = true;
                        }
                    }
                }
            }

            int defaultsCount = defaults != null ? defaults.Length : 0;
            int combinedCount = 0;
            Material[] combined = new Material[Mathf.Max(1, defaultsCount + activeCount)];

            if (!hasExclusiveActive && defaults != null)
            {
                for (int i = 0; i < defaults.Length; i++)
                {
                    Material defMat = defaults[i];
                    if (defMat == null) continue;
                    if (IsMaterialDisabledForRenderer(renderer, defMat))
                    {
                        continue;
                    }
                    if (combinedCount >= combined.Length)
                    {
                        combined = ExpandMaterialArray(combined, combinedCount + 1);
                    }
                    combined[combinedCount++] = defMat;
                }
            }

            for (int i = 0; i < activeCount; i++)
            {
                Material mat = activeMaterials[i];
                if (mat == null) continue;
                if (ContainsMaterial(combined, combinedCount, mat))
                {
                    continue;
                }

                if (combinedCount >= combined.Length)
                {
                    combined = ExpandMaterialArray(combined, combinedCount + 1);
                }

                combined[combinedCount++] = mat;
            }

            if (combinedCount == 0 && defaults != null)
            {
                for (int i = 0; i < defaults.Length; i++)
                {
                    Material defMat = defaults[i];
                    if (defMat == null) continue;
                    if (IsMaterialDisabledForRenderer(renderer, defMat))
                    {
                        continue;
                    }
                    if (combinedCount >= combined.Length)
                    {
                        combined = ExpandMaterialArray(combined, combinedCount + 1);
                    }

                    combined[combinedCount++] = defMat;
                }
            }

            if (combinedCount == 0)
            {
                renderer.sharedMaterials = new Material[0];
                renderer.enabled = false;
                return;
            }

            renderer.enabled = true;
            Material[] finalMaterials = new Material[combinedCount];
            for (int i = 0; i < combinedCount; i++)
            {
                finalMaterials[i] = combined[i];
            }

            renderer.sharedMaterials = finalMaterials;
        }

        private Material[] CloneMaterials(Material[] source)
        {
            if (source == null)
            {
                return null;
            }

            Material[] clone = new Material[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                clone[i] = source[i];
            }

            return clone;
        }
        
        private bool ContainsMaterial(Material[] array, int length, Material target)
        {
            if (target == null) return false;
            for (int i = 0; i < length; i++)
            {
                if (array[i] == target) return true;
            }
            return false;
        }
        
        private Material[] ExpandMaterialArray(Material[] source, int minimumSize)
        {
            int newSize = Mathf.Max(source.Length * 2, minimumSize);
            if (newSize < 1) newSize = 1;
            Material[] expanded = new Material[newSize];
            int copy = source.Length;
            for (int i = 0; i < copy; i++) expanded[i] = source[i];
            return expanded;
        }

        private bool IsMaterialDisabledForRenderer(int rendererIndex, Material material)
        {
            if (material == null || launchpad == null)
            {
                return false;
            }

            MaterialHandler[] handlers = launchpad.GetMaterialHandlers();
            if (handlers == null)
            {
                return false;
            }

            for (int h = 0; h < handlers.Length; h++)
            {
                MaterialHandler handler = handlers[h];
                if (handler == null || handler.folderEntries == null || handler.entryStates == null)
                {
                    continue;
                }

                int handlerFolderIdx = handler.folderIndex;
                if (materialRendererIndices == null || handlerFolderIdx < 0 || handlerFolderIdx >= materialRendererIndices.Length)
                {
                    continue;
                }

                if (materialRendererIndices[handlerFolderIdx] != rendererIndex)
                {
                    continue;
                }

                int count = handler.GetEntryCount();
                int entryLimit = Mathf.Min(count, handler.folderEntries.Length);
                int stateLimit = Mathf.Min(entryLimit, handler.entryStates.Length);

                for (int i = 0; i < stateLimit; i++)
                {
                    if (handler.folderEntries[i] != material)
                    {
                        continue;
                    }

                    if (!handler.entryStates[i])
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool IsMaterialDisabledForRenderer(Renderer renderer, Material material)
        {
            if (material == null || renderer == null || launchpad == null)
            {
                return false;
            }

            MaterialHandler[] handlers = launchpad.GetMaterialHandlers();
            if (handlers == null)
            {
                return false;
            }

            for (int h = 0; h < handlers.Length; h++)
            {
                MaterialHandler handler = handlers[h];
                if (handler == null || handler.folderEntries == null || handler.entryStates == null)
                {
                    continue;
                }

                Renderer[] handlerRenderers = handler.GetRenderersForFolder();
                if (!RendererArrayContains(handlerRenderers, renderer))
                {
                    continue;
                }

                int count = handler.GetEntryCount();
                int entryLimit = Mathf.Min(count, handler.folderEntries.Length);
                int stateLimit = Mathf.Min(entryLimit, handler.entryStates.Length);

                for (int i = 0; i < stateLimit; i++)
                {
                    if (handler.folderEntries[i] != material)
                    {
                        continue;
                    }

                    if (!handler.entryStates[i])
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        
        // Static helper method to access folderIndex field for UdonSharp compatibility
        public static int GetFolderIndex(MaterialHandler handler)
        {
            return handler != null ? handler.folderIndex : -1;
        }
    }
}
