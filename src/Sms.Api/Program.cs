var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "SMS API");

app.Run();

public partial class Program { } // for WebApplicationFactory in integration tests
