using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace FileRenamer
{
    public class MainForm : Form
    {
        // ============ 数据 ============
        private class FileItem
        {
            public string FullPath;
            public string Dir;
            public string OldName;      // 含扩展名
            public string BaseName;     // 不含扩展名
            public string Ext;          // 含点
            public DateTime Modified;
            public long Size;
            public string NewName;      // 预览结果（含扩展名）
        }

        private readonly List<FileItem> _files = new List<FileItem>();
        private List<FileItem> _view = new List<FileItem>();   // 排序后的完整视图（序号按此顺序分配）
        private int[] _rows = new int[0];                      // 当前显示的行 → _view 下标（筛选后）
        private readonly HashSet<FileItem> _conflicts = new HashSet<FileItem>();
        private bool _previewValid;                            // 预览是否与当前规则一致

        // ============ 控件 ============
        private ListView lv;
        private Button btnAddFiles, btnAddFolder, btnRemove, btnClear, btnPreview, btnExecute;
        private CheckBox chkSubDir, chkSeqEnable, chkRepEnable, chkRegex, chkStripOldSeq;
        private NumericUpDown numStart, numDigits, numStep;
        private TextBox txtSep, txtFind, txtReplace, txtFilter;
        private RadioButton rbPrefix, rbSuffix;
        private Label lblStatus, lblCount;
        private ColumnHeader colOld, colNew, colSize, colTime, colDir;
        private Timer _previewTimer;

        // ============ 排序状态（点击列头切换） ============
        private int _sortCol = 0;      // 0=文件名 2=大小 3=修改时间
        private bool _sortAsc = true;

        // ============ 配色 ============
        private static readonly Color Accent = Color.FromArgb(0, 120, 215);
        private static readonly Color AccentDark = Color.FromArgb(0, 90, 170);
        private static readonly Color ChangedText = Color.FromArgb(0, 102, 178);
        private static readonly Color DangerText = Color.FromArgb(200, 38, 38);
        private static readonly Color OkText = Color.FromArgb(19, 122, 58);
        private static readonly Color MutedText = Color.FromArgb(110, 118, 129);
        private const string FilterHint = "筛选列表中的文件名…";

        public MainForm()
        {
            Text = "批量文件重命名工具";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1200, 780);
            MinimumSize = new Size(1000, 680);
            Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.White;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            AllowDrop = true;

            BuildLayout();
            WireLivePreview();
        }

        // ============================================================
        //  布局
        // ============================================================
        private void BuildLayout()
        {
            // ---------- 顶部：标题 + 工具栏（AutoSize，随 DPI 自动增高，避免文字被裁切） ----------
            var top = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(16, 10, 16, 8),
                BackColor = Color.White,
                GrowStyle = TableLayoutPanelGrowStyle.AddRows,
            };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 3; i++) top.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var lblTitle = new Label
            {
                Text = "批量文件重命名",
                Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
                ForeColor = Color.FromArgb(32, 38, 46),
                AutoSize = true,
                Margin = new Padding(2, 0, 0, 0),
            };
            var lblTip = new Label
            {
                Text = "选择或拖入文件 → 设置重命名规则 → 生成预览确认无误 → 执行重命名",
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = MutedText,
                AutoSize = true,
                Margin = new Padding(3, 2, 0, 8),
            };

            var bar = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0),
                Padding = new Padding(0),
                BackColor = Color.White,
            };
            btnAddFiles = MakeBtn("添加文件…", (s, e) => AddFiles(), true);
            btnAddFolder = MakeBtn("添加文件夹…", (s, e) => AddFolder(), false);
            btnRemove = MakeBtn("移除选中", (s, e) => RemoveSelected(), false);
            btnClear = MakeBtn("清空列表", (s, e) => { _files.Clear(); ClearPreview(); RefreshView(); }, false);
            chkSubDir = new CheckBox
            {
                Text = "添加文件夹时包含子文件夹",
                AutoSize = true,
                Margin = new Padding(16, 0, 3, 0),
                ForeColor = Color.FromArgb(60, 66, 74),
            };
            bar.Controls.AddRange(new Control[] { btnAddFiles, btnAddFolder, btnRemove, btnClear, chkSubDir });

            top.Controls.Add(lblTitle, 0, 0);
            top.Controls.Add(lblTip, 0, 1);
            top.Controls.Add(bar, 0, 2);

            // ---------- 底部：操作栏 ----------
            var pnlAction = new Panel { Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(16, 10, 16, 10), BackColor = Color.White };

            var actionRight = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0),
            };
            btnPreview = MakeBtn("生成预览", (s, e) => DoPreview(), false);
            btnPreview.Font = new Font("Microsoft YaHei UI", 10F);
            btnPreview.Padding = new Padding(18, 4, 18, 4);
            btnPreview.MinimumSize = new Size(0, 34);
            btnExecute = MakeBtn("执行重命名", (s, e) => DoExecute(), true);
            btnExecute.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            btnExecute.Padding = new Padding(20, 4, 20, 4);
            btnExecute.MinimumSize = new Size(0, 34);
            actionRight.Controls.Add(btnPreview);
            actionRight.Controls.Add(btnExecute);

            lblStatus = new Label
            {
                Dock = DockStyle.Fill,
                Text = "就绪：请先添加文件，或直接把文件 / 文件夹拖到窗口中。",
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = MutedText,
                AutoEllipsis = true,
                Padding = new Padding(0, 0, 14, 0),
            };
            pnlAction.Controls.Add(lblStatus);
            pnlAction.Controls.Add(actionRight);

            // ---------- 中部：左规则区 + 右文件列表 ----------
            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(16, 8, 16, 10),
                BackColor = Color.FromArgb(247, 249, 251),
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 486));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var left = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Margin = new Padding(0, 0, 12, 0),
                Padding = new Padding(0),
                BackColor = Color.Transparent,
            };
            var cardSeq = BuildSequenceCard();
            var cardRep = BuildReplaceCard();
            left.Controls.Add(cardSeq);
            left.Controls.Add(cardRep);
            left.Resize += (s, e) =>
            {
                int w = left.ClientSize.Width - left.Padding.Horizontal - 6;
                if (w < 360) w = 360;
                cardSeq.Width = w;
                cardRep.Width = w;
            };

            var right = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), BackColor = Color.Transparent };
            var pnlListHeader = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = Color.Transparent };

            var lblList = new Label
            {
                Text = "文件列表",
                Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(32, 38, 46),
                AutoSize = true,
                Location = new Point(2, 5),
            };
            lblCount = new Label
            {
                Text = "",
                ForeColor = MutedText,
                AutoSize = true,
                Location = new Point(92, 8),
            };
            txtFilter = new TextBox
            {
                Width = 220,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                ForeColor = MutedText,
                Text = FilterHint,
            };
            txtFilter.Top = 4;
            txtFilter.Enter += (s, e) => { if (txtFilter.Text == FilterHint) { txtFilter.Text = ""; txtFilter.ForeColor = Color.Black; } };
            txtFilter.Leave += (s, e) => { if (txtFilter.Text.Trim().Length == 0) { txtFilter.Text = FilterHint; txtFilter.ForeColor = MutedText; } };
            pnlListHeader.Controls.AddRange(new Control[] { lblList, lblCount, txtFilter });
            pnlListHeader.Resize += (s, e) => { txtFilter.Left = Math.Max(300, pnlListHeader.Width - txtFilter.Width - 4); };

            lv = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                VirtualMode = true,
                MultiSelect = true,
                GridLines = false,
                HideSelection = false,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White,
                AllowDrop = true,
            };
            colOld = new ColumnHeader { Text = "原文件名", Width = 240 };
            colNew = new ColumnHeader { Text = "新文件名（预览）", Width = 240 };
            colSize = new ColumnHeader { Text = "大小", Width = 84, TextAlign = HorizontalAlignment.Right };
            colTime = new ColumnHeader { Text = "修改时间", Width = 142 };
            colDir = new ColumnHeader { Text = "所在目录", Width = 240 };
            lv.Columns.AddRange(new[] { colOld, colNew, colSize, colTime, colDir });

            lv.RetrieveVirtualItem += (s, e) =>
            {
                var f = _view[_rows[e.ItemIndex]];
                var it = new ListViewItem(new[]
                {
                    f.OldName,
                    f.NewName ?? "",
                    FormatSize(f.Size),
                    f.Modified.ToString("yyyy-MM-dd HH:mm:ss"),
                    f.Dir
                });
                if (_conflicts.Contains(f)) { it.ForeColor = DangerText; it.Font = new Font(lv.Font, FontStyle.Bold); }
                else if (!string.IsNullOrEmpty(f.NewName) && f.NewName != f.OldName) it.ForeColor = ChangedText;
                e.Item = it;
            };
            // 点击列头排序：再次点击同一列切换升/降序
            lv.ColumnClick += (s, e) =>
            {
                int col = e.Column;
                if (col != 0 && col != 2 && col != 3) return;   // 仅 文件名/大小/时间 可排序
                if (_sortCol == col) _sortAsc = !_sortAsc;
                else { _sortCol = col; _sortAsc = true; }
                RefreshView();
                SchedulePreview();
            };
            lv.SelectedIndexChanged += (s, e) => UpdateCountLabel();
            lv.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete && lv.SelectedIndices.Count > 0) { RemoveSelected(); e.Handled = true; } };

            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
            lv.DragEnter += OnDragEnter;
            lv.DragDrop += OnDragDrop;

            right.Controls.Add(lv);
            right.Controls.Add(pnlListHeader);

            body.Controls.Add(left, 0, 0);
            body.Controls.Add(right, 1, 0);

            // 控件添加顺序：Fill 先加，Dock 边缘后加（WinForms 中后加者优先占据边缘）
            Controls.Add(body);
            Controls.Add(pnlAction);
            Controls.Add(top);
        }

        // ---------------- 卡片构件（全部使用 AutoSize，随 DPI 自动增高） ----------------

        private static Panel MakeCard(string title, Control content)
        {
            var card = new Panel
            {
                Width = 452,
                Height = 120,
                AutoSize = false,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White,
                Margin = new Padding(0, 0, 0, 12),
                Padding = new Padding(14, 8, 14, 14),
            };
            var head = new Label
            {
                Text = title,
                Dock = DockStyle.Top,
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(32, 38, 46),
                Padding = new Padding(0, 2, 0, 10),
            };
            content.Dock = DockStyle.Top;
            card.Controls.Add(content);
            card.Controls.Add(head);
            // 卡片高度跟随内容（内容高度由各 AutoSize 行决定）
            EventHandler sync = (s, e) =>
            {
                int h = card.Padding.Vertical + head.PreferredHeight + content.Height + 4;
                if (card.Height != h) card.Height = h;
            };
            content.SizeChanged += sync;
            head.SizeChanged += sync;
            sync(null, EventArgs.Empty);
            return card;
        }

        // 卡片内容容器：自上而下堆叠若干行，行宽固定为卡片内容宽度
        private static FlowLayoutPanel MakeCardBody(params Control[] rows)
        {
            var body = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0),
                Padding = new Padding(0),
            };
            foreach (var r in rows) body.Controls.Add(r);
            return body;
        }

        // 一行：可选标签 + 输入控件 + 可选说明
        private static FlowLayoutPanel MakeRow(string label, Control field, Control extra)
        {
            var row = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 0, 10),
                Padding = new Padding(0),
            };
            if (!string.IsNullOrEmpty(label))
                row.Controls.Add(new Label
                {
                    Text = label,
                    AutoSize = true,
                    ForeColor = Color.FromArgb(72, 80, 90),
                    Margin = new Padding(0, 5, 8, 0),
                });
            if (field != null)
            {
                field.Margin = new Padding(0, 2, 0, 0);
                row.Controls.Add(field);
            }
            if (extra != null)
            {
                extra.Margin = new Padding(14, 5, 0, 0);
                row.Controls.Add(extra);
            }
            return row;
        }

        // 一行开关（复选框 / 单选按钮）
        private static FlowLayoutPanel MakeSwitchRow(params Control[] items)
        {
            var row = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 0, 10),
                Padding = new Padding(0),
            };
            for (int i = 0; i < items.Length; i++)
            {
                items[i].Margin = new Padding(0, 3, i == items.Length - 1 ? 0 : 18, 0);
                row.Controls.Add(items[i]);
            }
            return row;
        }

        private static CheckBox BoldBox(string text)
        {
            return new CheckBox
            {
                Text = text,
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(32, 38, 46),
            };
        }

        private static CheckBox PlainBox(string text)
        {
            return new CheckBox { Text = text, AutoSize = true, Checked = true, ForeColor = Color.FromArgb(60, 66, 74) };
        }

        private static NumericUpDown NumBox(int val, int min, int max)
        {
            return new NumericUpDown { Value = val, Minimum = min, Maximum = max, Width = 96 };
        }

        // ---------------- 功能一：序号 ----------------
        private Panel BuildSequenceCard()
        {
            chkSeqEnable = BoldBox("启用序号规则");
            rbPrefix = new RadioButton { Text = "加在开头", AutoSize = true, Checked = true };
            rbSuffix = new RadioButton { Text = "加在末尾", AutoSize = true };
            var switchRow = MakeSwitchRow(chkSeqEnable, rbPrefix, rbSuffix);

            numStart = NumBox(1, 0, 999999999);
            numDigits = NumBox(4, 1, 10);
            numStep = NumBox(1, 1, 1000);
            txtSep = new TextBox { Text = " - ", Width = 96, BorderStyle = BorderStyle.FixedSingle };
            chkStripOldSeq = PlainBox("先去掉原有的序号开头（如 001_、01-、3.）");

            var row1 = MakeRow("起始序号", numStart, null);
            var row2 = MakeRow("序号位数", numDigits, null);
            var row3 = MakeRow("递增步长", numStep, new Label
            {
                Text = "按列表当前顺序依次编号",
                AutoSize = true,
                ForeColor = MutedText,
                Font = new Font("Microsoft YaHei UI", 8F),
            });
            var row4 = MakeRow("连接符", txtSep, null);

            var body = MakeCardBody(switchRow, row1, row2, row3, row4, chkStripOldSeq);
            return MakeCard("功能一：添加排序序号", body);
        }

        // ---------------- 功能二：字段替换 ----------------
        private Panel BuildReplaceCard()
        {
            chkRepEnable = BoldBox("启用替换规则");
            chkRegex = PlainBox("使用正则表达式");
            chkRegex.Checked = false;
            var switchRow = MakeSwitchRow(chkRepEnable, chkRegex);

            txtFind = new TextBox { BorderStyle = BorderStyle.FixedSingle, Width = 380 };
            txtReplace = new TextBox { BorderStyle = BorderStyle.FixedSingle, Width = 380 };
            var row1 = MakeRow("查找", txtFind, null);
            var row2 = MakeRow("替换为", txtReplace, null);
            var hint = new Label
            {
                Text = "替换在序号之前执行；查找留空则不做替换。",
                AutoSize = true,
                ForeColor = MutedText,
                Font = new Font("Microsoft YaHei UI", 8F),
                Margin = new Padding(0, 2, 0, 0),
            };

            var body = MakeCardBody(switchRow, row1, row2, hint);
            return MakeCard("功能二：部分字段替换重命名", body);
        }

        private Button MakeBtn(string text, EventHandler handler, bool primary)
        {
            var b = new Button
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(12, 3, 12, 3),
                Margin = new Padding(0, 0, 8, 0),
                MinimumSize = new Size(0, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Accent : Color.White,
                ForeColor = primary ? Color.White : Color.FromArgb(40, 46, 54),
                UseVisualStyleBackColor = false,
                Cursor = Cursors.Hand,
            };
            b.FlatAppearance.BorderSize = primary ? 0 : 1;
            b.FlatAppearance.BorderColor = Color.FromArgb(186, 194, 204);
            if (primary) b.FlatAppearance.MouseOverBackColor = AccentDark;
            b.Click += handler;
            return b;
        }

        private NumericUpDown MakeNum(int val, int min, int max)
        {
            return new NumericUpDown { Value = val, Minimum = min, Maximum = max, Width = 92, Margin = new Padding(0, 6, 14, 0) };
        }

        // ============================================================
        //  实时预览联动
        // ============================================================
        private void WireLivePreview()
        {
            _previewTimer = new Timer { Interval = 320 };
            _previewTimer.Tick += (s, e) => { _previewTimer.Stop(); if (_files.Count > 0) RefreshPreview(); };

            EventHandler h = (s, e) => SchedulePreview();
            chkSeqEnable.CheckedChanged += h;
            chkRepEnable.CheckedChanged += h;
            chkRegex.CheckedChanged += h;
            chkStripOldSeq.CheckedChanged += h;
            rbPrefix.CheckedChanged += h;
            rbSuffix.CheckedChanged += h;
            numStart.ValueChanged += h;
            numDigits.ValueChanged += h;
            numStep.ValueChanged += h;
            txtSep.TextChanged += h;
            txtFind.TextChanged += h;
            txtReplace.TextChanged += h;
            txtFilter.TextChanged += (s, e) => { RefreshView(); };
            chkSubDir.CheckedChanged += (s, e) => { };
        }

        private void SchedulePreview()
        {
            _previewValid = false;
            UpdateButtons();
            if (_files.Count == 0) return;
            _previewTimer.Stop();
            _previewTimer.Start();
        }

        // ============================================================
        //  文件加载
        // ============================================================
        private void AddFiles()
        {
            using (var dlg = new OpenFileDialog { Multiselect = true, Title = "选择文件（可多选）", Filter = "所有文件 (*.*)|*.*" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                AddPaths(dlg.FileNames);
            }
        }

        private void AddFolder()
        {
            using (var dlg = new FolderBrowserDialog { Description = "选择文件夹" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var opt = chkSubDir.Checked ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                try
                {
                    var paths = Directory.EnumerateFiles(dlg.SelectedPath, "*.*", opt);
                    AddPaths(paths);
                }
                catch (Exception ex) { MessageBox.Show(this, "读取文件夹失败：" + ex.Message); }
            }
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
            else
                e.Effect = DragDropEffects.None;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            var paths = e.Data == null ? null : e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths == null || paths.Length == 0) return;

            var list = new List<string>();
            var opt = chkSubDir.Checked ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var bad = new StringBuilder();
            foreach (var p in paths)
            {
                try
                {
                    if (Directory.Exists(p)) list.AddRange(Directory.EnumerateFiles(p, "*.*", opt));
                    else if (File.Exists(p)) list.Add(p);
                }
                catch (Exception ex) { bad.AppendLine(p + "：" + ex.Message); }
            }
            AddPaths(list);
            if (bad.Length > 0)
                MessageBox.Show(this, "部分项目无法读取：\r\n" + bad, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void AddPaths(IEnumerable<string> paths)
        {
            var existing = new HashSet<string>(_files.Select(f => f.FullPath), StringComparer.OrdinalIgnoreCase);
            int added = 0;
            Cursor = Cursors.WaitCursor;
            lv.BeginUpdate();
            try
            {
                foreach (var p in paths)
                {
                    if (existing.Contains(p)) continue;
                    FileInfo fi;
                    try { fi = new FileInfo(p); } catch { continue; }
                    if (!fi.Exists) continue;
                    _files.Add(new FileItem
                    {
                        FullPath = fi.FullName,
                        Dir = fi.DirectoryName,
                        OldName = fi.Name,
                        BaseName = Path.GetFileNameWithoutExtension(fi.Name),
                        Ext = fi.Extension,
                        Modified = fi.LastWriteTime,
                        Size = fi.Length,
                    });
                    existing.Add(p);
                    added++;
                }
            }
            finally { Cursor = Cursors.Default; lv.EndUpdate(); }

            ClearPreview();
            RefreshView();
            SetStatus(string.Format("已添加 {0} 个文件，列表共 {1} 个。", added, _files.Count), MutedText);
            if (_files.Count > 0) RefreshPreview();
        }

        private void RemoveSelected()
        {
            if (lv.SelectedIndices.Count == 0) return;
            var toRemove = new HashSet<FileItem>();
            foreach (int row in lv.SelectedIndices)
                if (row >= 0 && row < _rows.Length) toRemove.Add(_view[_rows[row]]);
            _files.RemoveAll(f => toRemove.Contains(f));
            ClearPreview();
            RefreshView();
            RefreshPreview();
        }

        // ============================================================
        //  排序、筛选与视图
        // ============================================================
        private void RefreshView()
        {
            IEnumerable<FileItem> q = _files;
            bool asc = _sortAsc;
            switch (_sortCol)
            {
                case 2: q = asc ? q.OrderBy(f => f.Size) : q.OrderByDescending(f => f.Size); break;
                case 3: q = asc ? q.OrderBy(f => f.Modified) : q.OrderByDescending(f => f.Modified); break;
                default: q = asc ? q.OrderBy(f => f.OldName, StringComparer.CurrentCultureIgnoreCase) : q.OrderByDescending(f => f.OldName, StringComparer.CurrentCultureIgnoreCase); break;
            }
            _view = q.ToList();

            string filter = txtFilter != null ? txtFilter.Text.Trim() : "";
            if (filter.Length == 0 || filter == FilterHint)
            {
                _rows = new int[_view.Count];
                for (int i = 0; i < _rows.Length; i++) _rows[i] = i;
            }
            else
            {
                var rows = new List<int>();
                for (int i = 0; i < _view.Count; i++)
                {
                    var f = _view[i];
                    if (f.OldName.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                        (f.NewName != null && f.NewName.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0))
                        rows.Add(i);
                }
                _rows = rows.ToArray();
            }

            lv.VirtualListSize = _rows.Length;
            lv.Invalidate();
            UpdateCountLabel();
        }

        private void UpdateCountLabel()
        {
            if (lblCount == null) return;
            int sel = lv == null ? 0 : lv.SelectedIndices.Count;
            string s = string.Format("共 {0} 个文件", _files.Count);
            if (_rows.Length != _files.Count) s += string.Format("（显示 {0} 个）", _rows.Length);
            if (sel > 0) s += string.Format(" · 已选 {0} 个", sel);
            lblCount.Text = s;
        }

        private void UpdateButtons()
        {
            if (btnExecute == null) return;
            btnExecute.Enabled = _previewValid && _conflicts.Count == 0 && _view.Count > 0;
            btnPreview.Enabled = _files.Count > 0;
        }

        private void ClearPreview()
        {
            _previewValid = false;
            _conflicts.Clear();
            foreach (var f in _files) f.NewName = null;
            UpdateButtons();
        }

        private void SetStatus(string text, Color color)
        {
            lblStatus.Text = text;
            lblStatus.ForeColor = color;
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024L * 1024) return (bytes / 1024.0).ToString("0.0") + " KB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / 1048576.0).ToString("0.0") + " MB";
            return (bytes / 1073741824.0).ToString("0.00") + " GB";
        }

        // ============================================================
        //  新文件名生成
        // ============================================================
        private static readonly Regex OldSeqPattern = new Regex(@"^\s*\d{1,10}\s*[-_\.、\s]+", RegexOptions.Compiled);

        private string BuildNewName(FileItem f, int index)
        {
            string baseName = f.BaseName;

            // 功能二：字段替换
            if (chkRepEnable.Checked && txtFind.Text.Length > 0)
            {
                try
                {
                    baseName = chkRegex.Checked
                        ? Regex.Replace(baseName, txtFind.Text, txtReplace.Text)
                        : baseName.Replace(txtFind.Text, txtReplace.Text);
                }
                catch (Exception ex)
                {
                    throw new Exception("替换规则错误：" + ex.Message);
                }
            }

            // 功能一：序号
            if (chkSeqEnable.Checked)
            {
                if (chkStripOldSeq.Checked)
                    baseName = OldSeqPattern.Replace(baseName, "");
                long n = (long)numStart.Value + index * (long)numStep.Value;
                string seq = n.ToString("D" + (int)numDigits.Value);
                baseName = rbPrefix.Checked ? seq + txtSep.Text + baseName : baseName + txtSep.Text + seq;
            }

            // 清理非法字符（替换结果可能引入）
            foreach (char c in Path.GetInvalidFileNameChars())
                baseName = baseName.Replace(c, '_');

            return baseName + f.Ext;
        }

        // 重算预览（带冲突检测），不回弹错误对话框；返回是否存在冲突
        private bool RefreshPreview()
        {
            _conflicts.Clear();
            _previewValid = false;
            if (_view.Count == 0) { lv.Invalidate(); SetStatus("列表为空：请先添加文件。", MutedText); UpdateButtons(); return false; }

            if (!chkSeqEnable.Checked && !chkRepEnable.Checked)
            {
                foreach (var f in _view) f.NewName = null;
                lv.Invalidate();
                SetStatus("请至少启用一个功能（序号 或 字段替换）。", Accent);
                UpdateButtons();
                return false;
            }

            try
            {
                for (int i = 0; i < _view.Count; i++)
                    _view[i].NewName = BuildNewName(_view[i], i);
            }
            catch (Exception ex)
            {
                foreach (var f in _view) f.NewName = null;
                lv.Invalidate();
                SetStatus(ex.Message, DangerText);
                UpdateButtons();
                return false;
            }

            // 冲突检测：同目录下新名重复 或 与列表外文件撞名
            foreach (var g in _view.GroupBy(f => f.Dir + "|" + f.NewName, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                foreach (var f in g) _conflicts.Add(f);

            int external = 0;
            var inList = new HashSet<string>(_view.Select(f => f.FullPath), StringComparer.OrdinalIgnoreCase);
            foreach (var f in _view)
            {
                string target = Path.Combine(f.Dir, f.NewName);
                if (!inList.Contains(target) && File.Exists(target) && !target.Equals(f.FullPath, StringComparison.OrdinalIgnoreCase))
                {
                    external++;
                    _conflicts.Add(f);
                }
            }

            int changed = _view.Count(f => !string.Equals(f.NewName, f.OldName, StringComparison.Ordinal));
            lv.Invalidate();

            if (_conflicts.Count > 0)
            {
                SetStatus(string.Format("预览完成：{0} 个文件待改名，其中 {1} 个存在重名冲突（已标红），请调整规则。", changed, _conflicts.Count), DangerText);
                UpdateButtons();
                return false;
            }

            _previewValid = true;
            SetStatus(changed == 0
                ? string.Format("预览完成：{0} 个文件的名称没有变化，无需执行。", _view.Count)
                : string.Format("预览完成：{0} 个文件将按上方顺序重命名，无冲突，可以执行。", changed), OkText);
            UpdateButtons();
            return true;
        }

        private bool DoPreview()
        {
            if (_view.Count == 0) { SetStatus("列表为空：请先添加文件。", MutedText); return false; }
            if (!chkSeqEnable.Checked && !chkRepEnable.Checked)
            {
                MessageBox.Show(this, "请至少启用一个功能（序号 或 字段替换）。");
                return false;
            }
            if (!RefreshPreview())
            {
                if (_conflicts.Count > 0)
                    MessageBox.Show(this, "存在重名冲突（列表中已标红）：请调整规则或移除冲突文件后再执行。",
                        "无法执行", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            return true;
        }

        // ============================================================
        //  执行
        // ============================================================
        private void DoExecute()
        {
            if (_view.Count == 0) return;
            if (!_previewValid && !DoPreview()) return;

            var changed = _view.Where(f => !f.NewName.Equals(f.OldName, StringComparison.Ordinal)).ToList();
            if (changed.Count == 0) { SetStatus("没有需要重命名的文件。", MutedText); return; }

            var r = MessageBox.Show(this,
                string.Format("即将重命名 {0} 个文件，是否继续？", changed.Count),
                "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.OK && r != DialogResult.Yes) return;

            int ok = 0, fail = 0;
            var log = new StringBuilder();
            Cursor = Cursors.WaitCursor;
            btnExecute.Enabled = false;
            try
            {
                // 两阶段重命名，避免列表内互换名字时互相覆盖
                var tempMap = new List<Tuple<FileItem, string>>();
                foreach (var f in changed)
                {
                    try
                    {
                        string tmp = Path.Combine(f.Dir, ".__ren_tmp__" + Guid.NewGuid().ToString("N") + f.Ext);
                        File.Move(f.FullPath, tmp);
                        tempMap.Add(Tuple.Create(f, tmp));
                    }
                    catch (Exception ex) { fail++; log.AppendLine("[失败-暂存] " + f.OldName + "：" + ex.Message); }
                }
                foreach (var t in tempMap)
                {
                    var f = t.Item1;
                    try
                    {
                        string target = Path.Combine(f.Dir, f.NewName);
                        File.Move(t.Item2, target);
                        f.FullPath = target; f.OldName = f.NewName;
                        f.BaseName = Path.GetFileNameWithoutExtension(f.NewName);
                        ok++;
                    }
                    catch (Exception ex) { fail++; log.AppendLine("[失败] " + f.OldName + " → " + f.NewName + "：" + ex.Message); }
                }
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            ClearPreview();
            RefreshView();
            RefreshPreview();
            if (fail == 0) SetStatus(string.Format("完成：成功重命名 {0} 个文件。", ok), OkText);
            else SetStatus(string.Format("完成：成功 {0} 个，失败 {1} 个。", ok, fail), DangerText);
            UpdateButtons();
            if (fail > 0)
                MessageBox.Show(this, log.ToString(), "失败明细", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
