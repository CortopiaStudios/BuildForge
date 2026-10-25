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
    public static bool InjectRestoreFailure;
    public static bool UnreadablePreBuild;
    public static UnreadableProofException UnreadableThrown;
    public static bool NamelessHooks;
    public static bool NamelessPreBuildRan;
    public static bool NamelessPostBuildRan;
    public static bool UnreadableCleanup;
    public static bool UnreadableEditorCleanup;
    public static string EditorCleanupSetting = "base";
    public static bool UnfinishedApply;
    public static bool UnfinishedApplyRestoreFails;
    // In a file, so that it outlasts the editor between the runs of the Activate check.
    const string UnfinishedApplyFile = "proof-unfinished-apply.txt";
    public static string UnfinishedApplySetting
    {
        get => File.Exists(UnfinishedApplyFile) ? File.ReadAllText(UnfinishedApplyFile) : "base";
        set => File.WriteAllText(UnfinishedApplyFile, value);
    }
    // The run that leaves an Apply unfinished for the next runs passes this.
    public static bool UnfinishedApplyArg => Environment.GetCommandLineArgs().Contains("-proofUnfinishedApply");
    public static UnityEngine.Object TemporaryPreloadedAsset;
    static readonly Type EditorState = typeof(ForgeBuildRunner).Assembly.GetType("BuildForge.Editor.Core.ForgeEditorState");
    static readonly Type Persistence = typeof(ForgeBuildRunner).Assembly.GetType("BuildForge.Editor.Core.PlayerSettingsPersistence");
    static readonly Type Marker = typeof(ForgeBuildRunner).Assembly.GetType("BuildForge.Editor.Core.InterruptedBuildMarker");
    const BindingFlags Flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    public static void Run()
    {
        var forge = AssetDatabase.LoadAssetAtPath<BuildProfile>("Assets/Proof.asset").GetComponent<ForgeProfile>();
        // First, because its Apply refuses while scripts compile: a revert that removes a variant
        // define from the active profile, as the checks below do, requests a compilation that a
        // single run like this one never gets to.
        CheckActivateWhileApplyUnfinished(forge);
        CheckFailure(forge, "Development");
        EditorState.GetMethod("Apply", Flags).Invoke(null, new object[] { forge, "Development" });
        try { CheckFailure(forge, "Default"); }
        finally { EditorState.GetMethod("RevertCore", Flags).Invoke(null, null); }
        CheckPlayerFailure(forge);
        CheckRestoreFailure(forge);
        CheckUnreadablePreBuildFailure(forge);
        CheckNamelessPlugin(forge);
        CheckUnreadableRestoreFailure(forge);
        CheckUnreadableRestoreFailureWhileApplied(forge);
        File.WriteAllText("failure-proof.json", "{\"developmentFailureRestored\":true,\"releaseFailureRestoredAppliedDevelopment\":true,\"cleanupRan\":true,\"playerFailurePreloadedAssetsRestored\":true,\"builtPlayerRestoreFailureThrown\":true," +
            "\"unreadablePreBuildExceptionKept\":true,\"namelessPluginBuilt\":true,\"unreadableRestoreFailureContained\":true,\"unreadableRestoreFailureKeptAppliedEntry\":true," +
            "\"activateKeptUnfinishedBuild\":true,\"activateKeptUnfinishedApply\":true}");
    }

    static ForgeProfile ProofProfile() => AssetDatabase.LoadAssetAtPath<BuildProfile>("Assets/Proof.asset").GetComponent<ForgeProfile>();
    static string InterruptedError() => (string)EditorState.GetMethod("InterruptedError", Flags).Invoke(null, null);
    static bool IsApplied(ForgeProfile forge) => (bool)EditorState.GetMethod("IsApplied", Flags).Invoke(null, new object[] { forge });

    // Activate of the applied profile while its Apply or a build did not finish: nothing is
    // reverted, so the unfinished settings and their record stay as they are, and the log says so
    // and names them.
    static void CheckActivateKeepsUnfinished(ForgeProfile forge, string interrupted, Func<bool> settingKept)
    {
        var ledger = File.ReadAllBytes("UserSettings/ForgeEditorState.asset");
        var defines = forge.BuildProfile.scriptingDefines;
        var logs = new System.Collections.Generic.List<string>();
        Application.LogCallback collect = (message, stack, type) => logs.Add(message);
        Application.logMessageReceived += collect;
        try { EditorState.GetMethod("Activate", Flags).Invoke(null, new object[] { forge }); }
        catch (TargetInvocationException e) { throw new Exception($"Activate of the applied profile stopped: {e.InnerException?.Message}"); }
        finally { Application.logMessageReceived -= collect; }
        if (!settingKept())
            throw new Exception("Activate changed the settings whose Apply or build did not finish");
        if (!ledger.SequenceEqual(File.ReadAllBytes("UserSettings/ForgeEditorState.asset")) || InterruptedError() != interrupted)
            throw new Exception("Activate changed the record of the unfinished settings");
        if (!defines.SequenceEqual(forge.BuildProfile.scriptingDefines))
            throw new Exception("Activate changed the applied profile's defines");
        if (!IsApplied(forge))
            throw new Exception("Activate dropped the applied profile");
        if (!logs.Any(l => l.Contains("which is still applied") && l.Contains("nothing was reverted, but its Apply or a build did not finish")))
            throw new Exception($"Activate did not say that nothing was reverted: {string.Join(" | ", logs)}");
        if (!logs.Any(l => l.Contains(interrupted)))
            throw new Exception($"Activate did not name the unfinished settings: {string.Join(" | ", logs)}");
    }

    // An Apply that stops and can't undo its own write, as when the editor quits in the middle of
    // it: the profile stays applied with that entry unfinished, Activate of it keeps both, and the
    // next build stops, until Revert to Baseline puts the setting back.
    static void CheckActivateWhileApplyUnfinished(ForgeProfile forge)
    {
        UnfinishedApply = true;
        UnfinishedApplyRestoreFails = true;
        UnfinishedApplySetting = "base";
        try
        {
            try
            {
                EditorState.GetMethod("Apply", Flags).Invoke(null, new object[] { forge, "Development" });
                throw new Exception("Expected the Apply to stop");
            }
            catch (TargetInvocationException e) when (e.InnerException is AggregateException) { }
            var interrupted = InterruptedError();
            if (UnfinishedApplySetting != "partial" || !IsApplied(forge) || interrupted == null || !interrupted.Contains("did not finish"))
                throw new Exception("The stopped Apply did not leave its entry unfinished");
            CheckActivateKeepsUnfinished(forge, interrupted, () => UnfinishedApplySetting == "partial");
            try
            {
                ForgeBuildRunner.RunBuild(forge, "Development");
                throw new Exception("A build ran although the Apply of the applied profile did not finish");
            }
            catch (InvalidOperationException e) when (e.Message.Contains(interrupted)) { }
        }
        finally
        {
            UnfinishedApplyRestoreFails = false;
            EditorState.GetMethod("RevertCore", Flags).Invoke(null, null);
            UnfinishedApply = false;
        }
        if (InterruptedError() != null || IsApplied(forge) || UnfinishedApplySetting != "base")
            throw new Exception("Revert to Baseline did not put back the setting whose Apply did not finish");
        File.Delete(UnfinishedApplyFile);
    }

    // With -proofUnfinishedApply: an Apply of the proof profile that stops and can't undo its own
    // write, so its entry stays unfinished on disk for the next runs.
    public static void LeaveApplyUnfinished()
    {
        var forge = ProofProfile();
        UnfinishedApplySetting = "base";
        try
        {
            EditorState.GetMethod("Apply", Flags).Invoke(null, new object[] { forge, "Development" });
            throw new Exception("Expected the Apply to stop");
        }
        catch (TargetInvocationException e) when (e.InnerException is AggregateException) { }
        if (UnfinishedApplySetting != "partial" || !IsApplied(forge) || InterruptedError() == null)
            throw new Exception("The stopped Apply did not leave its entry unfinished");
    }

    // Started with -activeBuildProfile Assets/Other.asset, as when Unity switches profiles without
    // Build Forge: the proof profile stays applied with its Apply unfinished, and the warning
    // offers to activate it again.
    public static void CheckSwitchedAway()
    {
        var forge = ProofProfile();
        if (BuildProfile.GetActiveBuildProfile() != AssetDatabase.LoadAssetAtPath<BuildProfile>("Assets/Other.asset"))
            throw new Exception("Unity did not switch to the other profile");
        var interrupted = InterruptedError();
        if (!IsApplied(forge) || interrupted == null)
            throw new Exception("The switch changed the applied profile or its unfinished Apply");
        var args = new object[] { null };
        var mismatch = (string)EditorState.GetMethod("GetActiveMismatch", Flags).Invoke(null, args);
        if (mismatch == null || mismatch.IndexOf("activate 'Proof' again", StringComparison.OrdinalIgnoreCase) < 0)
            throw new Exception($"The warning does not offer to activate the applied profile again: {mismatch}");
        File.WriteAllText("proof-interrupted.txt", interrupted);
        File.Copy("UserSettings/ForgeEditorState.asset", "proof-ledger-switched.asset", true);
    }

    // After BuildForge.CommandLine.Activate of the applied profile: Unity is on it again and nothing
    // of the unfinished Apply changed, until Revert to Baseline puts its setting back.
    public static void CheckAfterActivate()
    {
        var forge = ProofProfile();
        if (BuildProfile.GetActiveBuildProfile() != forge.BuildProfile)
            throw new Exception("Activate did not make the applied profile active");
        if (!File.ReadAllBytes("UserSettings/ForgeEditorState.asset").SequenceEqual(File.ReadAllBytes("proof-ledger-switched.asset"))
            || InterruptedError() != File.ReadAllText("proof-interrupted.txt") || !IsApplied(forge))
            throw new Exception("Activate changed the record of the unfinished Apply");
        if (UnfinishedApplySetting != "partial")
            throw new Exception($"Activate changed the setting whose Apply did not finish: '{UnfinishedApplySetting}'");
        EditorState.GetMethod("RevertToBaseline", Flags).Invoke(null, null);
        if (InterruptedError() != null || IsApplied(forge) || UnfinishedApplySetting != "base")
            throw new Exception("Revert to Baseline did not put back the setting whose Apply did not finish");
        foreach (var file in new[] { "proof-interrupted.txt", "proof-ledger-switched.asset", UnfinishedApplyFile })
            File.Delete(file);
        File.WriteAllText("activate-proof.json", "{\"activateKeptUnfinishedApplyAfterUnitySwitched\":true,\"revertedAfterActivate\":true}");
    }

    static void CheckUnfinishedBuildStopsTheNext(ForgeProfile forge, string after, string stopMessage = "did not finish")
    {
        if (ForgeBuildRunner.IsForgeBuildInProgress)
            throw new Exception($"The build still counts as running after {after}");
        if (!File.Exists("UserSettings/ForgeBuildInProgress.json"))
            throw new Exception($"{after} did not keep the unfinished-build record");
        try
        {
            ForgeBuildRunner.RunBuild(forge, "Development");
            throw new Exception($"A build ran although the one with {after} did not finish");
        }
        catch (InvalidOperationException e) when (e.Message.Contains(stopMessage)) { }
    }

    // A pre-build hook's exception whose message can't be read: the build's own catch reads it
    // guarded, so the caller gets that same exception, and the project is put back.
    static void CheckUnreadablePreBuildFailure(ForgeProfile forge)
    {
        AssetDatabase.SaveAssets();
        var path = AssetDatabase.GetAssetPath(forge.BuildProfile);
        var before = File.ReadAllBytes(path);
        var product = PlayerSettings.productName;
        var project = File.ReadAllBytes("ProjectSettings/ProjectSettings.asset");
        UnreadablePreBuild = true;
        try
        {
            UnreadableProofException thrown = null;
            try { ForgeBuildRunner.RunBuild(forge, "Development"); }
            catch (UnreadableProofException e) { thrown = e; }
            if (thrown == null || !ReferenceEquals(thrown, UnreadableThrown))
                throw new Exception("The pre-build hook's exception did not reach the caller");
            if (!before.SequenceEqual(File.ReadAllBytes(path)) || product != PlayerSettings.productName
                || !project.SequenceEqual(File.ReadAllBytes("ProjectSettings/ProjectSettings.asset")))
                throw new Exception("The pre-build failure did not put the project back");
            if (File.Exists("UserSettings/ForgeBuildInProgress.json"))
                throw new Exception("A build that put everything back kept the unfinished-build record");
        }
        finally { UnreadablePreBuild = false; }
    }

    // A plugin whose name can't be read builds like any other: both hooks run, and the build's
    // log lines name it by its type.
    static void CheckNamelessPlugin(ForgeProfile forge)
    {
        var logs = new System.Collections.Generic.List<string>();
        Application.LogCallback collect = (message, stack, type) => logs.Add(message);
        NamelessHooks = true;
        NamelessPreBuildRan = NamelessPostBuildRan = false;
        Application.logMessageReceived += collect;
        try
        {
            var report = ForgeBuildRunner.RunBuild(forge, "Development");
            if (report == null || report.summary.result != BuildResult.Succeeded)
                throw new Exception("The build with a plugin whose name throws did not succeed");
            if (!NamelessPreBuildRan || !NamelessPostBuildRan)
                throw new Exception("The hooks of a plugin whose name throws did not run");
            var type = typeof(InjectNamelessHooks).FullName;
            if (!logs.Contains($"[Build Forge] Running pre-build: {type}") || !logs.Contains($"[Build Forge] Running post-build: {type}"))
                throw new Exception("The build's log lines do not name a plugin whose name throws by its type");
            if (File.Exists("UserSettings/ForgeBuildInProgress.json"))
                throw new Exception("A finished build kept the unfinished-build record");
        }
        finally
        {
            Application.logMessageReceived -= collect;
            NamelessHooks = false;
        }
    }

    // A built player whose cleanup plugin, named by a name that throws, throws an exception whose
    // message throws too: the failure is named by their types, the other restores still run, and
    // the public RunBuild throws once the project is back.
    static void CheckUnreadableRestoreFailure(ForgeProfile forge)
    {
        AssetDatabase.SaveAssets();
        var path = AssetDatabase.GetAssetPath(forge.BuildProfile);
        var before = File.ReadAllBytes(path);
        var product = PlayerSettings.productName;
        var project = File.ReadAllBytes("ProjectSettings/ProjectSettings.asset");
        UnreadableCleanup = true;
        try
        {
            try
            {
                ForgeBuildRunner.RunBuild(forge, "Development");
                throw new Exception("Expected unreadable restore failure was not reported");
            }
            catch (ForgeBuildRestoreException e)
            {
                if (e.Report == null || e.Report.summary.result != BuildResult.Succeeded)
                    throw new Exception("The unreadable restore failure does not carry the built player's report");
                var expected = $"Post-build restore failed in {typeof(InjectUnreadableCleanup).FullName}: {typeof(UnreadableProofException).FullName}";
                if (e.Failures.Count != 1 || e.Failures[0] != expected)
                    throw new Exception($"The unreadable restore failure is not named by its types: {string.Join(" | ", e.Failures)}");
            }
            if (!before.SequenceEqual(File.ReadAllBytes(path)) || product != PlayerSettings.productName
                || !project.SequenceEqual(File.ReadAllBytes("ProjectSettings/ProjectSettings.asset")))
                throw new Exception("The other steps did not put the project back after the unreadable restore failure");
            CheckUnfinishedBuildStopsTheNext(forge, "the unreadable restore failure");
        }
        finally
        {
            UnreadableCleanup = false;
            Marker.GetMethod("Clear", Flags).Invoke(null, null);
        }
    }

    // A restore failure whose message can't be read, from an editor-applicable plugin while a
    // profile is applied: its setting keeps the build's value that the failed restore left, rather
    // than getting the profile's again on top of it, and its entry stays unfinished, which stops
    // the next build before the record of the unfinished build does, until Revert to Baseline
    // puts the setting back.
    static void CheckUnreadableRestoreFailureWhileApplied(ForgeProfile forge)
    {
        UnreadableEditorCleanup = true;
        EditorCleanupSetting = "base";
        try
        {
            EditorState.GetMethod("Apply", Flags).Invoke(null, new object[] { forge, "Development" });
            if (EditorCleanupSetting != "applied")
                throw new Exception("The editor-applicable proof plugin was not applied");
            try
            {
                ForgeBuildRunner.RunBuild(forge, "Development");
                throw new Exception("Expected unreadable restore failure was not reported");
            }
            catch (ForgeBuildRestoreException e)
            {
                var expected = $"Post-build restore failed in Unreadable editor cleanup proof: {typeof(UnreadableProofException).FullName}";
                if (!e.Failures.Contains(expected) || !e.Failures.Any(f => f.StartsWith("Re-applying the editor profile after the build failed")))
                    throw new Exception($"The unreadable restore failure while applied is not reported as expected: {string.Join(" | ", e.Failures)}");
            }
            if (EditorCleanupSetting != "build")
                throw new Exception(EditorCleanupSetting == "applied"
                    ? "The settings were applied again on top of what the failed restore left"
                    : $"The setting no longer holds what the failed restore left: '{EditorCleanupSetting}'");
            var interrupted = (string)EditorState.GetMethod("InterruptedError", Flags).Invoke(null, null);
            if (interrupted == null)
                throw new Exception("The entry of the plugin whose restore failed did not stay unfinished");
            CheckActivateKeepsUnfinished(forge, interrupted, () => EditorCleanupSetting == "build");
            CheckUnfinishedBuildStopsTheNext(forge, "the unreadable restore failure while applied", interrupted);
        }
        finally
        {
            UnreadableEditorCleanup = false;
            Marker.GetMethod("Clear", Flags).Invoke(null, null);
            EditorState.GetMethod("RevertCore", Flags).Invoke(null, null);
        }
        if (EditorState.GetMethod("InterruptedError", Flags).Invoke(null, null) != null)
            throw new Exception("Revert to Baseline did not clear the unfinished entry");
        if (EditorCleanupSetting != "base")
            throw new Exception($"Revert to Baseline did not put back the setting the failed restore left: '{EditorCleanupSetting}'");
    }

    // A player that was built while a step after the build failed: the public RunBuild throws,
    // with the player's report, once the other steps have put the project back.
    static void CheckRestoreFailure(ForgeProfile forge)
    {
        AssetDatabase.SaveAssets();
        var path = AssetDatabase.GetAssetPath(forge.BuildProfile);
        var before = File.ReadAllBytes(path);
        var product = PlayerSettings.productName;
        var project = File.ReadAllBytes("ProjectSettings/ProjectSettings.asset");
        InjectRestoreFailure = true;
        try
        {
            try
            {
                ForgeBuildRunner.RunBuild(forge, "Development");
                throw new Exception("Expected restore failure was not reported");
            }
            catch (ForgeBuildRestoreException e)
            {
                if (e.Report == null || e.Report.summary.result != BuildResult.Succeeded)
                    throw new Exception("The restore failure does not carry the built player's report");
                if (e.Failures.Count != 1 || !e.Failures[0].Contains("Injected restore failure"))
                    throw new Exception("The restore failure does not name the failed step");
            }
            if (!before.SequenceEqual(File.ReadAllBytes(path)) || product != PlayerSettings.productName
                || !project.SequenceEqual(File.ReadAllBytes("ProjectSettings/ProjectSettings.asset")))
                throw new Exception("The other steps did not put the project back after the failed restore");
            // The failed restore keeps the record of a build that did not finish, which stops the next build.
            if (!File.Exists("UserSettings/ForgeBuildInProgress.json"))
                throw new Exception("The failed restore did not keep the unfinished-build record");
            try
            {
                ForgeBuildRunner.RunBuild(forge, "Development");
                throw new Exception("A build ran although the previous one did not finish");
            }
            catch (InvalidOperationException e) when (e.Message.Contains("did not finish")) { }
        }
        finally
        {
            InjectRestoreFailure = false;
            Marker.GetMethod("Clear", Flags).Invoke(null, null);
        }
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
            // As a build saves: SaveAssets alone leaves the embedded copy of a profile's own Player
            // Settings (-ProfilePlayerSettings) as it was, up to 6000.6.
            Persistence.GetMethod("SaveRestoredSettings", Flags, null, Type.EmptyTypes, null).Invoke(null, null);
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
    public bool IsApplicable(BuildProfile buildProfile) => true;
    public bool? IsEnabled(ForgeProfile forgeProfile) => VariantFailureProof.Inject;
    public void OnPreBuild(ForgeBuildContext context)
    {
        if (context.DevelopmentBuild != VariantFailureProof.ExpectedDevelopment
            || context.BuildProfile.scriptingDefines.Contains("DEVELOPMENT") != VariantFailureProof.ExpectedDevelopment)
            throw new Exception("Plugin received unresolved variant settings");
        throw new InvalidOperationException("Injected variant failure");
    }
    public void OnPostBuild(ForgeBuildContext context) => VariantFailureProof.CleanedUp = true;
}

[ForgePlugin]
public sealed class InjectRestoreFailure : IForgePlugin
{
    public string DisplayName => "Restore failure proof";
    public int Order => 760;
    public bool IsApplicable(BuildProfile buildProfile) => true;
    public bool? IsEnabled(ForgeProfile forgeProfile) => VariantFailureProof.InjectRestoreFailure;
    public void OnPreBuild(ForgeBuildContext context) { }
    public void OnPostBuild(ForgeBuildContext context) => throw new InvalidOperationException("Injected restore failure");
}

// An exception whose message and stack trace can't be read, as one that formats a field the
// failure left null.
public sealed class UnreadableProofException : Exception
{
    public override string Message => throw new InvalidOperationException("message unavailable");
    public override string StackTrace => throw new InvalidOperationException("stack trace unavailable");
}

[ForgePlugin]
public sealed class InjectUnreadablePreBuild : IForgePlugin
{
    public string DisplayName => "Unreadable pre-build proof";
    public int Order => 770;
    public bool IsApplicable(BuildProfile buildProfile) => true;
    public bool? IsEnabled(ForgeProfile forgeProfile) => VariantFailureProof.UnreadablePreBuild;
    public void OnPreBuild(ForgeBuildContext context)
    {
        VariantFailureProof.UnreadableThrown = new UnreadableProofException();
        throw VariantFailureProof.UnreadableThrown;
    }
    public void OnPostBuild(ForgeBuildContext context) { }
}

[ForgePlugin]
public sealed class InjectNamelessHooks : IForgePlugin
{
    public string DisplayName => VariantFailureProof.NamelessHooks ? throw new InvalidOperationException("name unavailable") : "Nameless proof";
    public int Order => 780;
    public bool IsApplicable(BuildProfile buildProfile) => true;
    public bool? IsEnabled(ForgeProfile forgeProfile) => VariantFailureProof.NamelessHooks;
    public void OnPreBuild(ForgeBuildContext context) => VariantFailureProof.NamelessPreBuildRan = true;
    public void OnPostBuild(ForgeBuildContext context) => VariantFailureProof.NamelessPostBuildRan = true;
}

[ForgePlugin]
public sealed class InjectUnreadableCleanup : IForgePlugin
{
    public string DisplayName => VariantFailureProof.UnreadableCleanup ? throw new InvalidOperationException("name unavailable") : "Unreadable cleanup proof";
    // The lowest: its pre-build hook runs first, so its post-build hook runs last.
    public int Order => 1;
    public bool IsApplicable(BuildProfile buildProfile) => true;
    public bool? IsEnabled(ForgeProfile forgeProfile) => VariantFailureProof.UnreadableCleanup;
    public void OnPreBuild(ForgeBuildContext context) { }
    public void OnPostBuild(ForgeBuildContext context) => throw new UnreadableProofException();
}

[ForgePlugin]
public sealed class InjectUnreadableEditorCleanup : IForgePlugin, IForgeEditorApplicable
{
    public string DisplayName => "Unreadable editor cleanup proof";
    public int Order => 790;
    public bool IsApplicable(BuildProfile buildProfile) => true;
    public bool? IsEnabled(ForgeProfile forgeProfile) => VariantFailureProof.UnreadableEditorCleanup;
    // A build-time value, which the post-build hook then fails to put back.
    public void OnPreBuild(ForgeBuildContext context) => VariantFailureProof.EditorCleanupSetting = "build";
    public void OnPostBuild(ForgeBuildContext context) => throw new UnreadableProofException();
    public string CaptureEditorState(BuildTargetGroup group) => VariantFailureProof.EditorCleanupSetting;
    public void ApplyToEditor(ForgeProfile forgeProfile, BuildTargetGroup group, string variant) => VariantFailureProof.EditorCleanupSetting = "applied";
    public void RestoreEditorState(BuildTargetGroup group, string json) => VariantFailureProof.EditorCleanupSetting = json;
}

// Its Apply writes, then fails, and while it is told to, undoing that fails too, so the Apply
// leaves its entry unfinished.
[ForgePlugin]
public sealed class InjectUnfinishedApply : IForgePlugin, IForgeEditorApplicable
{
    public string DisplayName => "Unfinished Apply proof";
    public int Order => 795;
    public bool IsApplicable(BuildProfile buildProfile) => true;
    public bool? IsEnabled(ForgeProfile forgeProfile) => VariantFailureProof.UnfinishedApply || VariantFailureProof.UnfinishedApplyArg;
    public void OnPreBuild(ForgeBuildContext context) { }
    public void OnPostBuild(ForgeBuildContext context) { }
    public string CaptureEditorState(BuildTargetGroup group) => VariantFailureProof.UnfinishedApplySetting;
    public void ApplyToEditor(ForgeProfile forgeProfile, BuildTargetGroup group, string variant)
    {
        VariantFailureProof.UnfinishedApplySetting = "partial";
        throw new InvalidOperationException("Injected apply failure");
    }
    public void RestoreEditorState(BuildTargetGroup group, string json)
    {
        if (VariantFailureProof.UnfinishedApplyRestoreFails || VariantFailureProof.UnfinishedApplyArg)
            throw new InvalidOperationException("Injected undo failure");
        VariantFailureProof.UnfinishedApplySetting = json;
    }
}
