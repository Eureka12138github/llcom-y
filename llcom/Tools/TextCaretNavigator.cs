using System;
using System.Collections.Generic;

namespace llcom.Tools;

/// <summary>
/// 多行输入框的纵向光标移动计算。
/// 按逻辑行（而不是 TextWrapping 产生的视觉行）计算目标位置，
/// 与 WPF TextBox 的 CaretIndex 配合使用。
/// </summary>
internal static class TextCaretNavigator
{
    /// <summary>一行文本的起始偏移与长度（长度不含行尾换行符）。</summary>
    private readonly struct LineInfo
    {
        public LineInfo(int start, int length)
        {
            Start = start;
            Length = length;
        }

        public int Start { get; }
        public int Length { get; }
    }

    /// <summary>
    /// 计算上下箭头移动后的光标位置。
    /// </summary>
    /// <param name="text">输入框完整文本。</param>
    /// <param name="caretIndex">当前光标偏移。</param>
    /// <param name="moveDown">true=向下，false=向上。</param>
    /// <param name="newCaretIndex">新的光标偏移。</param>
    /// <param name="targetLine">目标逻辑行号（供 ScrollToLine 使用）。</param>
    /// <returns>总是返回 true；保留返回值便于后续扩展“无法移动”的情况。</returns>
    public static bool TryMoveVertical(
        string text,
        int caretIndex,
        bool moveDown,
        out int newCaretIndex,
        out int targetLine
    )
    {
        text ??= "";
        caretIndex = Math.Max(0, Math.Min(caretIndex, text.Length));

        var lines = SplitLines(text);
        var lineIndex = FindLine(lines, caretIndex);
        var current = lines[lineIndex];
        var column = Math.Max(0, Math.Min(caretIndex - current.Start, current.Length));

        if (!moveDown)
        {
            // 第一行再向上：到整个文本开头
            if (lineIndex == 0)
            {
                newCaretIndex = 0;
                targetLine = 0;
                return true;
            }

            var target = lines[lineIndex - 1];
            // 目标行为空或其长度小于当前列：到目标行行首
            newCaretIndex = target.Length == 0 || target.Length < column
                ? target.Start
                : target.Start + column;
            targetLine = lineIndex - 1;
            return true;
        }

        // 最后一行再向下：到整个文本末尾（最后一行行尾）
        if (lineIndex == lines.Count - 1)
        {
            newCaretIndex = text.Length;
            targetLine = lineIndex;
            return true;
        }

        var next = lines[lineIndex + 1];
        // 目标行为空或其长度小于当前列：到目标行行首
        newCaretIndex = next.Length == 0 || next.Length < column
            ? next.Start
            : next.Start + column;
        targetLine = lineIndex + 1;
        return true;
    }

    /// <summary>
    /// 按 \r\n、\r、\n 拆分逻辑行。文本以换行结尾时，会保留最后一个空行，
    /// 这样从最后一行按向下可以落到文本末尾。
    /// </summary>
    private static List<LineInfo> SplitLines(string text)
    {
        var lines = new List<LineInfo>();
        var lineStart = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                lines.Add(new LineInfo(lineStart, i - lineStart));
                if (i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                lineStart = i + 1;
            }
            else if (text[i] == '\n')
            {
                lines.Add(new LineInfo(lineStart, i - lineStart));
                lineStart = i + 1;
            }
        }

        lines.Add(new LineInfo(lineStart, text.Length - lineStart));
        return lines;
    }

    /// <summary>
    /// 找到光标所在逻辑行。光标恰好落在下一行行首时归入下一行；
    /// 落在 CRLF 中间时按上一行行尾处理。
    /// </summary>
    private static int FindLine(List<LineInfo> lines, int caretIndex)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var nextStart = i + 1 < lines.Count ? lines[i + 1].Start : int.MaxValue;
            if (caretIndex < nextStart)
                return i;
        }

        return lines.Count - 1;
    }
}
