using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CalendarWidget;

public class EventStore
{
    private static readonly string EventsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CalendarWidget", "events.json");

    private List<CalendarEvent> _events = new();

    public void Load()
    {
        try
        {
            GoogleCalendarService.Log($"EventsPath: {EventsPath}");
            GoogleCalendarService.Log($"events.json existe: {File.Exists(EventsPath)}");

            if (!File.Exists(EventsPath)) return;

            var json = File.ReadAllText(EventsPath);
            GoogleCalendarService.Log($"JSON lido: {json}");

            var doc = JsonDocument.Parse(json);
            var arr = doc.RootElement.GetProperty("events");
            _events = JsonSerializer.Deserialize<List<CalendarEvent>>(arr.GetRawText()) ?? new();
            GoogleCalendarService.Log($"Eventos locais carregados: {_events.Count}");
        }
        catch (Exception ex)
        {
            GoogleCalendarService.Log($"EventStore.Load ERRO: {ex.Message}");
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(EventsPath)!);
            var json = JsonSerializer.Serialize(new { events = _events }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(EventsPath, json);
        }
        catch { }
    }

    public List<CalendarEvent> GetByDate(DateOnly date)
    {
        var key = date.ToString("yyyy-MM-dd");
        return _events.Where(e => e.Date == key).OrderBy(e => e.Time).ToList();
    }

    public void Add(CalendarEvent evt)
    {
        _events.Add(evt);
        Save();
    }

    public void Update(CalendarEvent evt)
    {
        var index = _events.FindIndex(e => e.Id == evt.Id);
        if (index >= 0)
        {
            _events[index] = evt;
            Save();
        }
    }

    public void Remove(string id)
    {
        _events.RemoveAll(e => e.Id == id);
        Save();
    }
}