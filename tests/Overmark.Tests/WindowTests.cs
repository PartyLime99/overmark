namespace Overmark.Tests;

/// <summary>
/// The test classes that build WPF windows. Each builds them on a thread of its own, and WPF setting up two windows
/// on different threads at the same moment can corrupt a dictionary they share, so these run one class at a time.
/// </summary>
[CollectionDefinition(Name)]
public class WindowTests
{
    public const string Name = "Windows";
}
