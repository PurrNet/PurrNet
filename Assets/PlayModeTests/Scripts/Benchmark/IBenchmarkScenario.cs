public interface IBenchmarkScenario
{
    BenchmarkMetrics? LastMetrics { get; }
    void ApplyOverrides(int? objectCount, float? pingsPerSecond);
}
