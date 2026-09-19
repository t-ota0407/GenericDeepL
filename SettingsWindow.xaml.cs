using System;
using System.Windows;
using System.Windows.Controls;

namespace GenericDeepL
{
    public partial class SettingsWindow : Window
    {
        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private readonly Settings _settings;

        public SettingsWindow(Settings settings)
        {
            InitializeComponent();
            _settings = settings;
            ApiKeyPasswordBox.Password = _settings.GoogleApiKey;

            // リージョン・モデル一覧の反映と SelectionChanged の購読は Loaded で行う
            Loaded += SettingsWindow_Loaded;
            
            // 送信制御の設定を反映
            ConfirmThresholdTextBox.Text = _settings.ConfirmThresholdLength.ToString();
            ConfirmThresholdHintText.Text =
                $"{Settings.MinConfirmThresholdLength:N0} 〜 {Settings.MaxConfirmThresholdLength:N0} の範囲で指定（既定 {Settings.DefaultConfirmThresholdLength:N0}）。超えた場合は送信するかどうかを都度確認します。";

            // スタートアップ設定を反映
            StartupCheckBox.IsChecked = _settings.RunAtStartup;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // 先に入力値を検証する。不正なら何も保存せず画面に留まる
            if (!int.TryParse(ConfirmThresholdTextBox.Text?.Trim(), out var threshold) ||
                threshold < Settings.MinConfirmThresholdLength ||
                threshold > Settings.MaxConfirmThresholdLength)
            {
                MessageBox.Show(
                    $"確認なしで送信する文字数は {Settings.MinConfirmThresholdLength:N0} 〜 {Settings.MaxConfirmThresholdLength:N0} の整数で入力してください。",
                    "入力エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                ConfirmThresholdTextBox.Focus();
                ConfirmThresholdTextBox.SelectAll();
                return;
            }

            // モデルは一覧から選ぶ方式のため、未選択のまま保存させない
            if (ModelComboBox.SelectedItem is not string selectedModel || string.IsNullOrWhiteSpace(selectedModel))
            {
                MessageBox.Show(
                    "モデルを選択してください。",
                    "入力エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                ModelComboBox.Focus();
                return;
            }

            _settings.GoogleApiKey = ApiKeyPasswordBox.Password;
            _settings.ConfirmThresholdLength = threshold;
            _settings.ModelName = selectedModel;

            
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
                        MessageBox.Show("スタートアップへの登録に失敗しました。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                        _settings.RunAtStartup = false;
                    }
                }
                else
                {
                    if (!StartupManager.Unregister())
                    {
                        MessageBox.Show("スタートアップの登録解除に失敗しました。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }

            _settings.Save();
            MessageBox.Show("設定を保存しました。", "設定", MessageBoxButton.OK, MessageBoxImage.Information);
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

            // models.json に定義されたモデルだけを選択肢にする（自由入力は不可）
            ModelComboBox.Items.Clear();
            foreach (var model in TranslationService.GetAvailableModels())
                ModelComboBox.Items.Add(model);

            // 保存済みのモデルが一覧に無い場合（古い設定を引き継いだ場合など）は既定を選ぶ
            if (!string.IsNullOrEmpty(_settings.ModelName) && ModelComboBox.Items.Contains(_settings.ModelName))
                ModelComboBox.SelectedItem = _settings.ModelName;
            else
                ModelComboBox.SelectedItem = TranslationService.GetDefaultModel();
        }
    }
}
