using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace RadiumLauncher.Views;

public partial class ConfirmationWindow : Window
{
    public ConfirmationWindow()
    {
        InitializeComponent();
    }

    public ConfirmationWindow(string title, string message, string primaryButtonText, string secondaryButtonText)
        : this()
    {
        this.GetControl<TextBlock>("WindowTitle").Text = title;
        this.GetControl<TextBlock>("MessageText").Text = message;
        this.GetControl<Button>("PrimaryButton").Content = primaryButtonText;
        this.GetControl<Button>("SecondaryButton").Content = secondaryButtonText;
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void PrimaryButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void SecondaryButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
