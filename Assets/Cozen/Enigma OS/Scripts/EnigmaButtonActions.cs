using UnityEngine;

namespace Cozen.EnigmaOS
{
    /// <summary>
    /// Companion component that stores the authored action list for
    /// <see cref="EnigmaButton"/>.  Lives on the same GameObject.
    ///
    /// Because this is a plain MonoBehaviour (not UdonSharpBehaviour), UdonSharp's
    /// serialisation formatter never touches it, avoiding the
    /// "Field of type 'EnigmaActionData[]' does not exist" error that occurs when
    /// the actions array lives directly on the UdonSharpBehaviour class.
    ///
    /// The array is only used at edit-time by the build step; at runtime the compiled
    /// rt* flat arrays on EnigmaButton are read instead.
    /// </summary>
    [AddComponentMenu("")] // hide from Add Component menu
    public class EnigmaButtonActions : MonoBehaviour
    {
        public EnigmaActionData[] actions = new EnigmaActionData[0];
    }
}
