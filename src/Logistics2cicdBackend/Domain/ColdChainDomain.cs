using System;
using System.Collections.Generic;

namespace Logistics2cicdBackend.Domain;

public static class ConsignmentStatus
{
    public const string AwaitingAssignment = "Awaiting_Assignment";
    public const string Assigned = "Assigned";
    public const string InTransit = "In_Transit";
    public const string TemperatureBreach = "Temperature_Breach";
    public const string ArrivedAtDestination = "Arrived_At_Destination";
    public const string Delivered = "Delivered";
    public const string DeliveryLocked = "Delivery_Locked";
}

public static class ConsignmentUrgency
{
    public const string Standard = "Standard";
    public const string Expedited = "Expedited";
    public const string LifeCritical = "Life-Critical";
}

public class Consignment
{
    public string Id { get; set; } = string.Empty;
    public string TrackingNumber { get; set; } = string.Empty;
    public string ItemDescription { get; set; } = string.Empty;
    public double MinTempC { get; set; }
    public double MaxTempC { get; set; }
    public double WeightKg { get; set; }
    public double LengthCm { get; set; }
    public double WidthCm { get; set; }
    public double HeightCm { get; set; }
    public string Urgency { get; set; } = ConsignmentUrgency.Standard;
    public double? OriginLat { get; set; }
    public double? OriginLng { get; set; }
    public double? DestLat { get; set; }
    public double? DestLng { get; set; }
    public string RecipientPhone { get; set; } = string.Empty;
    public string RecipientEmail { get; set; } = string.Empty;
    public string RecipientName { get; set; } = string.Empty;
    public string Status { get; set; } = ConsignmentStatus.AwaitingAssignment;
    public string? AssignedVehicleId { get; set; }
    public string? AssignedDriverId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public double? CurrentLat { get; set; }
    public double? CurrentLng { get; set; }
    public double? CurrentTempC { get; set; }
    public double? RemainingDistanceMeters { get; set; }
    public int? EtaMinutes { get; set; }
    public int ConsecutiveBreachCount { get; set; }
    public string? CurrentOtp { get; set; }
    public int FailedOtpAttempts { get; set; }
    public bool IsLocked { get; set; }
    public DateTimeOffset? ArrivedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
}

public class Vehicle
{
    public string Id { get; set; } = string.Empty;
    public string PlateNumber { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public bool IsRefrigerated { get; set; }
    public double MaxWeightCapacityKg { get; set; }
    public double CurrentWeightKg { get; set; }
    public string CoolingStatus { get; set; } = "Optimal";
}

public class Driver
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Status { get; set; } = "On-Duty";
    public int ActiveRoutesCount { get; set; }
    public int MaxRoutesLimit { get; set; } = 3;
}

public class TelemetryReading
{
    public string Id { get; set; } = string.Empty;
    public string ShipmentId { get; set; } = string.Empty;
    public double Lat { get; set; }
    public double Lng { get; set; }
    public double TempC { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public bool IsOutOfTolerance { get; set; }
}

public class BreachIncident
{
    public string Id { get; set; } = string.Empty;
    public string ShipmentId { get; set; } = string.Empty;
    public DateTimeOffset StartTimestamp { get; set; }
    public DateTimeOffset? NormalizedTimestamp { get; set; }
    public double PeakTemperatureC { get; set; }
    public double? DurationSeconds { get; set; }
    public bool IsActive { get; set; }
}

public class LoadingManifest
{
    public string ManifestId { get; set; } = string.Empty;
    public string VehicleId { get; set; } = string.Empty;
    public string DriverId { get; set; } = string.Empty;
    public List<string> ConsignmentIds { get; set; } = new();
    public double TotalWeightKg { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
}

public record TelemetryGraphPoint(DateTimeOffset Timestamp, double TempC, bool IsBreach);

public class PodReceipt
{
    public string PodId { get; set; } = string.Empty;
    public string ShipmentId { get; set; } = string.Empty;
    public DateTimeOffset DeliveredAt { get; set; }
    public string RecipientName { get; set; } = string.Empty;
    public string RecipientJobTitle { get; set; } = string.Empty;
    public string SignatureData { get; set; } = string.Empty;
    public List<TelemetryGraphPoint> TemperatureGraph { get; set; } = new();
    public int BreachIncidentsCount { get; set; }
}

public static class GeoUtils
{
    private const double EarthRadiusKm = 6371.0;

    public static double CalculateDistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        double dLat = ToRadians(lat2 - lat1);
        double dLon = ToRadians(lon2 - lon1);
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusKm * c * 1000.0;
    }

    private static double ToRadians(double degrees)
    {
        return degrees * Math.PI / 180.0;
    }
}
