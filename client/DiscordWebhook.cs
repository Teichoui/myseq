using System;
using System.Net;
using System.Net.Http;
using System.Text;

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
        /// an http(s) address, the request times out, or Discord answers with an error. Discord
        /// asking us to slow down (HTTP 429) is retried once if it names a short wait.
        /// This blocks, so call it from a worker thread.
        /// </summary>
        public static void Send(string webhookUrl, string content, int timeoutSeconds = 15)
        {
            Uri uri;
            if (string.IsNullOrWhiteSpace(webhookUrl)
                || !Uri.TryCreate(webhookUrl.Trim(), UriKind.Absolute, out uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                throw new ArgumentException("The Discord webhook URL is not a valid http or https address.");
            }

            EnableTls12();
            string payload = BuildPayload(content);

            using (HttpClient client = new HttpClient())
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

                    using (response)
                    {
                        if (response.IsSuccessStatusCode)
                            return;

                        int status = (int)response.StatusCode;

                        if (status == 429 && attempt == 0)
                        {
                            TimeSpan wait = response.Headers.RetryAfter != null && response.Headers.RetryAfter.Delta.HasValue
                                ? response.Headers.RetryAfter.Delta.Value
                                : TimeSpan.FromSeconds(1);

                            if (wait <= TimeSpan.FromSeconds(MaxRetryAfterSeconds))
                            {
                                System.Threading.Thread.Sleep(wait);
                                continue;
                            }
                        }

                        string detail = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        if (detail.Length > 200)
                            detail = detail.Substring(0, 200);
                        throw new HttpRequestException("Discord returned " + status + " " + response.ReasonPhrase + (detail.Length > 0 ? ": " + detail : ""));
                    }
                }
            }
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
