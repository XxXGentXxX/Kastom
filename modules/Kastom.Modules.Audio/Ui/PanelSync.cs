using System.Windows;
using System.Windows.Controls;

namespace Kastom.Modules.Audio.Ui;

/// <summary>
/// Зводить дітей панелі з новим списком даних на місці: наявні елементи оновлюються (той самий екземпляр),
/// нові додаються, зниклі прибираються, порядок береться зі списку. Панель містить лише такі елементи.
/// </summary>
internal static class PanelSync
{
    public static void Apply<TItem, TView>(
        Panel panel,
        Dictionary<string, TView> views,
        IReadOnlyList<TItem> items,
        Func<TItem, string> keyOf,
        Func<TItem, TView> create,
        Action<TView, TItem> update)
        where TView : UIElement
    {
        var keep = items.Select(keyOf).ToHashSet();
        foreach (var gone in views.Keys.Where(k => !keep.Contains(k)).ToList())
        {
            panel.Children.Remove(views[gone]);
            views.Remove(gone);
        }

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var key = keyOf(item);
            if (!views.TryGetValue(key, out var view))
            {
                view = create(item);
                views[key] = view;
                panel.Children.Insert(i, view);
            }
            else if (i >= panel.Children.Count || !ReferenceEquals(panel.Children[i], view))
            {
                panel.Children.Remove(view);
                panel.Children.Insert(i, view);
            }

            update(view, item);
        }
    }
}
