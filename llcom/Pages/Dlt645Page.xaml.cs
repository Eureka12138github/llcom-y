using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using llcom.Services.Dlt645;

namespace llcom.Pages;

/// <summary>
/// DL/T 645-2007 组帧与应答解析页面。
/// Phase 1：向导式组帧、帧预览、发送/复制、粘贴应答解析。
/// </summary>
public partial class Dlt645Page : Page
{
    private const string DefaultAddress = "000000000001";

    private bool _initialized;
    private bool _updatingUi;
    private string _lastManualAddress = DefaultAddress;
    private string? _lastParsedAddress;

    public Dlt645Page()
    {
        InitializeComponent();
    }

    private Dlt645Operation? SelectedOperation =>
        (OperationComboBox.SelectedItem as ChoiceItem<Dlt645Operation>)?.Value;

    private int PreambleCount =>
        int.TryParse(PreambleComboBox.SelectedItem?.ToString(), out var value) ? value : 3;

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_initialized)
        {
            PopulateChoices();
            _initialized = true;
        }

        UpdateOperationState();
        RefreshPreview();
    }

    private void PopulateChoices()
    {
        _updatingUi = true;
        try
        {
            OperationComboBox.Items.Clear();
            foreach (var operation in Dlt645Catalog.Operations)
            {
                OperationComboBox.Items.Add(
                    new ChoiceItem<Dlt645Operation>(
                        operation,
                        $"{Localize(operation.NameKey, operation.Name)} (0x{operation.Control:X2})"
                    )
                );
            }
            var customOperation = new Dlt645Operation(
                "Dlt645OpCustom",
                "自定义",
                0x00
            );
            OperationComboBox.Items.Add(
                new ChoiceItem<Dlt645Operation>(
                    customOperation,
                    Localize(customOperation.NameKey, customOperation.Name)
                )
            );
            OperationComboBox.SelectedIndex = 0;

            DiComboBox.Items.Clear();
            foreach (var identifier in Dlt645Catalog.DataIdentifiers)
            {
                DiComboBox.Items.Add(
                    new ChoiceItem<Dlt645DataIdentifier>(
                        identifier,
                        $"{Localize(identifier.NameKey, identifier.Name)} ({identifier.Hex})"
                    )
                );
            }
            DiComboBox.SelectedIndex = 0;
            if (DiComboBox.SelectedItem is ChoiceItem<Dlt645DataIdentifier> firstIdentifier)
                DiHexTextBox.Text = firstIdentifier.Value.Hex;

            PreambleComboBox.Items.Clear();
            for (var i = 0; i <= 4; i++)
                PreambleComboBox.Items.Add(i.ToString());
            PreambleComboBox.SelectedItem = "3";
        }
        finally
        {
            _updatingUi = false;
        }
    }

    private void OperationComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _updatingUi)
            return;
        UpdateOperationState();
        RefreshPreview();
    }

    private void AddressTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        NormalizeAddress();
        e.Handled = true;
    }

    private void AddressTextBox_LostFocus(object sender, RoutedEventArgs e) => NormalizeAddress();

    /// <summary>输入 1 / 123 后补成 12 位；AA 通配只做大写处理，不补零。</summary>
    private void NormalizeAddress()
    {
        if (!AddressTextBox.IsEnabled)
            return;

        var text = (AddressTextBox.Text ?? "").Trim();
        if (text.Length == 0)
            return;

        if (
            Dlt645Codec.TryNormalizeAddress(text, out var normalized, out _)
            && !string.Equals(normalized, AddressTextBox.Text, StringComparison.Ordinal)
        )
            AddressTextBox.Text = normalized;
    }

    private void HexTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = !e.Text.All(Uri.IsHexDigit);

    private void HexTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (
            sender is not TextBox box
            || !e.DataObject.GetDataPresent(DataFormats.Text)
            || e.DataObject.GetData(DataFormats.Text) is not string text
            || text.Length == 0
            || !text.All(Uri.IsHexDigit)
            || box.Text.Length - box.SelectionLength + text.Length > box.MaxLength
        )
        {
            e.CancelCommand();
        }
    }

    private void Input_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_initialized || _updatingUi)
            return;
        RefreshPreview();
    }

    private void Input_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _updatingUi)
            return;
        RefreshPreview();
    }

    private void DiComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _updatingUi)
            return;

        if (DiComboBox.SelectedItem is ChoiceItem<Dlt645DataIdentifier> item)
        {
            _updatingUi = true;
            try
            {
                DiHexTextBox.Text = item.Value.Hex;
            }
            finally
            {
                _updatingUi = false;
            }
        }
        RefreshPreview();
    }

    private void DiHexTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initialized || _updatingUi)
            return;

        if (
            DiComboBox.SelectedItem is ChoiceItem<Dlt645DataIdentifier> item
            && !string.Equals(
                DiHexTextBox.Text.Trim(),
                item.Value.Hex,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            _updatingUi = true;
            try
            {
                DiComboBox.SelectedIndex = -1;
            }
            finally
            {
                _updatingUi = false;
            }
        }
        RefreshPreview();
    }

    /// <summary>切换操作时同步地址/DI/附加数据的可用状态。</summary>
    private void UpdateOperationState()
    {
        var operation = SelectedOperation;
        var isReadAddress = operation?.Control == Dlt645Codec.ControlReadAddress;
        var isCustom = operation?.Control == 0x00;

        _updatingUi = true;
        try
        {
            if (isReadAddress)
            {
                if (
                    !string.Equals(
                        AddressTextBox.Text,
                        Dlt645Codec.WildcardAddress,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                    _lastManualAddress = AddressTextBox.Text;
                AddressTextBox.Text = Dlt645Codec.WildcardAddress;
            }
            else if (
                string.Equals(
                    AddressTextBox.Text,
                    Dlt645Codec.WildcardAddress,
                    StringComparison.OrdinalIgnoreCase
                )
                && !string.IsNullOrEmpty(_lastManualAddress)
            )
            {
                AddressTextBox.Text = _lastManualAddress;
            }

            AddressTextBox.IsEnabled = !isReadAddress;
            AddressHint.Visibility = isReadAddress ? Visibility.Visible : Visibility.Collapsed;
            DiComboBox.IsEnabled = operation?.RequiresDataIdentifier == true;
            DiHexTextBox.IsEnabled = operation?.RequiresDataIdentifier == true;
            ExtraDataTextBox.IsEnabled = operation?.IsWrite == true || isCustom;
            ExtraDataLabel.Text = isCustom
                ? Localize("Dlt645CustomData", "数据域（HEX）")
                : Localize("Dlt645ExtraData", "附加数据（HEX）");
            ControlCodeLabel.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
            ControlCodeTextBox.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
            if (isCustom && string.IsNullOrWhiteSpace(ControlCodeTextBox.Text))
                ControlCodeTextBox.Text = "11";
        }
        finally
        {
            _updatingUi = false;
        }
    }

    private void RefreshPreview()
    {
        if (!TryBuildFrame(out var frame, out var operation, out var error))
        {
            FramePreviewTextBox.Text = "";
            SetStatus(FrameSummaryText, error, true);
            SendButton.IsEnabled = false;
            CopyButton.IsEnabled = false;
            return;
        }

        if (
            !Dlt645Codec.TryParse(frame, out var parsed, out var parseError)
            || parsed == null
        )
        {
            FramePreviewTextBox.Text = "";
            SetStatus(FrameSummaryText, parseError, true);
            SendButton.IsEnabled = false;
            CopyButton.IsEnabled = false;
            return;
        }

        FramePreviewTextBox.Text = Dlt645Codec.ToHex(frame);
        FrameSummaryText.Foreground =
            TryFindResource("Brush.Foreground.Muted") as Brush ?? Brushes.Gray;
        FrameSummaryText.Text =
            $"{Localize("Dlt645Address", "Address")}: {parsed.Address}"
            + $" | C=0x{parsed.Control:X2} {OperationName(operation)}"
            + $" | L={parsed.Data.Length}"
            + $" | CS=0x{parsed.Checksum:X2}"
            + $" | FE×{parsed.PreambleCount}\n"
            + $"{Localize("Dlt645DataDomain", "Data")}: {Dlt645Codec.ToHex(parsed.Data)}\n"
            + $"{Localize("Dlt645EncodedData", "Encoded")}: {Dlt645Codec.ToHex(parsed.EncodedData)}";

        SendButton.IsEnabled = true;
        CopyButton.IsEnabled = true;
    }

    private bool TryBuildFrame(
        out byte[] frame,
        out Dlt645Operation? operation,
        out string error
    )
    {
        frame = Array.Empty<byte>();
        operation = SelectedOperation;
        error = "";

        if (operation == null)
        {
            error = Localize("Dlt645SelectOperation", "请选择操作");
            return false;
        }

        var address = AddressTextBox.Text.Trim();
        var data = Array.Empty<byte>();
        var control = operation.Control;

        if (control == 0x00)
        {
            if (!TryGetCustomControl(out control, out error))
                return false;
            if (!TryGetExtraData(out data, out error))
                return false;
        }
        else
        {
            switch (control)
            {
                case Dlt645Codec.ControlReadAddress:
                    address = Dlt645Codec.WildcardAddress;
                    break;

                case Dlt645Codec.ControlReadData:
                    if (!TryGetDataIdentifier(out var readDi, out error))
                        return false;
                    data = readDi;
                    break;

                case Dlt645Codec.ControlWriteData:
                    if (!TryGetDataIdentifier(out var writeDi, out error))
                        return false;
                    if (!TryGetExtraData(out var extraData, out error))
                        return false;
                    data = Concat(writeDi, extraData);
                    break;

                default:
                    error = Localize("Dlt645UnsupportedOperation", "当前操作暂未实现");
                    return false;
            }
        }

        try
        {
            frame = Dlt645Codec.BuildFrame(address, control, data, PreambleCount);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private bool TryGetCustomControl(out byte control, out string error)
    {
        control = 0;
        error = "";

        if (
            !Dlt645Codec.TryParseHex(ControlCodeTextBox.Text, out var parsed, out _)
            || parsed.Length != 1
        )
        {
            error = Localize("Dlt645InvalidControlCode", "控制码必须是 1 字节 HEX");
            return false;
        }

        control = parsed[0];
        return true;
    }

    private bool TryGetDataIdentifier(out byte[] dataIdentifier, out string error)
    {
        dataIdentifier = Array.Empty<byte>();
        error = "";

        if (
            !Dlt645Codec.TryParseHex(DiHexTextBox.Text, out var parsed, out _)
            || parsed.Length != 4
        )
        {
            error = Localize("Dlt645InvalidDi", "DI 必须是 4 字节 HEX");
            return false;
        }

        dataIdentifier = parsed;
        return true;
    }

    private bool TryGetExtraData(out byte[] extraData, out string error)
    {
        extraData = Array.Empty<byte>();
        error = "";

        var text = (ExtraDataTextBox.Text ?? "").Trim();
        if (text.Length == 0)
            return true;

        if (!Dlt645Codec.TryParseHex(text, out var parsed, out _))
        {
            error = Localize("Dlt645InvalidExtraData", "附加数据不是合法 HEX");
            return false;
        }

        extraData = parsed;
        return true;
    }

    private void SendButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBuildFrame(out var frame, out var operation, out var error))
        {
            SetStatus(StatusText, error, true);
            return;
        }

        if (
            operation != null
            && (
                operation.Control == 0x00
                || operation.IsWrite
                || operation.NeedsConfirmation
            )
        )
        {
            var isCustom = operation.Control == 0x00;
            var confirm = Tools.InputDialog.OpenDialog(
                isCustom
                    ? Localize(
                        "Dlt645CustomConfirmPrompt",
                        "即将发送自定义控制码，请输入 YES 确认"
                    )
                    : Localize(
                        "Dlt645WriteConfirmPrompt",
                        "即将发送写命令，请输入 YES 确认"
                    ),
                "NO",
                Localize("Dlt645SendConfirm", "发送确认")
            );
            if (
                !confirm.Item1
                || !string.Equals(confirm.Item2.Trim(), "YES", StringComparison.OrdinalIgnoreCase)
            )
                return;
        }

        if (!Tools.Global.uart.IsOpen())
        {
            SetStatus(
                StatusText,
                Localize("ErrorPortNotOpen", "请先打开串口！"),
                true
            );
            return;
        }

        try
        {
            Tools.Global.uart.SendData(frame);
            SetStatus(
                StatusText,
                $"{Localize("Dlt645Sent", "已发送")}: {Dlt645Codec.ToHex(frame)}",
                false
            );
        }
        catch (Exception ex)
        {
            SetStatus(StatusText, ex.Message, true);
        }
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(FramePreviewTextBox.Text))
            return;

        try
        {
            Clipboard.SetText(FramePreviewTextBox.Text);
            SetStatus(
                StatusText,
                Localize("Dlt645Copied", "帧已复制"),
                false
            );
        }
        catch (Exception ex)
        {
            SetStatus(StatusText, ex.Message, true);
        }
    }

    private void ParseButton_Click(object sender, RoutedEventArgs e)
    {
        _lastParsedAddress = null;
        UseAddressButton.Visibility = Visibility.Collapsed;
        ParseResultTextBox.Text = "";

        if (!Dlt645Codec.TryParseHex(ResponseTextBox.Text, out var bytes, out var hexError))
        {
            SetStatus(ParseStatusText, hexError, true);
            return;
        }

        if (!Dlt645Codec.TryParse(bytes, out var frame, out var parseError) || frame == null)
        {
            SetStatus(ParseStatusText, parseError, true);
            return;
        }

        var builder = new StringBuilder();
        builder.AppendLine($"{Localize("Dlt645PreambleCount", "前导 FE")}: {frame.PreambleCount}");
        builder.AppendLine($"{Localize("Dlt645Address", "地址")}: {frame.Address}");
        builder.AppendLine(
            $"{Localize("Dlt645Control", "控制码")}: 0x{frame.Control:X2} ({ControlDescription(frame)})"
        );
        builder.AppendLine($"{Localize("Dlt645FunctionCode", "功能码")}: 0x{frame.FunctionCode:X2}");
        builder.AppendLine($"{Localize("Dlt645Length", "数据长度")}: {frame.Data.Length}");
        builder.AppendLine(
            $"{Localize("Dlt645DataDomain", "数据域")}: {Dlt645Codec.ToHex(frame.Data)}"
        );
        builder.AppendLine(
            $"{Localize("Dlt645EncodedData", "传输数据域")}: {Dlt645Codec.ToHex(frame.EncodedData)}"
        );
        builder.AppendLine(
            $"{Localize("Dlt645Checksum", "校验和")}: 0x{frame.Checksum:X2} ({Localize("Dlt645ChecksumOk", "校验通过")})"
        );

        if (frame.FunctionCode == Dlt645Codec.ControlReadAddress && frame.IsResponse && !frame.IsException)
        {
            if (
                Dlt645Codec.TryGetReadAddress(frame, out var address, out var addressError)
            )
            {
                _lastParsedAddress = address;
                builder.AppendLine($"{Localize("Dlt645ParsedAddress", "表地址")}: {address}");
                UseAddressButton.Visibility = Visibility.Visible;
            }
            else
            {
                builder.AppendLine($"{Localize("Dlt645ParseFailed", "解析失败")}: {addressError}");
            }
        }

        ParseResultTextBox.Text = builder.ToString();
        SetStatus(
            ParseStatusText,
            Localize("Dlt645ParseOk", "解析成功"),
            false
        );
    }

    private void UseAddressButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_lastParsedAddress))
            return;

        if (SelectedOperation?.Control == Dlt645Codec.ControlReadAddress)
            OperationComboBox.SelectedIndex = 0;

        AddressTextBox.Text = _lastParsedAddress;
        UpdateOperationState();
        RefreshPreview();
        SetStatus(
            StatusText,
            Localize("Dlt645AddressApplied", "已填入解析出的表地址"),
            false
        );
    }

    private string ControlDescription(Dlt645Frame frame)
    {
        var function = frame.FunctionCode switch
        {
            Dlt645Codec.ControlReadData => Localize("Dlt645OpReadData", "读数据"),
            Dlt645Codec.ControlReadAddress => Localize("Dlt645OpReadAddress", "读通信地址"),
            Dlt645Codec.ControlWriteData => Localize("Dlt645OpWriteData", "写数据"),
            _ => $"0x{frame.FunctionCode:X2}",
        };
        var direction = frame.IsResponse
            ? Localize("Dlt645Response", "应答")
            : Localize("Dlt645Request", "请求");
        var exception = frame.IsException
            ? $" / {Localize("Dlt645Exception", "异常")}"
            : "";
        return $"{function} / {direction}{exception}";
    }

    private string OperationName(Dlt645Operation? operation) =>
        operation == null ? "" : Localize(operation.NameKey, operation.Name);

    private string Localize(string key, string fallback) =>
        TryFindResource(key) as string ?? fallback;

    private static byte[] Concat(byte[] first, byte[] second)
    {
        var result = new byte[first.Length + second.Length];
        Array.Copy(first, 0, result, 0, first.Length);
        Array.Copy(second, 0, result, first.Length, second.Length);
        return result;
    }

    private void SetStatus(TextBlock target, string text, bool isError)
    {
        target.Text = text;
        target.Foreground =
            TryFindResource(isError ? "Brush.State.Error" : "Brush.Foreground.Muted") as Brush
            ?? Brushes.Gray;
    }

    /// <summary>下拉框显示项：保证可编辑 ComboBox 的 Text 是友好的显示文本。</summary>
    private sealed class ChoiceItem<T>
    {
        public ChoiceItem(T value, string text)
        {
            Value = value;
            Text = text;
        }

        public T Value { get; }

        public string Text { get; }

        public override string ToString() => Text;
    }
}
