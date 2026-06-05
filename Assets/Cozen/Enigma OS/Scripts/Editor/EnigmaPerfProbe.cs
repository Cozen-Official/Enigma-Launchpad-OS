using System.IO;
using UnityEditor;
using UnityEngine;

namespace Cozen.EnigmaOS.Editor
{
    /// <summary>
    /// TEMP debug helper for diagnosing the value-field drag freeze on the
    /// EnigmaController inspector. Hooks <see cref="Application.logMessageReceived"/>
    /// and writes each log entry — message, type, and a millisecond timestamp
    /// since the probe was last reset — to <c>Temp/EnigmaPerfProbe.log</c>.
    ///
    /// Bypasses <see cref="Debug.Log"/> entirely (writes via <see cref="File"/>),
    /// so it doesn't re-enter the MCP plugin's <c>UnityLogCollector</c> the way
    /// the previous <c>PerfScope</c> instrumentation did and trip the
    /// <c>ai-editor-logs.txt</c> file-lock IOException cascade.
    ///
    /// Usage:
    ///   Tools > Enigma > Perf Probe > Start  (clears file, attaches hook)
    ///   Tools > Enigma > Perf Probe > Stop   (detaches hook)
    /// The .log file at Temp/EnigmaPerfProbe.log can then be inspected to see
    /// which messages were emitted during the lag window — including which
    /// subsystem (Enigma, Mochie, UdonSharp, VRChat SDK, MCP, Unity itself)
    /// produced them. Remove this file before shipping.
    /// </summary>
    internal static class EnigmaPerfProbe
    {
        private const string LogPath = "Temp/EnigmaPerfProbe.log";
        private static bool _attached;
        private static System.Diagnostics.Stopwatch _sw;
        private static readonly object _gate = new object();

        // Track wall-clock between events to spot main-thread freezes that
        // happen OUTSIDE OnInspectorGUI (e.g. prefab override recomputation
        // queued by SetDirty). MarkInspectorEnd records the timestamp at the
        // end of a draw; MarkInspectorStart logs the gap since that timestamp
        // when > 100ms. Anything over ~50ms is a perceptible hitch.
        private static long _lastInspectorEndMs;

        public static void MarkInspectorStart()
        {
            if (!_attached || _sw == null) return;
            long now = _sw.ElapsedMilliseconds;
            if (_lastInspectorEndMs > 0)
            {
                long gap = now - _lastInspectorEndMs;
                if (gap > 100)
                {
                    string evtType = Event.current != null ? Event.current.type.ToString() : "?";
                    Write("Gap", $"GAP between OnInspectorGUI calls = {gap}ms (next event = {evtType})");
                }
            }
        }

        public static void MarkInspectorEnd()
        {
            if (!_attached || _sw == null) return;
            _lastInspectorEndMs = _sw.ElapsedMilliseconds;
        }

        // Subscribed in Start, unsubscribed in Stop. Each tick records its own
        // wall-clock duration; values above 50ms are flagged. The first tick
        // after a Start has no baseline so we skip it.
        private static long _lastUpdateTickMs = -1;
        private static System.Diagnostics.Stopwatch _updateSw;
        private static void OnEditorUpdate()
        {
            if (!_attached || _sw == null) return;
            long now = _sw.ElapsedMilliseconds;
            if (_lastUpdateTickMs >= 0)
            {
                long delta = now - _lastUpdateTickMs;
                if (delta > 50)
                    Write("UpdateGap", $"EditorApplication.update gap = {delta}ms");
            }
            _lastUpdateTickMs = now;
        }

        // SceneView.duringSceneGui — fires while a SceneView window is being
        // drawn. Logging this tells us whether SceneView render is part of
        // the per-MouseDrag main-thread block. If 1.5s gaps correlate with
        // SceneView frames being drawn, the bottleneck is in scene rendering
        // (e.g. procedural skybox shader), not in our inspector code.
        private static long _lastSceneGuiMs = -1;
        private static int  _sceneGuiCount;
        private static void OnSceneGui(UnityEditor.SceneView sv)
        {
            if (!_attached || _sw == null) return;
            long now = _sw.ElapsedMilliseconds;
            _sceneGuiCount++;
            // Only log scene-view callbacks during the lag window — i.e. if
            // more than 200ms has passed since last one. Otherwise we'd spam.
            if (_lastSceneGuiMs >= 0 && (now - _lastSceneGuiMs) > 200)
                Write("SceneGui", $"SceneView.duringSceneGui delta = {now - _lastSceneGuiMs}ms (count={_sceneGuiCount})");
            _lastSceneGuiMs = now;
        }

        /// <summary>
        /// Wrap a code path with PerfTrace to write its elapsed time directly
        /// to the probe log file when it exceeds the threshold. Bypasses
        /// <see cref="Debug.Log"/> so it does not trigger the MCP collector's
        /// IOException cascade that the previous timing pass got stuck in.
        /// Active only while the probe is attached.
        /// </summary>
        internal struct PerfTrace : System.IDisposable
        {
            private readonly string _label;
            private readonly double _thresholdMs;
            private readonly System.Diagnostics.Stopwatch _local;
            public PerfTrace(string label, double thresholdMs = 1.0)
            {
                _label = label;
                _thresholdMs = thresholdMs;
                _local = _attached ? System.Diagnostics.Stopwatch.StartNew() : null;
            }
            public void Dispose()
            {
                if (_local == null) return;
                _local.Stop();
                double ms = _local.Elapsed.TotalMilliseconds;
                if (ms < _thresholdMs) return;
                var et = Event.current != null ? Event.current.type.ToString() : "?";
                Write("Trace", $"{_label} event={et} took {ms:F1}ms");
            }
        }

        [MenuItem("Tools/Enigma/Perf Probe/Start")]
        public static void Start()
        {
            lock (_gate)
            {
                if (_attached)
                {
                    Application.logMessageReceived -= OnLog;
                    EditorApplication.update -= OnEditorUpdate;
                }
                try { File.WriteAllText(LogPath, $"# EnigmaPerfProbe started at {System.DateTime.Now:O}\n"); }
                catch { /* swallow — file may not exist yet, WriteAllText creates it */ }
                _sw = System.Diagnostics.Stopwatch.StartNew();
                _lastInspectorEndMs = 0;
                _lastUpdateTickMs = -1;
                _lastSceneGuiMs = -1;
                _sceneGuiCount = 0;
                Application.logMessageReceived += OnLog;
                EditorApplication.update += OnEditorUpdate;
                UnityEditor.SceneView.duringSceneGui += OnSceneGui;
                _attached = true;
            }
            Debug.Log($"[EnigmaPerfProbe] STARTED — writing to {LogPath}");
        }

        [MenuItem("Tools/Enigma/Perf Probe/Stop")]
        public static void Stop()
        {
            lock (_gate)
            {
                if (!_attached) return;
                Application.logMessageReceived -= OnLog;
                EditorApplication.update -= OnEditorUpdate;
                UnityEditor.SceneView.duringSceneGui -= OnSceneGui;
                _attached = false;
            }
            Debug.Log($"[EnigmaPerfProbe] STOPPED — log is at {LogPath}");
        }

        [MenuItem("Tools/Enigma/Perf Probe/Mark (write SENTINEL line)")]
        public static void Mark()
        {
            Write("LogType.Log", "----------------- USER MARK -----------------");
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            // Skip our own marker / start / stop lines to avoid noise.
            if (condition != null && condition.StartsWith("[EnigmaPerfProbe]")) return;
            Write(type.ToString(), condition);
        }

        private static void Write(string type, string message)
        {
            long ms = _sw != null ? _sw.ElapsedMilliseconds : 0;
            string line = $"+{ms,7}ms  {type,-9}  {message}\n";
            try
            {
                // FileShare.ReadWrite lets external readers (this MCP session)
                // tail the file without blocking our writes. AppendAllText opens
                // and closes per call but the cost is bounded — and far cheaper
                // than the IOException cascade we hit going through Debug.Log.
                using (var stream = new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                using (var writer = new StreamWriter(stream))
                    writer.Write(line);
            }
            catch
            {
                // Drop the line silently if the disk write fails — we cannot
                // fall back to Debug.Log without re-entering the cascade we
                // are trying to escape.
            }
        }
    }
}
