using System.Net.Security;
using BambuLab.X1Camera;
using BambuLab.X1Camera.Probe;

ProbeCommandLine commandLine;
try
{
    commandLine = ProbeCommandLine.Parse(args);
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine(exception.Message);
    ProbeCommandLine.WriteUsage(Console.Error);
    return 2;
}

if (commandLine.ShowHelp)
{
    ProbeCommandLine.WriteUsage(Console.Out);
    return 0;
}

ProbeSettings settings;
try
{
    settings = await GetSettingsAsync(commandLine.ForceConfigure);
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Configuration cancelled.");
    return 2;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Could not configure the probe ({exception.GetType().Name}).");
    Console.Error.WriteLine("Run with --configure to replace invalid saved settings.");
    return 2;
}

using var cancellation = commandLine.Duration is { } duration
    ? new CancellationTokenSource(duration)
    : new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

var options = new X1CameraOptions(settings.Host, settings.AccessCode)
{
    Port = settings.Port,
    PinnedCertificateSha256 = settings.CertificateSha256
};

var units = 0L;
try
{
    await using var session = await new X1CameraClient().ConnectAsync(options, cancellation.Token);
    await using var output = commandLine.OutputPath is null
        ? null
        : new FileStream(commandLine.OutputPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 64 * 1024, FileOptions.Asynchronous);

    Console.WriteLine($"Connected: payload={session.PayloadType}, clock={session.ClockRate}.");
    Console.WriteLine(commandLine.Duration is null ? "Press Ctrl+C to stop." : $"Testing for {commandLine.Duration.Value.TotalSeconds:0} seconds.");
    await foreach (var unit in session.ReadNalUnitsAsync(cancellation.Token))
    {
        units++;
        if (output is not null)
        {
            await output.WriteAsync(unit.AnnexBData, cancellation.Token);
        }

        if (unit.IsKeyFrame || units % 300 == 0)
        {
            Console.WriteLine($"NAL units: {units}; last type: {unit.Type}; RTP timestamp: {unit.RtpTimestamp}");
        }
    }
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Console.WriteLine("Stopped.");
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Probe failed: {exception.Message}");
    return 1;
}
finally
{
    Console.WriteLine($"NAL units received: {units}.");
}

return 0;

static async Task<ProbeSettings> GetSettingsAsync(bool forceConfigure)
{
    if (!forceConfigure && ProbeSettings.TryFromEnvironment(out var environmentSettings))
    {
        Console.WriteLine("Using settings from environment variables.");
        return environmentSettings;
    }

    var settingsPath = ProbeSettingsStore.DefaultPath;
    if (!forceConfigure && File.Exists(settingsPath))
    {
        Console.WriteLine("Using encoded settings stored beside the application.");
        return await ProbeSettingsStore.LoadAsync(settingsPath, CancellationToken.None);
    }

    var host = ReadRequiredLine("Printer address: ");
    var accessCode = ConsoleSecretReader.Read("LAN access code: ");
    if (string.IsNullOrWhiteSpace(accessCode))
    {
        throw new OperationCanceledException();
    }

    Console.WriteLine("Checking the printer certificate without sending credentials...");
    var observation = await CertificatePinDiscovery.ObserveAsync(host, 322, CancellationToken.None);
    if (observation.PolicyErrors != SslPolicyErrors.None)
    {
        Console.WriteLine($"The operating system reported certificate validation errors: {observation.PolicyErrors}.");
    }

    if (!Confirm("Trust and pin the certificate currently presented by this printer?", defaultAnswer: false))
    {
        throw new OperationCanceledException();
    }

    var settings = new ProbeSettings(host, accessCode, observation.Sha256, 322);
    Console.WriteLine("Saved values use reversible Base64 encoding, not encryption.");
    if (Confirm("Save encoded settings in JSON beside the application?", defaultAnswer: false))
    {
        await ProbeSettingsStore.SaveAsync(settingsPath, settings, CancellationToken.None);
        Console.WriteLine("Encoded settings saved beside the application.");
    }

    return settings;
}

static string ReadRequiredLine(string prompt)
{
    Console.Write(prompt);
    var value = Console.ReadLine();
    return string.IsNullOrWhiteSpace(value) ? throw new OperationCanceledException() : value.Trim();
}

static bool Confirm(string prompt, bool defaultAnswer)
{
    Console.Write($"{prompt} [{(defaultAnswer ? "Y/n" : "y/N")}] ");
    var answer = Console.ReadLine()?.Trim();
    return string.IsNullOrEmpty(answer)
        ? defaultAnswer
        : answer.Equals("y", StringComparison.OrdinalIgnoreCase)
            || answer.Equals("yes", StringComparison.OrdinalIgnoreCase);
}
