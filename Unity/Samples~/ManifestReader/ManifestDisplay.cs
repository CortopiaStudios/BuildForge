using BuildForge.Runtime;
using UnityEngine;

namespace BuildForge.Samples
{
    /// <summary>
    /// Example MonoBehaviour that reads and displays the Build Forge manifest at runtime.
    /// Uses LoadAsync to avoid blocking the main thread on Android.
    /// Attach to any GameObject in your scene.
    /// </summary>
    public class ManifestDisplay : MonoBehaviour
    {
        async void Start()
        {
            var manifest = await BuildManifestLoader.LoadAsync();
            if (manifest == null)
            {
                Debug.Log("[ManifestDisplay] No build manifest found (only available in built players).");
                return;
            }

            Debug.Log($"[ManifestDisplay] BuildProfileName: {manifest.BuildProfileName}, BuildVariant: '{manifest.BuildVariant}'");
            Debug.Log($"[ManifestDisplay] DevelopmentBuild: {manifest.DevelopmentBuild}, ManagedCodeVariant: {manifest.ManagedCodeVariant}");
            Debug.Log($"[ManifestDisplay] BuildTimestamp: {manifest.BuildTimestamp}");
            Debug.Log($"[ManifestDisplay] BuildTarget: {manifest.BuildTarget}");
            Debug.Log($"[ManifestDisplay] BuildTargetGroup: {manifest.BuildTargetGroup}");
            Debug.Log($"[ManifestDisplay] ProductName: {manifest.ProductName}");
            Debug.Log($"[ManifestDisplay] ProductVersion: {manifest.ProductVersion}");
            Debug.Log($"[ManifestDisplay] BuildNumber: {manifest.BuildNumber}");
            Debug.Log($"[ManifestDisplay] UnityVersion: {manifest.UnityVersion}");
            Debug.Log($"[ManifestDisplay] BuildForgeVersion: {manifest.BuildForgeVersion}");

            foreach (var section in manifest.PluginSections)
            {
                Debug.Log($"[ManifestDisplay] --- {section.Name} ---");
                foreach (var entry in section.Entries)
                {
                    Debug.Log($"[ManifestDisplay]   {entry.Key}: {entry.Value}");
                }
            }
        }
    }
}
