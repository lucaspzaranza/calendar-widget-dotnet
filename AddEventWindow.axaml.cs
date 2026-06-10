using System;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace CalendarWidget;

public partial class AddEventWindow : Window
{
    public CalendarEvent? Result { get; private set; }

    private string _selectedColor = "#60a5fa";
    private Border? _selectedColorBorder;

    private readonly string _date;
    private string? _editingId;

    public bool SaveToGoogle { get; private set; } = false;

    public AddEventWindow() : this(DateTime.Today.ToString("yyyy-MM-dd"))
    {
    }

    public AddEventWindow(string date)
    {
        InitializeComponent();
        _date = date;
        TitleLabel.Text = $"Novo Evento - {FormatDate(_date)}";
        CloseButton.PointerPressed += (s, e) => Close();

        SelectColor(Color1);

        ToggleLocal.PointerPressed += (s, e) => SelectDestination(false);
        ToggleGoogle.PointerPressed += (s, e) => SelectDestination(true);

        var colorBorders = new[] { Color1, Color2, Color3, Color4, Color5, Color6, Color7, Color8, Color9, Color10, Color11 };
        foreach (var c in colorBorders)
            c.PointerPressed += (s, e) => SelectColor((Border)s!);

        SelectColor(Color11); // azul padrão

        SaveButton.PointerPressed += (s, e) => TrySave();
        KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter)
                TrySave();
        };
    }

    public AddEventWindow(CalendarEvent existing) : this(existing.Date)
    {
        NameInput.Text = existing.Name;
        TimeInput.Text = existing.Time;
        TitleLabel.Text = $"Editar Evento - {FormatDate(existing.Date)}";
        SaveLabel.Text = "Editar Evento";
        _editingId = existing.Id;

        var colorBorders = new[] { Color1, Color2, Color3, Color4, Color5, Color6, Color7, Color8, Color9, Color10, Color11 };
        var match = colorBorders.FirstOrDefault(c => c.Tag?.ToString() == existing.Color) ?? Color11;
        SelectColor(match);

        var parsedDate = DateOnly.ParseExact(existing.Date, "yyyy-MM-dd");
        var dateStr = parsedDate.ToString("dd/MM", new CultureInfo("pt-BR"));
        TitleLabel.Text = $"Editar Evento · {dateStr}";
        SaveLabel.Text = "Editar Evento";
    }

    private void SelectColor(Border border)
    {
        if (_selectedColorBorder != null)
            _selectedColorBorder.BorderThickness = new Avalonia.Thickness(0);

        _selectedColor = border.Tag?.ToString() ?? "#60a5fa";
        _selectedColorBorder = border;
        border.BorderThickness = new Avalonia.Thickness(2);
        border.BorderBrush = Brushes.White;
    }

    private string FormatDate(string date)
    {
        var parsed = DateOnly.ParseExact(date, "yyyy-MM-dd");
        return parsed.ToString("dd/MM", new CultureInfo("pt-BR"));
    }

    private void TrySave()
    {
        if (string.IsNullOrWhiteSpace(NameInput.Text)) return;

        Result = new CalendarEvent
        {
            Id = _editingId ?? Guid.NewGuid().ToString(),
            Date = _date,
            Name = NameInput.Text.Trim(),
            Time = TimeInput.Text?.Trim() ?? "",
            Color = _selectedColor
        };

        Close();
    }    

    private void SelectDestination(bool google)
    {
        SaveToGoogle = google;

        ToggleLocal.Background = google ? SolidColorBrush.Parse("#14FFFFFF") : SolidColorBrush.Parse("#33a8d4ff");
        ToggleLocal.BorderBrush = google ? SolidColorBrush.Parse("#33FFFFFF") : SolidColorBrush.Parse("#66a8d4ff");
        ToggleLocalText.Foreground = google ? SolidColorBrush.Parse("#AAFFFFFF") : SolidColorBrush.Parse("#a8d4ff");

        ToggleGoogle.Background = google ? SolidColorBrush.Parse("#33a8d4ff") : SolidColorBrush.Parse("#14FFFFFF");
        ToggleGoogle.BorderBrush = google ? SolidColorBrush.Parse("#66a8d4ff") : SolidColorBrush.Parse("#33FFFFFF");
        ToggleGoogleText.Foreground = google ? SolidColorBrush.Parse("#a8d4ff") : SolidColorBrush.Parse("#AAFFFFFF");
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }
}