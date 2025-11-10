using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

public class TcpServer
{
    private TcpListener _listener;
    private readonly int _port;
    private bool _isRunning;
    private readonly List<TcpClient> _clients = new();
    private readonly Random _random = new();

    public TcpServer(int port = 8888)
    {
        _port = port;
    }

    public async Task StartAsync()
    {
        _listener = new TcpListener(IPAddress.Any, _port);
        _listener.Start();
        _isRunning = true;

        Console.WriteLine($"TCP Server started on port {_port}");
        Console.WriteLine("Waiting for client connections...\n");

        // Accept clients in background
        _ = Task.Run(AcceptClientsAsync);

        // Generate and broadcast data
        await GenerateAndBroadcastDataAsync();
    }

    private async Task AcceptClientsAsync()
    {
        while (_isRunning)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync();
                _clients.Add(client);

                var endpoint = client.Client.RemoteEndPoint;
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Client connected: {endpoint}");
                Console.WriteLine($"Total connected clients: {_clients.Count}\n");
            }
            catch (Exception ex)
            {
                if (_isRunning)
                    Console.WriteLine($"Error accepting client: {ex.Message}");
            }
        }
    }

    private async Task GenerateAndBroadcastDataAsync()
    {
        int messageCounter = 0;

        while (_isRunning)
        {
            try
            {
                messageCounter++;

                // Rotate through different data types
                switch (messageCounter % 6)
                {
                    case 0:
                        await BroadcastStringData();
                        break;
                    case 1:
                        await BroadcastIntegerData();
                        break;
                    case 2:
                        await BroadcastByteArrayData();
                        break;
                    case 3:
                        await BroadcastDoubleData();
                        break;
                    case 4:
                        await BroadcastObjectCollection();
                        break;
                    case 5:
                        await BroadcastComplexJson();
                        break;
                }

                // Wait before sending next data
                await Task.Delay(1000);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in data generation: {ex.Message}");
            }
        }
    }

    private async Task BroadcastStringData()
    {
        var messages = new[]
        {
                "Hello from TCP Server!",
                "System Status: All services operational",
                "Real-time data streaming active",
                "Temperature monitoring in progress",
                "Network connectivity stable"
            };

        var message = messages[_random.Next(messages.Length)];
        var packet = new DataPacket
        {
            MessageType = "STRING",
            Data = message,
            ServerTime = DateTime.Now
        };

        await BroadcastToClients(packet, "String Message");
    }

    private async Task BroadcastIntegerData()
    {
        var value = _random.Next(1000, 9999);
        var packet = new DataPacket
        {
            MessageType = "INTEGER",
            Data = value,
            ServerTime = DateTime.Now
        };

        await BroadcastToClients(packet, $"Integer Value: {value}");
    }

    private async Task BroadcastDoubleData()
    {
        var value = Math.Round(_random.NextDouble() * 100, 2);
        var packet = new DataPacket
        {
            MessageType = "DOUBLE",
            Data = value,
            ServerTime = DateTime.Now
        };

        await BroadcastToClients(packet, $"Double Value: {value}");
    }

    private async Task BroadcastByteArrayData()
    {
        var byteArray = new byte[16];
        _random.NextBytes(byteArray);

        var packet = new DataPacket
        {
            MessageType = "BYTE_ARRAY",
            Data = Convert.ToBase64String(byteArray),
            ServerTime = DateTime.Now
        };

        await BroadcastToClients(packet, $"Byte Array ({byteArray.Length} bytes)");
    }

    private async Task BroadcastObjectCollection()
    {
        var sensors = new List<SensorData>
            {
                new() { Id = 1, Name = "Sensor-A", Temperature = Math.Round(20 + _random.NextDouble() * 10, 2), Timestamp = DateTime.Now, IsActive = true },
                new() { Id = 2, Name = "Sensor-B", Temperature = Math.Round(15 + _random.NextDouble() * 15, 2), Timestamp = DateTime.Now, IsActive = true },
                new() { Id = 3, Name = "Sensor-C", Temperature = Math.Round(25 + _random.NextDouble() * 5, 2), Timestamp = DateTime.Now, IsActive = _random.Next(2) == 0 }
            };

        var packet = new DataPacket
        {
            MessageType = "SENSOR_COLLECTION",
            Data = sensors,
            ServerTime = DateTime.Now
        };

        await BroadcastToClients(packet, $"Sensor Collection ({sensors.Count} sensors)");
    }

    private async Task BroadcastComplexJson()
    {
        var complexData = new
        {
            SystemInfo = new
            {
                ServerName = "TcpServer-01",
                Uptime = TimeSpan.FromHours(_random.Next(1, 100)).ToString(),
                ActiveConnections = _clients.Count,
                CpuUsage = Math.Round(_random.NextDouble() * 100, 1),
                MemoryUsage = Math.Round(_random.NextDouble() * 100, 1)
            },
            Metrics = new object[]
            {
                    new { Name = "RequestsPerSecond", Value = _random.Next(100, 1000) },
                    new { Name = "AverageLatency", Value = _random.Next(10, 100) },
                    new { Name = "ErrorRate", Value = Math.Round(_random.NextDouble() * 5, 2) }
                },
            Tags = new[] { "production", "monitoring", "realtime" }
        };

        var packet = new DataPacket
        {
            MessageType = "COMPLEX_JSON",
            Data = complexData,
            ServerTime = DateTime.Now
        };

        await BroadcastToClients(packet, "Complex JSON Data");
    }

    private async Task BroadcastToClients(DataPacket packet, string description)
    {
        var json = JsonSerializer.Serialize(packet, new JsonSerializerOptions
        {
            WriteIndented = false
        });

        var data = Encoding.UTF8.GetBytes(json + "\n");

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Broadcasting: {description}");

        var disconnectedClients = new List<TcpClient>();

        foreach (var client in _clients)
        {
            try
            {
                if (client.Connected)
                {
                    var stream = client.GetStream();
                    await stream.WriteAsync(data);
                    await stream.FlushAsync();
                }
                else
                {
                    disconnectedClients.Add(client);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending to client: {ex.Message}");
                disconnectedClients.Add(client);
            }
        }

        // Remove disconnected clients
        foreach (var client in disconnectedClients)
        {
            _clients.Remove(client);
            client.Close();
            Console.WriteLine($"Client disconnected. Remaining: {_clients.Count}");
        }
    }

    public void Stop()
    {
        _isRunning = false;

        foreach (var client in _clients)
        {
            client.Close();
        }

        _clients.Clear();
        _listener?.Stop();

        Console.WriteLine("\nServer stopped.");
    }
}

