using Avalonia.Controls;
using Avalonia.Input;

namespace CalendarWidget;

public partial class ConfirmDeleteWindow : Window
{
    public bool Confirmed { get; private set; }

    public ConfirmDeleteWindow() : this("") { }

    public ConfirmDeleteWindow(string eventName)
    {
        InitializeComponent();
        EventNameLabel.Text = $"Deseja excluir \"{eventName}\"?";
        NoButton.PointerPressed += (s, e) => Close();
        YesButton.PointerPressed += (s, e) => { Confirmed = true; Close(); };
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }
}