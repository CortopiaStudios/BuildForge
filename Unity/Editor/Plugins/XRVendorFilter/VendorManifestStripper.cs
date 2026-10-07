using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;

namespace BuildForge.Editor.Plugins
{
    /// <summary>
    /// Removes vendor manifest entries from a generated Gradle project, and
    /// finds what is left of the excluded libraries and the vendors' entries.
    /// Works on files only, so it can be tested without a build.
    /// </summary>
    internal static class VendorManifestStripper
    {
        internal const string AndroidNamespace = "http://schemas.android.com/apk/res/android";

        // Gradle keeps earlier builds' outputs next to the sources; only the
        // sources belong to this build. Unity's player data in assets holds no
        // libraries or manifests and is by far the largest part of the project.
        static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
        {
            "build", ".gradle", ".cxx", ".idea", "assets",
        };

        /// <summary>The Gradle project root for the path Unity passes, its unityLibrary module.</summary>
        internal static string ProjectRoot(string unityLibraryPath)
        {
            var full = Path.GetFullPath(unityLibraryPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var parent = Path.GetDirectoryName(full);
            return string.Equals(Path.GetFileName(full), "unityLibrary", StringComparison.OrdinalIgnoreCase) && parent != null
                ? parent
                : full;
        }

        static IEnumerable<string> Walk(string directory, bool includeDirectories)
        {
            foreach (var file in Directory.EnumerateFiles(directory))
                yield return file;
            foreach (var sub in Directory.EnumerateDirectories(directory))
            {
                if (SkippedDirectories.Contains(Path.GetFileName(sub)))
                    continue;
                if (includeDirectories)
                    yield return sub;
                foreach (var entry in Walk(sub, includeDirectories))
                    yield return entry;
            }
        }

        static IEnumerable<string> Manifests(string root)
            => Walk(root, false).Where(f => Path.GetFileName(f) == "AndroidManifest.xml");

        static bool Matches(XmlElement element, XRVendors.StripRules rules)
        {
            if (rules.Namespaces.Contains(element.NamespaceURI))
                return true;
            var name = element.GetAttribute("name", AndroidNamespace);
            return name.Length > 0 && rules.Prefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal));
        }

        static string Describe(XmlElement element)
        {
            var name = element.GetAttribute("name", AndroidNamespace);
            // Horizon OS elements carry an unprefixed name attribute.
            if (name.Length == 0)
                name = element.GetAttribute("name");
            return name.Length > 0 ? $"{element.LocalName} {name}" : element.LocalName;
        }

        static bool IsAttached(XmlNode node, XmlDocument document)
        {
            for (var current = node; current != null; current = current.ParentNode)
                if (current == document)
                    return true;
            return false;
        }

        /// <summary>Removes the matching elements and the namespace declarations they leave unused.</summary>
        internal static List<string> Strip(XmlDocument document, XRVendors.StripRules rules)
        {
            var removed = new List<string>();
            foreach (var element in document.SelectNodes("//*").Cast<XmlElement>().ToArray())
            {
                // An element inside one already removed went with it.
                if (!IsAttached(element, document) || !Matches(element, rules))
                    continue;
                removed.Add(Describe(element));
                element.ParentNode.RemoveChild(element);
            }

            var root = document.DocumentElement;
            if (root != null && removed.Count > 0)
            {
                var used = new HashSet<string>(document.SelectNodes("//*").Cast<XmlElement>().Select(e => e.NamespaceURI));
                foreach (var attribute in root.Attributes.Cast<XmlAttribute>().ToArray())
                    if (attribute.Prefix == "xmlns" && rules.Namespaces.Contains(attribute.Value) && !used.Contains(attribute.Value))
                        root.Attributes.Remove(attribute);
            }
            return removed;
        }

        static List<string> Find(XmlDocument document, XRVendors.StripRules rules)
            => document.SelectNodes("//*").Cast<XmlElement>().Where(e => Matches(e, rules)).Select(Describe).ToList();

        /// <summary>Strips every manifest among the project's sources; returns what was removed, by file.</summary>
        internal static List<string> StripProject(string root, XRVendors.StripRules rules)
        {
            var removed = new List<string>();
            foreach (var manifest in Manifests(root).ToList())
            {
                var document = new XmlDocument { PreserveWhitespace = true };
                document.Load(manifest);
                var entries = Strip(document, rules);
                if (entries.Count == 0)
                    continue;
                document.Save(manifest);
                removed.AddRange(entries.Select(e => $"{Path.GetRelativePath(root, manifest)}: {e}"));
            }
            return removed;
        }

        /// <summary>
        /// What remains in the project's sources of the excluded libraries (by
        /// file or folder name) and of the vendors' manifest entries: in the
        /// source manifests, and in the manifests inside the remaining Android
        /// libraries, which Gradle merges later.
        /// </summary>
        internal static List<string> FindLeftovers(string root, XRVendors.StripRules rules, ICollection<string> libraryFileNames)
        {
            var leftovers = new List<string>();
            var libraries = new HashSet<string>(libraryFileNames, StringComparer.OrdinalIgnoreCase);
            foreach (var entry in Walk(root, true))
            {
                var name = Path.GetFileName(entry);
                var relative = Path.GetRelativePath(root, entry);
                if (libraries.Contains(name))
                    leftovers.Add($"{relative}: excluded library");
                if (rules.IsEmpty || Directory.Exists(entry))
                    continue;
                if (name == "AndroidManifest.xml")
                {
                    var document = new XmlDocument();
                    document.Load(entry);
                    leftovers.AddRange(Find(document, rules).Select(e => $"{relative}: {e}"));
                }
                else if (name.EndsWith(".aar", StringComparison.OrdinalIgnoreCase))
                {
                    using var archive = new ZipArchive(File.OpenRead(entry), ZipArchiveMode.Read);
                    var manifest = archive.GetEntry("AndroidManifest.xml");
                    if (manifest == null)
                        continue;
                    var document = new XmlDocument();
                    using (var stream = manifest.Open())
                        document.Load(stream);
                    leftovers.AddRange(Find(document, rules).Select(e => $"{relative} (merged by Gradle): {e}"));
                }
            }
            return leftovers;
        }
    }
}
