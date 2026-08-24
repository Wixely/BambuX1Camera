using BambuLab.X1Camera;

var host = Environment.GetEnvironmentVariable("BAMBU_X1_HOST");
var accessCode = Environment.GetEnvironmentVariable("BAMBU_X1_ACCESS_CODE");
var certificatePin = Environment.GetEnvironmentVariable("BAMBU_X1_CERT_SHA256");

if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(accessCode))
{
    Console.Error.WriteLine("Set BAMBU_X1_HOST and BAMBU_X1_ACCESS_CODE before running the probe.");
    Console.Error.WriteLine("Set BAMBU_X1_CERT_SHA256 to the printer certificate fingerprint unless the OS trusts its certificate.");
    return 2;
}

var outputPath = args.Length == 2 && args[0] == "--output" ? args[1] : null;
if (args.Length is not 0 and not 2 || args.Length == 2 && args[0] != "--output")
{
    Console.Error.WriteLine("Usage: dotnet run --project samples/BambuLab.X1Camera.Probe [--output capture.h264]");
    return 2;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

var options = new X1CameraOptions(host, accessCode)
{
    PinnedCertificateSha256 = certificatePin
};

try
{
    await using var session = await new X1CameraClient().ConnectAsync(options, cancellation.Token);
    await using var output = outputPath is null
        ? null
        : new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 64 * 1024, FileOptions.Asynchronous);

    Console.WriteLine($"Connected: payload={session.PayloadType}, clock={session.ClockRate}. Press Ctrl+C to stop.");
    var units = 0L;
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

return 0;

