using Microsoft.AspNetCore.SignalR;

namespace SignalRTcpBridge_.Services;

public class TcpDataHub : Hub
{
    private readonly ILogger<TcpDataHub> _logger;
    public TcpDataHub(ILogger<TcpDataHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation($"Client connected: {Context.ConnectionId}");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation($"Client disconnected: {Context.ConnectionId}");
        await base.OnDisconnectedAsync(exception);
    }

    // Optional: Allow clients to request connection status
    public async Task GetConnectionStatus()
    {
        var service = Context.GetHttpContext()?.RequestServices
            .GetService<TcpListenerService>();

        var status = new
        {
            IsConnected = service?.IsConnected ?? false,
            ConnectedClients = Context.Items.Count
        };

        await Clients.Caller.SendAsync("ConnectionStatus", status);
    }
}
