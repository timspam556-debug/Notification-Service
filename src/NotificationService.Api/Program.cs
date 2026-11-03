var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.MapHealthChecks("/health");

app.Run();

// Позволяет интеграционным тестам запустить приложение через WebApplicationFactory.
public partial class Program;
