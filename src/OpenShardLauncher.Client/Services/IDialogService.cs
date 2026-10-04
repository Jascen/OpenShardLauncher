using System.ComponentModel;
using OpenShardLauncher.Client.ViewModels.Dialogs;

namespace OpenShardLauncher.Client.Services;

// Shows dialogs in the main window's overlay (Views/Dialogs/DialogHost), which dims the launcher behind them. View
// models ask for a dialog and await its result; they never create views.
public interface IDialogService : INotifyPropertyChanged
{
    // The dialog on top, shown by the overlay; null when none is open.
    DialogViewModelBase? Current { get; }

    bool IsOpen { get; }

    Task<TResult> ShowAsync<TResult>(DialogViewModelBase<TResult> dialog);
}
