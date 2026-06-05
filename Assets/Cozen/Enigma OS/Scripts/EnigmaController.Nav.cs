
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace Cozen.EnigmaOS
{
    public partial class EnigmaController
    {
        // ------------------------------------------------------------------------
        //  FOLDER & PAGE NAVIGATION
        // ------------------------------------------------------------------------

        public void CycleFolder(int direction)
        {
            if (!CanLocalUserInteract()) return;
            EnsureLocalOwnership();

            int total = rtFolderNames != null ? rtFolderNames.Length : 0;
            if (total <= 0) return;

            int prevFolder = currentFolderIndex;
            currentFolderIndex = (currentFolderIndex + direction + total) % total;
            currentPageIndex   = 0;
            Log($"CycleFolder() {prevFolder} -> {currentFolderIndex} (dir={direction})");

            DeferredRequestSerialization();
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(SyncUpdateDisplay));
        }

        public void GoToFolder(int folderIndex)
        {
            if (!CanLocalUserInteract()) return;
            EnsureLocalOwnership();

            int total = rtFolderNames != null ? rtFolderNames.Length : 0;
            if (total <= 0) return;

            currentFolderIndex = Mathf.Clamp(folderIndex, 0, total - 1);
            currentPageIndex   = 0;
            Log($"GoToFolder() folder={currentFolderIndex}");

            DeferredRequestSerialization();
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(SyncUpdateDisplay));
        }

        public void ChangePage(int direction)
        {
            if (!CanLocalUserInteract()) return;
            EnsureLocalOwnership();

            int totalPages = GetPageCount(currentFolderIndex);
            int prevPage = currentPageIndex;
            currentPageIndex = (currentPageIndex + direction + totalPages) % totalPages;
            Log($"ChangePage() {prevPage} -> {currentPageIndex} (dir={direction}, total={totalPages})");

            DeferredRequestSerialization();
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(SyncUpdateDisplay));
        }

        public void GoToPage(int pageIndex)
        {
            if (!CanLocalUserInteract()) return;
            EnsureLocalOwnership();

            int totalPages = GetPageCount(currentFolderIndex);
            if (totalPages <= 0) return;
            currentPageIndex = Mathf.Clamp(pageIndex, 0, totalPages - 1);
            Log($"GoToPage() page={currentPageIndex}");

            DeferredRequestSerialization();
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(SyncUpdateDisplay));
        }

        // ------------------------------------------------------------------------
        //  SCREEN HANDLER
        // ------------------------------------------------------------------------

        public void CycleScreen()
        {
            if (!CanLocalUserInteract()) return;
        }
    }
}
