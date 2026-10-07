using BuildForge.Editor.Configuration;
using NUnit.Framework;

namespace BuildForge.Tests.Editor
{
    public class MangleForFilenameTests
    {
        [TestCase("My Game", "My_Game")]
        [TestCase("Game: Remastered", "Game_Remastered")]
        [TestCase("Cats & Dogs", "Cats_and_Dogs")]
        [TestCase("Hero's Quest", "Heros_Quest")]
        [TestCase("Boom!", "Boom")]
        [TestCase("Game (Demo)", "Game_Demo")]
        [TestCase("Mr. Robot", "Mr_Robot")]
        [TestCase("Simple", "Simple")]
        [TestCase("  Spaced  Out  ", "Spaced_Out")]
        [TestCase("a---b", "a-b")]
        [TestCase("a___b", "a_b")]
        [TestCase("_leading_", "leading")]
        [TestCase("-trailing-", "trailing")]
        [TestCase("", "")]
        [TestCase("Already_Valid", "Already_Valid")]
        [TestCase("AB&CD", "ABandCD")]
        public void MangleForFilename_ProducesExpectedResult(string input, string expected)
        {
            Assert.AreEqual(expected, ForgeSettings.MangleForFilename(input));
        }
    }
}
