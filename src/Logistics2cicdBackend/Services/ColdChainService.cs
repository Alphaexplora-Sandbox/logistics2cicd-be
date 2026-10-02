using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Logistics2cicdBackend.Domain;

namespace Logistics2cicdBackend.Services;

public class ColdChainService : IColdChainService
{
    private readonly ConcurrentDictionary<string, Consignment> _consignments = new();
    private readonly ConcurrentDictionary<string, Vehicle> _vehicles = new();
    private readonly ConcurrentDictionary<string, Driver> _drivers = new();
    private readonly ConcurrentDictionary<string, List<TelemetryReading>> _telemetry = new();
    private readonly ConcurrentDictionary<string, List<BreachIncident>> _incidents = new();
    private readonly ConcurrentDictionary<string, LoadingManifest> _manifests = new();
    private readonly ConcurrentDictionary<string, PodReceipt> _podReceipts = new();

    public ColdChainService()
    {
        SeedInitialData();
    }

    private void SeedInitialData()
    {
        // Seed vehicles (Feature 2)
        var v1 = new Vehicle
        {
            Id = "vh-cold-01",
            PlateNumber = "REEF-101",
            Model = "ThermoKing Sprinter Reefer",
            IsRefrigerated = true,
            MaxWeightCapacityKg = 1200.0,
            CurrentWeightKg = 0.0,
            CoolingStatus = "Optimal"
        };

        var v2 = new Vehicle
        {
            Id = "vh-cold-02",
            PlateNumber = "FRST-202",
            Model = "Carrier Transicold Medium Duty",
            IsRefrigerated = true,
            MaxWeightCapacityKg = 2500.0,
            CurrentWeightKg = 0.0,
            CoolingStatus = "Optimal"
        };

        var v3 = new Vehicle
        {
            Id = "vh-dry-01",
            PlateNumber = "DRY-303",
            Model = "Standard Cargo Box Van",
            IsRefrigerated = false,
            MaxWeightCapacityKg = 1500.0,
            CurrentWeightKg = 0.0,
            CoolingStatus = "Inactive"
        };

        var v4 = new Vehicle
        {
            Id = "vh-cold-heavy",
            PlateNumber = "ARCT-404",
            Model = "Polar Freight Semi-Trailer",
            IsRefrigerated = true,
            MaxWeightCapacityKg = 3000.0,
            CurrentWeightKg = 2950.0,
            CoolingStatus = "Optimal"
        };

        _vehicles[v1.Id] = v1;
        _vehicles[v2.Id] = v2;
        _vehicles[v3.Id] = v3;
        _vehicles[v4.Id] = v4;

        // Seed drivers (Feature 2)
        var d1 = new Driver
        {
            Id = "drv-01",
            Name = "Marcus Vance",
            Phone = "+1-555-0111",
            Status = "On-Duty",
            ActiveRoutesCount = 0,
            MaxRoutesLimit = 3
        };

        var d2 = new Driver
        {
            Id = "drv-02",
            Name = "Sarah Chen",
            Phone = "+1-555-0122",
            Status = "On-Duty",
            ActiveRoutesCount = 1,
            MaxRoutesLimit = 3
        };

        var d3 = new Driver
        {
            Id = "drv-offduty",
            Name = "Dave Miller",
            Phone = "+1-555-0133",
            Status = "Off-Duty",
            ActiveRoutesCount = 0,
            MaxRoutesLimit = 3
        };

        var d4 = new Driver
        {
            Id = "drv-busy",
            Name = "Elena Rostova",
            Phone = "+1-555-0144",
            Status = "On-Duty",
            ActiveRoutesCount = 3,
            MaxRoutesLimit = 3
        };

        _drivers[d1.Id] = d1;
        _drivers[d2.Id] = d2;
        _drivers[d3.Id] = d3;
        _drivers[d4.Id] = d4;

        // Seed sample consignment
        var sampleConsignment = new Consignment
        {
            Id = "shp-sample-01",
            TrackingNumber = "TRK-88201",
            ItemDescription = "COVID-19 mRNA Vaccine Vials (Ultra-Cold)",
            MinTempC = 2.0,
            MaxTempC = 8.0,
            WeightKg = 42.5,
            LengthCm = 60.0,
            WidthCm = 40.0,
            HeightCm = 35.0,
            Urgency = ConsignmentUrgency.LifeCritical,
            OriginLat = 47.6062,
            OriginLng = -122.3321,
            DestLat = 37.7749,
            DestLng = -122.4194,
            RecipientPhone = "+1-555-0199",
            RecipientEmail = "rx-intake@sfgeneral.org",
            RecipientName = "Dr. Rachel Ward",
            Status = ConsignmentStatus.AwaitingAssignment,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-3)
        };

        _consignments[sampleConsignment.Id] = sampleConsignment;
    }

    // FEATURE 1: Cold-chain consignment intake & constraint definition
    public Consignment RegisterConsignment(CreateConsignmentRequest request)
    {
        // 1. Prevent form submission if min_temp is greater than or equal to max_temp
        if (request.MinTempC >= request.MaxTempC)
        {
            throw new ArgumentException("min_temp must be strictly less than max_temp.");
        }

        // 3. Reject registration if required coordinates or recipient contact phone/email are missing
        if (!request.OriginLat.HasValue || !request.OriginLng.HasValue ||
            !request.DestLat.HasValue || !request.DestLng.HasValue)
        {
            throw new ArgumentException("Origin coordinates (lat, lng) and destination coordinates (lat, lng) are required.");
        }

        if (string.IsNullOrWhiteSpace(request.RecipientPhone))
        {
            throw new ArgumentException("Recipient contact phone is required.");
        }

        if (string.IsNullOrWhiteSpace(request.RecipientEmail))
        {
            throw new ArgumentException("Recipient contact email is required.");
        }

        if (request.WeightKg <= 0)
        {
            throw new ArgumentException("Package weight must be greater than zero.");
        }

        // 2. Auto-generate an immutable tracking identifier in the format TRK-XXXXX
        string trackingId = $"TRK-{Random.Shared.Next(10000, 99999)}";
        string id = $"shp-{Guid.NewGuid().ToString("N")[..8]}";

        // 4. Enforce initial state as Awaiting_Assignment
        var consignment = new Consignment
        {
            Id = id,
            TrackingNumber = trackingId,
            ItemDescription = string.IsNullOrWhiteSpace(request.ItemDescription) ? "Cold-chain Medical Cargo" : request.ItemDescription,
            MinTempC = request.MinTempC,
            MaxTempC = request.MaxTempC,
            WeightKg = request.WeightKg,
            LengthCm = request.LengthCm,
            WidthCm = request.WidthCm,
            HeightCm = request.HeightCm,
            Urgency = string.IsNullOrWhiteSpace(request.Urgency) ? ConsignmentUrgency.Standard : request.Urgency,
            OriginLat = request.OriginLat,
            OriginLng = request.OriginLng,
            DestLat = request.DestLat,
            DestLng = request.DestLng,
            RecipientPhone = request.RecipientPhone.Trim(),
            RecipientEmail = request.RecipientEmail.Trim(),
            RecipientName = request.RecipientName ?? string.Empty,
            Status = ConsignmentStatus.AwaitingAssignment,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _consignments[consignment.Id] = consignment;
        return consignment;
    }

    public IReadOnlyList<Consignment> GetConsignments(string? status = null, string? urgency = null)
    {
        IEnumerable<Consignment> query = _consignments.Values;

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(c => string.Equals(c.Status, status, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(urgency))
        {
            query = query.Where(c => string.Equals(c.Urgency, urgency, StringComparison.OrdinalIgnoreCase));
        }

        return query.OrderByDescending(c => c.CreatedAt).ToList();
    }

    public Consignment? GetConsignmentById(string id)
    {
        _consignments.TryGetValue(id, out var consignment);
        return consignment;
    }

    public Consignment? GetConsignmentByTrackingNumber(string trackingNumber)
    {
        return _consignments.Values.FirstOrDefault(c => string.Equals(c.TrackingNumber, trackingNumber, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<Vehicle> GetVehicles()
    {
        return _vehicles.Values.OrderBy(v => v.Id).ToList();
    }

    public IReadOnlyList<Driver> GetDrivers()
    {
        return _drivers.Values.OrderBy(d => d.Id).ToList();
    }

    // FEATURE 2: Fleet compatibility checking & dispatch allocation
    public LoadingManifest AssignFleet(string consignmentId, AssignFleetRequest request)
    {
        if (!_consignments.TryGetValue(consignmentId, out var consignment))
        {
            throw new KeyNotFoundException($"Consignment '{consignmentId}' not found.");
        }

        if (!_vehicles.TryGetValue(request.VehicleId, out var vehicle))
        {
            throw new ArgumentException($"Vehicle '{request.VehicleId}' not found.");
        }

        if (!_drivers.TryGetValue(request.DriverId, out var driver))
        {
            throw new ArgumentException($"Driver '{request.DriverId}' not found.");
        }

        // 1. Block assignment if a temperature-sensitive shipment is assigned to a vehicle with is_refrigerated == false
        if (!vehicle.IsRefrigerated)
        {
            throw new InvalidOperationException("Block assignment: Temperature-sensitive consignments cannot be assigned to non-refrigerated vehicles.");
        }

        // 2. Block assignment if total assigned package weight exceeds the vehicle's max_weight_capacity_kg
        if (vehicle.CurrentWeightKg + consignment.WeightKg > vehicle.MaxWeightCapacityKg)
        {
            throw new InvalidOperationException($"Block assignment: Total package weight ({vehicle.CurrentWeightKg + consignment.WeightKg:F1}kg) exceeds vehicle maximum capacity ({vehicle.MaxWeightCapacityKg:F1}kg).");
        }

        // 3. Block assignment if the selected driver is marked Off-Duty or has exceeded their active route limit
        if (string.Equals(driver.Status, "Off-Duty", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Block assignment: Selected driver is currently marked Off-Duty.");
        }

        if (driver.ActiveRoutesCount >= driver.MaxRoutesLimit)
        {
            throw new InvalidOperationException($"Block assignment: Driver has reached or exceeded their active route limit ({driver.MaxRoutesLimit}).");
        }

        // 4. Update consignment state to Assigned and generate a consolidated vehicle loading manifest
        consignment.Status = ConsignmentStatus.Assigned;
        consignment.AssignedVehicleId = vehicle.Id;
        consignment.AssignedDriverId = driver.Id;

        vehicle.CurrentWeightKg += consignment.WeightKg;
        driver.ActiveRoutesCount += 1;

        string manifestId = $"MNF-{Random.Shared.Next(10000, 99999)}";
        var manifest = new LoadingManifest
        {
            ManifestId = manifestId,
            VehicleId = vehicle.Id,
            DriverId = driver.Id,
            ConsignmentIds = new List<string> { consignment.Id },
            TotalWeightKg = consignment.WeightKg,
            GeneratedAt = DateTimeOffset.UtcNow
        };

        _manifests[manifestId] = manifest;
        return manifest;
    }

    public LoadingManifest? GetLoadingManifest(string manifestId)
    {
        _manifests.TryGetValue(manifestId, out var manifest);
        return manifest;
    }

    // FEATURE 3: Real-time cold-chain telemetry & breach alerting
    public TelemetryReading IngestTelemetry(IngestTelemetryRequest request)
    {
        if (!_consignments.TryGetValue(request.shipment_id, out var consignment))
        {
            throw new KeyNotFoundException($"Consignment '{request.shipment_id}' not found.");
        }

        bool isOutOfTolerance = request.temp_c < consignment.MinTempC || request.temp_c > consignment.MaxTempC;

        var reading = new TelemetryReading
        {
            Id = Guid.NewGuid().ToString("N"),
            ShipmentId = consignment.Id,
            Lat = request.lat,
            Lng = request.lng,
            TempC = request.temp_c,
            Timestamp = request.timestamp == default ? DateTimeOffset.UtcNow : request.timestamp,
            IsOutOfTolerance = isOutOfTolerance
        };

        _telemetry.AddOrUpdate(
            consignment.Id,
            _ => new List<TelemetryReading> { reading },
            (_, list) =>
            {
                lock (list)
                {
                    list.Add(reading);
                }
                return list;
            });

        // Update current location and temperature
        consignment.CurrentLat = request.lat;
        consignment.CurrentLng = request.lng;
        consignment.CurrentTempC = request.temp_c;

        if (consignment.Status == ConsignmentStatus.Assigned)
        {
            consignment.Status = ConsignmentStatus.InTransit;
        }

        UpdateEta(consignment, request.lat, request.lng);

        var incidentsList = _incidents.GetOrAdd(consignment.Id, _ => new List<BreachIncident>());

        if (isOutOfTolerance)
        {
            HandleBreach(consignment, reading, incidentsList);
        }
        else
        {
            HandleNormalization(consignment, reading, incidentsList);
        }

        return reading;
    }

    private static void UpdateEta(Consignment consignment, double lat, double lng)
    {
        if (!consignment.DestLat.HasValue || !consignment.DestLng.HasValue)
        {
            return;
        }

        double remainingMeters = GeoUtils.CalculateDistanceMeters(
            lat,
            lng,
            consignment.DestLat.Value,
            consignment.DestLng.Value);

        consignment.RemainingDistanceMeters = remainingMeters;
        const double metersPerMinute = 833.33;
        int etaMinutes = (int)Math.Max(1, Math.Ceiling(remainingMeters / metersPerMinute));
        consignment.EtaMinutes = etaMinutes;
    }

    private static void HandleBreach(Consignment consignment, TelemetryReading reading, List<BreachIncident> incidentsList)
    {
        consignment.ConsecutiveBreachCount += 1;
        if (consignment.ConsecutiveBreachCount < 2)
        {
            return;
        }

        consignment.Status = ConsignmentStatus.TemperatureBreach;

        lock (incidentsList)
        {
            var activeIncident = incidentsList.FirstOrDefault(i => i.IsActive);
            if (activeIncident == null)
            {
                activeIncident = new BreachIncident
                {
                    Id = $"INC-{Guid.NewGuid().ToString("N")[..8]}",
                    ShipmentId = consignment.Id,
                    StartTimestamp = reading.Timestamp,
                    PeakTemperatureC = reading.TempC,
                    IsActive = true
                };
                incidentsList.Add(activeIncident);
            }
            else
            {
                UpdatePeakTemperature(consignment, activeIncident, reading.TempC);
            }
        }
    }

    private static void UpdatePeakTemperature(Consignment consignment, BreachIncident activeIncident, double tempC)
    {
        if (tempC > consignment.MaxTempC && tempC > activeIncident.PeakTemperatureC)
        {
            activeIncident.PeakTemperatureC = tempC;
        }
        else if (tempC < consignment.MinTempC && tempC < activeIncident.PeakTemperatureC)
        {
            activeIncident.PeakTemperatureC = tempC;
        }
    }

    private static void HandleNormalization(Consignment consignment, TelemetryReading reading, List<BreachIncident> incidentsList)
    {
        consignment.ConsecutiveBreachCount = 0;

        lock (incidentsList)
        {
            var activeIncident = incidentsList.FirstOrDefault(i => i.IsActive);
            if (activeIncident != null)
            {
                activeIncident.NormalizedTimestamp = reading.Timestamp;
                activeIncident.DurationSeconds = Math.Max(1.0, (reading.Timestamp - activeIncident.StartTimestamp).TotalSeconds);
                activeIncident.IsActive = false;
            }
        }

        if (consignment.Status == ConsignmentStatus.TemperatureBreach)
        {
            consignment.Status = ConsignmentStatus.InTransit;
        }
    }

    public IReadOnlyList<TelemetryReading> GetTelemetryHistory(string shipmentId)
    {
        if (_telemetry.TryGetValue(shipmentId, out var list))
        {
            lock (list)
            {
                return list.OrderBy(r => r.Timestamp).ToList();
            }
        }
        return Array.Empty<TelemetryReading>();
    }

    public IReadOnlyList<BreachIncident> GetBreachIncidents(string? shipmentId = null)
    {
        if (!string.IsNullOrWhiteSpace(shipmentId))
        {
            if (_incidents.TryGetValue(shipmentId, out var list))
            {
                lock (list)
                {
                    return list.OrderByDescending(i => i.StartTimestamp).ToList();
                }
            }
            return Array.Empty<BreachIncident>();
        }

        return _incidents.Values.SelectMany(l =>
        {
            lock (l)
            {
                return l.ToList();
            }
        }).OrderByDescending(i => i.StartTimestamp).ToList();
    }

    // FEATURE 4: Geofenced delivery handoff & digital proof of delivery (POD)
    public Consignment RecordArrival(string consignmentId, ArrivalRequest request)
    {
        if (!_consignments.TryGetValue(consignmentId, out var consignment))
        {
            throw new KeyNotFoundException($"Consignment '{consignmentId}' not found.");
        }

        if (consignment.IsLocked)
        {
            throw new InvalidOperationException("Delivery is locked due to previous failed verification attempts.");
        }

        if (!consignment.DestLat.HasValue || !consignment.DestLng.HasValue)
        {
            throw new InvalidOperationException("Consignment destination coordinates are missing.");
        }

        // 1. Disable the 'Complete Delivery' action until driver coordinates are < 200m from destination coordinates
        double distanceMeters = GeoUtils.CalculateDistanceMeters(
            request.DriverLat,
            request.DriverLng,
            consignment.DestLat.Value,
            consignment.DestLng.Value);

        consignment.RemainingDistanceMeters = distanceMeters;

        if (distanceMeters >= 200.0)
        {
            throw new InvalidOperationException($"Geofence violation: Driver is {distanceMeters:F1}m away. Must be within 200 meters of destination coordinates.");
        }

        // 2. Generate a 6-digit OTP on arrival
        string otp = Random.Shared.Next(100000, 999999).ToString();
        consignment.CurrentOtp = otp;
        consignment.FailedOtpAttempts = 0;
        consignment.Status = ConsignmentStatus.ArrivedAtDestination;
        consignment.ArrivedAt = DateTimeOffset.UtcNow;

        return consignment;
    }

    public PodReceipt CompleteDelivery(string consignmentId, CompleteDeliveryRequest request)
    {
        if (!_consignments.TryGetValue(consignmentId, out var consignment))
        {
            throw new KeyNotFoundException($"Consignment '{consignmentId}' not found.");
        }

        // Check lock status
        if (consignment.IsLocked)
        {
            throw new InvalidOperationException("Delivery submission is locked after 3 failed verification attempts.");
        }

        if (!consignment.DestLat.HasValue || !consignment.DestLng.HasValue)
        {
            throw new InvalidOperationException("Destination coordinates missing.");
        }

        // 1. Geofence verification (< 200m)
        double distanceMeters = GeoUtils.CalculateDistanceMeters(
            request.DriverLat,
            request.DriverLng,
            consignment.DestLat.Value,
            consignment.DestLng.Value);

        if (distanceMeters >= 200.0)
        {
            throw new InvalidOperationException($"Geofence constraint: Driver is {distanceMeters:F1}m away. Must be within 200 meters of destination coordinates.");
        }

        // 2. Verify 6-digit OTP; lock submission after 3 failed verification attempts
        VerifyOtp(consignment, request.OtpCode);

        // 3. Capture digital signature as vector path or base64 image along with recipient name and job title
        if (string.IsNullOrWhiteSpace(request.SignatureData))
        {
            throw new ArgumentException("Digital signature (vector path or base64) is required.");
        }

        if (string.IsNullOrWhiteSpace(request.RecipientName))
        {
            throw new ArgumentException("Recipient name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.RecipientJobTitle))
        {
            throw new ArgumentException("Recipient job title is required.");
        }

        // 4. Transition status to Delivered and generate an immutable POD receipt embedding the full in-transit temperature graph and completion timestamp
        consignment.Status = ConsignmentStatus.Delivered;
        consignment.DeliveredAt = DateTimeOffset.UtcNow;
        consignment.RecipientName = request.RecipientName.Trim();

        var telemetryList = GetTelemetryHistory(consignment.Id);
        var graphPoints = telemetryList.Select(r => new TelemetryGraphPoint(r.Timestamp, r.TempC, r.IsOutOfTolerance)).ToList();
        var incidents = GetBreachIncidents(consignment.Id);

        string podId = $"POD-{Random.Shared.Next(10000, 99999)}";
        var podReceipt = new PodReceipt
        {
            PodId = podId,
            ShipmentId = consignment.Id,
            DeliveredAt = consignment.DeliveredAt.Value,
            RecipientName = request.RecipientName.Trim(),
            RecipientJobTitle = request.RecipientJobTitle.Trim(),
            SignatureData = request.SignatureData,
            TemperatureGraph = graphPoints,
            BreachIncidentsCount = incidents.Count
        };

        _podReceipts[consignment.Id] = podReceipt;
        return podReceipt;
    }

    public PodReceipt? GetPodReceipt(string shipmentId)
    {
        _podReceipts.TryGetValue(shipmentId, out var pod);
        return pod;
    }

    private static void VerifyOtp(Consignment consignment, string? otpCode)
    {
        if (string.IsNullOrWhiteSpace(consignment.CurrentOtp) ||
            !string.Equals(consignment.CurrentOtp.Trim(), otpCode?.Trim(), StringComparison.Ordinal))
        {
            consignment.FailedOtpAttempts += 1;
            if (consignment.FailedOtpAttempts >= 3)
            {
                consignment.IsLocked = true;
                consignment.Status = ConsignmentStatus.DeliveryLocked;
                throw new InvalidOperationException("Delivery submission locked: 3 failed OTP verification attempts.");
            }
            throw new ArgumentException($"Invalid OTP code. {3 - consignment.FailedOtpAttempts} verification attempt(s) remaining.");
        }
    }
}
