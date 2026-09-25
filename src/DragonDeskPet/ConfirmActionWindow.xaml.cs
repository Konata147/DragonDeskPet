using System.Windows;

namespace DragonDeskPet;

public partial class ConfirmActionWindow : Window
{
    public ConfirmActionWindow(string title, string message, string confirmLabel)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmLabel;
        Loaded += (_, _) => CancelButton.Focus();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
