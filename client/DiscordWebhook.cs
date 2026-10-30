using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace myseq
{
    /// <summary>
    /// Posts a text message to a Discord webhook with a plain HTTPS request, so the client does not
    /// need the Discord.Net library (and the dozens of packages it pulls in) for one POST.
    /// </summary>
    public static class DiscordWebhook
    {
        /// <summary>Discord rejects messages longer than this.</summary>
        public const int MaxContentLength = 2000;

        private const int MaxRetryAfterSeconds = 10;

        /// <summary>
        /// Sends content to the webhook and returns when Discord accepts it. Throws if the URL is not
        /// an https address (http is accepted for localhost only, for testing), the request times out
        /// or cannot connect, or Discord answers with an error. Discord asking us to slow down
        /// (HTTP 429) is retried once if it names a short wait.
        /// This blocks, so call it from a worker thread.
        /// </summary>
        public static void Send(string webhookUrl, string content, int timeoutSeconds = 15)
        {
            // A webhook URL carries a secret token, so only send it over https. http is allowed for
            // localhost, which is how the tests run a fake Discord.
            Uri uri;
            if (string.IsNullOrWhiteSpace(webhookUrl)
                || !Uri.TryCreate(webhookUrl.Trim(), UriKind.Absolute, out uri)
                || !(uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
            {
                throw new ArgumentException("The Discord webhook URL must be an https address (http is only allowed for localhost).");
            }

            EnableTls12();
            string payload = BuildPayload(content);

            // Never follow a redirect: it would re-send the message and the token in the URL somewhere else.
            using (HttpClientHandler handler = new HttpClientHandler { AllowAutoRedirect = false })
            using (HttpClient client = new HttpClient(handler))
            {
                client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

                for (int attempt = 0; ; attempt++)
                {
                    HttpResponseMessage response;
                    try
                    {
                        using (StringContent body = new StringContent(payload, Encoding.UTF8, "application/json"))
                            response = client.PostAsync(uri, body).GetAwaiter().GetResult();
                    }
                    catch (System.Threading.Tasks.TaskCanceledException)
                    {
                        throw new TimeoutException("Discord did not answer within " + timeoutSeconds + " seconds.");
                    }
                    catch (HttpRequestException ex)
                    {
                        // HttpClient's own message is only "An error occurred while sending the request."
                        // The cause (name not found, connection refused, TLS failure) is in the innermost exception.
                        Exception root = ex;
                        while (root.InnerException != null)
                            root = root.InnerException;
                        throw new HttpRequestException("Could not reach Discord: " + root.Message, ex);
                    }

                    using (response)
                    {
                        if (response.IsSuccessStatusCode)
                            return;

                        int status = (int)response.StatusCode;
                        string detail = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                        if (status == 429 && attempt == 0)
                        {
                            TimeSpan wait = RetryDelay(response, detail);

                            if (wait <= TimeSpan.FromSeconds(MaxRetryAfterSeconds))
                            {
                                System.Threading.Thread.Sleep(wait);
                                continue;
                            }
                        }

                        if (detail.Length > 200)
                            detail = detail.Substring(0, 200);
                        throw new HttpRequestException("Discord returned " + status + " " + response.ReasonPhrase + (detail.Length > 0 ? ": " + detail : ""));
                    }
                }
            }
        }

        /// <summary>
        /// How long Discord wants us to wait after a 429. Its JSON body gives retry_after in seconds with
        /// fractions ({"retry_after": 1.75}); the Retry-After header is the fallback, and it is read as
        /// fractional too because HttpClient's typed parser rejects anything but whole seconds.
        /// Defaults to one second if neither can be read.
        /// </summary>
        internal static TimeSpan RetryDelay(HttpResponseMessage response, string body)
        {
            double seconds;

            Match fromBody = Regex.Match(body ?? "", "\"retry_after\"\\s*:\\s*([0-9]+(?:\\.[0-9]+)?)");
            if (fromBody.Success && double.TryParse(fromBody.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds))
                return TimeSpan.FromSeconds(seconds);

            if (response.Headers.RetryAfter != null && response.Headers.RetryAfter.Delta.HasValue)
                return response.Headers.RetryAfter.Delta.Value < TimeSpan.Zero ? TimeSpan.Zero : response.Headers.RetryAfter.Delta.Value;

            IEnumerable<string> raw;
            if (response.Headers.TryGetValues("Retry-After", out raw))
            {
                foreach (string value in raw)
                {
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) && seconds >= 0 && !double.IsInfinity(seconds))
                        return TimeSpan.FromSeconds(seconds);
                }
            }

            return TimeSpan.FromSeconds(1);
        }

        /// <summary>The JSON body Discord expects: {"content":"..."}, cut to the 2000 character limit.</summary>
        public static string BuildPayload(string content)
        {
            if (content == null)
                content = "";

            if (content.Length > MaxContentLength)
            {
                content = content.Substring(0, MaxContentLength);

                // Do not leave half of a surrogate pair (an emoji, say) at the cut.
                if (char.IsHighSurrogate(content[content.Length - 1]))
                    content = content.Substring(0, content.Length - 1);
            }

            StringBuilder json = new StringBuilder(content.Length + 16);
            json.Append("{\"content\":\"");

            foreach (char c in content)
            {
                switch (c)
                {
                    case '"': json.Append("\\\""); break;
                    case '\\': json.Append("\\\\"); break;
                    case '\n': json.Append("\\n"); break;
                    case '\r': json.Append("\\r"); break;
                    case '\t': json.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            json.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            json.Append(c);
                        break;
                }
            }

            json.Append("\"}");
            return json.ToString();
        }

        // Discord requires TLS 1.2. A .NET Framework 4.5 app does not turn it on by default, so add it
        // to whatever the process already allows. The "system default" setting (0) already includes it.
        private static void EnableTls12()
        {
            SecurityProtocolType current = ServicePointManager.SecurityProtocol;

            if ((int)current != 0 && (current & SecurityProtocolType.Tls12) == 0)
                ServicePointManager.SecurityProtocol = current | SecurityProtocolType.Tls12;
        }
    }
}
