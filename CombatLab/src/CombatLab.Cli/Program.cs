namespace CombatLab.Cli;

using CombatLab.Runner.Config;

internal static class Program
{
    public static int Main(string[] args)
    {
        return ConfigCommand.Execute(args, Console.Out, Console.Error, Directory.GetCurrentDirectory());
    }
}
