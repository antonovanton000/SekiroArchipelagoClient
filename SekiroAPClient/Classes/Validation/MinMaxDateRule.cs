using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace InjustUILibrary.Validation
{
    public class MinMaxDateRule : ValidationRule
    {
        public DateTime? MinDate { get; set; }
        public DateTime? MaxDate { get; set; }

        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            var date = value as DateTime?;
            if (date != null)
            {
                if (MinDate != null && MaxDate == null)
                {
                    if (MinDate > date)
                        return new ValidationResult(false, $"Выбранная дата меньше допустимой!");
                    else
                        return ValidationResult.ValidResult;

                }

                if (MaxDate != null && MinDate == null)
                {
                    if (MaxDate < date)
                        return new ValidationResult(false, $"Выбранная дата больше допустимой!");
                    else
                        return ValidationResult.ValidResult;
                }

                if (MaxDate != null && MinDate != null)
                {
                    if (MinDate < date && date < MaxDate)
                        return ValidationResult.ValidResult;
                    else
                        return new ValidationResult(false, $"Выбранная дата вне диапазона!");
                }
                
            }
            return ValidationResult.ValidResult;
        }
    }
}
