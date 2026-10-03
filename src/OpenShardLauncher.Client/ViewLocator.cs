using Avalonia.Controls;
using Avalonia.Controls.Templates;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OpenShardLauncher.Client;

// Shows a view model with its view, by convention: OpenShardLauncher.Client.ViewModels.Dialogs.ConfirmViewModel is shown
// with OpenShardLauncher.Client.Views.Dialogs.ConfirmView. View models never create views.
public sealed class ViewLocator : IDataTemplate
{
    private const string ViewModelSuffix = "ViewModel";

    public Control Build(object? param)
    {
        var name = ViewNameFor(param!.GetType());
        return Type.GetType(name) is { } type
            ? (Control)Activator.CreateInstance(type)!
            : new TextBlock { Text = $"No view for {param.GetType().FullName}" };
    }

    public bool Match(object? data) => data is ObservableObject && data.GetType().Name.EndsWith(ViewModelSuffix, StringComparison.Ordinal);

    internal static string ViewNameFor(Type viewModel)
    {
        var name = viewModel.FullName!.Replace(".ViewModels.", ".Views.", StringComparison.Ordinal);
        return name[..^ViewModelSuffix.Length] + "View";
    }
}
