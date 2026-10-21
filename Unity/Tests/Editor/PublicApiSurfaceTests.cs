using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BuildForge.Editor.Core;
using BuildForge.Runtime;
using NUnit.Framework;

namespace BuildForge.Tests.Editor
{
    /// <summary>
    /// Locks the 1.0 public API surface (see ARCHITECTURE.md "Public API
    /// Surface"). Exposing a type later is additive and non-breaking; hiding one
    /// after release is breaking — so any change to these lists must be a
    /// deliberate decision, not a side effect.
    /// </summary>
    public class PublicApiSurfaceTests
    {
        static readonly string[] ExpectedEditorPublicTypes =
        {
            "BuildForge.CommandLine",
            "BuildForge.Editor.Configuration.ForgeProfile",
            "BuildForge.Editor.Configuration.ForgeSettings",
            "BuildForge.Editor.Core.BuildEnvironment",
            "BuildForge.Editor.Core.BuildProfileUtility",
            "BuildForge.Editor.Core.ForgeBuildContext",
            "BuildForge.Editor.Core.ForgeBuildRestoreException",
            "BuildForge.Editor.Core.ForgeBuildRunner",
            "BuildForge.Editor.Core.ForgePluginAttribute",
            "BuildForge.Editor.Core.GitHelper",
            "BuildForge.Editor.Core.IForgeEditorApplicable",
            "BuildForge.Editor.Core.IForgeGradleProcessor",
            "BuildForge.Editor.Core.IForgeManifestContributor",
            "BuildForge.Editor.Core.IForgePlugin",
        };

        static readonly string[] ExpectedRuntimePublicTypes =
        {
            "BuildForge.Runtime.BuildManifest",
            "BuildForge.Runtime.BuildManifestLoader",
            "BuildForge.Runtime.ManifestEntry",
            "BuildForge.Runtime.ManifestSection",
        };

        static string[] PublicTypesOf(Assembly assembly) =>
            assembly.GetExportedTypes().Select(t => t.FullName).OrderBy(n => n).ToArray();

        [Test]
        public void EditorAssembly_PublicSurface_IsExactlyTheApprovedList()
        {
            CollectionAssert.AreEqual(
                ExpectedEditorPublicTypes.OrderBy(n => n).ToArray(),
                PublicTypesOf(typeof(ForgeBuildRunner).Assembly));
        }

        [Test]
        public void RuntimeAssembly_PublicSurface_IsExactlyTheApprovedList()
        {
            CollectionAssert.AreEqual(
                ExpectedRuntimePublicTypes.OrderBy(n => n).ToArray(),
                PublicTypesOf(typeof(BuildManifest).Assembly));
        }

        [Test]
        public void BundledPluginAssemblies_ExposeNoPublicTypes()
        {
            // Every bundled plugin has an assembly of its own. The OpenXR,
            // Addressables and XR Management ones exist only when their Unity
            // package is installed; assert emptiness for whichever are loaded.
            var names = new[]
            {
                "BuildForge.Editor.BuildNumber", "BuildForge.Editor.GitMetadata", "BuildForge.Editor.CloudDiagnostics",
                "BuildForge.Editor.AndroidKeystoreEnv", "BuildForge.Editor.AndroidManifest", "BuildForge.Editor.XRVendorFilter",
                "BuildForge.Editor.OpenXR", "BuildForge.Editor.Addressables", "BuildForge.Editor.XRManagement",
            };
            var pluginAssemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => names.Contains(a.GetName().Name)).ToList();
            Assert.That(pluginAssemblies.Count, Is.GreaterThanOrEqualTo(6), "The six package-free plugin assemblies are loaded.");

            foreach (var assembly in pluginAssemblies)
            {
                CollectionAssert.IsEmpty(PublicTypesOf(assembly),
                    $"{assembly.GetName().Name} must not expose public types.");
            }
        }

        // The public and protected members each public type declares, as ApiMembers writes them:
        // changing, removing or hiding one after 1.0 breaks callers, so a change here is a
        // deliberate API decision, as for the type lists above.
        static readonly string[] ExpectedMembers =
        {
            "BuildForge.CommandLine: static class",
            "BuildForge.CommandLine: static void Activate()",
            "BuildForge.CommandLine: static void Build()",
            "BuildForge.Editor.Configuration.ForgeProfile: .ctor()",
            "BuildForge.Editor.Configuration.ForgeProfile: T GetPluginConfig<T : class, new()>(string key)",
            "BuildForge.Editor.Configuration.ForgeProfile: UnityEditor.Build.Profile.BuildProfile BuildProfile { get; }",
            "BuildForge.Editor.Configuration.ForgeProfile: bool RemovePreviousBuildOutput { get; }",
            "BuildForge.Editor.Configuration.ForgeProfile: bool SupportsVariant(string variant)",
            "BuildForge.Editor.Configuration.ForgeProfile: class : UnityEngine.ScriptableObject",
            "BuildForge.Editor.Configuration.ForgeProfile: string DisplayName { get; }",
            "BuildForge.Editor.Configuration.ForgeProfile: string GetResolvedOutputPath(UnityEditor.BuildTarget target, string variant = null)",
            "BuildForge.Editor.Configuration.ForgeProfile: string OutputPath { get; }",
            "BuildForge.Editor.Configuration.ForgeProfile: void SetPluginConfig<T : class>(string key, T config)",
            "BuildForge.Editor.Configuration.ForgeSettings: .ctor()",
            "BuildForge.Editor.Configuration.ForgeSettings: System.Collections.Generic.IReadOnlyList<string> BuildVariants { get; }",
            "BuildForge.Editor.Configuration.ForgeSettings: T GetGlobalPluginConfig<T : class, new()>(string key)",
            "BuildForge.Editor.Configuration.ForgeSettings: bool IsPluginEnabled(System.Type pluginType)",
            "BuildForge.Editor.Configuration.ForgeSettings: class : UnityEditor.ScriptableSingleton<BuildForge.Editor.Configuration.ForgeSettings>",
            "BuildForge.Editor.Configuration.ForgeSettings: void SaveSettings()",
            "BuildForge.Editor.Configuration.ForgeSettings: void SetGlobalPluginConfig<T : class>(string key, T config)",
            "BuildForge.Editor.Core.BuildEnvironment: static bool IsCI { get; }",
            "BuildForge.Editor.Core.BuildEnvironment: static class",
            "BuildForge.Editor.Core.BuildProfileUtility: static UnityEditor.BuildTarget GetBuildTarget(UnityEditor.Build.Profile.BuildProfile profile)",
            "BuildForge.Editor.Core.BuildProfileUtility: static bool IsDedicatedServer(UnityEditor.Build.Profile.BuildProfile profile)",
            "BuildForge.Editor.Core.BuildProfileUtility: static class",
            "BuildForge.Editor.Core.ForgeBuildContext: BuildForge.Editor.Configuration.ForgeProfile ForgeProfile { get; }",
            "BuildForge.Editor.Core.ForgeBuildContext: T GetProperty<T>(string key, T defaultValue = null)",
            "BuildForge.Editor.Core.ForgeBuildContext: UnityEditor.Build.Profile.BuildProfile BuildProfile { get; }",
            "BuildForge.Editor.Core.ForgeBuildContext: UnityEditor.BuildTarget BuildTarget { get; }",
            "BuildForge.Editor.Core.ForgeBuildContext: UnityEditor.BuildTargetGroup BuildTargetGroup { get; }",
            "BuildForge.Editor.Core.ForgeBuildContext: bool DevelopmentBuild { get; }",
            "BuildForge.Editor.Core.ForgeBuildContext: bool IsCI { get; }",
            "BuildForge.Editor.Core.ForgeBuildContext: bool Succeeded { get; }",
            "BuildForge.Editor.Core.ForgeBuildContext: class",
            "BuildForge.Editor.Core.ForgeBuildContext: int? BuildNumber { get; set; }",
            "BuildForge.Editor.Core.ForgeBuildContext: string OutputPath { get; }",
            "BuildForge.Editor.Core.ForgeBuildContext: string Variant { get; }",
            "BuildForge.Editor.Core.ForgeBuildContext: string VariantDefine { get; }",
            "BuildForge.Editor.Core.ForgeBuildContext: void SetProperty(string key, object value)",
            "BuildForge.Editor.Core.ForgeBuildRestoreException: System.Collections.Generic.IReadOnlyList<string> Failures { get; }",
            "BuildForge.Editor.Core.ForgeBuildRestoreException: UnityEditor.Build.Reporting.BuildReport Report { get; }",
            "BuildForge.Editor.Core.ForgeBuildRestoreException: sealed class : System.Exception",
            "BuildForge.Editor.Core.ForgeBuildRunner: static UnityEditor.Build.Reporting.BuildReport RunBuild(BuildForge.Editor.Configuration.ForgeProfile forgeProfile)",
            "BuildForge.Editor.Core.ForgeBuildRunner: static UnityEditor.Build.Reporting.BuildReport RunBuild(BuildForge.Editor.Configuration.ForgeProfile forgeProfile, string variant)",
            "BuildForge.Editor.Core.ForgeBuildRunner: static bool IsForgeBuildInProgress { get; }",
            "BuildForge.Editor.Core.ForgeBuildRunner: static class",
            "BuildForge.Editor.Core.ForgeBuildRunner: static string GetCICommand(BuildForge.Editor.Configuration.ForgeProfile profile, string variant = null)",
            "BuildForge.Editor.Core.ForgePluginAttribute: .ctor()",
            "BuildForge.Editor.Core.ForgePluginAttribute: bool EnabledByDefault { get; set; }",
            "BuildForge.Editor.Core.ForgePluginAttribute: class : System.Attribute",
            "BuildForge.Editor.Core.GitHelper: static class",
            "BuildForge.Editor.Core.GitHelper: static int? RunInt(string arguments, int timeoutMs = 5000)",
            "BuildForge.Editor.Core.GitHelper: static string Run(string arguments, int timeoutMs = 5000)",
            "BuildForge.Editor.Core.IForgeEditorApplicable: default string MergeEditorState(UnityEditor.BuildTargetGroup group, string baselineJson, string expectedJson, string currentJson)",
            "BuildForge.Editor.Core.IForgeEditorApplicable: interface",
            "BuildForge.Editor.Core.IForgeEditorApplicable: string CaptureEditorState(UnityEditor.BuildTargetGroup group)",
            "BuildForge.Editor.Core.IForgeEditorApplicable: void ApplyToEditor(BuildForge.Editor.Configuration.ForgeProfile forgeProfile, UnityEditor.BuildTargetGroup group, string variant)",
            "BuildForge.Editor.Core.IForgeEditorApplicable: void RestoreEditorState(UnityEditor.BuildTargetGroup group, string json)",
            "BuildForge.Editor.Core.IForgeGradleProcessor: interface",
            "BuildForge.Editor.Core.IForgeGradleProcessor: void OnPostGenerateGradleAndroidProject(BuildForge.Editor.Core.ForgeBuildContext context, string path)",
            "BuildForge.Editor.Core.IForgeManifestContributor: interface",
            "BuildForge.Editor.Core.IForgeManifestContributor: void ContributeToManifest(BuildForge.Runtime.BuildManifest manifest, BuildForge.Editor.Core.ForgeBuildContext context)",
            "BuildForge.Editor.Core.IForgePlugin: bool IsApplicable(UnityEditor.Build.Profile.BuildProfile profile)",
            "BuildForge.Editor.Core.IForgePlugin: default System.Collections.Generic.IReadOnlyList<string> Validate(BuildForge.Editor.Configuration.ForgeProfile forgeProfile, string variant)",
            "BuildForge.Editor.Core.IForgePlugin: default bool? IsEnabled(BuildForge.Editor.Configuration.ForgeProfile forgeProfile)",
            "BuildForge.Editor.Core.IForgePlugin: default int Order { get; }",
            "BuildForge.Editor.Core.IForgePlugin: default string DescribeBuild(BuildForge.Editor.Configuration.ForgeProfile forgeProfile, bool isCI, string variant)",
            "BuildForge.Editor.Core.IForgePlugin: default string Description { get; }",
            "BuildForge.Editor.Core.IForgePlugin: default string NotApplicableReason(UnityEditor.Build.Profile.BuildProfile profile)",
            "BuildForge.Editor.Core.IForgePlugin: default void OnDrawProfileGUI(BuildForge.Editor.Configuration.ForgeProfile forgeProfile)",
            "BuildForge.Editor.Core.IForgePlugin: default void OnDrawSettingsGUI()",
            "BuildForge.Editor.Core.IForgePlugin: default void SetEnabled(BuildForge.Editor.Configuration.ForgeProfile forgeProfile, bool enabled)",
            "BuildForge.Editor.Core.IForgePlugin: interface",
            "BuildForge.Editor.Core.IForgePlugin: string DisplayName { get; }",
            "BuildForge.Editor.Core.IForgePlugin: void OnPostBuild(BuildForge.Editor.Core.ForgeBuildContext context)",
            "BuildForge.Editor.Core.IForgePlugin: void OnPreBuild(BuildForge.Editor.Core.ForgeBuildContext context)",
            "BuildForge.Runtime.BuildManifest: .ctor()",
            "BuildForge.Runtime.BuildManifest: BuildForge.Runtime.ManifestSection GetSection(string sectionName)",
            "BuildForge.Runtime.BuildManifest: System.Collections.Generic.IReadOnlyList<BuildForge.Runtime.ManifestSection> PluginSections { get; }",
            "BuildForge.Runtime.BuildManifest: bool DevelopmentBuild { get; }",
            "BuildForge.Runtime.BuildManifest: class",
            "BuildForge.Runtime.BuildManifest: static BuildForge.Runtime.BuildManifest FromJson(string json)",
            "BuildForge.Runtime.BuildManifest: string BuildForgeVersion { get; }",
            "BuildForge.Runtime.BuildManifest: string BuildNumber { get; }",
            "BuildForge.Runtime.BuildManifest: string BuildProfile { get; }",
            "BuildForge.Runtime.BuildManifest: string BuildTarget { get; }",
            "BuildForge.Runtime.BuildManifest: string BuildTargetGroup { get; }",
            "BuildForge.Runtime.BuildManifest: string BuildTimestamp { get; }",
            "BuildForge.Runtime.BuildManifest: string BuildVariant { get; }",
            "BuildForge.Runtime.BuildManifest: string ManagedCodeVariant { get; }",
            "BuildForge.Runtime.BuildManifest: string ProductName { get; }",
            "BuildForge.Runtime.BuildManifest: string ProductVersion { get; }",
            "BuildForge.Runtime.BuildManifest: string ToJson(bool prettyPrint = true)",
            "BuildForge.Runtime.BuildManifest: string UnityVersion { get; }",
            "BuildForge.Runtime.BuildManifest: void AddSection(BuildForge.Runtime.ManifestSection section)",
            "BuildForge.Runtime.BuildManifestLoader: static BuildForge.Runtime.BuildManifest Load()",
            "BuildForge.Runtime.BuildManifestLoader: static UnityEngine.Awaitable<BuildForge.Runtime.BuildManifest> LoadAsync()",
            "BuildForge.Runtime.BuildManifestLoader: static class",
            "BuildForge.Runtime.BuildManifestLoader: static void ClearCache()",
            "BuildForge.Runtime.ManifestEntry: .ctor(string key, string value)",
            "BuildForge.Runtime.ManifestEntry: class",
            "BuildForge.Runtime.ManifestEntry: string Key { get; }",
            "BuildForge.Runtime.ManifestEntry: string Value { get; }",
            "BuildForge.Runtime.ManifestSection: .ctor(string name)",
            "BuildForge.Runtime.ManifestSection: System.Collections.Generic.IReadOnlyList<BuildForge.Runtime.ManifestEntry> Entries { get; }",
            "BuildForge.Runtime.ManifestSection: class",
            "BuildForge.Runtime.ManifestSection: string Get(string key)",
            "BuildForge.Runtime.ManifestSection: string Name { get; }",
            "BuildForge.Runtime.ManifestSection: void Add(string key, string value)",
        };

        [Test]
        public void PublicTypes_DeclareExactlyTheApprovedMembers()
        {
            var actual = ApiMembers(typeof(ForgeBuildRunner).Assembly).Concat(ApiMembers(typeof(BuildManifest).Assembly))
                .OrderBy(m => m, StringComparer.Ordinal).ToList();
            var missing = ExpectedMembers.Except(actual).ToList();
            var added = actual.Except(ExpectedMembers).ToList();
            Assert.IsTrue(missing.Count == 0 && added.Count == 0,
                "The public members changed.\nNo longer there:\n  " + string.Join("\n  ", missing) +
                "\nNew or changed:\n  " + string.Join("\n  ", added) +
                "\nAll, as the list should read if the change is deliberate:\n" +
                string.Join("\n", actual.Select(m => "            \"" + m.Replace("\"", "\\\"") + "\",")));
        }

        const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                      BindingFlags.Static | BindingFlags.DeclaredOnly;

        static bool Visible(MethodBase m) => m != null && (m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly);

        static bool Visible(FieldInfo f) => f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly;

        // One line per type, with its kind and base, and one per member it declares, so a renamed,
        // retyped or newly public member, or a renamed parameter, which breaks named arguments,
        // shows up; inherited members belong to their own type.
        static IEnumerable<string> ApiMembers(Assembly assembly)
        {
            foreach (var type in assembly.GetExportedTypes())
            {
                var name = type.FullName;
                yield return $"{name}: {Kind(type)}";
                foreach (var c in type.GetConstructors(Declared).Where(Visible))
                    yield return $"{name}: {Access(c)}{(c.IsStatic ? "static " : "")}.ctor({Parameters(c)})";
                // Accessors are listed with their property or event; operators are special names too.
                foreach (var m in type.GetMethods(Declared).Where(m => Visible(m) && (!m.IsSpecialName || m.Name.StartsWith("op_"))))
                    yield return $"{name}: {Access(m)}{Modifiers(m, type)}{TypeName(m.ReturnType)} {m.Name}{Generic(m)}({Parameters(m)})";
                foreach (var pr in type.GetProperties(Declared))
                {
                    var get = pr.GetGetMethod(true);
                    var set = pr.GetSetMethod(true);
                    if (!Visible(get) && !Visible(set))
                        continue;
                    var any = Visible(get) ? get : set;
                    var index = pr.GetIndexParameters();
                    yield return $"{name}: {Modifiers(any, type)}{TypeName(pr.PropertyType)} {pr.Name}" +
                                 (index.Length > 0 ? $"[{string.Join(", ", index.Select(i => TypeName(i.ParameterType)))}]" : "") +
                                 " {" + (Visible(get) ? $" {Access(get)}get;" : "") + (Visible(set) ? $" {Access(set)}set;" : "") + " }";
                }
                foreach (var f in type.GetFields(Declared).Where(Visible))
                {
                    var kind = f.IsLiteral ? "const " : (f.IsStatic ? "static " : "") + (f.IsInitOnly ? "readonly " : "");
                    if (type.IsEnum && f.IsLiteral)
                        kind = "value ";
                    yield return $"{name}: {Access(f)}{kind}{TypeName(f.FieldType)} {f.Name}" +
                                 (f.IsLiteral && !type.IsEnum ? $" = {Literal(f.GetRawConstantValue())}" : "");
                }
                foreach (var e in type.GetEvents(Declared).Where(e => Visible(e.GetAddMethod(true))))
                    yield return $"{name}: {Access(e.GetAddMethod(true))}{(e.GetAddMethod(true).IsStatic ? "static " : "")}event {TypeName(e.EventHandlerType)} {e.Name}";
            }
        }

        static string Kind(Type t)
        {
            if (t.IsInterface) return "interface" + Interfaces(t);
            if (t.IsEnum) return "enum";
            if (t.IsValueType) return "struct" + Interfaces(t);
            var kind = t.IsAbstract && t.IsSealed ? "static class" : t.IsAbstract ? "abstract class" : t.IsSealed ? "sealed class" : "class";
            var bases = new List<string>();
            if (t.BaseType != null && t.BaseType != typeof(object))
                bases.Add(TypeName(t.BaseType));
            bases.AddRange(t.GetInterfaces().Where(i => t.BaseType == null || !i.IsAssignableFrom(t.BaseType)).Select(TypeName).OrderBy(n => n, StringComparer.Ordinal));
            return kind + (bases.Count > 0 ? " : " + string.Join(", ", bases) : "");
        }

        static string Interfaces(Type t)
        {
            var list = t.GetInterfaces().Select(TypeName).OrderBy(n => n, StringComparer.Ordinal).ToList();
            return list.Count > 0 ? " : " + string.Join(", ", list) : "";
        }

        static string Access(MethodBase m) => m.IsPublic ? "" : "protected ";

        static string Access(FieldInfo f) => f.IsPublic ? "" : "protected ";

        static string Modifiers(MethodInfo m, Type declaring)
        {
            if (m.IsStatic) return "static ";
            if (declaring.IsInterface) return m.IsAbstract ? "" : "default ";
            if (m.IsAbstract) return "abstract ";
            if (m.IsVirtual && !m.IsFinal) return m.GetBaseDefinition() != m ? "override " : "virtual ";
            if (m.IsVirtual && m.GetBaseDefinition() != m) return "sealed override ";
            return "";
        }

        static string Generic(MethodInfo m) => m.IsGenericMethodDefinition
            ? "<" + string.Join(", ", m.GetGenericArguments().Select(a => a.Name + Constraints(a))) + ">"
            : "";

        static string Constraints(Type argument)
        {
            var list = new List<string>();
            var attributes = argument.GenericParameterAttributes;
            if ((attributes & GenericParameterAttributes.ReferenceTypeConstraint) != 0) list.Add("class");
            if ((attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0) list.Add("struct");
            list.AddRange(argument.GetGenericParameterConstraints().Where(c => c != typeof(ValueType)).Select(TypeName));
            if ((attributes & GenericParameterAttributes.DefaultConstructorConstraint) != 0 &&
                (attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) == 0) list.Add("new()");
            return list.Count > 0 ? " : " + string.Join(", ", list) : "";
        }

        static string Parameters(MethodBase m) => string.Join(", ", m.GetParameters().Select(p =>
            (p.IsOut ? "out " : p.ParameterType.IsByRef ? "ref " : "") +
            (p.GetCustomAttributes(typeof(ParamArrayAttribute), false).Length > 0 ? "params " : "") +
            TypeName(p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType) + " " + p.Name +
            (p.HasDefaultValue ? " = " + Literal(p.DefaultValue) : "")));

        static string Literal(object value) => value switch
        {
            null => "null",
            string text => "\"" + text + "\"",
            bool flag => flag ? "true" : "false",
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
        };

        static string TypeName(Type t)
        {
            if (t.IsGenericParameter) return t.Name;
            if (t.IsArray) return TypeName(t.GetElementType()) + "[]";
            var nullable = Nullable.GetUnderlyingType(t);
            if (nullable != null) return TypeName(nullable) + "?";
            if (t == typeof(void)) return "void";
            if (t == typeof(string)) return "string";
            if (t == typeof(bool)) return "bool";
            if (t == typeof(int)) return "int";
            if (t == typeof(long)) return "long";
            if (t == typeof(float)) return "float";
            if (t == typeof(double)) return "double";
            if (t == typeof(object)) return "object";
            var name = t.IsNested ? TypeName(t.DeclaringType) + "." + t.Name : t.Namespace + "." + t.Name;
            if (!t.IsGenericType) return name;
            var tick = name.IndexOf('`');
            return (tick >= 0 ? name.Substring(0, tick) : name) + "<" + string.Join(", ", t.GetGenericArguments().Select(TypeName)) + ">";
        }

        [Test]
        public void ForgeBuildContext_HasNoPublicConstructor()
        {
            // Plugins receive contexts from the runner; they must not be able to
            // fabricate them.
            CollectionAssert.IsEmpty(
                typeof(ForgeBuildContext).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        }
    }
}
