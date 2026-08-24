using System.Text;

namespace BambuLab.X1Camera.Protocol;

internal sealed record SdpVideoTrack(int PayloadType, int ClockRate, string Control, byte[]? SequenceParameterSet, byte[]? PictureParameterSet)
{
    public static SdpVideoTrack Parse(byte[] body)
    {
        var text = Encoding.UTF8.GetString(body);
        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var inVideo = false;
        var payloadType = -1;
        var clockRate = 90_000;
        string? control = null;
        byte[]? sps = null;
        byte[]? pps = null;

        foreach (var line in lines)
        {
            if (line.StartsWith("m=", StringComparison.OrdinalIgnoreCase))
            {
                inVideo = line.StartsWith("m=video ", StringComparison.OrdinalIgnoreCase);
                if (inVideo)
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 4)
                    {
                        _ = int.TryParse(parts[3], out payloadType);
                    }
                }

                continue;
            }

            if (!inVideo)
            {
                continue;
            }

            if (line.StartsWith("a=control:", StringComparison.OrdinalIgnoreCase))
            {
                control = line[10..].Trim();
                continue;
            }

            if (line.StartsWith("a=rtpmap:", StringComparison.OrdinalIgnoreCase))
            {
                var mapping = line[9..].Split([' ', '/'], StringSplitOptions.RemoveEmptyEntries);
                if (mapping.Length >= 3
                    && int.TryParse(mapping[0], out var mappedPayload)
                    && mappedPayload == payloadType
                    && mapping[1].Equals("H264", StringComparison.OrdinalIgnoreCase))
                {
                    _ = int.TryParse(mapping[2], out clockRate);
                }

                continue;
            }

            if (line.StartsWith($"a=fmtp:{payloadType} ", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var parameter in line[(line.IndexOf(' ') + 1)..].Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    const string name = "sprop-parameter-sets=";
                    if (!parameter.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var values = parameter[name.Length..].Split(',', StringSplitOptions.TrimEntries);
                    try
                    {
                        sps = values.Length > 0 ? Convert.FromBase64String(values[0]) : null;
                        pps = values.Length > 1 ? Convert.FromBase64String(values[1]) : null;
                    }
                    catch (FormatException exception)
                    {
                        throw new InvalidDataException("The SDP contained invalid H.264 parameter sets.", exception);
                    }
                }
            }
        }

        if (payloadType < 0 || string.IsNullOrWhiteSpace(control))
        {
            throw new InvalidDataException("The SDP did not describe a controllable H.264 video track.");
        }

        return new SdpVideoTrack(payloadType, clockRate, control, sps, pps);
    }
}
