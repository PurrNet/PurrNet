public struct ScenarioDetails
{
    public string name;
    public ScenarioResult result;
    public double durationInMs;
    public ulong dataSent;
    public ulong dataReceived;
    public bool measured;
    public BenchmarkMetrics? benchmark;
}
