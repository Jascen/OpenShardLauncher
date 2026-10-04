using CommunityToolkit.Mvvm.Input;

namespace OpenShardLauncher.Client.ViewModels.Dialogs;

// A yes/no question. True when the player confirms.
public sealed partial class ConfirmViewModel(string title, string message, string confirmText, string cancelText)
    : DialogViewModelBase<bool>
{
    public override string Title => title;

    public string Message => message;

    public string ConfirmText => confirmText;

    public string CancelText => cancelText;

    [RelayCommand]
    private void Confirm() => Close(true);

    [RelayCommand]
    private void Cancel() => Close(false);
}
