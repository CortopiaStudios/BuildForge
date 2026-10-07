using System.Collections.Generic;
using BuildForge.Editor.Configuration;
using NUnit.Framework;
using UnityEditor;

namespace BuildForge.Tests.Editor
{
    public class EditorStateLedgerTests
    {
        [Test]
        public void MakeKey_TryParseKey_RoundTrip()
        {
            var key = EditorStateLedger.MakeKey("BuildForge.Editor.OpenXR.OpenXRPlugin", BuildTargetGroup.Android);
            Assert.AreEqual("BuildForge.Editor.OpenXR.OpenXRPlugin|Android", key);

            Assert.IsTrue(EditorStateLedger.TryParseKey(key, out var type, out var group));
            Assert.AreEqual("BuildForge.Editor.OpenXR.OpenXRPlugin", type);
            Assert.AreEqual(BuildTargetGroup.Android, group);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("NoSeparator")]
        [TestCase("|Android")]
        [TestCase("Plugin|")]
        [TestCase("Plugin|NotAGroup")]
        public void TryParseKey_RejectsGarbage(string key)
        {
            Assert.IsFalse(EditorStateLedger.TryParseKey(key, out _, out _));
        }

        [Test]
        public void Set_Upserts()
        {
            var entries = new List<EditorStateEntry>();
            EditorStateLedger.Set(entries, "P|Android", "b1", "e1");
            EditorStateLedger.Set(entries, "P|Android", "b2", "e2");
            EditorStateLedger.Set(entries, "P|Standalone", "b3", "e3");

            Assert.AreEqual(2, entries.Count);
            var android = EditorStateLedger.Find(entries, "P|Android");
            Assert.AreEqual("b2", android.baselineJson);
            Assert.AreEqual("e2", android.expectedJson);
        }

        [Test]
        public void Remove_ReturnsFalseWhenMissing()
        {
            var entries = new List<EditorStateEntry>();
            EditorStateLedger.Set(entries, "P|Android", "b", "e");

            Assert.IsFalse(EditorStateLedger.Remove(entries, "P|Standalone"));
            Assert.IsTrue(EditorStateLedger.Remove(entries, "P|Android"));
            Assert.IsEmpty(entries);
        }
    }
}
