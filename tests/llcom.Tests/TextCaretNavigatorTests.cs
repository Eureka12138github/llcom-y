using llcom.Tools;
using Xunit;

namespace llcom.Tests
{
    public class TextCaretNavigatorTests
    {
        [Fact]
        public void Down_MovesToSameColumn()
        {
            Assert.True(
                TextCaretNavigator.TryMoveVertical("abc\ndef", 1, true, out var caret, out var line)
            );
            Assert.Equal(5, caret);
            Assert.Equal(1, line);
        }

        [Fact]
        public void Up_MovesToSameColumn()
        {
            Assert.True(
                TextCaretNavigator.TryMoveVertical("abc\ndef", 5, false, out var caret, out var line)
            );
            Assert.Equal(1, caret);
            Assert.Equal(0, line);
        }

        [Fact]
        public void Down_TargetLineShorter_MovesToTargetLineStart()
        {
            Assert.True(
                TextCaretNavigator.TryMoveVertical("abcdef\nx", 4, true, out var caret, out var line)
            );
            Assert.Equal(7, caret);
            Assert.Equal(1, line);
        }

        [Fact]
        public void Up_TargetLineShorter_MovesToTargetLineStart()
        {
            Assert.True(
                TextCaretNavigator.TryMoveVertical("x\nabcdef", 6, false, out var caret, out var line)
            );
            Assert.Equal(0, caret);
            Assert.Equal(0, line);
        }

        [Fact]
        public void Up_OnFirstLine_MovesToTextStart()
        {
            Assert.True(
                TextCaretNavigator.TryMoveVertical("abc\ndef", 2, false, out var caret, out var line)
            );
            Assert.Equal(0, caret);
            Assert.Equal(0, line);
        }

        [Fact]
        public void Down_OnLastLine_MovesToTextEnd()
        {
            Assert.True(
                TextCaretNavigator.TryMoveVertical("abc\ndef", 5, true, out var caret, out var line)
            );
            Assert.Equal(7, caret);
            Assert.Equal(1, line);
        }

        [Fact]
        public void Down_TargetEmptyLine_MovesToThatLineStart()
        {
            Assert.True(
                TextCaretNavigator.TryMoveVertical("abc\n\nx", 1, true, out var caret, out var line)
            );
            Assert.Equal(4, caret);
            Assert.Equal(1, line);
        }

        [Fact]
        public void Up_FromEmptyLastLine_MovesToPreviousLineStart()
        {
            Assert.True(
                TextCaretNavigator.TryMoveVertical("abc\n", 4, false, out var caret, out var line)
            );
            Assert.Equal(0, caret);
            Assert.Equal(0, line);
        }

        [Fact]
        public void Down_FromLineEnd_WithTrailingNewline_MovesToEmptyLastLineStart()
        {
            Assert.True(
                TextCaretNavigator.TryMoveVertical("abc\n", 3, true, out var caret, out var line)
            );
            Assert.Equal(4, caret);
            Assert.Equal(1, line);
        }

        [Fact]
        public void CrLf_KeepsColumn()
        {
            Assert.True(
                TextCaretNavigator.TryMoveVertical("abc\r\ndef", 1, true, out var caret, out var line)
            );
            Assert.Equal(6, caret);
            Assert.Equal(1, line);

            Assert.True(
                TextCaretNavigator.TryMoveVertical("abc\r\ndef", 6, false, out caret, out line)
            );
            Assert.Equal(1, caret);
            Assert.Equal(0, line);
        }

        [Fact]
        public void NoTrailingNewline_DownOnLastLine_GoesToTextEnd()
        {
            Assert.True(
                TextCaretNavigator.TryMoveVertical("abc", 1, true, out var caret, out var line)
            );
            Assert.Equal(3, caret);
            Assert.Equal(0, line);
        }

        [Fact]
        public void EmptyText_UpAndDownStayAtStart()
        {
            Assert.True(
                TextCaretNavigator.TryMoveVertical("", 0, false, out var caret, out var line)
            );
            Assert.Equal(0, caret);
            Assert.Equal(0, line);

            Assert.True(
                TextCaretNavigator.TryMoveVertical("", 0, true, out caret, out line)
            );
            Assert.Equal(0, caret);
            Assert.Equal(0, line);
        }

        [Fact]
        public void CaretOutOfRange_IsClamped()
        {
            Assert.True(
                TextCaretNavigator.TryMoveVertical("abc\ndef", 99, true, out var caret, out var line)
            );
            Assert.Equal(7, caret);
            Assert.Equal(1, line);

            Assert.True(
                TextCaretNavigator.TryMoveVertical("abc\ndef", -5, false, out caret, out line)
            );
            Assert.Equal(0, caret);
            Assert.Equal(0, line);
        }
    }
}
