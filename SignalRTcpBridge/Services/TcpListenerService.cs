using Microsoft.AspNetCore.SignalR;
using Serilog;
using Serilog.Context;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace SignalRTcpBridge_.Services;
public class TcpListenerService : BackgroundService
{
    private readonly IHubContext<TcpDataHub> _hubContext;
    private readonly IConfiguration _configuration;
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private bool _isConnected;
    private string? _tcpEndPoint;
    private long _messageCounter;
    private long _bytesReceived;

    public bool IsConnected => _isConnected;
    public string? TcpEndPoint => _tcpEndPoint;

    public TcpListenerService(
        IHubContext<TcpDataHub> hubContext,
        IConfiguration configuration)
    {
        _hubContext = hubContext;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Log.Information("TCP Listener Service starting");

        var tcpHost = _configuration.GetValue<string>("TcpServer:Host") ?? "localhost";
        var tcpPort = _configuration.GetValue<int>("TcpServer:Port", 8888);
        _tcpEndPoint = $"{tcpHost}:{tcpPort}";

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConnectToTcpServerAsync(tcpHost, tcpPort, stoppingToken);
                await ListenForTcpDataAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                Log.Information("TCP Listener Service shutdown requested");
                break;
            }
            catch (Exception ex)
            {
                _isConnected = false;

                Log.Error(ex, "TCP Listener encountered error {@TcpEndPoint} {@MessageCount} {@BytesReceived}",
                    _tcpEndPoint, _messageCounter, _bytesReceived);

                // Notify SignalR clients about disconnection
                await _hubContext.Clients.All.SendAsync(
                    "TcpConnectionStatus",
                    new
                    {
                        Connected = false,
                        Message = "Disconnected from TCP server",
                        EndPoint = _tcpEndPoint,
                        Error = ex.Message
                    },
                    stoppingToken);
            }

            // Wait before reconnecting
            if (!stoppingToken.IsCancellationRequested)
            {
                var reconnectDelay = 5000;
                Log.Warning("Reconnecting to TCP server in {ReconnectDelayMs}ms {@TcpEndPoint}",
                    reconnectDelay, _tcpEndPoint);
                await Task.Delay(reconnectDelay, stoppingToken);
            }
        }

        Cleanup();
        Log.Information("TCP Listener Service stopped {@Statistics}", new
        {
            TotalMessages = _messageCounter,
            TotalBytes = _bytesReceived,
            EndPoint = _tcpEndPoint
        });
    }

    private async Task ConnectToTcpServerAsync(string host, int port, CancellationToken cancellationToken)
    {
        using (LogContext.PushProperty("TcpEndPoint", $"{host}:{port}"))
        {
            Log.Information("Attempting TCP connection {@Host} {@Port}", host, port);

            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync(host, port, cancellationToken);
            _stream = _tcpClient.GetStream();
            _isConnected = true;

            var localEndPoint = _tcpClient.Client.LocalEndPoint?.ToString();
            var remoteEndPoint = _tcpClient.Client.RemoteEndPoint?.ToString();

            Log.Information("TCP connection established {@LocalEndPoint} {@RemoteEndPoint}",
                localEndPoint, remoteEndPoint);

            // Notify SignalR clients about connection
            await _hubContext.Clients.All.SendAsync(
                "TcpConnectionStatus",
                new
                {
                    Connected = true,
                    Message = $"Connected to TCP server",
                    LocalEndPoint = localEndPoint,
                    RemoteEndPoint = remoteEndPoint,
                    Timestamp = DateTime.UtcNow
                },
                cancellationToken);
        }
    }

    private async Task ListenForTcpDataAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var messageBuilder = new StringBuilder();
        var remoteEndPoint = _tcpClient?.Client.RemoteEndPoint?.ToString();

        using (LogContext.PushProperty("RemoteEndPoint", remoteEndPoint))
        {
            Log.Information("Started listening for TCP data {@RemoteEndPoint}", remoteEndPoint);

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
                        Log.Warning("TCP connection closed by remote server {@RemoteEndPoint} {@MessageCount}",
                            remoteEndPoint, _messageCounter);
                        _isConnected = false;
                        break;
                    }

                    _bytesReceived += bytesRead;

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

                        await ProcessTcpMessageAsync(message, remoteEndPoint, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    Log.Information("TCP listener cancelled {@RemoteEndPoint}", remoteEndPoint);
                    break;
                }
                catch (Exception ex)
                {
                    _isConnected = false;
                    Log.Error(ex, "Error reading from TCP stream {@RemoteEndPoint} {@BytesRead}",
                        remoteEndPoint, _bytesReceived);
                    throw;
                }
            }
        }
    }

    private async Task ProcessTcpMessageAsync(string message, string? remoteEndPoint, CancellationToken cancellationToken)
    {
        _messageCounter++;
        var messageId = _messageCounter;

        try
        {
            // Parse JSON
            var jsonDoc = JsonDocument.Parse(message);
            var jsonObject = JsonSerializer.Deserialize<JsonElement>(message);

            var messageType = jsonObject.GetProperty("MessageType").GetString();
            var serverTime = jsonObject.GetProperty("ServerTime");
            var data = jsonObject.GetProperty("Data");
            
            // Structured data logging - logs to data-.log

            Log.ForContext("SourceContext", "TcpData")
               .Information("Received TCP message {MessageId} {@MessageType} {@Data} {@ServerTime} {@RemoteEndPoint} {@BytesReceived}",
                   messageId, messageType, (messageType == "COMPLEX_JSON" ? data.ToString() : data.ToString()), serverTime, remoteEndPoint, _bytesReceived);

            // Send to all connected SignalR clients
            await _hubContext.Clients.All.SendAsync(
                "ReceiveTcpData",
                jsonObject,
                cancellationToken);

            // Log successful broadcast
            Log.Debug("Broadcasted message {MessageId} {@MessageType} to SignalR clients",
                messageId, messageType);
        }
        catch (JsonException ex)
        {
            Log.Warning(ex, "Invalid JSON received {MessageId} {@MessagePreview} {@RemoteEndPoint}",
                messageId,
                message.Length > 100 ? message[..100] + "..." : message,
                remoteEndPoint);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error processing TCP message {MessageId} {@RemoteEndPoint}",
                messageId, remoteEndPoint);
        }
    }

    private void Cleanup()
    {
        try
        {
            _stream?.Close();
            _tcpClient?.Close();
            _isConnected = false;

            Log.Information("TCP connection cleanup completed {@TcpEndPoint}", _tcpEndPoint);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during TCP connection cleanup {@TcpEndPoint}", _tcpEndPoint);
        }
    }

    public override void Dispose()
    {
        Cleanup();
        base.Dispose();
    }
}
