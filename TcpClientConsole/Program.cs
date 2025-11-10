using System;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

string server = args.Length > 0 ? args[0] : "localhost";
int port = args.Length > 1 ? int.Parse(args[1]) : 8888;

Console.WriteLine("═══════════════════════════════════════");
Console.WriteLine("   TCP Client Tester - .NET 8.0");
Console.WriteLine("═══════════════════════════════════════");
Console.WriteLine($"Connecting to {server}:{port}...\n");

try
{
    using var client = new TcpClient();
    await client.ConnectAsync(server, port);

    Console.WriteLine("✓ Connected successfully!");
    Console.WriteLine("Press Ctrl+C to disconnect\n");
    Console.WriteLine("Waiting for messages...\n");

    using var stream = client.GetStream();
    var buffer = new byte[8192];
    int messageCount = 0;

    while (true)
    {
        var bytesRead = await stream.ReadAsync(buffer);
        if (bytesRead == 0) break;

        var message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
        var messages = message.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var msg in messages)
        {
            messageCount++;
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"[{messageCount}] {DateTime.Now:HH:mm:ss.fff}");
            Console.ResetColor();

            try
            {
                // Try to parse as JSON
                using var doc = JsonDocument.Parse(msg);
                var root = doc.RootElement;

                var messageType = root.GetProperty("MessageType").GetString();
                var serverTime = root.GetProperty("ServerTime").GetString();

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Type: {messageType}");
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine($"Server Time: {serverTime}");

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Data:");

                // Pretty print JSON
                var options = new JsonSerializerOptions { WriteIndented = true };
                var formatted = JsonSerializer.Serialize(root.GetProperty("Data"), options);
                Console.WriteLine(formatted);
                Console.ResetColor();
            }
            catch
            {
                // If not JSON, print raw
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine($"Raw: {msg}");
                Console.ResetColor();
            }

            Console.WriteLine(new string('─', 50));
            Console.WriteLine();
        }
    }
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"\nError: {ex.Message}");
    Console.ResetColor();
}

Console.WriteLine("\nConnection closed. Press any key to exit...");
Console.ReadKey();