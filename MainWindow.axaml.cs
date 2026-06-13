using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia;
using System;
using System.IO;
using System.Text.Json;
using System.Globalization;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace CalendarWidget;

public partial class MainWindow : Window
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CalendarWidget", "settings.json");

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll")]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    // --- API NATIVA DO WINDOWS (USER32.DLL) ---
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, 
        uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private DateTime _currentMonth;
    private readonly EventStore _eventStore = new();
    private readonly GoogleCalendarService _googleCalendar = new();

    public MainWindow()
    {
        GoogleCalendarService.Log("================================");
        GoogleCalendarService.Log("Iniciando Widget...");

        InitializeComponent();
        // Opcional: Remove a barra superior e as bordas para parecer um Widget real
        WindowDecorations = WindowDecorations.None; 
        ShowInTaskbar = false; // Não exibe o ícone na barra de tarefas inferior

        // Evento disparado assim que a janela é desenhada na tela
        Opened += MainWindow_Opened;

        _currentMonth = DateTime.Today;

        Opened += async (s, e) =>
        {
            RoundWindowCorners();
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            LoadPosition();
            _eventStore.Load();
            await _googleCalendar.AuthenticateAsync();
            UpdateGoogleButton();
            await RenderCalendarAsync();
        };

        Closing += (s, e) => SavePosition();

        CloseButton.PointerPressed += (s, e) => Close();        

        AddEventButton.PointerPressed += async (s, e) =>
        {
            var date = DateTime.Today.ToString("yyyy-MM-dd");
            var modal = new AddEventWindow(date);
            await modal.ShowDialog(this);

            if (modal.Result != null)
            {
                if (modal.SaveToGoogle && _googleCalendar.IsAuthenticated)
                    await _googleCalendar.CreateEventAsync(modal.Result);
                else
                    _eventStore.Add(modal.Result);
                _ = RenderCalendarAsync();
            }
        };

        PrevButton.PointerPressed += (s, e) =>
        {
            _currentMonth = _currentMonth.AddMonths(-1);
            _ = RenderCalendarAsync();
        };

        NextButton.PointerPressed += (s, e) =>
        {
            _currentMonth = _currentMonth.AddMonths(1);
            _ = RenderCalendarAsync();
        };

        GoogleButton.PointerPressed += async (s, e) =>
        {
            if (_googleCalendar.IsAuthenticated)
            {
                _googleCalendar.Logout();
                UpdateGoogleButton();
                _ = RenderCalendarAsync();
            }
            else
            {
                await _googleCalendar.AuthenticateAsync();
                UpdateGoogleButton();
                _ = RenderCalendarAsync();
            }
        };
    }

    private void RoundWindowCorners()
    {
        var hwnd = (TryGetPlatformHandle()?.Handle) ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero) return;
        int preference = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(hwnd, 33, ref preference, sizeof(int));
    }

    private void MainWindow_Opened(object? sender, EventArgs e)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        // 1. Obtém o ponteiro (Handle) da janela do Avalonia UI
        var platformHandle = this.TryGetPlatformHandle();
        if (platformHandle == null) return;
        IntPtr windowHandle = platformHandle.Handle;

        // 2. Localiza a janela principal do Gerenciador de Programas do Windows
        IntPtr progman = FindWindow("Progman", null!);

        // 3. Força o Windows a criar a camada de fundo ativa (WorkerW)
        // Envia a mensagem secreta 0x052C. Sem isso, o Windows não cria o espaço correto atrás dos ícones.
        IntPtr result;
        SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0x0000, 1000, out result);

        IntPtr workerW = IntPtr.Zero;

        // 4. Vasculha as janelas do sistema para achar a nova WorkerW que foi criada logo atrás dos ícones
        EnumWindows(new EnumWindowsProc((topHandle, topParam) =>
        {
            // Procura por uma janela WorkerW que tenha uma SHELLDLL_DefView (onde ficam os ícones do desktop)
            IntPtr shellView = FindWindowEx(topHandle, IntPtr.Zero, "SHELLDLL_DefView", null!);

            if (shellView != IntPtr.Zero)
            {
                // A WorkerW correta para colocar nosso app é a que está imediatamente após esta
                workerW = FindWindowEx(IntPtr.Zero, topHandle, "WorkerW", null!);
            }
            return true; // Continua a enumeração
        }), IntPtr.Zero);

        // Caso o método moderno falhe por conta da versão do Windows, pega o Progman padrão
        IntPtr finalDesktopHandle = workerW != IntPtr.Zero ? workerW : progman;

        if (finalDesktopHandle != IntPtr.Zero)
        {
            // 5. Anexa definitivamente a janela do Avalonia ao fundo da Área de Trabalho
            SetParent(windowHandle, finalDesktopHandle);
        }
    }    

    private async Task RenderCalendarAsync()
    {
        ShowLoading();
        try
        {
            var today = DateTime.Today;
            var firstDay = new DateTime(_currentMonth.Year, _currentMonth.Month, 1);
            var daysInMonth = DateTime.DaysInMonth(_currentMonth.Year, _currentMonth.Month);

            var culture = new CultureInfo("pt-BR");
            MonthLabel.Text = _currentMonth.ToString("MMMM yyyy", culture);
            MonthLabel.Text = char.ToUpper(MonthLabel.Text[0]) + MonthLabel.Text[1..];

            int startDay = (int)firstDay.DayOfWeek;

            DaysGrid.Children.Clear();

            // Dias do mês anterior
            var prevMonth = firstDay.AddDays(-1);
            int prevDaysInMonth = DateTime.DaysInMonth(prevMonth.Year, prevMonth.Month);
            for (int i = startDay - 1; i >= 0; i--)
            {
                var day = prevDaysInMonth - i;
                DaysGrid.Children.Add(MakeCell(day.ToString(), "#55FFFFFF", false, false));
            }

            // FASE 1: renderiza com eventos locais apenas
            for (int d = 1; d <= daysInMonth; d++)
            {
                bool isToday = (d == today.Day &&
                                _currentMonth.Month == today.Month &&
                                _currentMonth.Year == today.Year);

                var date = new DateOnly(_currentMonth.Year, _currentMonth.Month, d);
                var localEvents = _eventStore.GetByDate(date);
                var colors = localEvents.Count > 0 ? localEvents.Select(e => e.Color).ToArray() : null;
                DaysGrid.Children.Add(MakeCell(d.ToString(), "#CCFFFFFF", true, isToday, colors, localEvents));
            }

            // Dias do próximo mês
            int totalCells = (startDay + daysInMonth) > 35 ? 42 : 35;
            int filled = startDay + daysInMonth;
            int remaining = totalCells - filled;
            for (int d = 1; d <= remaining; d++)
                DaysGrid.Children.Add(MakeCell(d.ToString(), "#55FFFFFF", false, false));

            // Renderiza eventos de hoje com dados locais imediatamente
            await RenderEventsAsync();

            // FASE 2: busca Google Calendar em background e atualiza as células
            if (_googleCalendar.IsAuthenticated)
            {
                _ = Task.Run(async () =>
                {
                    var googleEventsByMonth = await _googleCalendar.GetEventsForMonthAsync(
                        _currentMonth.Year, _currentMonth.Month);

                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        for (int d = 1; d <= daysInMonth; d++)
                        {
                            var date = new DateOnly(_currentMonth.Year, _currentMonth.Month, d);
                            var dateKey = date.ToString("yyyy-MM-dd");

                            if (!googleEventsByMonth.ContainsKey(dateKey)) continue;

                            var googleEvents = googleEventsByMonth[dateKey];
                            var localEvents = _eventStore.GetByDate(date);
                            var allEvents = localEvents.Concat(googleEvents).OrderBy(e => e.Time).ToList();

                            bool isToday = (d == today.Day &&
                                            _currentMonth.Month == today.Month &&
                                            _currentMonth.Year == today.Year);

                            int cellIndex = startDay + d - 1;
                            if (cellIndex < DaysGrid.Children.Count)
                            {
                                var colors = allEvents.Select(e => e.Color).ToArray();
                                DaysGrid.Children[cellIndex] = MakeCell(d.ToString(), "#CCFFFFFF", true, isToday, colors, allEvents);
                            }
                        }

                        _ = RenderEventsAsync();
                    });
                });
            }   
        }
        finally
        {
            HideLoading();
        }        
    }

    private Border MakeCell(string day, string foreground, bool isCurrentMonth, bool isToday, string[]? eventColors = null, List<CalendarEvent>? events = null)
    {
        var innerStack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 3
        };

        var textBlock = new TextBlock
        {
            Text = day,
            FontSize = 14,
            FontWeight = Avalonia.Media.FontWeight.Bold,
            Foreground = SolidColorBrush.Parse(isToday ? "#a8d4ff" : foreground),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        innerStack.Children.Add(textBlock);

        if (eventColors != null && eventColors.Length > 0)
        {
            var dotsRow = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 2
            };

            foreach (var color in eventColors)
            {
                dotsRow.Children.Add(new Avalonia.Controls.Shapes.Ellipse
                {
                    Width = 4,
                    Height = 4,
                    Fill = SolidColorBrush.Parse(color)
                });
            }

            innerStack.Children.Add(dotsRow);
        }

        var border = new Border
        {
            Margin = new Thickness(1),
            Height = 36,
            Child = innerStack,
            Background = Brushes.Transparent,
            BoxShadow = BoxShadows.Parse("0 2 6 1 #3F000000")
        };

        if (isToday)
        {
            border.Background = SolidColorBrush.Parse("#33a8d4ff");
            border.BorderBrush = SolidColorBrush.Parse("#66a8d4ff");
            border.BorderThickness = new Thickness(1);
            border.CornerRadius = new CornerRadius(8);
        }

        if (eventColors != null && eventColors.Length > 0)
        {
            var tooltipStack = new StackPanel { Spacing = 4 };

            tooltipStack.Children.Add(new TextBlock
            {
                Text = $"{day} de {_currentMonth.ToString("MMMM", new CultureInfo("pt-BR"))}",
                FontSize = 12,
                Foreground = SolidColorBrush.Parse("#77FFFFFF"),
                FontWeight = Avalonia.Media.FontWeight.Medium
            });

            tooltipStack.Children.Add(new Border
            {
                Height = 1,
                Background = SolidColorBrush.Parse("#22FFFFFF")
            });

            foreach (var evt in events ?? new())
            {
                var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
                row.Children.Add(new Avalonia.Controls.Shapes.Ellipse
                {
                    Width = 6,
                    Height = 6,
                    Fill = SolidColorBrush.Parse(evt.Color),
                    VerticalAlignment = VerticalAlignment.Center
                });
                row.Children.Add(new TextBlock
                {
                    Text = $"{evt.Name} - {evt.Time}",
                    FontSize = 12,
                    Foreground = SolidColorBrush.Parse("#EEFFFFFF")
                });
                tooltipStack.Children.Add(row);
            }

            ToolTip.SetTip(border, tooltipStack);
        }

        if (isCurrentMonth)
        {
            var dateStr = new DateOnly(_currentMonth.Year, _currentMonth.Month, int.Parse(day))
                .ToString("yyyy-MM-dd");

            border.DoubleTapped += async (s, e) =>
            {
                var modal = new AddEventWindow(dateStr);
                await modal.ShowDialog(this);

                if (modal.Result != null)
                {
                    if (modal.SaveToGoogle && _googleCalendar.IsAuthenticated)
                        await _googleCalendar.CreateEventAsync(modal.Result);
                    else
                        _eventStore.Add(modal.Result);
                    _ = RenderCalendarAsync();
                }
            };

            if (events != null && events.Count > 0)
            {
                var contextMenu = new ContextMenu();

                foreach (var evt in events)
                {
                    var capturedEvt = evt;

                    contextMenu.Items.Add(new MenuItem
                    {
                        Header = $"{evt.Name} - {evt.Time}",
                        IsEnabled = false,
                        Foreground = SolidColorBrush.Parse("#77FFFFFF"), 
                        Margin = new Thickness(0, -3, 0, -10)
                    });
                    contextMenu.Items.Add(new Separator());

                    var editBtn = new Image
                    {
                        Source = new Avalonia.Media.Imaging.Bitmap(
                            Avalonia.Platform.AssetLoader.Open(new Uri("avares://CalendarWidget/Assets/pencil.png"))),
                        Width = 12,
                        Height = 12,
                    };

                    var deleteBtn = new Image
                    {
                        Source = new Avalonia.Media.Imaging.Bitmap(
                            Avalonia.Platform.AssetLoader.Open(new Uri("avares://CalendarWidget/Assets/trash.png"))),
                        Width = 12,
                        Height = 12,
                    };

                    var editItem = new MenuItem { Header = "Editar", Icon = editBtn, FontSize = 12 };

                    editItem.Click += async (s, e) =>
                    {
                        var modal = new AddEventWindow(capturedEvt);
                        await modal.ShowDialog(this);
                        if (modal.Result != null)
                        {
                            if (capturedEvt.IsGoogleEvent)
                                await _googleCalendar.UpdateEventAsync(modal.Result);
                            else
                                _eventStore.Update(modal.Result);
                            _ = RenderCalendarAsync();
                        }
                    };

                    var deleteItem = new MenuItem { Header = "Excluir", Icon = deleteBtn, FontSize = 12 };

                    deleteItem.Click += async (s, e) =>
                    {
                        var confirm = new ConfirmDeleteWindow(capturedEvt.Name);
                        await confirm.ShowDialog(this);
                        if (confirm.Confirmed)
                        {
                            if (capturedEvt.IsGoogleEvent)
                                await _googleCalendar.DeleteEventAsync(capturedEvt.GoogleEventId);
                            else
                                _eventStore.Remove(capturedEvt.Id);
                            _ = RenderCalendarAsync();
                        }
                    };

                    contextMenu.Items.Add(editItem);
                    contextMenu.Items.Add(deleteItem);

                    if (evt.Id != events.Last().Id)
                        contextMenu.Items.Add(new Separator());
                }

                border.ContextMenu = contextMenu;
            }
        }

        return border;
    }

    private async Task RenderEventsAsync()
    {
        var today = DateTime.Today;
        var events = _eventStore.GetByDate(DateOnly.FromDateTime(today));

        if (_googleCalendar.IsAuthenticated)
        {
            var googleEvents = await _googleCalendar.GetEventsForDateAsync(DateOnly.FromDateTime(today));
            events = events.Concat(googleEvents).OrderBy(e => e.Time).ToList();
        }

        TodayLabel.Text = $"HOJE - {today.Day} {today.ToString("MMM", new CultureInfo("pt-BR")).ToUpper()}";

        EventsList.Children.Clear();

        if (events.Count == 0)
        {
            EventsList.Children.Add(new TextBlock
            {
                Text = "Nenhum evento hoje.",
                FontSize = 13,
                Foreground = SolidColorBrush.Parse("#55FFFFFF"),
                Margin = new Thickness(4, 4, 0, 0)
            });
            return;
        }

        foreach (var evt in events)
        {
            var card = new Border
            {
                Width = 250,
                Background = SolidColorBrush.Parse("#0AFFFFFF"),
                BorderBrush = SolidColorBrush.Parse("#15FFFFFF"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(0),
            };
            
            ToolTip.SetTip(card, $"{evt.Name} — {evt.Time}");

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });

            var accent = new Border
            {
                Background = SolidColorBrush.Parse(evt.Color),
                CornerRadius = new CornerRadius(10, 0, 0, 10)
            };
            Grid.SetColumn(accent, 0);

            var content = new StackPanel { Margin = new Thickness(10, 8, 10, 8) };

            content.Children.Add(new TextBlock
            {
                Text = evt.Name,
                FontSize = 14,
                Foreground = SolidColorBrush.Parse("#EEFFFFFF"),
                FontWeight = Avalonia.Media.FontWeight.Medium
            });

            var bottomRow = new Grid();
            bottomRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
            bottomRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var timeLabel = new TextBlock
            {
                Text = evt.Time,
                FontSize = 12,
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
                Foreground = SolidColorBrush.Parse("#AAFFFFFF"),
                Margin = new Thickness(0, 2, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(timeLabel, 0);

            var icons = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 2, 0, 0)
            };

            var editBorder = new Border
            {
                Background = SolidColorBrush.Parse("#14FFFFFF"),
                BorderBrush = SolidColorBrush.Parse("#33FFFFFF"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(5),
                ZIndex = 10,
                Cursor = new Cursor(StandardCursorType.Hand)
            };

            var editBtn = new Image
            {
                Source = new Avalonia.Media.Imaging.Bitmap(
                    Avalonia.Platform.AssetLoader.Open(new Uri("avares://CalendarWidget/Assets/pencil.png"))),
                Width = 14,
                Height = 14,
                ZIndex = 10,
            };
            editBorder.Child = editBtn;
            var deleteBorder = new Border
            {
                Background = SolidColorBrush.Parse("#14FFFFFF"),
                BorderBrush = SolidColorBrush.Parse("#33FFFFFF"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(5),
                Cursor = new Cursor(StandardCursorType.Hand)
            };

            var deleteBtn = new Image
            {
                Source = new Avalonia.Media.Imaging.Bitmap(
                    Avalonia.Platform.AssetLoader.Open(new Uri("avares://CalendarWidget/Assets/trash.png"))),
                Width = 14,
                Height = 14,
            };
            deleteBorder.Child = deleteBtn;

            var capturedEvt = evt;

            editBorder.PointerPressed += async (s, e) =>
            {
                var modal = new AddEventWindow(capturedEvt);
                await modal.ShowDialog(this);
                if (modal.Result != null)
                {
                    if (capturedEvt.IsGoogleEvent)
                        await _googleCalendar.UpdateEventAsync(modal.Result);
                    else
                        _eventStore.Update(modal.Result);
                    _ = RenderCalendarAsync();
                }
            };

            deleteBorder.PointerPressed += async (s, e) =>
            {
                var confirm = new ConfirmDeleteWindow(capturedEvt.Name);
                await confirm.ShowDialog(this);
                if (confirm.Confirmed)
                {
                    if (capturedEvt.IsGoogleEvent)
                        await _googleCalendar.DeleteEventAsync(capturedEvt.GoogleEventId);
                    else
                        _eventStore.Remove(capturedEvt.Id);
                    _ = RenderCalendarAsync();
                }
            };
            
            ToolTip.SetTip(editBorder, "Editar evento");
            ToolTip.SetTip(deleteBorder, "Excluir evento");
            icons.Children.Add(editBorder);
            icons.Children.Add(deleteBorder);
            Grid.SetColumn(icons, 1);

            bottomRow.Children.Add(timeLabel);
            bottomRow.Children.Add(icons);
            content.Children.Add(bottomRow);

            Grid.SetColumn(content, 1);
            grid.Children.Add(accent);
            grid.Children.Add(content);
            card.Child = grid;

            EventsList.Children.Add(card);
        }
    }

    private void SavePosition()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var json = JsonSerializer.Serialize(new { X = Position.X, Y = Position.Y });
            File.WriteAllText(SettingsPath, json);
        }
        catch { }
    }

    private void LoadPosition()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var json = File.ReadAllText(SettingsPath);
            var doc = JsonDocument.Parse(json);
            var x = doc.RootElement.GetProperty("X").GetInt32();
            var y = doc.RootElement.GetProperty("Y").GetInt32();
            Position = new PixelPoint(x, y);
        }
        catch { }
    }

    private void UpdateGoogleButton()
    {
        var iconPath = _googleCalendar.IsAuthenticated
            ? "avares://CalendarWidget/Assets/logout-2.png"
            : "avares://CalendarWidget/Assets/google.png";

        GoogleIcon.Source = new Avalonia.Media.Imaging.Bitmap(
            Avalonia.Platform.AssetLoader.Open(new Uri(iconPath)));

        ToolTip.SetTip(GoogleButton, _googleCalendar.IsAuthenticated
            ? "Desconectar Google Calendar"
            : "Conectar Google Calendar");
    }

    private void ShowLoading()
    {
        LoadingOverlay.IsVisible = true;
    }

    private void HideLoading()
    {
        LoadingOverlay.IsVisible = false;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }
}