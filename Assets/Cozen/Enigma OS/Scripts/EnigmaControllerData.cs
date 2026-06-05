using UnityEngine;

namespace Cozen.EnigmaOS
{
    /// <summary>
    /// Editor-time data storage for EnigmaController.
    /// This is a plain MonoBehaviour (NOT UdonSharpBehaviour) so it doesn't get compiled
    /// into the Udon heap. The build step reads from here; the runtime never accesses it.
    /// </summary>
    public class EnigmaControllerData : MonoBehaviour
    {
        [SerializeField] public EnigmaFolderData[] folders = new EnigmaFolderData[0];
    }

#if UNITY_EDITOR
    /// <summary>
    /// Extension methods so existing editor code can keep calling ctrl.GetFolders()/SetFolders()
    /// without changing every call site. Data is stored on the companion EnigmaControllerData component.
    /// </summary>
    public static class EnigmaControllerDataExtensions
    {
        public static EnigmaFolderData[] GetFolders(this EnigmaController ctrl)
        {
            var data = ctrl.GetComponent<EnigmaControllerData>();
            if (data == null)
            {
                data = ctrl.gameObject.AddComponent<EnigmaControllerData>();
                UnityEditor.EditorUtility.SetDirty(ctrl.gameObject);
            }
            return data.folders ?? new EnigmaFolderData[0];
        }

        public static void SetFolders(this EnigmaController ctrl, EnigmaFolderData[] folders)
        {
            var data = ctrl.GetComponent<EnigmaControllerData>();
            if (data == null)
            {
                data = ctrl.gameObject.AddComponent<EnigmaControllerData>();
                UnityEditor.EditorUtility.SetDirty(ctrl.gameObject);
            }
            data.folders = folders;
            UnityEditor.EditorUtility.SetDirty(data);
        }
    }
#endif
}
