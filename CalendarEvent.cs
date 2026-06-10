using System;

namespace CalendarWidget;

public class CalendarEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Date { get; set; } = "";
    public string Name { get; set; } = "";
    public string Time { get; set; } = "";
    public string Color { get; set; } = "#60a5fa";
    public bool IsGoogleEvent => Id.StartsWith("gcal_");
    public string GoogleEventId => Id.Replace("gcal_", "");
}