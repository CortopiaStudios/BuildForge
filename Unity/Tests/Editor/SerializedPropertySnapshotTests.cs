using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using BuildForge.Editor.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace BuildForge.Tests.Editor
{
    public class SerializedPropertySnapshotTests
    {
        enum TestMode { A = 0, B = 10, C = 20 }

        [Serializable]
        struct TestDevice
        {
            public string visibleName;
            public string manifestName;
            public bool enabled;
        }

        class SnapshotTarget : ScriptableObject
        {
            public bool flag;
            public int count;
            public long big;
            public float ratio;
            public double precise;
            public TestMode mode;
            public string label;
            public string empty = "";
            public Texture2D icon;
            public Vector3 offset;
            public Color tint;
            public List<TestDevice> devices = new();
            [HideInInspector] public int hidden = 99;
            public int skipMe = 5;
            public Gradient unsupported = new();
        }

        const string IconPath = "Packages/com.cortopiastudios.buildforge/Editor/Icons/ForgeProfile.png";
        static readonly HashSet<string> BaseSkip = new() { "m_Script", "m_Name", "m_ObjectHideFlags", "m_EditorClassIdentifier" };

        SnapshotTarget source, target;

        [SetUp]
        public void SetUp()
        {
            source = ScriptableObject.CreateInstance<SnapshotTarget>();
            source.flag = true;
            source.count = -17;
            source.big = 5_000_000_000L;
            source.ratio = 0.5f;
            source.precise = 1e-7;
            source.mode = TestMode.C;
            source.label = "Quest 3";
            source.icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            source.offset = new Vector3(1.5f, -2f, 3.25f);
            source.tint = new Color(0.1f, 0.2f, 0.3f, 0.4f);
            source.devices = new List<TestDevice>
            {
                new TestDevice { visibleName = "Quest 2", manifestName = "quest2", enabled = true },
                new TestDevice { visibleName = "Quest 3", manifestName = "quest3", enabled = false },
            };
            target = ScriptableObject.CreateInstance<SnapshotTarget>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(source);
            UnityEngine.Object.DestroyImmediate(target);
        }

        List<PropertyValueEntry> CaptureSource(ISet<string> skip = null)
        {
            // The Gradient field is unsupported and warns once per capture.
            LogAssert.Expect(LogType.Warning, new Regex("unsupported.*not supported"));
            return SerializedPropertySnapshot.Capture(new SerializedObject(source), skip ?? BaseSkip);
        }

        static int IndexOf(List<PropertyValueEntry> entries, string path) => entries.FindIndex(e => e.path == path);
        static string ValueOf(List<PropertyValueEntry> entries, string path) => entries.First(e => e.path == path).value;

        [Test]
        public void Capture_RecordsLeaves_ArraySizeBeforeElements_CompoundsDecomposed()
        {
            var entries = CaptureSource();
            var paths = entries.Select(e => e.path).ToList();

            Assert.Less(IndexOf(entries, "devices.Array.size"), IndexOf(entries, "devices.Array.data[0].visibleName"));
            Assert.Less(IndexOf(entries, "devices.Array.data[0].enabled"), IndexOf(entries, "devices.Array.data[1].visibleName"));
            Assert.AreEqual("2", ValueOf(entries, "devices.Array.size"));
            Assert.AreEqual("quest2", ValueOf(entries, "devices.Array.data[0].manifestName"));

            CollectionAssert.Contains(paths, "offset.x");
            Assert.AreEqual("0.1,0.2,0.3,0.4", ValueOf(entries, "tint"), "Colors are a single leaf.");
            CollectionAssert.DoesNotContain(paths, "devices");
            CollectionAssert.DoesNotContain(paths, "offset");
            CollectionAssert.DoesNotContain(paths, "unsupported");
        }

        [Test]
        public void Capture_SkipsTopLevelSkipList_AndHiddenFields()
        {
            var skip = new HashSet<string>(BaseSkip) { "skipMe", "devices" };
            var entries = CaptureSource(skip);
            var paths = entries.Select(e => e.path).ToList();

            CollectionAssert.DoesNotContain(paths, "skipMe");
            CollectionAssert.DoesNotContain(paths, "hidden");
            Assert.IsFalse(paths.Any(p => p.StartsWith("devices")), "Skipping a container skips its children.");
            Assert.IsFalse(paths.Any(p => p.StartsWith("m_")), "Unity object plumbing is skipped.");
        }

        [Test]
        public void Enum_EncodesUnderlyingValue()
        {
            Assert.AreEqual("20", ValueOf(CaptureSource(), "mode"));
        }

        [Test]
        public void ObjectReference_NullIsEmpty_AssetIsGlobalObjectId()
        {
            Assert.IsNotNull(source.icon, "Test needs the package icon asset.");
            var entries = CaptureSource();
            StringAssert.StartsWith("GlobalObjectId_V1", ValueOf(entries, "icon"));

            source.icon = null;
            Assert.AreEqual("", ValueOf(CaptureSource(), "icon"));
        }

        [Test]
        public void Apply_RoundTrip_AllSupportedTypes()
        {
            var entries = CaptureSource();
            SerializedPropertySnapshot.Apply(new SerializedObject(target), entries);

            Assert.AreEqual(source.flag, target.flag);
            Assert.AreEqual(source.count, target.count);
            Assert.AreEqual(source.big, target.big);
            Assert.AreEqual(source.ratio, target.ratio);
            Assert.AreEqual(source.precise, target.precise);
            Assert.AreEqual(source.mode, target.mode);
            Assert.AreEqual(source.label, target.label);
            Assert.AreEqual("", target.empty);
            Assert.AreSame(source.icon, target.icon);
            Assert.AreEqual(source.offset, target.offset);
            Assert.AreEqual(source.tint, target.tint);
            Assert.AreEqual(2, target.devices.Count);
            Assert.AreEqual("Quest 3", target.devices[1].visibleName);
            Assert.IsFalse(target.devices[1].enabled);
            Assert.AreEqual(99, target.hidden, "Hidden fields are untouched.");
        }

        [Test]
        public void Apply_ResizesList_BothDirections()
        {
            target.devices = Enumerable.Range(0, 5).Select(i => new TestDevice { visibleName = "old" + i }).ToList();
            SerializedPropertySnapshot.Apply(new SerializedObject(target), CaptureSource());
            Assert.AreEqual(2, target.devices.Count);
            Assert.AreEqual("Quest 2", target.devices[0].visibleName);

            source.devices.Clear();
            SerializedPropertySnapshot.Apply(new SerializedObject(target), CaptureSource());
            Assert.AreEqual(0, target.devices.Count);
        }

        [Test]
        public void Float_IsCultureInvariant()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
                source.ratio = 0.5f;
                source.precise = -3.25;
                var entries = CaptureSource();

                Assert.AreEqual("0.5", ValueOf(entries, "ratio"));
                Assert.AreEqual("-3.25", ValueOf(entries, "precise"));

                SerializedPropertySnapshot.Apply(new SerializedObject(target), entries);
                Assert.AreEqual(0.5f, target.ratio);
                Assert.AreEqual(-3.25, target.precise);
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Test]
        public void Apply_UnknownPath_WarnsAndContinues()
        {
            var entries = new List<PropertyValueEntry>
            {
                new PropertyValueEntry { path = "gone", value = "1" },
                new PropertyValueEntry { path = "count", value = "7" },
            };
            LogAssert.Expect(LogType.Warning, new Regex("gone: property no longer exists"));

            SerializedPropertySnapshot.Apply(new SerializedObject(target), entries);

            Assert.AreEqual(7, target.count);
        }

        [Test]
        public void Apply_UnparseableValue_WarnsAndLeavesFieldUnchanged()
        {
            target.count = 3;
            LogAssert.Expect(LogType.Warning, new Regex("count: could not apply value"));

            SerializedPropertySnapshot.Apply(new SerializedObject(target),
                new List<PropertyValueEntry> { new PropertyValueEntry { path = "count", value = "seven" } });

            Assert.AreEqual(3, target.count);
        }
    }
}
