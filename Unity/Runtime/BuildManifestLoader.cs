// On Android (inside the APK) and WebGL (an HTTP URL) StreamingAssets is not
// on the file system and must be fetched with UnityWebRequest; other platforms
// read the file directly. The package declares the UnityWebRequest module, so
// a project that disabled it gets it back.
#if (UNITY_ANDROID || UNITY_WEBGL) && !UNITY_EDITOR
#define BUILDFORGE_MANIFEST_WEBREQUEST
#endif

using System.IO;
using System.Threading.Tasks;
using UnityEngine;
#if BUILDFORGE_MANIFEST_WEBREQUEST
using UnityEngine.Networking;
#endif

namespace BuildForge.Runtime
{
    public static class BuildManifestLoader
    {
        static BuildManifest cachedManifest;
        static bool loadAttempted;

        // Shared by every concurrent LoadAsync caller so the second caller
        // awaits the in-flight load instead of reading the still-empty cache.
        // A Task rather than an Awaitable: an Awaitable can only be awaited once.
        static Task<BuildManifest> loadTask;

        static string ManifestPath =>
            Path.Combine(Application.streamingAssetsPath, "BuildForge", "BuildManifest.json");

        /// <summary>
        /// Synchronous load. Blocks briefly on Android. Not supported on WebGL,
        /// where a fetch cannot complete without yielding to the browser; use
        /// <see cref="LoadAsync"/> there.
        /// </summary>
        public static BuildManifest Load()
        {
            if (loadAttempted)
                return cachedManifest;

#if UNITY_WEBGL && !UNITY_EDITOR
            Debug.LogWarning("[Build Forge] BuildManifestLoader.Load() cannot run on WebGL; use LoadAsync().");
            return null;
#else
            loadAttempted = true;
            var path = ManifestPath;

#if BUILDFORGE_MANIFEST_WEBREQUEST
            using (var request = UnityWebRequest.Get(path))
            {
                var op = request.SendWebRequest();
                float deadline = Time.realtimeSinceStartup + 5f;
                while (!op.isDone)
                {
                    if (Time.realtimeSinceStartup > deadline)
                    {
                        Debug.LogWarning("[Build Forge] Manifest load timed out.");
                        return null;
                    }
                }
                if (request.result != UnityWebRequest.Result.Success)
                    return null;
                cachedManifest = BuildManifest.FromJson(request.downloadHandler.text);
            }
#else
            if (!File.Exists(path))
                return null;
            cachedManifest = BuildManifest.FromJson(File.ReadAllText(path));
#endif

            return cachedManifest;
#endif
        }

        public static async Awaitable<BuildManifest> LoadAsync()
        {
            if (loadAttempted)
                return cachedManifest;

            if (loadTask == null)
                loadTask = LoadInternalAsync();
            return await loadTask;
        }

        static async Task<BuildManifest> LoadInternalAsync()
        {
            var path = ManifestPath;
            BuildManifest manifest = null;

            try
            {
#if BUILDFORGE_MANIFEST_WEBREQUEST
                using (var request = UnityWebRequest.Get(path))
                {
                    await request.SendWebRequest();
                    if (request.result == UnityWebRequest.Result.Success)
                        manifest = BuildManifest.FromJson(request.downloadHandler.text);
                }
#else
                if (File.Exists(path))
                    manifest = BuildManifest.FromJson(await File.ReadAllTextAsync(path));
#endif
            }
            finally
            {
                // Publish the result and the attempted flag together, after the
                // load finished, so Load() callers never observe a half-done state.
                cachedManifest = manifest;
                loadAttempted = true;
                loadTask = null;
            }

            return manifest;
        }

        public static void ClearCache()
        {
            cachedManifest = null;
            loadAttempted = false;
            loadTask = null;
        }
    }
}
