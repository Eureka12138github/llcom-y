using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using llcom.Services;

namespace llcom.Pages;

/// <summary>
/// 计算器小工具：多进制同步显示、IEEE-754 浮点解析、位运算。
/// 核心换算逻辑在 <see cref="CalculatorMath"/>，本类只处理界面状态。
/// </summary>
public partial class CalculatorPage : Page
{
    private ulong _value;
    private int _width = 32;
    private int _inputBase = 16;
    private bool _ready;
    private bool _suppressInput;
    private bool _suppressFloat;

    public CalculatorPage()
    {
        InitializeComponent();

        _ready = true;
        Width32Radio.IsChecked = true;
        BigEndianRadio.IsChecked = true;
        InputBaseBox.SelectedIndex = 0;

        UpdateDisplays();
        UpdateInputText();
        UpdateFloatResults();
    }

    private ulong Mask => CalculatorMath.Mask(_width);

    private void Width_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready || sender is not RadioButton radio || radio.Tag is not string tag)
            return;
        if (!int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bits))
            return;

        _width = bits;
        _value &= Mask;
        UpdateDisplays();
        UpdateInputText();
    }

    private void InputBase_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || InputBaseBox.SelectedItem is not ComboBoxItem item)
            return;
        if (!int.TryParse(item.Tag?.ToString(), out var baseValue))
            return;

        _inputBase = baseValue;
        UpdateInputText();
        ClearError();
    }

    private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready || _suppressInput)
            return;

        var text = InputBox.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            ClearError();
            return;
        }

        if (CalculatorMath.TryParseValue(text, _inputBase, out var parsed))
        {
            _value = parsed & Mask;
            ClearError();
            UpdateDisplays();
        }
        else
        {
            ShowError(TryFindResource("CalculatorInvalid") as string ?? "Invalid input");
        }
    }

    private void FloatInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready || _suppressFloat)
            return;

        if (sender is TextBox box)
        {
            var maxDigits = ReferenceEquals(box, Float32Input) ? 8 : 16;
            var sanitized = CalculatorMath.SanitizeHex(box.Text, maxDigits);
            if (!string.Equals(sanitized, box.Text, StringComparison.Ordinal))
            {
                _suppressFloat = true;
                box.Text = sanitized;
                box.CaretIndex = sanitized.Length;
                _suppressFloat = false;
            }
        }

        UpdateFloatResults();
    }

    private void Endian_Checked(object sender, RoutedEventArgs e)
    {
        if (_ready)
            UpdateFloatResults();
    }

    private void ExpressionBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_ready)
            UpdateExpressionResult();
    }

    private void UpdateExpressionResult()
    {
        if (string.IsNullOrWhiteSpace(ExpressionBox.Text))
        {
            ExpressionResult.Text = "—";
            ExpressionErrorText.Visibility = Visibility.Collapsed;
            return;
        }

        if (ExpressionEvaluator.TryEvaluate(ExpressionBox.Text, out var value, out var error))
        {
            ExpressionResult.Text = value.ToString("G15", CultureInfo.InvariantCulture);
            ExpressionErrorText.Visibility = Visibility.Collapsed;
            return;
        }

        ExpressionResult.Text = "—";
        ExpressionErrorText.Text =
            error == ExpressionError.DivideByZero
                ? TryFindResource("CalculatorDivideByZero") as string ?? "Division by zero"
                : TryFindResource("CalculatorExpressionInvalid") as string ?? "Invalid expression";
        ExpressionErrorText.Visibility = Visibility.Visible;
    }

    private void CopyValue_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not TextBox box)
            return;
        if (string.IsNullOrEmpty(box.Text))
            return;

        try
        {
            Clipboard.SetText(box.Text);
        }
        catch (Exception ex)
        {
            Tools.Logger.Swallowed("calculator clipboard", ex);
        }
    }

    private void BitOp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        var op = button.Tag?.ToString() ?? "";
        var operand = 0UL;
        if (op != "not")
        {
            if (!CalculatorMath.TryParseValue(OperandBox.Text, _inputBase, out operand))
            {
                ShowError(
                    TryFindResource("CalculatorInvalidOperand") as string ?? "Invalid operand"
                );
                return;
            }
        }

        if (!CalculatorMath.TryApplyBitOp(_value, operand, op, _width, out var result))
            return;

        _value = result;
        ClearError();
        UpdateDisplays();
        UpdateInputText();
    }

    private void UpdateDisplays()
    {
        HexBox.Text = CalculatorMath.FormatHex(_value, _width);
        BinBox.Text = CalculatorMath.FormatBin(_value, _width);
        OctBox.Text = CalculatorMath.FormatOct(_value, _width);
        DecBox.Text = _value.ToString(CultureInfo.InvariantCulture);
    }

    private void UpdateInputText()
    {
        _suppressInput = true;
        InputBox.Text = _inputBase switch
        {
            10 => _value.ToString(CultureInfo.InvariantCulture),
            8 => CalculatorMath.ToBaseString(_value, 8),
            2 => CalculatorMath.ToBaseString(_value, 2),
            _ => _value.ToString("X", CultureInfo.InvariantCulture),
        };
        _suppressInput = false;
    }

    private void UpdateFloatResults()
    {
        var bigEndian = BigEndianRadio.IsChecked == true;

        Float32Result.Text = CalculatorMath.FormatFloat(
            Float32Input.Text,
            4,
            bigEndian,
            out _
        );
        Float64Result.Text = CalculatorMath.FormatFloat(
            Float64Input.Text,
            8,
            bigEndian,
            out _
        );
    }

    private void ShowError(string message)
    {
        InputErrorText.Text = message;
        InputErrorText.Visibility = Visibility.Visible;
    }

    private void ClearError()
    {
        InputErrorText.Visibility = Visibility.Collapsed;
    }
}