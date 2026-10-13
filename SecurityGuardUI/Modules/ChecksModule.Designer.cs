namespace SecurityGuardUI.Modules
{
    partial class ChecksModule
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _poll.Dispose();
                if (components != null) components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.panelTop = new DevExpress.XtraEditors.PanelControl();
            this.labelTitle = new DevExpress.XtraEditors.LabelControl();
            this.labelRun = new DevExpress.XtraEditors.LabelControl();
            this.comboView = new DevExpress.XtraEditors.ImageComboBoxEdit();
            this.checkIgnored = new DevExpress.XtraEditors.CheckEdit();
            this.buttonRun = new DevExpress.XtraEditors.SimpleButton();
            this.buttonExport = new DevExpress.XtraEditors.SimpleButton();
            this.gridControl = new DevExpress.XtraGrid.GridControl();
            this.gridView = new DevExpress.XtraGrid.Views.Grid.GridView();
            this.colStatus = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colNew = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colArea = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colTitle = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colSummary = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colScript = new DevExpress.XtraGrid.Columns.GridColumn();
            this.splitterDetail = new DevExpress.XtraEditors.SplitterControl();
            this.panelDetail = new DevExpress.XtraEditors.PanelControl();
            this.splitDetail = new DevExpress.XtraEditors.SplitContainerControl();
            this.memoDetail = new DevExpress.XtraEditors.MemoEdit();
            this.panelDetailBar = new DevExpress.XtraEditors.PanelControl();
            this.buttonIgnore = new DevExpress.XtraEditors.SimpleButton();
            this.sqlScript = new SecurityGuardUI.Controls.SqlCodeEditor();
            this.memoScript = new DevExpress.XtraEditors.MemoEdit();
            this.labelNoScript = new DevExpress.XtraEditors.LabelControl();
            this.panelScriptBar = new DevExpress.XtraEditors.PanelControl();
            this.labelScriptTitle = new DevExpress.XtraEditors.LabelControl();
            this.buttonCopyScript = new DevExpress.XtraEditors.SimpleButton();
            this.labelStatus = new DevExpress.XtraEditors.LabelControl();
            ((System.ComponentModel.ISupportInitialize)(this.panelTop)).BeginInit();
            this.panelTop.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.comboView.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.checkIgnored.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.gridControl)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.gridView)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.panelDetail)).BeginInit();
            this.panelDetail.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitDetail)).BeginInit();
            this.splitDetail.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.memoDetail.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.panelDetailBar)).BeginInit();
            this.panelDetailBar.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.memoScript.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.panelScriptBar)).BeginInit();
            this.panelScriptBar.SuspendLayout();
            this.SuspendLayout();
            //
            // panelTop
            //
            this.panelTop.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
            this.panelTop.Controls.Add(this.labelTitle);
            this.panelTop.Controls.Add(this.labelRun);
            this.panelTop.Controls.Add(this.comboView);
            this.panelTop.Controls.Add(this.checkIgnored);
            this.panelTop.Controls.Add(this.buttonRun);
            this.panelTop.Controls.Add(this.buttonExport);
            this.panelTop.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelTop.Location = new System.Drawing.Point(0, 0);
            this.panelTop.Name = "panelTop";
            this.panelTop.Size = new System.Drawing.Size(1000, 84);
            this.panelTop.TabIndex = 0;
            //
            // labelTitle
            //
            this.labelTitle.Appearance.Font = new System.Drawing.Font("Segoe UI", 14F);
            this.labelTitle.Appearance.Options.UseFont = true;
            this.labelTitle.Location = new System.Drawing.Point(12, 8);
            this.labelTitle.Name = "labelTitle";
            this.labelTitle.Size = new System.Drawing.Size(180, 25);
            this.labelTitle.TabIndex = 0;
            this.labelTitle.Text = "Performans Önerileri";
            //
            // labelRun
            //
            this.labelRun.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.labelRun.Appearance.Options.UseForeColor = true;
            this.labelRun.AutoEllipsis = true;
            this.labelRun.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
            this.labelRun.Location = new System.Drawing.Point(12, 51);
            this.labelRun.Name = "labelRun";
            this.labelRun.Size = new System.Drawing.Size(590, 16);
            this.labelRun.TabIndex = 1;
            //
            // comboView
            //
            this.comboView.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.comboView.Location = new System.Drawing.Point(612, 48);
            this.comboView.Name = "comboView";
            this.comboView.Properties.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
            new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo)});
            this.comboView.Size = new System.Drawing.Size(200, 20);
            this.comboView.TabIndex = 2;
            this.comboView.SelectedIndexChanged += new System.EventHandler(this.comboView_SelectedIndexChanged);
            //
            // checkIgnored
            //
            this.checkIgnored.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.checkIgnored.Location = new System.Drawing.Point(822, 48);
            this.checkIgnored.Name = "checkIgnored";
            this.checkIgnored.Properties.Caption = "Görmezden gelinenler";
            this.checkIgnored.Size = new System.Drawing.Size(166, 20);
            this.checkIgnored.TabIndex = 3;
            this.checkIgnored.ToolTip = "Görmezden gelinen maddeleri de üstü çizili olarak göster.";
            this.checkIgnored.CheckedChanged += new System.EventHandler(this.checkIgnored_CheckedChanged);
            //
            // buttonRun
            //
            this.buttonRun.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonRun.Location = new System.Drawing.Point(732, 10);
            this.buttonRun.Name = "buttonRun";
            this.buttonRun.Size = new System.Drawing.Size(140, 26);
            this.buttonRun.TabIndex = 4;
            this.buttonRun.Text = "Şimdi Tara";
            this.buttonRun.ToolTip = "Taramayı hemen başlatır. Tarama sadece okur, sunucuda hiçbir şeyi değiştirmez; birkaç dakika sürebilir.";
            this.buttonRun.Click += new System.EventHandler(this.buttonRun_Click);
            //
            // buttonExport
            //
            this.buttonExport.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonExport.Location = new System.Drawing.Point(878, 10);
            this.buttonExport.Name = "buttonExport";
            this.buttonExport.Size = new System.Drawing.Size(110, 26);
            this.buttonExport.TabIndex = 5;
            this.buttonExport.Text = "Excel\'e Aktar";
            this.buttonExport.Click += new System.EventHandler(this.buttonExport_Click);
            //
            // gridControl
            //
            this.gridControl.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gridControl.Location = new System.Drawing.Point(0, 84);
            this.gridControl.MainView = this.gridView;
            this.gridControl.Name = "gridControl";
            this.gridControl.Size = new System.Drawing.Size(1000, 304);
            this.gridControl.TabIndex = 1;
            this.gridControl.ViewCollection.AddRange(new DevExpress.XtraGrid.Views.Base.BaseView[] {
            this.gridView});
            //
            // gridView
            //
            this.gridView.Columns.AddRange(new DevExpress.XtraGrid.Columns.GridColumn[] {
            this.colStatus,
            this.colNew,
            this.colArea,
            this.colTitle,
            this.colSummary,
            this.colScript});
            this.gridView.GridControl = this.gridControl;
            this.gridView.Name = "gridView";
            this.gridView.RowCellStyle += new DevExpress.XtraGrid.Views.Grid.RowCellStyleEventHandler(this.gridView_RowCellStyle);
            this.gridView.FocusedRowChanged += new DevExpress.XtraGrid.Views.Base.FocusedRowChangedEventHandler(this.gridView_FocusedRowChanged);
            //
            // colStatus
            //
            this.colStatus.Caption = "Durum";
            this.colStatus.FieldName = "Status";
            this.colStatus.Name = "colStatus";
            this.colStatus.OptionsColumn.AllowSort = DevExpress.Utils.DefaultBoolean.False;
            this.colStatus.Visible = true;
            this.colStatus.VisibleIndex = 0;
            this.colStatus.Width = 80;
            //
            // colNew
            //
            this.colNew.Caption = "Yeni";
            this.colNew.FieldName = "New";
            this.colNew.Name = "colNew";
            this.colNew.ToolTip = "Bir önceki taramada yoktu veya daha iyi durumdaydı.";
            this.colNew.Visible = true;
            this.colNew.VisibleIndex = 1;
            this.colNew.Width = 40;
            //
            // colArea
            //
            this.colArea.Caption = "Nerede";
            this.colArea.FieldName = "Area";
            this.colArea.Name = "colArea";
            this.colArea.Visible = true;
            this.colArea.VisibleIndex = 2;
            this.colArea.Width = 130;
            //
            // colTitle
            //
            this.colTitle.Caption = "Konu";
            this.colTitle.FieldName = "Title";
            this.colTitle.Name = "colTitle";
            this.colTitle.Visible = true;
            this.colTitle.VisibleIndex = 3;
            this.colTitle.Width = 220;
            //
            // colSummary
            //
            this.colSummary.Caption = "Sonuç";
            this.colSummary.FieldName = "Summary";
            this.colSummary.Name = "colSummary";
            this.colSummary.Visible = true;
            this.colSummary.VisibleIndex = 4;
            this.colSummary.Width = 480;
            //
            // colScript
            //
            this.colScript.Caption = "Script";
            this.colScript.FieldName = "Script";
            this.colScript.Name = "colScript";
            this.colScript.ToolTip = "Hazır düzeltme scripti var mı.";
            this.colScript.Visible = true;
            this.colScript.VisibleIndex = 5;
            this.colScript.Width = 50;
            //
            // splitterDetail
            //
            this.splitterDetail.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.splitterDetail.Location = new System.Drawing.Point(0, 388);
            this.splitterDetail.Name = "splitterDetail";
            this.splitterDetail.Size = new System.Drawing.Size(1000, 6);
            this.splitterDetail.TabIndex = 2;
            this.splitterDetail.TabStop = false;
            //
            // panelDetail
            //
            this.panelDetail.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
            this.panelDetail.Controls.Add(this.splitDetail);
            this.panelDetail.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panelDetail.Location = new System.Drawing.Point(0, 394);
            this.panelDetail.Name = "panelDetail";
            this.panelDetail.Size = new System.Drawing.Size(1000, 280);
            this.panelDetail.TabIndex = 3;
            //
            // splitDetail
            //
            this.splitDetail.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitDetail.Location = new System.Drawing.Point(0, 0);
            this.splitDetail.Name = "splitDetail";
            this.splitDetail.Panel1.Controls.Add(this.memoDetail);
            this.splitDetail.Panel1.Controls.Add(this.panelDetailBar);
            this.splitDetail.Panel1.Text = "Panel1";
            this.splitDetail.Panel2.Controls.Add(this.sqlScript);
            this.splitDetail.Panel2.Controls.Add(this.memoScript);
            this.splitDetail.Panel2.Controls.Add(this.labelNoScript);
            this.splitDetail.Panel2.Controls.Add(this.panelScriptBar);
            this.splitDetail.Panel2.Text = "Panel2";
            this.splitDetail.Size = new System.Drawing.Size(1000, 280);
            this.splitDetail.SplitterPosition = 520;
            this.splitDetail.TabIndex = 0;
            //
            // memoDetail
            //
            this.memoDetail.Dock = System.Windows.Forms.DockStyle.Fill;
            this.memoDetail.Location = new System.Drawing.Point(0, 34);
            this.memoDetail.Name = "memoDetail";
            this.memoDetail.Properties.ReadOnly = true;
            this.memoDetail.Size = new System.Drawing.Size(520, 246);
            this.memoDetail.TabIndex = 1;
            //
            // panelDetailBar
            //
            this.panelDetailBar.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
            this.panelDetailBar.Controls.Add(this.buttonIgnore);
            this.panelDetailBar.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelDetailBar.Location = new System.Drawing.Point(0, 0);
            this.panelDetailBar.Name = "panelDetailBar";
            this.panelDetailBar.Size = new System.Drawing.Size(520, 34);
            this.panelDetailBar.TabIndex = 0;
            //
            // buttonIgnore
            //
            this.buttonIgnore.Location = new System.Drawing.Point(4, 4);
            this.buttonIgnore.Name = "buttonIgnore";
            this.buttonIgnore.Size = new System.Drawing.Size(150, 26);
            this.buttonIgnore.TabIndex = 0;
            this.buttonIgnore.Text = "Görmezden Gel...";
            this.buttonIgnore.ToolTip = "Bilerek böyle bırakılan maddeyi notla görmezden gel: sayılmaz, mail atılmaz. İstendiğinde geri alınır.";
            this.buttonIgnore.Click += new System.EventHandler(this.buttonIgnore_Click);
            //
            // sqlScript
            //
            this.sqlScript.Dock = System.Windows.Forms.DockStyle.Fill;
            this.sqlScript.Location = new System.Drawing.Point(0, 34);
            this.sqlScript.Name = "sqlScript";
            this.sqlScript.ReadOnly = true;
            this.sqlScript.Size = new System.Drawing.Size(474, 246);
            this.sqlScript.TabIndex = 1;
            this.sqlScript.Text = "";
            //
            // memoScript
            //
            this.memoScript.Dock = System.Windows.Forms.DockStyle.Fill;
            this.memoScript.Location = new System.Drawing.Point(0, 34);
            this.memoScript.Name = "memoScript";
            this.memoScript.Properties.Appearance.Font = new System.Drawing.Font("Consolas", 10F);
            this.memoScript.Properties.Appearance.Options.UseFont = true;
            this.memoScript.Properties.ReadOnly = true;
            this.memoScript.Properties.WordWrap = false;
            this.memoScript.Properties.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.memoScript.Size = new System.Drawing.Size(474, 246);
            this.memoScript.TabIndex = 2;
            //
            // labelNoScript
            //
            this.labelNoScript.Appearance.Options.UseTextOptions = true;
            this.labelNoScript.Appearance.TextOptions.HAlignment = DevExpress.Utils.HorzAlignment.Center;
            this.labelNoScript.Appearance.TextOptions.VAlignment = DevExpress.Utils.VertAlignment.Center;
            this.labelNoScript.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
            this.labelNoScript.Dock = System.Windows.Forms.DockStyle.Fill;
            this.labelNoScript.Location = new System.Drawing.Point(0, 34);
            this.labelNoScript.Name = "labelNoScript";
            this.labelNoScript.Size = new System.Drawing.Size(474, 246);
            this.labelNoScript.TabIndex = 3;
            this.labelNoScript.Text = "Bu madde için hazır script yok.";
            //
            // panelScriptBar
            //
            this.panelScriptBar.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
            this.panelScriptBar.Controls.Add(this.labelScriptTitle);
            this.panelScriptBar.Controls.Add(this.buttonCopyScript);
            this.panelScriptBar.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelScriptBar.Location = new System.Drawing.Point(0, 0);
            this.panelScriptBar.Name = "panelScriptBar";
            this.panelScriptBar.Size = new System.Drawing.Size(474, 34);
            this.panelScriptBar.TabIndex = 0;
            //
            // labelScriptTitle
            //
            this.labelScriptTitle.Location = new System.Drawing.Point(144, 10);
            this.labelScriptTitle.Name = "labelScriptTitle";
            this.labelScriptTitle.Size = new System.Drawing.Size(30, 13);
            this.labelScriptTitle.TabIndex = 1;
            this.labelScriptTitle.Text = "Script";
            //
            // buttonCopyScript
            //
            this.buttonCopyScript.Location = new System.Drawing.Point(4, 4);
            this.buttonCopyScript.Name = "buttonCopyScript";
            this.buttonCopyScript.Size = new System.Drawing.Size(130, 26);
            this.buttonCopyScript.TabIndex = 0;
            this.buttonCopyScript.Text = "Script\'i Kopyala";
            this.buttonCopyScript.ToolTip = "SecurityGuard script\'i çalıştırmaz: kopyalayıp kendiniz inceleyin ve çalıştırın.";
            this.buttonCopyScript.Click += new System.EventHandler(this.buttonCopyScript_Click);
            //
            // labelStatus
            //
            this.labelStatus.Appearance.Options.UseForeColor = true;
            this.labelStatus.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
            this.labelStatus.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.labelStatus.Location = new System.Drawing.Point(0, 674);
            this.labelStatus.Name = "labelStatus";
            this.labelStatus.Padding = new System.Windows.Forms.Padding(6, 6, 0, 0);
            this.labelStatus.Size = new System.Drawing.Size(1000, 26);
            this.labelStatus.TabIndex = 4;
            //
            // ChecksModule
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.gridControl);
            this.Controls.Add(this.splitterDetail);
            this.Controls.Add(this.panelDetail);
            this.Controls.Add(this.labelStatus);
            this.Controls.Add(this.panelTop);
            this.Name = "ChecksModule";
            this.Size = new System.Drawing.Size(1000, 700);
            ((System.ComponentModel.ISupportInitialize)(this.panelTop)).EndInit();
            this.panelTop.ResumeLayout(false);
            this.panelTop.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.comboView.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.checkIgnored.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.gridControl)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.gridView)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.panelDetail)).EndInit();
            this.panelDetail.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitDetail)).EndInit();
            this.splitDetail.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.memoDetail.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.panelDetailBar)).EndInit();
            this.panelDetailBar.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.memoScript.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.panelScriptBar)).EndInit();
            this.panelScriptBar.ResumeLayout(false);
            this.panelScriptBar.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private DevExpress.XtraEditors.PanelControl panelTop;
        private DevExpress.XtraEditors.LabelControl labelTitle;
        private DevExpress.XtraEditors.LabelControl labelRun;
        private DevExpress.XtraEditors.ImageComboBoxEdit comboView;
        private DevExpress.XtraEditors.CheckEdit checkIgnored;
        private DevExpress.XtraEditors.SimpleButton buttonRun;
        private DevExpress.XtraEditors.SimpleButton buttonExport;
        private DevExpress.XtraGrid.GridControl gridControl;
        private DevExpress.XtraGrid.Views.Grid.GridView gridView;
        private DevExpress.XtraGrid.Columns.GridColumn colStatus;
        private DevExpress.XtraGrid.Columns.GridColumn colNew;
        private DevExpress.XtraGrid.Columns.GridColumn colArea;
        private DevExpress.XtraGrid.Columns.GridColumn colTitle;
        private DevExpress.XtraGrid.Columns.GridColumn colSummary;
        private DevExpress.XtraGrid.Columns.GridColumn colScript;
        private DevExpress.XtraEditors.SplitterControl splitterDetail;
        private DevExpress.XtraEditors.PanelControl panelDetail;
        private DevExpress.XtraEditors.SplitContainerControl splitDetail;
        private DevExpress.XtraEditors.MemoEdit memoDetail;
        private DevExpress.XtraEditors.PanelControl panelDetailBar;
        private DevExpress.XtraEditors.SimpleButton buttonIgnore;
        private SecurityGuardUI.Controls.SqlCodeEditor sqlScript;
        private DevExpress.XtraEditors.MemoEdit memoScript;
        private DevExpress.XtraEditors.LabelControl labelNoScript;
        private DevExpress.XtraEditors.PanelControl panelScriptBar;
        private DevExpress.XtraEditors.LabelControl labelScriptTitle;
        private DevExpress.XtraEditors.SimpleButton buttonCopyScript;
        private DevExpress.XtraEditors.LabelControl labelStatus;
    }
}
