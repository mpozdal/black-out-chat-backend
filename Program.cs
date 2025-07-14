using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace BlackOutChatServer
{
    public class Program
    {
        record ChatMessage(string type, string user, string ciphertext, string iv, string clientId, long Timestamp, long ExpiresAt);

        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();

            app.UseWebSockets();

            var clients = new ConcurrentBag<WebSocket>();
            var messages = new ConcurrentQueue<ChatMessage>();

            app.Map("/ws", async (HttpContext context) =>
            {
                if (!context.WebSockets.IsWebSocketRequest)
                {
                    context.Response.StatusCode = 400;
                    return;
                }

                var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                clients.Add(webSocket);
                Console.WriteLine("Client connected");

                var buffer = new byte[1024 * 8];

                try
                {
                    // Wyślij aktywne wiadomości po połączeniu
                    var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    var activeMessages = messages.Where(m => m.ExpiresAt > now).ToList();

                    foreach (var msg in activeMessages)
                    {
                        var json = JsonSerializer.Serialize(msg);
                        var data = Encoding.UTF8.GetBytes(json);
                        await webSocket.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, true, CancellationToken.None);
                    }

                    // Obsługa nowej wiadomości
                    while (true)
                    {
                        var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            Console.WriteLine("Client disconnected");
                            break;
                        }

                        var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        Console.WriteLine($"Received: {message}");

                        var incoming = JsonSerializer.Deserialize<ChatMessage>(message);

                        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        var msg = new ChatMessage(
                            type: incoming.type,
                            user: incoming!.user,
                            clientId: incoming.clientId,
                            ciphertext: incoming.ciphertext,
                            iv: incoming.iv,
                            Timestamp: nowMs,
                            ExpiresAt: nowMs + 15000
                        );

                        messages.Enqueue(msg);

                        var json = JsonSerializer.Serialize(msg);
                        var data = Encoding.UTF8.GetBytes(json);

                        foreach (var client in clients)
                        {
                            if (client.State == WebSocketState.Open)
                            {
                                await client.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, true, CancellationToken.None);
                            }
                        }
                    }
                }
                catch
                {
                    Console.WriteLine("Client forcibly disconnected");
                }
                finally
                {
                    if (webSocket.State != WebSocketState.Closed)
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed", CancellationToken.None);
                }
            });

            // (Opcjonalnie) czyść wygasłe wiadomości co minutę
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(60000);
                    var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    var fresh = new ConcurrentQueue<ChatMessage>(
                        messages.Where(m => m.ExpiresAt > now)
                    );

                    while (messages.TryDequeue(out _)) { }
                    foreach (var msg in fresh)
                        messages.Enqueue(msg);

                    Console.WriteLine("Expired messages cleared");
                }
            });

            var port = Environment.GetEnvironmentVariable("PORT") ?? "5001";
            app.Run($"http://0.0.0.0:{port}");
        }
    }
}
