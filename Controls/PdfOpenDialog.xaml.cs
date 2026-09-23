using System.Windows;

namespace Horizon.Stealth.Controls;

public partial class PdfOpenDialog : Window
{
    public bool OpenExternal { get; private set; }
    public bool Remember     { get; private set; }

    public PdfOpenDialog()
    {
        InitializeComponent();
    }

    private void BtnExternal_Click(object sender, RoutedEventArgs e)
    {
        OpenExternal = true;
        Remember     = ChkRemember.IsChecked == true;
        DialogResult = true;
    }

    private void BtnBrowser_Click(object sender, RoutedEventArgs e)
    {
        OpenExternal = false;
        Remember     = ChkRemember.IsChecked == true;
        DialogResult = true;
    }
}