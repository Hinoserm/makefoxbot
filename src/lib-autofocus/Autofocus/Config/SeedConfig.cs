namespace Autofocus.Config;

public record SeedConfig
{
    public int? Seed { get; set; }
    public int? SubSeed { get; set; }
    public double? SubseedStrength { get; set; }
    public uint? SeedResizeFromWidth { get; set; }
    public uint? SeedResizeFromHeight { get; set; }

    public static implicit operator SeedConfig(int value)
    {
        return new SeedConfig
        {
            Seed = value
        };
    }
}