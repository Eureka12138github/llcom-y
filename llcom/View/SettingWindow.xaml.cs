using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Xml;
using FontAwesome.WPF;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Search;
using llcom.ViewModels;

namespace llcom;

/// <summary>
/// SettingWindow.xaml 的交互逻辑
/// </summary>
public partial class SettingWindow : Window
{
    public SettingWindow()
    {
        InitializeComponent();
    }

    //发送/接收转换脚本编辑器（与主窗口 Lua 编辑器共用 LuaEditorViewModel）
    private LuaEditorViewModel _sendEditor;
    private LuaEditorViewModel _recvEditor;

    // ===== 日志缓冲上限（数字输入） =====

    /// <summary>数字输入框：只允许数字</summary>
    private void NumericBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        foreach (var c in e.Text)
        {
            if (!char.IsDigit(c))
            {
                e.Handled = true;
                return;
            }
        }
    }

    /// <summary>数字输入框：粘贴内容也必须是纯数字</summary>
    private void NumericBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetData(typeof(string)) is not string text)
        {
            e.CancelCommand();
            return;
        }
        foreach (var c in text)
        {
            if (!char.IsDigit(c))
            {
                e.CancelCommand();
                return;
            }
        }
    }

    /// <summary>数字输入框：回车时移出焦点，触发 LostFocus 校验</summary>
    private void NumericBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is UIElement element)
        {
            element.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
        }
    }

    /// <summary>日志保留条数：校验并把夹取后的值写回输入框</summary>
    private void LogMaxItemsBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var setting = Tools.Global.setting;
        var value = int.TryParse(logMaxItemsBox.Text, out var parsed)
            ? parsed
            : setting.logMaxItems;
        setting.logMaxItems = value;
        logMaxItemsBox.Text = setting.logMaxItems.ToString();
    }

    /// <summary>流式保留字符数：校验并把夹取后的值写回输入框</summary>
    private void StreamMaxCharsBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var setting = Tools.Global.setting;
        var value = int.TryParse(streamMaxCharsBox.Text, out var parsed)
            ? parsed
            : setting.streamMaxChars;
        setting.streamMaxChars = value;
        streamMaxCharsBox.Text = setting.streamMaxChars.ToString();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        this.DataContext = Tools.Global.setting;

        //重写关闭响应代码
        this.Closing += SettingWindow_Closing;

        //置顶显示以免被挡住
        this.Topmost = true;

        //数字输入框初值（OneWay 绑定之外的兜底同步）
        logMaxItemsBox.Text = Tools.Global.setting.logMaxItems.ToString();
        streamMaxCharsBox.Text = Tools.Global.setting.streamMaxChars.ToString();

        //快速搜索
        SearchPanel.Install(textEditor.TextArea);
        SearchPanel.Install(textEditorRev.TextArea);
        //嵌入资源的逻辑名由 RootNamespace 决定，不随 AssemblyName/exe 名变化
        string name = "llcom.Lua.xshd";
        System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
        using (System.IO.Stream s = assembly.GetManifestResourceStream(name))
        {
            using (XmlTextReader reader = new XmlTextReader(s))
            {
                var xshd = HighlightingLoader.LoadXshd(reader);
                textEditor.SyntaxHighlighting = HighlightingLoader.Load(
                    xshd,
                    HighlightingManager.Instance
                );
                textEditorRev.SyntaxHighlighting = HighlightingLoader.Load(
                    xshd,
                    HighlightingManager.Instance
                );
            }
        }
        //加载上次打开的文件（LuaEditorViewModel 构造时已加载）
        _sendEditor = new LuaEditorViewModel(
            "user_script_send_convert/",
            () => Tools.Global.setting.sendScript,
            v => Tools.Global.setting.sendScript = v,
            () => LuaEnv.LuaLoader.ClearRun()
        );
        _recvEditor = new LuaEditorViewModel(
            "user_script_recv_convert/",
            () => Tools.Global.setting.recvScript,
            v => Tools.Global.setting.recvScript = v,
            () => LuaEnv.LuaLoader.ClearRun(),
            f => Tools.Global.recvScriptBackup = f
        );
        luaFileList.DataContext = _sendEditor;
        luaFileListRev.DataContext = _recvEditor;
        //编辑器文本桥接（AvalonEdit Text 不可绑定，经回调访问）
        _sendEditor.SetTextBridge(() => textEditor.Text, t => textEditor.Text = t);
        _recvEditor.SetTextBridge(() => textEditorRev.Text, t => textEditorRev.Text = t);
    }

    private void SettingWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        //自动保存脚本
        _sendEditor?.OnAutoSave();
        _recvEditor?.OnAutoSave();
        if (Tools.Global.isMainWindowsClosed)
        {
            //说明软件关了
            e.Cancel = false;
        }
        else
        {
            e.Cancel = true; //取消这次关闭事件
            Hide(); //隐藏窗口，以便下次调用show
        }
    }

    private void ApiDocumentButton_Click(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Process.Start(Tools.Global.apiDocumentUrl);
    }

    private void OpenScriptFolderButton_Click(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Process.Start(
            "explorer.exe",
            Tools.Global.GetTrueProfilePath() + "user_script_send_convert"
        );
    }

    private void NewScriptButton_Click(object sender, RoutedEventArgs e)
    {
        luaTestWrapPanel.Visibility = Visibility.Collapsed;
        newLuaFileWrapPanel.Visibility = Visibility.Visible;
    }

    private void NewLuaFilebutton_Click(object sender, RoutedEventArgs e)
    {
        //创建/重名校验与加载都在 VM.CreateNew 内完成
        _sendEditor.CreateNew(newLuaFileNameTextBox.Text);
        newLuaFileWrapPanel.Visibility = Visibility.Collapsed;
    }

    private void NewLuaFileCancelbutton_Click(object sender, RoutedEventArgs e)
    {
        newLuaFileWrapPanel.Visibility = Visibility.Collapsed;
    }

    private void TestScriptButton_Click(object sender, RoutedEventArgs e)
    {
        newLuaFileWrapPanel.Visibility = Visibility.Collapsed;
        luaTestWrapPanel.Visibility = Visibility.Visible;
    }

    private void LuaTestbutton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_sendEditor?.SelectedFile))
        {
            try
            {
                byte[] r = LuaEnv.LuaLoader.Run(
                    $"{_sendEditor.SelectedFile}.lua",
                    new System.Collections.ArrayList
                    {
                        "uartData",
                        (luaTestHexCheck.IsChecked ?? false)
                            ? Tools.Global.Hex2Byte(luaTestTextBox.Text)
                            : Tools.Global.GetEncoding().GetBytes(luaTestTextBox.Text),
                    }
                );
                Tools.MessageBox.Show(
                    $"{TryFindResource("SettingLuaRunResult") as string ?? "?!"}\r\nHEX："
                        + Tools.Global.Byte2Hex(r)
                        + $"\r\n{TryFindResource("SettingLuaRawText") as string ?? "?!"}"
                        + Tools.Global.Byte2Readable(r)
                );
            }
            catch (Exception ex)
            {
                Tools.MessageBox.Show(
                    $"{TryFindResource("ErrorScript") as string ?? "?!"}\r\n" + ex.ToString()
                );
            }
        }
    }

    private void LuaTestCancelbutton_Click(object sender, RoutedEventArgs e)
    {
        luaTestWrapPanel.Visibility = Visibility.Collapsed;
    }

    private void TextEditor_LostFocus(object sender, RoutedEventArgs e)
    {
        //自动保存脚本
        _sendEditor?.OnAutoSave();
    }

    private void OpenLogButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(
                "explorer.exe",
                Tools.Global.GetTrueProfilePath() + "logs"
            );
        }
        catch
        {
            Tools.MessageBox.Show(
                $"尝试打开文件夹失败，请自行打开该路径：{Tools.Global.GetTrueProfilePath()}logs"
            );
        }
    }

    private void newScriptButtonRev_Click(object sender, RoutedEventArgs e)
    {
        luaTestWrapPanelRev.Visibility = Visibility.Collapsed;
        newLuaFileWrapPanelRev.Visibility = Visibility.Visible;
    }

    private void newLuaFilebuttonRev_Click(object sender, RoutedEventArgs e)
    {
        //创建/重名校验与加载都在 VM.CreateNew 内完成
        _recvEditor.CreateNew(newLuaFileNameTextBoxRev.Text);
        newLuaFileWrapPanelRev.Visibility = Visibility.Collapsed;
    }

    private void newLuaFileCancelbuttonRev_Click(object sender, RoutedEventArgs e)
    {
        newLuaFileWrapPanelRev.Visibility = Visibility.Collapsed;
    }

    private void testScriptButtonRev_Click(object sender, RoutedEventArgs e)
    {
        newLuaFileWrapPanelRev.Visibility = Visibility.Collapsed;
        luaTestWrapPanelRev.Visibility = Visibility.Visible;
    }

    private void openScriptFolderButtonRev_Click(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Process.Start(
            "explorer.exe",
            Tools.Global.GetTrueProfilePath() + "user_script_recv_convert"
        );
    }

    private void luaTestbuttonRev_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_recvEditor?.SelectedFile))
        {
            try
            {
                byte[] r = LuaEnv.LuaLoader.Run(
                    $"{_recvEditor.SelectedFile}.lua",
                    new System.Collections.ArrayList
                    {
                        "uartData",
                        (luaTestHexCheckRev.IsChecked ?? false)
                            ? Tools.Global.Hex2Byte(luaTestTextBoxRev.Text)
                            : Tools.Global.GetEncoding().GetBytes(luaTestTextBoxRev.Text),
                    },
                    "user_script_recv_convert/"
                );
                Tools.MessageBox.Show(
                    $"{TryFindResource("SettingLuaRunResult") as string ?? "?!"}\r\nHEX："
                        + Tools.Global.Byte2Hex(r)
                        + $"\r\n{TryFindResource("SettingLuaRawText") as string ?? "?!"}"
                        + Tools.Global.Byte2Readable(r)
                );
            }
            catch (Exception ex)
            {
                Tools.MessageBox.Show(
                    $"{TryFindResource("ErrorScript") as string ?? "?!"}\r\n" + ex.ToString()
                );
            }
        }
    }

    private void luaTestCancelbuttonRev_Click(object sender, RoutedEventArgs e)
    {
        luaTestWrapPanelRev.Visibility = Visibility.Collapsed;
    }

    private void textEditorRev_LostFocus(object sender, RoutedEventArgs e)
    {
        //自动保存脚本
        _recvEditor?.OnAutoSave();
    }
}
