using System.Drawing;
using System.Windows.Forms;

namespace NetFileConverter
{
    // partial соединяет этот файл разметки с основным файлом логики в один класс
    public partial class SettingsForm
    {
        // UI компоненты скрыты в дизайнере
        private System.Windows.Forms.ListView listViewWatchedFolders;
        private System.Windows.Forms.Button btnAddFolder;
        private System.Windows.Forms.Button btnRemoveFolder;
        private System.Windows.Forms.Button btnToggleFormat;
        private System.Windows.Forms.Button btnSave;
        private System.Windows.Forms.Button btnCancel;

        /// <summary>
        /// Метод инициализации компонентов. Управляется "дизайнером".
        /// </summary>
        private void InitializeComponent()
        {
            this.listViewWatchedFolders = new System.Windows.Forms.ListView();
            this.btnAddFolder = new System.Windows.Forms.Button();
            this.btnRemoveFolder = new System.Windows.Forms.Button();
            this.btnToggleFormat = new System.Windows.Forms.Button();
            this.btnSave = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            
            this.SuspendLayout();

            // Список папок
            this.listViewWatchedFolders.Location = new System.Drawing.Point(12, 12);
            this.listViewWatchedFolders.Name = "listViewWatchedFolders";
            this.listViewWatchedFolders.Size = new System.Drawing.Size(440, 250);
            this.listViewWatchedFolders.TabIndex = 0;

            // Кнопка "Добавить"
            this.btnAddFolder.Location = new System.Drawing.Point(465, 12);
            this.btnAddFolder.Name = "btnAddFolder";
            this.btnAddFolder.Size = new System.Drawing.Size(120, 30);
            this.btnAddFolder.TabIndex = 1;
            this.btnAddFolder.Text = "Добавить...";
            this.btnAddFolder.UseVisualStyleBackColor = true;
            this.btnAddFolder.Click += new System.EventHandler(this.btnAddFolder_Click);

            // Кнопка "Удалить"
            this.btnRemoveFolder.Location = new System.Drawing.Point(465, 48);
            this.btnRemoveFolder.Name = "btnRemoveFolder";
            this.btnRemoveFolder.Size = new System.Drawing.Size(120, 30);
            this.btnRemoveFolder.TabIndex = 2;
            this.btnRemoveFolder.Text = "Удалить";
            this.btnRemoveFolder.UseVisualStyleBackColor = true;
            this.btnRemoveFolder.Click += new System.EventHandler(this.btnRemoveFolder_Click);

            // Кнопка "Формат"
            this.btnToggleFormat.Location = new System.Drawing.Point(465, 84);
            this.btnToggleFormat.Name = "btnToggleFormat";
            this.btnToggleFormat.Size = new System.Drawing.Size(120, 30);
            this.btnToggleFormat.TabIndex = 3;
            this.btnToggleFormat.Text = "Формат (K/P)";
            this.btnToggleFormat.UseVisualStyleBackColor = true;
            this.btnToggleFormat.Click += new System.EventHandler(this.btnToggleFormat_Click);

            // Кнопка "Сохранить"
            this.btnSave.Location = new System.Drawing.Point(332, 280);
            this.btnSave.Name = "btnSave";
            this.btnSave.Size = new System.Drawing.Size(120, 35);
            this.btnSave.TabIndex = 4;
            this.btnSave.Text = "Сохранить";
            this.btnSave.UseVisualStyleBackColor = true;
            this.btnSave.Click += new System.EventHandler(this.btnSave_Click);

            // Кнопка "Отмена"
            this.btnCancel.Location = new System.Drawing.Point(465, 280);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(120, 35);
            this.btnCancel.TabIndex = 5;
            this.btnCancel.Text = "Отмена";
            this.btnCancel.UseVisualStyleBackColor = true;
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);

            // Параметры главного окна формы
            this.ClientSize = new System.Drawing.Size(600, 330);
            this.Controls.Add(this.listViewWatchedFolders);
            this.Controls.Add(this.btnAddFolder);
            this.Controls.Add(this.btnRemoveFolder);
            this.Controls.Add(this.btnToggleFormat);
            this.Controls.Add(this.btnSave);
            this.Controls.Add(this.btnCancel);
            
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Настройки отслеживания нетлистов";
            this.Load += new System.EventHandler(this.SettingsForm_Load);

            this.ResumeLayout(false);
        }
    }
}