using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Npgsql;
using SearchForInfinity.Models;
using SearchForInfinity.Properties;

namespace SearchForInfinity
{
  /// <summary>
  /// Logique d'interaction pour MainWindow.xaml
  /// </summary>
  public partial class MainWindow: Window
  {
    private NpgsqlConnection _connection;
    private ObservableCollection<SearchResult> _searchResults;

    private void DgResults_LoadingRow(object sender, DataGridRowEventArgs e)
    {
      if (e.Row.Item is SearchResult item)
      {
        try
        {
          // Log pour débogage
          Debug.WriteLine($"Traitement de la ligne - Table: {item.TableName}, Colonne: {item.ColumnName}, RowCount: {item.RowCount}");

          if (item.RowCount >= 1)
          {
            Debug.WriteLine($"Mise en surbrillance de la ligne - RowCount: {item.RowCount} > 1");
            e.Row.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#66BB6A"));
            e.Row.Foreground = Brushes.White;
          }
          else
          {
            e.Row.ClearValue(BackgroundProperty);
            e.Row.ClearValue(ForegroundProperty);
          }
        }
        catch (Exception exception)
        {
          Debug.WriteLine($"Erreur dans DgResults_LoadingRow: {exception.Message}");
          Debug.WriteLine($"StackTrace: {exception.StackTrace}");
        }
      }
      else
      {
        Debug.WriteLine("L'élément de la ligne n'est pas un SearchResult");
      }
    }

    public MainWindow()
    {
      InitializeComponent();
      _searchResults = new ObservableCollection<SearchResult>();
      dgResults.ItemsSource = _searchResults;

      // Charger les paramètres de connexion sauvegardés
      LoadConnectionSettings();

      // Restaurer la position et la taille de la fenêtre
      LoadWindowSettings();

      // S'abonner à l'événement de fermeture de la fenêtre
      Closing += MainWindow_Closing;
    }

    private void LoadWindowSettings()
    {
      var settings = Settings.Default;

      // Vérifier si les paramètres de fenêtre sont valides
      if (settings.WindowLeft >= 0 && settings.WindowTop >= 0 && settings.WindowWidth > 0 && settings.WindowHeight > 0)
      {
        Left = settings.WindowLeft;
        Top = settings.WindowTop;
        Width = settings.WindowWidth;
        Height = settings.WindowHeight;
        WindowState = settings.WindowState;
      }
    }

    private void SaveWindowSettings()
    {
      var settings = Settings.Default;

      // Sauvegarder la position et la taille de la fenêtre
      if (WindowState == WindowState.Normal)
      {
        settings.WindowLeft = Left;
        settings.WindowTop = Top;
        settings.WindowWidth = Width;
        settings.WindowHeight = Height;
      }
      else
      {
        // Si la fenêtre est maximisée ou minimisée, sauvegarder les valeurs restaurées
        var restoredState = RestoreBounds;
        settings.WindowLeft = restoredState.Left;
        settings.WindowTop = restoredState.Top;
        settings.WindowWidth = restoredState.Width;
        settings.WindowHeight = restoredState.Height;
      }

      settings.WindowState = WindowState;
      settings.Save();
    }

    private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
      // Sauvegarder les paramètres de connexion
      SaveConnectionSettings();

      // Sauvegarder la position et la taille de la fenêtre
      SaveWindowSettings();

      // Fermer proprement la connexion à la base de données
      try
      {
        if (_connection != null && _connection.State == ConnectionState.Open)
        {
          _connection.Close();
          _connection.Dispose();
        }
      }
      catch (Exception exception)
      {
        MessageBox.Show($"Erreur lors de la fermeture de la connexion : {exception.Message}",
            "Avertissement", MessageBoxButton.OK, MessageBoxImage.Warning);
      }
    }

    private void LoadConnectionSettings()
    {
      try
      {
        var settings = Settings.Default;
        txtServer.Text = settings.Server;
        txtPort.Text = settings.Port.ToString();
        txtDatabase.Text = settings.Database;
        txtUsername.Text = settings.Username;

        // Essayer de charger le mot de passe depuis des données sécurisées
        try
        {
          var passwordInBytes = Convert.FromBase64String(settings.Password ?? string.Empty);
          var entropy = Encoding.UTF8.GetBytes(Assembly.GetExecutingAssembly().FullName);
          var decrypted = ProtectedData.Unprotect(passwordInBytes, entropy, DataProtectionScope.CurrentUser);
          txtPassword.Password = Encoding.UTF8.GetString(decrypted);
        }
        catch
        {
          // En cas d'erreur, on laisse le mot de passe vide
          txtPassword.Password = string.Empty;
        }
      }
      catch (Exception exception)
      {
        MessageBox.Show($"Erreur lors du chargement des paramètres : {exception.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
      }
    }

    private void SaveConnectionSettings()
    {
      try
      {
        var settings = Settings.Default;
        settings.Server = txtServer.Text;

        if (int.TryParse(txtPort.Text, out int port))
        {
          settings.Port = port;
        }

        settings.Database = txtDatabase.Text;
        settings.Username = txtUsername.Text;

        // Sauvegarder le mot de passe de manière sécurisée
        if (!string.IsNullOrEmpty(txtPassword.Password))
        {
          var passwordInBytes = Encoding.UTF8.GetBytes(txtPassword.Password);
          var entropy = Encoding.UTF8.GetBytes(Assembly.GetExecutingAssembly().FullName);
          var encrypted = ProtectedData.Protect(passwordInBytes, entropy, DataProtectionScope.CurrentUser);
          settings.Password = Convert.ToBase64String(encrypted);
        }
        else
        {
          settings.Password = string.Empty;
        }

        settings.Save();
      }
      catch (Exception exception)
      {
        MessageBox.Show($"Erreur lors de la sauvegarde des paramètres : {exception.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
      }
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

    private NpgsqlConnection CreateConnection()
    {
      return new NpgsqlConnection(BuildConnectionString());
    }

    private async void BtnTestConnection_Click(object sender, RoutedEventArgs e)
    {
      try
      {
        UpdateStatus("Testing connection...");
        using (var conn = CreateConnection())
        {
          await conn.OpenAsync();
          var version = (await new NpgsqlCommand("SELECT version();", conn).ExecuteScalarAsync())?.ToString();

          // Mise à jour du statut avec succès
          UpdateStatus($"✓ Connected successfully!\n{version}");

          // Changer la couleur du bouton en vert
          var brush = new SolidColorBrush(Colors.Green);
          brush.Freeze();
          btnTestConnection.Background = brush;
          btnTestConnection.Foreground = new SolidColorBrush(Colors.White);

          // Activer le bouton Connect
          btnConnect.IsEnabled = true;
        }
      }
      catch (Exception exception)
      {
        // Mise à jour du statut avec erreur
        UpdateStatus($"✗ Connection failed: {exception.Message}", true);

        // Changer la couleur du bouton en rouge et désactiver le bouton Connect
        var brush = new SolidColorBrush(Colors.Red);
        brush.Freeze();
        btnTestConnection.Background = brush;
        btnTestConnection.Foreground = new SolidColorBrush(Colors.White);
        btnConnect.IsEnabled = false;
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
      catch (Exception exception)
      {
        MessageBox.Show($"Failed to connect to database: {exception.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
      catch (Exception exception)
      {
        MessageBox.Show($"Failed to load schemas: {exception.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
      }
    }

    private void CmbSchemas_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      btnSearch.IsEnabled = cmbSchemas.SelectedItem != null;
    }

    private async void BtnSearch_Click(object sender, RoutedEventArgs e)
    {
      if (cmbSchemas.SelectedItem == null)
      {
        return;
      }

      string schemaName = cmbSchemas.SelectedItem.ToString();
      
      // Afficher la fenêtre d'attente
      var waitWindow = WaitWindow.ShowWaitWindow(this, "Searching for infinity values...");
      
      try
      {
        await SearchForInfinityValuesAsync(schemaName);
      }
      finally
      {
        // Fermer la fenêtre d'attente dans tous les cas (succès ou erreur)
        waitWindow.Close();
      }

      // Redimensionner les colonnes après le chargement des données
      if (dgResults.Items.Count > 0)
      {
        dgResults.UpdateLayout();
        foreach (var column in dgResults.Columns)
        {
          // Forcer la mise à jour de la largeur pour s'adapter au contenu
          column.Width = 0;
          column.Width = DataGridLength.Auto;
        }
      }
    }

    private async Task SearchForInfinityValuesAsync(string schemaName)
    {
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
      catch (Exception exception)
      {
        UpdateStatus($"Error: {exception.Message}");
        MessageBox.Show($"An error occurred: {exception.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
      catch (Exception exception)
      {
        // Log the error but continue with other columns
        Console.WriteLine($"Error checking {schemaName}.{tableName}.{columnName}: {exception.Message}");
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
        catch (Exception exception)
        {
          MessageBox.Show($"Failed to load data: {exception.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
      }
    }

    private void BtnExport_Click(object sender, RoutedEventArgs e)
    {
      if (!_searchResults.Any())
      {
        return;
      }

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
      catch (Exception exception)
      {
        MessageBox.Show($"Failed to export results: {exception.Message}", "Error",
            MessageBoxButton.OK, MessageBoxImage.Error);
      }
    }

    private void UpdateStatus(string message, bool isError = false)
    {
      if (string.IsNullOrWhiteSpace(message))
      {
        return;
      }

      Action updateAction = () =>
      {
        // Créer un nouveau paragraphe pour le message
        var paragraph = new Paragraph();
        
        // Ajouter l'horodatage
        paragraph.Inlines.Add(new Run($"[{DateTime.Now:HH:mm:ss}] ")
        {
          Foreground = Brushes.Gray,
          FontStyle = FontStyles.Italic
        });

        // Ajouter le message
        var messageRun = new Run(message);
        if (isError)
        {
          messageRun.Foreground = Brushes.Red;
          messageRun.FontWeight = FontWeights.Bold;
        }
        paragraph.Inlines.Add(messageRun);

        // Ajouter le paragraphe au document
        rtbConnectionStatus.Document.Blocks.Add(paragraph);

        // Faire défiler vers le bas
        rtbConnectionStatus.ScrollToEnd();

        // Limiter le nombre de blocs pour éviter les problèmes de performances
        const int maxBlocks = 500;
        while (rtbConnectionStatus.Document.Blocks.Count > maxBlocks)
        {
          rtbConnectionStatus.Document.Blocks.Remove(rtbConnectionStatus.Document.Blocks.FirstBlock);
        }
      };

      if (rtbConnectionStatus.Dispatcher.CheckAccess())
      {
        updateAction();
      }
      else
      {
        rtbConnectionStatus.Dispatcher.Invoke(updateAction);
      }

      // Forcer la mise à jour de l'interface utilisateur
      CommandManager.InvalidateRequerySuggested();
    }

    private void BtnClearLogs_Click(object sender, RoutedEventArgs e)
    {
      rtbConnectionStatus.Document.Blocks.Clear();
      UpdateStatus("Historique des messages effacé");
    }
    
    private void RtbConnectionStatus_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
      // Créer un nouveau menu contextuel
      var contextMenu = new ContextMenu();
      
      // Copier
      var copyItem = new MenuItem { Header = "Copier", Command = ApplicationCommands.Copy };
      copyItem.Click += (s, args) =>
      {
        if (rtbConnectionStatus.Selection.IsEmpty)
        {
          rtbConnectionStatus.SelectAll();
          rtbConnectionStatus.Copy();
          rtbConnectionStatus.Selection.Select(rtbConnectionStatus.CaretPosition, rtbConnectionStatus.CaretPosition);
          UpdateStatus("Tout le contenu a été copié");
        }
        else
        {
          rtbConnectionStatus.Copy();
          UpdateStatus("Sélection copiée");
        }
      };
      
      // Tout sélectionner
      var selectAllItem = new MenuItem { Header = "Tout sélectionner", Command = ApplicationCommands.SelectAll };
      selectAllItem.Click += (s, args) => 
      {
        rtbConnectionStatus.SelectAll();
        rtbConnectionStatus.Focus();
      };
      
      // Effacer l'historique
      var clearItem = new MenuItem { Header = "Effacer l'historique" };
      clearItem.Click += BtnClearLogs_Click;
      
      // Ajouter les éléments au menu
      contextMenu.Items.Add(copyItem);
      contextMenu.Items.Add(new Separator());
      contextMenu.Items.Add(selectAllItem);
      contextMenu.Items.Add(new Separator());
      contextMenu.Items.Add(clearItem);
      
      // Définir le menu contextuel
      rtbConnectionStatus.ContextMenu = contextMenu;
      
      // Annuler l'événement d'ouverture du menu par défaut
      e.Handled = true;
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
      // Sauvegarder les paramètres de connexion avant de fermer
      SaveConnectionSettings();

      // Fermer proprement la connexion à la base de données
      try
      {
        if (_connection != null && _connection.State == ConnectionState.Open)
        {
          _connection.Close();
          _connection.Dispose();
        }
      }
      catch (Exception exception)
      {
        MessageBox.Show($"Erreur lors de la fermeture de la connexion : {exception.Message}",
            "Avertissement", MessageBoxButton.OK, MessageBoxImage.Warning);
      }
    }
  }
}
