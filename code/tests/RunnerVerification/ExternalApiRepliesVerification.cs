using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BigBlueFish.Windows;

// Drives the real external-API request path against a local OpenAI-compatible
// fixture, covering the replies that do not carry any speech text: a thinking
// model that spends the budget on reasoning, the same model when disabling
// reasoning is not honoured, and a server that ignores stream=true.
internal static class ExternalApiRepliesVerification
{
    private enum Mode { ThinkingThenContent, ThinkingOnly, PlainJson }

    private sealed class Fixture : IDisposable
    {
        private readonly HttpListener listener = new();
        private Task? loop;
        internal Mode Mode;
        internal int Requests;
        internal bool SawReasoningOff;

        internal void Start(int port)
        {
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();
            loop = Loop();
        }

        internal void Reset(Mode mode)
        {
            Mode = mode;
            Requests = 0;
            SawReasoningOff = false;
        }

        private async Task Loop()
        {
            while (true)
            {
                HttpListenerContext context;
                try { context = await listener.GetContextAsync(); }
                catch (Exception error) when (error is HttpListenerException or ObjectDisposedException or InvalidOperationException) { return; }
                try { await Handle(context); }
                catch (Exception)
                {
                    try { context.Response.StatusCode = 500; context.Response.Close(); } catch (Exception) { }
                }
            }
        }

        private async Task Handle(HttpListenerContext context)
        {
            using var document = await JsonDocument.ParseAsync(context.Request.InputStream);
            string? effort = document.RootElement.TryGetProperty("reasoning_effort", out var effortElement)
                && effortElement.ValueKind == JsonValueKind.String ? effortElement.GetString() : null;
            string model = document.RootElement.TryGetProperty("model", out var modelElement) ? modelElement.GetString() ?? "" : "";
            Interlocked.Increment(ref Requests);
            if (effort == "none") SawReasoningOff = true;

            if (Mode == Mode.PlainJson)
            {
                string json = JsonSerializer.Serialize(new
                {
                    id = "chatcmpl-fixture", @object = "chat.completion", model,
                    choices = new[] { new { index = 0, message = new { role = "assistant", content = "纯 JSON 回答" }, finish_reason = "stop" } }
                });
                await Write(context, "application/json", json);
                return;
            }

            bool reasoningOnly = Mode == Mode.ThinkingOnly || effort != "none";
            var body = new StringBuilder();
            body.Append(Chunk(model, new { role = "assistant", content = "" }, null));
            if (reasoningOnly)
            {
                foreach (string part in new[] { "用户", "在测试", "吐字", "，先想", "一想", "怎么回答", "……" })
                    body.Append(Chunk(model, new { reasoning = part }, null));
                body.Append(Chunk(model, new { }, "length"));
            }
            else
            {
                foreach (string part in new[] { "主人", "，鱼鱼", "听到", "你的", "声音", "啦～" })
                    body.Append(Chunk(model, new { content = part }, null));
                body.Append(Chunk(model, new { }, "stop"));
            }
            body.Append("data: [DONE]\n\n");
            await Write(context, "text/event-stream", body.ToString());
        }

        private static string Chunk(string model, object delta, string? finish)
            => "data: " + JsonSerializer.Serialize(new
            {
                id = "chatcmpl-fixture", @object = "chat.completion.chunk", model,
                choices = new[] { new { index = 0, delta, finish_reason = finish } }
            }, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull }) + "\n\n";

        private static async Task Write(HttpListenerContext context, string contentType, string body)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            context.Response.StatusCode = 200;
            context.Response.ContentType = contentType;
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }

        public void Dispose()
        {
            listener.Close();
            try { loop?.Wait(TimeSpan.FromSeconds(5)); } catch (AggregateException) { }
        }
    }

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    internal static async Task<int> Run()
    {
        int checks = 0;
        void Check(bool value, string message)
        {
            if (!value) throw new Exception(message);
            checks++;
            Console.WriteLine("PASS " + message);
        }

        int port = FreePort();
        using var fixture = new Fixture();
        fixture.Start(port);
        using var brain = new CompanionBrain(Process.Start, TimeSpan.FromSeconds(30), newsRoll: () => 1)
        {
            StopOnBattery = false,
            UseExternalApi = true,
            ExternalApiUrl = $"http://127.0.0.1:{port}/v1",
            ExternalApiModel = "qwen3:4b"
        };

        fixture.Reset(Mode.ThinkingThenContent);
        string answer = await brain.Generate(false, false);
        Check(answer.Contains("鱼鱼"), $"thinking model answers after one reasoning-disabled retry: {answer}");
        Check(fixture.Requests == 2 && fixture.SawReasoningOff,
            "the retry asks the same endpoint for an answer with reasoning_effort=none");

        fixture.Reset(Mode.ThinkingOnly);
        string thinking = await brain.Generate(false, false);
        Check(thinking.Contains("思考"), $"a reasoning-only reply explains the thinking model instead of a generic failure: {thinking}");
        Check(fixture.Requests == 2, "a server that ignores reasoning_effort is not retried twice");

        fixture.Reset(Mode.PlainJson);
        string plain = await brain.Generate(false, false);
        Check(plain.Contains("流式"), $"a non-SSE 200 reply points at the OpenAI-compatible address: {plain}");
        Check(fixture.Requests == 1, "a non-streaming reply is not retried");

        Console.WriteLine($"RESULT {checks} external API reply checks passed");
        return 0;
    }
}
