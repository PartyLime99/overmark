using Overmark.Capture;

namespace Overmark.Tests;

public class FileNamerTests
{
    private const string Folder = @"C:\Shots";
    private static readonly DateTime Time = new(2026, 10, 15, 9, 5, 7);

    [Fact]
    public void Uses_the_default_pattern()
    {
        Assert.Equal(@"C:\Shots\2026-10-15_09-05-07.png", FileNamer.NextPath(Folder, null, Time, _ => false));
    }

    [Fact]
    public void Adds_a_number_when_the_name_is_taken()
    {
        var taken = new HashSet<string> { @"C:\Shots\2026-10-15_09-05-07.png", @"C:\Shots\2026-10-15_09-05-07_2.png" };

        Assert.Equal(@"C:\Shots\2026-10-15_09-05-07_3.png", FileNamer.NextPath(Folder, null, Time, taken.Contains));
    }

    [Fact]
    public void Replaces_characters_that_files_cannot_have()
    {
        Assert.Equal(@"C:\Shots\2026_10_15.png", FileNamer.NextPath(Folder, "yyyy/MM/dd", Time, _ => false));
    }

    [Fact]
    public void Quoted_text_in_the_pattern_is_kept()
    {
        Assert.Equal(@"C:\Shots\Overmark 2026-10-15.png", FileNamer.NextPath(Folder, "'Overmark' yyyy-MM-dd", Time, _ => false));
    }

    [Fact]
    public void A_broken_pattern_falls_back_to_the_default()
    {
        Assert.Equal(@"C:\Shots\2026-10-15_09-05-07.png", FileNamer.NextPath(Folder, "yyyy'unclosed", Time, _ => false));
    }
}
