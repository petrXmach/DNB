using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DNBridge.Calc;
using Microsoft.Win32;

namespace DNBridge.Wpf;

/// <summary>
/// Test hub for the an3f4w calculation engine. Offers a basic DLL probe ("Test an3f4w DLL" —
/// <see cref="An3f4wSmokeTest"/>) plus a checklist of per-kind calculation tests
/// (<see cref="An3f4wCalcTest.Catalog"/>): each row has a checkbox, the input dump path (preset
/// from <c>data/</c>) and a Browse button. "RUN CALCULATIONS" runs every ticked test sequentially
/// into one trace log. The engine work lives in DNBridge.Calc; this window is a thin shell.
///
/// <para>The engine is single-instance and not thread-safe, so every test runs off the UI thread
/// (one <c>Task.Run</c>), and each <see cref="An3f4wCalcTest.Run"/> resets it with Done+Init — so
/// running several tests back-to-back needs no special handling between them.</para>
/// </summary>
public partial class CalcTestWindow : Window
{
    // One selectable test row: its catalog entry plus the controls that hold the user's choices.
    private sealed record TestRow(An3f4wCalcCase Case, CheckBox Check, TextBox PathBox);

    private readonly List<TestRow> _rows = new();
    private readonly string _dataDir;

    public CalcTestWindow()
    {
        InitializeComponent();
        _dataDir = ResolveDataDir();
        BuildTestRows();
    }

    // Find the data/ folder by walking up from the app base dir (handles bin/Debug/... depth) so
    // the presets work in a dev checkout; fall back to the repo's well-known absolute path.
    private static string ResolveDataDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "data");
            if (Directory.Exists(candidate))
                return candidate;
        }
        return @"C:\_EGC\DNBridge\data";
    }

    // Build one row per catalog entry: [check]  Name — description (calc_kind)   [path box] [Browse…]
    private void BuildTestRows()
    {
        foreach (An3f4wCalcCase c in An3f4wCalcTest.Catalog)
        {
            var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var check = new CheckBox
            {
                Content = $"{c.Name} — {c.Description}  (kind {c.CalcKind})",
                IsChecked = true,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(check, 0);

            var pathBox = new TextBox
            {
                Text = Path.Combine(_dataDir, c.FileName),
                Height = 24,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
            };
            Grid.SetColumn(pathBox, 1);

            var browse = new Button
            {
                Content = "Browse…",
                Width = 80,
                Height = 24,
                Margin = new Thickness(8, 0, 0, 0),
            };
            browse.Click += (_, _) => Browse(pathBox);
            Grid.SetColumn(browse, 2);

            grid.Children.Add(check);
            grid.Children.Add(pathBox);
            grid.Children.Add(browse);
            TestListPanel.Children.Add(grid);

            _rows.Add(new TestRow(c, check, pathBox));
        }
    }

    private void Browse(TextBox pathBox)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select engine-input dump",
            Filter = "Text dumps (*.txt;*.dat)|*.txt;*.dat|All files (*.*)|*.*",
            InitialDirectory = Directory.Exists(_dataDir) ? _dataDir : null,
        };
        if (dlg.ShowDialog(this) == true)
            pathBox.Text = dlg.FileName;
    }

    private void SelectAllButton_Click(object sender, RoutedEventArgs e) => SetAll(true);
    private void ClearAllButton_Click(object sender, RoutedEventArgs e) => SetAll(false);
    private void SetAll(bool on)
    {
        foreach (TestRow r in _rows)
            r.Check.IsChecked = on;
    }

    private void ClearLogButton_Click(object sender, RoutedEventArgs e) => LogBox.Clear();

    // Basic DLL probe — anInitLibrary + anDLLVersion. Off the UI thread (native load can block).
    private async void SmokeTestButton_Click(object sender, RoutedEventArgs e)
    {
        SmokeTestButton.IsEnabled = false;
        SmokeStatusText.Text = "DLL: testing…";
        SmokeStatusText.Foreground = Brushes.Gray;
        Log("===== Test an3f4w DLL (smoke probe) =====");

        var result = await Task.Run(An3f4wSmokeTest.Run);

        SmokeStatusText.Text = result.Ok ? $"DLL OK — v{result.Version}" : $"DLL FAIL — {result.Message}";
        SmokeStatusText.Foreground = result.Ok ? Brushes.Green : Brushes.Red;
        Log(result.Message);
        await Dispatcher.BeginInvoke(() => LogBox.ScrollToEnd());

        SmokeTestButton.IsEnabled = true;
    }

    // Run every ticked test sequentially. The engine is single-instance/stateful, so all runs go
    // on one background thread; each test resets the engine itself (Done+Init).
    private async void RunButton_Click(object sender, RoutedEventArgs e)
    {
        // Snapshot the selection on the UI thread (case + resolved path), then hand it to the worker.
        var selected = new List<(An3f4wCalcCase Case, string Path)>();
        foreach (TestRow r in _rows)
        {
            if (r.Check.IsChecked == true)
                selected.Add((r.Case, r.PathBox.Text.Trim()));
        }

        if (selected.Count == 0)
        {
            SetStatus("No tests selected.", Brushes.Red);
            return;
        }

        bool createOutFiles = CreateOutFilesCheck.IsChecked == true;

        SetBusy(true);
        SetStatus($"Running {selected.Count} test(s)…", Brushes.Gray);

        int ok = 0, fail = 0;
        await Task.Run(() =>
        {
            foreach ((An3f4wCalcCase c, string path) in selected)
            {
                string inputText;
                try
                {
                    inputText = File.ReadAllText(path);
                }
                catch (Exception ex)
                {
                    Log($"===== TEST: {c.Name} =====");
                    Log($"Cannot read \"{path}\": {ex.Message}");
                    fail++;
                    continue;
                }

                // Authoritative kind comes from the dump header (calc_kind=N); the catalog value is
                // only a fallback if the marker is missing.
                uint kind = An3f4wCalcTest.ParseCalcKind(inputText, c.CalcKind);
                var result = An3f4wCalcTest.Run(c.Name, kind, inputText, Log, createOutFiles);
                if (result.Ok) ok++; else fail++;
                Log(result.Message);
                Log(string.Empty);
            }
        });

        await Dispatcher.BeginInvoke(() => LogBox.ScrollToEnd());
        SetStatus($"Done — {ok} OK, {fail} failed (of {selected.Count}).",
                  fail == 0 ? Brushes.Green : Brushes.Red);
        SetBusy(false);
    }

    private void SetBusy(bool busy)
    {
        RunButton.IsEnabled = !busy;
        SmokeTestButton.IsEnabled = !busy;
        SelectAllButton.IsEnabled = !busy;
        ClearAllButton.IsEnabled = !busy;
    }

    // Append one log line, marshalled to the UI thread (the worker calls this from a Task).
    private void Log(string line) =>
        Dispatcher.BeginInvoke(() =>
            LogBox.AppendText($"[{DateTime.Now:HH:mm:ss.fff}] {line}{Environment.NewLine}"));

    private void SetStatus(string text, Brush brush)
    {
        StatusText.Text = text;
        StatusText.Foreground = brush;
    }
}
