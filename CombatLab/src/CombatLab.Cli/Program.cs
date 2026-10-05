namespace CombatLab.Cli;

using CombatLab.Runner.Config;
using CombatLab.Runner.Replays;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length != 0 && args[0] == "run-demo")
            return BattleDemoCommand.Execute(args, Console.Out, Console.Error, Directory.GetCurrentDirectory());
        return ConfigCommand.Execute(args, Console.Out, Console.Error, Directory.GetCurrentDirectory());
    }
}
