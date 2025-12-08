using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Cozen
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class PropertyHandler : UdonSharpBehaviour
    {
        [Tooltip("Parent launchpad that owns folder selection and UI updates.")]
        public EnigmaLaunchpad launchpad;

        [Tooltip("Folder index used to map page changes and selections.")]
        public int folderIndex;

        [Tooltip("Entries for THIS properties folder only.")]
        public string[] propertyEntries;

        [Tooltip("Optional display names for each property entry.")]
        public string[] propertyDisplayNames;

        [Tooltip("Material index on the renderer to apply each property value to.")]
        public int[] propertyMaterialIndices;

        [Tooltip("Shader property name for each entry.")]
        public string[] propertyNames;

        [Tooltip("Property type for each entry (0 = Float, 1 = Color, 2 = Vector, 3 = Texture).")]
        public int[] propertyTypes;

        [Tooltip("Float values to apply when toggled on.")]
        public float[] propertyFloatValues;

        [Tooltip("Color values to apply when toggled on.")]
        [ColorUsage(true, true)]
        public Color[] propertyColorValues;

        [Tooltip("Vector values to apply when toggled on.")]
        public Vector4[] propertyVectorValues;

        [Tooltip("Texture values to apply when toggled on.")]
        public Texture[] propertyTextureValues;

        [Tooltip("Renderers controlled by this properties folder.")]
        public Renderer[] propertyRenderers;

        [UdonSynced] private bool[] entryStates;
        [UdonSynced] private int currentPage;

        private MaterialPropertyBlock[] propertyBlocks;
        private MaterialPropertyBlock[][] initialPropertyBlocks;
        private bool[] initialEntryStates;
        private Renderer[] emptyRendererArray;

        private const int PropertyTypeFloat = 0;
        private const int PropertyTypeColor = 1;
        private const int PropertyTypeVector = 2;
        private const int PropertyTypeTexture = 3;

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

        public override void OnDeserialization()
        {
            base.OnDeserialization();
            
            // Ensure property blocks exist before applying states
            EnsurePropertyBlocks();
            
            // Apply the synced entry states - only apply active entries
            // We skip inactive entries because applying them would reset the property
            // to the material default, potentially overwriting the active entry's value
            // when multiple entries target the same property
            if (entryStates != null)
            {
                for (int i = 0; i < entryStates.Length; i++)
                {
                    if (entryStates[i])
                    {
                        ApplyPropertyState(i, true);
                    }
                }
            }
            
            if (launchpad != null)
            {
                launchpad.RequestDisplayUpdateFromHandler();
            }
        }

        public void InitializePropertyRuntime()
        {
            EnsureEntryArrays();
            SyncEntryStatesWithRenderers();
            CaptureInitialState();
            CaptureInitialPropertyBlocks();
            EnsurePropertyBlocks();
            RestoreLocalState();
        }

        public int GetEntryCount()
        {
            return propertyEntries != null ? propertyEntries.Length : 0;
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
        /// Applies all entry states to their corresponding properties.
        /// Used by PresetHandler after applying preset snapshots.
        /// </summary>
        public void ApplyStates()
        {
            ApplyAllEntryStates();
        }

        public string GetLabel(int buttonIndex)
        {
            if (buttonIndex == 10)
            {
                return launchpad != null ? launchpad.GetFolderLabelForIndex(folderIndex, false) : string.Empty;
            }

            if (buttonIndex == 9)
            {
                return GetPageLabel();
            }

            if (!IsHandlerConfigured())
            {
                return string.Empty;
            }

            return GetEntryLabel(buttonIndex);
        }

        public bool IsInteractable(int buttonIndex)
        {
            bool configured = IsHandlerConfigured();

            if (buttonIndex == 10)
            {
                return launchpad != null;
            }

            if (buttonIndex == 9)
            {
                return configured && GetPageCount() > 1;
            }

            if (!configured)
            {
                return false;
            }

            return HasValidEntry(buttonIndex);
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

        public void OnSelect(int buttonIndex)
        {
            if (!IsHandlerConfigured() || launchpad == null)
            {
                return;
            }

            int localIndex = GetLocalEntryIndex(buttonIndex);
            if (localIndex < 0 || localIndex >= GetEntryCount())
            {
                return;
            }

            if (entryStates == null || localIndex >= entryStates.Length)
            {
                return;
            }

            EnsureLocalOwnership();
            bool newState = !entryStates[localIndex];
            bool folderExclusive = launchpad.IsFolderExclusive(folderIndex);

            if (newState)
            {
                if (folderExclusive)
                {
                    ClearOtherEntries(localIndex);
                }
                else
                {
                    ClearEntriesWithSameProperty(localIndex);
                }
            }

            entryStates[localIndex] = newState;
            ApplyPropertyState(localIndex, newState);
            RequestSerialization();
        }

        public void OnPageChange(int direction)
        {
            if (!IsHandlerConfigured())
            {
                return;
            }

            int pageCount = GetPageCount();
            if (pageCount <= 1)
            {
                return;
            }

            EnsureLocalOwnership();
            int nextPage = currentPage + (direction > 0 ? 1 : -1);
            if (nextPage < 0)
            {
                nextPage = pageCount - 1;
            }
            else if (nextPage >= pageCount)
            {
                nextPage = 0;
            }

            currentPage = nextPage;
            RequestSerialization();
        }

        public string GetPageLabel()
        {
            int totalPages = GetPageCount();
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

        public void OnLaunchpadDeserialized()
        {
            RestoreLocalState();
        }

        private void CaptureInitialState()
        {
            if (entryStates != null)
            {
                int count = entryStates.Length;
                initialEntryStates = new bool[count];
                for (int i = 0; i < count; i++)
                {
                    initialEntryStates[i] = entryStates[i];
                }
            }
        }

        private void CaptureInitialPropertyBlocks()
        {
            Renderer[] renderers = GetPropertyRenderers();
            int rendererCount = renderers.Length;

            if (initialPropertyBlocks == null || initialPropertyBlocks.Length != rendererCount)
            {
                initialPropertyBlocks = new MaterialPropertyBlock[rendererCount][];
            }

            for (int i = 0; i < rendererCount; i++)
            {
                Renderer renderer = renderers[i];
                Material[] materials = renderer != null ? renderer.sharedMaterials : null;
                int materialCount = materials != null ? materials.Length : 0;

                MaterialPropertyBlock[] rendererBlocks = initialPropertyBlocks[i];
                if (rendererBlocks == null || rendererBlocks.Length != materialCount)
                {
                    rendererBlocks = new MaterialPropertyBlock[materialCount];
                }

                for (int j = 0; j < materialCount; j++)
                {
                    if (rendererBlocks[j] == null)
                    {
                        rendererBlocks[j] = new MaterialPropertyBlock();
                    }

                    if (renderer != null)
                    {
                        renderer.GetPropertyBlock(rendererBlocks[j], j);
                    }
                }

                initialPropertyBlocks[i] = rendererBlocks;
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
            RestoreInitialPropertyBlocks();
            RestoreLocalState();
        }

        public void RestoreLocalState()
        {
            if (!IsHandlerConfigured() || entryStates == null)
            {
                return;
            }

            ApplyAllEntryStates();
        }

        private bool IsHandlerConfigured()
        {
            return launchpad != null &&
                launchpad.GetFolderTypeForIndex(folderIndex) == ToggleFolderType.Properties &&
                GetEntryCount() > 0;
        }

        private void EnsureEntryArrays()
        {
            if (propertyEntries == null)
            {
                propertyEntries = new string[0];
            }

            int count = GetEntryCount();
            if (entryStates == null || entryStates.Length != count)
            {
                bool[] newStates = new bool[count];
                if (entryStates != null)
                {
                    int copyLength = Mathf.Min(entryStates.Length, count);
                    for (int i = 0; i < copyLength; i++)
                    {
                        newStates[i] = entryStates[i];
                    }
                }

                entryStates = newStates;
            }

            EnsureIntArraySize(ref propertyMaterialIndices, count, 0);
            EnsureStringArraySize(ref propertyDisplayNames, count);
            EnsureStringArraySize(ref propertyNames, count);
            EnsureIntArraySize(ref propertyTypes, count, PropertyTypeFloat);
            EnsureFloatArraySize(ref propertyFloatValues, count);
            EnsureColorArraySize(ref propertyColorValues, count);
            EnsureVectorArraySize(ref propertyVectorValues, count);
            EnsureTextureArraySize(ref propertyTextureValues, count);
        }

        private string GetEntryLabel(int buttonIndex)
        {
            if (launchpad == null)
            {
                return string.Empty;
            }

            int localIndex = GetLocalEntryIndex(buttonIndex);
            if (localIndex < 0 || localIndex >= GetEntryCount())
            {
                return string.Empty;
            }

            string entryName = GetDisplayName(localIndex);
            if (string.IsNullOrEmpty(entryName))
            {
                entryName = GetPropertyName(localIndex);
            }

            return string.IsNullOrEmpty(entryName) ? string.Empty : entryName;
        }

        private bool TryGetEntryState(int buttonIndex, out bool state)
        {
            state = false;
            if (launchpad == null)
            {
                return false;
            }

            int localIndex = GetLocalEntryIndex(buttonIndex);
            if (localIndex < 0 || localIndex >= GetEntryCount())
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

        private int GetLocalEntryIndex(int buttonIndex)
        {
            if (launchpad == null)
            {
                return -1;
            }

            return currentPage * launchpad.GetItemsPerPage() + buttonIndex;
        }

        private int GetTotalPages()
        {
            int itemsPerPage = launchpad != null ? launchpad.GetItemsPerPage() : 9;
            int count = GetEntryCount();
            if (itemsPerPage <= 0)
            {
                return 0;
            }

            return (count + itemsPerPage - 1) / itemsPerPage;
        }

        public static int GetFolderIndex(PropertyHandler handler)
        {
            return handler != null ? handler.folderIndex : -1;
        }

        private void EnsureLocalOwnership()
        {
            if (!Networking.IsOwner(gameObject))
            {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            }
        }

        private void EnsurePropertyBlocks()
        {
            Renderer[] renderers = GetPropertyRenderers();
            int rendererCount = renderers.Length;

            if (propertyBlocks == null || propertyBlocks.Length != rendererCount)
            {
                propertyBlocks = new MaterialPropertyBlock[rendererCount];
            }

            for (int i = 0; i < rendererCount; i++)
            {
                if (propertyBlocks[i] == null)
                {
                    propertyBlocks[i] = new MaterialPropertyBlock();
                }
            }
        }

        private Renderer[] GetPropertyRenderers()
        {
            if (propertyRenderers == null || propertyRenderers.Length == 0)
            {
                if (emptyRendererArray == null)
                {
                    emptyRendererArray = new Renderer[0];
                }

                return emptyRendererArray;
            }

            return propertyRenderers;
        }

        private void ApplyAllEntryStates()
        {
            if (!IsHandlerConfigured())
            {
                return;
            }

            if (entryStates == null)
            {
                return;
            }

            EnsurePropertyBlocks();

            for (int i = 0; i < entryStates.Length; i++)
            {
                bool desired = entryStates[i];
                ApplyPropertyState(i, desired);
            }
        }

        private void RestoreInitialPropertyBlocks()
        {
            if (initialPropertyBlocks == null)
            {
                return;
            }

            Renderer[] renderers = GetPropertyRenderers();
            int rendererCount = renderers.Length < initialPropertyBlocks.Length ? renderers.Length : initialPropertyBlocks.Length;

            for (int i = 0; i < rendererCount; i++)
            {
                Renderer renderer = renderers[i];
                MaterialPropertyBlock[] rendererBlocks = initialPropertyBlocks[i];

                if (renderer == null || rendererBlocks == null)
                {
                    continue;
                }

                int blockCount = rendererBlocks.Length;
                for (int j = 0; j < blockCount; j++)
                {
                    MaterialPropertyBlock block = rendererBlocks[j];
                    renderer.SetPropertyBlock(block, j);
                }
            }
        }

        private void ApplyPropertyState(int localIndex, bool active)
        {
            if (!IsHandlerConfigured())
            {
                return;
            }

            string propertyName = GetPropertyName(localIndex);
            if (string.IsNullOrEmpty(propertyName))
            {
                return;
            }

            Renderer[] renderers = GetPropertyRenderers();
            if (renderers.Length == 0)
            {
                return;
            }

            EnsurePropertyBlocks();

            int materialIndex = GetMaterialIndex(localIndex);
            int propertyType = GetPropertyType(localIndex);

            for (int i = 0; i < renderers.Length; i++)
            {
                ApplyPropertyToRenderer(renderers[i], i, materialIndex, propertyName, propertyType, localIndex, active);
            }
        }

        private void ApplyPropertyToRenderer(Renderer renderer, int rendererIndex, int materialIndex, string propertyName, int propertyType, int localIndex, bool active)
        {
            if (renderer == null)
            {
                return;
            }

            if (materialIndex < 0)
            {
                return;
            }

            if (propertyBlocks == null || rendererIndex < 0 || rendererIndex >= propertyBlocks.Length)
            {
                return;
            }

            Material[] materials = renderer.sharedMaterials;
            if (materials == null || materialIndex >= materials.Length)
            {
                return;
            }

            Material targetMaterial = materials[materialIndex];
            if (targetMaterial == null || !targetMaterial.HasProperty(propertyName))
            {
                return;
            }

            MaterialPropertyBlock mpb = propertyBlocks[rendererIndex];
            if (mpb == null)
            {
                return;
            }

            renderer.GetPropertyBlock(mpb, materialIndex);

            if (active)
            {
                if (propertyType == PropertyTypeFloat)
                {
                    if (propertyFloatValues != null && localIndex < propertyFloatValues.Length)
                    {
                        mpb.SetFloat(propertyName, propertyFloatValues[localIndex]);
                    }
                }
                else if (propertyType == PropertyTypeColor)
                {
                    if (propertyColorValues != null && localIndex < propertyColorValues.Length)
                    {
                        mpb.SetColor(propertyName, propertyColorValues[localIndex]);
                    }
                }
                else if (propertyType == PropertyTypeVector)
                {
                    if (propertyVectorValues != null && localIndex < propertyVectorValues.Length)
                    {
                        mpb.SetVector(propertyName, propertyVectorValues[localIndex]);
                    }
                }
                else if (propertyType == PropertyTypeTexture)
                {
                    if (propertyTextureValues != null && localIndex < propertyTextureValues.Length)
                    {
                        mpb.SetTexture(propertyName, propertyTextureValues[localIndex]);
                    }
                }
            }
            else
            {
                if (propertyType == PropertyTypeFloat)
                {
                    mpb.SetFloat(propertyName, targetMaterial.GetFloat(propertyName));
                }
                else if (propertyType == PropertyTypeColor)
                {
                    mpb.SetColor(propertyName, targetMaterial.GetColor(propertyName));
                }
                else if (propertyType == PropertyTypeVector)
                {
                    mpb.SetVector(propertyName, targetMaterial.GetVector(propertyName));
                }
                else if (propertyType == PropertyTypeTexture)
                {
                    mpb.SetTexture(propertyName, targetMaterial.GetTexture(propertyName));
                }
            }

            renderer.SetPropertyBlock(mpb, materialIndex);
        }

        private void SyncEntryStatesWithRenderers()
        {
            if (!IsHandlerConfigured() || entryStates == null || entryStates.Length == 0)
            {
                return;
            }

            for (int i = 0; i < entryStates.Length; i++)
            {
                if (entryStates[i])
                {
                    return;
                }
            }

            Renderer[] renderers = GetPropertyRenderers();
            if (renderers.Length == 0)
            {
                return;
            }

            for (int i = 0; i < entryStates.Length; i++)
            {
                entryStates[i] = false;
            }

            for (int i = 0; i < entryStates.Length; i++)
            {
                string propertyName = GetPropertyName(i);
                int materialIndex = GetMaterialIndex(i);
                int propertyType = GetPropertyType(i);

                if (string.IsNullOrEmpty(propertyName))
                {
                    continue;
                }

                if (!HasEntryValue(i, propertyType))
                {
                    continue;
                }

                if (!RendererMatchesEntry(renderers, materialIndex, propertyName, propertyType, i))
                {
                    continue;
                }

                bool alreadyMatched = false;
                for (int j = 0; j < i; j++)
                {
                    if (entryStates[j] && IsSamePropertyTarget(j, propertyName, materialIndex))
                    {
                        alreadyMatched = true;
                        break;
                    }
                }

                if (!alreadyMatched)
                {
                    entryStates[i] = true;
                }
            }
        }

        private bool RendererMatchesEntry(Renderer[] renderers, int materialIndex, string propertyName, int propertyType, int entryIndex)
        {
            if (renderers == null || renderers.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                Material[] materials = renderer.sharedMaterials;
                if (materials == null || materialIndex < 0 || materialIndex >= materials.Length)
                {
                    continue;
                }

                Material material = materials[materialIndex];
                if (material == null || !material.HasProperty(propertyName))
                {
                    continue;
                }

                if (propertyType == PropertyTypeFloat)
                {
                    float current = material.GetFloat(propertyName);
                    return Mathf.Approximately(current, propertyFloatValues[entryIndex]);
                }
                else if (propertyType == PropertyTypeColor)
                {
                    Color current = material.GetColor(propertyName);
                    return AreColorsEqual(current, propertyColorValues[entryIndex]);
                }
                else if (propertyType == PropertyTypeVector)
                {
                    Vector4 current = material.GetVector(propertyName);
                    return AreVectorsEqual(current, propertyVectorValues[entryIndex]);
                }
                else if (propertyType == PropertyTypeTexture)
                {
                    Texture current = material.GetTexture(propertyName);
                    return current == propertyTextureValues[entryIndex];
                }
            }

            return false;
        }

        private bool HasEntryValue(int entryIndex, int propertyType)
        {
            if (entryIndex < 0)
            {
                return false;
            }

            if (propertyType == PropertyTypeFloat)
            {
                return propertyFloatValues != null && entryIndex < propertyFloatValues.Length;
            }
            else if (propertyType == PropertyTypeColor)
            {
                return propertyColorValues != null && entryIndex < propertyColorValues.Length;
            }
            else if (propertyType == PropertyTypeVector)
            {
                return propertyVectorValues != null && entryIndex < propertyVectorValues.Length;
            }
            else if (propertyType == PropertyTypeTexture)
            {
                return propertyTextureValues != null && entryIndex < propertyTextureValues.Length;
            }

            return false;
        }

        private bool AreColorsEqual(Color a, Color b)
        {
            return Mathf.Approximately(a.r, b.r) &&
                Mathf.Approximately(a.g, b.g) &&
                Mathf.Approximately(a.b, b.b) &&
                Mathf.Approximately(a.a, b.a);
        }

        private bool AreVectorsEqual(Vector4 a, Vector4 b)
        {
            return Mathf.Approximately(a.x, b.x) &&
                Mathf.Approximately(a.y, b.y) &&
                Mathf.Approximately(a.z, b.z) &&
                Mathf.Approximately(a.w, b.w);
        }

        private void ClearOtherEntries(int activeIndex)
        {
            if (entryStates == null)
            {
                return;
            }

            for (int i = 0; i < entryStates.Length; i++)
            {
                if (i == activeIndex)
                {
                    continue;
                }

                if (entryStates[i])
                {
                    entryStates[i] = false;
                    ApplyPropertyState(i, false);
                }
            }
        }

        private void ClearEntriesWithSameProperty(int activeIndex)
        {
            if (entryStates == null)
            {
                return;
            }

            string propertyName = GetPropertyName(activeIndex);
            int materialIndex = GetMaterialIndex(activeIndex);

            if (string.IsNullOrEmpty(propertyName))
            {
                return;
            }

            for (int i = 0; i < entryStates.Length; i++)
            {
                if (i == activeIndex)
                {
                    continue;
                }

                if (entryStates[i] && IsSamePropertyTarget(i, propertyName, materialIndex))
                {
                    entryStates[i] = false;
                    ApplyPropertyState(i, false);
                }
            }
        }

        private bool IsSamePropertyTarget(int entryIndex, string propertyName, int materialIndex)
        {
            if (propertyNames == null || entryIndex < 0 || entryIndex >= propertyNames.Length)
            {
                return false;
            }

            if (propertyNames[entryIndex] != propertyName)
            {
                return false;
            }

            return GetMaterialIndex(entryIndex) == materialIndex;
        }

        private string GetDisplayName(int localIndex)
        {
            if (propertyDisplayNames == null || localIndex < 0 || localIndex >= propertyDisplayNames.Length)
            {
                if (propertyEntries != null && localIndex >= 0 && localIndex < propertyEntries.Length)
                {
                    return propertyEntries[localIndex];
                }

                return string.Empty;
            }

            return propertyDisplayNames[localIndex];
        }

        private string GetPropertyName(int localIndex)
        {
            if (propertyNames == null || localIndex < 0 || localIndex >= propertyNames.Length)
            {
                return string.Empty;
            }

            return propertyNames[localIndex];
        }

        private int GetPropertyType(int localIndex)
        {
            if (propertyTypes == null || localIndex < 0 || localIndex >= propertyTypes.Length)
            {
                return PropertyTypeFloat;
            }

            return propertyTypes[localIndex];
        }

        private int GetMaterialIndex(int localIndex)
        {
            if (propertyMaterialIndices == null || localIndex < 0 || localIndex >= propertyMaterialIndices.Length)
            {
                return 0;
            }

            return propertyMaterialIndices[localIndex];
        }

        private bool HasValidEntry(int buttonIndex)
        {
            if (!IsHandlerConfigured())
            {
                return false;
            }

            int localIndex = GetLocalEntryIndex(buttonIndex);
            if (localIndex < 0 || localIndex >= GetEntryCount())
            {
                return false;
            }

            string propertyName = GetPropertyName(localIndex);
            return !string.IsNullOrEmpty(propertyName) && GetPropertyRenderers().Length > 0;
        }

        private void EnsureIntArraySize(ref int[] array, int count, int defaultValue)
        {
            if (array == null || array.Length != count)
            {
                int[] newArray = new int[count];
                if (array != null)
                {
                    int copyLength = Mathf.Min(array.Length, count);
                    for (int i = 0; i < copyLength; i++)
                    {
                        newArray[i] = array[i];
                    }
                }

                for (int i = 0; i < count; i++)
                {
                    if (i >= (array != null ? array.Length : 0))
                    {
                        newArray[i] = defaultValue;
                    }
                }

                array = newArray;
            }
        }

        private void EnsureFloatArraySize(ref float[] array, int count)
        {
            if (array == null || array.Length != count)
            {
                float[] newArray = new float[count];
                if (array != null)
                {
                    int copyLength = Mathf.Min(array.Length, count);
                    for (int i = 0; i < copyLength; i++)
                    {
                        newArray[i] = array[i];
                    }
                }

                array = newArray;
            }
        }

        private void EnsureColorArraySize(ref Color[] array, int count)
        {
            if (array == null || array.Length != count)
            {
                Color[] newArray = new Color[count];
                if (array != null)
                {
                    int copyLength = Mathf.Min(array.Length, count);
                    for (int i = 0; i < copyLength; i++)
                    {
                        newArray[i] = array[i];
                    }
                }

                array = newArray;
            }
        }

        private void EnsureVectorArraySize(ref Vector4[] array, int count)
        {
            if (array == null || array.Length != count)
            {
                Vector4[] newArray = new Vector4[count];
                if (array != null)
                {
                    int copyLength = Mathf.Min(array.Length, count);
                    for (int i = 0; i < copyLength; i++)
                    {
                        newArray[i] = array[i];
                    }
                }

                array = newArray;
            }
        }

        private void EnsureTextureArraySize(ref Texture[] array, int count)
        {
            if (array == null || array.Length != count)
            {
                Texture[] newArray = new Texture[count];
                if (array != null)
                {
                    int copyLength = Mathf.Min(array.Length, count);
                    for (int i = 0; i < copyLength; i++)
                    {
                        newArray[i] = array[i];
                    }
                }

                array = newArray;
            }
        }

        private void EnsureStringArraySize(ref string[] array, int count)
        {
            if (array == null || array.Length != count)
            {
                string[] newArray = new string[count];
                if (array != null)
                {
                    int copyLength = Mathf.Min(array.Length, count);
                    for (int i = 0; i < copyLength; i++)
                    {
                        newArray[i] = array[i];
                    }
                }

                array = newArray;
            }
        }
    }
}
