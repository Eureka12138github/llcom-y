using System.Linq;
using llcom.Services.Dlt645;
using Xunit;

namespace llcom.Tests
{
    public class Dlt645FrameInterpreterTests
    {
        private static Dlt645Frame Build(byte control, params byte[] data) =>
            Dlt645Codec.Parse(
                Dlt645Codec.BuildFrame("000000000001", control, data, 0)
            );

        private static byte[] Hex(string text)
        {
            Assert.True(Dlt645Codec.TryParseHex(text, out var bytes, out var error), error);
            return bytes;
        }

        [Theory]
        [InlineData(0x08, "Dlt645OpBroadcastTime", "广播校时")]
        [InlineData(0x11, "Dlt645OpReadData", "读数据")]
        [InlineData(0x13, "Dlt645OpReadAddress", "读通信地址")]
        [InlineData(0x14, "Dlt645OpWriteData", "写数据")]
        [InlineData(0x15, "Dlt645OpWriteAddress", "写通信地址")]
        [InlineData(0x16, "Dlt645OpFreeze", "冻结命令")]
        [InlineData(0x17, "Dlt645OpChangeBaud", "更改通信速率")]
        [InlineData(0x18, "Dlt645OpChangePassword", "修改密码")]
        [InlineData(0x19, "Dlt645OpMaxDemandReset", "最大需量清零")]
        [InlineData(0x1A, "Dlt645OpMeterReset", "电表清零")]
        [InlineData(0x1B, "Dlt645OpEventReset", "事件清零")]
        public void FunctionNames_CoverCommonCommands(
            byte functionCode,
            string expectedKey,
            string expectedFallback
        )
        {
            Assert.True(
                Dlt645FrameInterpreter.TryGetFunctionName(
                    functionCode,
                    out var key,
                    out var fallback
                )
            );
            Assert.Equal(expectedKey, key);
            Assert.Equal(expectedFallback, fallback);
        }

        [Fact]
        public void ReadFollowUpFunction_IsNotHandled()
        {
            Assert.False(
                Dlt645FrameInterpreter.TryGetFunctionName(0x12, out _, out _)
            );
        }

        [Fact]
        public void CatalogOperations_AllHaveFunctionNames()
        {
            foreach (var operation in Dlt645Catalog.Operations)
            {
                Assert.True(
                    Dlt645FrameInterpreter.TryGetFunctionName(
                        operation.Control,
                        out _,
                        out _
                    ),
                    $"缺少功能码名称：0x{operation.Control:X2}"
                );
            }
        }

        [Fact]
        public void ExceptionWord_IsDecodedBitByBit()
        {
            var interpretation = Dlt645FrameInterpreter.Interpret(Build(0xD4, 0x46));

            Assert.Equal((byte)0x46, interpretation.ErrorWord);
            Assert.Equal(3, interpretation.ErrorBits.Count);
            Assert.Contains(
                interpretation.ErrorBits,
                bit => bit.Key == "Dlt645ErrPassword"
            );
            Assert.Contains(
                interpretation.ErrorBits,
                bit => bit.Key == "Dlt645ErrNoData"
            );
            Assert.Contains(
                interpretation.ErrorBits,
                bit => bit.Key == "Dlt645ErrRateCount"
            );
        }

        [Fact]
        public void ReadDataResponse_SplitsDiAndData()
        {
            var interpretation = Dlt645FrameInterpreter.Interpret(
                Build(0x91, 0x00, 0x01, 0x01, 0x02, 0x34, 0x12)
            );

            Assert.Contains(
                interpretation.Fields,
                field =>
                    field.NameKey == "Dlt645DataIdentifier"
                    && field.Value == "02010100"
            );
            Assert.Contains(
                interpretation.Fields,
                field => field.NameKey == "Dlt645Data" && field.Value == "12 34"
            );
        }

        [Fact]
        public void ParseWithoutDecode_KeepsAlreadyReducedData()
        {
            // 主日志勾选「减33H」后复制出来的帧：数据域已经是减 33H 的结果，
            // 解析时不应再减一次（否则 DI 会变成 D1CDCECE）。
            var raw = Hex(
                "FE FE FE FE 68 02 00 00 00 00 00 68 91 08 01 01 00 04 03 11 11 26 BC 16"
            );

            Assert.True(
                Dlt645Codec.TryParse(raw, out var frame, out var error, decodeData: false),
                error
            );
            Assert.NotNull(frame);
            Assert.Equal("01 01 00 04 03 11 11 26", Dlt645Codec.ToHex(frame!.Data));

            var interpretation = Dlt645FrameInterpreter.Interpret(frame);
            Assert.Contains(
                interpretation.Fields,
                field =>
                    field.NameKey == "Dlt645DataIdentifier" && field.Value == "04000101"
            );
            Assert.Contains(
                interpretation.Fields,
                field =>
                    field.NameKey == "Dlt645Data" && field.Value == "26 11 11 03"
            );
        }

        [Fact]
        public void WriteDataRequest_SplitsDiPasswordOperatorAndData()
        {
            var interpretation = Dlt645FrameInterpreter.Interpret(
                Build(
                    0x14,
                    0x00,
                    0x01,
                    0x01,
                    0x02,
                    0x02,
                    0x00,
                    0x00,
                    0x00,
                    0x78,
                    0x56,
                    0x34,
                    0x12,
                    0x01,
                    0x30
                )
            );

            Assert.Contains(
                interpretation.Fields,
                field =>
                    field.NameKey == "Dlt645DataIdentifier"
                    && field.Value == "02010100"
            );
            Assert.Contains(
                interpretation.Fields,
                field =>
                    field.NameKey == "Dlt645PasswordPermission"
                    && field.Value == "0x02"
            );
            Assert.Contains(
                interpretation.Fields,
                field => field.NameKey == "Dlt645Password" && field.Value == "00 00 00"
            );
            Assert.Contains(
                interpretation.Fields,
                field =>
                    field.NameKey == "Dlt645OperatorCode"
                    && field.Value == "78 56 34 12"
            );
            Assert.Contains(
                interpretation.Fields,
                field => field.NameKey == "Dlt645Data" && field.Value == "01 30"
            );
        }

        [Fact]
        public void BroadcastTime_FormatsBcdDateTime()
        {
            var interpretation = Dlt645FrameInterpreter.Interpret(
                Build(0x08, 0x30, 0x45, 0x12, 0x25, 0x11, 0x26)
            );

            Assert.Contains(
                interpretation.Fields,
                field =>
                    field.NameKey == "Dlt645Time"
                    && field.Value == "2026-11-25 12:45:30"
            );
        }

        [Fact]
        public void FreezeTime_FormatsBcdMonthDayTime()
        {
            var interpretation = Dlt645FrameInterpreter.Interpret(
                Build(0x16, 0x30, 0x12, 0x25, 0x11)
            );

            Assert.Contains(
                interpretation.Fields,
                field =>
                    field.NameKey == "Dlt645FreezeTime"
                    && field.Value == "11-25 12:30"
            );
        }

        [Fact]
        public void WriteAddressRequest_FormatsNewAddress()
        {
            var interpretation = Dlt645FrameInterpreter.Interpret(
                Build(0x15, 0x03, 0x02, 0x01, 0x00, 0x00, 0x00)
            );

            Assert.Contains(
                interpretation.Fields,
                field =>
                    field.NameKey == "Dlt645NewAddress"
                    && field.Value == "000000010203"
            );
        }

        [Fact]
        public void ResetCommand_SplitsPasswordAndOperator()
        {
            var interpretation = Dlt645FrameInterpreter.Interpret(
                Build(0x1A, 0x02, 0x01, 0x02, 0x03, 0x78, 0x56, 0x34, 0x12)
            );

            Assert.Contains(
                interpretation.Fields,
                field =>
                    field.NameKey == "Dlt645PasswordPermission"
                    && field.Value == "0x02"
            );
            Assert.Contains(
                interpretation.Fields,
                field => field.NameKey == "Dlt645Password" && field.Value == "01 02 03"
            );
            Assert.Contains(
                interpretation.Fields,
                field =>
                    field.NameKey == "Dlt645OperatorCode"
                    && field.Value == "78 56 34 12"
            );
        }

        [Fact]
        public void OverlongReadData_AddsWarning()
        {
            var data = Enumerable
                .Repeat((byte)0x01, 197)
                .Prepend((byte)0x00)
                .Prepend((byte)0x01)
                .Prepend((byte)0x01)
                .Prepend((byte)0x02)
                .ToArray();

            var interpretation = Dlt645FrameInterpreter.Interpret(Build(0x91, data));

            Assert.Contains(
                interpretation.Warnings,
                warning => warning.Key == "Dlt645WarnReadLength"
            );
        }

        [Fact]
        public void ExceptionWithWrongDataLength_AddsWarning()
        {
            var interpretation = Dlt645FrameInterpreter.Interpret(
                Build(0xD1, 0x01, 0x02)
            );

            Assert.Equal((byte)0x01, interpretation.ErrorWord);
            Assert.Contains(
                interpretation.Warnings,
                warning => warning.Key == "Dlt645WarnExceptionLength"
            );
        }
    }
}
