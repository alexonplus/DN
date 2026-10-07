using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using StackExchange.Redis;
var builder = WebApplication.CreateBuilder(args);
// --- Connection strings ---
var postgresConnection = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not set.");
var redisConnection = builder.Configuration.GetConnectionString("Redis")
    ?? throw new InvalidOperationException("ConnectionStrings:Redis is not set.");
var rabbitHost = builder.Configuration["RabbitMQ:Host"] ?? "localhost";
var rabbitUser = builder.Configuration["RabbitMQ:User"] ?? "guest";
var rabbitPass = builder.Configuration["RabbitMQ:Password"] ?? "guest";
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(postgresConnection));
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(redisConnection));
    builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
var app = builder.Build();
app.UseCors();
// --- DB init: EnsureCreated with retry ---
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var retries = 5;
    while (retries > 0)
    {
        try
        {
            db.Database.EnsureCreated();
            break;
        }
        catch (Exception ex)
        {
            retries--;
            logger.LogWarning(ex, "DB not ready, retries left: {Retries}", retries);
            if (retries == 0)
            {
                logger.LogError("Database unavailable after all retries.");
                throw;
            }
            Thread.Sleep(3000);
        }
    }
}
// 1. Health check
app.MapGet("/health", async (ApplicationDbContext db, IConnectionMultiplexer redis) =>
{
    var postgresOk = await db.Database.CanConnectAsync();
    var redisOk = redis.IsConnected;
    var healthy = postgresOk && redisOk;
    return Results.Json(
        new
        {
            status = healthy ? "Healthy" : "Degraded",
            timestamp = DateTime.UtcNow,
            services = new
            {
                postgres = postgresOk ? "Connected" : "Failed",
                redis = redisOk ? "Connected" : "Failed",
                messageBroker = "Configured"
            }
        },
        statusCode: healthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
});
// 2. Get all entries, cached in Redis
app.MapGet("/api/tasks", async (ApplicationDbContext db, IConnectionMultiplexer redis) =>
{
    try
    {
        var cacheDb = redis.GetDatabase();
        var cachedData = await cacheDb.StringGetAsync("death_note_entries");
        if (cachedData.HasValue)
        {
            var cachedEntries = JsonSerializer.Deserialize<List<DeathNoteEntry>>(cachedData.ToString());
            return Results.Ok(new { source = "Redis Cache", count = cachedEntries?.Count ?? 0, data = cachedEntries });
        }
        var entries = await db.Entries.AsNoTracking().ToListAsync();
        await cacheDb.StringSetAsync("death_note_entries", JsonSerializer.Serialize(entries), TimeSpan.FromSeconds(60));
        return Results.Ok(new { source = "PostgreSQL Database", count = entries.Count, data = entries });
    }
    catch (RedisConnectionException)
    {
        var entries = await db.Entries.AsNoTracking().ToListAsync();
        return Results.Ok(new { source = "PostgreSQL Database (cache unavailable)", count = entries.Count, data = entries });
    }
});
// 3. Create an entry
app.MapPost("/api/tasks", async (DeathNoteRequest? request, ApplicationDbContext db, IConnectionMultiplexer redis) =>
{
    if (request is null || string.IsNullOrWhiteSpace(request.Name))
    {
        return Results.BadRequest(new { error = "Name cannot be empty" });
    }
    var entry = new DeathNoteEntry
    {
        Id = Guid.NewGuid(),
        Name = request.Name.Trim(),
        Cause = request.Cause?.Trim() is { Length: > 0 } c ? c : "Heart Attack",
        DeathDate = request.DeathDate.HasValue 
            ? DateTime.SpecifyKind(request.DeathDate.Value, DateTimeKind.Utc) 
            : DateTime.UtcNow.AddSeconds(40)
    };
    db.Entries.Add(entry);
    await db.SaveChangesAsync();
    try
    {
        await redis.GetDatabase().KeyDeleteAsync("death_note_entries");
    }
    catch (RedisConnectionException)
    {
    }
    try
    {
        var factory = new ConnectionFactory
        {
            HostName = rabbitHost,
            UserName = rabbitUser,
            Password = rabbitPass
        };
        using var connection = await factory.CreateConnectionAsync();
        using var channel = await connection.CreateChannelAsync();
        await channel.QueueDeclareAsync(queue: "death_events", durable: true,
            exclusive: false, autoDelete: false, arguments: null);
        var message = JsonSerializer.Serialize(new
        {
            @event = "EntryWritten",
            name = entry.Name,
            timestamp = DateTime.UtcNow
        });
        var body = System.Text.Encoding.UTF8.GetBytes(message);
        await channel.BasicPublishAsync(exchange: string.Empty, routingKey: "death_events", body: body);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"RabbitMQ publish error: {ex.Message}");
    }
    return Results.Created($"/api/tasks/{entry.Id}", entry);
});
// 4. Get single entry by id
app.MapGet("/api/tasks/{id:guid}", async (Guid id, ApplicationDbContext db) =>
{
    var entry = await db.Entries.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id);
    return entry is null ? Results.NotFound() : Results.Ok(entry);
});
app.Run();
// --- Models ---
public class DeathNoteEntry
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Cause { get; set; } = "Heart Attack";
    public DateTime DeathDate { get; set; }
}
public record DeathNoteRequest(string Name, string? Cause, DateTime? DeathDate);
public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }
    public DbSet<DeathNoteEntry> Entries => Set<DeathNoteEntry>();
}