using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace InjustUILibrary.Validation
{
    public class MinMaxSelectionCountRule : ValidationRule
    {
        public int? MinCount { get; set; }
        public int? MaxCount { get; set; }

        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            var count = value as int?;
            if (count != null)
            {
                if (MinCount != null && MaxCount == null)
                {
                    if (MinCount > count)
                        return new ValidationResult(false, $"Выбранная количество меньше допустимого!");
                    else
                        return ValidationResult.ValidResult;

                }

                if (MaxCount != null && MinCount == null)
                {
                    if (MaxCount < count)
                        return new ValidationResult(false, $"Выбранное количество больше допустимого!");
                    else
                        return ValidationResult.ValidResult;
                }

                if (MaxCount != null && MinCount != null)
                {
                    if (MinCount < count && count < MaxCount)
                        return ValidationResult.ValidResult;
                    else
                        return new ValidationResult(false, $"Выбранное количество вне диапазона!");
                }                
            }
            return ValidationResult.ValidResult;
        }
    }
}
