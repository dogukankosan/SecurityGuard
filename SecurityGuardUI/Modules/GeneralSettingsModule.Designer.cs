namespace SecurityGuardUI.Modules
{
    partial class GeneralSettingsModule
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        // Dispose: GeneralSettingsModule.cs (kalın yazı fontu da serbest bırakılır)

        #region Component Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.panelTop = new DevExpress.XtraEditors.PanelControl();
            this.labelTitle = new DevExpress.XtraEditors.LabelControl();
            this.labelInfo = new DevExpress.XtraEditors.LabelControl();
            this.labelChanges = new DevExpress.XtraEditors.LabelControl();
            this.buttonSave = new DevExpress.XtraEditors.SimpleButton();
            this.buttonDiscard = new DevExpress.XtraEditors.SimpleButton();
            this.buttonDefault = new DevExpress.XtraEditors.SimpleButton();
            this.gridControl = new DevExpress.XtraGrid.GridControl();
            this.gridView = new DevExpress.XtraGrid.Views.Grid.GridView();
            this.colGroup = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colLabel = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colValue = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colDefault = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colDescription = new DevExpress.XtraGrid.Columns.GridColumn();
            ((System.ComponentModel.ISupportInitialize)(this.panelTop)).BeginInit();
            this.panelTop.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.gridControl)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.gridView)).BeginInit();
            this.SuspendLayout();
            //
            // panelTop
            //
            this.panelTop.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
            this.panelTop.Controls.Add(this.labelTitle);
            this.panelTop.Controls.Add(this.labelInfo);
            this.panelTop.Controls.Add(this.labelChanges);
            this.panelTop.Controls.Add(this.buttonSave);
            this.panelTop.Controls.Add(this.buttonDiscard);
            this.panelTop.Controls.Add(this.buttonDefault);
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
            this.labelTitle.Size = new System.Drawing.Size(116, 25);
            this.labelTitle.TabIndex = 0;
            this.labelTitle.Text = "Genel Ayarlar";
            //
            // labelInfo
            //
            this.labelInfo.Location = new System.Drawing.Point(12, 38);
            this.labelInfo.Name = "labelInfo";
            this.labelInfo.Size = new System.Drawing.Size(560, 13);
            this.labelInfo.TabIndex = 1;
            this.labelInfo.Text = "Değer sütununa tıklayıp değiştirin, sonra Kaydet. Değişiklikler servis yeniden başlatılmadan uygulanır.";
            //
            // labelChanges
            //
            this.labelChanges.Appearance.Options.UseForeColor = true;
            this.labelChanges.Location = new System.Drawing.Point(12, 60);
            this.labelChanges.Name = "labelChanges";
            this.labelChanges.Size = new System.Drawing.Size(0, 13);
            this.labelChanges.TabIndex = 2;
            //
            // buttonSave
            //
            this.buttonSave.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonSave.Location = new System.Drawing.Point(888, 12);
            this.buttonSave.Name = "buttonSave";
            this.buttonSave.Size = new System.Drawing.Size(100, 26);
            this.buttonSave.TabIndex = 5;
            this.buttonSave.Text = "Kaydet";
            this.buttonSave.Click += new System.EventHandler(this.buttonSave_Click);
            //
            // buttonDiscard
            //
            this.buttonDiscard.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonDiscard.Location = new System.Drawing.Point(742, 12);
            this.buttonDiscard.Name = "buttonDiscard";
            this.buttonDiscard.Size = new System.Drawing.Size(140, 26);
            this.buttonDiscard.TabIndex = 4;
            this.buttonDiscard.Text = "Değişiklikleri Geri Al";
            this.buttonDiscard.Click += new System.EventHandler(this.buttonDiscard_Click);
            //
            // buttonDefault
            //
            this.buttonDefault.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonDefault.Location = new System.Drawing.Point(596, 12);
            this.buttonDefault.Name = "buttonDefault";
            this.buttonDefault.Size = new System.Drawing.Size(140, 26);
            this.buttonDefault.TabIndex = 3;
            this.buttonDefault.Text = "Varsayılana Döndür";
            this.buttonDefault.Click += new System.EventHandler(this.buttonDefault_Click);
            //
            // gridControl
            //
            this.gridControl.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gridControl.Location = new System.Drawing.Point(0, 84);
            this.gridControl.MainView = this.gridView;
            this.gridControl.Name = "gridControl";
            this.gridControl.Size = new System.Drawing.Size(1000, 616);
            this.gridControl.TabIndex = 1;
            this.gridControl.ViewCollection.AddRange(new DevExpress.XtraGrid.Views.Base.BaseView[] {
            this.gridView});
            //
            // gridView
            //
            this.gridView.Columns.AddRange(new DevExpress.XtraGrid.Columns.GridColumn[] {
            this.colGroup,
            this.colLabel,
            this.colValue,
            this.colDefault,
            this.colDescription});
            this.gridView.GridControl = this.gridControl;
            this.gridView.GroupCount = 1;
            this.gridView.Name = "gridView";
            this.gridView.SortInfo.AddRange(new DevExpress.XtraGrid.Columns.GridColumnSortInfo[] {
            new DevExpress.XtraGrid.Columns.GridColumnSortInfo(this.colGroup, DevExpress.Data.ColumnSortOrder.Ascending)});
            this.gridView.CustomRowCellEdit += new DevExpress.XtraGrid.Views.Grid.CustomRowCellEditEventHandler(this.gridView_CustomRowCellEdit);
            this.gridView.RowCellStyle += new DevExpress.XtraGrid.Views.Grid.RowCellStyleEventHandler(this.gridView_RowCellStyle);
            this.gridView.FocusedRowChanged += new DevExpress.XtraGrid.Views.Base.FocusedRowChangedEventHandler(this.gridView_FocusedRowChanged);
            this.gridView.CellValueChanged += new DevExpress.XtraGrid.Views.Base.CellValueChangedEventHandler(this.gridView_CellValueChanged);
            this.gridView.CellValueChanging += new DevExpress.XtraGrid.Views.Base.CellValueChangedEventHandler(this.gridView_CellValueChanging);
            this.gridView.CustomColumnSort += new DevExpress.XtraGrid.Views.Base.CustomColumnSortEventHandler(this.gridView_CustomColumnSort);
            this.gridView.CustomColumnDisplayText += new DevExpress.XtraGrid.Views.Base.CustomColumnDisplayTextEventHandler(this.gridView_CustomColumnDisplayText);
            //
            // colGroup
            //
            this.colGroup.Caption = "Grup";
            this.colGroup.FieldName = "Group";
            this.colGroup.Name = "colGroup";
            this.colGroup.SortMode = DevExpress.XtraGrid.ColumnSortMode.Custom;
            //
            // colLabel
            //
            this.colLabel.Caption = "Ayar";
            this.colLabel.FieldName = "Label";
            this.colLabel.Name = "colLabel";
            this.colLabel.Visible = true;
            this.colLabel.VisibleIndex = 0;
            this.colLabel.Width = 220;
            //
            // colValue
            //
            this.colValue.Caption = "Değer";
            this.colValue.FieldName = "Value";
            this.colValue.Name = "colValue";
            this.colValue.Visible = true;
            this.colValue.VisibleIndex = 1;
            this.colValue.Width = 190;
            //
            // colDefault
            //
            this.colDefault.Caption = "Varsayılan";
            this.colDefault.FieldName = "DefaultText";
            this.colDefault.Name = "colDefault";
            this.colDefault.Visible = true;
            this.colDefault.VisibleIndex = 2;
            this.colDefault.Width = 130;
            //
            // colDescription
            //
            this.colDescription.Caption = "Açıklama";
            this.colDescription.FieldName = "Description";
            this.colDescription.Name = "colDescription";
            this.colDescription.Visible = true;
            this.colDescription.VisibleIndex = 3;
            this.colDescription.Width = 460;
            //
            // GeneralSettingsModule
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.gridControl);
            this.Controls.Add(this.panelTop);
            this.Name = "GeneralSettingsModule";
            this.Size = new System.Drawing.Size(1000, 700);
            ((System.ComponentModel.ISupportInitialize)(this.panelTop)).EndInit();
            this.panelTop.ResumeLayout(false);
            this.panelTop.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.gridControl)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.gridView)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private DevExpress.XtraEditors.PanelControl panelTop;
        private DevExpress.XtraEditors.LabelControl labelTitle;
        private DevExpress.XtraEditors.LabelControl labelInfo;
        private DevExpress.XtraEditors.LabelControl labelChanges;
        private DevExpress.XtraEditors.SimpleButton buttonSave;
        private DevExpress.XtraEditors.SimpleButton buttonDiscard;
        private DevExpress.XtraEditors.SimpleButton buttonDefault;
        private DevExpress.XtraGrid.GridControl gridControl;
        private DevExpress.XtraGrid.Views.Grid.GridView gridView;
        private DevExpress.XtraGrid.Columns.GridColumn colGroup;
        private DevExpress.XtraGrid.Columns.GridColumn colLabel;
        private DevExpress.XtraGrid.Columns.GridColumn colValue;
        private DevExpress.XtraGrid.Columns.GridColumn colDefault;
        private DevExpress.XtraGrid.Columns.GridColumn colDescription;
    }
}
