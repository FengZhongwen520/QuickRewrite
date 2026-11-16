namespace QuickRewrite.Models;

public readonly record struct SelectionBounds(double Left, double Top, double Width, double Height);

public sealed record SelectionSnapshot(
    string Text,
    IntPtr TargetWindow,
    string WindowTitle,
    string ProcessName,
    SelectionBounds? Bounds = null);

public sealed record SelectionCaptureResult(
    bool Success,
    SelectionSnapshot? Snapshot,
    string? Error)
{
    public static SelectionCaptureResult Ok(SelectionSnapshot snapshot) => new(true, snapshot, null);
    public static SelectionCaptureResult Fail(string error) => new(false, null, error);
}
