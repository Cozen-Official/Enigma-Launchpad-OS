#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace Cozen
{
    /// <summary>
    /// A search window for selecting shader properties from a hierarchical list.
    /// Provides a searchable UI with grouping by material and shader sections.
    /// </summary>
    internal class PropertySearchWindow
    {
        private readonly List<SearchTreeEntry> entries = new List<SearchTreeEntry>();

        public PropertySearchWindow(string title)
        {
            entries.Add(new SearchTreeGroupEntry(new GUIContent(title), 0));
        }

        /// <summary>
        /// Opens the search window at the current mouse position or a specified position.
        /// </summary>
        /// <param name="onSelect">Callback when a property is selected, receives the property name</param>
        /// <param name="pos">Optional position for the window. If null, uses current mouse position</param>
        public void Open(Action<string> onSelect, Vector2? pos = null)
        {
            var searchContext = new SearchWindowContext(
                GUIUtility.GUIToScreenPoint(pos ?? Event.current.mousePosition), 
                500, 
                300
            );
            var provider = UnityEngine.ScriptableObject.CreateInstance<PropertySearchWindowProvider>();
            provider.InitProvider(() => entries, (entry, userData) => {
                if (entry.userData != null)
                {
                    onSelect((string)entry.userData);
                }
                return true;
            });
            SearchWindow.Open(searchContext, provider);
        }

        /// <summary>
        /// Gets the main group for adding entries to the search tree.
        /// </summary>
        public Group GetMainGroup()
        {
            return new Group(entries, 1);
        }

        /// <summary>
        /// Represents a group in the search tree that can contain other groups or entries.
        /// </summary>
        public class Group
        {
            private readonly List<SearchTreeEntry> entries;
            private readonly int level;

            public Group(List<SearchTreeEntry> entries, int level)
            {
                this.entries = entries;
                this.level = level;
            }

            /// <summary>
            /// Adds a subgroup with the given title.
            /// </summary>
            public Group AddGroup(string title)
            {
                entries.Add(new SearchTreeGroupEntry(new GUIContent(title), level));
                return new Group(entries, level + 1);
            }

            /// <summary>
            /// Adds an entry to this group.
            /// </summary>
            /// <param name="title">Display name for the entry</param>
            /// <param name="value">Value to return when selected (property name)</param>
            public void Add(string title, string value = null)
            {
                entries.Add(new SearchTreeEntry(new GUIContent(title))
                {
                    userData = value,
                    level = level
                });
            }
        }
    }
}
#endif
