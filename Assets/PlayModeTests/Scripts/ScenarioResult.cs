public struct ScenarioResult
{
    public bool success;
    public bool warning;
    public string message;

    public static ScenarioResult Ok()
    {
        return new ScenarioResult
        {
            success = true
        };
    }

    public static ScenarioResult Ok(string message)
    {
        return new ScenarioResult
        {
            success = true,
            message = message
        };
    }

    /// <summary>
    /// Passes, but CI surfaces the message as a warning: for soft limits that timing noise on
    /// shared runners can cross without anything being broken.
    /// </summary>
    public static ScenarioResult Warn(string message)
    {
        return new ScenarioResult
        {
            success = true,
            warning = true,
            message = message
        };
    }

    public static ScenarioResult Fail(string message)
    {
        return new ScenarioResult
        {
            success = false,
            message = message
        };
    }
}
