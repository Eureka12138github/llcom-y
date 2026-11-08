using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using llcom.Model;
using llcom.Services;

namespace llcom.ViewModels;

/// <summary>
/// 脚本分类筛选项：Value 为空表示“全部分类”，Label 为界面显示文本。
/// </summary>
public sealed class ScriptCategoryOption
{
    public ScriptCategoryOption(string value, string label)
    {
        Value = value;
        Label = label;
    }

    public string Value { get; }

    public string Label { get; }

    public override string ToString() => Label;
}

/// <summary>
/// Lua 脚本编辑器 ViewModel（Step 7）。
/// 通用组件：主窗口的 user_script_run 编辑器、设置窗口的 send/recv_convert 编辑器共用，
/// 消除原 MainWindow/SettingWindow 中的重复实现。
/// 职责：文件列表加载、搜索/分类/收藏筛选、打开/保存/新建、自动保存、外部修改检测、脚本重载回调。
/// 运行/日志等主窗口特有逻辑保留在 code-behind。
/// </summary>
public partial class LuaEditorViewModel : ObservableObject
{
    private readonly string _folderName; //脚本目录（相对 ProfilePath），如 "user_script_run/"
    private readonly Func<string> _getCurrentScript; //当前选中脚本名（对应 setting 属性）
    private readonly Action<string> _setCurrentScript; //设置当前选中脚本名
    private readonly Action _onReload; //脚本重载回调（如 LuaLoader.ClearRun）
    private readonly Action<string> _onLoaded; //加载完成回调（如设置 recvScriptBackup）
    private readonly ScriptMetadataStore _metadataStore;
    private readonly Dictionary<string, ScriptMetadata> _metadata = new(
        StringComparer.OrdinalIgnoreCase
    );
    private List<string> _allFiles = new List<string>();

    //编辑器文本访问桥（AvalonEdit Text 不可绑定，由 View 通过 SetTextBridge 接入；
    //接入前使用内部缓冲，避免 VM 持有 UI 控件引用）
    private Func<string> _getText;
    private Action<string> _setText;
    private string _fallbackText = "";

    /// <summary>当前筛选条件下可见的脚本文件列表（不带 .lua 后缀）</summary>
    public ObservableCollection<string> Files { get; } = new ObservableCollection<string>();

    /// <summary>当前筛选条件下的脚本项（含分类/描述/收藏），供搜索结果展示</summary>
    public ObservableCollection<ScriptListItem> ScriptItems { get; } =
        new ObservableCollection<ScriptListItem>();

    /// <summary>分类筛选项；第一项固定为“全部分类”</summary>
    public ObservableCollection<ScriptCategoryOption> CategoryOptions { get; } =
        new ObservableCollection<ScriptCategoryOption>();

    /// <summary>文件列表选中项（绑定 ComboBox）</summary>
    [ObservableProperty]
    private string _selectedFile = "";

    /// <summary>
    /// ComboBox 的选中项。文件被搜索结果过滤掉时可以为空，但不会清空 <see cref="SelectedFile"/>，
    /// 避免筛选时丢失当前正在编辑的脚本。
    /// </summary>
    [ObservableProperty]
    private string _visibleSelectedFile = "";

    /// <summary>搜索文本（匹配文件名、描述、分类）</summary>
    [ObservableProperty]
    private string _searchText = "";

    /// <summary>当前分类筛选项</summary>
    [ObservableProperty]
    private ScriptCategoryOption _selectedCategoryOption = new ScriptCategoryOption("", "");

    /// <summary>是否只看收藏</summary>
    [ObservableProperty]
    private bool _favoritesOnly;

    /// <summary>当前脚本是否已收藏</summary>
    [ObservableProperty]
    private bool _isSelectedFavorite;

    //加载锁 + 文件时间戳（自动保存 / 外部修改检测依据）
    private bool _loading;
    private string _lastFile = "";
    private DateTime _lastFileTime;
    private DateTime _lastChangeTime;

    public LuaEditorViewModel(
        string folderName,
        Func<string> getCurrent,
        Action<string> setCurrent,
        Action? onReload = null,
        Action<string>? onLoaded = null
    )
    {
        _folderName = folderName;
        _getCurrentScript = getCurrent;
        _setCurrentScript = setCurrent;
        _getText = () => _fallbackText;
        _setText = t => _fallbackText = t;
        _onReload = onReload ?? (() => { });
        _onLoaded = onLoaded ?? (_ => { });
        _metadataStore = new ScriptMetadataStore(Tools.Global.ProfilePath + _folderName);

        CategoryOptions.Add(new ScriptCategoryOption("", AllCategoriesLabel));
        SelectedCategoryOption = CategoryOptions[0];

        RefreshList();
        //目录可能尚未创建（首次运行），此时暂不加载；待 Initial() 生成文件结构后再刷新
        if (Directory.Exists(Tools.Global.ProfilePath + _folderName))
            LoadFile(getCurrent());
    }

    /// <summary>“全部分类”的显示文本</summary>
    private static string AllCategoriesLabel =>
        (Application.Current?.TryFindResource("ScriptCategoryAll") as string) ?? "All";

    /// <summary>当前脚本的分类（只读，供设置分类对话框显示默认值）</summary>
    public string SelectedCategoryValue => GetSelectedMetadata()?.Category ?? "";

    /// <summary>当前脚本的描述（只读，供设置描述对话框显示默认值）</summary>
    public string SelectedDescriptionValue => GetSelectedMetadata()?.Description ?? "";

    /// <summary>当前脚本的分类显示文本（未分类时显示本地化占位文字）</summary>
    public string SelectedCategoryDisplay
    {
        get
        {
            var category = SelectedCategoryValue;
            if (!string.IsNullOrWhiteSpace(category))
                return category;
            return Application.Current?.TryFindResource("ScriptUncategorized") as string
                ?? "Uncategorized";
        }
    }

    /// <summary>
    /// 由 View 接入编辑器文本读写（AvalonEdit TextEditor.Text）。
    /// 接入时会把已加载的缓冲文本同步到控件。
    /// </summary>
    public void SetTextBridge(Func<string> getText, Action<string> setText)
    {
        _getText = getText;
        _setText = setText;
        setText(_fallbackText);
    }

    /// <summary>脚本完整路径</summary>
    private string FullPath(string fileName) =>
        Tools.Global.ProfilePath + _folderName + fileName + ".lua";

    /// <summary>
    /// 刷新文件列表（脚本目录下的 .lua 文件），并合并 sidecar 元数据。
    /// </summary>
    public void RefreshList()
    {
        var dirPath = Tools.Global.ProfilePath + _folderName;
        _loading = true;
        try
        {
            //目录可能尚未创建（首次运行、文件结构由 Initial() 生成）——此时不应崩溃
            _allFiles = Directory.Exists(dirPath)
                ? Directory
                    .GetFiles(dirPath, "*.lua")
                    .Select(Path.GetFileNameWithoutExtension)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToList()
                : new List<string>();

            MergeMetadata();
            RefreshCategoriesCore();

            _lastFile = _getCurrentScript();
            //注意：SelectedFile 赋值必须在 _loading=false 之前，
            //否则会触发 OnSelectedFileChanged → LoadFile → RefreshList 无限递归卡死
            SelectedFile = _lastFile;
            ApplyFilterCore();
        }
        finally
        {
            _loading = false;
        }

        SyncVisibleSelection();
        SyncSelectedMetadata();
    }

    /// <summary>
    /// 加载脚本文件；不存在时回退到 "default"（若 default 也不存在则创建）
    /// </summary>
    public void LoadFile(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return;
        if (!File.Exists(FullPath(fileName)))
        {
            _setCurrentScript("default");
            if (!File.Exists(FullPath("default")))
                File.Create(FullPath("default")).Close();
        }
        else
        {
            _setCurrentScript(fileName);
        }

        try
        {
            _setText(File.ReadAllText(FullPath(_getCurrentScript())));
        }
        catch
        {
            Tools.MessageBox.Show(
                "File load failed.\r\n" + "Do not open this file in other application!"
            );
            return;
        }

        //记录最后时间；修改时间使用文件时间
        _lastFileTime = File.GetLastWriteTime(FullPath(_getCurrentScript()));
        _lastChangeTime = _lastFileTime;

        RefreshList();
        SyncSelectedMetadata();
        _onReload(); //脚本已变更，通知重载（发送/接收转换脚本缓存）
        _onLoaded(_getCurrentScript());
    }

    /// <summary>
    /// 保存脚本文件（仅在编辑器有改动时写盘，避免无谓 IO）
    /// </summary>
    public void SaveFile(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return;
        try
        {
            if (_lastChangeTime > _lastFileTime)
            {
                File.WriteAllText(FullPath(fileName), _getText());
                _lastFileTime = File.GetLastWriteTime(FullPath(fileName));
            }
        }
        catch (Exception ex)
        {
            Tools.Logger.Swallowed("save lua file", ex);
        }
    }

    /// <summary>编辑器文本变化（标记已修改，供自动保存判断）</summary>
    public void OnTextChanged() => _lastChangeTime = DateTime.Now;

    /// <summary>失焦/窗口切换/关闭时的自动保存</summary>
    public void OnAutoSave() => SaveFile(_lastFile);

    /// <summary>窗口激活时检测外部修改（其他编辑器改过则重新加载）</summary>
    public void CheckExternalChange()
    {
        if (string.IsNullOrEmpty(_lastFile))
            return;
        var fileTime = File.GetLastWriteTime(FullPath(_lastFile));
        if (fileTime > _lastFileTime)
            LoadFile(_lastFile);
    }

    /// <summary>
    /// 新建脚本（重名/非法名提示）
    /// </summary>
    public void CreateNew(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            Tools.MessageBox.Show(
                (Application.Current?.TryFindResource("LuaNoName") as string) ?? "?!"
            );
            return;
        }
        if (File.Exists(FullPath(fileName)))
        {
            Tools.MessageBox.Show(
                (Application.Current?.TryFindResource("LuaExist") as string) ?? "?!"
            );
            return;
        }
        try
        {
            File.Create(FullPath(fileName)).Close();
            LoadFile(fileName);
        }
        catch
        {
            Tools.MessageBox.Show(
                (Application.Current?.TryFindResource("LuaCreateFail") as string) ?? "?!"
            );
        }
    }

    /// <summary>切换当前脚本的收藏状态</summary>
    public void ToggleSelectedFavorite()
    {
        var metadata = GetSelectedMetadata();
        if (metadata == null)
            return;
        metadata.Favorite = !metadata.Favorite;
        SaveMetadata();
        IsSelectedFavorite = metadata.Favorite;
        ApplyFilter();
    }

    /// <summary>设置当前脚本的分类（空字符串表示未分类）</summary>
    public void SetSelectedCategory(string category)
    {
        var metadata = GetSelectedMetadata();
        if (metadata == null)
            return;
        metadata.Category = (category ?? "").Trim();
        SaveMetadata();
        RefreshCategoriesCore();
        ApplyFilter();
        OnPropertyChanged(nameof(SelectedCategoryDisplay));
    }

    /// <summary>设置当前脚本的描述</summary>
    public void SetSelectedDescription(string description)
    {
        var metadata = GetSelectedMetadata();
        if (metadata == null)
            return;
        metadata.Description = (description ?? "").Trim();
        SaveMetadata();
        ApplyFilter();
    }

    /// <summary>置顶/取消置顶当前脚本（置顶项按名称排序在普通项之前）</summary>
    public void SetSelectedPinned(bool pinned)
    {
        var metadata = GetSelectedMetadata();
        if (metadata == null)
            return;
        metadata.Order = pinned ? -1 : 0;
        SaveMetadata();
        ApplyFilter();
    }

    /// <summary>按当前搜索/分类/收藏条件重建可见列表</summary>
    public void ApplyFilter()
    {
        if (_loading)
            return;
        ApplyFilterCore();
        SyncVisibleSelection();
    }

    private void ApplyFilterCore()
    {
        IEnumerable<string> query = _allFiles;

        var search = (SearchText ?? "").Trim();
        if (search.Length > 0)
            query = query.Where(name => MatchesSearch(name, search));

        var category = SelectedCategoryOption?.Value ?? "";
        if (category.Length > 0)
            query = query.Where(name =>
                string.Equals(
                    GetMetadata(name)?.Category ?? "",
                    category,
                    StringComparison.OrdinalIgnoreCase
                )
            );

        if (FavoritesOnly)
            query = query.Where(name => GetMetadata(name)?.Favorite == true);

        var ordered = query
            .OrderBy(name => GetMetadata(name)?.Order ?? 0)
            .ThenBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        ScriptItems.Clear();
        foreach (var name in ordered)
        {
            ScriptItems.Add(
                new ScriptListItem(
                    name,
                    GetMetadata(name) ?? new ScriptMetadata { File = name }
                )
            );
        }

        Files.Clear();
        foreach (var name in ordered)
            Files.Add(name);

        //当前脚本即使不匹配筛选条件也保留在下拉列表中，避免 ComboBox 变成空白。
        if (
            !string.IsNullOrEmpty(SelectedFile)
            && _allFiles.Contains(SelectedFile, StringComparer.OrdinalIgnoreCase)
            && !Files.Contains(SelectedFile)
        )
            Files.Add(SelectedFile);
    }

    private bool MatchesSearch(string name, string search)
    {
        if (name.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0)
            return true;
        var metadata = GetMetadata(name);
        if (metadata == null)
            return false;
        return (metadata.Category ?? "").IndexOf(search, StringComparison.CurrentCultureIgnoreCase)
                >= 0
            || (metadata.Description ?? "").IndexOf(
                    search,
                    StringComparison.CurrentCultureIgnoreCase
                )
                >= 0;
    }

    private void MergeMetadata()
    {
        var loaded = _metadataStore.Load();
        var needsSave =
            loaded.Keys.Any(name => !_allFiles.Contains(name, StringComparer.OrdinalIgnoreCase))
            || _allFiles.Any(name => !loaded.ContainsKey(name));

        _metadata.Clear();
        foreach (var name in _allFiles)
        {
            if (loaded.TryGetValue(name, out var metadata))
            {
                metadata.File = name;
                _metadata[name] = metadata;
            }
            else
            {
                _metadata[name] = new ScriptMetadata { File = name };
            }
        }

        if (needsSave)
            SaveMetadata();
    }

    private void RefreshCategoriesCore()
    {
        var current = SelectedCategoryOption?.Value ?? "";
        var categories = _metadata
            .Values.Select(metadata => metadata.Category)
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(category => category, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var previousLoading = _loading;
        _loading = true;
        try
        {
            CategoryOptions.Clear();
            CategoryOptions.Add(new ScriptCategoryOption("", AllCategoriesLabel));
            foreach (var category in categories)
                CategoryOptions.Add(new ScriptCategoryOption(category, category));

            SelectedCategoryOption =
                CategoryOptions.FirstOrDefault(option =>
                    string.Equals(option.Value, current, StringComparison.OrdinalIgnoreCase)
                ) ?? CategoryOptions[0];
        }
        finally
        {
            _loading = previousLoading;
        }
    }

    private ScriptMetadata? GetMetadata(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return null;
        return _metadata.TryGetValue(fileName, out var metadata) ? metadata : null;
    }

    private ScriptMetadata? GetSelectedMetadata() => GetMetadata(SelectedFile);

    private void SaveMetadata() => _metadataStore.Save(_metadata.Values);

    private void SyncSelectedMetadata()
    {
        IsSelectedFavorite = GetSelectedMetadata()?.Favorite ?? false;
        OnPropertyChanged(nameof(SelectedCategoryDisplay));
    }

    private void SyncVisibleSelection()
    {
        var previousLoading = _loading;
        _loading = true;
        try
        {
            VisibleSelectedFile = Files.Contains(SelectedFile) ? SelectedFile : "";
        }
        finally
        {
            _loading = previousLoading;
        }
    }

    /// <summary>
    /// 文件列表选中变化：保存上一个文件，加载新文件
    /// </summary>
    partial void OnSelectedFileChanged(string value)
    {
        if (value == null || _loading)
            return;
        if (_lastFile != "")
            SaveFile(_lastFile);
        LoadFile(value);
    }

    partial void OnVisibleSelectedFileChanged(string value)
    {
        if (_loading || string.IsNullOrEmpty(value))
            return;
        if (!string.Equals(value, SelectedFile, StringComparison.Ordinal))
            SelectedFile = value;
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnFavoritesOnlyChanged(bool value) => ApplyFilter();

    partial void OnSelectedCategoryOptionChanged(ScriptCategoryOption value) => ApplyFilter();
}
