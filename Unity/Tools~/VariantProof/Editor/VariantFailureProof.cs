using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class VariantFailureProof
{
    public static bool Inject;
    public static bool ExpectedDevelopment;
    public static bool CleanedUp;
    public static bool InjectPlayerFailure;
    public static UnityEngine.Object TemporaryPreloadedAsset;
    static readonly Type EditorState = typeof(ForgeBuildRunner).Assembly.GetType("BuildForge.Editor.Core.ForgeEditorState");
    const BindingFlags Flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    public static void Run()
    {
        var forge = AssetDatabase.LoadAssetAtPath<ForgeProfile>("Assets/ProofForge.asset");
        CheckFailure(forge, "Development");
        EditorState.GetMethod("Apply", Flags).Invoke(null, new object[] { forge, "Development" });
        try { CheckFailure(forge, null); }
        finally { EditorState.GetMethod("RevertCore", Flags).Invoke(null, null); }
        CheckPlayerFailure(forge);
        File.WriteAllText("failure-proof.json", "{\"developmentFailureRestored\":true,\"releaseFailureRestoredAppliedDevelopment\":true,\"cleanupRan\":true,\"playerFailurePreloadedAssetsRestored\":true}");
    }

    static void CheckPlayerFailure(ForgeProfile forge)
    {
        const string folder = "Assets/BuildForgePreloadProof";
        if (AssetDatabase.IsValidFolder(folder)) throw new Exception("Preload proof folder already exists");
        var original = PlayerSettings.GetPreloadedAssets();
        AssetDatabase.CreateFolder("Assets", "BuildForgePreloadProof");
        try
        {
            foreach (var name in new[] { "first", "second", "temporary" })
                File.WriteAllText($"{folder}/{name}.txt", name);
            AssetDatabase.Refresh();
            var expected = new UnityEngine.Object[] {
                AssetDatabase.LoadAssetAtPath<TextAsset>($"{folder}/second.txt"),
                AssetDatabase.LoadAssetAtPath<TextAsset>($"{folder}/first.txt") };
            TemporaryPreloadedAsset = AssetDatabase.LoadAssetAtPath<TextAsset>($"{folder}/temporary.txt");
            PlayerSettings.SetPreloadedAssets(expected);
            AssetDatabase.SaveAssets();
            var before = File.ReadAllBytes("ProjectSettings/ProjectSettings.asset");
            InjectPlayerFailure = true;
            var report = ForgeBuildRunner.RunBuild(forge, "Development");
            if (report == null || report.summary.result != BuildResult.Failed)
                throw new Exception("Expected player preprocess failure was not reached");
            if (!expected.SequenceEqual(PlayerSettings.GetPreloadedAssets()) ||
                !before.SequenceEqual(File.ReadAllBytes("ProjectSettings/ProjectSettings.asset")))
                throw new Exception("Player failure did not restore legitimate preloaded assets in order");
        }
        finally
        {
            InjectPlayerFailure = false;
            TemporaryPreloadedAsset = null;
            PlayerSettings.SetPreloadedAssets(original);
            AssetDatabase.SaveAssets();
            AssetDatabase.DeleteAsset(folder);
        }
    }
    static void CheckFailure(ForgeProfile forge, string variant)
    {
        AssetDatabase.SaveAssets();
        var path = AssetDatabase.GetAssetPath(forge.BuildProfile);
        var before = File.ReadAllBytes(path);
        var product = PlayerSettings.productName;
        var project = File.ReadAllBytes("ProjectSettings/ProjectSettings.asset");
        Inject = true;
        ExpectedDevelopment = variant == "Development";
        CleanedUp = false;
        try
        {
            try
            {
                ForgeBuildRunner.RunBuild(forge, variant);
                throw new Exception("Expected injected failure was not reached");
            }
            catch (InvalidOperationException e) when (e.Message == "Injected variant failure") { }
            if (!CleanedUp || !before.SequenceEqual(File.ReadAllBytes(path)) || product != PlayerSettings.productName
                || !project.SequenceEqual(File.ReadAllBytes("ProjectSettings/ProjectSettings.asset")))
                throw new Exception("Variant failure did not restore saved configuration");
        }
        finally { Inject = false; }
    }
}

public sealed class InjectPlayerPreloadFailure : IPreprocessBuildWithReport
{
    // Run after XR's native preprocessors, then abort before their postprocessors.
    public int callbackOrder => 10000;
    public void OnPreprocessBuild(BuildReport report)
    {
        if (!VariantFailureProof.InjectPlayerFailure) return;
        PlayerSettings.SetPreloadedAssets(PlayerSettings.GetPreloadedAssets()
            .Concat(new[] { VariantFailureProof.TemporaryPreloadedAsset }).ToArray());
        throw new BuildFailedException("Injected player preload failure");
    }
}

[ForgePlugin]
public sealed class InjectVariantFailure : IForgePlugin
{
    public string DisplayName => "Variant failure proof";
    public int Order => 750;
    public bool IsApplicable(BuildProfile profile) => true;
    public bool? IsEnabled(ForgeProfile profile) => VariantFailureProof.Inject;
    public void OnPreBuild(ForgeBuildContext context)
    {
        if (context.DevelopmentBuild != VariantFailureProof.ExpectedDevelopment
            || context.BuildProfile.scriptingDefines.Contains("DEVELOPMENT") != VariantFailureProof.ExpectedDevelopment)
            throw new Exception("Plugin received unresolved variant settings");
        throw new InvalidOperationException("Injected variant failure");
    }
    public void OnPostBuild(ForgeBuildContext context) => VariantFailureProof.CleanedUp = true;
}
