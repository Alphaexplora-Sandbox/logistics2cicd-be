using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Logistics2cicdBackend.Domain;
using Logistics2cicdBackend.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Logistics2cicdBackend.Tests;

public class ColdChainApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ColdChainApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // =========================================================================
    // FEATURE 1: Cold-chain consignment intake & constraint definition
    // =========================================================================

    [Fact]
    public async Task Feature1_PreventSubmission_WhenMinTempGreaterOrEqualToMaxTemp()
    {
        var client = _factory.CreateClient();

        // min_temp > max_temp
        var invalidReq1 = new CreateConsignmentRequest(
            "Insulin Glargine Vials",
            8.0,
            2.0,
            12.5,
            30, 20, 15,
            ConsignmentUrgency.Standard,
            47.6062, -122.3321,
            37.7749, -122.4194,
            "+1-555-0100",
            "lead@coldchain.com",
            "Warehouse Intake");

        var response1 = await client.PostAsJsonAsync("/api/v1/consignments", invalidReq1);
        Assert.Equal(HttpStatusCode.BadRequest, response1.StatusCode);

        // min_temp == max_temp
        var invalidReq2 = new CreateConsignmentRequest(
            "Blood Plasma",
            4.0,
            4.0,
            10.0,
            25, 25, 25,
            ConsignmentUrgency.Expedited,
            47.6062, -122.3321,
            37.7749, -122.4194,
            "+1-555-0100",
            "lead@coldchain.com",
            "Warehouse Intake");

        var response2 = await client.PostAsJsonAsync("/api/v1/consignments", invalidReq2);
        Assert.Equal(HttpStatusCode.BadRequest, response2.StatusCode);
    }

    [Fact]
    public async Task Feature1_AutoGenerateTrackingId_AndEnforceInitialStateAwaitingAssignment()
    {
        var client = _factory.CreateClient();

        var validReq = new CreateConsignmentRequest(
            "mRNA Vaccines Ultra Cold",
            -25.0,
            -15.0,
            18.0,
            40, 30, 20,
            ConsignmentUrgency.LifeCritical,
            47.6062, -122.3321,
            37.7749, -122.4194,
            "+1-555-0199",
            "rx@hospital.org",
            "Lead Pharmacist");

        var response = await client.PostAsJsonAsync("/api/v1/consignments", validReq);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<Consignment>();
        Assert.NotNull(created);

        // Acceptance Criteria: Auto-generate immutable tracking identifier in format TRK-XXXXX
        Assert.Matches(@"^TRK-\d{5}$", created!.TrackingNumber);

        // Acceptance Criteria: Enforce initial state as Awaiting_Assignment
        Assert.Equal(ConsignmentStatus.AwaitingAssignment, created.Status);
        Assert.Equal(-25.0, created.MinTempC);
        Assert.Equal(-15.0, created.MaxTempC);
        Assert.Equal(18.0, created.WeightKg);
    }

    [Fact]
    public async Task Feature1_RejectRegistration_WhenCoordinatesOrContactMissing()
    {
        var client = _factory.CreateClient();

        // Missing coordinates
        var missingCoords = new CreateConsignmentRequest(
            "Vaccines",
            2.0, 8.0,
            5.0,
            20, 20, 20,
            ConsignmentUrgency.Standard,
            null, null,
            37.7749, -122.4194,
            "+1-555-0199",
            "rx@hospital.org",
            "Lead");

        var resCoords = await client.PostAsJsonAsync("/api/v1/consignments", missingCoords);
        Assert.Equal(HttpStatusCode.BadRequest, resCoords.StatusCode);

        // Missing phone
        var missingPhone = new CreateConsignmentRequest(
            "Vaccines",
            2.0, 8.0,
            5.0,
            20, 20, 20,
            ConsignmentUrgency.Standard,
            47.6062, -122.3321,
            37.7749, -122.4194,
            "",
            "rx@hospital.org",
            "Lead");

        var resPhone = await client.PostAsJsonAsync("/api/v1/consignments", missingPhone);
        Assert.Equal(HttpStatusCode.BadRequest, resPhone.StatusCode);

        // Missing email
        var missingEmail = new CreateConsignmentRequest(
            "Vaccines",
            2.0, 8.0,
            5.0,
            20, 20, 20,
            ConsignmentUrgency.Standard,
            47.6062, -122.3321,
            37.7749, -122.4194,
            "+1-555-0199",
            "   ",
            "Lead");

        var resEmail = await client.PostAsJsonAsync("/api/v1/consignments", missingEmail);
        Assert.Equal(HttpStatusCode.BadRequest, resEmail.StatusCode);
    }

    // =========================================================================
    // FEATURE 2: Fleet compatibility checking & dispatch allocation
    // =========================================================================

    [Fact]
    public async Task Feature2_BlockAssignment_WhenVehicleNotRefrigerated()
    {
        var client = _factory.CreateClient();

        // Create cold-chain consignment
        var regRes = await client.PostAsJsonAsync("/api/v1/consignments", new CreateConsignmentRequest(
            "Temperature-Sensitive Antibiotics",
            2.0, 8.0,
            25.0,
            30, 30, 30,
            ConsignmentUrgency.Standard,
            47.6062, -122.3321,
            37.7749, -122.4194,
            "+1-555-0199",
            "clinic@health.gov",
            "Nurse Lead"));
        var consignment = await regRes.Content.ReadFromJsonAsync<Consignment>();

        // Attempt assignment to non-refrigerated dry vehicle (vh-dry-01)
        var assignReq = new AssignFleetRequest("vh-dry-01", "drv-01");
        var assignRes = await client.PostAsJsonAsync($"/api/v1/consignments/{consignment!.Id}/assign", assignReq);

        Assert.Equal(HttpStatusCode.BadRequest, assignRes.StatusCode);
        var body = await assignRes.Content.ReadAsStringAsync();
        Assert.Contains("non-refrigerated", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Feature2_BlockAssignment_WhenPackageExceedsMaxWeightCapacity()
    {
        var client = _factory.CreateClient();

        // Create heavy consignment (100 kg)
        var regRes = await client.PostAsJsonAsync("/api/v1/consignments", new CreateConsignmentRequest(
            "Bulk Cold-Chain Reagents",
            2.0, 8.0,
            100.0,
            50, 50, 50,
            ConsignmentUrgency.Standard,
            47.6062, -122.3321,
            37.7749, -122.4194,
            "+1-555-0199",
            "lab@pharma.com",
            "Lab Lead"));
        var consignment = await regRes.Content.ReadFromJsonAsync<Consignment>();

        // vh-cold-heavy has MaxWeightCapacity 3000 and CurrentWeight 2950 (only 50kg left!)
        var assignReq = new AssignFleetRequest("vh-cold-heavy", "drv-01");
        var assignRes = await client.PostAsJsonAsync($"/api/v1/consignments/{consignment!.Id}/assign", assignReq);

        Assert.Equal(HttpStatusCode.BadRequest, assignRes.StatusCode);
        var body = await assignRes.Content.ReadAsStringAsync();
        Assert.Contains("capacity", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Feature2_BlockAssignment_WhenDriverOffDutyOrExceededLimit()
    {
        var client = _factory.CreateClient();

        var regRes = await client.PostAsJsonAsync("/api/v1/consignments", new CreateConsignmentRequest(
            "Cell Cultures",
            -4.0, 0.0,
            10.0,
            20, 20, 20,
            ConsignmentUrgency.Expedited,
            47.6062, -122.3321,
            37.7749, -122.4194,
            "+1-555-0199",
            "bio@research.org",
            "Researcher"));
        var consignment = await regRes.Content.ReadFromJsonAsync<Consignment>();

        // drv-offduty is Off-Duty
        var offDutyRes = await client.PostAsJsonAsync(
            $"/api/v1/consignments/{consignment!.Id}/assign",
            new AssignFleetRequest("vh-cold-01", "drv-offduty"));
        Assert.Equal(HttpStatusCode.BadRequest, offDutyRes.StatusCode);

        // drv-busy has 3 active routes (MaxRoutesLimit: 3)
        var busyRes = await client.PostAsJsonAsync(
            $"/api/v1/consignments/{consignment.Id}/assign",
            new AssignFleetRequest("vh-cold-01", "drv-busy"));
        Assert.Equal(HttpStatusCode.BadRequest, busyRes.StatusCode);
    }

    [Fact]
    public async Task Feature2_UpdateStateToAssigned_AndGenerateConsolidatedLoadingManifest()
    {
        var client = _factory.CreateClient();

        var regRes = await client.PostAsJsonAsync("/api/v1/consignments", new CreateConsignmentRequest(
            "COVID Test Kits",
            2.0, 8.0,
            15.0,
            25, 25, 25,
            ConsignmentUrgency.Standard,
            47.6062, -122.3321,
            37.7749, -122.4194,
            "+1-555-0199",
            "depot@health.org",
            "Dispatcher"));
        var consignment = await regRes.Content.ReadFromJsonAsync<Consignment>();

        // Assign valid vehicle and driver (vh-cold-02, drv-02)
        var assignRes = await client.PostAsJsonAsync(
            $"/api/v1/consignments/{consignment!.Id}/assign",
            new AssignFleetRequest("vh-cold-02", "drv-02"));

        Assert.Equal(HttpStatusCode.OK, assignRes.StatusCode);
        var manifest = await assignRes.Content.ReadFromJsonAsync<LoadingManifest>();
        Assert.NotNull(manifest);
        Assert.Matches(@"^MNF-\d{5}$", manifest!.ManifestId);
        Assert.Equal("vh-cold-02", manifest.VehicleId);
        Assert.Equal("drv-02", manifest.DriverId);
        Assert.Contains(consignment.Id, manifest.ConsignmentIds);

        // Verify consignment state updated to Assigned
        var updatedConsignment = await client.GetFromJsonAsync<Consignment>($"/api/v1/consignments/{consignment.Id}");
        Assert.Equal(ConsignmentStatus.Assigned, updatedConsignment!.Status);
    }

    // =========================================================================
    // FEATURE 3: Real-time cold-chain telemetry & breach alerting
    // =========================================================================

    [Fact]
    public async Task Feature3_IngestTelemetry_BreachAlertingAfterTwoConsecutiveReadings_AndDynamicEta()
    {
        var client = _factory.CreateClient();

        // Register consignment with tolerance [2.0°C, 8.0°C]
        var regRes = await client.PostAsJsonAsync("/api/v1/consignments", new CreateConsignmentRequest(
            "Biologic Therapeutics",
            2.0, 8.0,
            20.0,
            30, 30, 30,
            ConsignmentUrgency.LifeCritical,
            47.6062, -122.3321,
            47.6100, -122.3300,
            "+1-555-0199",
            "biologics@lab.org",
            "Clinical Director"));
        var consignment = await regRes.Content.ReadFromJsonAsync<Consignment>();

        // Assign fleet so it can enter transit
        await client.PostAsJsonAsync(
            $"/api/v1/consignments/{consignment!.Id}/assign",
            new AssignFleetRequest("vh-cold-01", "drv-01"));

        // 1st telemetry reading: within range (4.5°C) -> Status should be In_Transit
        var t1 = new IngestTelemetryRequest(consignment.Id, 47.6070, -122.3315, 4.5, DateTimeOffset.UtcNow.AddMinutes(-10));
        var resT1 = await client.PostAsJsonAsync("/api/v1/telemetry/ingest", t1);
        Assert.Equal(HttpStatusCode.OK, resT1.StatusCode);

        var c1 = await client.GetFromJsonAsync<Consignment>($"/api/v1/consignments/{consignment.Id}");
        Assert.Equal(ConsignmentStatus.InTransit, c1!.Status);
        Assert.NotNull(c1.EtaMinutes);
        Assert.True(c1.EtaMinutes > 0);

        // 2nd reading: OUT OF TOLERANCE (10.5°C) - 1st out of tolerance reading -> Should NOT yet breach status
        var t2 = new IngestTelemetryRequest(consignment.Id, 47.6080, -122.3310, 10.5, DateTimeOffset.UtcNow.AddMinutes(-5));
        var resT2 = await client.PostAsJsonAsync("/api/v1/telemetry/ingest", t2);
        Assert.Equal(HttpStatusCode.OK, resT2.StatusCode);

        var c2 = await client.GetFromJsonAsync<Consignment>($"/api/v1/consignments/{consignment.Id}");
        Assert.Equal(ConsignmentStatus.InTransit, c2!.Status); // 1 violation: not yet breach state

        // 3rd reading: OUT OF TOLERANCE (12.8°C) - 2nd CONSECUTIVE reading -> MUST transition to Temperature_Breach!
        var t3 = new IngestTelemetryRequest(consignment.Id, 47.6090, -122.3305, 12.8, DateTimeOffset.UtcNow);
        var resT3 = await client.PostAsJsonAsync("/api/v1/telemetry/ingest", t3);
        Assert.Equal(HttpStatusCode.OK, resT3.StatusCode);

        var c3 = await client.GetFromJsonAsync<Consignment>($"/api/v1/consignments/{consignment.Id}");
        Assert.Equal(ConsignmentStatus.TemperatureBreach, c3!.Status);

        // Verify incident logging (start timestamp and peak temperature)
        var incidents = await client.GetFromJsonAsync<List<BreachIncident>>("/api/v1/incidents");
        Assert.NotNull(incidents);
        var activeIncident = Assert.Single(incidents!, i => i.ShipmentId == consignment.Id);
        Assert.Equal(12.8, activeIncident.PeakTemperatureC);
        Assert.True(activeIncident.IsActive);

        // 4th reading: Normalized temperature (5.0°C) -> incident normalized and duration computed
        var t4 = new IngestTelemetryRequest(consignment.Id, 47.6095, -122.3302, 5.0, DateTimeOffset.UtcNow.AddMinutes(2));
        await client.PostAsJsonAsync("/api/v1/telemetry/ingest", t4);

        var c4 = await client.GetFromJsonAsync<Consignment>($"/api/v1/consignments/{consignment.Id}");
        Assert.Equal(ConsignmentStatus.InTransit, c4!.Status);

        var updatedIncidents = await client.GetFromJsonAsync<List<BreachIncident>>("/api/v1/incidents");
        var resolvedIncident = Assert.Single(updatedIncidents!, i => i.ShipmentId == consignment.Id);
        Assert.False(resolvedIncident.IsActive);
        Assert.NotNull(resolvedIncident.DurationSeconds);
        Assert.True(resolvedIncident.DurationSeconds > 0);
    }

    // =========================================================================
    // FEATURE 4: Geofenced delivery handoff & digital proof of delivery (POD)
    // =========================================================================

    [Fact]
    public async Task Feature4_DisableDeliveryWhenBeyond200m_AndLockAfter3FailedOtps()
    {
        var client = _factory.CreateClient();

        // Destination: (47.61000, -122.33000)
        var regRes = await client.PostAsJsonAsync("/api/v1/consignments", new CreateConsignmentRequest(
            "Sterile Surgical Implants",
            15.0, 25.0,
            8.0,
            15, 15, 15,
            ConsignmentUrgency.Standard,
            47.6062, -122.3321,
            47.61000, -122.33000,
            "+1-555-0199",
            "or@surgery.org",
            "Head Nurse"));
        var consignment = await regRes.Content.ReadFromJsonAsync<Consignment>();

        // 1. Far away from destination (approx 10km away: 47.70000, -122.33000) -> Arrival must fail geofence
        var farArrival = new ArrivalRequest(47.70000, -122.33000);
        var farArrivalRes = await client.PostAsJsonAsync($"/api/v1/consignments/{consignment!.Id}/arrival", farArrival);
        Assert.Equal(HttpStatusCode.BadRequest, farArrivalRes.StatusCode);
        var farBody = await farArrivalRes.Content.ReadAsStringAsync();
        Assert.Contains("within 200 meters", farBody, StringComparison.OrdinalIgnoreCase);

        // 2. Near destination (approx 50m away: 47.61030, -122.33000) -> Arrival succeeds and generates 6-digit OTP
        var nearArrival = new ArrivalRequest(47.61030, -122.33000);
        var nearArrivalRes = await client.PostAsJsonAsync($"/api/v1/consignments/{consignment.Id}/arrival", nearArrival);
        Assert.Equal(HttpStatusCode.OK, nearArrivalRes.StatusCode);

        var arrivedConsignment = await nearArrivalRes.Content.ReadFromJsonAsync<Consignment>();
        Assert.NotNull(arrivedConsignment);
        Assert.Equal(ConsignmentStatus.ArrivedAtDestination, arrivedConsignment!.Status);
        Assert.NotNull(arrivedConsignment.CurrentOtp);
        Assert.Matches(@"^\d{6}$", arrivedConsignment.CurrentOtp);

        string validOtp = arrivedConsignment.CurrentOtp;

        // 3. Complete delivery while far away (> 200m) must be disabled / blocked
        var farDelivery = new CompleteDeliveryRequest(
            47.70000, -122.33000,
            validOtp,
            "Pharmacist Jane",
            "Lead Pharmacist",
            "data:image/svg+xml;base64,PHN2Zz48L3N2Zz4=");
        var farDeliveryRes = await client.PostAsJsonAsync($"/api/v1/consignments/{consignment.Id}/complete-delivery", farDelivery);
        Assert.Equal(HttpStatusCode.BadRequest, farDeliveryRes.StatusCode);

        // 4. Test 3 failed OTP attempts locks delivery
        var wrongOtpRequest = new CompleteDeliveryRequest(
            47.61030, -122.33000,
            "000000", // Wrong OTP
            "Pharmacist Jane",
            "Lead Pharmacist",
            "data:image/svg+xml;base64,PHN2Zz48L3N2Zz4=");

        // Attempt 1
        var fail1 = await client.PostAsJsonAsync($"/api/v1/consignments/{consignment.Id}/complete-delivery", wrongOtpRequest);
        Assert.Equal(HttpStatusCode.BadRequest, fail1.StatusCode);

        // Attempt 2
        var fail2 = await client.PostAsJsonAsync($"/api/v1/consignments/{consignment.Id}/complete-delivery", wrongOtpRequest);
        Assert.Equal(HttpStatusCode.BadRequest, fail2.StatusCode);

        // Attempt 3 -> LOCKS delivery!
        var fail3 = await client.PostAsJsonAsync($"/api/v1/consignments/{consignment.Id}/complete-delivery", wrongOtpRequest);
        Assert.Equal(HttpStatusCode.BadRequest, fail3.StatusCode);
        var lockMsg = await fail3.Content.ReadAsStringAsync();
        Assert.Contains("locked", lockMsg, StringComparison.OrdinalIgnoreCase);

        // Verify status in repository is DeliveryLocked
        var lockedConsignment = await client.GetFromJsonAsync<Consignment>($"/api/v1/consignments/{consignment.Id}");
        Assert.True(lockedConsignment!.IsLocked);
        Assert.Equal(ConsignmentStatus.DeliveryLocked, lockedConsignment.Status);
    }

    [Fact]
    public async Task Feature4_SuccessfulHandoff_GeneratesImmutablePodReceiptWithTemperatureGraph()
    {
        var client = _factory.CreateClient();

        // Create consignment
        var regRes = await client.PostAsJsonAsync("/api/v1/consignments", new CreateConsignmentRequest(
            "Monoclonal Antibodies",
            2.0, 8.0,
            12.0,
            20, 20, 20,
            ConsignmentUrgency.LifeCritical,
            47.6062, -122.3321,
            47.61000, -122.33000,
            "+1-555-0199",
            "oncology@clinic.org",
            "Oncologist"));
        var consignment = await regRes.Content.ReadFromJsonAsync<Consignment>();

        // Ingest telemetry points to build in-transit temperature graph
        await client.PostAsJsonAsync("/api/v1/telemetry/ingest", new IngestTelemetryRequest(
            consignment!.Id, 47.6065, -122.3320, 4.2, DateTimeOffset.UtcNow.AddMinutes(-30)));
        await client.PostAsJsonAsync("/api/v1/telemetry/ingest", new IngestTelemetryRequest(
            consignment.Id, 47.6080, -122.3310, 5.1, DateTimeOffset.UtcNow.AddMinutes(-15)));
        await client.PostAsJsonAsync("/api/v1/telemetry/ingest", new IngestTelemetryRequest(
            consignment.Id, 47.6098, -122.3302, 4.8, DateTimeOffset.UtcNow.AddMinutes(-5)));

        // Arrive within 200m
        var arrivalRes = await client.PostAsJsonAsync(
            $"/api/v1/consignments/{consignment.Id}/arrival",
            new ArrivalRequest(47.61010, -122.33005));
        var arrivedConsignment = await arrivalRes.Content.ReadFromJsonAsync<Consignment>();
        string otp = arrivedConsignment!.CurrentOtp!;

        // Complete delivery with valid signature, recipient name, title, and OTP
        var completeReq = new CompleteDeliveryRequest(
            47.61010, -122.33005,
            otp,
            "Dr. Amanda Cole",
            "Chief Pharmacist",
            "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

        var completeRes = await client.PostAsJsonAsync(
            $"/api/v1/consignments/{consignment.Id}/complete-delivery",
            completeReq);

        Assert.Equal(HttpStatusCode.OK, completeRes.StatusCode);
        var pod = await completeRes.Content.ReadFromJsonAsync<PodReceipt>();

        Assert.NotNull(pod);
        Assert.Matches(@"^POD-\d{5}$", pod!.PodId);
        Assert.Equal(consignment.Id, pod.ShipmentId);
        Assert.Equal("Dr. Amanda Cole", pod.RecipientName);
        Assert.Equal("Chief Pharmacist", pod.RecipientJobTitle);
        Assert.NotEmpty(pod.SignatureData);

        // Full in-transit temperature graph embedded
        Assert.NotNull(pod.TemperatureGraph);
        Assert.Equal(3, pod.TemperatureGraph.Count);
        Assert.Contains(pod.TemperatureGraph, p => p.TempC == 4.2);
        Assert.Contains(pod.TemperatureGraph, p => p.TempC == 5.1);

        // Verify status transitioned to Delivered
        var deliveredConsignment = await client.GetFromJsonAsync<Consignment>($"/api/v1/consignments/{consignment.Id}");
        Assert.Equal(ConsignmentStatus.Delivered, deliveredConsignment!.Status);
        Assert.NotNull(deliveredConsignment.DeliveredAt);

        // Verify POD endpoint
        var getPodRes = await client.GetAsync($"/api/v1/consignments/{consignment.Id}/pod");
        Assert.Equal(HttpStatusCode.OK, getPodRes.StatusCode);
    }

    // =========================================================================
    // ADDITIONAL COVERAGE & EDGE CASES
    // =========================================================================

    [Fact]
    public async Task Consignments_QueryAndFilters_WorkCorrectly()
    {
        var client = _factory.CreateClient();

        // Get all consignments
        var allRes = await client.GetFromJsonAsync<List<Consignment>>("/api/v1/consignments");
        Assert.NotNull(allRes);
        Assert.NotEmpty(allRes!);

        // Filter by status
        var filteredStatus = await client.GetFromJsonAsync<List<Consignment>>("/api/v1/consignments?status=Awaiting_Assignment");
        Assert.NotNull(filteredStatus);

        // Filter by urgency
        var filteredUrgency = await client.GetFromJsonAsync<List<Consignment>>("/api/v1/consignments?urgency=Life-Critical");
        Assert.NotNull(filteredUrgency);

        // Get single by ID (valid)
        var sample = allRes![0];
        var singleRes = await client.GetAsync($"/api/v1/consignments/{sample.Id}");
        Assert.Equal(HttpStatusCode.OK, singleRes.StatusCode);

        // Get single by ID (missing -> 404)
        var missingRes = await client.GetAsync("/api/v1/consignments/shp-non-existent");
        Assert.Equal(HttpStatusCode.NotFound, missingRes.StatusCode);

        // Track by code (valid)
        var trackValid = await client.GetAsync($"/api/v1/consignments/track/{sample.TrackingNumber}");
        Assert.Equal(HttpStatusCode.OK, trackValid.StatusCode);

        // Track by code (missing -> 404)
        var trackMissing = await client.GetAsync("/api/v1/consignments/track/TRK-999999");
        Assert.Equal(HttpStatusCode.NotFound, trackMissing.StatusCode);
    }

    [Fact]
    public async Task FleetAndManifests_Endpoints_WorkCorrectly()
    {
        var client = _factory.CreateClient();

        // Vehicles
        var vehicles = await client.GetFromJsonAsync<List<Vehicle>>("/api/v1/fleet/vehicles");
        Assert.NotNull(vehicles);
        Assert.NotEmpty(vehicles!);

        // Drivers
        var drivers = await client.GetFromJsonAsync<List<Driver>>("/api/v1/fleet/drivers");
        Assert.NotNull(drivers);
        Assert.NotEmpty(drivers!);

        // Manifest 404
        var missingManifest = await client.GetAsync("/api/v1/fleet/manifests/MNF-00000");
        Assert.Equal(HttpStatusCode.NotFound, missingManifest.StatusCode);

        // Assign with missing consignment (404)
        var assignMissingConsignment = await client.PostAsJsonAsync(
            "/api/v1/consignments/shp-missing/assign",
            new AssignFleetRequest("vh-cold-01", "drv-01"));
        Assert.Equal(HttpStatusCode.NotFound, assignMissingConsignment.StatusCode);

        // Assign with missing vehicle (400)
        var sample = (await client.GetFromJsonAsync<List<Consignment>>("/api/v1/consignments"))![0];
        var assignMissingVehicle = await client.PostAsJsonAsync(
            $"/api/v1/consignments/{sample.Id}/assign",
            new AssignFleetRequest("vh-missing", "drv-01"));
        Assert.Equal(HttpStatusCode.BadRequest, assignMissingVehicle.StatusCode);

        // Assign with missing driver (400)
        var assignMissingDriver = await client.PostAsJsonAsync(
            $"/api/v1/consignments/{sample.Id}/assign",
            new AssignFleetRequest("vh-cold-01", "drv-missing"));
        Assert.Equal(HttpStatusCode.BadRequest, assignMissingDriver.StatusCode);
    }

    [Fact]
    public async Task Telemetry_HistoryAndEdgeCases_WorkCorrectly()
    {
        var client = _factory.CreateClient();

        // Telemetry ingest for missing shipment (404)
        var missingTelemetry = await client.PostAsJsonAsync(
            "/api/v1/telemetry/ingest",
            new IngestTelemetryRequest("shp-missing", 47.0, -122.0, 5.0, DateTimeOffset.UtcNow));
        Assert.Equal(HttpStatusCode.NotFound, missingTelemetry.StatusCode);

        // Ingest below min_temp breach (consecutive)
        var regRes = await client.PostAsJsonAsync("/api/v1/consignments", new CreateConsignmentRequest(
            "Cryo Sample",
            -20.0, -10.0,
            5.0,
            10, 10, 10,
            ConsignmentUrgency.Standard,
            47.6062, -122.3321,
            47.6100, -122.3300,
            "+1-555-0199",
            "cryo@lab.org",
            "Tech"));
        var consignment = await regRes.Content.ReadFromJsonAsync<Consignment>();

        // Reading 1: too warm (-5°C > -10°C)
        await client.PostAsJsonAsync("/api/v1/telemetry/ingest",
            new IngestTelemetryRequest(consignment!.Id, 47.6070, -122.3315, -5.0, DateTimeOffset.UtcNow.AddMinutes(-5)));

        // Reading 2: even warmer (-2°C) -> peak updated
        await client.PostAsJsonAsync("/api/v1/telemetry/ingest",
            new IngestTelemetryRequest(consignment.Id, 47.6070, -122.3315, -2.0, DateTimeOffset.UtcNow));

        // Get telemetry history
        var history = await client.GetFromJsonAsync<List<TelemetryReading>>($"/api/v1/telemetry/{consignment.Id}");
        Assert.NotNull(history);
        Assert.Equal(2, history!.Count);

        // Get filtered incidents
        var incidents = await client.GetFromJsonAsync<List<BreachIncident>>($"/api/v1/incidents?shipmentId={consignment.Id}");
        Assert.NotNull(incidents);
        Assert.NotEmpty(incidents!);
    }

    [Fact]
    public async Task Delivery_MissingEntitiesAndFields_ReturnBadRequestOrNotFound()
    {
        var client = _factory.CreateClient();

        // Arrival 404
        var arrivalMissing = await client.PostAsJsonAsync(
            "/api/v1/consignments/shp-missing/arrival",
            new ArrivalRequest(47.6100, -122.3300));
        Assert.Equal(HttpStatusCode.NotFound, arrivalMissing.StatusCode);

        // Complete delivery 404
        var completeMissing = await client.PostAsJsonAsync(
            "/api/v1/consignments/shp-missing/complete-delivery",
            new CompleteDeliveryRequest(47.6100, -122.3300, "123456", "Name", "Title", "sig"));
        Assert.Equal(HttpStatusCode.NotFound, completeMissing.StatusCode);

        // POD 404
        var podMissing = await client.GetAsync("/api/v1/consignments/shp-missing/pod");
        Assert.Equal(HttpStatusCode.NotFound, podMissing.StatusCode);

        // Intake invalid weight <= 0
        var invalidWeight = new CreateConsignmentRequest(
            "Zero weight",
            2.0, 8.0,
            -1.0,
            10, 10, 10,
            ConsignmentUrgency.Standard,
            47.6062, -122.3321,
            47.6100, -122.3300,
            "+1-555-0199",
            "test@test.com",
            "Test");
        var resWeight = await client.PostAsJsonAsync("/api/v1/consignments", invalidWeight);
        Assert.Equal(HttpStatusCode.BadRequest, resWeight.StatusCode);

        // Complete delivery missing signature, name or title
        var regRes = await client.PostAsJsonAsync("/api/v1/consignments", new CreateConsignmentRequest(
            "Test Consignment",
            2.0, 8.0,
            5.0,
            10, 10, 10,
            ConsignmentUrgency.Standard,
            47.6062, -122.3321,
            47.6100, -122.3300,
            "+1-555-0199",
            "test@test.com",
            "Test"));
        var consignment = await regRes.Content.ReadFromJsonAsync<Consignment>();

        // Arrive
        var arr = await client.PostAsJsonAsync(
            $"/api/v1/consignments/{consignment!.Id}/arrival",
            new ArrivalRequest(47.6101, -122.3300));
        var arrConsignment = await arr.Content.ReadFromJsonAsync<Consignment>();
        string otp = arrConsignment!.CurrentOtp!;

        // Missing signature
        var missingSig = await client.PostAsJsonAsync(
            $"/api/v1/consignments/{consignment.Id}/complete-delivery",
            new CompleteDeliveryRequest(47.6101, -122.3300, otp, "Name", "Title", ""));
        Assert.Equal(HttpStatusCode.BadRequest, missingSig.StatusCode);

        // Missing name
        var missingName = await client.PostAsJsonAsync(
            $"/api/v1/consignments/{consignment.Id}/complete-delivery",
            new CompleteDeliveryRequest(47.6101, -122.3300, otp, "", "Title", "sig"));
        Assert.Equal(HttpStatusCode.BadRequest, missingName.StatusCode);

        // Missing job title
        var missingTitle = await client.PostAsJsonAsync(
            $"/api/v1/consignments/{consignment.Id}/complete-delivery",
            new CompleteDeliveryRequest(47.6101, -122.3300, otp, "Name", "", "sig"));
        Assert.Equal(HttpStatusCode.BadRequest, missingTitle.StatusCode);
    }
}
