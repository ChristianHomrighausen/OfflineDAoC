using System;
using System.IO;
using DOL.GS;
using NUnit.Framework;

[TestFixture, NonParallelizable]
public class UT_BotLevelCapStore
{
    private string _directory;
    private string PathName => Path.Combine(_directory, BotLevelCapStore.FileName);

    [SetUp]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "bot-levelcap-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void Cleanup()
    {
        Directory.Delete(_directory, true);
        // Leave the static store empty for other tests.
        BotLevelCapStore.Initialize(Path.GetTempPath() + "missing-" + Guid.NewGuid().ToString("N"));
    }

    [Test]
    public void MissingFileMeansNoCaps()
    {
        BotLevelCapStore.Initialize(_directory);
        Assert.That(BotLevelCapStore.ExperiencePaused, Is.False);
        Assert.That(BotLevelCapStore.TryGetCap(1, out _), Is.False);
        Assert.That(BotLevelCapStore.LevelLimitFor(1, 10, 50), Is.EqualTo(50));
    }

    [Test]
    public void ReadsTheFormatOfTheExistingFile()
    {
        File.WriteAllText(PathName, "{\n  \"Version\": 1,\n  \"Caps\": {\n    \"1\": 32,\n    \"47\": 17,\n    \"422\": 32\n  }\n}");
        BotLevelCapStore.Initialize(_directory);

        Assert.That(BotLevelCapStore.ExperiencePaused, Is.False);
        Assert.That(BotLevelCapStore.TryGetCap(47, out byte cap), Is.True);
        Assert.That(cap, Is.EqualTo(17));
        Assert.That(BotLevelCapStore.LevelLimitFor(1, 20, 50), Is.EqualTo(32));
        Assert.That(BotLevelCapStore.LevelLimitFor(2, 20, 50), Is.EqualTo(50));
    }

    [TestCase("not json")]
    [TestCase("[]")]
    [TestCase("{\"Caps\":{}}")]
    [TestCase("{\"Version\":2,\"Caps\":{}}")]
    [TestCase("{\"Version\":1,\"Caps\":{},\"Extra\":1}")]
    [TestCase("{\"Version\":1,\"Caps\":{\"0\":10}}")]
    [TestCase("{\"Version\":1,\"Caps\":{\"5\":51}}")]
    [TestCase("{\"Version\":1,\"Caps\":{\"5\":\"10\"}}")]
    [TestCase("{\"Version\":1,\"Caps\":{\"5\":10,\"5\":12}}")]
    public void BrokenFilePausesExperienceAndIsNeverOverwritten(string json)
    {
        File.WriteAllText(PathName, json);
        BotLevelCapStore.Initialize(_directory);

        Assert.That(BotLevelCapStore.ExperiencePaused, Is.True);
        Assert.That(BotLevelCapStore.LevelLimitFor(5, 12, 50), Is.EqualTo(12));
        Assert.That(() => BotLevelCapStore.SetCap(5, 20), Throws.InvalidOperationException);
        Assert.That(File.ReadAllText(PathName), Is.EqualTo(json));
    }

    [Test]
    public void SetAndRemoveAreSavedAndSurviveARestart()
    {
        BotLevelCapStore.Initialize(_directory);
        BotLevelCapStore.SetCap(12, 27);
        BotLevelCapStore.SetCap(3, 22);

        BotLevelCapStore.Initialize(_directory);
        Assert.That(BotLevelCapStore.TryGetCap(12, out byte cap), Is.True);
        Assert.That(cap, Is.EqualTo(27));

        BotLevelCapStore.SetCap(12, null);
        BotLevelCapStore.Initialize(_directory);
        Assert.That(BotLevelCapStore.TryGetCap(12, out _), Is.False);
        Assert.That(BotLevelCapStore.TryGetCap(3, out _), Is.True);
        Assert.That(Directory.GetFiles(_directory, "*.tmp"), Is.Empty);
    }

    [Test]
    public void InvalidCapsAreRefused()
    {
        BotLevelCapStore.Initialize(_directory);
        Assert.That(() => BotLevelCapStore.SetCap(1, 0), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => BotLevelCapStore.SetCap(1, 51), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => BotLevelCapStore.SetCap(0, 10), Throws.TypeOf<ArgumentOutOfRangeException>());
    }
}
