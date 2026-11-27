using AslHelp.Memory;
using AslHelp.Memory.Watching;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace AslHelp.Engines.Tests;

[TestFixture]
public class MemberChainTests
{
    // A member written after a chain in a script goes to the resolver with the last hop.
    [Test]
    public void GetMember_ANameAfterAChain_AppendsTheResolvedHop()
    {
        RecordingResolver resolver = new();
        dynamic chain = new MemberChain<string>(resolver, "root");

        object longer = chain.First.Second;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(longer.ToString(), Is.EqualTo("root.First.Second"));
            Assert.That(resolver.Tails, Is.EqualTo(["root", ".First"]));
        }
    }

    // A game member with the name of an inherited method still goes to the resolver.
    [Test]
    public void GetMember_TheNameOfAnInheritedMethod_AppendsTheResolvedHop()
    {
        dynamic chain = new MemberChain<string>(new RecordingResolver(), "root");

        object longer = chain.ToString.Equals.GetType.TryGetMember;

        Assert.That(longer.ToString(), Is.EqualTo("root.ToString.Equals.GetType.TryGetMember"));
    }

    // A member the resolver refuses throws its exception from the script line with the typo.
    [Test]
    public void GetMember_AMemberTheResolverRefuses_ThrowsTheExceptionOfTheResolver()
    {
        dynamic chain = new MemberChain<string>(new RecordingResolver { Refused = "Missing" }, "root");

        Assert.That(() => { object _ = chain.First.Missing; }, Throws.InvalidOperationException.With.Message.Contains("'Missing'"));
    }

    // A method call on a chain asks the resolver for a member with that name first, so it fails like a typo.
    [Test]
    public void Invoke_AMethodTheChainDoesNotHave_ThrowsTheExceptionOfTheResolver()
    {
        dynamic chain = new MemberChain<string>(new RecordingResolver { Refused = "Read" }, "root");

        Assert.That(() => chain.Read(), Throws.InvalidOperationException.With.Message.Contains("'Read'"));
    }

    // An index written after a chain goes to the resolver with the last hop and the key.
    [Test]
    public void GetIndex_OneIndex_AppendsTheHopOfTheResolver()
    {
        RecordingResolver resolver = new();
        dynamic chain = new MemberChain<string>(resolver, "root");

        object longer = chain.Items[2];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(longer.ToString(), Is.EqualTo("root.Items[2]"));
            Assert.That(resolver.Tails, Is.EqualTo(["root", ".Items"]));
        }
    }

    // 2 indexes or a null index throw before the chain calls the resolver, because an index hop is 1 element.
    [Test]
    public void GetIndex_TwoOrNullIndexes_ThrowsWithoutTheResolver()
    {
        RecordingResolver resolver = new();
        dynamic chain = new MemberChain<string>(resolver, "root");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => { object _ = chain[1, 2]; }, Throws.InvalidOperationException.With.Message.EqualTo(
                "Can't index root: write 1 index that isn't null, like [2] or [\"two\"]."));
            Assert.That(() => { object _ = chain[null]; }, Throws.InvalidOperationException);
            Assert.That(resolver.Tails, Is.Empty);
        }
    }

    // 2 chains that continue the same chain don't get each other's hops.
    [Test]
    public void GetMember_TwoNamesAfterTheSameChain_LeavesTheSharedChainAsItWas()
    {
        dynamic root = new MemberChain<string>(new RecordingResolver(), "root");
        object shared = root.Shared;

        object first = ((dynamic)shared).First;
        object second = ((dynamic)shared).Second;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(shared.ToString(), Is.EqualTo("root.Shared"));
            Assert.That(first.ToString(), Is.EqualTo("root.Shared.First"));
            Assert.That(second.ToString(), Is.EqualTo("root.Shared.Second"));
        }
    }

    // The watcher comes from the resolver, which gets the tick counter and every hop in order.
    [Test]
    public void CreateWatcher_AChain_PassesTheTicksAndEveryHopToTheResolver()
    {
        RecordingResolver resolver = new();
        ManualTicks ticks = new();
        dynamic chain = new MemberChain<string>(resolver, "root");
        IWatcherSource source = chain.First.Second;

        var watcher = source.CreateWatcher(ticks);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher, Is.SameAs(resolver.Created[0]));
            Assert.That(resolver.CreatedWith, Is.SameAs(ticks));
            Assert.That(resolver.CreatedFrom, Is.EqualTo(["root", ".First", ".Second"]));
        }
    }

    // A chain that already has a watcher still gives a new watcher per call and a longer chain. The hops of the 1st
    // watcher stay as they were.
    [Test]
    public void CreateWatcher_AChainWithAWatcher_StaysUsable()
    {
        RecordingResolver resolver = new();
        dynamic root = new MemberChain<string>(resolver, "root");
        object chain = root.First;
        var source = (IWatcherSource)chain;

        var first = source.CreateWatcher(new ManualTicks());
        var hops = resolver.CreatedFrom;
        object longer = ((dynamic)chain).Second;
        var second = source.CreateWatcher(new ManualTicks());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(hops, Is.EqualTo(["root", ".First"]));
            Assert.That(longer.ToString(), Is.EqualTo("root.First.Second"));
        }
    }
}

file sealed class RecordingResolver : IMemberResolver<string>
{
    public List<Watcher> Created { get; } = [];

    public IReadOnlyList<string>? CreatedFrom { get; private set; }

    public ITickCounter? CreatedWith { get; private set; }

    public string? Refused { get; init; }

    public List<string> Tails { get; } = [];

    public Watcher CreateWatcher(ITickCounter ticks, IReadOnlyList<string> hops)
    {
        CreatedWith = ticks;
        CreatedFrom = hops;
        Created.Add(new MemoryWatcher<int>(ticks, new BufferMemory(Address.Zero, new byte[4], MemoryLayout.X64), Address.Zero));

        return Created[^1];
    }

    public string ResolveMember(string tail, string name)
    {
        if (name == Refused)
        {
            throw new InvalidOperationException($"'{tail}' doesn't have a member '{name}'.");
        }

        Tails.Add(tail);

        return $".{name}";
    }

    public string ResolveIndex(string tail, object index)
    {
        Tails.Add(tail);

        return $"[{index}]";
    }
}

file sealed class ManualTicks : ITickCounter
{
    public long Value { get; set; }
}
