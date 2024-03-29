﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DatabaseFileExport.Classes;
using DatabaseFileExport.Classes.HelpClasses;
using DatabaseFileExport.Enums;
using DatabaseFileExport.Exceptions;
using DatabaseFileExport.Models;
using DatabaseFileExport.MyExtensions;

namespace DatabaseFileExport
{
    public partial class MainForm : Form
    {
        private static ExportFileModel ExportFileModel;
        private readonly LogToUser LogToUser = new LogToUser();
        private static Dictionary<string, List<string>> DataBaseVarBinaryTables;

        public MainForm()
        {
            InitializeComponent();
        }

        private async void CheckConnectionStringButton_Click(object sender, EventArgs e)
        {
            SelectDataBaseTableComboBox.SelectedItem = null;
            SelectDataBaseTableComboBox.SelectedText = "Выбрать таблицу";

            ExportFileModel = new ExportFileModel();
            
            if (ConnectionStringTextBox.Text.Length < 5)
            {
                LogToUser.Log<DialogResult>(LogLevel.Error, "Неверный формат строки подключения!");
                return;
            }
            
            #region CheckConnectionString
            
            try
            {
                ExportFileModel.Connectionstring = new SqlConnectionStringBuilder(ConnectionStringTextBox.Text);
            
                DataBaseVarBinaryTables = await DataBaseShemaManager.GetVarBinaryTables(ExportFileModel.Connectionstring.ConnectionString);
                SelectDataBaseTableComboBox.Fill(DataBaseVarBinaryTables.Keys.ToList());
                DBTablePanel.Visible = true;
            }
            catch (KeyNotFoundException keyEx)
            {
                LogToUser.Log<DialogResult>(LogLevel.Error,
                    $"Ошибка преобразования строки подключения!\n{keyEx.Message}");
            }
            catch (FormatException formatEx)
            {
                LogToUser.Log<DialogResult>(LogLevel.Error,
                    $"Неверный формат строки подключения! \n{formatEx.Message}");
            }
            catch (ArgumentException argumentEx)
            {
                LogToUser.Log<DialogResult>(LogLevel.Error,
                    $"Неверный аргумент строки подключения! \n{argumentEx.Message}");
            }
            catch (SqlException sqlEx)
            {
                LogToUser.Log<DialogResult>(LogLevel.Fatal, sqlEx.Message);
            }
            catch (FillException fillException)
            {
                LogToUser.Log<DialogResult>(LogLevel.Fatal, fillException.Message);
            }
            catch (Exception exception)
            {
                LogToUser.Log<DialogResult>(LogLevel.Error,
                    $"Ошибка! \n{exception.Message}");
            }
            
            #endregion
        }

        private async void SelectDataBaseTableComboBox_SelectionChangeCommitted(object sender, EventArgs e)
        {
            try
            {
                if (!(sender is ComboBox combobox)) return;

                SQLQueryPanel.Visible = false;
                InsertButton.Visible = false;
                
                string selectedTable = combobox.SelectedItem.ToString();

                DBTableNameTextBox.Text = selectedTable;
                ExportFileModel.DataBaseTable = selectedTable;

                DataTable tableNotVarbinaryColumns =
                   await DataBaseShemaManager.GetNotVarBinaryColumnFromTable(
                        ExportFileModel.Connectionstring.ConnectionString, selectedTable);

                ColumnToUpdateComboBox.Fill(DataBaseVarBinaryTables[selectedTable]);
                FilterTableComboBox.Fill(tableNotVarbinaryColumns, "COLUMN_NAME");
                
                SQLQueryPanel.Visible = true;
                InsertButton.Visible = true;
            }
            catch (SqlException sqlEx)
            {
                LogToUser.Log<DialogResult>(LogLevel.Fatal, $"Ошибка выполнения SQL-запроса {sqlEx.Message}");
            }
            catch (FillException fillException)
            {
                LogToUser.Log<DialogResult>(LogLevel.Fatal, fillException.Message);
            }
            catch (Exception exception)
            {
                LogToUser.Log<DialogResult>(LogLevel.Fatal, exception.Message);
            }
        }

        private void SelectFileButton_Click(object sender, EventArgs e)
        {
            try
            {
                using (OpenFileDialog getFile = new OpenFileDialog())
                {
                    getFile.Title = "Выбирите файл который нужно вставить в базу данных";
                    getFile.Multiselect = false;

                    if (FileWorker.SelectFileFromPC(getFile))
                    {
                        ExportFileModel.FilePath = getFile.FileName;
                        FileInfo fileName = new FileInfo(getFile.FileName);
                        SelectFileButton.Text = fileName.Name;
                    }
                }
            }
            catch (FileWorkerException fileEx)
            {
                LogToUser.Log<DialogResult>(LogLevel.Error, $"Произошла ошибка при выборе файла.\n{fileEx.Message}");
            }
            catch (Exception exception)
            {
                LogToUser.Log<DialogResult>(LogLevel.Fatal, $"Ошибка! {exception.Message}");
            }
        }
        
        private async void InsertButton_Click(object sender, EventArgs e)
        {
            try
            {
                bool isItemsSelected = IsItemSelected(ColumnToUpdateComboBox, FilterTableComboBox);

                if (!isItemsSelected)
                {
                    LogToUser.Log<DialogResult>(LogLevel.Error, "Выберите данные из выпадающего списка!");
                    return;
                }

                if (string.IsNullOrEmpty(ExportFileModel.FilePath))
                {
                    LogToUser.Log<DialogResult>(LogLevel.Error, "Экспортируемый файл обязателен! \nПожалуйста выберете файл");
                    return;
                }

                string columnToUpdate = ColumnToUpdateComboBox.SelectedItem.ToString();
                string filterTableComboBox = FilterTableComboBox.SelectedItem.ToString();
                string filterText = FilterTextBox.Text;

                if (string.IsNullOrWhiteSpace(filterText))
                    if (LogToUser.Log<DialogResult>(LogLevel.Info, "Поле фильтрации не заполненно.\nПродолжить?") == DialogResult.Cancel)
                        return;

                string updateFileSql =
                    $"UPDATE {ExportFileModel.DataBaseTable} SET [{columnToUpdate}] = @IM WHERE [{filterTableComboBox}] = N'{filterText}'";

                SqlCommand updateFileCommand = new SqlCommand(updateFileSql);
                byte[] imageData = FileWorker.GetFileBytes(ExportFileModel.FilePath);

                updateFileCommand.Parameters.AddWithValue("@IM", imageData);

                await SQLQueryExecutor.Execute(ExportFileModel.Connectionstring.ConnectionString, updateFileCommand);

                LogToUser.Log<DialogResult>(LogLevel.Info, "Готово");
            }
            catch (FileWorkerException fileEx)
            {
                LogToUser.Log<DialogResult>(LogLevel.Error, $"Ошибка чтения выбранного файла. \n\n{fileEx.Message}");
            }
            catch (SqlException sqlEx)
            {
                LogToUser.Log<DialogResult>(LogLevel.Fatal, $"Ошибка выполнения SQL-запроса. \n\n{sqlEx.Message}");
            }
            catch (Exception ex)
            {
                LogToUser.Log<DialogResult>(LogLevel.Fatal, $"Ошибка \n\n {ex.Message}");
            }
        }

        private static bool IsItemSelected(params ComboBox[] comboBoxes)
        {
            foreach (ComboBox comboboxToValidate in comboBoxes)
            {
                if (comboboxToValidate.SelectedItem == null) 
                    return false;
                
                if (string.IsNullOrEmpty(comboboxToValidate.SelectedItem.ToString()) || comboboxToValidate.SelectedItem.ToString() == " ")
                    return false;
            }

            return true;
        }
    }
}