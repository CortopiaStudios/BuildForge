using System;
using System.Collections.Generic;
using UnityEngine;

namespace BuildForge.Editor.OpenXR
{
    /// <summary>
    /// Everything the OpenXR plugin may write for one build target group: the
    /// simple top-level OpenXRSettings properties and the full serialized state
    /// of every managed feature. Used both as the build-time restore snapshot
    /// and as the persisted baseline/expected state for editor application.
    /// Lists are sorted on serialization so two captures of the same state
    /// produce identical strings (Unity reorders the features array).
    /// </summary>
    [Serializable]
    internal class OpenXREditorStateSnapshot
    {
        public int version = 1;
        public List<SettingsOverride> settings = new();
        public List<OpenXRFeatureState> features = new();

        public string ToJson()
        {
            settings.Sort((a, b) => string.CompareOrdinal(a.propertyName, b.propertyName));
            features.Sort((a, b) => string.CompareOrdinal(a.featureTypeName, b.featureTypeName));
            return JsonUtility.ToJson(this);
        }

        /// <summary>Null for empty or unparseable input.</summary>
        public static OpenXREditorStateSnapshot FromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
                return null;
            try
            {
                return JsonUtility.FromJson<OpenXREditorStateSnapshot>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Build Forge/OpenXR] Could not parse settings snapshot: {e.Message}");
                return null;
            }
        }
    }

    [Serializable]
    internal class OpenXRFeatureState
    {
        /// <summary>Type.FullName of the feature — the same key OpenXRProfileConfig uses.</summary>
        public string featureTypeName;
        /// <summary>Kept separately from the JSON so restore can go through the enabled setter last.</summary>
        public bool enabled;
        /// <summary>EditorJsonUtility.ToJson(feature); asset references are written as {fileID, guid, type}, so this is session-stable.</summary>
        public string json;
    }
}
