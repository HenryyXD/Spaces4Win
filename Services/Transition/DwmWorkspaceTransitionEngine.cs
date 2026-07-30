using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Spaces4Win.Config;

namespace Spaces4Win.Services.Transition;

/// <summary>WPF + DWM thumbnail engine for workspace slide/fade transitions.</summary>
public sealed class DwmWorkspaceTransitionEngine : IWorkspaceTransitionEngine
{
    public async Task PlayAsync(TransitionPlayContext context, CancellationToken cancellationToken)
    {
        var dispatcher = Application.Current?.Dispatcher
                         ?? throw new InvalidOperationException("No WPF dispatcher");

        if (!dispatcher.CheckAccess())
        {
            await dispatcher.InvokeAsync(
                () => PlayAsync(context, cancellationToken),
                DispatcherPriority.Normal).Task.Unwrap().ConfigureAwait(true);
            return;
        }

        TransitionOverlayWindow? overlay = null;
        try
        {
            overlay = new TransitionOverlayWindow();
            overlay.Prepare(context);
            overlay.Show();
            overlay.ActivateOverlayNoFocus();

            await dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            await Task.Delay(16, cancellationToken).ConfigureAwait(true);

            // Incoming windows become visible only under the opaque overlay.
            context.PrepareIncomingUnderCover?.Invoke();

            overlay.RegisterThumbnails();
            overlay.SyncThumbnails();

            context.ApplySwitch();

            await overlay.AnimateAsync(context.Options, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            try
            {
                overlay?.Cleanup();
                overlay?.Close();
            }
            catch
            {
                // ignore cleanup failures
            }
        }
    }
}
