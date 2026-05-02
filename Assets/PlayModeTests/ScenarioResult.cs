public struct ScenarioResult
{
    public bool success;
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

    public static ScenarioResult Fail(string message)
    {
        return new ScenarioResult
        {
            success = false,
            message = message
        };
    }
}
