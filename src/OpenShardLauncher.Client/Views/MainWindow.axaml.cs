using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace OpenShardLauncher.Client.Views;

// Window chrome only: the window has no system title bar, so it is dragged from anywhere that isn't a control and has
// its own minimize and close buttons. Everything else is bound to MainWindowViewModel.
public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void Minimize_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
