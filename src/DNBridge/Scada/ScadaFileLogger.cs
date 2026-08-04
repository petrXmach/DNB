using DNBridge.Events;

namespace DNBridge.Scada;

/// <summary>
/// Appends SCADA traffic to disk in the same format shown in the WPF "SCADA Traffic"
/// detail pane (<see cref="TrafficFormat.ScadaMaster"/> / <see cref="TrafficFormat.ScadaDetailLine"/>).
/// Enabled by setting <c>[Scada] Scada_log</c> to a folder; one file per calendar date,
/// named <c>scada_MMdd.log</c>, rotated automatically if a session crosses midnight.
/// </summary>
public sealed class ScadaFileLogger : IDisposable
{
    private readonly string? _folder;
    private readonly object _lock = new();
    private StreamWriter? _writer;
    private DateTime _writerDate;

    public ScadaFileLogger(string? folder) =>
        _folder = string.IsNullOrWhiteSpace(folder) ? null : folder;

    public bool Enabled => _folder != null;

    /// <summary>Writes a blank line + "*** App started &lt;datetime&gt; ***" to today's file.</summary>
    public void LogStart()
    {
        if (!Enabled)
            return;

        Write($"{Environment.NewLine}*** App started {DateTime.Now:yyyy-MM-dd HH:mm:ss} ***");
    }

    /// <summary>Writes one traffic item — master line, then detail rows (if any).</summary>
    public void LogTraffic(object? sender, ScadaDataEventArgs e)
    {
        if (!Enabled)
            return;

        Write(string.IsNullOrEmpty(e.Detail) ? e.Summary : $"{e.Summary}{Environment.NewLine}{e.Detail}");
    }

    private void Write(string text)
    {
        lock (_lock)
        {
            var today = DateTime.Now.Date;
            if (_writer == null || today != _writerDate)
            {
                _writer?.Dispose();
                Directory.CreateDirectory(_folder!);
                var path = Path.Combine(_folder!, $"scada_{today:MMdd}.log");
                _writer = new StreamWriter(path, append: true) { AutoFlush = true };
                _writerDate = today;
            }

            _writer.WriteLine(text);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
