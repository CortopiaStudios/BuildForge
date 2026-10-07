using BuildForge.Editor.Core;
using NUnit.Framework;
using UnityEngine;

namespace BuildForge.Tests.Editor
{
    /// <summary>
    /// The variant ledger keeps only what editor Apply changed. Undoing starts
    /// from the profile's current values, so edits made after Apply survive,
    /// while an untouched profile returns to its exact previous content.
    /// </summary>
    public class VariantDefineLedgerTests
    {
        static readonly string[] Original = { "USER_SYMBOL", "DEVELOPMENT", "OTHER" };
        // A Default apply with a rule owning DEVELOPMENT: the alias is removed, the variant define added.
        static readonly string[] Written = { "USER_SYMBOL", "OTHER", "BUILD_VARIANT_DEFAULT" };

        [Test]
        public void UndoDefines_UntouchedSinceApply_RestoresOriginalExactly()
        {
            CollectionAssert.AreEqual(Original, VariantDefineLedger.UndoDefines(Written, Original, Written));
        }

        [Test]
        public void UndoDefines_UserAddedDefineSinceApply_IsKept()
        {
            var current = new[] { "USER_SYMBOL", "OTHER", "BUILD_VARIANT_DEFAULT", "ENABLE_LOGS" };
            CollectionAssert.AreEqual(new[] { "USER_SYMBOL", "DEVELOPMENT", "OTHER", "ENABLE_LOGS" },
                VariantDefineLedger.UndoDefines(current, Original, Written));
        }

        [Test]
        public void UndoDefines_UserRemovedDefineSinceApply_StaysRemoved()
        {
            var current = new[] { "USER_SYMBOL", "BUILD_VARIANT_DEFAULT" };
            CollectionAssert.AreEqual(new[] { "USER_SYMBOL", "DEVELOPMENT" },
                VariantDefineLedger.UndoDefines(current, Original, Written));
        }

        // A define Build Forge removed goes back after its original predecessor,
        // or first when it had none, so the committed asset shows no reordering.
        [Test]
        public void UndoDefines_RemovedDefineReturnsToItsOriginalPosition()
        {
            var original = new[] { "DEVELOPMENT", "USER_SYMBOL", "OTHER" };
            var written = new[] { "USER_SYMBOL", "OTHER", "BUILD_VARIANT_DEFAULT" };
            var current = new[] { "USER_SYMBOL", "OTHER", "BUILD_VARIANT_DEFAULT", "NEW" };
            CollectionAssert.AreEqual(new[] { "DEVELOPMENT", "USER_SYMBOL", "OTHER", "NEW" },
                VariantDefineLedger.UndoDefines(current, original, written));
        }

        [Test]
        public void UndoDefines_NeverReturnsAVariantDefine()
        {
            // A profile committed while a variant was applied carries a stale
            // variant define into the list recorded before Apply.
            var original = new[] { "USER_SYMBOL", "BUILD_VARIANT_INTERNAL" };
            var written = new[] { "USER_SYMBOL", "BUILD_VARIANT_DEFAULT" };
            CollectionAssert.AreEqual(new[] { "USER_SYMBOL" }, VariantDefineLedger.UndoDefines(written, original, written));
            CollectionAssert.AreEqual(new[] { "USER_SYMBOL", "ADDED" },
                VariantDefineLedger.UndoDefines(new[] { "USER_SYMBOL", "BUILD_VARIANT_DEFAULT", "ADDED" }, original, written));
        }

        [Test]
        public void UndoDefines_NullLists_AreEmpty()
        {
            CollectionAssert.IsEmpty(VariantDefineLedger.UndoDefines(null, null, null));
        }

        // Field order follows VariantBuildSettings.Fields: development, profiler,
        // deep profiling, script debugging, wait for debugger.
        static string Flags(params bool[] values)
            => $"{{\"values\":[{string.Join(",", System.Array.ConvertAll(values, v => v ? "true" : "false"))}]," +
               "\"present\":[true,true,true,true,true]}";

        [Test]
        public void UndoSettings_FlagChangedByApplyAndUntouched_IsRestored()
        {
            var before = Flags(false, false, false, true, false);
            var written = Flags(true, false, false, true, false);
            var undo = VariantBuildSettings.Undo(written, before, written);
            Assert.That(undo, Is.Not.Null);
            var parsed = JsonUtility.FromJson<Snapshot>(undo);
            CollectionAssert.AreEqual(new[] { true, false, false, false, false }, parsed.present);
            Assert.That(parsed.values[0], Is.False);
        }

        [Test]
        public void UndoSettings_FlagChangedByUserSinceApply_KeepsUserValue()
        {
            // Apply disabled development and script debugging; the user then
            // turned script debugging back on. Only development is undone.
            var before = Flags(true, false, false, true, false);
            var written = Flags(false, false, false, false, false);
            var current = Flags(false, false, false, true, false);
            var parsed = JsonUtility.FromJson<Snapshot>(VariantBuildSettings.Undo(current, before, written));
            CollectionAssert.AreEqual(new[] { true, false, false, false, false }, parsed.present);
            Assert.That(parsed.values[0], Is.True);
        }

        [Test]
        public void UndoSettings_NothingChangedByApply_IsNull()
        {
            var flags = Flags(true, false, false, true, false);
            Assert.That(VariantBuildSettings.Undo(Flags(false, true, true, false, true), flags, flags), Is.Null);
            Assert.That(VariantBuildSettings.Undo(null, flags, flags), Is.Null);
        }

        // Mirrors the private snapshot shape to read Undo's result.
        [System.Serializable]
        class Snapshot { public bool[] values; public bool[] present; }
    }
}
