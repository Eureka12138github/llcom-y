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
    private Dlt645Session? _session;
    private List<Dlt645DataIdentifier> _allIdentifiers = new List<Dlt645DataIdentifier>();

    public Dlt645Page()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) => Dlt645ReceiveOptions.IsToolActive = IsVisible;
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
            LoadIdentifiers();
            SetAddressState(
                "Brush.Surface.Muted",
                Localize("Dlt645ReadAddressButton", "读取表地址")
            );
        }

        Dlt645ReceiveOptions.IsToolActive = IsVisible;
        DecodeDataCheckBox.IsChecked = Dlt645ReceiveOptions.DecodeData;
        StartSession();
        UpdateOperationState();
        RefreshPreview();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        Dlt645ReceiveOptions.IsToolActive = false;
        _session?.Dispose();
        _session = null;
    }

    private void StartSession()
    {
        if (_session != null)
            return;
        _session = new Dlt645Session();
        _session.Start();
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

    private void LoadIdentifiers()
    {
        var currentHex = DiHexTextBox.Text.Trim();
        _allIdentifiers = Dlt645IdentifierStore.LoadAll();
        RefreshDiCategories();
        RefreshDiChoices(currentHex);
    }

    private void RefreshDiCategories()
    {
        var current = (DiCategoryComboBox.SelectedItem as ChoiceItem<string>)?.Value ?? "";

        _updatingUi = true;
        try
        {
            DiCategoryComboBox.Items.Clear();
            DiCategoryComboBox.Items.Add(
                new ChoiceItem<string>("", Localize("Dlt645DiAllCategories", "全部分类"))
            );
            foreach (
                var category in _allIdentifiers
                    .Select(item => item.Category)
                    .Where(category => !string.IsNullOrWhiteSpace(category))
                    .Distinct(StringComparer.CurrentCultureIgnoreCase)
                    .OrderBy(category => category, StringComparer.CurrentCultureIgnoreCase)
            )
            {
                DiCategoryComboBox.Items.Add(new ChoiceItem<string>(category, category));
            }

            DiCategoryComboBox.SelectedItem =
                DiCategoryComboBox
                    .Items.OfType<ChoiceItem<string>>()
                    .FirstOrDefault(item =>
                        string.Equals(item.Value, current, StringComparison.OrdinalIgnoreCase)
                    ) ?? DiCategoryComboBox.Items[0];
        }
        finally
        {
            _updatingUi = false;
        }
    }

    private void RefreshDiChoices(string? preferredHex = null)
    {
        var search = (DiSearchTextBox.Text ?? "").Trim();
        var category = (DiCategoryComboBox.SelectedItem as ChoiceItem<string>)?.Value ?? "";
        var selectedHex =
            preferredHex
            ?? (DiComboBox.SelectedItem as ChoiceItem<Dlt645DataIdentifier>)?.Value.Hex
            ?? DiHexTextBox.Text.Trim();

        var filtered = _allIdentifiers
            .Where(item =>
                (category.Length == 0
                    || string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase))
                && (
                    search.Length == 0
                    || item.Name.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0
                    || item.Hex.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                    || (item.Category ?? "").IndexOf(
                            search,
                            StringComparison.CurrentCultureIgnoreCase
                        )
                        >= 0
                )
            )
            .ToList();

        _updatingUi = true;
        try
        {
            DiComboBox.Items.Clear();
            foreach (var identifier in filtered)
            {
                DiComboBox.Items.Add(
                    new ChoiceItem<Dlt645DataIdentifier>(
                        identifier,
                        Localize(identifier.NameKey, identifier.Name)
                    )
                );
            }

            var match = DiComboBox
                .Items.OfType<ChoiceItem<Dlt645DataIdentifier>>()
                .FirstOrDefault(item =>
                    string.Equals(item.Value.Hex, selectedHex, StringComparison.OrdinalIgnoreCase)
                );
            if (match != null)
            {
                DiComboBox.SelectedItem = match;
            }
            else if (
                string.IsNullOrWhiteSpace(selectedHex)
                && DiComboBox.Items.Count > 0
            )
            {
                DiComboBox.SelectedIndex = 0;
                if (DiComboBox.SelectedItem is ChoiceItem<Dlt645DataIdentifier> first)
                    DiHexTextBox.Text = first.Value.Hex;
            }
        }
        finally
        {
            _updatingUi = false;
        }
    }

    private void DiFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _updatingUi)
            return;
        RefreshDiChoices();
    }

    private void ManageDiButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new View.Dlt645IdentifierManagerWindow
        {
            Owner = Window.GetWindow(this),
        };
        if (window.ShowDialog() == true)
            LoadIdentifiers();
    }

    private void DeleteSelectedDi_Click(object sender, RoutedEventArgs e)
    {
        var code =
            (DiComboBox.SelectedItem as ChoiceItem<Dlt645DataIdentifier>)?.Value.Hex
            ?? DiHexTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(code))
            return;

        var result = System.Windows.MessageBox.Show(
            string.Format(
                Localize("Dlt645DeleteDiConfirm", "确定删除 DI {0} 吗？"),
                code
            ),
            Localize("Dlt645DeleteDi", "删除 DI"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning
        );
        if (result != MessageBoxResult.Yes)
            return;

        if (!Dlt645IdentifierStore.Delete(code))
        {
            SetStatus(
                StatusText,
                Localize("Dlt645DiNotFound", "没有找到要删除的 DI"),
                true
            );
            return;
        }

        DiHexTextBox.Text = "";
        LoadIdentifiers();
        SetStatus(StatusText, Localize("Dlt645DiDeleted", "DI 已删除"), false);
    }

    private void DecodeDataCheckBox_Changed(object sender, RoutedEventArgs e) =>
        Dlt645ReceiveOptions.DecodeData = DecodeDataCheckBox.IsChecked == true;

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
            var showExtra = operation?.IsWrite == true || isCustom;
            ExtraDataLabel.Visibility = showExtra ? Visibility.Visible : Visibility.Collapsed;
            ExtraDataTextBox.Visibility = showExtra ? Visibility.Visible : Visibility.Collapsed;
            ExtraDataTextBox.IsEnabled = showExtra;
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

    private async void ReadAddressButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null)
            return;

        if (!Tools.Global.uart.IsOpen())
        {
            SetAddressState(
                "Brush.State.Error",
                Localize("ErrorPortNotOpen", "请先打开串口！")
            );
            return;
        }

        ReadAddressButton.IsEnabled = false;
        SetAddressState(
            "Brush.State.Info",
            Localize("Dlt645ReadingAddress", "正在读取表地址…")
        );

        try
        {
            var responses = await _session.RequestAsync(
                Dlt645Codec.BuildReadAddressRequest(PreambleCount),
                frame =>
                    frame.FunctionCode == Dlt645Codec.ControlReadAddress
                    && frame.IsResponse
                    && !frame.IsException
                    && frame.Data.Length == Dlt645Codec.AddressLength,
                1500
            );

            var addresses = new List<string>();
            foreach (var frame in responses)
            {
                if (
                    Dlt645Codec.TryGetReadAddress(frame, out var address, out _)
                    && !addresses.Contains(address, StringComparer.OrdinalIgnoreCase)
                )
                    addresses.Add(address);
            }

            if (addresses.Count == 0)
            {
                SetAddressState(
                    "Brush.State.Error",
                    Localize("Dlt645ReadAddressNoResponse", "读取失败：没有收到有效应答")
                );
                return;
            }

            var chosen = addresses[0];
            var multiple =
                addresses.Count > 1
                    ? Localize(
                        "Dlt645MultipleAddressesShort",
                        "（收到多个地址，使用第一个）"
                    )
                    : "";

            _lastManualAddress = chosen;
            if (SelectedOperation?.Control != Dlt645Codec.ControlReadAddress)
                AddressTextBox.Text = chosen;

            SetAddressState(
                "Brush.State.Success",
                $"{Localize("Dlt645Connected", "已连接")}: {chosen}{multiple}"
            );
        }
        catch (Exception ex)
        {
            SetAddressState("Brush.State.Error", ex.Message);
        }
        finally
        {
            ReadAddressButton.IsEnabled = true;
        }
    }

    private void SetAddressState(string brushKey, string tooltip)
    {
        ReadAddressButton.Background =
            TryFindResource(brushKey) as Brush ?? Brushes.Gray;
        ReadAddressButton.ToolTip = tooltip;
    }

    private void SendButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBuildFrame(out var frame, out _, out var error))
        {
            SetStatus(StatusText, error, true);
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

    private void SendToMainBoxButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBuildFrame(out var frame, out _, out var error))
        {
            SetStatus(StatusText, error, true);
            return;
        }

        Tools.Global.SendToMainSendBox(Dlt645Codec.ToHex(frame));
        SetStatus(
            StatusText,
            Localize("Dlt645SentToMainBox", "已填入主发送框"),
            false
        );
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
