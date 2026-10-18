using System.Data;
using System.Drawing;
using System.Windows.Forms;

namespace TEWUniverse.Studio;

/// <summary>
/// Phase 1 visual shell. Database reads and writes remain in MainForm / DatabaseSession.
/// Worker fields are intentionally shown using their real MDB names until the
/// human-readable TEW IX mapping layer is implemented in Phase 2.
/// </summary>
internal sealed partial class MainForm
{
    private readonly Color _edge = Color.FromArgb(51, 75, 96);
    private readonly Color _surface = Color.FromArgb(22, 36, 51);
    private readonly Color _fieldBg = Color.FromArgb(29, 47, 64);
    private readonly Color _cyan = Color.FromArgb(31, 217, 213);
    private readonly Color _light = Color.FromArgb(232, 244, 249);
    private readonly Label _projectState = new();
    private readonly Label _workingState = new();
    private readonly Label _backupState = new();
    private readonly Label _dirtyState = new();
    private readonly Label _profileRecord = new();
    private readonly Label _biographyPreview = new();
    private string? _lastBackupFile;

    private void BuildShell()
    {
        SuspendLayout();

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 84,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Color.FromArgb(17, 30, 43),
            Padding = new Padding(14, 15, 12, 8)
        };
        AddAction(actions, "Import TEW9.mdb", 154, (_, _) => ImportDatabase());
        AddAction(actions, "Open working project", 176, (_, _) => OpenWorkingProject());
        SetupAction(_saveButton, "Save changes", 146, true, (_, _) => SaveWorker());
        _saveButton.Enabled = false;
        actions.Controls.Add(_saveButton);
        AddAction(actions, "Save copy as...", 140, (_, _) => SaveWorkingCopyAs());
        SetupAction(_backupButton, "Create Backup", 149, false, (_, _) => CreateBackup());
        _backupButton.Enabled = false;
        actions.Controls.Add(_backupButton);
        AddAction(actions, "Settings", 108, (_, _) => OpenSettings());
        SetupAction(_updatesButton, "Check for updates", 155, false,
            async (_, _) => await CheckForUpdatesAsync(true));
        actions.Controls.Add(_updatesButton);
        AddAction(actions, "Update log", 108, (_, _) => OpenUpdateLog());

        var bottom = new Panel
        {
            Dock = DockStyle.Bottom, Height = 56,
            Padding = new Padding(12, 3, 12, 3),
            BackColor = Color.FromArgb(17, 30, 43)
        };
        var footerRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1,
            BackColor = Color.Transparent, Margin = Padding.Empty
        };
        footerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24));
        footerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        footerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 23));
        footerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));
        foreach (var label in new[] { _projectState, _workingState, _backupState, _dirtyState })
        {
            label.Dock = DockStyle.Fill;
            label.AutoEllipsis = true;
            label.ForeColor = _light;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Font = new Font("Segoe UI", 9);
            label.Padding = new Padding(10, 0, 5, 0);
        }
        footerRow.Controls.Add(_projectState, 0, 0);
        footerRow.Controls.Add(_workingState, 1, 0);
        footerRow.Controls.Add(_backupState, 2, 0);
        footerRow.Controls.Add(_dirtyState, 3, 0);
        bottom.Controls.Add(footerRow);

        var workspace = new Panel { Dock = DockStyle.Fill, BackColor = _bg };

        var navigation = new Panel
        {
            Dock = DockStyle.Left, Width = 180,
            BackColor = Color.FromArgb(16, 30, 43),
            Padding = new Padding(10, 15, 10, 10)
        };
        var product = new Label
        {
            Dock = DockStyle.Top, Height = 66,
            Text = "TEW UNIVERSE\nSTUDIO  |  TEW IX",
            ForeColor = _light, Font = new Font("Segoe UI", 11, FontStyle.Bold),
            Padding = new Padding(6, 5, 0, 0)
        };
        var navButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Color.Transparent
        };
        AddModule(navButtons, "Workers", true, null);
        foreach (var module in new[] { "Companies", "Titles", "Events", "Stables",
            "Storylines", "Databases", "Tools" })
        {
            string name = module;
            AddModule(navButtons, name, false, () =>
                MessageBox.Show(this,
                    $"{name} is part of the planned TEW Universe creation suite.\n\n" +
                    "Phase 1 establishes the navigation and editor layout. " +
                    "This module will be connected when its data operations are implemented.",
                    $"{name} module", MessageBoxButtons.OK, MessageBoxIcon.Information));
        }
        AddModule(navButtons, "Settings", false, OpenSettings);

        var navBottom = new Label
        {
            Dock = DockStyle.Bottom, Height = 43,
            Text = "TEW IX DATABASE EDITOR",
            ForeColor = _muted, Font = new Font("Segoe UI", 8, FontStyle.Bold),
            Padding = new Padding(5, 12, 0, 0)
        };
        navigation.Controls.Add(navButtons);
        navigation.Controls.Add(navBottom);
        navigation.Controls.Add(product);

        var browser = new Panel
        {
            Dock = DockStyle.Left, Width = 316,
            BackColor = Color.FromArgb(20, 35, 49),
            Padding = new Padding(12, 12, 12, 12)
        };
        var browserHeader = new Label
        {
            Dock = DockStyle.Top, Height = 48,
            Text = "WORKERS", ForeColor = _light,
            Font = new Font("Segoe UI", 15, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _search.Dock = DockStyle.Top;
        _search.Height = 34;
        _search.Multiline = false;
        _search.BorderStyle = BorderStyle.FixedSingle;
        _search.BackColor = _fieldBg;
        _search.ForeColor = _light;
        _search.PlaceholderText = "Search name or worker ID";
        _search.Font = new Font("Segoe UI", 11);
        _search.TextChanged += (_, _) => FilterWorkers();
        var searchFrame = new Panel
        {
            Dock = DockStyle.Top, Height = 49,
            Padding = new Padding(3, 5, 3, 7)
        };
        searchFrame.Controls.Add(_search);

        _workerCount.Dock = DockStyle.Top;
        _workerCount.Height = 44;
        _workerCount.Text = "No working database loaded";
        _workerCount.ForeColor = _muted;
        _workerCount.Padding = new Padding(1, 9, 0, 0);
        _workerCount.Font = new Font("Segoe UI", 9);

        var filterRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 43, WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.Transparent, Padding = new Padding(0, 4, 0, 3)
        };
        var allWorkers = new Button
        {
            Text = "All workers", Size = new Size(102, 31),
            BackColor = _cyan, ForeColor = Color.FromArgb(12, 35, 42),
            FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        allWorkers.FlatAppearance.BorderSize = 0;
        allWorkers.Click += (_, _) => _search.Clear();
        var futureFilters = new Label
        {
            Text = "Advanced filters: Phase 2", Width = 169, Height = 31,
            ForeColor = _muted, TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Segoe UI", 8)
        };
        filterRow.Controls.Add(allWorkers);
        filterRow.Controls.Add(futureFilters);

        var heading = new Label
        {
            Dock = DockStyle.Top, Height = 33, Text = "NAME                                ID",
            ForeColor = _muted, Font = new Font("Segoe UI", 8, FontStyle.Bold),
            BackColor = Color.FromArgb(26, 46, 63),
            Padding = new Padding(8, 10, 0, 0)
        };
        _workerList.Dock = DockStyle.Fill;
        _workerList.BorderStyle = BorderStyle.None;
        _workerList.BackColor = _surface;
        _workerList.ForeColor = _light;
        _workerList.Font = new Font("Segoe UI", 9);
        _workerList.DrawMode = DrawMode.OwnerDrawFixed;
        _workerList.ItemHeight = 48;
        _workerList.IntegralHeight = false;
        _workerList.DrawItem += DrawWorker;
        _workerList.SelectedIndexChanged += (_, _) => WorkerSelected();
        browser.Controls.Add(_workerList);
        browser.Controls.Add(heading);
        browser.Controls.Add(_workerCount);
        browser.Controls.Add(filterRow);
        browser.Controls.Add(searchFrame);
        browser.Controls.Add(browserHeader);

        var editor = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = _bg,
            Padding = new Padding(14, 10, 13, 11)
        };
        var workerHeader = new Panel
        {
            Dock = DockStyle.Top, Height = 80,
            BackColor = Color.FromArgb(18, 31, 45),
            Padding = new Padding(12, 7, 10, 6)
        };
        _header.Dock = DockStyle.Fill;
        _header.Text = "Choose an existing worker\nSelect a worker in the browser to begin editing.";
        _header.Font = new Font("Segoe UI", 16, FontStyle.Bold);
        _header.ForeColor = _light;
        workerHeader.Controls.Add(_header);

        _tabs.Dock = DockStyle.Fill;
        _tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
        _tabs.SizeMode = TabSizeMode.Fixed;
        _tabs.ItemSize = new Size(128, 43);
        _tabs.Font = new Font("Segoe UI", 10, FontStyle.Bold);
        _tabs.DrawItem += DrawTab;

        var profile = MakeTab("Profile");
        var profileColumns = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2, RowCount = 1,
            BackColor = _bg, Padding = new Padding(0, 8, 0, 0)
        };
        profileColumns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        profileColumns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        profileColumns.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var profileData = BuildFieldSection("PROFILE INFORMATION", "tblWorker");
        profileColumns.Controls.Add(profileData, 0, 0);
        profileColumns.Controls.Add(BuildProfileAside(), 1, 0);
        profile.Controls.Add(profileColumns);
        _tabs.TabPages.Add(profile);

        foreach (var (title, table) in new[] {
            ("Skills", "tblWorkerSkill"),
            ("Popularity", "tblWorkerOver"),
            ("Physical", "tblWorkerPhysical")
        })
        {
            var page = MakeTab(title);
            var section = BuildFieldSection(title.ToUpperInvariant() + "   |   EXISTING WORKER DATA", table);
            page.Controls.Add(section);
            _tabs.TabPages.Add(page);
        }

        var biography = MakeTab("Biography");
        var bioFrame = new Panel
        {
            Dock = DockStyle.Fill, Padding = new Padding(16),
            BackColor = _surface
        };
        var bioTitle = new Label
        {
            Dock = DockStyle.Top, Height = 52,
            Text = "WORKER BIOGRAPHY\nEdit existing biography text and use Save changes.",
            ForeColor = _light,
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };
        _biography.Dock = DockStyle.Fill;
        _biography.Multiline = true;
        _biography.ScrollBars = ScrollBars.Vertical;
        _biography.BorderStyle = BorderStyle.FixedSingle;
        _biography.Font = new Font("Segoe UI", 11);
        _biography.BackColor = _fieldBg;
        _biography.ForeColor = _light;
        _biography.Enabled = false;
        _biography.TextChanged += (_, _) =>
        {
            RefreshBiographyPreview();
            RefreshShellStatus();
        };
        bioFrame.Controls.Add(_biography);
        bioFrame.Controls.Add(bioTitle);
        biography.Controls.Add(bioFrame);
        _tabs.TabPages.Add(biography);

        editor.Controls.Add(_tabs);
        editor.Controls.Add(workerHeader);

        // Keep the editor flexible while both navigation columns remain fixed.
        workspace.Controls.Add(editor);
        workspace.Controls.Add(browser);
        workspace.Controls.Add(navigation);

        var messageStrip = new Panel
        {
            Dock = DockStyle.Bottom, Height = 27,
            Padding = new Padding(12, 2, 12, 1),
            BackColor = Color.FromArgb(21, 39, 53)
        };
        _status.Text = "Ready. No project open.";
        _status.Dock = DockStyle.Fill;
        _status.ForeColor = _muted;
        _status.AutoEllipsis = true;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.Font = new Font("Segoe UI", 8);
        messageStrip.Controls.Add(_status);

        Controls.Add(workspace);
        Controls.Add(bottom);
        Controls.Add(messageStrip);
        Controls.Add(actions);
        RefreshShellStatus();
        ResumeLayout(performLayout: true);
    }

    private void AddAction(FlowLayoutPanel parent, string title, int width, EventHandler click)
    {
        var button = new Button();
        SetupAction(button, title, width, false, click);
        parent.Controls.Add(button);
    }

    private void SetupAction(Button button, string title, int width, bool prominent, EventHandler click)
    {
        button.Text = title;
        button.Size = new Size(width, 47);
        button.Margin = new Padding(0, 0, 9, 0);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = prominent ? _cyan : _edge;
        button.FlatAppearance.BorderSize = 1;
        button.BackColor = prominent ? _cyan : Color.FromArgb(29, 48, 65);
        button.ForeColor = prominent ? Color.FromArgb(10, 32, 42) : _light;
        button.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
        button.Click += click;
    }

    private void AddModule(FlowLayoutPanel parent, string title, bool selected, Action? action)
    {
        var button = new Button
        {
            Text = title,
            TextAlign = ContentAlignment.MiddleLeft,
            Size = new Size(147, 47),
            Margin = new Padding(0, 2, 0, 3),
            Padding = new Padding(13, 0, 0, 0),
            FlatStyle = FlatStyle.Flat,
            FlatAppearance = { BorderSize = selected ? 1 : 0 },
            BackColor = selected ? Color.FromArgb(20, 85, 105) : Color.Transparent,
            ForeColor = selected ? _cyan : _muted,
            Font = new Font("Segoe UI", 10, selected ? FontStyle.Bold : FontStyle.Regular),
            Cursor = Cursors.Hand
        };
        if (action != null) button.Click += (_, _) => action();
        else button.Click += (_, _) => _search.Focus();
        parent.Controls.Add(button);
    }

    private TabPage MakeTab(string name) => new(name)
    {
        UseVisualStyleBackColor = false,
        BackColor = _bg,
        ForeColor = _light,
        Padding = new Padding(5)
    };

    private Panel BuildFieldSection(string title, string table)
    {
        var section = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(9),
            Margin = new Padding(3),
            BackColor = _surface
        };
        var label = new Label
        {
            Text = title,
            Dock = DockStyle.Top, Height = 43,
            ForeColor = _light,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Padding = new Padding(5, 8, 0, 0)
        };
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            BackgroundColor = _surface,
            GridColor = _edge,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            EnableHeadersVisualStyles = false,
            ColumnHeadersHeight = 42,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            RowTemplate = { Height = 37 },
            Font = new Font("Segoe UI", 10),
            DefaultCellStyle =
            {
                BackColor = _surface, ForeColor = _light,
                SelectionBackColor = Color.FromArgb(32, 86, 105),
                SelectionForeColor = Color.White, Padding = new Padding(6, 4, 3, 4)
            },
            AlternatingRowsDefaultCellStyle =
            {
                BackColor = Color.FromArgb(25, 42, 58), ForeColor = _light
            },
            ColumnHeadersDefaultCellStyle =
            {
                BackColor = Color.FromArgb(29, 55, 73), ForeColor = _light,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Alignment = DataGridViewContentAlignment.MiddleLeft
            }
        };
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Field", HeaderText = "DATABASE FIELD",
            ReadOnly = true, FillWeight = 47, SortMode = DataGridViewColumnSortMode.NotSortable
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Value", HeaderText = "VALUE  (EDIT HERE)",
            FillWeight = 53, SortMode = DataGridViewColumnSortMode.NotSortable
        });
        grid.DataError += (_, args) =>
        {
            args.ThrowException = false;
            SetStatus("Invalid value. Correct the selected database field.");
        };
        grid.CellValueChanged += (_, _) => RefreshShellStatus();
        grid.CellEndEdit += (_, _) => RefreshShellStatus();
        _grids[table] = grid;
        section.Controls.Add(grid);
        section.Controls.Add(label);
        return section;
    }

    private Control BuildProfileAside()
    {
        var wrap = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = _bg,
            Padding = new Padding(9, 0, 0, 0)
        };
        var summary = new Panel
        {
            Dock = DockStyle.Top,
            Height = 220,
            BackColor = _surface,
            Padding = new Padding(14)
        };
        var summaryTitle = new Label
        {
            Dock = DockStyle.Top, Height = 43,
            Text = "KEY INFORMATION",
            ForeColor = _cyan,
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };
        _profileRecord.Dock = DockStyle.Fill;
        _profileRecord.ForeColor = _light;
        _profileRecord.Font = new Font("Segoe UI", 10);
        _profileRecord.Text = "Select an existing worker.\n\nWorker ID and editing state will appear here.";
        summary.Controls.Add(_profileRecord);
        summary.Controls.Add(summaryTitle);

        var bio = new Panel
        {
            Dock = DockStyle.Fill, Padding = new Padding(14),
            BackColor = _surface
        };
        var bioTitle = new Label
        {
            Dock = DockStyle.Top, Height = 45,
            Text = "BIOGRAPHY PREVIEW",
            ForeColor = _cyan,
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };
        var bioOpen = new Button
        {
            Dock = DockStyle.Bottom,
            Height = 43,
            Text = "Open Biography Editor",
            BackColor = Color.FromArgb(27, 62, 77),
            ForeColor = _light,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        bioOpen.FlatAppearance.BorderColor = _edge;
        bioOpen.Click += (_, _) => _tabs.SelectedIndex = _tabs.TabPages.Count - 1;
        _biographyPreview.Dock = DockStyle.Fill;
        _biographyPreview.ForeColor = _muted;
        _biographyPreview.Font = new Font("Segoe UI", 10);
        _biographyPreview.Text = "No biography loaded.";
        _biographyPreview.AutoEllipsis = true;
        bio.Controls.Add(_biographyPreview);
        bio.Controls.Add(bioOpen);
        bio.Controls.Add(bioTitle);

        wrap.Controls.Add(bio);
        wrap.Controls.Add(summary);
        return wrap;
    }

    private void DrawTab(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _tabs.TabPages.Count) return;
        var selected = e.Index == _tabs.SelectedIndex;
        using var brush = new SolidBrush(selected ? _cyan : Color.FromArgb(29, 48, 65));
        e.Graphics.FillRectangle(brush, e.Bounds);
        TextRenderer.DrawText(e.Graphics, _tabs.TabPages[e.Index].Text,
            new Font("Segoe UI", 9, FontStyle.Bold), e.Bounds,
            selected ? Color.FromArgb(12, 35, 42) : _light,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private void DrawWorker(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _workerList.Items.Count) return;
        if (_workerList.Items[e.Index] is not WorkerItem worker) return;
        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        using (var brush = new SolidBrush(
            selected ? Color.FromArgb(15, 95, 126) :
            e.Index % 2 == 0 ? _surface : Color.FromArgb(24, 41, 56)))
            e.Graphics.FillRectangle(brush, e.Bounds);

        using (var avatar = new SolidBrush(selected ? _cyan : Color.FromArgb(119, 158, 180)))
            e.Graphics.FillEllipse(avatar, e.Bounds.X + 11, e.Bounds.Y + 15, 16, 16);

        var nameRect = new Rectangle(e.Bounds.X + 37, e.Bounds.Y + 3,
            Math.Max(70, e.Bounds.Width - 105), e.Bounds.Height - 5);
        var idRect = new Rectangle(e.Bounds.Right - 74, e.Bounds.Y + 3,
            66, e.Bounds.Height - 5);
        TextRenderer.DrawText(e.Graphics, worker.Name, _workerList.Font, nameRect,
            selected ? Color.White : _light,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
            TextFormatFlags.SingleLine);
        TextRenderer.DrawText(e.Graphics, worker.Id.ToString(), _workerList.Font, idRect,
            selected ? Color.White : _muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Right |
            TextFormatFlags.SingleLine);
        e.DrawFocusRectangle();
    }

    private void RefreshBiographyPreview()
    {
        var text = _biography.Text.Trim();
        _biographyPreview.Text = text.Length == 0 ? "No biography available." :
            text.Length > 350 ? text[..350] + "..." : text;
    }

    private int PendingEditCount()
    {
        if (_currentWorker == null || _session == null) return 0;
        int count = 0;
        foreach (var grid in _grids.Values)
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.Tag is not FieldRef field) continue;
                var current = Convert.ToString(row.Cells["Value"].FormattedValue) ?? "";
                if (!string.Equals(current, field.Before, StringComparison.Ordinal))
                    count++;
            }
        }
        if (_bioExists && _biography.Text != _originalBio) count++;
        return count;
    }

    private void RefreshShellStatus()
    {
        var loaded = _session != null;
        _projectState.Text = "PROJECT   " +
            (loaded ? Path.GetFileName(_session!.PathOnDisk) : "No project loaded");
        _workingState.Text = "CURRENT COPY   " +
            (loaded ? _session!.PathOnDisk : "Not selected");
        _backupState.Text = "BACKUP   " +
            (_lastBackupFile == null ? "Not created this session" :
                Path.GetFileName(_lastBackupFile));
        var changes = PendingEditCount();
        _dirtyState.Text = changes == 0 ? "●  All changes saved" :
            "●  " + changes + " unsaved change" + (changes == 1 ? "" : "s");
        _dirtyState.ForeColor = changes == 0 ? _accent : Color.FromArgb(255, 193, 90);
        _profileRecord.Text = _currentWorker == null
            ? "No worker selected.\n\nSearch the library to open an existing record."
            : "WORKER ID   " + _currentWorker.Id +
              "\n\nNAME   " + _currentWorker.Name +
              "\n\nWORKING DATABASE   " + (loaded ? Path.GetFileName(_session!.PathOnDisk) : "None") +
              "\n\nEDITING   Existing records only";
    }
}
