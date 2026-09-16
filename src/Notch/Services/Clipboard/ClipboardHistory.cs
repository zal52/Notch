using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Notch.Services.Contracts;

namespace Notch.Services.Clipboard;

public sealed class ClipboardHistory
{
    public const int MaximumTextLength = 65_536;
    private readonly List<ClipboardEntry> _entries = new();
    private readonly ReadOnlyCollection<ClipboardEntry> _readOnlyEntries;
    private readonly int _capacity;
    private readonly int _characterBudget;
    private readonly int _imageByteBudget;
    private int _characters;
    private long _imageBytes;
    public IReadOnlyList<ClipboardEntry> Entries => _readOnlyEntries;

    public ClipboardHistory(int capacity = 50, int characterBudget = 524_288, int imageByteBudget = 64 * 1024 * 1024)
    {
        if (capacity < 1 || characterBudget < 1 || imageByteBudget < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _characterBudget = characterBudget;
        _imageByteBudget = imageByteBudget;
        _readOnlyEntries = _entries.AsReadOnly();
    }

    public bool AddText(string text) => Add(new ClipboardContent.Text(text));

    public bool Add(ClipboardContent content)
    {
        switch (content)
        {
            case ClipboardContent.Text text:
                if (text.Value.Length == 0 || text.Value.Length > MaximumTextLength || text.Value.Length > _characterBudget) return false;
                if (_entries.Count > 0 && _entries[0].Content is ClipboardContent.Text latest && latest.Value == text.Value) return false;
                break;
            case ClipboardContent.Image image:
                if (!ClipboardImageLimits.ValidDimensions(image.Width, image.Height) || image.PngBytes.IsEmpty
                    || image.PngBytes.Length > ClipboardImageLimits.MaximumPngBytes || image.ThumbnailPng.Length > 1024 * 1024
                    || ImageBytes(image) > _imageByteBudget) return false;
                if (_entries.Count > 0 && _entries[0].Content is ClipboardContent.Image previous && ClipboardImageLimits.SameImage(image, previous)) return false;
                break;
            default: return false;
        }
        _entries.Insert(0, new ClipboardEntry(Guid.NewGuid(), DateTimeOffset.Now, content));
        Account(content, 1);
        while (_entries.Count > _capacity || _characters > _characterBudget || _imageBytes > _imageByteBudget)
        {
            Account(_entries[^1].Content, -1);
            _entries.RemoveAt(_entries.Count - 1);
        }
        return true;
    }

    private static long ImageBytes(ClipboardContent.Image image) => (long)image.PngBytes.Length + image.ThumbnailPng.Length;
    private void Account(ClipboardContent content, int direction)
    {
        if (content is ClipboardContent.Text text) _characters += direction * text.Value.Length;
        else if (content is ClipboardContent.Image image) _imageBytes += direction * ImageBytes(image);
    }

    public void Clear() { _entries.Clear(); _characters = 0; _imageBytes = 0; }
}
