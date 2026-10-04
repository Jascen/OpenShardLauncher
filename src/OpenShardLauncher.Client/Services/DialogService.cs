using CommunityToolkit.Mvvm.ComponentModel;
using OpenShardLauncher.Client.ViewModels.Dialogs;

namespace OpenShardLauncher.Client.Services;

// Keeps the open dialogs as a stack; the overlay shows the top one. No Avalonia here: the overlay binds to Current,
// so view model tests drive dialogs through the same service the app uses.
public sealed class DialogService : ObservableObject, IDialogService
{
    private readonly List<DialogViewModelBase> _open = [];

    public DialogViewModelBase? Current => _open.Count > 0 ? _open[^1] : null;

    public bool IsOpen => _open.Count > 0;

    public async Task<TResult> ShowAsync<TResult>(DialogViewModelBase<TResult> dialog)
    {
        var closed = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnCloseRequested(object? sender, TResult result) => closed.TrySetResult(result);

        dialog.CloseRequested += OnCloseRequested;
        SetOpen(() => _open.Add(dialog));
        try
        {
            return await closed.Task;
        }
        finally
        {
            dialog.CloseRequested -= OnCloseRequested;
            SetOpen(() => _open.Remove(dialog));
        }
    }

    private void SetOpen(Action change)
    {
        change();
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(IsOpen));
    }
}
