using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CalendarWidget;

public class GoogleCalendarService
{
    // private static readonly string[] Scopes = { CalendarService.Scope.CalendarReadonly };
    private static readonly string[] Scopes = { CalendarService.Scope.Calendar };
    private static readonly string CredentialsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CalendarWidget", "credentials.json");
    private static readonly string TokenPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CalendarWidget", "token");

    private CalendarService? _service;

    private static readonly Dictionary<string, string> ColorIdMap = new()
    {
        { "#ac725e", "1" },  // Flamingo
        { "#d06b64", "2" },  // Tomato
        { "#f83a22", "11" }, // Tomato
        { "#fa573c", "3" },  // Tangerine
        { "#ff7537", "6" },  // Mandarin
        { "#ffad46", "5" },  // Banana
        { "#42d692", "10" }, // Sage
        { "#16a765", "8" },  // Basil
        { "#7bd148", "9" },  // Peacock
        { "#b3dc6c", "4" },  // Sage
        { "#4986e7", "7" },  // Blueberry
    };

    private static readonly Dictionary<string, string> ColorIdToHex = new()
    {
        { "1",  "#ac725e" },
        { "2",  "#d06b64" },
        { "11", "#f83a22" },
        { "3",  "#fa573c" },
        { "6",  "#ff7537" },
        { "5",  "#ffad46" },
        { "10", "#42d692" },
        { "8",  "#16a765" },
        { "9",  "#7bd148" },
        { "4",  "#b3dc6c" },
        { "7",  "#4986e7" },
    };

    private string ResolveColor(Google.Apis.Calendar.v3.Data.Event e)
    {
        if (!string.IsNullOrEmpty(e.ColorId) && ColorIdToHex.TryGetValue(e.ColorId, out var hex))
            return hex;
        return "#4986e7";
    }

    public static void Log(string msg)
    {
        try
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "CalendarWidget", "log.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.AppendAllText(logPath, $"{DateTime.Now}: {msg}\n");
        }
        catch { }
    }

    public async Task<bool> AuthenticateAsync()
    {
        Log("AuthenticateAsync iniciado");
        try
        {
            Log($"CredentialsPath: {CredentialsPath}");
            Log($"Credentials existe: {File.Exists(CredentialsPath)}");

            if (!File.Exists(CredentialsPath))
            {
                Log("Credentials não encontrado, retornando false");
                return false;
            }

            Log("Abrindo stream das credenciais");
            using var stream = new FileStream(CredentialsPath, FileMode.Open, FileAccess.Read);

            Log("Chamando GoogleWebAuthorizationBroker");
            var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                GoogleClientSecrets.FromStream(stream).Secrets,
                Scopes,
                "user",
                CancellationToken.None,
                new FileDataStore(TokenPath, true));

            Log("Autenticado com sucesso");
            _service = new CalendarService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "CalendarWidget"
            });

            return true;
        }
        catch (Exception ex)
        {
            Log($"ERRO: {ex.Message}\n{ex.StackTrace}");
            return false;
        }
    }

    public async Task<List<CalendarEvent>> GetEventsForDateAsync(DateOnly date)
    {
        if (_service == null)
        {
            Console.WriteLine("Google Calendar: service é null");
            return new();
        }

        try
        {
            var request = _service.Events.List("primary");
            request.TimeMinDateTimeOffset = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeZoneInfo.Local.GetUtcOffset(date.ToDateTime(TimeOnly.MinValue)));
            request.TimeMaxDateTimeOffset = new DateTimeOffset(date.ToDateTime(TimeOnly.MaxValue), TimeZoneInfo.Local.GetUtcOffset(date.ToDateTime(TimeOnly.MaxValue)));
            request.SingleEvents = true;
            request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;

            var result = await request.ExecuteAsync();
            Console.WriteLine($"Google eventos encontrados pro dia de hoje: {result.Items?.Count ?? 0}");

            foreach (var e in result.Items ?? new List<Google.Apis.Calendar.v3.Data.Event>())
                Console.WriteLine($"  - {e.Summary} | {e.Start?.DateTimeRaw ?? e.Start?.Date}");

            return result.Items?.Select(e => new CalendarEvent
            {
                Id = $"gcal_{e.Id}",
                Date = date.ToString("yyyy-MM-dd"),
                Name = e.Summary ?? "(sem título)",
                Time = e.Start.DateTimeDateTimeOffset.HasValue
                    ? e.Start.DateTimeDateTimeOffset.Value.ToString("HH:mm")
                    : "dia todo",
                // Color = "#60a5fa"
                Color = ResolveColor(e)
            }).ToList() ?? new();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Google Calendar erro: {ex.Message}");
            return new();
        }
    }

    public async Task<Dictionary<string, List<CalendarEvent>>> GetEventsForMonthAsync(int year, int month)
    {
        if (_service == null) return new();

        try
        {
            var firstDay = new DateTime(year, month, 1);
            var lastDay = new DateTime(year, month, DateTime.DaysInMonth(year, month));

            var request = _service.Events.List("primary");
            request.TimeMinDateTimeOffset = new DateTimeOffset(firstDay, TimeZoneInfo.Local.GetUtcOffset(firstDay));
            request.TimeMaxDateTimeOffset = new DateTimeOffset(lastDay.AddDays(1), TimeZoneInfo.Local.GetUtcOffset(lastDay));
            request.SingleEvents = true;
            request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;

            var result = await request.ExecuteAsync();

            var grouped = new Dictionary<string, List<CalendarEvent>>();

            foreach (var e in result.Items ?? new List<Google.Apis.Calendar.v3.Data.Event>())
            {
                string dateKey;
                if (e.Start.DateTimeDateTimeOffset.HasValue)
                    dateKey = e.Start.DateTimeDateTimeOffset.Value.ToString("yyyy-MM-dd");
                else
                    dateKey = e.Start.Date;

                if (!grouped.ContainsKey(dateKey))
                    grouped[dateKey] = new();

                grouped[dateKey].Add(new CalendarEvent
                {
                    Id = $"gcal_{e.Id}",
                    Date = dateKey,
                    Name = e.Summary ?? "(sem título)",
                    Time = e.Start.DateTimeDateTimeOffset.HasValue
                        ? e.Start.DateTimeDateTimeOffset.Value.ToString("HH:mm")
                        : "dia todo",
                    // Color = "#60a5fa"
                    Color = ResolveColor(e)
                });
            }

            return grouped;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Google Calendar erro: {ex.Message}");
            return new();
        }
    }
 
    private static TimeOnly ParseTime(string time)
    {
        // Tenta os formatos mais comuns
        string[] formats = { "HH:mm", "H:mm", "HH'h'mm", "H'h'mm", "0H'h'mm" };
        
        foreach (var format in formats)
            if (TimeOnly.TryParseExact(time, format, out var result))
                return result;

        // Tenta formato "9h" ou "10h" sem minutos
        if (time.EndsWith("h") && int.TryParse(time.TrimEnd('h'), out var hour))
            return new TimeOnly(hour, 0);

        // Fallback pro TryParse genérico
        if (TimeOnly.TryParse(time, out var fallback))
            return fallback;

        return TimeOnly.MinValue;
    }

    public async Task<bool> CreateEventAsync(CalendarEvent evt)
    {
        if (_service == null) return false;

        try
        {
            var googleEvent = new Google.Apis.Calendar.v3.Data.Event
            {
                Summary = evt.Name,
            };

            if (ColorIdMap.TryGetValue(evt.Color, out var colorId))
                googleEvent.ColorId = colorId;

            if (evt.Time != "dia todo" && !string.IsNullOrEmpty(evt.Time))
            {
                var date = DateOnly.ParseExact(evt.Date, "yyyy-MM-dd");
                var time = ParseTime(evt.Time);
                var dt = date.ToDateTime(time);

                googleEvent.Start = new Google.Apis.Calendar.v3.Data.EventDateTime
                {
                    DateTimeDateTimeOffset = new DateTimeOffset(dt, TimeZoneInfo.Local.GetUtcOffset(dt))
                };
                googleEvent.End = new Google.Apis.Calendar.v3.Data.EventDateTime
                {
                    DateTimeDateTimeOffset = new DateTimeOffset(dt.AddHours(1), TimeZoneInfo.Local.GetUtcOffset(dt))
                };
            }
            else
            {
                googleEvent.Start = new Google.Apis.Calendar.v3.Data.EventDateTime { Date = evt.Date };
                googleEvent.End = new Google.Apis.Calendar.v3.Data.EventDateTime { Date = evt.Date };
            }

            await _service.Events.Insert(googleEvent, "primary").ExecuteAsync();
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Google create erro: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> UpdateEventAsync(CalendarEvent evt)
    {
        if (_service == null) return false;

        try
        {
            var googleEvent = await _service.Events.Get("primary", evt.GoogleEventId).ExecuteAsync();

            googleEvent.Summary = evt.Name;

            if (evt.Time != "dia todo")
            {
                var date = DateOnly.ParseExact(evt.Date, "yyyy-MM-dd");
                var time = TimeOnly.ParseExact(evt.Time, "HH:mm");
                var dt = date.ToDateTime(time);
                googleEvent.Start = new Google.Apis.Calendar.v3.Data.EventDateTime
                {
                    DateTimeDateTimeOffset = new DateTimeOffset(dt, TimeZoneInfo.Local.GetUtcOffset(dt))
                };
                googleEvent.End = new Google.Apis.Calendar.v3.Data.EventDateTime
                {
                    DateTimeDateTimeOffset = new DateTimeOffset(dt.AddHours(1), TimeZoneInfo.Local.GetUtcOffset(dt))
                };
            }

            await _service.Events.Update(googleEvent, "primary", evt.GoogleEventId).ExecuteAsync();
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Google update erro: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> DeleteEventAsync(string googleEventId)
    {
        if (_service == null) return false;

        try
        {
            await _service.Events.Delete("primary", googleEventId).ExecuteAsync();
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Google delete erro: {ex.Message}");
            return false;
        }
    }

    public void Logout()
    {
        _service = null;
        try
        {
            if (Directory.Exists(TokenPath))
                Directory.Delete(TokenPath, true);
        }
        catch { }
    }
 
    public bool IsAuthenticated => _service != null;
}