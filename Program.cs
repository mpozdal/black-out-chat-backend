using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseWebSockets();

var clients = new ConcurrentBag<WebSocket>();

app.Map("/ws", async (HttpContext context) =>
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        clients.Add(webSocket);
        Console.WriteLine("Client connected");

        var buffer = new byte[1024 * 4];

        try
        {
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

                // Rozsyłanie do wszystkich klientów
                foreach (var client in clients)
                {
                    if (client.State == WebSocketState.Open)
                    {
                        var data = Encoding.UTF8.GetBytes(message);
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
            // Nie można usunąć z ConcurrentBag, ale stan i tak będzie zamknięty
            if (webSocket.State != WebSocketState.Closed)
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed", CancellationToken.None);
        }
    }
    else
    {
        context.Response.StatusCode = 400;
    }
});

var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
app.Run($"http://0.0.0.0:{port}");
