using llcom.Services;
using Xunit;

namespace llcom.Tests
{
    public class ExpressionEvaluatorTests
    {
        private static double Eval(string expression)
        {
            Assert.True(
                ExpressionEvaluator.TryEvaluate(expression, out var value, out var error),
                error.ToString()
            );
            return value;
        }

        [Theory]
        [InlineData("1+2*3", 7)]
        [InlineData("(1+2)*3", 9)]
        [InlineData("10/4", 2.5)]
        [InlineData("1.5+2.25", 3.75)]
        [InlineData("7%4", 3)]
        [InlineData("2^3^2", 512)]
        [InlineData("-2^2", -4)]
        [InlineData("2^-2", 0.25)]
        [InlineData("1 + 2 * (3 - 4) / 2", 0)]
        [InlineData("3×4", 12)]
        [InlineData("8÷2", 4)]
        public void TryEvaluate_ComputesWithPrecedence(string expression, double expected)
        {
            Assert.Equal(expected, Eval(expression), 10);
        }

        [Fact]
        public void TryEvaluate_OneThirdIsApproximatelyCorrect()
        {
            Assert.Equal(1.0 / 3.0, Eval("1/3"), 12);
        }

        [Theory]
        [InlineData("1+")]
        [InlineData("abc")]
        [InlineData("1..2")]
        [InlineData("((1+2)")]
        [InlineData("1+2)")]
        public void TryEvaluate_RejectsInvalidSyntax(string expression)
        {
            Assert.False(
                ExpressionEvaluator.TryEvaluate(
                    expression,
                    out _,
                    out var error
                )
            );
            Assert.Equal(ExpressionError.Syntax, error);
        }

        [Theory]
        [InlineData("1/0")]
        [InlineData("5%(2-2)")]
        public void TryEvaluate_RejectsDivisionByZero(string expression)
        {
            Assert.False(
                ExpressionEvaluator.TryEvaluate(
                    expression,
                    out _,
                    out var error
                )
            );
            Assert.Equal(ExpressionError.DivideByZero, error);
        }

        [Fact]
        public void TryEvaluate_EmptyIsNeutral()
        {
            Assert.False(ExpressionEvaluator.TryEvaluate("  ", out _, out var error));
            Assert.Equal(ExpressionError.None, error);
        }
    }
}