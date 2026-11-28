using NUnit.Framework;
using System;

namespace AslHelp.Engines.Tests;

public sealed class PathKeyTests
{
    // Every integer size gives the same key as the long, so 2 and 2L find the same item.
    [TestCase((sbyte)2)]
    [TestCase((byte)2)]
    [TestCase((short)2)]
    [TestCase((ushort)2)]
    [TestCase(2)]
    [TestCase(2u)]
    [TestCase(2L)]
    [TestCase(2UL)]
    public void TryCreate_EveryIntegerSize_GivesTheKeyOfTheLong(object index)
    {
        bool created = PathKey.TryCreate(index, out var key);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(created, Is.True);
            Assert.That(key, Is.EqualTo((PathKey)2L));
            Assert.That(key.GetHashCode(), Is.EqualTo(((PathKey)2L).GetHashCode()));
            Assert.That(key.Value, Is.TypeOf<long>());
        }
    }

    // A string and a bool keep their own type.
    [Test]
    public void TryCreate_AStringAndABool_GivesTheirKeys()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(PathKey.TryCreate("two", out var text), Is.True);
            Assert.That(text.Value, Is.EqualTo("two"));
            Assert.That(PathKey.TryCreate(true, out var flag), Is.True);
            Assert.That(flag.Value, Is.True);
            Assert.That(text, Is.Not.EqualTo((PathKey)2L));
        }
    }

    // A ulong past long.MaxValue stays a ulong, so a collection with ulong keys still finds it.
    [Test]
    public void TryCreate_AUlongPastLongMaxValue_KeepsTheUlong()
    {
        _ = PathKey.TryCreate(ulong.MaxValue, out var key);

        Assert.That(key.Value, Is.TypeOf<ulong>().And.EqualTo(ulong.MaxValue));
    }

    // A double, null or any other object can't be a key, so a dynamic caller can throw its own message.
    [TestCase(1.5)]
    [TestCase(null)]
    public void TryCreate_ADoubleOrNull_ReturnsFalse(object? value)
    {
        Assert.That(PathKey.TryCreate(value, out _), Is.False);
    }

    // A key prints the way a script writes it, with the invariant minus sign.
    [Test]
    [SetCulture("sv-SE")]
    public void ToString_EachKind_PrintsTheKeyAsWritten()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(((PathKey)2).ToString(), Is.EqualTo("2"));
            Assert.That(((PathKey)(-3)).ToString(), Is.EqualTo("-3"));
            Assert.That(((PathKey)"two").ToString(), Is.EqualTo("\"two\""));
            Assert.That(((PathKey)true).ToString(), Is.EqualTo("true"));
        }
    }

    // The default key is the index 0.
    [Test]
    public void Value_DefaultKey_IsTheIndexZero()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(default(PathKey), Is.EqualTo((PathKey)0));
            Assert.That(default(PathKey).ToString(), Is.EqualTo("0"));
        }
    }

    // A quote or a backslash in a string key prints escaped, so a key like a"]["b doesn't read as 2 keys in a path.
    [Test]
    public void ToString_AQuoteOrBackslashInTheKey_EscapesThem()
    {
        Assert.That(((PathKey)"a\"][\"b\\c").ToString(), Is.EqualTo("\"a\\\"][\\\"b\\\\c\""));
    }

    // -1 and ulong.MaxValue have the same bits, but they're 2 different keys.
    [Test]
    public void Equals_MinusOneAndUlongMaxValue_Differ()
    {
        _ = PathKey.TryCreate(ulong.MaxValue, out var key);

        Assert.That(key, Is.Not.EqualTo((PathKey)(-1L)));
    }

    // String keys compare ordinally, so case and culture rules don't make 2 keys equal.
    [Test]
    [SetCulture("de-DE")]
    public void Equals_StringKeys_CompareOrdinally()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That((PathKey)"Key", Is.Not.EqualTo((PathKey)"key"));
            Assert.That((PathKey)"ß", Is.Not.EqualTo((PathKey)"ss"));
            Assert.That((PathKey)"ゲーム", Is.EqualTo((PathKey)new string(['ゲ', 'ー', 'ム'])));
        }
    }

    // A null string can't be a key.
    [Test]
    public void ImplicitString_Null_Throws()
    {
        string key = null!;

        Assert.That(() => (PathKey)key, Throws.ArgumentNullException);
    }
}
