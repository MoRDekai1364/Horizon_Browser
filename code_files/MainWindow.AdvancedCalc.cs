// ═══════════════════════════════════════════════════════════════════════════════
//  MainWindow.AdvancedCalc.cs  —  Horizon Browser
//  Drop alongside MainWindow.xaml.cs.  No .csproj edits needed.
//
//  Replaces / supersedes OpenCalculatorWindow() from MainWindow.Widgets.cs.
//  The partial class is already declared there; just rename the method here
//  so it wins:  OpenCalculatorWindow() calls OpenAdvancedCalculatorWindow().
// ═══════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Horizon.Stealth.Services;

namespace Horizon.Stealth;

public partial class MainWindow
{
    // Called from HeaderWidget_LeftClick / Widgets.cs
    private void OpenCalculatorWindow() => OpenAdvancedCalculatorWindow();

    // ── Palette ───────────────────────────────────────────────────────────────
    private static readonly Color C_Bg      = Color.FromRgb(0x12, 0x12, 0x12);
    private static readonly Color C_Surface = Color.FromRgb(0x1C, 0x1C, 0x1C);
    private static readonly Color C_Num     = Color.FromRgb(0x28, 0x28, 0x28);
    private static readonly Color C_Op      = Color.FromRgb(0x1A, 0x34, 0x54);
    private static readonly Color C_Fn      = Color.FromRgb(0x1A, 0x2A, 0x1A);
    private static readonly Color C_Eq      = Color.FromRgb(0x0A, 0x44, 0x18);
    private static readonly Color C_Cls     = Color.FromRgb(0x36, 0x16, 0x10);
    private static readonly Color C_Ai      = Color.FromRgb(0x22, 0x14, 0x38);
    private static readonly Color C_Text    = Color.FromRgb(0xDD, 0xDD, 0xDD);
    private static readonly Color C_Dim     = Color.FromRgb(0x99, 0x99, 0x99);
    private static Brush B(Color c) => new SolidColorBrush(c);

    private void OpenAdvancedCalculatorWindow()
    {
        // ── Shared display state ──────────────────────────────────────────────
        var history = new List<string>();

        // ── Window ────────────────────────────────────────────────────────────
        var win = new Window
        {
            Title           = "Calculator",
            Width           = 520, Height = 640,
            MinWidth        = 440, MinHeight = 560,
            Background      = B(C_Bg),
            WindowStyle     = WindowStyle.ToolWindow,
            ResizeMode      = ResizeMode.CanResize,
            Owner           = this,
            ShowInTaskbar   = false,
            Topmost         = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        // ── Outer layout ──────────────────────────────────────────────────────
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // tabs
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // body
        win.Content = root;

        // ── Tab bar ───────────────────────────────────────────────────────────
        var tabBar = new StackPanel { Orientation = Orientation.Horizontal, Background = B(Color.FromRgb(0x0C,0x0C,0x0C)) };
        Grid.SetRow(tabBar, 0);
        root.Children.Add(tabBar);

        var tabBody = new Grid();
        Grid.SetRow(tabBody, 1);
        root.Children.Add(tabBody);

        Grid? stdPanel = null, sciPanel = null, aiPanel = null;

        Button MakeTab(string label)
        {
            var b = new Button
            {
                Content         = label,
                Height          = 30, Padding = new Thickness(18, 0, 18, 0),
                Background      = B(C_Surface), Foreground = B(C_Dim),
                BorderThickness = new Thickness(0,0,1,0), BorderBrush = B(Color.FromRgb(0x2A,0x2A,0x2A)),
                FontSize        = 12, FontWeight = FontWeights.SemiBold,
                Cursor          = Cursors.Hand,
            };
            b.Template = CalcRoundedTemplate(0);
            tabBar.Children.Add(b);
            return b;
        }
        var btnStd = MakeTab("Standard");
        var btnSci = MakeTab("Scientific");
        var btnAi  = MakeTab("⚡ AI");

        void ActivateTab(int idx)
        {
            stdPanel!.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
            sciPanel!.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
            aiPanel! .Visibility = idx == 2 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var (b, i) in new[]{(btnStd,0),(btnSci,1),(btnAi,2)})
            {
                b.Background = B(i == idx ? C_Fn : C_Surface);
                b.Foreground = B(i == idx ? C_Text : C_Dim);
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  STANDARD TAB
        // ═════════════════════════════════════════════════════════════════════
        stdPanel = new Grid { Margin = new Thickness(8) };
        for (int i = 0; i < 7; i++) stdPanel.RowDefinitions.Add(new RowDefinition
            { Height = i == 0 ? new GridLength(54) : new GridLength(1, GridUnitType.Star) });
        for (int j = 0; j < 4; j++) stdPanel.ColumnDefinitions.Add(new ColumnDefinition
            { Width = new GridLength(1, GridUnitType.Star) });

        var stdDisplay = MakeDisplay();
        Grid.SetColumnSpan(stdDisplay, 4);
        stdPanel.Children.Add(stdDisplay);

        // Standard calculator engine (shared also by Sci)
        string stdOp = ""; string stdA = ""; bool stdNew = true;

        void StdPress(string v)
        {
            if (v is "+" or "-" or "×" or "÷" or "^" or "%" )
            { stdA = stdDisplay.Text; stdOp = v; stdNew = true; return; }

            if (v == "=")
            {
                if (string.IsNullOrEmpty(stdOp)) return;
                if (!double.TryParse(stdA, NumberStyles.Any, CultureInfo.InvariantCulture, out double a) ||
                    !double.TryParse(stdDisplay.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double b)) return;
                double r = stdOp switch { "+"=>(a+b), "-"=>(a-b), "×"=>(a*b),
                    "÷"=>(b==0?double.NaN:a/b), "^"=>Math.Pow(a,b), "%"=>(a*b/100), _=>b };
                stdDisplay.Text = double.IsNaN(r) ? "Error" : r.ToString("G14", CultureInfo.InvariantCulture);
                stdOp = ""; stdNew = true; return;
            }
            if (v == "C") { stdDisplay.Text = "0"; stdA=""; stdOp=""; stdNew=true; return; }
            if (v == "⌫") { stdDisplay.Text = stdDisplay.Text.Length>1?stdDisplay.Text[..^1]:"0"; return; }
            if (v == "+/-") { if (double.TryParse(stdDisplay.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double n)) stdDisplay.Text = (-n).ToString(CultureInfo.InvariantCulture); return; }
            if (v == ".")  { if (stdNew){stdDisplay.Text="0.";stdNew=false;return;} if(!stdDisplay.Text.Contains('.'))stdDisplay.Text+="."; return; }
            if (stdNew) { stdDisplay.Text = v; stdNew = false; }
            else stdDisplay.Text = stdDisplay.Text == "0" ? v : stdDisplay.Text + v;
        }

        (string, int, int, int, Color)[] stdBtns = {
            ("C",1,0,1,C_Cls),("⌫",1,1,1,C_Cls),("+/-",1,2,1,C_Cls),("÷",1,3,1,C_Op),
            ("7",2,0,1,C_Num),("8",2,1,1,C_Num),("9",2,2,1,C_Num),("×",2,3,1,C_Op),
            ("4",3,0,1,C_Num),("5",3,1,1,C_Num),("6",3,2,1,C_Num),("-",3,3,1,C_Op),
            ("1",4,0,1,C_Num),("2",4,1,1,C_Num),("3",4,2,1,C_Num),("+",4,3,1,C_Op),
            ("0",5,0,2,C_Num),(".",5,2,1,C_Num),("=",5,3,1,C_Eq),
        };
        foreach (var (lbl, r, c, span, bg) in stdBtns)
        {
            var btn = CalcBtn(lbl, bg); var cap = lbl;
            btn.Click += (_, _) => StdPress(cap);
            Grid.SetRow(btn, r); Grid.SetColumn(btn, c);
            if (span>1) Grid.SetColumnSpan(btn, span);
            stdPanel.Children.Add(btn);
        }
        tabBody.Children.Add(stdPanel);

        // ═════════════════════════════════════════════════════════════════════
        //  SCIENTIFIC TAB
        // ═════════════════════════════════════════════════════════════════════
        sciPanel = new Grid { Margin = new Thickness(8), Visibility = Visibility.Collapsed };
        // rows: display + 8 button rows
        sciPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(54) });
        for (int i = 0; i < 8; i++) sciPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        for (int j = 0; j < 8; j++) sciPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var sciDisplay = MakeDisplay();
        Grid.SetColumnSpan(sciDisplay, 8);
        sciPanel.Children.Add(sciDisplay);

        // Sci engine: expression accumulation with Math evaluator
        var sciExpr = new StringBuilder();
        bool sciJustEvaled = false;
        bool sciInvMode = false;
        bool sciHypMode = false;
        bool sciDegMode = true; // degrees by default

        TextBlock sciSubDisplay = new TextBlock
        {
            Foreground = B(C_Dim), FontSize = 11, FontFamily = new FontFamily("Consolas"),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 2, 4, 0), Text = ""
        };

        var sciDisplayWrap = new Grid();
        sciDisplayWrap.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        sciDisplayWrap.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        // We'll reuse sciDisplay inline

        double ToRad(double d) => sciDegMode ? d * Math.PI / 180.0 : d;
        double FromRad(double r) => sciDegMode ? r * 180.0 / Math.PI : r;

        Button? degRadBtn = null; Button? invBtn = null; Button? hypBtn = null;

        void SciEval()
        {
            string raw = sciExpr.ToString().Trim();
            if (string.IsNullOrEmpty(raw)) return;

            // Replace constants + functions in expression for DataTable evaluation
            string e2 = raw
                .Replace("π", Math.PI.ToString("R", CultureInfo.InvariantCulture))
                .Replace("e",  Math.E .ToString("R", CultureInfo.InvariantCulture))
                .Replace("×","*").Replace("÷","/").Replace("^","**");

            // Handle single-value display number + trig/log
            if (double.TryParse(sciDisplay.Text.Replace(",",""), NumberStyles.Any, CultureInfo.InvariantCulture, out double num))
            {
                // If expression ends with a function call indicator, apply to display
            }

            // Full expression evaluator via DataTable
            try
            {
                // Expand ** to Math.Pow via a custom pass
                string dtExpr = ExpandPow(e2);
                var dt  = new DataTable();
                var res = dt.Compute(dtExpr, "");
                double val = Convert.ToDouble(res, CultureInfo.InvariantCulture);
                sciDisplay.Text = FormatSciResult(val);
                sciExpr.Clear();
                sciExpr.Append(sciDisplay.Text);
                sciJustEvaled = true;
            }
            catch
            {
                sciDisplay.Text = "Syntax Error";
                sciExpr.Clear();
                sciJustEvaled = true;
            }
        }

        void SciApplyFunc(string fn)
        {
            if (!double.TryParse(sciDisplay.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double x))
            { sciDisplay.Text = "Error"; return; }

            double r = fn switch
            {
                "sin"  => sciHypMode ? Math.Sinh(ToRad(x)) : Math.Sin(ToRad(x)),
                "cos"  => sciHypMode ? Math.Cosh(ToRad(x)) : Math.Cos(ToRad(x)),
                "tan"  => sciHypMode ? Math.Tanh(ToRad(x)) : Math.Tan(ToRad(x)),
                "asin" => sciHypMode ? Math.Log(x+Math.Sqrt(x*x+1)) : FromRad(Math.Asin(x)),
                "acos" => sciHypMode ? Math.Log(x+Math.Sqrt(x*x-1)) : FromRad(Math.Acos(x)),
                "atan" => sciHypMode ? 0.5*Math.Log((1+x)/(1-x))   : FromRad(Math.Atan(x)),
                "log"  => Math.Log10(x),
                "ln"   => Math.Log(x),
                "log2" => Math.Log2(x),
                "√"    => Math.Sqrt(x),
                "∛"    => Math.Cbrt(x),
                "x²"   => x * x,
                "x³"   => x * x * x,
                "1/x"  => x == 0 ? double.NaN : 1.0 / x,
                "x!"   => Factorial((long)Math.Abs(x)),
                "eˣ"   => Math.Exp(x),
                "10ˣ"  => Math.Pow(10, x),
                "2ˣ"   => Math.Pow(2, x),
                "abs"  => Math.Abs(x),
                "ceil" => Math.Ceiling(x),
                "floor"=> Math.Floor(x),
                "round"=> Math.Round(x, MidpointRounding.AwayFromZero),
                _      => x
            };
            sciDisplay.Text = double.IsNaN(r) || double.IsInfinity(r) ? "Error" : FormatSciResult(r);
            sciExpr.Clear(); sciExpr.Append(sciDisplay.Text);
            sciJustEvaled = true;
        }

        void SciPress(string v)
        {
            switch (v)
            {
                case "=": SciEval(); return;
                case "C": sciDisplay.Text="0"; sciExpr.Clear(); sciJustEvaled=false; return;
                case "CE": sciDisplay.Text="0"; sciJustEvaled=false; return;
                case "⌫":
                    if (sciJustEvaled) { sciDisplay.Text="0"; sciExpr.Clear(); sciJustEvaled=false; }
                    else sciDisplay.Text = sciDisplay.Text.Length>1?sciDisplay.Text[..^1]:"0";
                    return;
                case "+/-":
                    if(double.TryParse(sciDisplay.Text,NumberStyles.Any,CultureInfo.InvariantCulture,out double neg))
                        sciDisplay.Text = (-neg).ToString(CultureInfo.InvariantCulture);
                    return;
                case ".":
                    if (sciJustEvaled) { sciDisplay.Text="0."; sciJustEvaled=false; return; }
                    if (!sciDisplay.Text.Contains('.')) sciDisplay.Text += ".";
                    return;
                case "π": sciDisplay.Text = Math.PI.ToString("G14",CultureInfo.InvariantCulture); sciJustEvaled=true; return;
                case "e":  sciDisplay.Text = Math.E .ToString("G14",CultureInfo.InvariantCulture); sciJustEvaled=true; return;
                case "Ans": /* keep last result */ return;
                case "DEG": sciDegMode=true;  if(degRadBtn!=null)degRadBtn.Content="DEG"; return;
                case "RAD": sciDegMode=false; if(degRadBtn!=null)degRadBtn.Content="RAD"; return;
                case "INV":
                    sciInvMode = !sciInvMode;
                    if(invBtn!=null) invBtn.Background = B(sciInvMode?C_Op:C_Fn);
                    UpdateSciInvLabels(sciPanel, sciInvMode, sciHypMode);
                    return;
                case "HYP":
                    sciHypMode = !sciHypMode;
                    if(hypBtn!=null) hypBtn.Background = B(sciHypMode?C_Op:C_Fn);
                    return;
                case "sin" or "cos" or "tan":
                    SciApplyFunc(sciInvMode ? "a"+v : v); return;
                case "log" or "ln" or "log2" or "√" or "∛" or "x²" or "x³" or "1/x"
                     or "x!" or "eˣ" or "10ˣ" or "2ˣ" or "abs" or "ceil" or "floor" or "round":
                    SciApplyFunc(v); return;
                case "+" or "-" or "×" or "÷" or "^":
                    if (sciJustEvaled) { sciExpr.Clear(); sciExpr.Append(sciDisplay.Text); sciJustEvaled=false; }
                    else sciExpr.Append(sciDisplay.Text);
                    sciExpr.Append(v);
                    sciDisplay.Text = "0"; sciJustEvaled=false;
                    return;
                case "(": sciExpr.Append("("); sciDisplay.Text="0"; sciJustEvaled=false; return;
                case ")":
                    sciExpr.Append(sciDisplay.Text); sciExpr.Append(")");
                    sciDisplay.Text="0"; sciJustEvaled=false; return;
                case "EE": sciExpr.Append(sciDisplay.Text+"e"); sciDisplay.Text="0"; sciJustEvaled=false; return;
                case "Rand": sciDisplay.Text = new Random().NextDouble().ToString("G6",CultureInfo.InvariantCulture); sciJustEvaled=true; return;
                default: // digit
                    if (sciJustEvaled) { sciDisplay.Text=v; sciJustEvaled=false; }
                    else sciDisplay.Text = sciDisplay.Text=="0"?v:sciDisplay.Text+v;
                    return;
            }
        }

        // Scientific button layout: 8 cols × 8 rows of buttons
        // (row, col, label, color, tooltip)
        (int r, int c, string lbl, Color bg, string tip)[] sciBtns = {
            // Row 1: Mode / meta
            (1,0,"INV",   C_Fn,  "Inverse trig / functions"),
            (1,1,"HYP",   C_Fn,  "Hyperbolic mode"),
            (1,2,"DEG",   C_Fn,  "Toggle Degrees / Radians"),
            (1,3,"(",     C_Op,  "Open parenthesis"),
            (1,4,")",     C_Op,  "Close parenthesis"),
            (1,5,"C",     C_Cls, "Clear all"),
            (1,6,"CE",    C_Cls, "Clear entry"),
            (1,7,"⌫",    C_Cls, "Backspace"),
            // Row 2: Trig
            (2,0,"sin",   C_Fn,  "Sine"),
            (2,1,"cos",   C_Fn,  "Cosine"),
            (2,2,"tan",   C_Fn,  "Tangent"),
            (2,3,"π",     C_Fn,  "Pi constant"),
            (2,4,"e",     C_Fn,  "Euler's number"),
            (2,5,"7",     C_Num, ""),
            (2,6,"8",     C_Num, ""),
            (2,7,"9",     C_Num, ""),
            // Row 3: Powers / roots
            (3,0,"x²",    C_Fn,  "Square"),
            (3,1,"x³",    C_Fn,  "Cube"),
            (3,2,"√",     C_Fn,  "Square root"),
            (3,3,"∛",     C_Fn,  "Cube root"),
            (3,4,"^",     C_Op,  "Power xʸ"),
            (3,5,"4",     C_Num, ""),
            (3,6,"5",     C_Num, ""),
            (3,7,"6",     C_Num, ""),
            // Row 4: Log / exp
            (4,0,"log",   C_Fn,  "Log base 10"),
            (4,1,"ln",    C_Fn,  "Natural log"),
            (4,2,"log2",  C_Fn,  "Log base 2"),
            (4,3,"eˣ",    C_Fn,  "e to the x"),
            (4,4,"10ˣ",   C_Fn,  "10 to the x"),
            (4,5,"1",     C_Num, ""),
            (4,6,"2",     C_Num, ""),
            (4,7,"3",     C_Num, ""),
            // Row 5: Misc math
            (5,0,"1/x",   C_Fn,  "Reciprocal"),
            (5,1,"x!",    C_Fn,  "Factorial"),
            (5,2,"abs",   C_Fn,  "Absolute value"),
            (5,3,"Rand",  C_Fn,  "Random [0,1)"),
            (5,4,"EE",    C_Op,  "Scientific notation ×10ⁿ"),
            (5,5,"+/-",   C_Num, "Negate"),
            (5,6,"0",     C_Num, ""),
            (5,7,".",     C_Num, "Decimal point"),
            // Row 6: Rounding + operators
            (6,0,"ceil",  C_Fn,  "Ceiling"),
            (6,1,"floor", C_Fn,  "Floor"),
            (6,2,"round", C_Fn,  "Round"),
            (6,3,"2ˣ",    C_Fn,  "2 to the x"),
            (6,4,"÷",     C_Op,  "Divide"),
            (6,5,"×",     C_Op,  "Multiply"),
            (6,6,"-",     C_Op,  "Subtract"),
            (6,7,"+",     C_Op,  "Add"),
            // Row 7: Memory + =
            (7,0,"MS",    C_Fn,  "Memory Store"),
            (7,1,"MR",    C_Fn,  "Memory Recall"),
            (7,2,"MC",    C_Fn,  "Memory Clear"),
            (7,3,"M+",    C_Fn,  "Memory Add"),
            (7,4,"M-",    C_Fn,  "Memory Subtract"),
            (7,5,"(",     C_Op,  ""),
            (7,6,")",     C_Op,  ""),
            (7,7,"=",     C_Eq,  "Evaluate"),
        };

        double sciMem = 0.0;

        foreach (var (r, c, lbl, bg, tip) in sciBtns)
        {
            var btn = CalcBtn(lbl, bg, 11);
            if (!string.IsNullOrEmpty(tip)) btn.ToolTip = tip;
            var cap = lbl;

            // Memory buttons handled specially
            if (cap == "MS") { btn.Click += (_, _) => { if (double.TryParse(sciDisplay.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double v)) sciMem = v; }; }
            else if (cap == "MR") { btn.Click += (_, _) => { sciDisplay.Text = FormatSciResult(sciMem); sciJustEvaled=true; }; }
            else if (cap == "MC") { btn.Click += (_, _) => sciMem = 0; }
            else if (cap == "M+") { btn.Click += (_, _) => { if (double.TryParse(sciDisplay.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double v)) sciMem += v; }; }
            else if (cap == "M-") { btn.Click += (_, _) => { if (double.TryParse(sciDisplay.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double v)) sciMem -= v; }; }
            else if (cap == "DEG") { btn.Click += (_, _) => { sciDegMode=!sciDegMode; btn.Content=sciDegMode?"DEG":"RAD"; }; degRadBtn = btn; }
            else if (cap == "INV") { btn.Click += (_, _) => SciPress("INV"); invBtn = btn; }
            else if (cap == "HYP") { btn.Click += (_, _) => SciPress("HYP"); hypBtn = btn; }
            else btn.Click += (_, _) => SciPress(cap);

            Grid.SetRow(btn, r); Grid.SetColumn(btn, c);
            sciPanel.Children.Add(btn);
        }

        // Sub-expression display above sci buttons
        var sciSubRow = new TextBlock
        {
            Foreground = B(C_Dim), FontSize = 10, FontFamily = new FontFamily("Consolas"),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 1, 4, 1), Text = "",
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        // Re-hook display to show expression
        var sciDispTimer = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromMilliseconds(200) };
        sciDispTimer.Tick += (_, _) => sciSubRow.Text = sciExpr.Length > 0 ? sciExpr.ToString() : "";
        sciDispTimer.Start();
        win.Closed += (_, _) => sciDispTimer.Stop();

        tabBody.Children.Add(sciPanel);

        // ═════════════════════════════════════════════════════════════════════
        //  AI TAB
        // ═════════════════════════════════════════════════════════════════════
        aiPanel = new Grid { Margin = new Thickness(8), Visibility = Visibility.Collapsed };
        aiPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // provider row
        aiPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // api key row
        aiPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // chat
        aiPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // input

        // Provider selector
        var providerRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,0,0,6) };
        string aiProvider = "Claude";
        Button MakeProvBtn(string name)
        {
            var pb = new Button
            {
                Content = name, Height = 26, Padding = new Thickness(10, 0, 10, 0),
                Background = B(name == "Claude" ? C_Op : C_Surface),
                Foreground = B(C_Text), BorderThickness = new Thickness(0),
                FontSize = 11, Cursor = Cursors.Hand, Margin = new Thickness(0,0,4,0)
            };
            pb.Template = CalcRoundedTemplate(4);
            pb.Click += (_, _) =>
            {
                aiProvider = name;
                foreach (Button x in providerRow.Children) x.Background = B(C_Surface);
                pb.Background = B(C_Op);
            };
            providerRow.Children.Add(pb);
            return pb;
        }
        MakeProvBtn("Claude"); MakeProvBtn("ChatGPT"); MakeProvBtn("Gemini");
        Grid.SetRow(providerRow, 0);
        aiPanel.Children.Add(providerRow);

        // API key input
        var keyRow = new Grid { Margin = new Thickness(0,0,0,6) };
        keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(keyRow, 1);
        keyRow.Children.Add(new TextBlock { Text = "API Key:", Foreground = B(C_Dim), FontSize=11,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,8,0) });
        var keyBox = new PasswordBox
        {
            Password = SettingsService.Current.ClaudeApiKey,
            Background = B(C_Surface), Foreground = B(C_Text),
            BorderBrush = B(Color.FromRgb(0x33,0x33,0x33)), BorderThickness = new Thickness(1),
            FontSize = 11, Padding = new Thickness(5, 3, 5, 3), Height = 28
        };
        keyBox.PasswordChanged += (_, _) =>
        {
            // Save to the appropriate slot
            switch (aiProvider)
            {
                case "Claude":  SettingsService.Current.ClaudeApiKey     = keyBox.Password; break;
                case "ChatGPT": SettingsService.Current.ChatGptApiKey    = keyBox.Password; break;
                case "Gemini":  SettingsService.Current.GeminiApiKey     = keyBox.Password; break;
            }
            SettingsService.Save();
        };
        // When provider switches, reload correct key
        foreach (Button pb in providerRow.Children)
        {
            var capName = (string)pb.Content;
            pb.Click += (_, _) =>
            {
                keyBox.Password = capName switch
                {
                    "Claude"  => SettingsService.Current.ClaudeApiKey,
                    "ChatGPT" => SettingsService.Current.ChatGptApiKey,
                    "Gemini"  => SettingsService.Current.GeminiApiKey,
                    _         => ""
                };
            };
        }
        Grid.SetColumn(keyBox, 1);
        keyRow.Children.Add(keyBox);
        aiPanel.Children.Add(keyRow);

        // Chat history
        var chatScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = B(C_Surface),
            Margin = new Thickness(0,0,0,6)
        };
        var chatStack = new StackPanel { Margin = new Thickness(6) };
        chatScroll.Content = chatStack;
        Grid.SetRow(chatScroll, 2);
        aiPanel.Children.Add(chatScroll);

        // Input row
        var inputRow = new Grid();
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetRow(inputRow, 3);
        aiPanel.Children.Add(inputRow);

        var aiInput = new TextBox
        {
            Background = B(C_Surface), Foreground = B(C_Text),
            BorderBrush = B(Color.FromRgb(0x2A,0x2A,0x2A)), BorderThickness = new Thickness(1),
            FontSize = 12, Padding = new Thickness(6, 4, 6, 4), AcceptsReturn = false,
            VerticalContentAlignment = VerticalAlignment.Center, Height = 34
        };
        var sendBtn = CalcBtn("↵ Ask", C_Eq, 11);
        sendBtn.MinWidth = 64; sendBtn.Height = 34;
        Grid.SetColumn(sendBtn, 1);
        inputRow.Children.Add(aiInput);
        inputRow.Children.Add(sendBtn);

        void AddChat(string who, string text, Color col)
        {
            var tb = new TextBlock
            {
                Text = $"[{who}]  {text}",
                Foreground = B(col), FontSize = 11, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 4),
                FontFamily = new FontFamily("Consolas")
            };
            chatStack.Children.Add(tb);
            chatScroll.ScrollToEnd();
        }

        static string LocalSolve(string q)
        {
            // Try DataTable first
            try
            {
                string e2 = q.Replace("×","*").Replace("÷","/").Replace("^","**").Replace("π", Math.PI.ToString("R",CultureInfo.InvariantCulture));
                var dt = new DataTable();
                var res = dt.Compute(ExpandPow(e2), "");
                return Convert.ToDouble(res, CultureInfo.InvariantCulture).ToString("G14", CultureInfo.InvariantCulture);
            }
            catch { return ""; }
        }

        async void SendAiQuery()
        {
            string q = aiInput.Text.Trim();
            if (string.IsNullOrEmpty(q)) return;
            aiInput.Clear();
            AddChat("You", q, C_Text);

            // Try local first
            string local = LocalSolve(q);
            if (!string.IsNullOrEmpty(local)) { AddChat("Calc", local, Color.FromRgb(0x44,0xFF,0x88)); return; }

            string key = aiProvider switch
            {
                "Claude"  => SettingsService.Current.ClaudeApiKey,
                "ChatGPT" => SettingsService.Current.ChatGptApiKey,
                "Gemini"  => SettingsService.Current.GeminiApiKey,
                _         => ""
            };

            if (string.IsNullOrEmpty(key))
            {
                AddChat("Error", $"No API key for {aiProvider}. Enter it above.", Color.FromRgb(0xFF,0x44,0x44));
                return;
            }

            AddChat(aiProvider, "…", C_Dim);
            var loadingMsg = (TextBlock)chatStack.Children[^1];

            try
            {
                string reply = aiProvider switch
                {
                    "ChatGPT" => await CallOpenAI(q, key),
                    "Gemini"  => await CallGemini(q, key),
                    _         => await CallClaude(q, key)
                };
                loadingMsg.Text = $"[{aiProvider}]  {reply}";
                loadingMsg.Foreground = B(Color.FromRgb(0x88,0xCC,0xFF));
            }
            catch (Exception ex)
            {
                loadingMsg.Text = $"[Error]  {ex.Message}";
                loadingMsg.Foreground = B(Color.FromRgb(0xFF,0x55,0x55));
            }
            chatScroll.ScrollToEnd();
        }

        sendBtn.Click += (_, _) => SendAiQuery();
        aiInput.KeyDown += (_, e) => { if (e.Key == Key.Return) { SendAiQuery(); e.Handled = true; } };
        tabBody.Children.Add(aiPanel);

        // ── Tab switching ─────────────────────────────────────────────────────
        btnStd.Click += (_, _) => ActivateTab(0);
        btnSci.Click += (_, _) => ActivateTab(1);
        btnAi .Click += (_, _) => ActivateTab(2);
        ActivateTab(0);

        win.Show();
    }

    // ── API call helpers ──────────────────────────────────────────────────────

    private static readonly HttpClient _calcHttp = new() { Timeout = TimeSpan.FromSeconds(30) };

    private const string CalcSystemPrompt =
        "You are a precise calculator and mathematician. Answer ONLY with the numeric result " +
        "or a concise step-by-step solution. No prose, no markdown, no units unless requested. " +
        "For equations, show each step on a new line ending with the final answer.";

    private static async Task<string> CallClaude(string question, string key)
    {
        var body = new { model = "claude-haiku-4-5-20251001", max_tokens = 1024,
            system = CalcSystemPrompt,
            messages = new[] { new { role = "user", content = question } } };
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        req.Headers.Add("x-api-key", key);
        req.Headers.Add("anthropic-version", "2023-06-01");
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var resp = await _calcHttp.SendAsync(req);
        string json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("content", out var content) && content.GetArrayLength() > 0)
            return content[0].GetProperty("text").GetString() ?? "No response";
        if (doc.RootElement.TryGetProperty("error", out var err))
            throw new Exception(err.GetProperty("message").GetString());
        return json;
    }

    private static async Task<string> CallOpenAI(string question, string key)
    {
        var body = new
        {
            model = "gpt-4o-mini",
            messages = new[]
            {
                new { role = "system", content = CalcSystemPrompt },
                new { role = "user",   content = question }
            },
            max_tokens = 1024
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        req.Headers.Add("Authorization", $"Bearer {key}");
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var resp = await _calcHttp.SendAsync(req);
        string json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("choices", out var ch) && ch.GetArrayLength() > 0)
            return ch[0].GetProperty("message").GetProperty("content").GetString() ?? "No response";
        if (doc.RootElement.TryGetProperty("error", out var err))
            throw new Exception(err.GetProperty("message").GetString());
        return json;
    }

    private static async Task<string> CallGemini(string question, string key)
    {
        var body = new
        {
            systemInstruction = new { parts = new[] { new { text = CalcSystemPrompt } } },
            contents = new[] { new { parts = new[] { new { text = question } } } }
        };
        string url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key={Uri.EscapeDataString(key)}";
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var resp = await _calcHttp.SendAsync(req);
        string json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("candidates", out var cands) && cands.GetArrayLength() > 0)
            return cands[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString() ?? "No response";
        if (doc.RootElement.TryGetProperty("error", out var err))
            throw new Exception(err.GetProperty("message").GetString());
        return json;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string ExpandPow(string expr)
    {
        // DataTable doesn't support **: replace a**b → (Math.Pow stub not available in DataTable)
        // We convert x**n to repeated multiplication for small integers, else leave for error
        return Regex.Replace(expr, @"(\-?\d+\.?\d*)\*\*(\-?\d+\.?\d*)", m =>
        {
            if (double.TryParse(m.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double b2) &&
                double.TryParse(m.Groups[2].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double exp))
                return Math.Pow(b2, exp).ToString("R", CultureInfo.InvariantCulture);
            return m.Value;
        });
    }

    private static double Factorial(long n)
    {
        if (n > 20) return double.PositiveInfinity;
        double r = 1; for (long i = 2; i <= n; i++) r *= i; return r;
    }

    private static string FormatSciResult(double v)
    {
        if (double.IsNaN(v)) return "Undefined";
        if (double.IsInfinity(v)) return v > 0 ? "∞" : "-∞";
        if (Math.Abs(v) >= 1e15 || (Math.Abs(v) < 1e-10 && v != 0))
            return v.ToString("G6", CultureInfo.InvariantCulture);
        return v.ToString("G14", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
    }

    private static void UpdateSciInvLabels(Grid sciPanel, bool inv, bool hyp)
    {
        // Update sin/cos/tan button labels when INV toggled
        foreach (var child in sciPanel.Children.OfType<Button>())
        {
            string cur = child.Content?.ToString() ?? "";
            if (!inv && cur is "asin" or "acos" or "atan")
                child.Content = cur[1..]; // strip leading 'a'
            else if (inv && cur is "sin" or "cos" or "tan")
                child.Content = "a" + cur;
        }
    }

    // ── UI factory helpers ────────────────────────────────────────────────────

    private static TextBox MakeDisplay()
        => new TextBox
        {
            Text = "0",
            HorizontalContentAlignment = HorizontalAlignment.Right,
            Background      = new SolidColorBrush(Color.FromRgb(0x0C, 0x0C, 0x0C)),
            Foreground      = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)),
            FontSize        = 26, FontWeight = FontWeights.Bold,
            FontFamily      = new FontFamily("Consolas"),
            IsReadOnly      = true, Margin = new Thickness(2, 2, 2, 6),
            Padding         = new Thickness(6, 4, 6, 4)
        };

    private static Button CalcBtn(string label, Color bg, int fontSize = 14)
    {
        var b = new Button
        {
            Content = label, FontSize = fontSize,
            Background = new SolidColorBrush(bg),
            Foreground = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
            Margin = new Thickness(2), Cursor = Cursors.Hand
        };
        b.Template = CalcRoundedTemplate(3);
        return b;
    }

    private static ControlTemplate CalcRoundedTemplate(int radius)
    {
        var t  = new ControlTemplate(typeof(Button));
        var bd = new FrameworkElementFactory(typeof(Border));
        bd.SetBinding(Border.BackgroundProperty,      new System.Windows.Data.Binding("Background")      { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        bd.SetBinding(Border.BorderBrushProperty,     new System.Windows.Data.Binding("BorderBrush")     { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        bd.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty,   VerticalAlignment.Center);
        bd.AppendChild(cp);
        t.VisualTree = bd;
        return t;
    }
}