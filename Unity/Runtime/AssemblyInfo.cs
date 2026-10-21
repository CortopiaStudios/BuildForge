using System.Runtime.CompilerServices;

// Only Build Forge sets a manifest's fields; game code and plugins read them, and
// plugins add sections.
[assembly: InternalsVisibleTo("BuildForge.Editor")]
[assembly: InternalsVisibleTo("BuildForge.Tests.Editor")]
