using Logistics2cicdBackend;
using Logistics2cicdBackend.Domain;
using Logistics2cicdBackend.Services;

var builder = WebApplication.CreateBuilder(args);

// Registered as an interface so a test can replace it without replacing
// the host. A concrete registration would leave nothing to substitute.
builder.Services.AddSingleton<IServiceStatus, ServiceStatus>();
builder.Services.AddSingleton<IColdChainService, ColdChainService>();

// Describes the API so the contract tests have something to generate
// from. Document only — Swagger UI is a separate package and is not
// referenced, so nothing new is published by the running service.
builder.Services.AddOpenApi();

// A browser only lets a frontend on another origin call this API when that
// origin is listed here. ALPHACI sets CORS_ORIGINS to the deployed
// frontend's address on managed hosting; a local run falls back to the
// frontend dev server.
var corsOrigins = (builder.Configuration["CORS_ORIGINS"] ?? "http://localhost:3000")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

app.UseCors();

// The production gate probes /health after a deployment, so this endpoint
// is part of the pipeline contract rather than a convenience.
// .Produces<T>() is what puts a response SHAPE in the document. Without
// it the schema says an endpoint exists and nothing about what it
// returns, and a contract test generated from that can only check the
// status code — it would pass against an endpoint returning anything.
app.MapGet("/health", (IServiceStatus status) =>
    Results.Ok(new HealthResponse(status.CurrentStatus(), ServiceInfo.Name)))
   .Produces<HealthResponse>(StatusCodes.Status200OK);

app.MapGet("/", () => Results.Ok(new HealthResponse("ready", ServiceInfo.Name)))
   .Produces<HealthResponse>(StatusCodes.Status200OK);

// FEATURE 1: Consignment Intake & Constraints
app.MapPost("/api/v1/consignments", (CreateConsignmentRequest request, IColdChainService service) =>
{
    try
    {
        var consignment = service.RegisterConsignment(request);
        return Results.Created($"/api/v1/consignments/{consignment.Id}", consignment);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).Produces<Consignment>(StatusCodes.Status201Created)
  .Produces(StatusCodes.Status400BadRequest);

app.MapGet("/api/v1/consignments", (string? status, string? urgency, IColdChainService service) =>
    Results.Ok(service.GetConsignments(status, urgency)))
   .Produces<List<Consignment>>(StatusCodes.Status200OK);

app.MapGet("/api/v1/consignments/{id}", (string id, IColdChainService service) =>
{
    var consignment = service.GetConsignmentById(id);
    return consignment != null ? Results.Ok(consignment) : Results.NotFound(new { error = $"Consignment '{id}' not found." });
}).Produces<Consignment>(StatusCodes.Status200OK)
  .Produces(StatusCodes.Status404NotFound);

app.MapGet("/api/v1/consignments/track/{trackingNumber}", (string trackingNumber, IColdChainService service) =>
{
    var consignment = service.GetConsignmentByTrackingNumber(trackingNumber);
    return consignment != null ? Results.Ok(consignment) : Results.NotFound(new { error = $"Tracking number '{trackingNumber}' not found." });
}).Produces<Consignment>(StatusCodes.Status200OK)
  .Produces(StatusCodes.Status404NotFound);

// FEATURE 2: Fleet Compatibility & Dispatch
app.MapGet("/api/v1/fleet/vehicles", (IColdChainService service) =>
    Results.Ok(service.GetVehicles()))
   .Produces<List<Vehicle>>(StatusCodes.Status200OK);

app.MapGet("/api/v1/fleet/drivers", (IColdChainService service) =>
    Results.Ok(service.GetDrivers()))
   .Produces<List<Driver>>(StatusCodes.Status200OK);

app.MapPost("/api/v1/consignments/{id}/assign", (string id, AssignFleetRequest request, IColdChainService service) =>
{
    try
    {
        var manifest = service.AssignFleet(id, request);
        return Results.Ok(manifest);
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).Produces<LoadingManifest>(StatusCodes.Status200OK)
  .Produces(StatusCodes.Status400BadRequest)
  .Produces(StatusCodes.Status404NotFound);

app.MapGet("/api/v1/fleet/manifests/{manifestId}", (string manifestId, IColdChainService service) =>
{
    var manifest = service.GetLoadingManifest(manifestId);
    return manifest != null ? Results.Ok(manifest) : Results.NotFound(new { error = $"Manifest '{manifestId}' not found." });
}).Produces<LoadingManifest>(StatusCodes.Status200OK)
  .Produces(StatusCodes.Status404NotFound);

// FEATURE 3: Real-Time Telemetry & Breach Alerting
app.MapPost("/api/v1/telemetry/ingest", (IngestTelemetryRequest request, IColdChainService service) =>
{
    try
    {
        var reading = service.IngestTelemetry(request);
        return Results.Ok(reading);
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).Produces<TelemetryReading>(StatusCodes.Status200OK)
  .Produces(StatusCodes.Status400BadRequest)
  .Produces(StatusCodes.Status404NotFound);

app.MapGet("/api/v1/telemetry/{shipmentId}", (string shipmentId, IColdChainService service) =>
    Results.Ok(service.GetTelemetryHistory(shipmentId)))
   .Produces<List<TelemetryReading>>(StatusCodes.Status200OK);

app.MapGet("/api/v1/incidents", (string? shipmentId, IColdChainService service) =>
    Results.Ok(service.GetBreachIncidents(shipmentId)))
   .Produces<List<BreachIncident>>(StatusCodes.Status200OK);

// FEATURE 4: Geofenced Delivery Handoff & Proof of Delivery (POD)
app.MapPost("/api/v1/consignments/{id}/arrival", (string id, ArrivalRequest request, IColdChainService service) =>
{
    try
    {
        var consignment = service.RecordArrival(id, request);
        return Results.Ok(consignment);
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).Produces<Consignment>(StatusCodes.Status200OK)
  .Produces(StatusCodes.Status400BadRequest)
  .Produces(StatusCodes.Status404NotFound);

app.MapPost("/api/v1/consignments/{id}/complete-delivery", (string id, CompleteDeliveryRequest request, IColdChainService service) =>
{
    try
    {
        var pod = service.CompleteDelivery(id, request);
        return Results.Ok(pod);
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).Produces<PodReceipt>(StatusCodes.Status200OK)
  .Produces(StatusCodes.Status400BadRequest)
  .Produces(StatusCodes.Status404NotFound);

app.MapGet("/api/v1/consignments/{id}/pod", (string id, IColdChainService service) =>
{
    var pod = service.GetPodReceipt(id);
    return pod != null ? Results.Ok(pod) : Results.NotFound(new { error = $"POD receipt for '{id}' not found." });
}).Produces<PodReceipt>(StatusCodes.Status200OK)
  .Produces(StatusCodes.Status404NotFound);

await app.RunAsync();

namespace Logistics2cicdBackend
{
    public record HealthResponse(string Status, string Service);

    public static class ServiceInfo
    {
        public const string Name = "logistics2cicd-backend";
    }

    // Exposed so the test project can host the application in memory.
    public partial class Program;
}
