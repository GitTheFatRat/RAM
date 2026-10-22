namespace RbxManager.App.UiTests;

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class AutomationNode
{
    [JsonPropertyName("controlType")]
    public string ControlType { get; set; } = string.Empty;

    [JsonPropertyName("automationId")]
    public string AutomationId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; }

    [JsonPropertyName("isOffscreen")]
    public bool IsOffscreen { get; set; }

    [JsonPropertyName("hasTextValue")]
    public bool HasTextValue { get; set; }

    [JsonPropertyName("boundingRectangle")]
    public BoundingBox BoundingRectangle { get; set; } = new();

    [JsonPropertyName("children")]
    public List<AutomationNode> Children { get; set; } = new();
}

public sealed class BoundingBox
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("width")]
    public double Width { get; set; }

    [JsonPropertyName("height")]
    public double Height { get; set; }
}

public static class AutomationSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string ToJson(AutomationNode root)
    {
        return JsonSerializer.Serialize(root, JsonOptions);
    }
}
