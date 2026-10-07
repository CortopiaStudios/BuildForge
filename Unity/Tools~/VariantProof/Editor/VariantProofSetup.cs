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
        native.name = "Proof";
        native.scenes = new[] { new EditorBuildSettingsScene("Assets/Proof.unity", true) };
        native.overrideGlobalScenes = true;
        native.scriptingDefines = new[] { "BUILD_PROFILE_PROOF" };
        var forge = AssetDatabase.LoadAssetAtPath<ForgeProfile>("Assets/ProofForge.asset");
        if (forge == null)
        {
            forge = ScriptableObject.CreateInstance<ForgeProfile>();
            AssetDatabase.CreateAsset(forge, "Assets/ProofForge.asset");
        }
        var fp = new SerializedObject(forge);
        fp.FindProperty("buildProfile").objectReferenceValue = native;
        fp.FindProperty("outputPath").stringValue = "Builds/{Variant}/Proof";
        fp.ApplyModifiedPropertiesWithoutUndo();
        var settings = new SerializedObject(ForgeSettings.instance);
        var names = settings.FindProperty("buildVariants");
        names.arraySize = 3;
        var variants = new[] { "Development", "China", "ChinaDevelopment" };
        for (var i = 0; i < 3; i++) names.GetArrayElementAtIndex(i).stringValue = variants[i];
        var rules = settings.FindProperty("variantRules"); rules.arraySize = 4;
        for (var i = 0; i < 4; i++)
        {
            var rule = rules.GetArrayElementAtIndex(i);
            rule.FindPropertyRelative("variant").stringValue = i == 0 ? "Default" : variants[i - 1];
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
        EditorUtility.SetDirty(native);
        AssetDatabase.SaveAssets();
    }
}
