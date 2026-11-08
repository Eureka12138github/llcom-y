using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using llcom.ViewModels;

namespace llcom.View;

/// <summary>
/// 脚本选择/筛选栏：主窗口“运行脚本”和设置窗口的发送/接收脚本共用。
/// 包含搜索、分类、收藏筛选，以及收藏/分类/更多操作入口。
/// </summary>
public partial class ScriptPickerBar : UserControl
{
    public ScriptPickerBar()
    {
        InitializeComponent();
    }

    /// <summary>页面自己的操作按钮（新建/运行/打开目录等），由使用方注入。</summary>
    public static readonly DependencyProperty ActionsContentProperty = DependencyProperty.Register(
        nameof(ActionsContent),
        typeof(object),
        typeof(ScriptPickerBar),
        new PropertyMetadata(null)
    );

    public object? ActionsContent
    {
        get => GetValue(ActionsContentProperty);
        set => SetValue(ActionsContentProperty, value);
    }

    private LuaEditorViewModel? Editor => DataContext as LuaEditorViewModel;

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateClearButton();
        if (!IsLoaded)
            return;
        ResultsPopup.IsOpen = !string.IsNullOrWhiteSpace(SearchBox.Text);
    }

    private void SearchBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(SearchBox.Text))
            ResultsPopup.IsOpen = true;
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ResultsPopup.IsOpen = false;
            e.Handled = true;
            return;
        }

        if (!ResultsPopup.IsOpen || Editor == null || Editor.ScriptItems.Count == 0)
            return;

        if (e.Key == Key.Down)
        {
            ResultsList.SelectedIndex = 0;
            ResultsList.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            SelectScript(Editor.ScriptItems[0].Name);
            e.Handled = true;
        }
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        ResultsPopup.IsOpen = false;
        SearchBox.Focus();
    }

    private void ResultsList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (
            ItemsControl.ContainerFromElement(ResultsList, e.OriginalSource as DependencyObject)
            is not ListBoxItem item
            || item.DataContext is not ScriptListItem script
        )
            return;
        SelectScript(script.Name);
    }

    private void ResultsList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ResultsPopup.IsOpen = false;
            SearchBox.Focus();
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter)
            return;

        var selected = ResultsList.SelectedItem as ScriptListItem;
        if (selected == null && Editor != null && Editor.ScriptItems.Count > 0)
            selected = Editor.ScriptItems[0];
        if (selected != null)
            SelectScript(selected.Name);
        e.Handled = true;
    }

    private void SelectScript(string name)
    {
        if (Editor == null || string.IsNullOrEmpty(name))
            return;
        Editor.SelectedFile = name;
        ResultsPopup.IsOpen = false;
    }

    private void Favorite_Click(object sender, RoutedEventArgs e) =>
        Editor?.ToggleSelectedFavorite();

    private void Pin_Click(object sender, RoutedEventArgs e) => Editor?.SetSelectedPinned(true);

    private void Unpin_Click(object sender, RoutedEventArgs e) => Editor?.SetSelectedPinned(false);

    private void Category_Click(object sender, RoutedEventArgs e)
    {
        var editor = Editor;
        if (editor == null || string.IsNullOrEmpty(editor.SelectedFile))
            return;
        var title = TryFindResource("ScriptSetCategory") as string ?? "Category";
        var prompt = TryFindResource("ScriptCategoryPrompt") as string ?? title;
        var suggestions = editor
            .CategoryOptions.Select(option => option.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .OrderBy(value => value, System.StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var result = Tools.InputDialog.OpenDialog(
            prompt,
            editor.SelectedCategoryValue,
            title,
            suggestions.Count > 0 ? suggestions : null
        );
        if (result.Item1)
            editor.SetSelectedCategory(result.Item2);
    }

    private void Description_Click(object sender, RoutedEventArgs e)
    {
        var editor = Editor;
        if (editor == null || string.IsNullOrEmpty(editor.SelectedFile))
            return;
        var title = TryFindResource("ScriptSetDescription") as string ?? "Description";
        var prompt = TryFindResource("ScriptDescriptionPrompt") as string ?? title;
        var result = Tools.InputDialog.OpenDialog(
            prompt,
            editor.SelectedDescriptionValue,
            title
        );
        if (result.Item1)
            editor.SetSelectedDescription(result.Item2);
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e) => OpenMoreMenu(MoreButton);

    private void ScriptCombo_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        OpenMoreMenu(ScriptCombo);
        e.Handled = true;
    }

    private void OpenMoreMenu(UIElement target)
    {
        var menu = MoreButton.ContextMenu;
        if (menu == null)
            return;
        menu.PlacementTarget = target;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void UpdateClearButton()
    {
        if (ClearSearchButton == null)
            return;
        ClearSearchButton.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }
}
