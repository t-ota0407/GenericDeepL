using System;
using System.Windows;
using System.Windows.Controls;

namespace GenericDeepL
{
    public partial class SettingsWindow : Window
    {
        private readonly Settings _settings;

        public SettingsWindow(Settings settings)
        {
            InitializeComponent();
            _settings = settings;
            ApiKeyPasswordBox.Password = _settings.GoogleApiKey;

            // リージョン・モデル一覧の反映と SelectionChanged の購読は Loaded で行う
            Loaded += SettingsWindow_Loaded;
            
            // 翻訳スタイル選択を設定
            switch (_settings.TranslationStyle)
            {
                case "標準":
                    StyleStandard.IsChecked = true;
                    break;
                case "カジュアル":
                    StyleCasual.IsChecked = true;
                    break;
                case "フォーマル":
                    StyleFormal.IsChecked = true;
                    break;
                case "厳密な直訳":
                    StyleLiteral.IsChecked = true;
                    break;
                case "自然さを優先した翻訳":
                    StyleNatural.IsChecked = true;
                    break;
                default:
                    StyleStandard.IsChecked = true;
                    break;
            }
            
            // スタートアップ設定を反映
            StartupCheckBox.IsChecked = _settings.RunAtStartup;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            _settings.GoogleApiKey = ApiKeyPasswordBox.Password;
            
            // 選択されたモデルを取得
            if (ModelComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem selectedModelItem)
            {
                _settings.ModelName = selectedModelItem.Content.ToString();
            }
            
            // 選択されたリージョンを取得
            if (RegionComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem selectedRegionItem)
            {
                _settings.Region = selectedRegionItem.Tag?.ToString() ?? TranslationService.GetDefaultRegion();
            }
            
            // 選択された翻訳スタイルを取得
            if (StyleStandard.IsChecked == true)
                _settings.TranslationStyle = "標準";
            else if (StyleCasual.IsChecked == true)
                _settings.TranslationStyle = "カジュアル";
            else if (StyleFormal.IsChecked == true)
                _settings.TranslationStyle = "フォーマル";
            else if (StyleLiteral.IsChecked == true)
                _settings.TranslationStyle = "厳密な直訳";
            else if (StyleNatural.IsChecked == true)
                _settings.TranslationStyle = "自然さを優先した翻訳";
            
            // スタートアップ設定を保存
            bool previousStartupSetting = _settings.RunAtStartup;
            _settings.RunAtStartup = StartupCheckBox.IsChecked == true;
            
            // スタートアップ設定が変更された場合、レジストリを更新
            if (previousStartupSetting != _settings.RunAtStartup)
            {
                if (_settings.RunAtStartup)
                {
                    if (!StartupManager.Register())
                    {
                        MessageBox.Show("Failed to register startup setting.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        _settings.RunAtStartup = false;
                    }
                }
                else
                {
                    if (!StartupManager.Unregister())
                    {
                        MessageBox.Show("Failed to unregister startup setting.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            
            _settings.Save();
            MessageBox.Show("Settings saved successfully.", "Settings", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= SettingsWindow_Loaded;

            // region-models.json に準拠してリージョン選択肢を構築
            RegionComboBox.Items.Clear();
            var regionIds = TranslationService.GetRegionIds();
            var defaultRegion = TranslationService.GetDefaultRegion();
            var savedRegion = string.IsNullOrEmpty(_settings.Region) ? defaultRegion : _settings.Region;
            int regionSelectIndex = 0;
            for (int i = 0; i < regionIds.Length; i++)
            {
                var item = new ComboBoxItem
                {
                    Content = GetRegionDisplayName(regionIds[i]),
                    Tag = regionIds[i]
                };
                RegionComboBox.Items.Add(item);
                if (string.Equals(regionIds[i], savedRegion, StringComparison.OrdinalIgnoreCase))
                    regionSelectIndex = i;
            }
            if (RegionComboBox.Items.Count > 0)
                RegionComboBox.SelectedIndex = regionSelectIndex;

            RegionComboBox.SelectionChanged += RegionComboBox_SelectionChanged;
            RefreshModelsForSelectedRegion();
            foreach (System.Windows.Controls.ComboBoxItem item in ModelComboBox.Items)
            {
                if (item.Content?.ToString() == _settings.ModelName)
                {
                    item.IsSelected = true;
                    break;
                }
            }
        }

        private static string GetRegionDisplayName(string regionId)
        {
            return regionId switch
            {
                "asia-northeast1" => "asia-northeast1 (東京)",
                "us-central1" => "us-central1 (アイオワ)",
                "europe-west9" => "europe-west9 (パリ)",
                _ => regionId
            };
        }

        private void RegionComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            RefreshModelsForSelectedRegion();
        }

        private string GetSelectedRegion()
        {
            if (RegionComboBox.SelectedItem is ComboBoxItem selectedRegionItem)
                return selectedRegionItem.Tag?.ToString() ?? TranslationService.GetDefaultRegion();
            return TranslationService.GetDefaultRegion();
        }

        private void RefreshModelsForSelectedRegion()
        {
            string region = GetSelectedRegion();
            string previouslySelected = ModelComboBox.SelectedItem is ComboBoxItem prev ? prev.Content?.ToString() : null;
            string[] models = TranslationService.GetModelsForRegion(region);

            ModelComboBox.Items.Clear();
            int selectIndex = 0;
            for (int i = 0; i < models.Length; i++)
            {
                var item = new ComboBoxItem { Content = models[i] };
                ModelComboBox.Items.Add(item);
                if (models[i] == previouslySelected)
                    selectIndex = i;
            }
            if (ModelComboBox.Items.Count > 0)
                ModelComboBox.SelectedIndex = selectIndex;
        }
    }
}
