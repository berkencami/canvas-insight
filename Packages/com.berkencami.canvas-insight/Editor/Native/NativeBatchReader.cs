using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Profiling;

namespace CanvasInsight.Editor
{
    public sealed class NativeBatch
    {
        public string Reason;
        public readonly List<UnityEngine.Object> Members = new List<UnityEngine.Object>();
    }

    /// <summary>
    /// Reads the batches Unity actually built, from the profiler's UI Details data.
    /// This goes through internal editor APIs: if they are missing in a Unity version,
    /// <see cref="IsSupported"/> is false and the tool falls back to simulation only.
    /// Data exists only for frames recorded in Play Mode with the profiler enabled.
    /// </summary>
    public static class NativeBatchReader
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        static readonly Type _propertyType;
        static readonly MethodInfo _setRoot, _getInfo, _getInstanceIds;
        static readonly FieldInfo _objectId, _parentId, _isBatch, _reason, _idsIndex, _idsCount;

        public static bool IsSupported { get; }

        static NativeBatchReader()
        {
            try
            {
                _propertyType = typeof(ProfilerDriver).Assembly.GetType("UnityEditorInternal.ProfilerProperty");
                _setRoot = _propertyType?.GetMethod("SetRoot", Any, null, new[] { typeof(int), typeof(int), typeof(int) }, null);
                _getInfo = _propertyType?.GetMethod("GetUISystemProfilerInfo", Any);
                _getInstanceIds = _propertyType?.GetMethod("GetUISystemBatchInstanceIDs", Any);
                var info = _getInfo?.ReturnType.GetElementType();
                _objectId = info?.GetField("objectInstanceId", Any);
                _parentId = info?.GetField("parentId", Any);
                _isBatch = info?.GetField("isBatch", Any);
                _reason = info?.GetField("batchBreakingReason", Any);
                _idsIndex = info?.GetField("instanceIDsIndex", Any);
                _idsCount = info?.GetField("instanceIDsCount", Any);
                IsSupported = _setRoot != null && _getInstanceIds != null && _objectId != null && _parentId != null
                              && _isBatch != null && _reason != null && _idsIndex != null && _idsCount != null;
            }
            catch (Exception)
            {
                IsSupported = false;
            }
        }

        public static bool IsRecording => ProfilerDriver.enabled
                                          && ProfilerDriver.IsAreaEnabled(ProfilerArea.UIDetails);

        static bool _recording, _wasEnabled, _wasUIEnabled, _wasUIDetailsEnabled;

        /// <summary>Turns on profiler recording with the UI areas, remembering the user's previous settings.</summary>
        public static void StartRecording()
        {
            if (!_recording)
            {
                _wasEnabled = ProfilerDriver.enabled;
                _wasUIEnabled = ProfilerDriver.IsAreaEnabled(ProfilerArea.UI);
                _wasUIDetailsEnabled = ProfilerDriver.IsAreaEnabled(ProfilerArea.UIDetails);
                _recording = true;
            }
            ProfilerDriver.SetAreaEnabled(ProfilerArea.UI, true);
            ProfilerDriver.SetAreaEnabled(ProfilerArea.UIDetails, true);
            ProfilerDriver.enabled = true;
        }

        /// <summary>Restores the profiler to how it was before <see cref="StartRecording"/>.</summary>
        public static void StopRecording()
        {
            if (!_recording) return;
            _recording = false;
            ProfilerDriver.SetAreaEnabled(ProfilerArea.UI, _wasUIEnabled);
            ProfilerDriver.SetAreaEnabled(ProfilerArea.UIDetails, _wasUIDetailsEnabled);
            ProfilerDriver.enabled = _wasEnabled;
        }

        /// <summary>
        /// Batches of every canvas in the newest recorded frame that has UI data, keyed by canvas
        /// instance id. Returns false when nothing is available.
        /// </summary>
        public static bool TryReadLatest(Dictionary<int, List<NativeBatch>> result, out int frame)
        {
            result.Clear();
            frame = -1;
            if (!IsSupported) return false;

            int last = ProfilerDriver.lastFrameIndex;
            // The newest frames can lack UI data (e.g. the frame recording stopped on), so look a bit further back.
            int first = Math.Max(ProfilerDriver.firstFrameIndex, last - 120);
            for (int f = last; f >= first && f >= 0; f--)
            {
                if (!TryReadFrame(f, result)) continue;
                frame = f;
                return true;
            }
            return false;
        }

        static bool TryReadFrame(int frame, Dictionary<int, List<NativeBatch>> result)
        {
            var property = (IDisposable)Activator.CreateInstance(_propertyType, true);
            try
            {
                _setRoot.Invoke(property, new object[] { frame, -1, 0 });
                var infos = _getInfo.Invoke(property, null) as Array;
                if (infos == null || infos.Length == 0) return false;
                var ids = _getInstanceIds.Invoke(property, null) as int[] ?? Array.Empty<int>();

                bool anyBatch = false;
                foreach (var info in infos)
                {
                    if (!(bool)_isBatch.GetValue(info))
                    {
                        int canvasId = (int)_objectId.GetValue(info);
                        if (!result.ContainsKey(canvasId)) result[canvasId] = new List<NativeBatch>();
                        continue;
                    }

                    anyBatch = true;
                    int parent = (int)_parentId.GetValue(info);
                    if (!result.TryGetValue(parent, out var list)) result[parent] = list = new List<NativeBatch>();
                    var batch = new NativeBatch { Reason = Nicify(_reason.GetValue(info).ToString()) };
                    int start = (int)_idsIndex.GetValue(info), count = (int)_idsCount.GetValue(info);
                    for (int i = 0; i < count && start + i < ids.Length; i++)
                        batch.Members.Add(EditorUtility.InstanceIDToObject(ids[start + i]));
                    list.Add(batch);
                }
                return anyBatch;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                property.Dispose();
            }
        }

        static string Nicify(string reason) => reason == "NoBreaking" ? "First batch" : ObjectNames.NicifyVariableName(reason);
    }
}
