#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Cozen.EnigmaOS.Editor
{
    /// <summary>
    /// One-shot "Star Enigma OS on GitHub" prompt that fires after Unity
    /// finishes loading. Two buttons:
    /// <list type="bullet">
    /// <item><b>Star on GitHub</b> — opens the repo in the default browser and
    /// flips a permanent <see cref="EditorPrefs"/> flag so the prompt never
    /// re-appears for this user.</item>
    /// <item><b>Remind Me Later</b> — sets a <see cref="SessionState"/> flag
    /// so the prompt is suppressed for the rest of the current Unity session
    /// but re-shows the next time the project is opened.</item>
    /// </list>
    ///
    /// Closing the dialog with the window close button is treated as
    /// "Remind Me Later".
    ///
    /// Dismissal keys (a dev-only reset menu item lives in the un-shipped
    /// Cozen/Editor/EnigmaOSDevMenu — keep the key strings there in sync):
    /// <list type="bullet">
    /// <item><c>EnigmaOS.StarPrompt.NeverAsk</c> — EditorPrefs, permanent.</item>
    /// <item><c>EnigmaOS.StarPrompt.SessionDismissed</c> — SessionState,
    /// one Unity session.</item>
    /// </list>
    ///
    /// Batch mode (CI / automated builds) is skipped entirely so the dialog
    /// can never stall a build pipeline.
    /// </summary>
    [InitializeOnLoad]
    internal static class EnigmaStarPrompt
    {
        private const string RepoUrl =
            "https://github.com/Cozen-Official/Enigma-Launchpad-OS";

        // EditorPrefs key for the permanent "Star on GitHub" dismissal.
        // Survives across Unity sessions and project opens.
        private const string NeverAskKey = "EnigmaOS.StarPrompt.NeverAsk";

        // SessionState key for the one-session "Remind Me Later" dismissal.
        // Survives domain reloads within a session but resets on editor quit.
        private const string SessionDismissKey = "EnigmaOS.StarPrompt.SessionDismissed";

        private const string DialogTitle = "Enjoying Enigma OS?";
        private const string DialogMessage =
            "If Enigma OS has been useful for your world, please consider " +
            "giving the repo a star on GitHub. It's free, takes one click, " +
            "and helps the project a lot.";
        private const string OkButton = "Star on GitHub";
        private const string CancelButton = "Remind Me Later";

        static EnigmaStarPrompt()
        {
            // Defer to delayCall so the dialog doesn't fight asset import,
            // script compile, or the initial Unity load. delayCall fires
            // on the next editor tick after the load settles.
            EditorApplication.delayCall += TryShow;
        }

        private static void TryShow()
        {
            // CI / headless builds — never block.
            if (Application.isBatchMode) return;

            // If Unity is still compiling or refreshing the AssetDatabase,
            // re-queue ourselves and try again on the next tick. Showing a
            // modal dialog mid-compile can interleave badly with the AutoLink
            // installer's own prompt and with prefab/script reimports.
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += TryShow;
                return;
            }

            // Permanent dismissal — user already starred (or chose to).
            if (EditorPrefs.GetBool(NeverAskKey, false)) return;

            // One-session dismissal — user clicked "Remind Me Later" this
            // session and we're back here after a domain reload.
            if (SessionState.GetBool(SessionDismissKey, false)) return;

            bool starClicked = EditorUtility.DisplayDialog(
                DialogTitle,
                DialogMessage,
                OkButton,
                CancelButton);

            if (starClicked)
            {
                Application.OpenURL(RepoUrl);
                EditorPrefs.SetBool(NeverAskKey, true);
            }
            else
            {
                // Remind Me Later — suppress for the rest of this Unity
                // session; will re-show on next editor launch.
                SessionState.SetBool(SessionDismissKey, true);
            }
        }
    }
}
#endif
