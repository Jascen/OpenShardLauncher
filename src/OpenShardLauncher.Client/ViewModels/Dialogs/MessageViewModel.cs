using CommunityToolkit.Mvvm.Input;

namespace OpenShardLauncher.Client.ViewModels.Dialogs;

// Something to read, with a single OK button.
public sealed partial class MessageViewModel(string title, string message, string okText) : DialogViewModelBase<bool>
{
    public override string Title => title;

    public string Message => message;

    public string OkText => okText;

    [RelayCommand]
    private void Ok() => Close(true);
}
