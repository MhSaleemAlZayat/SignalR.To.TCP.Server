using Serilog;
using SignalRTcpBridge_.Services;

var builder = WebApplication.CreateBuilder(args);

// Add configuration
//builder.Configuration.AddJsonFile("appsettings.json", optional: false);

//builder.Host.UseSerilog((context, services, configuration) => configuration
//        .ReadFrom.Configuration(new ConfigurationBuilder()
//            .AddJsonFile("appsettings.json")
//            .Build())
//        .CreateLogger());
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration) // Use the context parameter
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());


// Add services to the container.
builder.Services.AddControllersWithViews()
    .AddRazorRuntimeCompilation();

builder.Services.AddSignalR();
builder.Services.AddHostedService<TcpListenerService>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});



var app = builder.Build();

app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000}ms";
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
        diagnosticContext.Set("RemoteIP", httpContext.Connection.RemoteIpAddress);
    };
});

// Configure middleware
app.UseCors();
app.UseRouting();

// Map SignalR hub
app.MapHub<TcpDataHub>("/tcpHub");

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
