namespace Overmark.Tests;

public class CommandLineTests
{
    [Fact]
    public void Edit_flag_gives_a_full_path()
    {
        Assert.Equal(@"C:\Shots\a.png", CommandLine.EditPath(["--edit", @"C:\Shots\a.png"]));
        Assert.Equal(Path.GetFullPath("a.png"), CommandLine.EditPath(["--EDIT", "a.png"]));
    }

    [Fact]
    public void A_lone_file_path_is_opened_too()
    {
        Assert.Equal(@"C:\Shots\a.png", CommandLine.EditPath([@"C:\Shots\a.png"]));
    }

    [Theory]
    [InlineData]
    [InlineData("--edit")]
    [InlineData("--quiet")]
    [InlineData("--edit", "")]
    [InlineData("a.png", "b.png")]
    public void Anything_else_is_not_an_edit(params string[] args)
    {
        Assert.Null(CommandLine.EditPath(args));
    }
}
