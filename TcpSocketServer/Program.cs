using System;
using System.Collections.Generic;
using System.Threading.Tasks;

Console.WriteLine("═══════════════════════════════════════");
Console.WriteLine("   TCP Socket Server - .NET 9.0");
Console.WriteLine("═══════════════════════════════════════\n");

var server = new TcpServer(port: 8888);

// Handle graceful shutdown
Console.CancelKeyPress += (sender, e) =>
{
    e.Cancel = true;
    Console.WriteLine("\n\nShutting down server...");
    server.Stop();
    Environment.Exit(0);
};

try
{
    await server.StartAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"Server error: {ex.Message}");
}

