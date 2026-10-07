using BuildForge.Editor.Core;
using NUnit.Framework;

namespace BuildForge.Tests.Editor
{
    public class NormalizeValueTests
    {
        [Test]
        public void TopLevel_FileID_Zero()
        {
            Assert.AreEqual("__null_ref__", PlayerSettingsDiffComputer.NormalizeValue("{fileID: 0}"));
        }

        [Test]
        public void TopLevel_InstanceID_Zero()
        {
            Assert.AreEqual("__null_ref__", PlayerSettingsDiffComputer.NormalizeValue("{instanceID: 0}"));
        }

        [Test]
        public void Empty_String()
        {
            Assert.AreEqual("__empty__", PlayerSettingsDiffComputer.NormalizeValue(""));
        }

        [Test]
        public void Empty_Brackets()
        {
            Assert.AreEqual("__empty__", PlayerSettingsDiffComputer.NormalizeValue("[]"));
        }

        [Test]
        public void Nested_InstanceID_CanonicalizedToFileID()
        {
            var withInstanceID = PlayerSettingsDiffComputer.NormalizeValue(
                "    height: 180\n    banner: {instanceID: 0}");
            var withFileID = PlayerSettingsDiffComputer.NormalizeValue(
                "    height: 180\n    banner: {fileID: 0}");
            Assert.AreEqual(withFileID, withInstanceID);
        }

        [Test]
        public void Nested_NonZeroRefs_NotCanonicalized()
        {
            var result = PlayerSettingsDiffComputer.NormalizeValue(
                "    icon: {instanceID: 12345}");
            Assert.That(result, Does.Contain("{instanceID: 12345}"));
        }

        [Test]
        public void Nested_MultipleNullRefs_AllCanonicalized()
        {
            var result = PlayerSettingsDiffComputer.NormalizeValue(
                "    a: {instanceID: 0}\n    b: {instanceID: 0}");
            Assert.That(result, Does.Not.Contain("instanceID"));
            Assert.That(result, Does.Contain("{fileID: 0}"));
        }

        [Test]
        public void Whitespace_Collapsed()
        {
            Assert.AreEqual("a b c", PlayerSettingsDiffComputer.NormalizeValue("  a   b   c  "));
        }

        [Test]
        public void RealDifference_NotNormalized()
        {
            var a = PlayerSettingsDiffComputer.NormalizeValue("height: 180");
            var b = PlayerSettingsDiffComputer.NormalizeValue("height: 320");
            Assert.AreNotEqual(a, b);
        }
    }
}
