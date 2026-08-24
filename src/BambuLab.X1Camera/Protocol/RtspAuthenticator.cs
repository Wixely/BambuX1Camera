using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BambuLab.X1Camera.Protocol;

internal abstract class RtspAuthenticator
{
    public static RtspAuthenticator Parse(string challenge, string username, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(challenge);

        if (challenge.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)
            || challenge.Equals("Basic", StringComparison.OrdinalIgnoreCase))
        {
            return new BasicAuthenticator(username, password);
        }

        if (!challenge.StartsWith("Digest ", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("The printer requested an unsupported RTSP authentication scheme.");
        }

        var parameters = ParseParameters(challenge[7..]);
        if (!parameters.TryGetValue("realm", out var realm) || !parameters.TryGetValue("nonce", out var nonce))
        {
            throw new InvalidDataException("The RTSP digest challenge omitted its realm or nonce.");
        }

        parameters.TryGetValue("opaque", out var opaque);
        parameters.TryGetValue("algorithm", out var algorithm);
        parameters.TryGetValue("qop", out var qop);
        if (!string.IsNullOrWhiteSpace(algorithm) && !algorithm.Equals("MD5", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException($"RTSP digest algorithm '{algorithm}' is not supported.");
        }

        var supportsAuthQop = string.IsNullOrWhiteSpace(qop)
            || qop.Split(',').Any(value => value.Trim().Equals("auth", StringComparison.OrdinalIgnoreCase));
        if (!supportsAuthQop)
        {
            throw new NotSupportedException("The RTSP digest challenge does not offer the 'auth' quality of protection.");
        }

        return new DigestAuthenticator(username, password, realm, nonce, opaque, string.IsNullOrWhiteSpace(qop) ? null : "auth");
    }

    public abstract string CreateAuthorization(string method, Uri requestUri);

    private static Dictionary<string, string> ParseParameters(string input)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        while (index < input.Length)
        {
            while (index < input.Length && (input[index] == ',' || char.IsWhiteSpace(input[index])))
            {
                index++;
            }

            var equals = input.IndexOf('=', index);
            if (equals < 0)
            {
                break;
            }

            var name = input[index..equals].Trim();
            index = equals + 1;
            string value;
            if (index < input.Length && input[index] == '"')
            {
                index++;
                var builder = new StringBuilder();
                while (index < input.Length)
                {
                    var current = input[index++];
                    if (current == '"')
                    {
                        break;
                    }

                    if (current == '\\' && index < input.Length)
                    {
                        current = input[index++];
                    }

                    builder.Append(current);
                }

                value = builder.ToString();
            }
            else
            {
                var comma = input.IndexOf(',', index);
                var end = comma < 0 ? input.Length : comma;
                value = input[index..end].Trim();
                index = end;
            }

            result[name] = value;
        }

        return result;
    }

    private sealed class BasicAuthenticator(string username, string password) : RtspAuthenticator
    {
        private readonly string value = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));

        public override string CreateAuthorization(string method, Uri requestUri) => $"Basic {value}";
    }

    private sealed class DigestAuthenticator(
        string username,
        string password,
        string realm,
        string nonce,
        string? opaque,
        string? qop) : RtspAuthenticator
    {
        private int nonceCount;

        public override string CreateAuthorization(string method, Uri requestUri)
        {
            var uri = requestUri.AbsoluteUri;
            var ha1 = Md5Hex($"{username}:{realm}:{password}");
            var ha2 = Md5Hex($"{method}:{uri}");
            var builder = new StringBuilder("Digest ")
                .Append("username=\"").Append(Escape(username)).Append("\", ")
                .Append("realm=\"").Append(Escape(realm)).Append("\", ")
                .Append("nonce=\"").Append(Escape(nonce)).Append("\", ")
                .Append("uri=\"").Append(Escape(uri)).Append("\", ");

            string response;
            if (qop is not null)
            {
                var count = Interlocked.Increment(ref nonceCount).ToString("x8", CultureInfo.InvariantCulture);
                var cnonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
                response = Md5Hex($"{ha1}:{nonce}:{count}:{cnonce}:{qop}:{ha2}");
                builder.Append("qop=").Append(qop).Append(", nc=").Append(count)
                    .Append(", cnonce=\"").Append(cnonce).Append("\", ");
            }
            else
            {
                response = Md5Hex($"{ha1}:{nonce}:{ha2}");
            }

            builder.Append("response=\"").Append(response).Append("\", algorithm=MD5");
            if (!string.IsNullOrEmpty(opaque))
            {
                builder.Append(", opaque=\"").Append(Escape(opaque)).Append('"');
            }

            return builder.ToString();
        }

        private static string Md5Hex(string value) => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

        private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}

