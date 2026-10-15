using System;
using System.Collections.Generic;
using Logistics2cicdBackend.Domain;

namespace Logistics2cicdBackend.Services;

public record CreateConsignmentRequest(
    string ItemDescription,
    double MinTempC,
    double MaxTempC,
    double WeightKg,
    double LengthCm,
    double WidthCm,
    double HeightCm,
    string? Urgency,
    double? OriginLat,
    double? OriginLng,
    double? DestLat,
    double? DestLng,
    string RecipientPhone,
    string RecipientEmail,
    string? RecipientName);

public record AssignFleetRequest(
    string VehicleId,
    string DriverId);

public record IngestTelemetryRequest(
    string shipment_id,
    double lat,
    double lng,
    double temp_c,
    DateTimeOffset timestamp);

public record ArrivalRequest(
    double DriverLat,
    double DriverLng);

public record CompleteDeliveryRequest(
    double DriverLat,
    double DriverLng,
    string OtpCode,
    string RecipientName,
    string RecipientJobTitle,
    string SignatureData);

public interface IColdChainService
{
    Consignment RegisterConsignment(CreateConsignmentRequest request);
    IReadOnlyList<Consignment> GetConsignments(string? status = null, string? urgency = null);
    Consignment? GetConsignmentById(string id);
    Consignment? GetConsignmentByTrackingNumber(string trackingNumber);

    IReadOnlyList<Vehicle> GetVehicles();
    IReadOnlyList<Driver> GetDrivers();
    LoadingManifest AssignFleet(string consignmentId, AssignFleetRequest request);
    LoadingManifest? GetLoadingManifest(string manifestId);

    TelemetryReading IngestTelemetry(IngestTelemetryRequest request);
    IReadOnlyList<TelemetryReading> GetTelemetryHistory(string shipmentId);
    IReadOnlyList<BreachIncident> GetBreachIncidents(string? shipmentId = null);

    Consignment RecordArrival(string consignmentId, ArrivalRequest request);
    PodReceipt CompleteDelivery(string consignmentId, CompleteDeliveryRequest request);
    PodReceipt? GetPodReceipt(string shipmentId);
}
