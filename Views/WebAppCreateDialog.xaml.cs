using System.Windows;
using System.Windows.Input;

namespace Horizon.Stealth.Views;

public partial class WebAppCreateDialog : Window
{
    public string AppName => TxtName.Text.Trim();
    public bool CreateDesktopShortcut => ChkDesktop.IsChecked == true;
    public bool CreateStartMenuShortcut => ChkStartMenu.IsChecked == true;
    public bool RegisterInShareMenu => ChkShareMenu.IsChecked == true;

    public WebAppCreateDialog(string defaultName, string url, bool desktopDefault, bool startMenuDefault)
    {
        InitializeComponent();
        TxtName.Text = defaultName;
        TxtUrl.Text = url;
        ChkDesktop.IsChecked = desktopDefault;
        ChkStartMenu.IsChecked = startMenuDefault;
        ChkShareMenu.IsChecked = false;

        Loaded += (_, _) =>
        {
            TxtName.Focus();
            TxtName.SelectAll();
        };
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void BtnClose_Down(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void BtnCreate_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
