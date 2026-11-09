using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using llcom.Services.Dlt645;
using Microsoft.Win32;

namespace llcom.View;

/// <summary>DI 配置管理窗口：表格编辑、删除、恢复默认、导入导出。</summary>
public partial class Dlt645IdentifierManagerWindow : Window
{
    private readonly ObservableCollection<Dlt645IdentifierEntry> _entries =
        new ObservableCollection<Dlt645IdentifierEntry>();

    public Dlt645IdentifierManagerWindow()
    {
        InitializeComponent();
        IdentifierGrid.ItemsSource = _entries;
        LoadEntries();
    }

    private void LoadEntries()
    {
        _entries.Clear();
        foreach (var entry in Dlt645IdentifierStore.LoadEntries())
            _entries.Add(Clone(entry));
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var entry = new Dlt645IdentifierEntry
        {
            Name = Localize("Dlt645DiNewName", "新 DI"),
        };
        _entries.Add(entry);
        IdentifierGrid.SelectedItem = entry;
        IdentifierGrid.ScrollIntoView(entry);
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (
            var entry in IdentifierGrid.SelectedItems.OfType<Dlt645IdentifierEntry>().ToList()
        )
            _entries.Remove(entry);
    }

    private void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(
            Localize(
                "Dlt645DiRestoreConfirm",
                "确定恢复默认 DI 吗？当前所有 DI 配置将被覆盖。"
            ),
            Localize("Dlt645DiRestoreDefaults", "恢复默认"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning
        );
        if (result != MessageBoxResult.Yes)
            return;

        _entries.Clear();
        foreach (var entry in Dlt645IdentifierStore.DefaultEntries())
            _entries.Add(Clone(entry));
    }

    private void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "JSON|*.json|All files|*.*",
            Title = Localize("Dlt645ImportDi", "导入 DI"),
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var imported = Dlt645IdentifierStore.LoadEntriesFromFile(dialog.FileName);
            foreach (var entry in imported)
            {
                var index = _entries
                    .Select((item, i) => new { item, i })
                    .FirstOrDefault(pair =>
                        string.Equals(
                            pair.item.Code,
                            entry.Code,
                            System.StringComparison.OrdinalIgnoreCase
                        )
                    );
                if (index != null)
                    _entries[index.i] = Clone(entry);
                else
                    _entries.Add(Clone(entry));
            }
        }
        catch (System.Exception ex)
        {
            System.Windows.MessageBox.Show(
                ex.Message,
                Localize("Dlt645ImportDi", "导入 DI"),
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "JSON|*.json|All files|*.*",
            FileName = "dlt645-di.json",
            Title = Localize("Dlt645ExportDi", "导出 DI"),
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            Dlt645IdentifierStore.Export(dialog.FileName, _entries);
            System.Windows.MessageBox.Show(
                Localize("Dlt645ExportDiDone", "DI 已导出"),
                Localize("Dlt645ExportDi", "导出 DI")
            );
        }
        catch (System.Exception ex)
        {
            System.Windows.MessageBox.Show(
                ex.Message,
                Localize("Dlt645ExportDi", "导出 DI"),
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        IdentifierGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        IdentifierGrid.CommitEdit(DataGridEditingUnit.Row, true);

        var normalized = new List<Dlt645IdentifierEntry>();
        var codes = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _entries)
        {
            if (!Dlt645IdentifierStore.TryNormalizeEntry(entry, out var valid))
            {
                System.Windows.MessageBox.Show(
                    string.Format(
                        Localize(
                            "Dlt645DiInvalidRow",
                            "存在无效 DI：名称不能为空，DI 必须是 4 字节 HEX。请检查：{0}"
                        ),
                        entry.Name
                    ),
                    Localize("Dlt645ManageDiTitle", "管理数据标识"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return;
            }
            if (!codes.Add(valid.Code))
            {
                System.Windows.MessageBox.Show(
                    string.Format(
                        Localize("Dlt645DiDuplicateCode", "DI 重复：{0}"),
                        valid.Code
                    ),
                    Localize("Dlt645ManageDiTitle", "管理数据标识"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return;
            }
            normalized.Add(valid);
        }

        Dlt645IdentifierStore.SaveEntries(normalized);
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private string Localize(string key, string fallback) =>
        TryFindResource(key) as string ?? fallback;

    private static Dlt645IdentifierEntry Clone(Dlt645IdentifierEntry entry) =>
        new Dlt645IdentifierEntry
        {
            Code = entry.Code,
            Name = entry.Name,
            Category = entry.Category,
            Unit = entry.Unit,
            Decimals = entry.Decimals,
            Note = entry.Note,
        };
}
