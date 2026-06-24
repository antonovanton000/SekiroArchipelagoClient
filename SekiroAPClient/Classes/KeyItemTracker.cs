using SekiroAPClient.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SekiroAPClient.Classes
{
    public class KeyItemTracker
    {
        public ObservableCollection<KeyItemTrackModel> KeyItems { get; set; } = [];

        public void InitializeKeyItems(bool withApItems = false)
        {
            KeyItems.Add(new KeyItemTrackModel
            {
                Name = "Shinobi Prosthetic",
                GoodId = 2310,
                CheckedImageSource = new BitmapImage(new Uri("pack://application:,,,/Images/items/prosthetic_tool.png", UriKind.RelativeOrAbsolute)),
                UnCheckedImageSource = new BitmapImage(new Uri("pack://application:,,,/Images/items/prosthetic_tool_no.png", UriKind.RelativeOrAbsolute)),                
            });
            KeyItems.Add(new KeyItemTrackModel
            {
                Name = "Young Lord's Bell Charm",
                GoodId = 9010,
                CheckedImageSource = new BitmapImage(new Uri("pack://application:,,,/Images/items/young_bell.png", UriKind.RelativeOrAbsolute)),
                UnCheckedImageSource = new BitmapImage(new Uri("pack://application:,,,/Images/items/young_bell_no.png", UriKind.RelativeOrAbsolute)),
            });
            KeyItems.Add(new KeyItemTrackModel
            {
                Name = "Hidden Temple Key",
                GoodId = 9403,
                CheckedImageSource = new BitmapImage(new Uri("pack://application:,,,/Images/items/hidden_temple_key.png", UriKind.RelativeOrAbsolute)),
                UnCheckedImageSource = new BitmapImage(new Uri("pack://application:,,,/Images/items/hidden_temple_key_no.png", UriKind.RelativeOrAbsolute)),
            });
            if (withApItems)
            {
                KeyItems.Add(new KeyItemTrackModel
                {
                    Name = "Ashina Requisitions Whistle",
                    GoodId = 9410,
                    CheckedImageSource = new BitmapImage(new Uri("pack://application:,,,/Images/items/whistle.png", UriKind.RelativeOrAbsolute)),
                    UnCheckedImageSource = new BitmapImage(new Uri("pack://application:,,,/Images/items/whistle_no.png", UriKind.RelativeOrAbsolute)),
                });
                KeyItems.Add(new KeyItemTrackModel
                {
                    Name = "Abandoned Dungeon Key",
                    GoodId = 9407,
                    CheckedImageSource = new BitmapImage(new Uri("pack://application:,,,/Images/items/dungeon_key.png", UriKind.RelativeOrAbsolute)),
                    UnCheckedImageSource = new BitmapImage(new Uri("pack://application:,,,/Images/items/dungeon_key_no.png", UriKind.RelativeOrAbsolute)),
                });
                KeyItems.Add(new KeyItemTrackModel
                {
                    Name = "Senpou Temple Key",
                    GoodId = 9406,
                    CheckedImageSource = new BitmapImage(new Uri("pack://application:,,,/Images/items/senpou_key.png", UriKind.RelativeOrAbsolute)),
                    UnCheckedImageSource = new BitmapImage(new Uri("pack://application:,,,/Images/items/senpou_key_no.png", UriKind.RelativeOrAbsolute)),
                });
                KeyItems.Add(new KeyItemTrackModel
                {
                    Name = "Ashina Depths Key",
                    GoodId = 9408,
                    CheckedImageSource = new BitmapImage(new Uri("pack://application:,,,/Images/items/depths_key.png", UriKind.RelativeOrAbsolute)),
                    UnCheckedImageSource = new BitmapImage(new Uri("pack://application:,,,/Images/items/depths_key_no.png", UriKind.RelativeOrAbsolute)),
                });
                KeyItems.Add(new KeyItemTrackModel
                {
                    Name = "Bell of Dispelling",
                    GoodId = 9409,
                    CheckedImageSource = new BitmapImage(new Uri("pack://application:,,,/Images/items/disbell.png", UriKind.RelativeOrAbsolute)),
                    UnCheckedImageSource = new BitmapImage(new Uri("pack://application:,,,/Images/items/disbell_no.png", UriKind.RelativeOrAbsolute)),
                });
            }
            KeyItems.Add(new KeyItemTrackModel
            {
                Name = "Gun Fort Shrine Key",
                GoodId = 9405,
                CheckedImageSource = new BitmapImage(new Uri("pack://application:,,,/Images/items/gun_fort_key.png", UriKind.RelativeOrAbsolute)),
                UnCheckedImageSource = new BitmapImage(new Uri("pack://application:,,,/Images/items/gun_fort_key_no.png", UriKind.RelativeOrAbsolute)),
            });
            KeyItems.Add(new KeyItemTrackModel
            {
                Name = "Mortal Blade",
                GoodId = 2400,
                CheckedImageSource = new BitmapImage(new Uri("pack://application:,,,/Images/items/mortal_blade.png", UriKind.RelativeOrAbsolute)),
                UnCheckedImageSource = new BitmapImage(new Uri("pack://application:,,,/Images/items/mortal_blade_no.png", UriKind.RelativeOrAbsolute)),
            });
            KeyItems.Add(new KeyItemTrackModel
            {
                Name = "Lotus of the Palace",
                GoodId = 2500,
                CheckedImageSource = new BitmapImage(new Uri("pack://application:,,,/Images/items/lotus.png", UriKind.RelativeOrAbsolute)),
                UnCheckedImageSource = new BitmapImage(new Uri("pack://application:,,,/Images/items/lotus_no.png", UriKind.RelativeOrAbsolute)),
            });
            KeyItems.Add(new KeyItemTrackModel
            {
                Name = "Shelter Stone",
                GoodId = 2501,
                CheckedImageSource = new BitmapImage(new Uri("pack://application:,,,/Images/items/shelter_stone.png", UriKind.RelativeOrAbsolute)),
                UnCheckedImageSource = new BitmapImage(new Uri("pack://application:,,,/Images/items/shelter_stone_no.png", UriKind.RelativeOrAbsolute)),
            });
            KeyItems.Add(new KeyItemTrackModel
            {
                Name = "Mibu Breathing Technique",
                GoodId = 2420,
                CheckedImageSource = new BitmapImage(new Uri("pack://application:,,,/Images/items/mibubreathing.png", UriKind.RelativeOrAbsolute)),
                UnCheckedImageSource = new BitmapImage(new Uri("pack://application:,,,/Images/items/mibubreathing_no.png", UriKind.RelativeOrAbsolute)),
            });
            KeyItems.Add(new KeyItemTrackModel
            {
                Name = "Aromatic Branch",
                GoodId = 2502,
                CheckedImageSource = new BitmapImage(new Uri("pack://application:,,,/Images/items/branch.png", UriKind.RelativeOrAbsolute)),
                UnCheckedImageSource = new BitmapImage(new Uri("pack://application:,,,/Images/items/branch_no.png", UriKind.RelativeOrAbsolute)),
            });
            KeyItems.Add(new KeyItemTrackModel
            {
                Name = "Father's Bell Charm",
                GoodId = 9011,
                CheckedImageSource = new BitmapImage(new Uri("pack://application:,,,/Images/items/fathers_bell_charm.png", UriKind.RelativeOrAbsolute)),
                UnCheckedImageSource = new BitmapImage(new Uri("pack://application:,,,/Images/items/fathers_bell_charm_no.png", UriKind.RelativeOrAbsolute)),
            });
            KeyItems.Add(new KeyItemTrackModel
            {
                Name = "Secret Passage Key",
                GoodId = 9404,
                CheckedImageSource = new BitmapImage(new Uri("pack://application:,,,/Images/items/secret_passage_key.png", UriKind.RelativeOrAbsolute)),
                UnCheckedImageSource = new BitmapImage(new Uri("pack://application:,,,/Images/items/secret_passage_key_no.png", UriKind.RelativeOrAbsolute)),
            });
            KeyItems.Add(new KeyItemTrackModel
            {
                Name = "Divine Dragon's Tears",
                GoodId = 9000,
                CheckedImageSource = new BitmapImage(new Uri("pack://application:,,,/Images/items/dragon_tears.png", UriKind.RelativeOrAbsolute)),
                UnCheckedImageSource = new BitmapImage(new Uri("pack://application:,,,/Images/items/dragon_tears_no.png", UriKind.RelativeOrAbsolute)),
            });
        }

        public bool CheckItem(int goodId)
        {
            var item = KeyItems.FirstOrDefault(i => i.GoodId == goodId);
            if (item!=null)
            {
                item.IsChecked = true;
                return true;
            }
            return false;
        }

    }
}
