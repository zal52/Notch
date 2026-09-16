using Notch.Services.Clipboard;
using Notch.Services.Contracts;
using Xunit;

namespace Notch.Tests;

public sealed class ClipboardTests
{
    [Fact]
    public void HistoryDeduplicatesOnlyConsecutiveEntries()
    {
        var history = new ClipboardHistory();
        Assert.True(history.AddText("A"));
        Assert.False(history.AddText("A"));
        Assert.True(history.AddText("B"));
        Assert.True(history.AddText("A"));
        Assert.Equal(new[] { "A", "B", "A" }, history.Entries.Select(e => ((ClipboardContent.Text)e.Content).Value));
    }

    [Fact]
    public void HistoryRespectsCountAndMemoryBudgetsWithoutTruncating()
    {
        var history = new ClipboardHistory(capacity: 2, characterBudget: 7);
        history.AddText("abc");
        history.AddText("def");
        history.AddText("xyz");
        Assert.Equal(2, history.Entries.Count);
        history.AddText("1234567");
        Assert.Single(history.Entries);
        Assert.False(history.AddText("12345678"));
        Assert.False(history.AddText(new string('x', ClipboardHistory.MaximumTextLength + 1)));
        Assert.False(history.AddText(""));
        history.Clear();
        Assert.Empty(history.Entries);
        Assert.True(history.AddText("1234567"));
    }

    [Fact]
    public async Task MonitoringStartsWithoutReadingExistingClipboardAndStopsWithoutPolling()
    {
        var platform = new FakeTextClipboardPlatform();
        platform.Emit("pre-existing");
        await using var service = new ClipboardService(platform, new ClipboardHistory());
        await service.StartAsync(default);
        Assert.Equal(0, platform.Reads);
        Assert.Empty(service.History);
        platform.Emit("new text");
        await UntilAsync(() => service.History.Count == 1);
        await service.StopAsync(default);
        var reads = platform.Reads;
        platform.Emit("ignored while paused");
        await Task.Delay(40);
        Assert.Equal(reads, platform.Reads);
        await service.StartAsync(default);
        Assert.Equal(reads, platform.Reads);
    }

    [Fact]
    public async Task TemporarilyLockedClipboardRetriesAndKeepsExactUnicodeText()
    {
        var platform = new FakeTextClipboardPlatform { ReadFailures = 2 };
        await using var service = new ClipboardService(platform, new ClipboardHistory());
        await service.StartAsync(default);
        platform.Emit("  Привет 👋\r\n第二行  ");
        await UntilAsync(() => service.History.Count == 1);
        Assert.Equal(3, platform.Reads);
        Assert.Equal("  Привет 👋\r\n第二行  ", ((ClipboardContent.Text)service.History[0].Content).Value);
    }

    [Fact]
    public async Task RestoringAnOldEntryProducesOneNewEntryDespiteOwnNotification()
    {
        var platform = new FakeTextClipboardPlatform();
        await using var service = new ClipboardService(platform, new ClipboardHistory());
        await service.StartAsync(default);
        platform.Emit("first");
        await UntilAsync(() => service.History.Count == 1);
        platform.Emit("second");
        await UntilAsync(() => service.History.Count == 2);
        await service.WriteAsync(service.History[1].Content, default);
        await UntilAsync(() => service.History.Count == 3);
        Assert.Equal("first", platform.CurrentText);
        Assert.Equal(3, service.History.Count);
        Assert.Equal(1, platform.Writes);
    }

    [Fact]
    public async Task PermanentLockIsBoundedAndNextNotificationRecovers()
    {
        var platform = new FakeTextClipboardPlatform { ReadFailures = 10 };
        await using var service = new ClipboardService(platform, new ClipboardHistory());
        await service.StartAsync(default);
        platform.Emit("locked");
        await UntilAsync(() => service.StatusMessage is not null);
        Assert.Equal(5, platform.Reads);
        Assert.Empty(service.History);
        platform.ReadFailures = 0;
        platform.Emit("recovered");
        await UntilAsync(() => service.History.Count == 1);
        Assert.Null(service.StatusMessage);
    }

    [Fact]
    public async Task StopCancelsPendingRetryAndClearDoesNotRewriteSystemClipboard()
    {
        var platform = new FakeTextClipboardPlatform { ReadFailures = 10 };
        await using var service = new ClipboardService(platform, new ClipboardHistory());
        await service.StartAsync(default);
        platform.Emit("pending");
        await UntilAsync(() => platform.Reads == 1);
        await service.StopAsync(default);
        Assert.False(service.IsMonitoring);
        Assert.Empty(service.History);
        service.ClearHistory();
        Assert.Equal("pending", platform.CurrentText);
        Assert.Equal(0, platform.Writes);
    }

    [Fact]
    public async Task ClearPreventsAnEarlierPendingReadFromRepopulatingHistory()
    {
        var platform = new FakeTextClipboardPlatform { ReadFailures = 1 };
        await using var service = new ClipboardService(platform, new ClipboardHistory());
        await service.StartAsync(default);
        platform.Emit("old pending text");
        await UntilAsync(() => platform.Reads == 1);
        service.ClearHistory();
        await UntilAsync(() => platform.Reads == 2);
        Assert.Empty(service.History);
        platform.Emit("new text");
        await UntilAsync(() => service.History.Count == 1);
        Assert.Equal("new text", ((ClipboardContent.Text)service.History[0].Content).Value);
    }

    internal static async Task UntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!predicate()) await Task.Delay(5, timeout.Token);
    }
}
