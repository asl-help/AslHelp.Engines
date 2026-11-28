using NUnit.Framework;

namespace AslHelp.Engines.Tests.Profiles;

public sealed class FakeVersionEdgeTests
{
    // A version read from game files can have any number of digits, so the made-up engine reads a run past int.MaxValue as int.MaxValue.
    [Test]
    public void Parse_DigitRunPastIntMax_ReadsIntMax()
    {
        Assert.That(FakeVersion.Parse("2022.3.12345678901f1")!.Parts[2], Is.EqualTo(int.MaxValue));
    }

    // The parser returns null for text that doesn't start with 2 to 4 numbers between dots.
    [TestCase("abc")]
    [TestCase("2022")]
    [TestCase("v4.24")]
    public void Parse_TextThatIsNotAVersionOfTheEngine_ReturnsNull(string text)
    {
        Assert.That(FakeVersion.Parse(text), Is.Null);
    }

    // Every version form of the made-up engine parses and keeps its text as written.
    [TestCase("2022.3.5f1")]
    [TestCase("4.24")]
    [TestCase("3.8225")]
    [TestCase("2019.3.0f11-Sunflower")]
    public void Parse_VersionOfTheEngine_KeepsTheText(string text)
    {
        Assert.That(FakeVersion.Parse(text)?.Text, Is.EqualTo(text));
    }
}
