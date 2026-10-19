using System;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BuildForge.Tests.Editor
{
    /// <summary>
    /// A plugin whose creation failed doesn't run, so builds stop while it is on in
    /// Project Settings instead of going out without it.
    /// </summary>
    public class PluginLoadFailureTests
    {
        [Test]
        public void APluginThatFailedToLoad_StopsBuilds_UnlessItIsOff()
        {
            var failures = new[] { (typeof(PluginLoadFailureTests), "Its constructor threw.") };
            var error = ForgePluginRegistry.LoadError(failures, _ => false);
            StringAssert.Contains(typeof(PluginLoadFailureTests).FullName, error);
            StringAssert.Contains("Its constructor threw.", error);
            StringAssert.Contains("Installed Plugins", error);
            Assert.IsNull(ForgePluginRegistry.LoadError(failures, _ => true), "Off in Project Settings: it wouldn't run anyway.");
            Assert.IsNull(ForgePluginRegistry.LoadError(Array.Empty<(Type, string)>(), _ => false));
        }

        [Test]
        public void ABuild_StopsBeforeItStarts_WhileAPluginFailedToLoad()
        {
            var previousSettings = EditorJsonUtility.ToJson(ForgeSettings.instance);
            var native = VariantRulesTests.CreateNativeProfile();
            var forge = ScriptableObject.CreateInstance<ForgeProfile>();
            ForgePluginRegistry.GetPlugins();
            var failure = (typeof(PluginLoadFailureTests), "Its constructor threw.");
            ForgePluginRegistry.LoadFailures.Add(failure);
            try
            {
                var settings = new SerializedObject(ForgeSettings.instance);
                settings.FindProperty("buildVariants").arraySize = 0;
                settings.FindProperty("variantRules").arraySize = 0;
                settings.ApplyModifiedPropertiesWithoutUndo();
                var fp = new SerializedObject(forge);
                fp.FindProperty("buildProfile").objectReferenceValue = native;
                fp.ApplyModifiedPropertiesWithoutUndo();
                var before = EditorJsonUtility.ToJson(native);

                var message = Assert.Throws<InvalidOperationException>(() => ForgeBuildRunner.RunBuild(forge)).Message;
                StringAssert.Contains("could not be loaded", message);
                StringAssert.Contains(typeof(PluginLoadFailureTests).FullName, message);
                Assert.That(EditorJsonUtility.ToJson(native), Is.EqualTo(before));
            }
            finally
            {
                ForgePluginRegistry.LoadFailures.Remove(failure);
                EditorJsonUtility.FromJsonOverwrite(previousSettings, ForgeSettings.instance);
                UnityEngine.Object.DestroyImmediate(forge);
                UnityEngine.Object.DestroyImmediate(native);
            }
        }
    }
}
