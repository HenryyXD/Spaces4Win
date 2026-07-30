using System.Windows;
using Spaces4Win.Config;

namespace Spaces4Win;

public enum ElevationPromptResult
{
    Cancel,
    RestartNow,
    AlwaysElevate,
    NeverAsk
}

public partial class ElevationPromptWindow : Window
{
    public ElevationPromptResult Result { get; private set; } = ElevationPromptResult.Cancel;

    public ElevationPromptWindow()
    {
        InitializeComponent();
    }

    public static ElevationPromptResult ShowPrompt(Window? owner = null)
    {
        var dialog = new ElevationPromptWindow
        {
            Owner = owner
        };

        dialog.ShowDialog();
        return dialog.Result;
    }

    private void Restart_Click(object sender, RoutedEventArgs e)
    {
        Result = ElevationPromptResult.RestartNow;
        DialogResult = true;
    }

    private void Always_Click(object sender, RoutedEventArgs e)
    {
        Result = ElevationPromptResult.AlwaysElevate;
        DialogResult = true;
    }

    private void Never_Click(object sender, RoutedEventArgs e)
    {
        Result = ElevationPromptResult.NeverAsk;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Result = ElevationPromptResult.Cancel;
        DialogResult = false;
    }
}
