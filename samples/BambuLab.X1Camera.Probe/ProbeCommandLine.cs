namespace BambuLab.X1Camera.Probe;

internal sealed record ProbeCommandLine(
    bool ForceConfigure,
    TimeSpan? Duration,
    string? OutputPath,
    string? PngPath,
    bool ShowHelp)
{
    public static ProbeCommandLine Parse(string[] args)
    {
        var forceConfigure = false;
        TimeSpan? duration = null;
        string? outputPath = null;
        string? pngPath = null;
        var showHelp = false;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--configure":
                    forceConfigure = true;
                    break;
                case "--duration":
                    var seconds = ReadValue(args, ref index, "--duration");
                    if (!int.TryParse(seconds, out var parsedSeconds) || parsedSeconds is < 1 or > 86_400)
                    {
                        throw new ArgumentException("--duration must be a whole number from 1 through 86400 seconds.");
                    }

                    duration = TimeSpan.FromSeconds(parsedSeconds);
                    break;
                case "--output":
                    outputPath = ReadValue(args, ref index, "--output");
                    break;
                case "--png":
                    pngPath = ReadValue(args, ref index, "--png");
                    break;
                case "--help" or "-h":
                    showHelp = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{args[index]}'.");
            }
        }

        if (outputPath is not null && pngPath is not null)
        {
            throw new ArgumentException("--output and --png cannot be used together because a session has one active reader.");
        }

        return new ProbeCommandLine(forceConfigure, duration, outputPath, pngPath, showHelp);
    }

    public static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("Usage: BambuLab.X1Camera.Probe [--configure] [--duration seconds] [--output capture.h264 | --png still.png]");
        writer.WriteLine("  --configure       Ignore saved settings and prompt for replacement values.");
        writer.WriteLine("  --duration N      Stop automatically after 1 through 86400 seconds.");
        writer.WriteLine("  --output PATH     Save encoded Annex-B H.264 without overwriting an existing file.");
        writer.WriteLine("  --png PATH        Decode the next complete keyframe and save it as PNG without overwriting.");
    }

    private static string ReadValue(string[] args, ref int index, string argument)
    {
        index++;
        if (index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
        {
            throw new ArgumentException($"{argument} requires a value.");
        }

        return args[index];
    }
}
