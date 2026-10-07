using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditorInternal;
using UnityEngine;

namespace BuildForge.Editor.UI
{
    /// <summary>
    /// Integrates Build Forge settings into Unity's Project Settings window.
    /// </summary>
    internal class ForgeSettingsProvider : SettingsProvider
    {
        SerializedObject serializedSettings;
        ReorderableList variantList;

        // Known optional plugins that live in separate assemblies
        // and require specific packages to compile
        static readonly (string name, string description, string packageId)[] OptionalPlugins = new[]
        {
            ("XR Loaders", "Per-profile XR provider selection", "com.unity.xr.management"),
            ("OpenXR", "Per-profile XR feature and settings overrides", "com.unity.xr.openxr"),
            ("Addressables Stripper", "Strip Addressable groups and labels from builds", "com.unity.addressables"),
            ("Addressables Rebuild", "Rebuild Addressables content before building", "com.unity.addressables"),
        };

        static System.Collections.Generic.Dictionary<string, bool> packageInstallCache;
        static double lastPackageCacheTime;

        ForgeSettingsProvider()
            : base("Project/Build Forge", SettingsScope.Project)
        {
            label = "Build Forge";
            keywords = new[] { "Build Forge", "Build", "Profiles", "Intercept" };
        }

        static bool IsPackageInstalled(string packageId)
        {
            if (packageInstallCache == null ||
                EditorApplication.timeSinceStartup - lastPackageCacheTime > 30)
            {
                packageInstallCache = new System.Collections.Generic.Dictionary<string, bool>();
                foreach (var pkg in UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages())
                    packageInstallCache[pkg.name] = true;
                lastPackageCacheTime = EditorApplication.timeSinceStartup;
            }

            return packageInstallCache.ContainsKey(packageId);
        }

        public override void OnGUI(string searchContext)
        {
            serializedSettings ??= new SerializedObject(ForgeSettings.instance);
            serializedSettings.Update();

            // Project Settings pages get the default (narrow) label width, which
            // truncates labels like "Maintain Build Profile Defines"; Unity's own
            // pages widen it the same way.
            var previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 260f;

            EditorGUILayout.Space(8);

            EditorGUILayout.LabelField("Build Interception", EditorStyles.boldLabel);
            var interceptProp = serializedSettings.FindProperty("interceptBuilds");
            EditorGUILayout.PropertyField(interceptProp,
                new UnityEngine.GUIContent("Intercept Builds",
                    "When enabled, builds started outside of Build Forge will be blocked."));

            EditorGUILayout.Space(8);

            EditorGUILayout.LabelField("Build Manifest", EditorStyles.boldLabel);
            var manifestProp = serializedSettings.FindProperty("writeBuildManifest");
            EditorGUILayout.PropertyField(manifestProp,
                new UnityEngine.GUIContent("Write Build Manifest",
                    "When enabled, Build Forge writes a JSON manifest to the build for runtime reading."));
            var logManifestProp = serializedSettings.FindProperty("logManifestContents");
            EditorGUILayout.PropertyField(logManifestProp,
                new UnityEngine.GUIContent("Log Manifest Contents",
                    "When enabled, logs the full manifest JSON to the console during the build."));

            EditorGUILayout.Space(8);

            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            var mangleProp = serializedSettings.FindProperty("mangleProductName");
            EditorGUILayout.PropertyField(mangleProp,
                new UnityEngine.GUIContent("Sanitize Product Name",
                    "Replace spaces, special characters, and other problematic characters " +
                    "in the {ProjectName} placeholder for safe filenames."));
            var revealProp = serializedSettings.FindProperty("revealBuildInFileBrowser");
            EditorGUILayout.PropertyField(revealProp,
                new UnityEngine.GUIContent("Reveal Build In File Browser",
                    "After a successful build started from the editor, show the built file in Explorer, " +
                    "Finder, or the Linux file manager, like Unity's own Build button. Never in CI or batch mode."));

            EditorGUILayout.Space(8);

            EditorGUILayout.LabelField("Profiles", EditorStyles.boldLabel);
            var saveOnEditProp = serializedSettings.FindProperty("saveProfilesOnEdit");
            EditorGUILayout.PropertyField(saveOnEditProp,
                new UnityEngine.GUIContent("Save Profiles On Edit",
                    "Write Build Forge settings to disk right after they are edited in the Inspector, " +
                    "so the Build Profile's .asset file always matches the Inspector. When off, Unity saves them on " +
                    "Save Project, reload, or quit like any other asset."));
            var definesProp = serializedSettings.FindProperty("maintainBuildProfileDefines");
            EditorGUILayout.PropertyField(definesProp,
                new UnityEngine.GUIContent("Maintain Build Profile Defines",
                    "Keep a BUILD_PROFILE_<NAME> scripting define in each referenced Unity Build Profile's " +
                    "Scripting Defines list (written when Build Forge touches the profile, replaced after a " +
                    "rename), so code can compile conditionally per profile with #if BUILD_PROFILE_<NAME>."));

            EditorGUILayout.Space(8);

            EditorGUILayout.LabelField("Play Mode", EditorStyles.boldLabel);
            var playModeProp = serializedSettings.FindProperty("playModeFollowsAppliedProfile");
            EditorGUILayout.PropertyField(playModeProp,
                new UnityEngine.GUIContent("Play Mode Follows Applied Profile",
                    "Play Mode runs on Standalone's XR settings. When enabled, applying a profile for another " +
                    "platform, such as Android, also gives Standalone that platform's XR loaders, Initialize XR on " +
                    "Startup and OpenXR feature states. Revert to Baseline puts Standalone back; builds are unaffected."));

            EditorGUILayout.Space(8);

            // Drawn like Unity's own lists in Player Settings (Graphics APIs): a
            // plain header, no foldout or count field, elements without labels.
            var variantsProp = serializedSettings.FindProperty("buildVariants");
            if (variantList == null || variantList.serializedProperty.serializedObject != serializedSettings)
            {
                variantList = new ReorderableList(serializedSettings, variantsProp, true, true, true, true);
                variantList.drawHeaderCallback = rect => EditorGUI.LabelField(rect,
                    new UnityEngine.GUIContent("Build Variants",
                        "Project-wide names. Each selects BUILD_VARIANT_<NAME> and any configured variant rules " +
                        "for development mode, additional defines, product marking and plugins. " +
                        "Chosen per build in the build window or with -forgeVariant on the command line."));
                variantList.drawElementCallback = (rect, index, active, focused) =>
                {
                    rect.y += 1f;
                    rect.height = EditorGUIUtility.singleLineHeight;
                    EditorGUI.PropertyField(rect, variantsProp.GetArrayElementAtIndex(index), UnityEngine.GUIContent.none);
                };
                variantList.onAddCallback = list =>
                {
                    list.serializedProperty.arraySize++;
                    list.serializedProperty.GetArrayElementAtIndex(list.serializedProperty.arraySize - 1).stringValue = "";
                    list.index = list.serializedProperty.arraySize - 1;
                };
            }
            variantList.DoLayoutList();
            EditorGUILayout.PropertyField(serializedSettings.FindProperty("variantRules"),
                new GUIContent("Variant Rules", "Optional development, scripting-define, product-name, version and build configuration rules. Use Default for the release build."), true);
            var variantWarning = BuildVariants.ConfigurationError();
            if (variantWarning != null)
                EditorGUILayout.HelpBox(variantWarning, MessageType.Error);
            if (ForgeSettings.instance.BuildVariants.Count > 0)
            {
                var markProp = serializedSettings.FindProperty("markVariantBuilds");
                EditorGUILayout.PropertyField(markProp,
                    new UnityEngine.GUIContent("Mark Variant Builds",
                        "Suffix a variant build's product name with the variant, e.g. \"Game (Internal)\", for the " +
                        "duration of the build: the app display name, window title, file name ({ProjectName}) and the " +
                        "manifest's ProductName show it, so an internal build is hard to distribute by mistake. The " +
                        "default build is never suffixed. The application identifier is not changed, so an internal " +
                        "build replaces the installed public one on a device rather than sitting beside it."));
            }
            EditorGUILayout.Space(8);

            EditorGUILayout.LabelField("Installed Plugins", EditorStyles.boldLabel);
            var plugins = ForgePluginRegistry.GetPlugins();
            if (plugins.Count == 0)
            {
                EditorGUILayout.LabelField("No plugins discovered.", EditorStyles.miniLabel);
            }
            else
            {
                foreach (var plugin in plugins)
                {
                    var typeName = plugin.GetType().FullName;
                    var safeDefault = ForgePluginRegistry.IsSafeToDefaultEnable(plugin);
                    var isDisabled = ForgeSettings.instance.IsPluginDisabledWithDefault(typeName, safeDefault);
                    var newEnabled = EditorGUILayout.Toggle(
                        new UnityEngine.GUIContent(plugin.DisplayName, plugin.Description), !isDisabled);
                    if (newEnabled == isDisabled)
                    {
                        ForgeSettings.instance.SetPluginDisabled(typeName, !newEnabled);
                    }

                    if (!isDisabled)
                    {
                        EditorGUI.indentLevel++;
                        plugin.OnDrawSettingsGUI();
                        EditorGUI.indentLevel--;
                    }
                }
            }

            // Show optional plugins that require packages not yet installed
            var activeNames = new System.Collections.Generic.HashSet<string>();
            foreach (var plugin in plugins)
                activeNames.Add(plugin.DisplayName);

            bool hasUnavailable = false;
            foreach (var (name, _, packageId) in OptionalPlugins)
            {
                if (!activeNames.Contains(name) && !IsPackageInstalled(packageId))
                {
                    hasUnavailable = true;
                    break;
                }
            }

            if (hasUnavailable)
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("Available Plugins (require package install)", EditorStyles.boldLabel);

                // Deduplicate by package
                var shownPackages = new System.Collections.Generic.HashSet<string>();
                foreach (var (name, description, packageId) in OptionalPlugins)
                {
                    if (activeNames.Contains(name) || IsPackageInstalled(packageId))
                        continue;
                    if (!shownPackages.Add(packageId))
                        continue;

                    // Collect all plugin names for this package
                    var pluginNames = new System.Collections.Generic.List<string>();
                    foreach (var (n, _, pid) in OptionalPlugins)
                    {
                        if (pid == packageId)
                            pluginNames.Add(n);
                    }

                    EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                    EditorGUILayout.BeginVertical();
                    EditorGUILayout.LabelField(string.Join(", ", pluginNames), EditorStyles.boldLabel);
                    EditorGUILayout.LabelField($"Requires: {packageId}", EditorStyles.miniLabel);
                    EditorGUILayout.EndVertical();
                    if (GUILayout.Button("Install", GUILayout.Width(60), GUILayout.ExpandWidth(false)))
                    {
                        Client.Add(packageId);
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }

            if (serializedSettings.ApplyModifiedProperties())
            {
                ForgeSettings.instance.SaveSettings();
            }

            EditorGUILayout.Space(10);

            EditorGUIUtility.labelWidth = previousLabelWidth;
        }

        [SettingsProvider]
        static SettingsProvider CreateProvider()
        {
            return new ForgeSettingsProvider();
        }
    }
}
