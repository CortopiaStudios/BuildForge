using System.Runtime.CompilerServices;

// The bundled plugins compile like a user's plugin, against the public API. The one
// exception: XR Loaders and OpenXR implement the internal IForgePlayModeMirror for
// Play Mode Follows Applied Profile, so their assemblies see internals; they use
// nothing else of them.
[assembly: InternalsVisibleTo("BuildForge.Editor.OpenXR")]
[assembly: InternalsVisibleTo("BuildForge.Editor.XRManagement")]

[assembly: InternalsVisibleTo("BuildForge.Tests.Editor")]
[assembly: InternalsVisibleTo("BuildForge.Tests.XRManagement")]
[assembly: InternalsVisibleTo("BuildForge.Tests.Addressables")]
[assembly: InternalsVisibleTo("BuildForge.Tests.OpenXR")]
