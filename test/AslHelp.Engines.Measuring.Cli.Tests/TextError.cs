namespace AslHelp.Engines.Measuring.Cli.Tests;

// Fails with the text a test passes, because a test of a failed row only needs a message.
internal sealed record TextError(
    string Text) : ResultError
{
    public override string Message => Text;
}
