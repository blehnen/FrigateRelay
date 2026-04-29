using FluentAssertions;
using FrigateRelay.Sources.FrigateMqtt;
using FrigateRelay.Sources.FrigateMqtt.Payloads;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;

namespace FrigateRelay.Sources.FrigateMqtt.Tests;

/// <summary>
/// Deserialization tests for Frigate MQTT payload DTOs.
/// Covers all three event types, snake_case round-trip, optional fields, and zone arrays.
/// </summary>
[TestClass]
public sealed class PayloadDeserializationTests
{
    // Sample "new" event from RESEARCH.md annotated sample
    private const string NewEventJson = """
        {
          "type": "new",
          "before": {
            "id": "1714000001.123456-abc123",
            "camera": "front_door",
            "label": "person",
            "sub_label": null,
            "score": 0.84,
            "top_score": 0.84,
            "start_time": 1714000001.123,
            "end_time": null,
            "stationary": false,
            "active": true,
            "false_positive": false,
            "current_zones": [],
            "entered_zones": [],
            "has_snapshot": false,
            "has_clip": false,
            "thumbnail": null,
            "frame_time": 1714000001.1,
            "box": [100, 200, 300, 500],
            "area": 40000,
            "region": [0, 0, 1920, 1080],
            "motionless_count": 0,
            "position_changes": 0,
            "attributes": {},
            "current_attributes": []
          },
          "after": {
            "id": "1714000001.123456-abc123",
            "camera": "front_door",
            "label": "person",
            "sub_label": null,
            "score": 0.91,
            "top_score": 0.91,
            "start_time": 1714000001.123,
            "end_time": null,
            "stationary": false,
            "active": true,
            "false_positive": false,
            "current_zones": ["driveway"],
            "entered_zones": ["driveway"],
            "has_snapshot": true,
            "has_clip": false,
            "thumbnail": null,
            "frame_time": 1714000001.2,
            "box": [105, 205, 305, 505],
            "area": 40000,
            "region": [0, 0, 1920, 1080],
            "motionless_count": 0,
            "position_changes": 1,
            "attributes": {},
            "current_attributes": []
          }
        }
        """;

    private const string UpdateStationaryJson = """
        {
          "type": "update",
          "before": {
            "id": "1714000002.000000-def456",
            "camera": "backyard",
            "label": "car",
            "sub_label": null,
            "score": 0.75,
            "top_score": 0.80,
            "start_time": 1714000002.0,
            "end_time": null,
            "stationary": false,
            "active": true,
            "false_positive": false,
            "current_zones": [],
            "entered_zones": [],
            "has_snapshot": true,
            "has_clip": false,
            "thumbnail": null,
            "frame_time": 1714000002.0,
            "box": [10, 20, 100, 200],
            "area": 16200,
            "region": [0, 0, 1920, 1080],
            "motionless_count": 0,
            "position_changes": 2,
            "attributes": {},
            "current_attributes": []
          },
          "after": {
            "id": "1714000002.000000-def456",
            "camera": "backyard",
            "label": "car",
            "sub_label": null,
            "score": 0.75,
            "top_score": 0.80,
            "start_time": 1714000002.0,
            "end_time": null,
            "stationary": true,
            "active": false,
            "false_positive": false,
            "current_zones": [],
            "entered_zones": [],
            "has_snapshot": true,
            "has_clip": false,
            "thumbnail": null,
            "frame_time": 1714000002.5,
            "box": [10, 20, 100, 200],
            "area": 16200,
            "region": [0, 0, 1920, 1080],
            "motionless_count": 10,
            "position_changes": 2,
            "attributes": {},
            "current_attributes": []
          }
        }
        """;

    private const string EndEventJson = """
        {
          "type": "end",
          "before": {
            "id": "1714000003.000000-ghi789",
            "camera": "side_gate",
            "label": "dog",
            "sub_label": null,
            "score": 0.65,
            "top_score": 0.72,
            "start_time": 1714000003.0,
            "end_time": null,
            "stationary": false,
            "active": true,
            "false_positive": false,
            "current_zones": ["yard"],
            "entered_zones": ["yard"],
            "has_snapshot": true,
            "has_clip": false,
            "thumbnail": null,
            "frame_time": 1714000003.0,
            "box": [50, 50, 150, 150],
            "area": 10000,
            "region": [0, 0, 1280, 720],
            "motionless_count": 0,
            "position_changes": 5,
            "attributes": {},
            "current_attributes": []
          },
          "after": {
            "id": "1714000003.000000-ghi789",
            "camera": "side_gate",
            "label": "dog",
            "sub_label": null,
            "score": 0.65,
            "top_score": 0.72,
            "start_time": 1714000003.0,
            "end_time": 1714000060.5,
            "stationary": false,
            "active": true,
            "false_positive": false,
            "current_zones": ["yard"],
            "entered_zones": ["yard"],
            "has_snapshot": true,
            "has_clip": true,
            "thumbnail": null,
            "frame_time": 1714000060.5,
            "box": [55, 55, 155, 155],
            "area": 10000,
            "region": [0, 0, 1280, 720],
            "motionless_count": 0,
            "position_changes": 8,
            "attributes": {},
            "current_attributes": []
          }
        }
        """;

    private const string SubLabelNullJson = """
        {
          "type": "update",
          "before": {
            "id": "1714000004.000000-jkl012",
            "camera": "front_door",
            "label": "person",
            "sub_label": null,
            "score": 0.90,
            "top_score": 0.92,
            "start_time": 1714000004.0,
            "end_time": null,
            "stationary": false,
            "active": true,
            "false_positive": false,
            "current_zones": [],
            "entered_zones": [],
            "has_snapshot": false,
            "has_clip": false,
            "thumbnail": null,
            "frame_time": 1714000004.0,
            "box": [0, 0, 100, 100],
            "area": 10000,
            "region": [0, 0, 1920, 1080],
            "motionless_count": 0,
            "position_changes": 0,
            "attributes": {},
            "current_attributes": []
          },
          "after": {
            "id": "1714000004.000000-jkl012",
            "camera": "front_door",
            "label": "person",
            "sub_label": null,
            "score": 0.92,
            "top_score": 0.92,
            "start_time": 1714000004.0,
            "end_time": null,
            "stationary": false,
            "active": true,
            "false_positive": false,
            "current_zones": [],
            "entered_zones": [],
            "has_snapshot": false,
            "has_clip": false,
            "thumbnail": null,
            "frame_time": 1714000004.1,
            "box": [0, 0, 100, 100],
            "area": 10000,
            "region": [0, 0, 1920, 1080],
            "motionless_count": 0,
            "position_changes": 0,
            "attributes": {},
            "current_attributes": []
          }
        }
        """;

    // JSON where thumbnail field is completely omitted (not present)
    private const string ThumbnailOmittedJson = """
        {
          "type": "new",
          "before": {
            "id": "1714000005.000000-mno345",
            "camera": "garage",
            "label": "car",
            "score": 0.88,
            "top_score": 0.88,
            "start_time": 1714000005.0,
            "stationary": false,
            "active": true,
            "false_positive": false,
            "current_zones": [],
            "entered_zones": [],
            "has_snapshot": false,
            "has_clip": false,
            "frame_time": 1714000005.0,
            "box": [0, 0, 50, 50],
            "area": 2500,
            "region": [0, 0, 1920, 1080],
            "motionless_count": 0,
            "position_changes": 0,
            "attributes": {},
            "current_attributes": []
          },
          "after": {
            "id": "1714000005.000000-mno345",
            "camera": "garage",
            "label": "car",
            "score": 0.88,
            "top_score": 0.88,
            "start_time": 1714000005.0,
            "stationary": false,
            "active": true,
            "false_positive": false,
            "current_zones": [],
            "entered_zones": [],
            "has_snapshot": false,
            "has_clip": false,
            "frame_time": 1714000005.0,
            "box": [0, 0, 50, 50],
            "area": 2500,
            "region": [0, 0, 1920, 1080],
            "motionless_count": 0,
            "position_changes": 0,
            "attributes": {},
            "current_attributes": []
          }
        }
        """;

    private const string EmptyZonesJson = """
        {
          "type": "new",
          "before": {
            "id": "1714000006.000000-pqr678",
            "camera": "backyard",
            "label": "person",
            "score": 0.80,
            "top_score": 0.80,
            "start_time": 1714000006.0,
            "stationary": false,
            "active": true,
            "false_positive": false,
            "current_zones": [],
            "entered_zones": [],
            "has_snapshot": false,
            "has_clip": false,
            "thumbnail": null,
            "frame_time": 1714000006.0,
            "box": [0, 0, 100, 100],
            "area": 10000,
            "region": [0, 0, 1920, 1080],
            "motionless_count": 0,
            "position_changes": 0,
            "attributes": {},
            "current_attributes": []
          },
          "after": {
            "id": "1714000006.000000-pqr678",
            "camera": "backyard",
            "label": "person",
            "score": 0.80,
            "top_score": 0.80,
            "start_time": 1714000006.0,
            "stationary": false,
            "active": true,
            "false_positive": false,
            "current_zones": [],
            "entered_zones": [],
            "has_snapshot": false,
            "has_clip": false,
            "thumbnail": null,
            "frame_time": 1714000006.0,
            "box": [0, 0, 100, 100],
            "area": 10000,
            "region": [0, 0, 1920, 1080],
            "motionless_count": 0,
            "position_changes": 0,
            "attributes": {},
            "current_attributes": []
          }
        }
        """;

    /// <summary>
    /// Test 1: Deserializes a "new" event and asserts key fields on the after object.
    /// </summary>
    [TestMethod]
    public void Deserialize_NewEvent_PopulatesTypeAndAfterFields()
    {
        var evt = JsonSerializer.Deserialize<FrigateEvent>(NewEventJson, FrigateJsonOptions.Default);

        evt.Should().NotBeNull();
        evt!.Type.Should().Be("new");
        evt.After!.Camera.Should().Be("front_door");
        evt.After!.Label.Should().Be("person");
        evt.After!.CurrentZones.Should().Equal("driveway");
        evt.After!.Stationary.Should().BeFalse();
        evt.After!.FalsePositive.Should().BeFalse();
    }

    /// <summary>
    /// Test 2: Deserializes an "update" event where after.stationary is true.
    /// </summary>
    [TestMethod]
    public void Deserialize_UpdateEvent_StationaryTrueRoundTrips()
    {
        var evt = JsonSerializer.Deserialize<FrigateEvent>(UpdateStationaryJson, FrigateJsonOptions.Default);

        evt.Should().NotBeNull();
        evt!.Type.Should().Be("update");
        evt.After!.Stationary.Should().BeTrue();
        evt.Before!.Stationary.Should().BeFalse();
    }

    /// <summary>
    /// Test 3: Deserializes an "end" event and asserts EndTime is non-null.
    /// </summary>
    [TestMethod]
    public void Deserialize_EndEvent_AfterEndTimeIsNonNull()
    {
        var evt = JsonSerializer.Deserialize<FrigateEvent>(EndEventJson, FrigateJsonOptions.Default);

        evt.Should().NotBeNull();
        evt!.Type.Should().Be("end");
        evt.After!.EndTime.Should().NotBeNull();
        evt.After!.EndTime.Should().BeApproximately(1714000060.5, 0.001);
    }

    /// <summary>
    /// Test 4: Confirms that a null sub_label in JSON deserializes to SubLabel == null.
    /// Verifies snake_case → PascalCase mapping (sub_label → SubLabel).
    /// </summary>
    [TestMethod]
    public void Deserialize_SubLabelNull_DeserializesToNullSubLabel()
    {
        var evt = JsonSerializer.Deserialize<FrigateEvent>(SubLabelNullJson, FrigateJsonOptions.Default);

        evt.Should().NotBeNull();
        evt!.After!.SubLabel.Should().BeNull();
        evt.Before!.SubLabel.Should().BeNull();
    }

    /// <summary>
    /// Test 5: Confirms that an omitted thumbnail field deserializes to null (not an error).
    /// </summary>
    [TestMethod]
    public void Deserialize_ThumbnailOmitted_DeserializesToNull()
    {
        var evt = JsonSerializer.Deserialize<FrigateEvent>(ThumbnailOmittedJson, FrigateJsonOptions.Default);

        evt.Should().NotBeNull();
        evt!.After!.Thumbnail.Should().BeNull();
        evt.Before!.Thumbnail.Should().BeNull();
    }

    /// <summary>
    /// Test 6: Confirms that current_zones: [] deserializes to a non-null empty IReadOnlyList.
    /// </summary>
    [TestMethod]
    public void Deserialize_EmptyCurrentZones_DeserializesToNonNullEmptyList()
    {
        var evt = JsonSerializer.Deserialize<FrigateEvent>(EmptyZonesJson, FrigateJsonOptions.Default);

        evt.Should().NotBeNull();
        evt!.After!.CurrentZones.Should().NotBeNull();
        evt.After!.CurrentZones.Should().BeEmpty();
        evt.After!.EnteredZones.Should().NotBeNull();
        evt.After!.EnteredZones.Should().BeEmpty();
    }
}
