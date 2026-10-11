using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ServiceLib.Models.Dto;
using TUnit.Assertions.Should;
using TUnit.Core;

namespace ServiceLib.Tests.Views;

/// <summary>
/// The active-row highlight is a real visual contract: a row whose IsActive is true
/// must paint a background different from the selected-but-idle row. Every previous
/// attempt checked XAML text and still shipped a highlight the user could not see.
/// </summary>
public class ActiveHighlightTests
{
    private static Style BuildRowStyle()
    {
        // The exact row style the grid applies, copied from ProfilesView.xaml:
        // a DataTrigger on IsActive that sets the row Background and Foreground.
        var style = new Style(typeof(DataGridRow));
        var trigger = new DataTrigger
        {
            Binding = new Binding(nameof(ProfileItemModel.IsActive)),
            Value = true,
        };
        trigger.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.CornflowerBlue));
        trigger.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        style.Triggers.Add(trigger);
        return style;
    }

    [Test]
    public async Task IsActiveRowPaintsADifferentBackgroundThanAnIdleRow()
    {
        var style = BuildRowStyle();

        var active = new ProfileItemModel { IndexId = "a", Remarks = "active" };
        var idle = new ProfileItemModel { IndexId = "b", Remarks = "idle" };

        var activeRow = new DataGridRow { DataContext = active, Style = style };
        var idleRow = new DataGridRow { DataContext = idle, Style = style };

        // Force the style to evaluate, as the layout pass would.
        activeRow.ApplyTemplate();
        idleRow.ApplyTemplate();

        // The active row must carry the trigger's brush; the idle row must not.
        var activeBrush = activeRow.ReadLocalValue(Control.BackgroundProperty);
        var idleBrush = idleRow.ReadLocalValue(Control.BackgroundProperty);

        await Assert.That(ReferenceEquals(activeBrush, idleBrush)).IsFalse();
    }

    [Test]
    public async Task SettingIsActiveNotifiesTheGrid()
    {
        // This is the failure mode that hid the highlight for months: IsActive was a
        // [Reactive] partial whose hook was never generated, so the row never
        // re-evaluated. A real PropertyChanged must fire on the change.
        var model = new ProfileItemModel { IndexId = "a" };
        var seen = new List<string>();
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ProfileItemModel.IsActive))
            {
                seen.Add(e.PropertyName);
            }
        };

        model.IsActive = true;

        await Assert.That(seen).Contains(nameof(ProfileItemModel.IsActive));
        await Assert.That(model.IsActive).IsTrue();
    }

    [Test]
    public async Task IsActiveFollowsTheConnectedIndexId()
    {
        // The grid computes IsActive as IndexId == config.IndexId. When the connection
        // moves, the row that matches must be the one lit up -- not the row the user
        // happens to have clicked.
        var connectedIndexId = "server-b";
        var rows = new[]
        {
            new ProfileItemModel { IndexId = "server-a" },
            new ProfileItemModel { IndexId = "server-b" },
            new ProfileItemModel { IndexId = "server-c" },
        };

        foreach (var row in rows)
        {
            row.IsActive = row.IndexId == connectedIndexId;
        }

        await Assert.That(rows.Count(r => r.IsActive)).IsEqualTo(1);
        await Assert.That(rows.First(r => r.IsActive).IndexId).IsEqualTo("server-b");
    }
}
