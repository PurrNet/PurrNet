using System;

public static class CommandLineUtils
{
    public static bool TryGetArgument(string arg, out string value)
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == arg)
            {
                value = args[i + 1];
                return true;
            }
        }


        value = default;
        return false;
    }

    public static bool HasFlag(string flag)
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == flag)
                return true;
        }
        return false;
    }
}
