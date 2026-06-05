#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace Cozen.EnigmaOS.Editor
{
    /// <summary>
    /// A search window for selecting shader properties from a hierarchical list.
    /// Provides a searchable UI with grouping by material and shader sections.
    /// </summary>
    internal class EnigmaPropertySearch
    {
        private readonly List<SearchTreeEntry> entries = new List<SearchTreeEntry>();

        public EnigmaPropertySearch(string title)
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
        /// Opens the search window and delivers the selected entry's <c>userData</c>
        /// as an <c>int[]</c> triple <c>{ category, target, operation }</c>.
        /// Populate the tree with the object overload of <see cref="Group.Add"/> passing
        /// <c>new int[] { cat, tgt, op }</c> as the userData argument.
        /// </summary>
        public void Open(Action<int[]> onSelect, Vector2? pos = null)
        {
            var searchContext = new SearchWindowContext(
                GUIUtility.GUIToScreenPoint(pos ?? Event.current.mousePosition),
                500,
                300
            );
            var provider = UnityEngine.ScriptableObject.CreateInstance<PropertySearchWindowProvider>();
            provider.InitProvider(() => entries, (entry, userData) =>
            {
                if (entry.userData is int[] triple)
                    onSelect(triple);
                return true;
            });
            SearchWindow.Open(searchContext, provider);
        }

        /// <summary>
        /// Opens the search window and delivers the selected entry's <c>userData</c>
        /// cast to <see cref="UnityEngine.Behaviour"/>.  Populate the tree with the
        /// object overload of <see cref="Group.Add"/> to pass Behaviour references.
        /// </summary>
        public void Open(Action<UnityEngine.Behaviour> onSelect, Vector2? pos = null)
        {
            var searchContext = new SearchWindowContext(
                GUIUtility.GUIToScreenPoint(pos ?? Event.current.mousePosition),
                500,
                300
            );
            var provider = UnityEngine.ScriptableObject.CreateInstance<PropertySearchWindowProvider>();
            provider.InitProvider(() => entries, (entry, userData) =>
            {
                if (entry.userData is UnityEngine.Behaviour beh)
                    onSelect(beh);
                return true;
            });
            SearchWindow.Open(searchContext, provider);
        }

        /// <summary>
        /// Opens the search window and delivers the selected entry's <c>userData</c>
        /// cast to <see cref="UnityEngine.Component"/>.  Populate the tree with the
        /// object overload of <see cref="Group.Add"/> to pass component references.
        /// </summary>
        public void Open(Action<UnityEngine.Component> onSelect, Vector2? pos = null)
        {
            var searchContext = new SearchWindowContext(
                GUIUtility.GUIToScreenPoint(pos ?? Event.current.mousePosition),
                500,
                300
            );
            var provider = UnityEngine.ScriptableObject.CreateInstance<PropertySearchWindowProvider>();
            provider.InitProvider(() => entries, (entry, userData) =>
            {
                if (entry.userData is UnityEngine.Component comp)
                    onSelect(comp);
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
            private static Texture2D _blankIcon;
            private static Texture2D BlankIcon
            {
                get
                {
                    if (_blankIcon == null)
                    {
                        _blankIcon = new Texture2D(1, 1);
                        _blankIcon.SetPixel(0, 0, Color.clear);
                        _blankIcon.Apply();
                    }
                    return _blankIcon;
                }
            }

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
                entries.Add(new SearchTreeEntry(new GUIContent(title, BlankIcon))
                {
                    userData = value,
                    level = level
                });
            }

            /// <summary>
            /// Adds an entry whose selection value is any arbitrary object
            /// (e.g. a <see cref="UnityEngine.Component"/> reference).
            /// </summary>
            public void Add(string title, object userData)
            {
                entries.Add(new SearchTreeEntry(new GUIContent(title, BlankIcon))
                {
                    userData = userData,
                    level = level
                });
            }
        }
    }
}
#endif
