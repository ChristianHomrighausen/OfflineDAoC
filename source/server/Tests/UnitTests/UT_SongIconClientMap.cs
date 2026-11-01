using DOL.GS.PacketHandler;
using NUnit.Framework;

[TestFixture]
public class UT_SongIconClientMap
{
    [Test]
    public void TheEmbeddedClientTableIsLoaded()
    {
        Assert.That(SongIconClientMap.Count, Is.EqualTo(5875));
    }

    // Pairs from the client's spells.csv, as in the Song-Icons v2 hotfix map.
    [TestCase((ushort)1, (ushort)1000)]
    [TestCase((ushort)89, (ushort)1102)]
    [TestCase((ushort)14969, (ushort)4220)]
    public void KnownSongIconsAreTranslated(ushort spellIcon, ushort expected)
    {
        Assert.That(SongIconClientMap.IconForSong(spellIcon), Is.EqualTo(expected));
    }

    // 1011 and 2221 are listed twice in the client table and left out; 19 and 65535 are not in it.
    [TestCase((ushort)1011)]
    [TestCase((ushort)2221)]
    [TestCase((ushort)19)]
    [TestCase((ushort)65535)]
    public void UnknownAndAmbiguousIconsStayUnchanged(ushort spellIcon)
    {
        Assert.That(SongIconClientMap.IconForSong(spellIcon), Is.EqualTo(spellIcon));
    }
}
