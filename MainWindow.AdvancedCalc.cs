using Horizon.Stealth.Views;

namespace Horizon.Stealth;

public partial class MainWindow
{
    private void OpenCalculatorWindow()
    {
        var win = new AdvancedCalculatorWindow(this, w => MinimizeWidgetToDock(w, "Calculator"));
        win.Show();
    }
}