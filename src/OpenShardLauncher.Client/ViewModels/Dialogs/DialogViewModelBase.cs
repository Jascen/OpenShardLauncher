using CommunityToolkit.Mvvm.ComponentModel;

namespace OpenShardLauncher.Client.ViewModels.Dialogs;

// A dialog shown by IDialogService in the window's overlay. The dialog host binds to this type; the view is found by
// ViewLocator (FooViewModel → Views.Dialogs.FooView).
public abstract class DialogViewModelBase : ObservableObject
{
    public abstract string Title { get; }
}

// A dialog that ends with a result: it raises CloseRequested and IDialogService.ShowAsync returns the result.
public abstract class DialogViewModelBase<TResult> : DialogViewModelBase
{
    public event EventHandler<TResult>? CloseRequested;

    protected void Close(TResult result) => CloseRequested?.Invoke(this, result);
}
