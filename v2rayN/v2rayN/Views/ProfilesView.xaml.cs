using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using v2rayN.Base;
using Point = System.Windows.Point;

namespace v2rayN.Views;

public partial class ProfilesView
{
    private static Config _config;
    private static readonly string _tag = "ProfilesView";
    private bool _fittedToLayout;
    private bool _fittedOnLoad;

    public ProfilesView()
    {
        InitializeComponent();
        lstGroup.MaxHeight = Math.Floor(SystemParameters.WorkArea.Height * 0.20 / 40) * 40;

        _config = AppManager.Instance.Config;

        btnAutofitColumnWidth.Click += BtnAutofitColumnWidth_Click;
        txtServerFilter.PreviewKeyDown += TxtServerFilter_PreviewKeyDown;
        lstProfiles.PreviewKeyDown += LstProfiles_PreviewKeyDown;
        lstProfiles.SelectionChanged += LstProfiles_SelectionChanged;
        lstProfiles.LoadingRow += LstProfiles_LoadingRow;
        lstProfiles.SizeChanged += LstProfiles_SizeChanged;
        lstProfiles.Loaded += LstProfiles_Loaded;
        lstGroup.PreviewMouseRightButtonDown += LstGroup_PreviewMouseRightButtonDown;
        menuSelectAll.Click += menuSelectAll_Click;

        if (_config.UiItem.EnableDragDropSort)
        {
            lstProfiles.AllowDrop = true;
            lstProfiles.PreviewMouseLeftButtonDown += LstProfiles_PreviewMouseLeftButtonDown;
            lstProfiles.MouseMove += LstProfiles_MouseMove;
            lstProfiles.DragEnter += LstProfiles_DragEnter;
            lstProfiles.Drop += LstProfiles_Drop;
        }

        this.WhenActivated(disposables =>
        {
            this.OneWayBind(ViewModel, vm => vm.ProfileItems, v => v.lstProfiles.ItemsSource).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedProfile, v => v.lstProfiles.SelectedItem).DisposeWith(disposables);

            this.OneWayBind(ViewModel, vm => vm.SubItems, v => v.lstGroup.ItemsSource).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedSub, v => v.lstGroup.SelectedItem).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.ServerFilter, v => v.txtServerFilter.Text).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddSubCmd, v => v.btnAddSub).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.EditSubCmd, v => v.btnEditSub).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.EditSubCmd, v => v.menuSubEdit).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddSubCmd, v => v.menuSubAdd).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.DeleteSubCmd, v => v.menuSubDelete).DisposeWith(disposables);

            //servers delete
            this.BindCommand(ViewModel, vm => vm.EditServerCmd, v => v.menuEditServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RemoveServerCmd, v => v.menuRemoveServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RemoveDuplicateServerCmd, v => v.menuRemoveDuplicateServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.CopyServerCmd, v => v.menuCopyServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SetDefaultServerCmd, v => v.menuSetDefaultServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.ShareServerCmd, v => v.menuShareServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.GenGroupAllServerCmd, v => v.menuGenGroupAllServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.GenGroupRegionServerCmd, v => v.menuGenGroupRegionServer).DisposeWith(disposables);

            //servers move
            this.OneWayBind(ViewModel, vm => vm.SubItems, v => v.cmbMoveToGroup.ItemsSource).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedMoveToGroup, v => v.cmbMoveToGroup.SelectedItem).DisposeWith(disposables);

            this.BindCommand(ViewModel, vm => vm.MoveTopCmd, v => v.menuMoveTop).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.MoveUpCmd, v => v.menuMoveUp).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.MoveDownCmd, v => v.menuMoveDown).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.MoveBottomCmd, v => v.menuMoveBottom).DisposeWith(disposables);

            //servers ping
            this.BindCommand(ViewModel, vm => vm.MixedTestServerCmd, v => v.menuMixedTestServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.TcpingServerCmd, v => v.menuTcpingServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RealPingServerCmd, v => v.menuRealPingServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.UdpTestServerCmd, v => v.menuUdpTestServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SpeedServerCmd, v => v.menuSpeedServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SortServerResultCmd, v => v.menuSortServerResult).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RemoveInvalidServerResultCmd, v => v.menuRemoveInvalidServerResult).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.FastRealPingCmd, v => v.btnFastRealPing).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RefreshServerFlagsCmd, v => v.btnRefreshServerFlags).DisposeWith(disposables);

            //servers export
            this.BindCommand(ViewModel, vm => vm.Export2ClientConfigCmd, v => v.menuExport2ClientConfig).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.Export2ClientConfigClipboardCmd, v => v.menuExport2ClientConfigClipboard).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.Export2ShareUrlCmd, v => v.menuExport2ShareUrl).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.Export2ShareUrlBase64Cmd, v => v.menuExport2ShareUrlBase64).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.Export2InnerUriCmd, v => v.menuExport2InnerUri).DisposeWith(disposables);

            ViewModel.ShowYesNoInteraction.RegisterHandler(interaction =>
            {
                var message = interaction.Input;
                var result = UI.ShowYesNo(message) != MessageBoxResult.No;
                interaction.SetOutput(result);
            }).DisposeWith(disposables);

            ViewModel.SaveFileDialogInteraction.RegisterHandler(async interaction =>
            {
                var viewModel = ViewModel;
                if (viewModel is null)
                {
                    interaction.SetOutput(false);
                    return;
                }
                var profileItem = interaction.Input;
                if (UI.SaveFileDialog(out var fileName, "Config|*.json") != true)
                {
                    interaction.SetOutput(false);
                    return;
                }
                await viewModel.Export2ClientConfigResult(fileName, profileItem);
                interaction.SetOutput(true);
            }).DisposeWith(disposables);

            ViewModel.SetClipboardDataInteraction.RegisterHandler(interaction =>
            {
                var strData = interaction.Input;
                WindowsUtils.SetClipboardData(strData);
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);

            ViewModel.ProfilesFocusInteraction.RegisterHandler(interaction =>
            {
                lstProfiles.Focus();
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);

            ViewModel.ShareServerInteraction.RegisterHandler(async interaction =>
            {
                var url = interaction.Input;
                if (url.IsNullOrEmpty())
                {
                    interaction.SetOutput(RxVoid.Default);
                    return;
                }
                await ShareServer(url);
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);

            ViewModel.DispatcherRefreshServersBizInteraction.RegisterHandler(interaction =>
            {
                Application.Current?.Dispatcher.Invoke(RefreshServersBiz, DispatcherPriority.Normal);
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);

            ViewModel.AdjustMainLvColWidthInteraction.RegisterHandler(interaction =>
            {
                AutofitColumnWidth();
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);

            AppEvents.AppExitRequested
              .AsObservable()
              .ObserveOn(RxSchedulers.MainThreadScheduler)
              .Subscribe(_ => StorageUI())
              .DisposeWith(disposables);
        });

        RestoreUI();
    }

    #region Event

    public async Task ShareServer(string url)
    {
        var img = QRCodeWindowsUtils.GetQRCode(url);
        var dialog = new QrcodeView()
        {
            imgQrcode = { Source = img },
            txtContent = { Text = url },
        };

        await DialogHost.Show(dialog, "RootDialog");
    }

    public void RefreshServersBiz()
    {
        if (lstProfiles.SelectedIndex > 0)
        {
            lstProfiles.ScrollIntoView(lstProfiles.SelectedItem, null);
        }

        // The rows are laid out here for the first time, so this is the earliest moment
        // the columns can be fitted against real content. This runs whatever the autofit
        // setting says: a first look that clips every header is a bug, not a preference,
        // so the setting only governs the adjustments that come later (resize, toolbar).
        // Refitting on every refresh would make the columns twitch while a ping streams
        // its delays in, hence the one-shot flag.
        if (!_fittedToLayout && AvailableListWidth() > 0)
        {
            AutofitColumnWidth();
            _fittedToLayout = true;
        }
    }

    private void LstProfiles_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.SelectedProfiles = lstProfiles.SelectedItems.Cast<ProfileItemModel>().ToList();
        }
    }

    private void LstProfiles_LoadingRow(object? sender, DataGridRowEventArgs e)
    {
        e.Row.Header = $" {e.Row.GetIndex() + 1}";
    }

    private void LstProfiles_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_config.UiItem.DoubleClick2Activate)
        {
            ViewModel?.SetDefaultServer();
        }
        else
        {
            ViewModel?.EditServerAsync();
        }
    }

    private void LstProfiles_ColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not DataGridColumnHeader colHeader || colHeader.TabIndex < 0 || colHeader.Column == null)
        {
            return;
        }

        var colName = ((MyDGTextColumn)colHeader.Column).ExName;
        ViewModel?.SortServer(colName);
    }

    private void menuSelectAll_Click(object sender, RoutedEventArgs e)
    {
        lstProfiles.SelectAll();
    }

    /// <summary>
    /// A right-click on a chip must act on THAT chip. WPF does not move the
    /// selection on a right-click, so without this the menu would edit or delete
    /// whichever group happened to be selected -- and several groups can share
    /// a name, so the user cannot tell them apart afterwards.
    /// </summary>
    private void LstGroup_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // ContainerFromElement is the built-in way to get the chip that was hit;
        // walking the visual tree by hand would throw on some hit targets.
        var chip = ItemsControl.ContainerFromElement(lstGroup, e.OriginalSource as DependencyObject) as ListBoxItem;
        if (chip?.DataContext is SubItem sub)
        {
            lstGroup.SelectedItem = sub;
        }
    }

    private void LstProfiles_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
        {
            switch (e.Key)
            {
                case Key.A:
                    menuSelectAll_Click(null, null);
                    break;

                case Key.C:
                    ViewModel?.Export2ShareUrlAsync(false);
                    break;

                case Key.D:
                    ViewModel?.EditServerAsync();
                    break;

                case Key.F:
                    ViewModel?.ShareServerAsync();
                    break;

                case Key.O:
                    ViewModel?.ServerSpeedtest(ESpeedActionType.Tcping);
                    break;

                case Key.R:
                    ViewModel?.ServerSpeedtest(ESpeedActionType.Realping);
                    break;

                case Key.T:
                    ViewModel?.ServerSpeedtest(ESpeedActionType.Speedtest);
                    break;

                case Key.E:
                    ViewModel?.ServerSpeedtest(ESpeedActionType.Mixedtest);
                    break;
            }
        }
        else
        {
            switch (e.Key)
            {
                case Key.Enter:
                    //case Key.Return:
                    ViewModel?.SetDefaultServer();
                    break;

                case Key.Delete:
                case Key.Back:
                    ViewModel?.RemoveServerAsync();
                    break;

                case Key.T:
                    ViewModel?.MoveServer(EMove.Top);
                    break;

                case Key.U:
                    ViewModel?.MoveServer(EMove.Up);
                    break;

                case Key.D:
                    ViewModel?.MoveServer(EMove.Down);
                    break;

                case Key.B:
                    ViewModel?.MoveServer(EMove.Bottom);
                    break;

                case Key.Escape:
                    ViewModel?.ServerSpeedtestStop();
                    break;
            }
        }
    }

    private void BtnAutofitColumnWidth_Click(object sender, RoutedEventArgs e)
    {
        AutofitColumnWidth();
    }

    // WPF sizes an Auto column from its cells alone, so "Delay (ms)" above a "-1" cell
    // was cut to "Dela" and the columns together stopped short of the window, leaving a
    // dead band on the right. Fit the header too, then spend the leftover width.
    private void AutofitColumnWidth()
    {
        try
        {
            var columns = lstProfiles.Columns.Where(t => t.Visibility == Visibility.Visible).ToList();
            if (columns.Count == 0)
            {
                return;
            }

            foreach (var it in columns)
            {
                it.Width = new DataGridLength(1, DataGridLengthUnitType.Auto);
            }
            // Measure now instead of waiting for the next layout pass, otherwise the
            // grid is drawn once at Auto and visibly jumps when the fit lands.
            lstProfiles.UpdateLayout();

            var content = columns.Select(t => t.ActualWidth).ToArray();
            var header = columns.Select(t => MeasureHeader(t)).ToArray();
            var widths = ColumnWidthFit.Fit(content, header, AvailableListWidth());
            for (var i = 0; i < columns.Count; i++)
            {
                columns[i].Width = new DataGridLength(widths[i]);
            }
            // MeasureHeader just measured those headers unconstrained; re-lay them out
            // under the widths they were given so nothing is drawn from that pass.
            lstProfiles.UpdateLayout();

            // The fit is arithmetic on edges it measured; the grid is the truth. On the
            // first layout the vertical scrollbar is not switched on yet, so the room came
            // out a few pixels wide and the grid answered with a horizontal scrollbar under
            // the last row. Whatever the arithmetic believed, take the scroll it still
            // offers off the widest column - never below that column's own header.
            for (var guard = 0; guard < 3; guard++)
            {
                var overflow = FindDescendant<ScrollViewer>(lstProfiles)?.ScrollableWidth ?? 0;
                if (overflow <= 0.5)
                {
                    break;
                }

                var largest = -1;
                for (var i = 0; i < columns.Count; i++)
                {
                    if (columns[i].ActualWidth > ColumnWidthFit.Floor(header[i])
                        && (largest < 0 || columns[i].ActualWidth > columns[largest].ActualWidth))
                    {
                        largest = i;
                    }
                }
                if (largest < 0)
                {
                    break;
                }

                var take = Math.Min(overflow, columns[largest].ActualWidth - ColumnWidthFit.Floor(header[largest]));
                if (take <= 0)
                {
                    break;
                }
                columns[largest].Width = new DataGridLength(columns[largest].ActualWidth - take);
                lstProfiles.UpdateLayout();
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    // MaterialDesign pads the header on both sides and keeps room for the sort arrow;
    // without an allowance here a header looks fitted while its last letter is clipped.
    private const double HeaderChrome = 42;

    private double MeasureHeader(DataGridColumn column)
    {
        // The realized header is the only place the real font, padding and sort
        // arrow are all present; a bare text probe disagreed with it by enough to
        // leave "Remarks" at 58px and trim it to "R..".
        var measured = MeasureRealizedHeader(column);
        if (column.Header is not string text || text.Length == 0)
        {
            return measured;
        }

        var probe = new TextBlock
        {
            Text = text,
            FontFamily = lstProfiles.FontFamily,
            FontSize = lstProfiles.FontSize,
            FontWeight = FontWeights.SemiBold
        };
        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        // Header cells that are scrolled out of view have no realized element.
        return Math.Max(measured, Math.Ceiling(probe.DesiredSize.Width) + HeaderChrome);
    }

    private double MeasureRealizedHeader(DataGridColumn column)
    {
        var header = FindHeader(lstProfiles, column);
        if (header == null)
        {
            return 0;
        }

        header.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return Math.Ceiling(header.DesiredSize.Width);
    }

    private static DataGridColumnHeader? FindHeader(DependencyObject root, DataGridColumn column)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is DataGridColumnHeader header && ReferenceEquals(header.Column, column))
            {
                return header;
            }

            var found = FindHeader(child, column);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static T? FindDescendant<T>(DependencyObject root, Func<T, bool>? accept = null)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match && (accept == null || accept(match)))
            {
                return match;
            }

            var found = FindDescendant(child, accept);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private double AvailableListWidth()
    {
        // The room the columns have is the distance from the row numbers to the vertical
        // scrollbar's own left edge. The scroll viewer's viewport reaches a few pixels
        // further, under the themed scrollbar, and a fit that trusted it left the columns
        // summing past the room they had, which is what switched the horizontal scrollbar on.
        var header = FindDescendant<DataGridRowHeader>(lstProfiles);
        var bar = FindDescendant<ScrollBar>(lstProfiles,
            b => b.Orientation == Orientation.Vertical && b.IsVisible);
        if (header != null && bar != null && bar.ActualWidth > 0)
        {
            var left = bar.TransformToAncestor(lstProfiles).Transform(default(Point)).X;
            var right = header.TransformToAncestor(lstProfiles).Transform(new Point(header.ActualWidth, 0)).X;
            var room = left - right;
            if (room > 0)
            {
                return room;
            }
        }

        var viewport = FindDescendant<ScrollViewer>(lstProfiles)?.ViewportWidth ?? 0;
        var width = viewport - (header?.ActualWidth ?? 0);
        return width > 0 ? width : 0;
    }

    // The one-shot fit can run while the grid is still building its template, before the
    // vertical scrollbar has a width to measure against - which is why its arithmetic came
    // out wide. Loaded is after the template is real, so the fit there starts from the
    // scrollbar's own edge. It runs once; later changes belong to the resize path.
    private void LstProfiles_Loaded(object sender, RoutedEventArgs e)
    {
        if (_fittedOnLoad || lstProfiles.Items.Count == 0)
        {
            return;
        }

        _fittedOnLoad = true;
        AutofitColumnWidth();
    }

    // A window that grew leaves the fitted columns short of its new edge; one that shrank
    // would force a horizontal scrollbar. Refit on a real change rather than on every
    // pixel of a drag, which is what makes a live resize feel sticky.
    private void LstProfiles_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_config?.UiItem.EnableAutoAdjustMainLvColWidth != true
            || lstProfiles.Items.Count == 0
            || Math.Abs(e.NewSize.Width - e.PreviousSize.Width) < 24)
        {
            return;
        }

        _fittedToLayout = true;
        AutofitColumnWidth();
    }

    private void TxtServerFilter_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Return)
        {
            ViewModel?.RefreshServers();
        }
    }

    #endregion Event

    #region UI

    private void RestoreUI()
    {
        try
        {
            var lvColumnItem = _config.UiItem.MainColumnItem.OrderBy(t => t.Index).ToList();
            var displayIndex = 0;

            // A width saved from the content-only Auto layout is narrower than its own
            // header - "Dela" over "Delay (ms)" - and applying it is what brought the
            // clipped look back on every start. That is a measurement artifact, not a
            // layout choice: keep the saved order and visibility, drop the widths and let
            // the first fit derive them from the header and the real cells.
            var savedWidthsAreReadable = lvColumnItem
                .Where(t => t.Width >= 0)
                .Join(lstProfiles.Columns.Cast<MyDGTextColumn>(), t => t.Name, c => c.ExName, (t, c) => (t.Width, Header: MeasureHeader(c)))
                .All(p => p.Width >= p.Header);
            if (savedWidthsAreReadable && lvColumnItem.Any(t => t.Width >= 0))
            {
                _fittedToLayout = true;
            }

            foreach (var item in lvColumnItem)
            {
                foreach (var item2 in lstProfiles.Columns.Cast<MyDGTextColumn>())
                {
                    if (item2.ExName == item.Name)
                    {
                        if (item.Width < 0)
                        {
                            item2.Visibility = Visibility.Hidden;
                        }
                        else
                        {
                            item2.Width = savedWidthsAreReadable
                                ? item.Width
                                : new DataGridLength(1, DataGridLengthUnitType.Auto);
                            item2.DisplayIndex = displayIndex++;
                        }
                        if (item.Name.StartsWith("to", StringComparison.CurrentCultureIgnoreCase))
                        {
                            item2.Visibility = _config.GuiItem.EnableStatistics ? Visibility.Visible : Visibility.Hidden;
                        }
                        if (item.Name.Equals("IpInfo", StringComparison.CurrentCultureIgnoreCase))
                        {
                            item2.Visibility = _config.SpeedTestItem.IPAPIUrl.IsNotEmpty() && !_config.UiItem.HideColumnIpInfo ? Visibility.Visible : Visibility.Hidden;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    private void StorageUI()
    {
        try
        {
            List<ColumnItem> lvColumnItem = [];
            foreach (var item2 in lstProfiles.Columns.Cast<MyDGTextColumn>())
            {
                lvColumnItem.Add(new()
                {
                    Name = item2.ExName,
                    Width = (int)(item2.Visibility == Visibility.Visible ? item2.ActualWidth : -1),
                    Index = item2.DisplayIndex
                });
            }
            _config.UiItem.MainColumnItem = lvColumnItem;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    #endregion UI

    #region Drag and Drop

    private Point startPoint = new();
    private int startIndex = -1;
    private readonly string formatData = "ProfileItemModel";

    /// <summary>
    /// Helper to search up the VisualTree
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="current"></param>
    /// <returns></returns>
    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        do
        {
            if (current is T)
            {
                return (T)current;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        while (current != null);
        return null;
    }

    private void LstProfiles_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Get current mouse position
        startPoint = e.GetPosition(null);
    }

    private void LstProfiles_MouseMove(object sender, MouseEventArgs e)
    {
        // Get the current mouse position
        var mousePos = e.GetPosition(null);
        var diff = startPoint - mousePos;

        if (e.LeftButton == MouseButtonState.Pressed &&
            (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                   Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance))
        {
            // Get the dragged Item
            if (sender is not DataGrid listView)
            {
                return;
            }

            var listViewItem = FindAncestor<DataGridRow>((DependencyObject)e.OriginalSource);
            if (listViewItem == null)
            {
                return;           // Abort
            }
            // Find the data behind the ListViewItem
            var item = (ProfileItemModel)listView.ItemContainerGenerator.ItemFromContainer(listViewItem);
            if (item == null)
            {
                return;                   // Abort
            }
            // Initialize the drag & drop operation
            startIndex = lstProfiles.SelectedIndex;
            DataObject dragData = new(formatData, item);
            DragDrop.DoDragDrop(listViewItem, dragData, DragDropEffects.Copy | DragDropEffects.Move);
        }
    }

    private void LstProfiles_DragEnter(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(formatData) || sender != e.Source)
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private void LstProfiles_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(formatData) && sender == e.Source)
        {
            // Get the drop Item destination
            if (sender is not DataGrid listView)
            {
                return;
            }

            var listViewItem = FindAncestor<DataGridRow>((DependencyObject)e.OriginalSource);
            if (listViewItem == null)
            {
                // Abort
                e.Effects = DragDropEffects.None;
                return;
            }
            // Find the data behind the Item
            var item = (ProfileItemModel)listView.ItemContainerGenerator.ItemFromContainer(listViewItem);
            if (item == null)
            {
                return;
            }
            // Move item into observable collection
            // (this will be automatically reflected to lstView.ItemsSource)
            e.Effects = DragDropEffects.Move;

            ViewModel?.MoveServerTo(startIndex, item);

            startIndex = -1;
        }
    }

    #endregion Drag and Drop
}
