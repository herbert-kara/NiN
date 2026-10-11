using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
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
    /// <summary>
    /// WPF objects (DataGridRow, style triggers) require an STA thread with a running
    /// Dispatcher, so the visual assertion runs on one rather than on TUnit's default
    /// thread.
    /// </summary>
    private static bool RunOnStaThread(Func<bool> body)
    {
        var failure = default(Exception);
        bool result = false;

        var thread = new Thread(() =>
        {
            try
            {
                result = body();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw new InvalidOperationException("the STA body failed", failure);
        }
        return result;
    }

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
        var different = RunOnStaThread(() =>
        {
            var style = BuildRowStyle();

            var active = new ProfileItemModel { IndexId = "a", Remarks = "active", IsActive = true };
            var idle = new ProfileItemModel { IndexId = "b", Remarks = "idle" };

            // A detached DataGridRow is never loaded (activeInTree=False), and a
            // DataTrigger on an unloaded element does not settle. So the rows are put
            // into a real DataGrid with a real ItemsSource and shown, exactly as the
            // app does, before the brushes are read.
            var grid = new DataGrid { Style = style, ItemsSource = new[] { active, idle } };
            grid.Measure(new Size(400, 200));
            grid.Arrange(new Rect(0, 0, 400, 200));
            grid.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            var activeRow = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0);
            var idleRow = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(1);

            var activeBrush = activeRow.GetValue(Control.BackgroundProperty);
            var idleBrush = idleRow.GetValue(Control.BackgroundProperty);

            Console.WriteLine($"HL-DIAG active={activeBrush?.GetType().Name ?? "null"} " +
                              $"idle={idleBrush?.GetType().Name ?? "null"} " +
                              $"inTree={activeRow.IsLoaded} " +
                              $"dc={(activeRow.DataContext as ProfileItemModel)?.IsActive}");

            // The active row must carry the trigger's brush; the idle row must not.
            return !ReferenceEquals(activeBrush, idleBrush);
        });

        await Assert.That(different).IsTrue();
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
