using Npgsql;
using SearchForInfinity.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace SearchForInfinity
{
  /// <summary>
  /// Logique d'interaction pour MainWindow.xaml
  /// </summary>
  public partial class MainWindow: Window
  {
    private NpgsqlConnection _connection;
    private ObservableCollection<SearchResult> _searchResults;

    public MainWindow()
    {
      InitializeComponent();
      _searchResults = new ObservableCollection<SearchResult>();
      dgResults.ItemsSource = _searchResults;
    }

    private string BuildConnectionString()
    {
      var builder = new NpgsqlConnectionStringBuilder
      {
        Host = txtServer.Text,
        Port = int.Parse(txtPort.Text),
        Database = txtDatabase.Text,
        Username = txtUsername.Text,
        Password = txtPassword.Password,
        CommandTimeout = 300, // 5 minutes timeout
        Timeout = 10, // Connection timeout in seconds
        KeepAlive = 30 // Keep alive in seconds
      };

      return builder.ConnectionString;
    }

    private async void BtnTestConnection_Click(object sender, RoutedEventArgs e)
    {
      try
      {
        using (var conn = new NpgsqlConnection(BuildConnectionString()))
        {
          await conn.OpenAsync();
          lblConnectionStatus.Text = "Connection successful!";
          lblConnectionStatus.Foreground = Brushes.Green;
        }
      }
      catch (Exception ex)
      {
        lblConnectionStatus.Text = $"Connection failed: {ex.Message}";
        lblConnectionStatus.Foreground = Brushes.Red;
      }
    }

    private async void BtnConnect_Click(object sender, RoutedEventArgs e)
    {
      try
      {
        _connection = new NpgsqlConnection(BuildConnectionString());
        await _connection.OpenAsync();

        // Load schemas
        await LoadSchemasAsync();

        // Enable search tab
        tabSearch.IsEnabled = true;

        // Switch to search tab
        (tabSearch.Parent as TabControl).SelectedItem = tabSearch;

        UpdateStatus("Connected to database. Please select a schema and click Search.");
      }
      catch (Exception ex)
      {
        MessageBox.Show($"Failed to connect to database: {ex.Message}", "Error",
            MessageBoxButton.OK, MessageBoxImage.Error);
      }
    }

    private async Task LoadSchemasAsync()
    {
      try
      {
        cmbSchemas.Items.Clear();

        const string query = @"
                    SELECT schema_name 
                    FROM information_schema.schemata 
                    WHERE schema_name NOT IN ('pg_catalog', 'information_schema', 'pg_toast')
                    ORDER BY schema_name";

        using (var cmd = new NpgsqlCommand(query, _connection))
        using (var reader = await cmd.ExecuteReaderAsync())
        {
          while (await reader.ReadAsync())
          {
            cmbSchemas.Items.Add(reader.GetString(0));
          }
        }

        if (cmbSchemas.Items.Count > 0)
        {
          cmbSchemas.SelectedIndex = 0;
        }
      }
      catch (Exception ex)
      {
        MessageBox.Show($"Failed to load schemas: {ex.Message}", "Error",
            MessageBoxButton.OK, MessageBoxImage.Error);
      }
    }

    private void CmbSchemas_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      btnSearch.IsEnabled = cmbSchemas.SelectedItem != null;
    }

    private async void BtnSearch_Click(object sender, RoutedEventArgs e)
    {
      if (cmbSchemas.SelectedItem == null) return;

      var schemaName = cmbSchemas.SelectedItem.ToString();

      try
      {
        UpdateStatus("Searching for timestamp columns with infinity values...");
        _searchResults.Clear();

        // Query to find timestamp columns that might contain infinity values
        string query = @"
                    SELECT 
                        table_schema,
                        table_name, 
                        column_name,
                        data_type,
                        COUNT(*) as row_count
                    FROM information_schema.columns
                    WHERE 
                        table_schema = @schemaName
                        AND data_type IN ('timestamp without time zone', 'timestamp with time zone', 'date')
                    GROUP BY table_schema, table_name, column_name, data_type
                    ORDER BY table_name, column_name";

        var timestampColumns = new List<(string TableName, string ColumnName, string DataType)>();

        using (var cmd = new NpgsqlCommand(query, _connection))
        {
          cmd.Parameters.AddWithValue("schemaName", schemaName);

          using (var reader = await cmd.ExecuteReaderAsync())
          {
            while (await reader.ReadAsync())
            {
              timestampColumns.Add((
                  reader.GetString(1), // table_name
                  reader.GetString(2), // column_name
                  reader.GetString(3)  // data_type
              ));

              _searchResults.Add(new SearchResult
              {
                SchemaName = reader.GetString(0),
                TableName = reader.GetString(1),
                ColumnName = reader.GetString(2),
                DataType = reader.GetString(3),
                RowCount = 0
              });
            }
          }
        }

        // Now check each column for infinity values
        foreach (var column in timestampColumns)
        {
          await CheckForInfinityValuesAsync(schemaName, column.TableName, column.ColumnName, column.DataType);
        }

        UpdateStatus($"Search completed. Found {_searchResults.Count} timestamp columns.");
        btnExport.IsEnabled = _searchResults.Any();
      }
      catch (Exception ex)
      {
        UpdateStatus($"Error: {ex.Message}");
        MessageBox.Show($"An error occurred: {ex.Message}", "Error",
            MessageBoxButton.OK, MessageBoxImage.Error);
      }
    }

    private async Task CheckForInfinityValuesAsync(string schemaName, string tableName, string columnName, string dataType)
    {
      try
      {
        // For each column, check if it contains infinity values
        string query = $"SELECT COUNT(*) FROM \"{schemaName}\".\"{tableName}\" WHERE \"{columnName}\" = 'infinity'::timestamp OR \"{columnName}\" = '-infinity'::timestamp";

        using (var cmd = new NpgsqlCommand(query, _connection))
        {
          var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());

          // Update the row count in the results
          var result = _searchResults.FirstOrDefault(r =>
              r.SchemaName == schemaName &&
              r.TableName == tableName &&
              r.ColumnName == columnName);

          if (result != null)
          {
            result.RowCount = count;

            // Refresh the DataGrid
            var index = _searchResults.IndexOf(result);
            _searchResults.RemoveAt(index);
            _searchResults.Insert(index, result);
          }
        }
      }
      catch (Exception ex)
      {
        // Log the error but continue with other columns
        Console.WriteLine($"Error checking {schemaName}.{tableName}.{columnName}: {ex.Message}");
      }
    }

    private void DgResults_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
      if (dgResults.SelectedItem is SearchResult selectedResult && selectedResult.RowCount > 0)
      {
        try
        {
          // Query to get sample data from the selected table
          string query = $"SELECT * FROM \"{selectedResult.SchemaName}\".\"{selectedResult.TableName}\" WHERE \"{selectedResult.ColumnName}\" = 'infinity'::timestamp OR \"{selectedResult.ColumnName}\" = '-infinity'::timestamp";

          using (var cmd = new NpgsqlCommand(query, _connection))
          using (var adapter = new NpgsqlDataAdapter(cmd))
          {
            var dataTable = new DataTable();
            adapter.Fill(dataTable);

            // Show the results in a new window
            var window = new Window
            {
              Title = $"Details: {selectedResult.SchemaName}.{selectedResult.TableName}.{selectedResult.ColumnName}",
              Width = 800,
              Height = 600,
              WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            var dataGrid = new DataGrid
            {
              ItemsSource = dataTable.DefaultView,
              AutoGenerateColumns = true,
              CanUserAddRows = false,
              CanUserDeleteRows = false,
              IsReadOnly = true
            };

            window.Content = dataGrid;
            window.Owner = this;
            window.Show();
          }
        }
        catch (Exception ex)
        {
          MessageBox.Show($"Failed to load data: {ex.Message}", "Error",
              MessageBoxButton.OK, MessageBoxImage.Error);
        }
      }
    }

    private void BtnExport_Click(object sender, RoutedEventArgs e)
    {
      if (!_searchResults.Any()) return;

      try
      {
        var saveFileDialog = new SaveFileDialog
        {
          Filter = "CSV files (*.csv)|*.csv",
          FileName = $"infinity_search_results_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };

        if (saveFileDialog.ShowDialog() == true)
        {
          var csv = new StringBuilder();

          // Add header
          csv.AppendLine("Schema,Table,Column,Data Type,Infinity Rows");

          // Add data
          foreach (var item in _searchResults)
          {
            csv.AppendLine($"\"{item.SchemaName}\",\"{item.TableName}\",\"{item.ColumnName}\",\"{item.DataType}\",{item.RowCount}");
          }

          File.WriteAllText(saveFileDialog.FileName, csv.ToString());

          UpdateStatus($"Results exported to {saveFileDialog.FileName}");
          MessageBox.Show($"Results successfully exported to {saveFileDialog.FileName}",
              "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
      }
      catch (Exception ex)
      {
        MessageBox.Show($"Failed to export results: {ex.Message}", "Error",
            MessageBoxButton.OK, MessageBoxImage.Error);
      }
    }

    private void UpdateStatus(string message)
    {
      lblStatus.Text = message;
      lblStatusBar.Text = message;

      // Force UI update
      CommandManager.InvalidateRequerySuggested();
    }
  }
}
