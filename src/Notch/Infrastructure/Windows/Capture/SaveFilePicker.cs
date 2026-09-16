using System;
using System.Windows;
using Microsoft.Win32;
using Notch.Services.Screenshots;

namespace Notch.Infrastructure.Windows.Capture;

public sealed class SaveFilePicker(Func<Window?> getOwner) : ISaveFilePicker
{
    public string? ChoosePngPath(string suggestedName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Сохранить скриншот", FileName = suggestedName,
            Filter = "Изображение PNG (*.png)|*.png", DefaultExt = ".png", AddExtension = true, OverwritePrompt = true
        };
        return dialog.ShowDialog(getOwner()) == true ? dialog.FileName : null;
    }
}
