using Microsoft.AspNetCore.SignalR;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace SignalRTcpBridge_.Services;

public class TcpListenerService : BackgroundService
{
    private readonly ILogger<TcpListenerService> _logger;
    private readonly IHubContext<TcpDataHub> _hubContext;
    private readonly IConfiguration _configuration;
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private bool _isConnected;

    public bool IsConnected => _isConnected;

    public TcpListenerService(
        ILogger<TcpListenerService> logger,
        IHubContext<TcpDataHub> hubContext,
        IConfiguration configuration)
    {
        _logger = logger;
        _hubContext = hubContext;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TCP Listener Service starting...");

        var tcpHost = _configuration.GetValue<string>("TcpServer:Host") ?? "localhost";
        var tcpPort = _configuration.GetValue<int>("TcpServer:Port", 8888);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConnectToTcpServerAsync(tcpHost, tcpPort, stoppingToken);
                await ListenForTcpDataAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in TCP listener");
                _isConnected = false;

                // Notify SignalR clients about disconnection
                await _hubContext.Clients.All.SendAsync(
                    "TcpConnectionStatus",
                    new { Connected = false, Message = "Disconnected from TCP server" },
                    stoppingToken);
            }

            // Wait before reconnecting
            if (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Reconnecting to TCP server in 5 seconds...");
                await Task.Delay(5000, stoppingToken);
            }
        }

        Cleanup();
    }

    private async Task ConnectToTcpServerAsync(string host, int port, CancellationToken cancellationToken)
    {
        _logger.LogInformation($"Connecting to TCP server at {host}:{port}...");

        _tcpClient = new TcpClient();
        await _tcpClient.ConnectAsync(host, port, cancellationToken);
        _stream = _tcpClient.GetStream();
        _isConnected = true;

        _logger.LogInformation("Connected to TCP server successfully!");

        // Notify SignalR clients about connection
        await _hubContext.Clients.All.SendAsync(
            "TcpConnectionStatus",
            new { Connected = true, Message = $"Connected to TCP server at {host}:{port}" },
            cancellationToken);
    }

    private async Task ListenForTcpDataAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var messageBuilder = new StringBuilder();

        while (_isConnected && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_stream == null || !_stream.DataAvailable)
                {
                    await Task.Delay(100, cancellationToken);
                    continue;
                }

                var bytesRead = await _stream.ReadAsync(buffer, cancellationToken);

                if (bytesRead == 0)
                {
                    _logger.LogWarning("TCP connection closed by server");
                    _isConnected = false;
                    break;
                }

                var data = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                messageBuilder.Append(data);

                // Process complete messages (separated by newlines)
                var messages = messageBuilder.ToString().Split('\n');

                // Keep the last incomplete message in the builder
                messageBuilder.Clear();
                if (!data.EndsWith('\n'))
                {
                    messageBuilder.Append(messages[^1]);
                    messages = messages[..^1];
                }

                // Broadcast each complete message to SignalR clients
                foreach (var message in messages)
                {
                    if (string.IsNullOrWhiteSpace(message))
                        continue;

                    try
                    {
                        // Parse and validate JSON
                        var jsonDoc = JsonDocument.Parse(message);
                        var jsonObject = JsonSerializer.Deserialize<JsonElement>(message);

                        _logger.LogDebug($"Broadcasting message type: {jsonObject.GetProperty("MessageType").GetString()}");

                        // Send to all connected SignalR clients
                        await _hubContext.Clients.All.SendAsync(
                            "ReceiveTcpData",
                            jsonObject,
                            cancellationToken);
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning($"Invalid JSON received: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("TCP listener cancelled");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading from TCP stream");
                _isConnected = false;
                throw;
            }
        }
    }

    private void Cleanup()
    {
        _stream?.Close();
        _tcpClient?.Close();
        _isConnected = false;
        _logger.LogInformation("TCP Listener Service stopped");
    }

    public override void Dispose()
    {
        Cleanup();
        base.Dispose();
    }
}