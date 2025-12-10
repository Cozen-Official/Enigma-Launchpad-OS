#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace Cozen
{
    /// <summary>
    /// Provider for PropertySearchWindow that implements Unity's ISearchWindowProvider interface.
    /// </summary>
    internal class PropertySearchWindowProvider : ScriptableObject, ISearchWindowProvider
    {
        private Func<List<SearchTreeEntry>> _onCreateSearchTree;
        private List<string> _staticSearchEntries;
        private Func<SearchTreeEntry, object, bool> _onSelectEntry;
        private object _userData;

        public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
        {
            if (_staticSearchEntries == null)
            {
                return _onCreateSearchTree?.Invoke();
            }

            var entries = new List<SearchTreeEntry>();
            foreach (var entry in _staticSearchEntries)
            {
                entries.Add(new SearchTreeEntry(new GUIContent(entry)));
            }
            return entries;
        }

        public bool OnSelectEntry(SearchTreeEntry entry, SearchWindowContext context)
        {
            return _onSelectEntry?.Invoke(entry, _userData) ?? false;
        }

        /// <summary>
        /// Initializes the provider with a dynamic tree builder function.
        /// </summary>
        public void InitProvider(
            Func<List<SearchTreeEntry>> onCreateSearchTree, 
            Func<SearchTreeEntry, object, bool> onSelectEntry, 
            object userData = null)
        {
            _onCreateSearchTree = onCreateSearchTree;
            _onSelectEntry = onSelectEntry;
            _userData = userData;
        }

        /// <summary>
        /// Initializes the provider with a static list of entries.
        /// </summary>
        public void InitProvider(
            List<string> staticSearchEntries, 
            Func<SearchTreeEntry, object, bool> onSelectEntry, 
            object userData = null)
        {
            _staticSearchEntries = staticSearchEntries;
            _onSelectEntry = onSelectEntry;
            _userData = userData;
        }
    }
}
#endif
