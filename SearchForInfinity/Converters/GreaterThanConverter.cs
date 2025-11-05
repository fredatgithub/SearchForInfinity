using System;
using System.Globalization;
using System.Windows.Data;

namespace SearchForInfinity.Converters
{
  public class GreaterThanConverter: IValueConverter
  {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
      if (value == null || parameter == null)
        return false;

      double valueToCompare;
      double compareValue;

      if (!double.TryParse(value.ToString(), out valueToCompare) ||
          !double.TryParse(parameter.ToString(), out compareValue))
        return false;

      return valueToCompare > compareValue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
      throw new NotImplementedException();
    }
  }
}
