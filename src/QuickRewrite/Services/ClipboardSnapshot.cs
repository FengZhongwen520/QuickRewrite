using System.Windows;

namespace QuickRewrite.Services;

public sealed class ClipboardSnapshot
{
    private readonly List<(string Format, object Data)> _items;
    private readonly bool _hadData;
    private readonly bool _captureSucceeded;

    private ClipboardSnapshot(List<(string Format, object Data)> items, bool hadData, bool captureSucceeded)
    {
        _items = items;
        _hadData = hadData;
        _captureSucceeded = captureSucceeded;
    }

    public static bool TrySetText(string text, int attempts = 4)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch
            {
                if (attempt < attempts - 1)
                {
                    Thread.Sleep(25 * (attempt + 1));
                }
            }
        }

        return false;
    }

    public static ClipboardSnapshot Capture()
    {
        var items = new List<(string Format, object Data)>();

        try
        {
            var dataObject = Clipboard.GetDataObject();
            if (dataObject is null)
            {
                return new ClipboardSnapshot(items, hadData: false, captureSucceeded: true);
            }

            foreach (var format in dataObject.GetFormats(autoConvert: false).Distinct(StringComparer.Ordinal))
            {
                try
                {
                    var data = dataObject.GetData(format, autoConvert: false);
                    if (data is not null)
                    {
                        items.Add((format, data!));
                    }
                }
                catch
                {
                    // Some clipboard providers advertise delayed formats that cannot be materialized.
                }
            }

            return new ClipboardSnapshot(items, hadData: true, captureSucceeded: true);
        }
        catch
        {
            return new ClipboardSnapshot(items, hadData: false, captureSucceeded: false);
        }
    }

    public void Restore()
    {
        try
        {
            if (_items.Count == 0)
            {
                if (_captureSucceeded && !_hadData)
                {
                    Clipboard.Clear();
                }

                return;
            }

            var dataObject = new DataObject();
            foreach (var item in _items)
            {
                dataObject.SetData(item.Format, item.Data);
            }

            Clipboard.SetDataObject(dataObject, copy: true);
        }
        catch
        {
            // Best effort only. Never let clipboard restoration break the rewrite flow.
        }
    }
}
