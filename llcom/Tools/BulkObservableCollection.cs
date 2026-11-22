using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace llcom.Tools;

/// <summary>
/// 支持“从头部批量删除”的集合：删除后只发一次 <see cref="INotifyCollectionChanged"/>
/// 的 Reset 通知，而不是逐条 RemoveAt 造成的 N 次集合变更。
/// 日志条数很大（默认 5 万）时，逐条删除会反复搬运整张列表并驱动 WPF 列表重建，
/// 批量删除把这两项成本都降到“一次 O(n) + 一次 Reset”。
/// </summary>
public sealed class BulkObservableCollection<T> : Collection<T>, INotifyCollectionChanged, INotifyPropertyChanged
{
    private const string IndexerName = "Item[]";

    private readonly List<T> _items;

    public BulkObservableCollection()
        : this(new List<T>()) { }

    private BulkObservableCollection(List<T> items)
        : base(items)
    {
        _items = items;
    }

    /// <summary>集合内容变化（新增、删除、批量裁剪）。</summary>
    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    /// <summary>Count / Item[] 变化通知。</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 从头部删除 <paramref name="count"/> 条，只触发一次 Reset。
    /// </summary>
    public void TrimStart(int count)
    {
        if (count <= 0 || _items.Count == 0)
            return;

        if (count > _items.Count)
            count = _items.Count;

        _items.RemoveRange(0, count);
        RaiseReset();
    }

    protected override void InsertItem(int index, T item)
    {
        _items.Insert(index, item);
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(IndexerName);
        CollectionChanged?.Invoke(
            this,
            new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, index)
        );
    }

    protected override void RemoveItem(int index)
    {
        var removed = _items[index];
        _items.RemoveAt(index);
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(IndexerName);
        CollectionChanged?.Invoke(
            this,
            new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, removed, index)
        );
    }

    protected override void SetItem(int index, T item)
    {
        var old = _items[index];
        _items[index] = item;
        OnPropertyChanged(IndexerName);
        CollectionChanged?.Invoke(
            this,
            new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, item, old, index)
        );
    }

    protected override void ClearItems()
    {
        if (_items.Count == 0)
            return;

        _items.Clear();
        RaiseReset();
    }

    private void RaiseReset()
    {
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(IndexerName);
        CollectionChanged?.Invoke(
            this,
            new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset)
        );
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}