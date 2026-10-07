using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Horizon.Stealth.Controls;
using Horizon.Stealth.Services;

namespace Horizon.Stealth.Views;

public partial class StackElementWizardWindow : Window
{
    public sealed class SourceRow
    {
        public string Id { get; init; } = "";
        public string Icon { get; init; } = "";
        public string Name { get; init; } = "";
    }

    private static readonly int[] RefreshChoices = { 0, 1, 5, 15, 30, 60 };

    private readonly StackElementData? _editing;
    private readonly int _minStep;
    private int _step;
    private StackElementKind _kind = StackElementKind.Site;
    private string _sourceId = "";
    private string _preparedKey = "";
    private StackElementData? _draft;

    public StackElementData? Result { get; private set; }

    public StackElementWizardWindow(StackElementData? editing = null)
    {
        InitializeComponent();
        _editing = editing;

        foreach (int minutes in RefreshChoices)
            CmbRefresh.Items.Add(minutes == 0 ? "Off" : minutes + " min");
        CmbRefresh.SelectedIndex = 0;

        if (editing == null)
        {
            _minStep = 1;
            UpdateCardSelection();
            ShowStep(1);
            return;
        }

        Title = "Edit Element";
        TxtHeader.Text = "EDIT ELEMENT //";
        _kind = editing.Kind;
        _sourceId = editing.SourceId;
        _minStep = _kind == StackElementKind.Site ? 2 : 3;
        _preparedKey = BuildKey();

        TxtTitle.Text = editing.Title;
        TxtIcon.Text = editing.Icon;
        if (_kind == StackElementKind.Site) TxtSiteUrl.Text = editing.Url;
        if (_kind == StackElementKind.Template) TxtTemplateUrl.Text = editing.Url;
        int refreshIndex = Array.IndexOf(RefreshChoices, editing.RefreshMinutes);
        CmbRefresh.SelectedIndex = refreshIndex >= 0 ? refreshIndex : 0;
        RbIsolated.IsChecked = editing.LoginProfile == StackLoginProfile.Isolated;
        RbShared.IsChecked = editing.LoginProfile != StackLoginProfile.Isolated;
        TxtUserCss.Text = editing.UserCss;

        ConfigureStep3();
        ShowStep(3);
    }

    private string _offeredKey = "";

    private void OfferTemplate()
    {
        if (_kind != StackElementKind.Site || _editing != null) return;
        var entry = StackElementCatalog.DetectTemplate(TxtSiteUrl.Text);
        if (entry == null) return;

        string key = entry.Id + "|" + TxtSiteUrl.Text;
        if (key == _offeredKey) return;
        _offeredKey = key;

        var answer = MessageBox.Show(this, $"This looks like {entry.Name}. Optimize it as a {entry.Name} template?", "Optimize site", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        _kind = StackElementKind.Template;
        _sourceId = entry.Id;
        TxtTemplateUrl.Text = TxtSiteUrl.Text;
        UpdateCardSelection();
    }

    private string BuildKey() => _kind + "|" + _sourceId;

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            DialogResult = false;
        }
    }

    private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement card || card.Tag is not string tag) return;
        if (!Enum.TryParse(tag, out StackElementKind kind)) return;
        if (kind != _kind) _sourceId = "";
        _kind = kind;
        UpdateCardSelection();
    }

    private void UpdateCardSelection()
    {
        SetCardSelected(CardWidget, _kind == StackElementKind.Widget);
        SetCardSelected(CardSite, _kind == StackElementKind.Site);
        SetCardSelected(CardTemplate, _kind == StackElementKind.Template);
    }

    private void SetCardSelected(Border card, bool selected)
    {
        card.SetResourceReference(Border.BorderBrushProperty, selected ? "Brush_Accent" : "Brush_ButtonBorder");
        card.BorderThickness = new Thickness(selected ? 2 : 1);
    }

    private void ConfigureStep2()
    {
        bool site = _kind == StackElementKind.Site;
        PnlSiteUrl.Visibility = site ? Visibility.Visible : Visibility.Collapsed;
        LstSource.Visibility = site ? Visibility.Collapsed : Visibility.Visible;
        TxtStep2Title.Text = _kind switch
        {
            StackElementKind.Widget => "Choose a widget",
            StackElementKind.Template => "Choose a template",
            _ => "Enter the web address"
        };

        if (site) return;
        var rows = StackElementCatalog.For(_kind)
            .Select(e => new SourceRow { Id = e.Id, Icon = e.Icon, Name = e.Name })
            .ToList();
        LstSource.ItemsSource = rows;
        LstSource.SelectedItem = rows.FirstOrDefault(r => r.Id == _sourceId);
    }

    private bool CollectStep2()
    {
        if (_kind == StackElementKind.Site)
        {
            var probe = new StackElementData { Kind = StackElementKind.Site, Url = TxtSiteUrl.Text, Title = "x" };
            if (!StackElementStore.Validate(probe, out string error))
            {
                ShowError(error);
                return false;
            }
            TxtSiteUrl.Text = probe.Url;
            _sourceId = "";
            return true;
        }

        if (LstSource.SelectedItem is not SourceRow row)
        {
            ShowError("Choose an item from the list.");
            return false;
        }
        _sourceId = row.Id;
        return true;
    }

    private void ConfigureStep3()
    {
        string key = BuildKey();
        if (key != _preparedKey)
        {
            _preparedKey = key;
            var entry = StackElementCatalog.Find(_kind, _sourceId);
            TxtTitle.Text = entry?.Name ?? "";
            TxtIcon.Text = entry?.Icon ?? "\U0001F310";
            CmbRefresh.SelectedIndex = 0;
            RbShared.IsChecked = true;
            TxtUserCss.Text = "";
            if (_kind != StackElementKind.Template) TxtTemplateUrl.Text = "";
        }
        else if (string.IsNullOrWhiteSpace(TxtIcon.Text))
        {
            TxtIcon.Text = StackElementCatalog.Find(_kind, _sourceId)?.Icon ?? "\U0001F310";
        }

        PnlTemplateUrl.Visibility = _kind == StackElementKind.Template ? Visibility.Visible : Visibility.Collapsed;
        PnlWebOptions.Visibility = _kind == StackElementKind.Widget ? Visibility.Collapsed : Visibility.Visible;
        TxtTitleHint.Visibility = _kind == StackElementKind.Site ? Visibility.Visible : Visibility.Collapsed;
    }

    private StackElementData BuildDraft()
    {
        string url = _kind == StackElementKind.Site
            ? TxtSiteUrl.Text
            : _kind == StackElementKind.Template ? TxtTemplateUrl.Text : "";

        return new StackElementData
        {
            Id = _editing?.Id ?? Guid.NewGuid().ToString("N"),
            Kind = _kind,
            Title = TxtTitle.Text,
            Icon = TxtIcon.Text,
            SourceId = _sourceId,
            Url = url,
            RefreshMinutes = CmbRefresh.SelectedIndex >= 0 ? RefreshChoices[CmbRefresh.SelectedIndex] : 0,
            LoginProfile = RbIsolated.IsChecked == true ? StackLoginProfile.Isolated : StackLoginProfile.Shared,
            UserCss = TxtUserCss.Text
        };
    }

    private bool CollectStep3()
    {
        var draft = BuildDraft();
        if (!StackElementStore.Validate(draft, out string error))
        {
            ShowError(error);
            return false;
        }
        _draft = draft;
        return true;
    }

    private void ConfigureStep4()
    {
        if (_draft == null) return;
        PnlPreview.Child = StackElementBuilders.BuildPlaceholder(_draft, "Brush_Text");

        var lines = new List<string> { "Type: " + _kind };
        if (_kind != StackElementKind.Widget)
        {
            lines.Add("Address: " + _draft.Url);
            lines.Add("Refresh: " + (_draft.RefreshMinutes == 0 ? "Off" : _draft.RefreshMinutes + " min"));
            lines.Add("Login: " + (_draft.LoginProfile == StackLoginProfile.Shared ? "Shared with the main browser" : "Separate profile"));
            if (!string.IsNullOrWhiteSpace(_draft.UserCss)) lines.Add("Custom CSS: " + _draft.UserCss.Trim().Length + " characters");
        }
        TxtSummary.Text = string.Join("\n", lines);
    }

    private void ShowStep(int step)
    {
        _step = step;
        PnlStep1.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        PnlStep2.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        PnlStep3.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        PnlStep4.Visibility = step == 4 ? Visibility.Visible : Visibility.Collapsed;
        TxtSteps.Text = $"STEP {step} / 4";
        BtnBack.IsEnabled = step > _minStep;
        BtnNext.Content = step == 4 ? (_editing == null ? "Add" : "Save") : "Next";
        HideError();
    }

    private void ShowError(string message)
    {
        TxtError.Text = message;
        TxtError.Visibility = Visibility.Visible;
    }

    private void HideError()
    {
        TxtError.Text = "";
        TxtError.Visibility = Visibility.Collapsed;
    }

    private void BtnNext_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            switch (_step)
            {
                case 1:
                    ConfigureStep2();
                    ShowStep(2);
                    break;
                case 2:
                    if (!CollectStep2()) return;
                    OfferTemplate();
                    ConfigureStep3();
                    ShowStep(3);
                    break;
                case 3:
                    if (!CollectStep3()) return;
                    ConfigureStep4();
                    ShowStep(4);
                    break;
                default:
                    Result = _draft;
                    DialogResult = true;
                    break;
            }
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "StackElementWizardWindow.Next");
            ShowError("Unexpected error. See log.");
        }
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        if (_step <= _minStep) return;
        int target = _step - 1;
        if (target == 2) ConfigureStep2();
        if (target == 3) ConfigureStep3();
        ShowStep(target);
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
