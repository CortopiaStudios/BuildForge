namespace BuildForge.Editor.Configuration
{
    /// <summary>
    /// Represents a single difference between a Build Profile's Player Settings
    /// and the platform's base Player Settings.
    /// </summary>
    internal class DiffEntry
    {
        public string PropertyPath { get; }
        public string DisplayName { get; }
        public string BaseValue { get; }
        public string ProfileValue { get; }
        public DiffType Type { get; }

        public DiffEntry(string propertyPath, string displayName, string baseValue, string profileValue, DiffType type)
        {
            PropertyPath = propertyPath;
            DisplayName = displayName;
            BaseValue = baseValue;
            ProfileValue = profileValue;
            Type = type;
        }
    }

    internal enum DiffType
    {
        Modified,
        Added,
        Removed
    }
}
