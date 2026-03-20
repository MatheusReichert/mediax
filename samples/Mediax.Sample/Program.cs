using Mediax.AspNetCore;
using Mediax.Behaviors;
using Mediax.Core;
using Mediax.Runtime;
using Mediax.Sample.Orders;

var builder = WebApplication.CreateBuilder(args);

// Register Mediax — source generator discovers all [Handler]-annotated classes
builder.Services.AddMediax(pipeline =>
{
    // Register global behaviors (applied to every request in order)
    pipeline.UseGlobal(typeof(LogBehavior<,>));
    pipeline.UseGlobal(typeof(TracingBehavior<,>));
    pipeline.UseGlobal(typeof(ValidationBehavior<,>));
});

builder.Services.AddLogging();

var app = builder.Build();

// Initialize the Mediax runtime with the DI container
app.UseMediax();

// Minimal API endpoints
app.MapPost("/orders", async (CreateOrderCommand command, CancellationToken ct) =>
{
    var result = await command.Send(ct);
    return result.Match(
        ok: id => Results.Created($"/orders/{id}", new { id }),
        fail: err => err.Type switch
        {
            ErrorType.Validation => Results.ValidationProblem(
                err.Details?.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal) ?? []),
            _ => Results.Problem(err.Message, statusCode: 500)
        });
});

app.MapGet("/orders/{id:guid}", async (Guid id, CancellationToken ct) =>
{
    var result = await new GetOrderQuery(id).Send(ct);
    return result.Match(
        ok: dto => dto is null ? Results.NotFound() : Results.Ok(dto),
        fail: err => Results.Problem(err.Message));
});

app.Run();
