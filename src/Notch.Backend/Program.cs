using Notch.Backend;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

if (args is ["--desktop"])
{
    // Private inherited pipe: no static secret, command-line credential or settings file.
    var token = await Console.In.ReadLineAsync();
    if (token is null || token.Length != 64 || !token.All(Uri.IsHexDigit)) return;
    await using var app = BackendApplication.Build([], builder =>
    {
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
    }, token);
    // EOF also arrives if the desktop crashes: do not leave an orphan server.
    _ = Task.Run(async () =>
    {
        await Console.In.ReadToEndAsync();
        app.Lifetime.StopApplication();
    });
    await app.StartAsync();
    var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!;
    Console.WriteLine(addresses.Addresses.Single());
    await Console.Out.FlushAsync();
    await app.WaitForShutdownAsync();
}
else
{
    await BackendApplication.Build(args).RunAsync();
}

