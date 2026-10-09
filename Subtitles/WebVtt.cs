using System.Text.RegularExpressions;

namespace MediaPager.App.Core.Subtitles;

/// <summary>
/// SRT → WebVTT conversion for browser subtitle tracks. The plugin SDK returns native subtitle
/// content; hosts render it as VTT so HTML5 <track> elements work. Mirrors the legacy converter:
/// strip the BOM, normalize newlines, switch the millisecond separator from ',' to '.'.
/// </summary>
public static partial class WebVtt
{
    [GeneratedRegex(@"(\d{2}:\d{2}:\d{2}),(\d{3})")]
    private static partial Regex MillisecondsSeparator();

    public static string ConvertSrt(string srt) =>
        "WEBVTT\n\n" + MillisecondsSeparator().Replace(
            srt.Replace("\r\n", "\n").Replace("\r", "\n").TrimStart('\uFEFF'),
            "$1.$2");
}