using Microsoft.AspNetCore.SignalR;
using Serilog;

namespace SignalRTcpBridge_.Services;

public class TcpDataHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var remoteEndPoint = Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString();

        Log.Information("SignalR client connected {@ConnectionId} from {@RemoteEndPoint}",
            Context.ConnectionId, remoteEndPoint);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (exception != null)
        {
            Log.Warning(exception, "SignalR client disconnected with error {@ConnectionId}",
                Context.ConnectionId);
        }
        else
        {
            Log.Information("SignalR client disconnected {@ConnectionId}",
                Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task GetConnectionStatus()
    {
        var service = Context.GetHttpContext()?.RequestServices
            .GetService<TcpListenerService>();

        var status = new
        {
            IsConnected = service?.IsConnected ?? false,
            TcpEndPoint = service?.TcpEndPoint,
            ConnectedClients = Context.Items.Count,
            RequestedBy = Context.ConnectionId
        };

        Log.Debug("Connection status requested {@Status}", status);

        await Clients.Caller.SendAsync("ConnectionStatus", status);
    }
}