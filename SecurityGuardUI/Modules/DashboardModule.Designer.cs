namespace SecurityGuardUI.Modules
{
    partial class DashboardModule
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _refresh.Dispose();
                if (components != null) components.Dispose();
            }
            base.Dispose(disposing);
        }

        private static DevExpress.XtraGrid.Columns.GridColumn Col(string field, string caption, int width, int index)
        {
            return new DevExpress.XtraGrid.Columns.GridColumn { FieldName = field, Caption = caption, Width = width, Visible = true, VisibleIndex = index, Name = "col" + field };
        }

        #region Component Designer generated code

        private void InitializeComponent()
        {
            this.panelTop = new DevExpress.XtraEditors.PanelControl();
            this.labelTitle = new DevExpress.XtraEditors.LabelControl();
            this.labelInfo = new DevExpress.XtraEditors.LabelControl();
            this.buttonRefresh = new DevExpress.XtraEditors.SimpleButton();
            this.tableTiles = new System.Windows.Forms.TableLayoutPanel();
            this.split = new DevExpress.XtraEditors.SplitContainerControl();
            this.labelEvents = new DevExpress.XtraEditors.LabelControl();
            this.gridEvents = new DevExpress.XtraGrid.GridControl();
            this.viewEvents = new DevExpress.XtraGrid.Views.Grid.GridView();
            this.colEventSeverity = Col("Severity", "Önem", 50, 1);
            this.labelTodos = new DevExpress.XtraEditors.LabelControl();
            this.gridTodos = new DevExpress.XtraGrid.GridControl();
            this.viewTodos = new DevExpress.XtraGrid.Views.Grid.GridView();
            this.colTodoLevel = Col("Level", "Önem", 50, 0);
            ((System.ComponentModel.ISupportInitialize)(this.panelTop)).BeginInit();
            this.panelTop.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.split)).BeginInit();
            this.split.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.gridEvents)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.viewEvents)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.gridTodos)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.viewTodos)).BeginInit();
            this.SuspendLayout();
            //
            // panelTop
            //
            this.panelTop.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
            this.panelTop.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelTop.Size = new System.Drawing.Size(1000, 64);
            this.panelTop.Controls.Add(this.labelTitle);
            this.panelTop.Controls.Add(this.labelInfo);
            this.panelTop.Controls.Add(this.buttonRefresh);
            this.labelTitle.Appearance.Font = new System.Drawing.Font("Segoe UI", 14F);
            this.labelTitle.Appearance.Options.UseFont = true;
            this.labelTitle.Location = new System.Drawing.Point(12, 8);
            this.labelTitle.Text = "Panel";
            this.labelInfo.Location = new System.Drawing.Point(12, 40);
            this.labelInfo.Text = "";
            this.buttonRefresh.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonRefresh.Location = new System.Drawing.Point(878, 10);
            this.buttonRefresh.Size = new System.Drawing.Size(110, 26);
            this.buttonRefresh.Text = "Yenile";
            this.buttonRefresh.Click += new System.EventHandler(this.buttonRefresh_Click);
            //
            // tableTiles: 4 x 2 kutucuk
            //
            this.tableTiles.ColumnCount = 5;
            for (int i = 0; i < 5; i++) this.tableTiles.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 20F));
            this.tableTiles.RowCount = 2;
            this.tableTiles.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.tableTiles.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.tableTiles.Dock = System.Windows.Forms.DockStyle.Top;
            this.tableTiles.Padding = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.tableTiles.Size = new System.Drawing.Size(1000, 270);
            //
            // split: solda son olaylar, sağda yapılacaklar
            //
            this.split.Dock = System.Windows.Forms.DockStyle.Fill;
            this.split.Panel1.Controls.Add(this.gridEvents);
            this.split.Panel1.Controls.Add(this.labelEvents);
            this.split.Panel2.Controls.Add(this.gridTodos);
            this.split.Panel2.Controls.Add(this.labelTodos);
            this.split.SplitterPosition = 560;
            this.labelEvents.Dock = System.Windows.Forms.DockStyle.Top;
            this.labelEvents.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
            this.labelEvents.Padding = new System.Windows.Forms.Padding(6, 6, 0, 0);
            this.labelEvents.Size = new System.Drawing.Size(560, 28);
            this.labelEvents.Text = "Son önemli olaylar (7 gün, kritik ve uyarı)";
            this.gridEvents.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gridEvents.MainView = this.viewEvents;
            this.gridEvents.ViewCollection.AddRange(new DevExpress.XtraGrid.Views.Base.BaseView[] { this.viewEvents });
            this.viewEvents.GridControl = this.gridEvents;
            var colTime = Col("Time", "Zaman", 95, 0);
            colTime.DisplayFormat.FormatType = DevExpress.Utils.FormatType.DateTime;
            colTime.DisplayFormat.FormatString = "dd.MM HH:mm";
            foreach (var c in new[] { this.colEventSeverity, this.colTodoLevel })
            {
                c.AppearanceCell.Options.UseTextOptions = true;
                c.AppearanceCell.TextOptions.HAlignment = DevExpress.Utils.HorzAlignment.Near;
            }
            this.viewEvents.Columns.AddRange(new DevExpress.XtraGrid.Columns.GridColumn[] { colTime, this.colEventSeverity, Col("Name", "Olay", 180, 2), Col("Summary", "Özet", 320, 3) });
            this.viewEvents.RowCellStyle += new DevExpress.XtraGrid.Views.Grid.RowCellStyleEventHandler(this.viewEvents_RowCellStyle);
            this.viewEvents.CustomColumnDisplayText += new DevExpress.XtraGrid.Views.Base.CustomColumnDisplayTextEventHandler(this.viewEvents_CustomColumnDisplayText);
            this.viewEvents.DoubleClick += new System.EventHandler(this.viewEvents_DoubleClick);
            this.labelTodos.Dock = System.Windows.Forms.DockStyle.Top;
            this.labelTodos.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
            this.labelTodos.Padding = new System.Windows.Forms.Padding(6, 6, 0, 0);
            this.labelTodos.Size = new System.Drawing.Size(430, 28);
            this.labelTodos.Text = "Yapılması önerilenler";
            this.gridTodos.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gridTodos.MainView = this.viewTodos;
            this.gridTodos.ViewCollection.AddRange(new DevExpress.XtraGrid.Views.Base.BaseView[] { this.viewTodos });
            this.viewTodos.GridControl = this.gridTodos;
            var colText = Col("Text", "Ne yapılmalı", 380, 1);
            colText.ColumnEdit = new DevExpress.XtraEditors.Repository.RepositoryItemMemoEdit();
            this.gridTodos.RepositoryItems.Add(colText.ColumnEdit);
            this.viewTodos.Columns.AddRange(new DevExpress.XtraGrid.Columns.GridColumn[] { this.colTodoLevel, colText });
            this.viewTodos.RowCellStyle += new DevExpress.XtraGrid.Views.Grid.RowCellStyleEventHandler(this.viewTodos_RowCellStyle);
            this.viewTodos.CustomColumnDisplayText += new DevExpress.XtraGrid.Views.Base.CustomColumnDisplayTextEventHandler(this.viewTodos_CustomColumnDisplayText);
            this.viewTodos.DoubleClick += new System.EventHandler(this.viewTodos_DoubleClick);
            //
            // DashboardModule
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.split);
            this.Controls.Add(this.tableTiles);
            this.Controls.Add(this.panelTop);
            this.Name = "DashboardModule";
            this.Size = new System.Drawing.Size(1000, 700);
            ((System.ComponentModel.ISupportInitialize)(this.panelTop)).EndInit();
            this.panelTop.ResumeLayout(false);
            this.panelTop.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.split)).EndInit();
            this.split.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.gridEvents)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.viewEvents)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.gridTodos)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.viewTodos)).EndInit();
            this.ResumeLayout(false);
        }

        #endregion

        private DevExpress.XtraEditors.PanelControl panelTop;
        private DevExpress.XtraEditors.LabelControl labelTitle;
        private DevExpress.XtraEditors.LabelControl labelInfo;
        private DevExpress.XtraEditors.SimpleButton buttonRefresh;
        private System.Windows.Forms.TableLayoutPanel tableTiles;
        private DevExpress.XtraEditors.SplitContainerControl split;
        private DevExpress.XtraEditors.LabelControl labelEvents;
        private DevExpress.XtraGrid.GridControl gridEvents;
        private DevExpress.XtraGrid.Views.Grid.GridView viewEvents;
        private DevExpress.XtraGrid.Columns.GridColumn colEventSeverity;
        private DevExpress.XtraEditors.LabelControl labelTodos;
        private DevExpress.XtraGrid.GridControl gridTodos;
        private DevExpress.XtraGrid.Views.Grid.GridView viewTodos;
        private DevExpress.XtraGrid.Columns.GridColumn colTodoLevel;
    }
}
