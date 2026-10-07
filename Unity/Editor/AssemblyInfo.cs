using System.Runtime.CompilerServices;

// Bundled plugin assemblies (conditionally compiled when their Unity package
// is present) are first-party: they may use internal core machinery.
[assembly: InternalsVisibleTo("BuildForge.Editor.OpenXR")]
[assembly: InternalsVisibleTo("BuildForge.Editor.Addressables")]
[assembly: InternalsVisibleTo("BuildForge.Editor.XRManagement")]

[assembly: InternalsVisibleTo("BuildForge.Tests.Editor")]
[assembly: InternalsVisibleTo("BuildForge.Tests.XRManagement")]
[assembly: InternalsVisibleTo("BuildForge.Tests.Addressables")]
[assembly: InternalsVisibleTo("BuildForge.Tests.OpenXR")]
