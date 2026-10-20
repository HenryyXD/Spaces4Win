using System.IO;
using Spaces4Win.Services;

namespace Spaces4Win.Tests;

public class DiagnosticLogTests
{
    [Fact]
    public void Format_includes_exception_and_recent_activity()
    {
        var text = DiagnosticLogWriter.Format(
            "ERR",
            "hide failed",
            new InvalidOperationException("boom"),
            new[] { "13:00:00.000 switch DISPLAY1 -> 2" });

        Assert.Contains("ERR", text);
        Assert.Contains("hide failed", text);
        Assert.Contains("InvalidOperationException", text);
        Assert.Contains("boom", text);
        Assert.Contains("recent:", text);
        Assert.Contains("switch DISPLAY1 -> 2", text);
    }

    [Fact]
    public void Append_rotates_when_the_file_exceeds_the_limit()
    {
        var dir = Path.Combine(Path.GetTempPath(), "s4w-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "spaces4win.log");
            var writer = new DiagnosticLogWriter(path, maxBytes: 80, archives: 2);
            writer.Append(new string('a', 60));
            writer.Append(new string('b', 60));
            writer.Append(new string('c', 60));

            Assert.True(File.Exists(path));
            Assert.True(File.Exists(path + ".1"));
            Assert.Contains("c", File.ReadAllText(path));
            Assert.Contains("b", File.ReadAllText(path + ".1"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Session_marker_roundtrip_reports_an_unclean_previous_session()
    {
        var dir = Path.Combine(Path.GetTempPath(), "s4w-marker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "session.pid");
            Assert.Null(DiagnosticLogWriter.ReadMarker(path));

            DiagnosticLogWriter.WriteMarker(path, "pid=42 started=then");
            Assert.Equal("pid=42 started=then", DiagnosticLogWriter.ReadMarker(path));

            DiagnosticLogWriter.ClearMarker(path);
            Assert.Null(DiagnosticLogWriter.ReadMarker(path));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
