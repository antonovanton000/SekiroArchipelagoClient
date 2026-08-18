using CommunityToolkit.Mvvm.Input;
using InjustUILibrary.Classes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace InjustUILibrary.Controls
{
    public class ValidationForm : ItemsControl
    {
        public ValidationForm() 
        {
            Background = Application.Current.FindResource("BackgroundBrush") as Brush;
            ButtonSubmitCommand = new RelayCommand(() => { Submit(); });
            ButtonClearFormCommand = new RelayCommand(() => { ClearForm(); });
        }

        public ICommand ValidSubmitCommand
        {
            get { return (ICommand)GetValue(ValidSubmitCommandProperty); }
            set { SetValue(ValidSubmitCommandProperty, value); }
        }

        // Using a DependencyProperty as the backing store for ValidSubmitCommand.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty ValidSubmitCommandProperty =
            DependencyProperty.Register("ValidSubmitCommand", typeof(ICommand), typeof(ValidationForm), new PropertyMetadata(null));


        public ICommand NotValidSubmitCommand
        {
            get { return (ICommand)GetValue(NotValidSubmitCommandProperty); }
            set { SetValue(NotValidSubmitCommandProperty, value); }
        }

        // Using a DependencyProperty as the backing store for NotValidSubmitCommand.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty NotValidSubmitCommandProperty =
            DependencyProperty.Register("NotValidSubmitCommand", typeof(ICommand), typeof(ValidationForm), new PropertyMetadata(null));

        public ICommand ClearFormCommand
        {
            get { return (ICommand)GetValue(ClearFormCommandProperty); }
            set { SetValue(ClearFormCommandProperty, value); }
        }

        // Using a DependencyProperty as the backing store for ClearFormCommand.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty ClearFormCommandProperty =
            DependencyProperty.Register("ClearFormCommand", typeof(ICommand), typeof(ValidationForm), new PropertyMetadata(null));


        public Func<IEnumerable<AdditionalValidationResult>> AdditionalValidationFunction
        {
            get { return (Func<IEnumerable<AdditionalValidationResult>>)GetValue(AdditionalValidationFunctionProperty); }
            set { SetValue(AdditionalValidationFunctionProperty, value); }
        }

        // Using a DependencyProperty as the backing store for AdditionalValidationFunction.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty AdditionalValidationFunctionProperty =
            DependencyProperty.Register("AdditionalValidationFunction", typeof(Func<IEnumerable<AdditionalValidationResult>>), typeof(ValidationForm), new PropertyMetadata(null));


        public ICommand ButtonSubmitCommand
        {
            get { return (ICommand)GetValue(ButtonSubmitCommandProperty); }
            set { SetValue(ButtonSubmitCommandProperty, value); }
        }

        // Using a DependencyProperty as the backing store for SubmitCommand.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty ButtonSubmitCommandProperty =
            DependencyProperty.Register("ButtonSubmit", typeof(ICommand), typeof(ValidationForm), new PropertyMetadata(null));


        public ICommand ButtonClearFormCommand
        {
            get { return (ICommand)GetValue(ButtonClearFormCommandProperty); }
            set { SetValue(ButtonClearFormCommandProperty, value); }
        }

        // Using a DependencyProperty as the backing store for ClearFormCommand.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty ButtonClearFormCommandProperty =
            DependencyProperty.Register("ButtonClearFormCommand", typeof(ICommand), typeof(ValidationForm), new PropertyMetadata(null));


        public new Brush Background
        {
            get { return (Brush)GetValue(BackgroundProperty); }
            set { SetValue(BackgroundProperty, value); }
        }

        // Using a DependencyProperty as the backing store for Background.  This enables animation, styling, binding, etc...
        public static new readonly DependencyProperty BackgroundProperty =
            DependencyProperty.Register("Background", typeof(Brush), typeof(ValidationForm), new PropertyMetadata(null));


        private void Submit()
        {
            var hasErrors = false;
            foreach (var el in Items.Cast<FrameworkElement>().Select(i => i as IValidationField).Where(i => i != null))
            {
                if (el?.Validate() == false)
                {
                    hasErrors = true;
                }
            }
            if (!hasErrors)
            {
                var res = AdditionalValidationFunction?.Invoke();
                if (res?.Count() > 0)
                {
                    foreach (var item in res)
                    {
                        var el = Items.Cast<FrameworkElement>().Select(i => i as IValidationField).Where(i => i != null).FirstOrDefault(i => i.BindPropertyName == item.BindPropertyName);
                        if (el != null)
                        {
                            el.IsError = true;
                            el.ErrorMessage = item.ErrorMessage;
                        }
                    }
                    NotValidSubmitCommand?.Execute(null);
                }
                else
                {
                    ValidSubmitCommand?.Execute(null);
                }
            }
            else
            {
                NotValidSubmitCommand?.Execute(null);
            }
        }

        private void ClearForm()
        {
            foreach (var el in Items.Cast<FrameworkElement>().Select(i => i as IValidationField).Where(i => i != null))
            {
                el.IsError = false;
                el.ErrorMessage = string.Empty;
            }

            ClearFormCommand?.Execute(null);
        }

    }
}
