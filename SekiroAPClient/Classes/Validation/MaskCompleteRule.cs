using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace InjustUILibrary.Validation
{
    public class MaskCompleteRule : ValidationRule
    {
        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {        
            return ValidationResult.ValidResult;
        }
    }
}
