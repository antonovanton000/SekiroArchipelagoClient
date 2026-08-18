using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Media;

namespace SekiroAPClient.Models
{
    public partial class KeyItemTrackModel : ObservableObject
    {        
        public string Name { get; set; }
        public ImageSource CheckedImageSource { get; set; } = default!;
        public ImageSource UnCheckedImageSource { get; set; } = default!;
        
        [ObservableProperty]
        bool isChecked = false;
        public int GoodId { get; set; }
    }
}
