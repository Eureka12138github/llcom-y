using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using llcom.Model;

namespace llcom.Pages;

/// <summary>
/// OnlineScriptsPage.xaml 的交互逻辑
/// </summary>
public partial class OnlineScriptsPage : Page, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    /// <summary>
    /// 设置属性值，值变化时触发 PropertyChanged（XAML 生成的 g.cs 基类固定为 Page，
    /// 无法继承 ObservableObject/ObservablePage，故类内自带此帮助方法）
    /// </summary>
    protected bool SetProperty<T>(
        ref T field,
        T value,
        [CallerMemberName] string propertyName = null
    )
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private const string OfficialInfoUrl = "https://github.com/chenxuuu/llcom/discussions/87";
    private const string ProjectInfoUrl =
        "https://github.com/Eureka12138github/llcom-y/tree/master/community-scripts";
    private const string ProjectSubmitUrl =
        "https://github.com/Eureka12138github/llcom-y/issues/new?template=script-submission.yml";

    private readonly ObservableCollection<OnlineScript> scripts =
        new ObservableCollection<OnlineScript>();
    private bool _loaded;
    private int _refreshVersion;

    public OnlineScriptsPage()
    {
        InitializeComponent();
    }

    private int _progress = 0;
    public int Progress
    {
        get => _progress;
        set => SetProperty(ref _progress, value);
    }

    private bool _isIndeterminate = true;
    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        set => SetProperty(ref _isIndeterminate, value);
    }

    private string _loadingMsg = "";
    public string LoadingMsg
    {
        get => _loadingMsg;
        set => SetProperty(ref _loadingMsg, value);
    }

    private bool _isLoding = true;
    public bool IsLoding
    {
        get => _isLoding;
        set => SetProperty(ref _isLoding, value);
    }

    /// <summary>当前脚本来源。</summary>
    private OnlineScriptSource _source = OnlineScriptSource.Official;

    private void SourceButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender == OfficialSourceButton)
            SetSource(OnlineScriptSource.Official);
        else if (sender == ProjectSourceButton)
            SetSource(OnlineScriptSource.Project);
    }

    private void SetSource(OnlineScriptSource source)
    {
        if (_source == source)
        {
            //ToggleButton 点击后可能把自己取消选中；这里恢复两个按钮的选中态
            UpdateSourceButtons();
            return;
        }

        _source = source;
        Tools.Global.setting.onlineScriptSource = (int)source;
        UpdateSourceButtons();

        if (_loaded)
            _ = RefreshList();
    }

    private void UpdateSourceButtons()
    {
        if (OfficialSourceButton != null)
            OfficialSourceButton.IsChecked = _source == OnlineScriptSource.Official;
        if (ProjectSourceButton != null)
            ProjectSourceButton.IsChecked = _source == OnlineScriptSource.Project;
    }

    /// <summary>空列表 / 加载失败时的提示文本。</summary>
    private string _listStateText = "";
    public string ListStateText
    {
        get => _listStateText;
        set => SetProperty(ref _listStateText, value);
    }

    private bool _hasListState;
    public bool HasListState
    {
        get => _hasListState;
        set => SetProperty(ref _hasListState, value);
    }

    /// <summary>
    /// 加载中。。。
    /// </summary>
    /// <param name="show"></param>
    /// <param name="progress"></param>
    private void Loading(string show = null, int? progress = null)
    {
        LoadingMsg = show ?? TryFindResource("Loading") as string ?? "?!";
        IsIndeterminate = progress == null;
        Progress = progress ?? 0;
        IsLoding = true;
    }

    private void UnLoading() => IsLoding = false;

    /// <summary>
    /// 刷新脚本列表
    /// </summary>
    /// <returns></returns>
    private async Task RefreshList()
    {
        if (!_loaded)
            return;

        var version = ++_refreshVersion;
        var source = _source;
        Loading(TryFindResource("OnlineScriptLoading") as string);
        HasListState = false;
        scripts.Clear();

        List<OnlineScript> result = null;
        var fetchTask = Task.Run(() =>
        {
            try
            {
                result = Tools.Global.GetOnlineScripts(
                    source,
                    (got, total) =>
                    {
                        var percent = total > 0 ? (int)(got * 100.0 / total) : 0;
                        //进度回调来自后台线程，必须回到 UI 线程再更新绑定属性
                        Dispatcher.BeginInvoke(
                            new Action(() =>
                            {
                                if (version != _refreshVersion)
                                    return;
                                Loading(
                                    TryFindResource("OnlineScriptLoading") as string,
                                    percent
                                );
                            })
                        );
                    }
                );
            }
            catch (Exception ex)
            {
                Tools.Logger.Swallowed("online scripts", ex);
            }
        });

        //官方 GraphQL 没有设置请求超时；这里兜底，避免页面无限转圈
        var completed = await Task.WhenAny(fetchTask, Task.Delay(30000));
        if (completed != fetchTask)
        {
            if (version == _refreshVersion)
            {
                //让仍在后台运行的请求的进度回调失效，避免超时后又把页面拉回加载中
                _refreshVersion++;
                ListStateText = GetLoadFailedText();
                HasListState = true;
                UnLoading();
            }
            return;
        }
        await fetchTask;

        if (version != _refreshVersion)
            return;

        if (result != null)
        {
            foreach (var d in result)
            {
                d.TypeText = GetTypeText(d.Type);
                scripts.Add(d);
            }

            if (scripts.Count == 0)
            {
                ListStateText = TryFindResource("OnlineScriptEmpty") as string ?? "?!";
                HasListState = true;
            }
        }
        else
        {
            ListStateText = GetLoadFailedText();
            HasListState = true;
        }

        UnLoading();
    }

    private string GetLoadFailedText()
    {
        return _source == OnlineScriptSource.Project
            ? TryFindResource("OnlineScriptProjectLoadFailed") as string
                ?? TryFindResource("OnlineScriptLoadFailed") as string
                ?? "?!"
            : TryFindResource("OnlineScriptLoadFailed") as string ?? "?!";
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
            return;
        _loaded = true;

        var saved = (OnlineScriptSource)Tools.Global.setting.onlineScriptSource;
        _source =
            saved == OnlineScriptSource.Project
                ? OnlineScriptSource.Project
                : OnlineScriptSource.Official;

        this.DataContext = this;
        UpdateSourceButtons();

        //绑上去
        ScriptListItemsControl.ItemsSource = scripts;

        //打开时刷新一下
        await RefreshList();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshList();
    }

    private void InfoButton_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl(_source == OnlineScriptSource.Official ? OfficialInfoUrl : ProjectInfoUrl);
    }

    private void SubmitButton_Click(object sender, RoutedEventArgs e)
    {
        //官方脚本沿用原项目的讨论区投稿；本项目使用 Issue 表单
        OpenUrl(_source == OnlineScriptSource.Official ? OfficialInfoUrl : ProjectSubmitUrl);
    }

    /// <summary>
    /// 是否在看脚本列表页？
    /// </summary>
    private bool _isInList = true;
    public bool IsInList
    {
        get => _isInList;
        set => SetProperty(ref _isInList, value);
    }

    private OnlineScript _scriptNow = new OnlineScript();
    public OnlineScript ScriptNow
    {
        get => _scriptNow;
        set => SetProperty(ref _scriptNow, value);
    }

    //打开了某个脚本的详情页
    private void Button_Click(object sender, RoutedEventArgs e)
    {
        var data = ((Button)sender).Tag as OnlineScript;
        if (data == null)
            return;

        ScriptNow.Author = data.Author;
        ScriptNow.Version = data.Version;
        ScriptNow.Name = data.Name;
        ScriptNow.Description = data.Description;
        ScriptNow.Note = data.Note;
        ScriptNow.Url = data.Url;
        ScriptNow.Script = data.Script;
        ScriptNow.Type = data.Type;
        ScriptNow.TypeText = data.TypeText;

        IsInList = false;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        IsInList = true;
    }

    private void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        var folder = ScriptNow.Type switch
        {
            "send" => "user_script_send_convert",
            "recv" => "user_script_recv_convert",
            _ => "user_script_run",
        };
        var destDir = $"{Tools.Global.ProfilePath}{folder}/";

        while (true)
        {
            var (result, fileName) = Tools.InputDialog.OpenDialog(
                TryFindResource("OnlineScriptDownloadSaveNotice") as string ?? "?!",
                $"{ScriptNow.Name}",
                TryFindResource("OnlineScriptDownloadTitle") as string ?? "?!"
            );
            if (!result)
                return;

            var safeName = string.Join(
                "_",
                (fileName ?? "").Split(Path.GetInvalidFileNameChars())
            );
            if (string.IsNullOrWhiteSpace(safeName))
                continue;

            var dest = $"{destDir}{safeName}.lua";

            //文件已经有了
            if (File.Exists(dest))
            {
                Tools.MessageBox.Show(TryFindResource("LuaExist") as string ?? "?!");
                continue; //回到最开始
            }

            try
            {
                if (!Directory.Exists(destDir))
                    Directory.CreateDirectory(destDir);

                File.WriteAllText(dest, ScriptNow.Script);
                Tools.Global.RefreshLuaScriptList();

                var message = TryFindResource("SaveSucceed") as string ?? "?!";
                if (ScriptNow.Type == "send" || ScriptNow.Type == "recv")
                    message +=
                        "\r\n"
                        + (TryFindResource("OnlineScriptSavedConvertTip") as string ?? "");
                Tools.MessageBox.Show(message);
            }
            catch (Exception err)
            {
                Tools.MessageBox.Show(err.ToString());
            }
            return;
        }
    }

    private void UrlButton_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl(ScriptNow.Url);
    }

    private string GetTypeText(string type)
    {
        return type switch
        {
            "send" => TryFindResource("OnlineScriptTypeSend") as string ?? "send",
            "recv" => TryFindResource("OnlineScriptTypeRecv") as string ?? "recv",
            _ => TryFindResource("OnlineScriptTypeRun") as string ?? "run",
        };
    }

    private void OpenUrl(string url)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(url))
                System.Diagnostics.Process.Start(url);
        }
        catch (Exception ex)
        {
            Tools.Logger.Swallowed("online scripts", ex);
        }
    }
}
