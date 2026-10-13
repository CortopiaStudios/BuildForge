using BuildForge.Editor.UI;
using NUnit.Framework;
using UnityEngine;

namespace BuildForge.Tests.Editor
{
    /// <summary>
    /// Labels in the build window's fixed-width controls: a label that doesn't fit
    /// ends in an ellipsis instead of being cut at both ends, and the tooltip then
    /// starts with the whole label.
    /// </summary>
    public class BuildWindowLabelTests
    {
        // EditorStyles exist only while the editor draws its GUI, not in a batch test
        // run; a style with Unity's built-in runtime font measures text anywhere.
        static GUIStyle Style() =>
            new GUIStyle { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"), fontSize = 12 };

        [Test]
        public void FitContent_KeepsALabelThatFits()
        {
            var style = Style();
            var width = style.CalcSize(new GUIContent("Build (Internal)")).x + 1f;

            var content = ForgeBuildWindow.FitContent("Build (Internal)", "Build this profile through Build Forge.", null, style, width);

            Assert.AreEqual("Build (Internal)", content.text);
            Assert.AreEqual("Build this profile through Build Forge.", content.tooltip);
        }

        [Test]
        public void FitContent_EndsALongLabelInAnEllipsis_KeepsTheSuffix_AndShowsTheWholeLabelInTheTooltip()
        {
            var style = Style();
            const string label = "Variant: A Very Long Variant Name";
            var width = style.CalcSize(new GUIContent("Variant: A Very  ▾")).x;

            var content = ForgeBuildWindow.FitContent(label, "The build variant.", null, style, width, "  ▾");

            StringAssert.EndsWith("…  ▾", content.text);
            Assert.LessOrEqual(style.CalcSize(content).x, width);
            Assert.AreEqual(label + "\n\nThe build variant.", content.tooltip);
        }
    }
}
