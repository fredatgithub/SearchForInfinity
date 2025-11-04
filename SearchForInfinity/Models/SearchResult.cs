namespace SearchForInfinity.Models
{
  public class SearchResult
  {
    public string SchemaName { get; set; }
    public string TableName { get; set; }
    public string ColumnName { get; set; }
    public string DataType { get; set; }
    public int RowCount { get; set; }
  }
}
