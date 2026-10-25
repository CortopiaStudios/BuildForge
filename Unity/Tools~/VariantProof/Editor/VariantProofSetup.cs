using System;
using System.Reflection;
using BuildForge.Editor.Configuration;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class VariantProofSetup
{
    public static void Prepare()
    {
        const string path = "Assets/Proof.asset";
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, "Assets/Proof.unity");
        var method = typeof(BuildProfile).GetMethod("CreateInstance", BindingFlags.Static | BindingFlags.NonPublic,
            null, new[] { typeof(BuildTarget), typeof(StandaloneBuildSubtarget) }, null);
        var native = AssetDatabase.LoadAssetAtPath<BuildProfile>(path);
        if (native == null)
        {
            native = (BuildProfile)method.Invoke(null, new object[] { BuildTarget.StandaloneWindows64, StandaloneBuildSubtarget.Player });
            AssetDatabase.CreateAsset(native, path);
        }
        // A second profile, without Build Forge settings, for Unity to switch to on its own.
        if (AssetDatabase.LoadAssetAtPath<BuildProfile>("Assets/Other.asset") == null)
        {
            var other = (BuildProfile)method.Invoke(null, new object[] { BuildTarget.StandaloneWindows64, StandaloneBuildSubtarget.Player });
            other.scenes = new[] { new EditorBuildSettingsScene("Assets/Proof.unity", true) };
            other.overrideGlobalScenes = true;
            AssetDatabase.CreateAsset(other, "Assets/Other.asset");
        }
        native.name = "Proof";
        native.scenes = new[] { new EditorBuildSettingsScene("Assets/Proof.unity", true) };
        native.overrideGlobalScenes = true;
        native.scriptingDefines = new[] { "BUILD_PROFILE_PROOF" };
        // Stored inside the Build Profile, as Assets > Build Forge > Add Build Forge Settings does.
        var forge = (ForgeProfile)typeof(ForgeProfile).Assembly.GetType("BuildForge.Editor.Core.ForgeProfileEmbedding")
            .GetMethod("Add", BindingFlags.Static | BindingFlags.Public).Invoke(null, new object[] { native });
        var fp = new SerializedObject(forge);
        fp.FindProperty("outputPath").stringValue = "Builds/{Variant}/Proof";
        fp.ApplyModifiedPropertiesWithoutUndo();
        var settings = new SerializedObject(ForgeSettings.instance);
        var names = settings.FindProperty("buildVariants");
        names.arraySize = 4;
        var variants = new[] { "Default", "Development", "China", "ChinaDevelopment" };
        for (var i = 0; i < 4; i++) names.GetArrayElementAtIndex(i).stringValue = variants[i];
        var rules = settings.FindProperty("variantRules"); rules.arraySize = 4;
        for (var i = 0; i < 4; i++)
        {
            var rule = rules.GetArrayElementAtIndex(i);
            rule.FindPropertyRelative("variant").stringValue = variants[i];
            rule.FindPropertyRelative("developmentBuild").enumValueIndex = i == 1 || i == 3 ? 2 : 1;
            rule.FindPropertyRelative("markProductName").enumValueIndex = i == 1 || i == 3 ? 2 : 1;
            var defines = rule.FindPropertyRelative("scriptingDefines");
            defines.arraySize = i == 0 ? 0 : i == 3 ? 2 : 1;
            if (i > 0) defines.GetArrayElementAtIndex(0).stringValue = i == 2 ? "REGION_CHINA" : "DEVELOPMENT";
            if (i == 3) defines.GetArrayElementAtIndex(1).stringValue = "REGION_CHINA";
        }
        settings.ApplyModifiedPropertiesWithoutUndo();
        ForgeSettings.instance.SaveSettings();
        PlayerSettings.productName = "Variant Proof";
        PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
        // With -proofProfilePlayerSettings the profile gets its own copy of these, so the builds restore
        // them in the profile: an embedded YAML copy up to 6000.6, a sub-asset from 6000.7.
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-proofProfilePlayerSettings") >= 0)
            typeof(BuildProfile).Assembly.GetType("UnityEditor.Build.Profile.BuildProfileModuleUtil")
                .GetMethod("CreatePlayerSettingsFromGlobal", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Invoke(null, new object[] { native });
        EditorUtility.SetDirty(native);
        AssetDatabase.SaveAssets();
    }
}
