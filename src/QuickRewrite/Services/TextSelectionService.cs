using System.Globalization;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace QuickRewrite.Services;

internal static class TextSelectionService
{
    public static bool TrySelectPreviousText(string text)
    {
        var characterCount = StringInfo.ParseCombiningCharacters(text).Length;
        if (characterCount <= 0)
        {
            return true;
        }

        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is null ||
                !focused.TryGetCurrentPattern(TextPattern.Pattern, out var patternObject) ||
                patternObject is not TextPattern textPattern)
            {
                return false;
            }

            var ranges = textPattern.GetSelection();
            var previousSelection = ranges.Length > 0 ? ranges[0].Clone() : null;
            var range = ranges.Length > 0 ? ranges[0] : textPattern.DocumentRange;

            // The caret is at the end after pasting. Collapse to that end, then
            // move only the start endpoint backwards to select the inserted text.
            range.MoveEndpointByRange(
                TextPatternRangeEndpoint.Start,
                range,
                TextPatternRangeEndpoint.End);

            var moved = range.MoveEndpointByUnit(
                TextPatternRangeEndpoint.Start,
                TextUnit.Character,
                -characterCount);

            if (moved == 0 || !TextEquals(range.GetText(-1), text))
            {
                previousSelection?.Select();
                return false;
            }

            range.Select();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TextEquals(string? actual, string expected)
    {
        if (actual is null)
        {
            return false;
        }

        return string.Equals(NormalizeNewlines(actual), NormalizeNewlines(expected), StringComparison.Ordinal);
    }

    private static string NormalizeNewlines(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }
}
